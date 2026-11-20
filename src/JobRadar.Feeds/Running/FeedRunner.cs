using System.Diagnostics.Metrics;
using JobRadar.Feeds.Content;
using JobRadar.Feeds.Sources;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobRadar.Feeds.Running;

public sealed record FeedRunReport(int FeedsRun, int FeedsNotModified, int PostingsPublished, int Failures);

/// <summary>
/// One pass over the configured feeds: fetch the ones that are due, parse, publish <c>JobPostingDiscovered</c>
/// and remember what happened. A failing feed never stops the others and is retried with backoff.
/// </summary>
public sealed class FeedRunner(
    IEnumerable<IJobBoardSource> sources,
    IFeedContentProvider content,
    IFeedStateStore stateStore,
    IPublishEndpoint publisher,
    IOptions<FeedOptions> options,
    TimeProvider clock,
    ILogger<FeedRunner> logger,
    IMeterFactory meters)
{
    public const string MeterName = "JobRadar.Feeds";
    private readonly Dictionary<string, IJobBoardSource> _byKind = sources.ToDictionary(s => s.Kind, StringComparer.OrdinalIgnoreCase);
    private readonly Counter<long> _discovered = meters.Create(MeterName).CreateCounter<long>("jobradar.feeds.postings_discovered");

    public async Task<FeedRunReport> RunDueAsync(CancellationToken cancellationToken)
    {
        var states = await stateStore.LoadAsync(cancellationToken);
        var report = new FeedRunReport(0, 0, 0, 0);
        foreach (var target in options.Value.Sources.Select(s => s.ToTarget()))
        {
            states.TryGetValue(target.Key, out var state);
            if (!IsDue(target, state, clock.GetUtcNow()))
            {
                continue;
            }

            report = Add(report, await RunOneAsync(target, state, cancellationToken));
        }

        return report;
    }

    /// <summary>Due when never run, when its interval has passed, or sooner (with backoff) after a failure.</summary>
    public static bool IsDue(BoardTarget target, SourceState? state, DateTimeOffset now)
    {
        if (state?.LastRunAt is not { } last)
        {
            return true;
        }

        var wait = state.ConsecutiveFailures == 0
            ? target.Interval
            : TimeSpan.FromMinutes(Math.Min(target.Interval.TotalMinutes, 15 * Math.Pow(2, state.ConsecutiveFailures - 1)));
        return now - last >= wait;
    }

    private async Task<FeedRunReport> RunOneAsync(BoardTarget target, SourceState? state, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (!_byKind.TryGetValue(target.Kind, out var source))
        {
            logger.LogWarning("Feed {Feed} has unknown kind {Kind}; skipped", target.Key, target.Kind);
            return new FeedRunReport(0, 0, 0, 1);
        }

        try
        {
            var fetched = await content.GetAsync(source, target, state, cancellationToken);
            if (fetched.IsNotModified)
            {
                await stateStore.SaveAsync(target.Key, (state ?? new SourceState()) with { LastRunAt = now, ConsecutiveFailures = 0, LastError = null }, cancellationToken);
                logger.LogInformation("Feed {Feed} not modified", target.Key);
                return new FeedRunReport(1, 1, 0, 0);
            }

            var postings = source.Parse(fetched.Body!, target, now);
            await publisher.PublishBatch(postings, cancellationToken);
            _discovered.Add(postings.Count, new KeyValuePair<string, object?>("feed", target.Kind));
            await stateStore.SaveAsync(target.Key, new SourceState { LastRunAt = now, ETag = fetched.ETag, LastModified = fetched.LastModified }, cancellationToken);
            logger.LogInformation("Feed {Feed} published {Count} postings", target.Key, postings.Count);
            return new FeedRunReport(1, 0, postings.Count, 0);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var failures = (state?.ConsecutiveFailures ?? 0) + 1;
            await stateStore.SaveAsync(target.Key, (state ?? new SourceState()) with { LastRunAt = now, ConsecutiveFailures = failures, LastError = ex.GetType().Name }, cancellationToken);
            logger.LogWarning(ex, "Feed {Feed} failed ({Failures} in a row)", target.Key, failures);
            return new FeedRunReport(1, 0, 0, 1);
        }
    }

    private static FeedRunReport Add(FeedRunReport a, FeedRunReport b) =>
        new(a.FeedsRun + b.FeedsRun, a.FeedsNotModified + b.FeedsNotModified, a.PostingsPublished + b.PostingsPublished, a.Failures + b.Failures);
}
