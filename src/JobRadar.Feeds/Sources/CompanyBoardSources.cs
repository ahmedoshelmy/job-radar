using System.Text.Json;
using JobRadar.Contracts;
using JobRadar.Feeds.Text;

namespace JobRadar.Feeds.Sources;

/// <summary>
/// Greenhouse job board API: <c>boards-api.greenhouse.io/v1/boards/{board}/jobs?content=true</c>.
/// Public, no key; the content field is entity-escaped HTML.
/// </summary>
public sealed class GreenhouseSource : IJobBoardSource
{
    public string Kind => "greenhouse";
    public bool NeedsBoard => true;

    public Uri BuildUri(BoardTarget target) =>
        new($"https://boards-api.greenhouse.io/v1/boards/{Uri.EscapeDataString(target.Board!)}/jobs?content=true");

    public IReadOnlyList<JobPostingDiscovered> Parse(string body, BoardTarget target, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(body);
        var result = new List<JobPostingDiscovered>();
        foreach (var j in doc.RootElement.Items("jobs"))
        {
            var url = FeedText.SafeUrl(j.Str("absolute_url"));
            var title = FeedText.Clean(j.Str("title"));
            if (url is null || title.Length == 0)
            {
                continue;
            }

            var where = FeedText.Clean(j.Prop("location").Str("name"));
            var (mode, countries) = SplitGreenhouseLocation(where);
            result.Add(new JobPostingDiscovered
            {
                Source = Kind,
                ExternalId = $"{target.Board}:{j.Str("id")}",
                Url = url,
                Title = title,
                Company = FeedText.Clean(j.Str("company_name"), 120) is { Length: > 0 } c ? c : target.Label,
                Location = where,
                WorkMode = mode,
                Countries = countries,
                Seniority = Classify.SeniorityOf(title),
                PostedAt = FeedText.ParseDate(j.Str("first_published")) ?? FeedText.ParseDate(j.Str("updated_at")),
                Description = FeedText.HtmlToText(j.Str("content")),
                Tags = j.Items("departments").Select(d => FeedText.Clean(d.Str("name"), 60)).Where(d => d.Length > 0).ToArray(),
                DiscoveredAt = now,
            });
        }

        return result;
    }

    /// <summary>"Remote, Germany; Remote, Egypt" is remote in two countries; "Cairo, Egypt" is one place.</summary>
    internal static (WorkMode Mode, IReadOnlyList<string> Countries) SplitGreenhouseLocation(string where)
    {
        var parts = where.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var allRemote = parts.Length > 0 && parts.All(p => p.Contains("remote", StringComparison.OrdinalIgnoreCase));
        var mode = allRemote ? WorkMode.Remote : Classify.WorkModeOf(where);
        if (mode != WorkMode.Remote)
        {
            return (mode, parts);
        }

        var countries = parts
            .Select(p => p.Replace("Remote", "", StringComparison.OrdinalIgnoreCase).Trim(' ', ',', '-', '–'))
            .Where(p => p.Length > 0)
            .ToArray();
        return (mode, countries);
    }
}

/// <summary>Lever postings API: <c>api.lever.co/v0/postings/{board}?mode=json</c>. Public, no key.</summary>
public sealed class LeverSource : IJobBoardSource
{
    public string Kind => "lever";
    public bool NeedsBoard => true;

    public Uri BuildUri(BoardTarget target) =>
        new($"https://api.lever.co/v0/postings/{Uri.EscapeDataString(target.Board!)}?mode=json");

    public IReadOnlyList<JobPostingDiscovered> Parse(string body, BoardTarget target, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(body);
        var result = new List<JobPostingDiscovered>();
        foreach (var j in doc.RootElement.Items())
        {
            var url = FeedText.SafeUrl(j.Str("hostedUrl"));
            var title = FeedText.Clean(j.Str("text"));
            if (url is null || title.Length == 0)
            {
                continue;
            }

            var cats = j.Prop("categories");
            var all = cats.Strings("allLocations", 20);
            var where = FeedText.Clean(cats.Str("location"), 200) is { Length: > 0 } loc ? loc : string.Join(", ", all);
            var salary = j.Prop("salaryRange");
            var currency = salary.Str("currency");
            var period = salary.Str("interval");
            result.Add(new JobPostingDiscovered
            {
                Source = Kind,
                ExternalId = $"{target.Board}:{j.Str("id")}",
                Url = url,
                Title = title,
                Company = target.Label,
                Location = where,
                WorkMode = Classify.WorkModeField(j.Str("workplaceType")) ?? Classify.WorkModeOf(where),
                Countries = all.Count > 0 ? all : where.Length > 0 ? [where] : [],
                Seniority = Classify.SeniorityOf(title),
                SalaryText = SalaryText(salary.Num("min"), salary.Num("max"), currency, period),
                SalaryMinUsd = FeedText.UsdPerYear(salary.Num("min"), currency, period),
                SalaryMaxUsd = FeedText.UsdPerYear(salary.Num("max"), currency, period),
                PostedAt = FeedText.ParseDate(j.Str("createdAt")),
                Description = FeedText.HtmlToText(j.Str("descriptionPlain") is { Length: > 0 } plain ? plain : j.Str("description")),
                Tags = JsonRead.NonEmpty(cats.Str("team"), cats.Str("department"), cats.Str("commitment")),
                DiscoveredAt = now,
            });
        }

        return result;
    }

