using System.Text.Json;
using System.Text.RegularExpressions;
using JobRadar.Contracts;
using JobRadar.Feeds.Text;

namespace JobRadar.Feeds.Sources;

/// <summary>
/// Remotive remote-jobs API: <c>remotive.com/api/remote-jobs</c>. Terms: no key, a few calls a day at most,
/// credit Remotive and link back to the posting, never republish to other job sites.
/// </summary>
public sealed class RemotiveSource : IJobBoardSource
{
    public string Kind => "remotive";
    public bool NeedsBoard => false;

    /// <summary>The board token, when set, is a Remotive category slug (for example <c>software-dev</c>).</summary>
    public Uri BuildUri(BoardTarget target) => string.IsNullOrWhiteSpace(target.Board)
        ? new Uri("https://remotive.com/api/remote-jobs")
        : new Uri($"https://remotive.com/api/remote-jobs?category={Uri.EscapeDataString(target.Board)}");

    public IReadOnlyList<JobPostingDiscovered> Parse(string body, BoardTarget target, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(body);
        var result = new List<JobPostingDiscovered>();
        foreach (var j in doc.RootElement.Items("jobs"))
        {
            var url = FeedText.SafeUrl(j.Str("url"));
            var title = FeedText.Clean(j.Str("title"));
            if (url is null || title.Length == 0)
            {
                continue;
            }

            var where = FeedText.Clean(j.Str("candidate_required_location"), 400);
            result.Add(new JobPostingDiscovered
            {
                Source = Kind,
                ExternalId = j.Str("id"),
                Url = url,
                Title = title,
                Company = FeedText.Clean(j.Str("company_name"), 120),
                Location = where.Length > 0 ? where : "Remote",
                WorkMode = WorkMode.Remote,
                Countries = FeedText.SplitPlaces(where),
                Seniority = Classify.SeniorityOf(title),
                SalaryText = FeedText.Clean(j.Str("salary"), 80) is { Length: > 0 } s ? s : null,
                PostedAt = FeedText.ParseDate(j.Str("publication_date")),
                Description = FeedText.HtmlToText(j.Str("description")),
                Tags = j.Strings("tags").Concat(JsonRead.NonEmpty(j.Str("category"))).ToArray(),
                DiscoveredAt = now,
            });
        }

        return result;
    }
}

/// <summary>
/// Arbeitnow job board API: <c>www.arbeitnow.com/api/job-board-api</c>. Public, no key; link back to each posting.
/// </summary>
public sealed partial class ArbeitnowSource : IJobBoardSource
{
    public string Kind => "arbeitnow";
    public bool NeedsBoard => false;

    public Uri BuildUri(BoardTarget target) => new("https://www.arbeitnow.com/api/job-board-api");

    public IReadOnlyList<JobPostingDiscovered> Parse(string body, BoardTarget target, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(body);
        var result = new List<JobPostingDiscovered>();
        foreach (var j in doc.RootElement.Items("data"))
        {
            var url = FeedText.SafeUrl(j.Str("url"));
            var title = FeedText.Clean(j.Str("title"));
            if (url is null || title.Length == 0)
            {
                continue;
            }

            var where = FeedText.Clean(j.Str("location"), 200);
            var remote = j.Bool("remote") == true;
            // "Remote", "Homeoffice" say nothing about where; a city on a remote job means remote within that country.
            var vague = where.Length == 0 || VaguePlace().IsMatch(where);
            result.Add(new JobPostingDiscovered
            {
                Source = Kind,
                ExternalId = FeedText.Clean(j.Str("slug"), 300) is { Length: > 0 } slug ? slug : url,
                Url = url,
                Title = title,
                Company = FeedText.Clean(j.Str("company_name"), 120),
                Location = where.Length > 0 ? where : remote ? "Remote" : "",
                WorkMode = remote ? WorkMode.Remote : WorkMode.Onsite,
                Countries = vague ? [] : [where],
                Seniority = Classify.SeniorityOf(title),
                PostedAt = FeedText.ParseDate(j.Str("created_at")),
                Description = FeedText.HtmlToText(j.Str("description")),
                Tags = j.Strings("tags").Concat(j.Strings("job_types")).ToArray(),
                DiscoveredAt = now,
            });
        }

        return result;
    }

    [GeneratedRegex("^(remote( job)?|homeoffice|home office|anywhere)$", RegexOptions.IgnoreCase)]
    private static partial Regex VaguePlace();
}
