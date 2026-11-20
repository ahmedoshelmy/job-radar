using JobRadar.Contracts;
using JobRadar.Feeds;
using JobRadar.Feeds.Running;
using JobRadar.Feeds.Sources;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace JobRadar.UnitTests.Feeds;

public sealed class FeedRunnerTests : IDisposable
{
    private readonly string _stateDir = Path.Combine(Path.GetTempPath(), "jobradar-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_stateDir))
        {
            Directory.Delete(_stateDir, recursive: true);
        }
    }

    private static readonly BoardTarget Daily = new("lever", "x", "X", TimeSpan.FromHours(24));
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_feed_that_never_ran_is_due() => FeedRunner.IsDue(Daily, null, Now).ShouldBeTrue();

    [Fact]
    public void A_healthy_feed_waits_its_interval()
    {
        FeedRunner.IsDue(Daily, new SourceState { LastRunAt = Now.AddHours(-23) }, Now).ShouldBeFalse();
        FeedRunner.IsDue(Daily, new SourceState { LastRunAt = Now.AddHours(-24) }, Now).ShouldBeTrue();
    }

    [Theory]
    [InlineData(1, 15)]
    [InlineData(2, 30)]
    [InlineData(3, 60)]
    [InlineData(20, 24 * 60)]
    public void A_failing_feed_backs_off_exponentially_up_to_its_interval(int failures, int minutes)
    {
        var state = new SourceState { LastRunAt = Now.AddMinutes(-minutes), ConsecutiveFailures = failures };

        FeedRunner.IsDue(Daily, state, Now).ShouldBeTrue();
        FeedRunner.IsDue(Daily, state with { LastRunAt = Now.AddMinutes(-minutes + 1) }, Now).ShouldBeFalse();
    }

    [Fact]
    public async Task A_fixture_pass_publishes_every_posting_then_resumes_without_refetching()
    {
        var clock = new FakeTimeProvider(Now);
        await using var provider = BuildServices(clock);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var first = await RunAsync(provider);
        first.ShouldBe(new FeedRunReport(FeedsRun: 7, FeedsNotModified: 0, PostingsPublished: 20, Failures: 1));
        harness.Published.Select<JobPostingDiscovered>().Count().ShouldBe(20);

        clock.Advance(TimeSpan.FromMinutes(5));
        var second = await RunAsync(provider);
        second.ShouldBe(new FeedRunReport(0, 0, 0, 0), "healthy feeds are not due; the missing one is in backoff");

        clock.Advance(TimeSpan.FromMinutes(15));
        (await RunAsync(provider)).ShouldBe(new FeedRunReport(1, 0, 0, 1), "the failing feed is retried after 15 minutes");
    }

    private static async Task<FeedRunReport> RunAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FeedRunner>().RunDueAsync(CancellationToken.None);
    }

    private ServiceProvider BuildServices(TimeProvider clock)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Feeds:Mode"] = "Fixtures",
            ["Feeds:SchedulerEnabled"] = "false",
            ["Feeds:FixturesPath"] = Path.Combine(AppContext.BaseDirectory, "fixtures", "feeds"),
            ["Feeds:StatePath"] = Path.Combine(_stateDir, "feeds.json"),
        };
        string[][] sources =
        [
            ["greenhouse", "nilewave"], ["lever", "cairo-robotics"], ["ashby", "deltasoft"], ["workable", "pyramid-data"],
            ["remotive", "software-dev"], ["arbeitnow", ""], ["greenhouse", "no-such-board"],
        ];
        for (var i = 0; i < sources.Length; i++)
        {
            settings[$"Feeds:Sources:{i}:Kind"] = sources[i][0];
            settings[$"Feeds:Sources:{i}:Board"] = sources[i][1];
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();
        services.AddJobFeeds(configuration);
        services.AddSingleton(clock);
        services.AddMassTransitTestHarness();
        return services.BuildServiceProvider();
    }
}
