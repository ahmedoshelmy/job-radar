using JobRadar.Feeds.Running;
using JobRadar.Feeds.Sources;
using Microsoft.Extensions.Options;

namespace JobRadar.Feeds.Content;

/// <summary>Reads <c>{FixturesPath}/{kind}-{board}.json</c> (or <c>{kind}.json</c>). No network at all.</summary>
public sealed class FixtureFeedContentProvider(IOptions<FeedOptions> options) : IFeedContentProvider
{
    public async Task<FeedContent> GetAsync(IJobBoardSource source, BoardTarget target, SourceState? state, CancellationToken cancellationToken)
    {
        var root = options.Value.FixturesPath;
        var path = new[] { Path.Combine(root, $"{target.Key}.json"), Path.Combine(root, $"{target.Kind}.json") }
            .FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException($"No fixture for feed '{target.Key}' under '{root}'.");

        var body = await File.ReadAllTextAsync(path, cancellationToken);
        return new FeedContent(body, null, File.GetLastWriteTimeUtc(path));
    }
}