    internal static string? SalaryText(decimal? min, decimal? max, string currency, string period)
    {
        if (min is null && max is null)
        {
            return null;
        }

        var range = string.Join("–", new[] { min, max }.Where(v => v is not null).Select(v => v!.Value.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture)));
        return $"{currency} {range}{(period.Length > 0 ? $" / {period}" : "")}".Trim();
    }
}

/// <summary>Ashby posting API: <c>api.ashbyhq.com/posting-api/job-board/{board}?includeCompensation=true</c>.</summary>
public sealed class AshbySource : IJobBoardSource
{
    public string Kind => "ashby";
    public bool NeedsBoard => true;

    public Uri BuildUri(BoardTarget target) =>
        new($"https://api.ashbyhq.com/posting-api/job-board/{Uri.EscapeDataString(target.Board!)}?includeCompensation=true");

    public IReadOnlyList<JobPostingDiscovered> Parse(string body, BoardTarget target, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(body);
        var result = new List<JobPostingDiscovered>();
        foreach (var j in doc.RootElement.Items("jobs").Where(j => j.Bool("isListed") != false))
        {
            var url = FeedText.SafeUrl(j.Str("jobUrl"));
            var title = FeedText.Clean(j.Str("title"));
            if (url is null || title.Length == 0)
            {
                continue;
            }

            var address = j.Prop("address").Prop("postalAddress");
            var city = FeedText.Clean(address.Str("addressLocality"), 80);
            var country = FeedText.Clean(address.Str("addressCountry"), 80);
            var office = FeedText.Clean(j.Str("location"), 120);
            var where = string.Join(", ", new[] { city, country }.Where(x => x.Length > 0)) is { Length: > 0 } w ? w : office;
            var mode = Classify.WorkModeField(j.Str("workplaceType"))
                ?? (j.Bool("isRemote") == true ? WorkMode.Remote : Classify.WorkModeOf(office));
            var countries = new[] { country.Length > 0 ? country : office }
                .Concat(j.Items("secondaryLocations").Select(s => FeedText.Clean(s.Str("location"), 120)))
                .Where(x => x.Length > 0)
                .ToArray();
            var comp = j.Prop("compensation");
            result.Add(new JobPostingDiscovered
            {
                Source = Kind,
                ExternalId = $"{target.Board}:{j.Str("id")}",
                Url = url,
                Title = title,
                Company = target.Label,
                Location = where,
                WorkMode = mode,
                Countries = countries,
                Seniority = Classify.SeniorityOf(title),
                SalaryText = FeedText.Clean(comp.Str("compensationTierSummary"), 120) is { Length: > 0 } s ? s : null,
                PostedAt = FeedText.ParseDate(j.Str("publishedAt")),
                Description = j.Str("descriptionPlain") is { Length: > 0 } plain ? FeedText.Clean(plain, FeedText.DescriptionCap) : FeedText.HtmlToText(j.Str("descriptionHtml")),
                Tags = JsonRead.NonEmpty(j.Str("department"), j.Str("team"), j.Str("employmentType")),
                DiscoveredAt = now,
            });
        }

        return result;
    }
}

/// <summary>Workable widget API: <c>apply.workable.com/api/v1/widget/accounts/{board}?details=true</c>.</summary>
public sealed class WorkableSource : IJobBoardSource
{
    public string Kind => "workable";
    public bool NeedsBoard => true;

    public Uri BuildUri(BoardTarget target) =>
        new($"https://apply.workable.com/api/v1/widget/accounts/{Uri.EscapeDataString(target.Board!)}?details=true");

    public IReadOnlyList<JobPostingDiscovered> Parse(string body, BoardTarget target, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(body);
        var company = FeedText.Clean(doc.RootElement.Str("name"), 120) is { Length: > 0 } n ? n : target.Label;
        var result = new List<JobPostingDiscovered>();
        foreach (var j in doc.RootElement.Items("jobs"))
        {
            var url = FeedText.SafeUrl(j.Str("url")) ?? FeedText.SafeUrl(j.Str("shortlink"));
            var title = FeedText.Clean(j.Str("title"));
            if (url is null || title.Length == 0)
            {
                continue;
            }

            var country = FeedText.Clean(j.Str("country"), 80);
            var where = string.Join(", ", new[] { FeedText.Clean(j.Str("city"), 80), FeedText.Clean(j.Str("state"), 80), country }.Where(x => x.Length > 0));
            var mode = j.Bool("telecommuting") == true ? WorkMode.Remote : where.Length > 0 ? WorkMode.Onsite : WorkMode.Unknown;
            result.Add(new JobPostingDiscovered
            {
                Source = Kind,
                ExternalId = $"{target.Board}:{j.Str("shortcode")}",
                Url = url,
                Title = title,
                Company = company,
                Location = where,
                WorkMode = mode,
                Countries = country.Length > 0 ? [country] : [],
                Seniority = Classify.SeniorityOf(title, j.Str("experience")),
                PostedAt = FeedText.ParseDate(j.Str("published_on")) ?? FeedText.ParseDate(j.Str("created_at")),
                Description = FeedText.HtmlToText(j.Str("description")),
                Tags = JsonRead.NonEmpty(j.Str("department"), j.Str("function"), j.Str("employment_type")),
                DiscoveredAt = now,
            });
        }

        return result;
    }
}
