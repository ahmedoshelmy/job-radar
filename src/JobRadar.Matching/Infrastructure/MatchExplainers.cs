using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using JobRadar.Matching.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobRadar.Matching.Infrastructure;

public enum AiProvider
{
    None,
    OpenRouter,
    Ollama,
}

/// <summary>
/// <c>Ai:*</c> settings. Free providers only: OpenRouter accepts <c>:free</c> model ids only, Ollama runs locally.
/// Provider <c>None</c> (the default) turns the feature off.
/// </summary>
public sealed class AiOptions
{
    public const string Section = "Ai";

    public AiProvider Provider { get; set; } = AiProvider.None;

    /// <summary>OpenAI-compatible base URL ending in <c>/</c>; empty means the provider's default.</summary>
    public string? BaseUrl { get; set; }

    public string Model { get; set; } = "";

    /// <summary>OpenRouter only. Comes from the environment (<c>Ai__ApiKey</c>), never from a committed file.</summary>
    public string? ApiKey { get; set; }

    public int TimeoutSeconds { get; set; } = 20;

    public Uri ResolvedBaseUrl => new(!string.IsNullOrWhiteSpace(BaseUrl)
        ? BaseUrl
        : Provider == AiProvider.Ollama ? "http://localhost:11434/v1/" : "https://openrouter.ai/api/v1/");

    public static bool IsValid(AiOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);
        if (!string.IsNullOrWhiteSpace(o.BaseUrl) && !Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _))
        {
            return false;
        }

        return o.Provider switch
        {
            AiProvider.None => true,
            AiProvider.OpenRouter => !string.IsNullOrWhiteSpace(o.ApiKey) && o.Model.EndsWith(":free", StringComparison.Ordinal),
            _ => !string.IsNullOrWhiteSpace(o.Model),
        };
    }
}

internal sealed class DisabledMatchExplainer : IMatchExplainer
{
    public bool IsEnabled => false;

    public Task<string?> ExplainAsync(ExplanationRequest request, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
}

/// <summary>
/// Calls <c>POST {base}/chat/completions</c>, the shape both OpenRouter and Ollama speak. Any failure is logged and
/// answered with null: the explanation is a nice-to-have on top of the rule reasons, never a reason for a 500.
/// </summary>
internal sealed class ChatCompletionsMatchExplainer(HttpClient http, IOptions<AiOptions> options, ILogger<ChatCompletionsMatchExplainer> logger)
    : IMatchExplainer
{
    private const int MaxSummaryChars = 800;

    public bool IsEnabled => true;

    public async Task<string?> ExplainAsync(ExplanationRequest request, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.ResolvedBaseUrl, "chat/completions"))
        {
            Content = JsonContent.Create(new ChatRequest(
                settings.Model,
                [new ChatMessage("system", ExplanationPrompt.System), new ChatMessage("user", ExplanationPrompt.Build(request))],
                MaxTokens: 220,
                Temperature: 0.2)),
        };
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        }

        try
        {
            using var response = await http.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Match explanation from {Provider} failed with {Status}", settings.Provider, (int)response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken);
            var text = body?.Choices is [var first, ..] ? first.Message?.Content?.Trim() : null;
            return string.IsNullOrEmpty(text) ? null : text.Length > MaxSummaryChars ? text[..MaxSummaryChars] + " …" : text;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Match explanation from {Provider} failed", settings.Provider);
            return null;
        }
    }

    private sealed record ChatMessage(string Role, string Content);

    private sealed record ChatRequest(
        string Model,
        IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        double Temperature);

    private sealed record ChatResponse(IReadOnlyList<ChatChoice>? Choices);

    private sealed record ChatChoice(ChatMessage? Message);
}
