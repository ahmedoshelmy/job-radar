using JobRadar.Feeds.Running;
using JobRadar.Feeds.Sources;

namespace JobRadar.Feeds.Content;

/// <summary>What a fetch returned. <see cref="NotModified"/> means the conditional request said nothing changed.</summary>
public sealed record FeedContent(string? Body, string? ETag, DateTimeOffset? LastModified)
{
    public static readonly FeedContent NotModified = new(null, null, null);

    public bool IsNotModified => Body is null;
}

/// <summary>Where feed bodies come from: the live HTTP API, or recorded fixtures for CI, demos and offline work.</summary>
public interface IFeedContentProvider
{
    Task<FeedContent> GetAsync(IJobBoardSource source, BoardTarget target, SourceState? state, CancellationToken cancellationToken);
}
