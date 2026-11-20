using System.Globalization;
using System.Text;
using JobRadar.Matching.Domain;
using Microsoft.Extensions.Caching.Memory;

namespace JobRadar.Matching.Application;

/// <summary>Everything a language model may see to explain one match: the rule score stays the source of truth.</summary>
public sealed record ExplanationRequest(PostingSnapshot Posting, Profile Profile, int Score, IReadOnlyList<MatchReason> Reasons);

/// <summary>
/// Optional "why this matches" in plain words from a free model (OpenRouter <c>:free</c> or a local Ollama).
/// Off by default; when off or failing, the rule reasons are the whole answer.
/// </summary>
public interface IMatchExplainer
{
    bool IsEnabled { get; }

    /// <summary>A short paragraph, or null when the provider did not answer usefully.</summary>
    Task<string?> ExplainAsync(ExplanationRequest request, CancellationToken cancellationToken);
}

public sealed record MatchExplanation(Guid PostingId, int Score, IReadOnlyList<MatchReason> Reasons, bool AiEnabled, string? Summary);

/// <summary>Rule reasons always; an AI summary on top when enabled, cached until the posting is rescored.</summary>
public sealed class ExplainMatch(IMatchingStore store, IMatchExplainer explainer, IMemoryCache cache)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(12);

    public async Task<MatchExplanation?> HandleAsync(Guid postingId, CancellationToken cancellationToken)
    {
        var posting = await store.FindAsync(postingId, cancellationToken);
        if (posting is null)
        {
            return null;
        }

        if (!explainer.IsEnabled)
        {
            return new MatchExplanation(postingId, posting.Score, posting.Reasons, AiEnabled: false, Summary: null);
        }

        var key = $"explain:{postingId}:{posting.ScoredAt.UtcTicks}";
        if (!cache.TryGetValue(key, out string? summary))
        {
            var profile = await store.GetProfileAsync(cancellationToken);
            summary = await explainer.ExplainAsync(new ExplanationRequest(posting.Snapshot, profile, posting.Score, posting.Reasons), cancellationToken);
            if (summary is not null)
            {
                cache.Set(key, summary, CacheFor);
            }
        }

        return new MatchExplanation(postingId, posting.Score, posting.Reasons, AiEnabled: true, summary);
    }
}

/// <summary>The prompt, built from facts only. Long descriptions are cut so a free model's context is never the limit.</summary>
public static class ExplanationPrompt
{
    public const int MaxDescriptionChars = 1500;

    public const string System =
        "You explain why a job posting does or does not suit a candidate, in two or three plain sentences. " +
        "Use only the facts given. Do not invent requirements, salaries or company details. No lists, no headings.";

    public static string Build(ExplanationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (posting, profile) = (request.Posting, request.Profile);
        var description = posting.Description.Length > MaxDescriptionChars
            ? posting.Description[..MaxDescriptionChars] + " …"
            : posting.Description;

        var text = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"Candidate: {profile.Headline}")
            .AppendLine(CultureInfo.InvariantCulture, $"Skills: {string.Join(", ", profile.Skills)}")
            .AppendLine(CultureInfo.InvariantCulture, $"Can work from: {string.Join(", ", profile.Locations)}{(profile.RemoteOnly ? " (remote only)" : "")}")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"Posting: {posting.Title} at {posting.Company}, {posting.Location} ({posting.WorkMode}, {posting.Seniority})")
            .AppendLine(CultureInfo.InvariantCulture, $"Description: {description}")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"Rule-based score: {request.Score}/100 because:");
        foreach (var reason in request.Reasons)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- {(reason.Points >= 0 ? "+" : "")}{reason.Points} {reason.Text}");
        }

        return text.ToString();
    }
}
