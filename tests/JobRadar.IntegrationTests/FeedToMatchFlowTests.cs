using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobRadar.Feeds;
using JobRadar.Feeds.Running;
using MassTransit;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobRadar.IntegrationTests;

/// <summary>
/// The main flow end to end: a fixture feed run (six boards) → RabbitMQ → Catalog dedupe → Matching → SignalR,
/// then the Tracker over HTTP. No public network: the feeds are the invented fixtures.
/// </summary>
public sealed class FeedToMatchFlowTests(JobRadarFixture fixture) : IClassFixture<JobRadarFixture>
{
    private const int UniqueOpenings = 17; // 20 parsed postings, three of them mirrored on a second feed
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private sealed record Posting(Guid Id, string Title, string Company, string Source, string[] AlsoSeenIn, int? Score);

    private sealed record Match(Guid PostingId, int Score, string Title, string[] Reasons);

    private sealed record Application(Guid Id, string Stage, string[] AllowedNext, Stage[] History);

    private sealed record Stage(string? From, string To);

    [Fact]
    public async Task Fixture_feeds_are_deduplicated_scored_pushed_live_and_tracked()
    {
        var http = fixture.Api.CreateClient();
        var matches = new ConcurrentBag<Match>();
        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(fixture.Api.Server.BaseAddress, "hubs/matches"), o =>
            {
                o.HttpMessageHandlerFactory = _ => fixture.Api.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions = Json)
            .Build();
        hub.On<Match>("match", matches.Add);
        await hub.StartAsync();

        var report = await RunFixtureFeedsAsync();
        report.PostingsPublished.ShouldBe(20);
        report.Failures.ShouldBe(0);

        var postings = await EventuallyAsync(
            () => http.GetFromJsonAsync<Posting[]>("/api/postings", Json),
            p => p!.Length == UniqueOpenings && p.All(x => x.Score is not null));
        // Consumers may see feeds in any order, so check the credited feeds as a set rather than who came first.
        Feeds(postings!, p => p.Company == "Nilewave Labs" && p.Title.Contains(".NET", StringComparison.Ordinal)).ShouldBe(["greenhouse", "remotive"]);
        Feeds(postings!, p => p.Company.StartsWith("Deltasoft", StringComparison.Ordinal) && p.Title.StartsWith("Platform", StringComparison.Ordinal)).ShouldBe(["arbeitnow", "ashby"]);
        Feeds(postings!, p => p.Company == "Cairo Robotics" && p.Title.Contains("C++", StringComparison.Ordinal)).ShouldBe(["lever", "remotive"], "same canonical URL, different title");

        var live = await EventuallyAsync(() => Task.FromResult(matches.ToArray()), m => m.Any(x => x.Title.Contains("Backend Engineer (.NET)", StringComparison.Ordinal)));
        var best = live.Single(x => x.Title.Contains("Backend Engineer (.NET)", StringComparison.Ordinal));
        best.Score.ShouldBe(82);
        best.Reasons.ShouldContain("+15 Remote");
        live.ShouldNotContain(x => x.Title == "Frontend Engineer, React");

        var filtered = await http.GetFromJsonAsync<Posting[]>("/api/postings?minScore=60&remote=true&query=.net", Json);
        filtered!.ShouldNotBeEmpty();
        filtered!.ShouldAllBe(p => p.Score >= 60);

        await TrackAsync(http, postings!.Single(p => p.Company == "Nilewave Labs" && p.Title.Contains(".NET", StringComparison.Ordinal)).Id);

        var stats = await EventuallyAsync(() => http.GetFromJsonAsync<JsonElement>("/api/stats", Json), s => s.GetProperty("totalPostings").GetInt32() == UniqueOpenings);
        stats.GetProperty("bySource").GetArrayLength().ShouldBe(6);
    }

    private static string[] Feeds(Posting[] postings, Func<Posting, bool> which)
    {
        var posting = postings.Single(which);
        return [.. posting.AlsoSeenIn.Append(posting.Source).Order(StringComparer.Ordinal)];
    }

    private static async Task TrackAsync(HttpClient http, Guid postingId)
    {
        var created = await http.PostAsJsonAsync("/api/applications", new { postingId, note = "great fit" }, Json);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var application = (await created.Content.ReadFromJsonAsync<Application>(Json))!;
        application.Stage.ShouldBe("Saved");

        var duplicate = await http.PostAsJsonAsync("/api/applications", new { postingId }, Json);
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var applied = await http.PostAsJsonAsync($"/api/applications/{application.Id}/stage", new { stage = "Applied" }, Json);
        applied.StatusCode.ShouldBe(HttpStatusCode.OK);
        var moved = (await applied.Content.ReadFromJsonAsync<Application>(Json))!;
        moved.History.Select(h => h.To).ShouldBe(["Saved", "Applied"]);
        moved.AllowedNext.ShouldBe(["Interview", "Rejected", "Withdrawn"]);

        var illegal = await http.PostAsJsonAsync($"/api/applications/{application.Id}/stage", new { stage = "Offer" }, Json);
        illegal.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await illegal.Content.ReadAsStringAsync()).ShouldContain("cannot move from Applied to Offer");
    }

    /// <summary>The worker's real code path (FeedRunner + fixtures) publishing to the test RabbitMQ.</summary>
    private async Task<FeedRunReport> RunFixtureFeedsAsync()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Feeds:Mode"] = "Fixtures",
            ["Feeds:SchedulerEnabled"] = "false",
            ["Feeds:FixturesPath"] = Path.Combine(AppContext.BaseDirectory, "fixtures", "feeds"),
            ["Feeds:StatePath"] = Path.Combine(Path.GetTempPath(), "jobradar-it", Guid.NewGuid().ToString("N"), "feeds.json"),
        };
        string[][] sources =
        [
            ["greenhouse", "nilewave", "Nilewave Labs"], ["lever", "cairo-robotics", "Cairo Robotics"], ["ashby", "deltasoft", "Deltasoft"],
            ["workable", "pyramid-data", "Pyramid Data"], ["remotive", "software-dev", "Remotive"], ["arbeitnow", "", "Arbeitnow"],
        ];
        for (var i = 0; i < sources.Length; i++)
        {
            settings[$"Feeds:Sources:{i}:Kind"] = sources[i][0];
            settings[$"Feeds:Sources:{i}:Board"] = sources[i][1];
            settings[$"Feeds:Sources:{i}:Label"] = sources[i][2];
        }

        var services = new ServiceCollection().AddLogging().AddMetrics();
        services.AddJobFeeds(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddMassTransit(x => x.UsingRabbitMq((_, cfg) => cfg.Host(new Uri(fixture.RabbitConnection))));
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        try
        {
            await using var scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<FeedRunner>().RunDueAsync(CancellationToken.None);
        }
        finally
        {
            await bus.StopAsync();
        }
    }

    /// <summary>Bounded polling: async consumers settle within seconds; fail after 60 s instead of hanging.</summary>
    private static async Task<T> EventuallyAsync<T>(Func<Task<T>> probe, Func<T, bool> done)
    {
        var last = default(T);
        for (var attempt = 0; attempt < 120; attempt++)
        {
            last = await probe();
            if (last is not null && done(last))
            {
                return last;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"Condition not met in 60 s; last value: {JsonSerializer.Serialize(last, Json)}");
    }
}
