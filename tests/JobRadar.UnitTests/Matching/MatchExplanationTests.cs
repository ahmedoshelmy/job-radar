using System.Net;
using System.Text;
using System.Text.Json;
using JobRadar.Contracts;
using JobRadar.Matching.Application;
using JobRadar.Matching.Domain;
using JobRadar.Matching.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobRadar.UnitTests.Matching;

public sealed class MatchExplanationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private static ScoredPosting Scored(string description = "C# and PostgreSQL on Azure")
    {
        var posting = new ScoredPosting
        {
            PostingId = Guid.NewGuid(),
            Url = "https://example.com/jobs/1",
            AddedAt = Now,
            Snapshot = new PostingSnapshot(
                "Senior .NET Engineer", "Nilewave Labs", "Remote, EMEA", WorkMode.Remote, ["Egypt"], Seniority.Senior, null, null, description, []),
        };
        posting.Apply(MatchScorer.Score(posting.Snapshot, Profile.Demo(Now)), Now);
        return posting;
    }

    [Fact]
    public async Task Disabled_answers_with_the_rule_reasons_only()
    {
        var posting = Scored();
        var explainer = new CountingExplainer(enabled: false);
        var result = await Handler(posting, explainer).HandleAsync(posting.PostingId, CancellationToken.None);

        result.ShouldNotBeNull();
        result.AiEnabled.ShouldBeFalse();
        result.Summary.ShouldBeNull();
        result.Reasons.ShouldBe(posting.Reasons);
        explainer.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Enabled_adds_a_summary_and_caches_it_until_the_posting_is_rescored()
    {
        var posting = Scored();
        var explainer = new CountingExplainer(enabled: true);
        var handler = Handler(posting, explainer);

        (await handler.HandleAsync(posting.PostingId, CancellationToken.None))!.Summary.ShouldBe("summary 1");
        (await handler.HandleAsync(posting.PostingId, CancellationToken.None))!.Summary.ShouldBe("summary 1");
        explainer.Calls.ShouldBe(1);

        posting.Apply(new MatchResult(10, []), Now.AddMinutes(1));
        (await handler.HandleAsync(posting.PostingId, CancellationToken.None))!.Summary.ShouldBe("summary 2");
    }

    [Fact]
    public async Task Unknown_posting_is_null()
    {
        var result = await Handler(Scored(), new CountingExplainer(enabled: true)).HandleAsync(Guid.NewGuid(), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public void The_prompt_carries_the_facts_and_cuts_long_descriptions()
    {
        var posting = Scored(description: new string('x', ExplanationPrompt.MaxDescriptionChars + 500));
        var prompt = ExplanationPrompt.Build(new ExplanationRequest(posting.Snapshot, Profile.Demo(Now), posting.Score, posting.Reasons));

        prompt.ShouldContain("Senior .NET Engineer at Nilewave Labs");
        prompt.ShouldContain($"Rule-based score: {posting.Score}/100");
        prompt.ShouldContain(posting.Reasons[0].Text);
        prompt.ShouldNotContain(new string('x', ExplanationPrompt.MaxDescriptionChars + 1));
    }

    [Fact]
    public async Task Chat_completions_client_sends_model_and_key_and_reads_the_first_choice()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"choices":[{"message":{"role":"assistant","content":"  A good fit.  "}}]}""");
        var explainer = Explainer(handler, new AiOptions { Provider = AiProvider.OpenRouter, Model = "some/model:free", ApiKey = "test-key" });

        var summary = await explainer.ExplainAsync(Request(), CancellationToken.None);

        summary.ShouldBe("A good fit.");
        handler.Request!.RequestUri.ShouldBe(new Uri("https://openrouter.ai/api/v1/chat/completions"));
        handler.Request.Headers.Authorization!.Parameter.ShouldBe("test-key");
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("model").GetString().ShouldBe("some/model:free");
        body.RootElement.GetProperty("max_tokens").GetInt32().ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "{}")]
    [InlineData(HttpStatusCode.OK, "not json")]
    [InlineData(HttpStatusCode.OK, """{"choices":[]}""")]
    public async Task Chat_completions_failures_degrade_to_no_summary(HttpStatusCode status, string body)
    {
        var explainer = Explainer(new StubHandler(status, body), new AiOptions { Provider = AiProvider.Ollama, Model = "llama3.2" });

        (await explainer.ExplainAsync(Request(), CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [InlineData(AiProvider.None, "", null, null, true)]
    [InlineData(AiProvider.OpenRouter, "some/model:free", "key", null, true)]
    [InlineData(AiProvider.OpenRouter, "some/paid-model", "key", null, false)]
    [InlineData(AiProvider.OpenRouter, "some/model:free", null, null, false)]
    [InlineData(AiProvider.Ollama, "llama3.2", null, "http://host.docker.internal:11434/v1/", true)]
    [InlineData(AiProvider.Ollama, "", null, null, false)]
    [InlineData(AiProvider.Ollama, "llama3.2", null, "not a url", false)]
    public void Options_allow_only_free_providers_fully_configured(AiProvider provider, string model, string? key, string? baseUrl, bool valid)
    {
        AiOptions.IsValid(new AiOptions { Provider = provider, Model = model, ApiKey = key, BaseUrl = baseUrl }).ShouldBe(valid);
    }

    private static ExplanationRequest Request()
    {
        var posting = Scored();
        return new ExplanationRequest(posting.Snapshot, Profile.Demo(Now), posting.Score, posting.Reasons);
    }

    private static ExplainMatch Handler(ScoredPosting posting, IMatchExplainer explainer) =>
        new(new OnePostingStore(posting), explainer, new MemoryCache(new MemoryCacheOptions()));

    private static ChatCompletionsMatchExplainer Explainer(StubHandler handler, AiOptions options) =>
        new(new HttpClient(handler), Options.Create(options), NullLogger<ChatCompletionsMatchExplainer>.Instance);

    private sealed class CountingExplainer(bool enabled) : IMatchExplainer
    {
        public int Calls { get; private set; }

        public bool IsEnabled => enabled;

        public Task<string?> ExplainAsync(ExplanationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<string?>($"summary {++Calls}");
    }

    private sealed class OnePostingStore(ScoredPosting posting) : IMatchingStore
    {
        public Task<Profile> GetProfileAsync(CancellationToken cancellationToken) => Task.FromResult(Profile.Demo(Now));

        public Task<ScoredPosting?> FindAsync(Guid postingId, CancellationToken cancellationToken) =>
            Task.FromResult(postingId == posting.PostingId ? posting : null);

        public Task<IReadOnlyList<ScoredPosting>> AllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ScoredPosting>>([posting]);

        public void Add(ScoredPosting posting) => throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
