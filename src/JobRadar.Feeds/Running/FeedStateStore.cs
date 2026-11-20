using System.Text.Json;
using Microsoft.Extensions.Options;

namespace JobRadar.Feeds.Running;

/// <summary>Per-feed memory between runs: when it last ran and the validators for the next conditional request.</summary>
public sealed record SourceState
{
    public DateTimeOffset? LastRunAt { get; init; }
    public string? ETag { get; init; }
    public DateTimeOffset? LastModified { get; init; }
    public int ConsecutiveFailures { get; init; }
    public string? LastError { get; init; }
}

public interface IFeedStateStore
{
    Task<IReadOnlyDictionary<string, SourceState>> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(string key, SourceState state, CancellationToken cancellationToken);
}

/// <summary>
/// A small JSON file, written atomically (temp file + rename), so a restarted worker resumes where it stopped
/// instead of hitting every feed again.
/// </summary>
public sealed class JsonFileFeedStateStore(IOptions<FeedOptions> options) : IFeedStateStore, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyDictionary<string, SourceState>> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(string key, SourceState state, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var all = await ReadAsync(cancellationToken);
            all[key] = state;
            var path = options.Value.StatePath;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(all, Json), cancellationToken);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, SourceState>> ReadAsync(CancellationToken cancellationToken)
    {
        var path = options.Value.StatePath;
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<Dictionary<string, SourceState>>(stream, Json, cancellationToken) ?? [];
    }

    public void Dispose() => _gate.Dispose();
}
