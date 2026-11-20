using JobRadar.Contracts;

namespace JobRadar.Matching.Domain;

public sealed class DomainRuleException(string message) : Exception(message);

/// <summary>What the user is looking for. Single-user app: one profile, stable id.</summary>
public sealed class Profile
{
    public static readonly Guid DefaultId = new("a11ce5ed-c0de-4bad-9fab-defa017f11e0");
    public const int DefaultNotifyThreshold = 60;

    public Guid Id { get; init; } = DefaultId;
    public string Headline { get; private set; } = "";
    public List<string> Skills { get; private set; } = [];
    public List<Seniority> Seniorities { get; private set; } = [];
    public bool RemoteOnly { get; private set; }

    /// <summary>Places the user can work from or for: countries, regions ("EMEA"), or "Worldwide".</summary>
    public List<string> Locations { get; private set; } = [];

    public int? SalaryFloorUsd { get; private set; }

    /// <summary>A posting asking for this many years or more is marked down.</summary>
    public int? MaxYearsAsked { get; private set; }

    public List<string> ExcludedTitleWords { get; private set; } = [];
    public int NotifyThreshold { get; private set; } = DefaultNotifyThreshold;
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Profile Demo(DateTimeOffset now)
    {
        var profile = new Profile();
        profile.Update(new ProfileDraft(
            "Backend .NET engineer, remote / EMEA",
            [".NET", "C#", "ASP.NET Core", "PostgreSQL", "Azure", "Docker", "RabbitMQ"],
            [Seniority.Junior, Seniority.Mid, Seniority.Senior],
            RemoteOnly: true,
            ["Egypt", "EMEA", "Worldwide", "Anywhere"],
            SalaryFloorUsd: 40_000,
            MaxYearsAsked: 7,
            ["frontend", "ios", "android", "qa", "sales"],
            DefaultNotifyThreshold), now);
        return profile;
    }

    public void Update(ProfileDraft draft, DateTimeOffset now)
    {
        if (draft.Skills.Count == 0)
        {
            throw new DomainRuleException("A profile needs at least one skill to score against.");
        }

        if (draft.NotifyThreshold is < 0 or > 100)
        {
            throw new DomainRuleException("The notify threshold is a score from 0 to 100.");
        }

        if (draft.SalaryFloorUsd is < 0 || draft.MaxYearsAsked is < 0)
        {
            throw new DomainRuleException("Salary floor and years must not be negative.");
        }

        Headline = draft.Headline.Trim();
        Skills = Distinct(draft.Skills);
        Seniorities = [.. draft.Seniorities.Distinct()];
        RemoteOnly = draft.RemoteOnly;
        Locations = Distinct(draft.Locations);
        SalaryFloorUsd = draft.SalaryFloorUsd;
        MaxYearsAsked = draft.MaxYearsAsked;
        ExcludedTitleWords = Distinct(draft.ExcludedTitleWords);
        NotifyThreshold = draft.NotifyThreshold;
        UpdatedAt = now;
    }

    private static List<string> Distinct(IEnumerable<string> values) =>
        [.. values.Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
}

public sealed record ProfileDraft(
    string Headline,
    IReadOnlyList<string> Skills,
    IReadOnlyList<Seniority> Seniorities,
    bool RemoteOnly,
    IReadOnlyList<string> Locations,
    int? SalaryFloorUsd,
    int? MaxYearsAsked,
    IReadOnlyList<string> ExcludedTitleWords,
    int NotifyThreshold);
