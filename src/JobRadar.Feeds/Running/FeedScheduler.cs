using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobRadar.Feeds.Running;

/// <summary>Wakes every <see cref="FeedOptions.TickSeconds"/> and runs whatever feeds are due. One pass at a time.</summary>
public sealed class FeedScheduler(IServiceScopeFactory scopes, IOptions<FeedOptions> options, TimeProvider clock, ILogger<FeedScheduler> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.Value.TickSeconds)), clock);
        do
        {
            await using var scope = scopes.CreateAsyncScope();
            var report = await scope.ServiceProvider.GetRequiredService<FeedRunner>().RunDueAsync(stoppingToken);
            if (report.FeedsRun > 0)
            {
                logger.LogInformation(
                    "Feed pass: {Run} run, {NotModified} not modified, {Published} postings, {Failures} failures",
                    report.FeedsRun, report.FeedsNotModified, report.PostingsPublished, report.Failures);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
