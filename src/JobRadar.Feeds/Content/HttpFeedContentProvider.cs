using System.Net;
using System.Net.Http.Headers;
using JobRadar.Feeds.Running;
using JobRadar.Feeds.Sources;

namespace JobRadar.Feeds.Content;

/// <summary>
/// Live fetch with conditional requests: the last ETag / Last-Modified go back to the server, and a 304 costs the
/// feed almost nothing. Retries, circuit breaking and the per-host rate limit sit in the HttpClient's resilience pipeline.
/// </summary>
public sealed class HttpFeedContentProvider(HttpClient http) : IFeedContentProvider
{
    public async Task<FeedContent> GetAsync(IJobBoardSource source, BoardTarget target, SourceState? state, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, source.BuildUri(target));
        if (state?.ETag is { } etag && EntityTagHeaderValue.TryParse(etag, out var tag))
        {
            request.Headers.IfNoneMatch.Add(tag);
        }

        if (state?.LastModified is { } lastModified)
        {
            request.Headers.IfModifiedSince = lastModified;
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return FeedContent.NotModified;
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new FeedContent(body, response.Headers.ETag?.ToString(), response.Content.Headers.LastModified);
    }
}
