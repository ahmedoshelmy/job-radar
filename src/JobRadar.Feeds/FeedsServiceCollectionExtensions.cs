using Microsoft.Extensions.Configuration;
using System.Threading.RateLimiting;
using JobRadar.Feeds.Content;
using JobRadar.Feeds.Running;
using JobRadar.Feeds.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace JobRadar.Feeds;

public static class FeedsServiceCollectionExtensions
{
    public static IServiceCollection AddJobFeeds(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(FeedOptions.Section);
        services.AddOptions<FeedOptions>()
            .Bind(section)
            .Validate(o => o.Sources.Count > 0, "Feeds:Sources needs at least one feed.")
            .Validate(o => o.Sources.All(s => s.IntervalHours >= 1), "Feeds:Sources:*:IntervalHours must be at least 1 (be polite).")
            .ValidateOnStart();

        services.AddSingleton<IJobBoardSource, GreenhouseSource>();
        services.AddSingleton<IJobBoardSource, LeverSource>();
        services.AddSingleton<IJobBoardSource, AshbySource>();
        services.AddSingleton<IJobBoardSource, WorkableSource>();
        services.AddSingleton<IJobBoardSource, RemotiveSource>();
        services.AddSingleton<IJobBoardSource, ArbeitnowSource>();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IFeedStateStore, JsonFileFeedStateStore>();
        services.AddScoped<FeedRunner>();

        if (section.GetValue(nameof(FeedOptions.Mode), FeedMode.Fixtures) == FeedMode.Live)
        {
            services.AddLiveHttp(section.GetValue(nameof(FeedOptions.UserAgent), new FeedOptions().UserAgent)!);
        }
        else
        {
            services.AddSingleton<IFeedContentProvider, FixtureFeedContentProvider>();
        }

        if (section.GetValue(nameof(FeedOptions.SchedulerEnabled), true))
        {
            services.AddHostedService<FeedScheduler>();
        }

        return services;
    }

    /// <summary>
    /// Polite HTTP: one pipeline per host (SelectPipelineByAuthority), so each feed owner gets its own rate limit and its
    /// own circuit breaker; one slow board never throttles another.
    /// </summary>
    private static void AddLiveHttp(this IServiceCollection services, string userAgent)
    {
        services.AddHttpClient<IFeedContentProvider, HttpFeedContentProvider>(c =>
            {
                c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
                c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                c.MaxResponseContentBufferSize = 20 * 1024 * 1024;
                c.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddResilienceHandler("feeds", static builder => builder
                .AddTimeout(TimeSpan.FromSeconds(90))
                .AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromSeconds(2),
                })
                .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    MinimumThroughput = 4,
                    SamplingDuration = TimeSpan.FromMinutes(2),
                    BreakDuration = TimeSpan.FromMinutes(10),
                })
                .AddRateLimiter(new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 1,
                    TokensPerPeriod = 1,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(2),
                    QueueLimit = 50,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true,
                }))
                .AddTimeout(TimeSpan.FromSeconds(30)))
            .SelectPipelineByAuthority();
    }
}
