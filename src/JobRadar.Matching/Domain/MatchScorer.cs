using System.Globalization;
using System.Text.RegularExpressions;
using JobRadar.Contracts;

namespace JobRadar.Matching.Domain;

/// <summary>The posting fields scoring needs, copied from <c>JobPostingAdded</c> (Matching never reads Catalog tables).</summary>
public sealed record PostingSnapshot(
    string Title,
    string Company,
    string Location,
    WorkMode WorkMode,
    IReadOnlyList<string> Countries,
    Seniority Seniority,
    int? SalaryMinUsd,
    int? SalaryMaxUsd,
    string Description,
    IReadOnlyList<string> Tags);

/// <summary>One line of "why": a code for the UI, signed points and a human sentence.</summary>
public sealed record MatchReason(string Code, int Points, string Text);

public sealed record MatchResult(int Score, IReadOnlyList<MatchReason> Reasons);

/// <summary>
/// Rule-based, explainable scoring: every point comes with a reason the user can read and disagree with.
/// Pure and deterministic, so each rule has its own unit test. The total is clamped to 0..100.
/// </summary>
public static partial class MatchScorer
{
    public const int TitleSkillPoints = 12;
    public const int BodySkillPoints = 5;
    public const int SkillCap = 45;
    public const int LevelFits = 15;
    public const int LevelUnknown = 5;
    public const int LevelOff = -20;
    public const int RemotePoints = 15;
    public const int NotRemotePenalty = -40;
    public const int PlaceFits = 15;
    public const int PlaceUnstated = 5;
    public const int PlaceClosed = -25;
    public const int SalaryFits = 10;
    public const int SalaryBelow = -30;
    public const int TooManyYears = -20;
    public const int ExcludedWord = -50;

    private static readonly string[] OpenWorld = ["worldwide", "anywhere", "global", "globally", "international"];

    public static MatchResult Score(PostingSnapshot posting, Profile profile)
    {
        var reasons = new List<MatchReason>();
        reasons.AddRange(Skills(posting, profile));
        reasons.Add(Level(posting, profile));
        reasons.AddRange(Place(posting, profile));
        reasons.Add(Salary(posting, profile));
        reasons.AddRange(Years(posting, profile));
        reasons.AddRange(Excluded(posting, profile));

        var total = Math.Clamp(reasons.Sum(r => r.Points), 0, 100);
        return new MatchResult(total, reasons);
    }

    internal static IEnumerable<MatchReason> Skills(PostingSnapshot posting, Profile profile)
    {
        var body = $"{posting.Description} {string.Join(' ', posting.Tags)}";
        var inTitle = profile.Skills.Where(s => Mentions(posting.Title, s)).ToList();
        var inBody = profile.Skills.Except(inTitle).Where(s => Mentions(body, s)).ToList();
        if (inTitle.Count == 0 && inBody.Count == 0)
        {
            yield return new MatchReason("skills", 0, "None of your skills are mentioned");
            yield break;
        }

        var points = Math.Min(SkillCap, (inTitle.Count * TitleSkillPoints) + (inBody.Count * BodySkillPoints));
        var parts = new List<string>();
        if (inTitle.Count > 0)
        {
            parts.Add($"{string.Join(", ", inTitle)} in the title");
        }

        if (inBody.Count > 0)
        {
            parts.Add($"{string.Join(", ", inBody)} in the description");
        }

        yield return new MatchReason("skills", points, $"Skills: {string.Join("; ", parts)}");
    }

    internal static MatchReason Level(PostingSnapshot posting, Profile profile)
    {
        var level = posting.Seniority;
        if (level == Seniority.Unknown)
        {
            return new MatchReason("level", LevelUnknown, "Level not stated");
        }

        var name = level.ToString().ToLowerInvariant();
        return profile.Seniorities.Count == 0 || profile.Seniorities.Contains(level)
            ? new MatchReason("level", LevelFits, $"{Capitalize(name)} level fits")
            : new MatchReason("level", LevelOff, $"{Capitalize(name)} level; you target {string.Join(", ", profile.Seniorities.Select(s => s.ToString().ToLowerInvariant()))}");
    }

    internal static IEnumerable<MatchReason> Place(PostingSnapshot posting, Profile profile)
    {
        switch (posting.WorkMode)
        {
            case WorkMode.Remote:
                yield return new MatchReason("remote", RemotePoints, "Remote");
                break;
            case WorkMode.Hybrid or WorkMode.Onsite when profile.RemoteOnly:
                yield return new MatchReason("remote", NotRemotePenalty, $"{posting.WorkMode}; you want remote only");
                yield break;
            default:
                break;
        }

        var places = posting.Countries.Count > 0 ? posting.Countries : [posting.Location];
        places = [.. places.Where(p => p.Length > 0 && !p.Equals("remote", StringComparison.OrdinalIgnoreCase))];
        if (places.Count == 0)
        {
            yield return new MatchReason("place", PlaceUnstated, "No region limit stated");
            yield break;
        }

        var open = places.FirstOrDefault(p => OpenWorld.Any(w => p.Contains(w, StringComparison.OrdinalIgnoreCase)));
        if (open is not null)
        {
            yield return new MatchReason("place", PlaceFits, $"Open to {open}");
            yield break;
        }

        var fit = places.FirstOrDefault(p => profile.Locations.Any(l => PlaceMatches(p, l)));
        yield return fit is not null
            ? new MatchReason("place", PlaceFits, $"Open to {fit}")
            : new MatchReason("place", PlaceClosed, $"Limited to {string.Join(", ", places.Take(3))}");
    }

    internal static MatchReason Salary(PostingSnapshot posting, Profile profile)
    {
        var top = posting.SalaryMaxUsd ?? posting.SalaryMinUsd;
        if (top is null)
        {
            return new MatchReason("salary", 0, "Salary not listed in USD");
        }

        var shown = top.Value.ToString("#,0", CultureInfo.InvariantCulture);
        if (profile.SalaryFloorUsd is not { } floor)
        {
            return new MatchReason("salary", 0, $"Pays up to ${shown}");
        }

        return top >= floor
            ? new MatchReason("salary", SalaryFits, $"Pays up to ${shown}, above your floor")
            : new MatchReason("salary", SalaryBelow, $"Pays up to ${shown}, below your ${floor.ToString("#,0", CultureInfo.InvariantCulture)} floor");
    }

    internal static IEnumerable<MatchReason> Years(PostingSnapshot posting, Profile profile)
    {
        if (profile.MaxYearsAsked is { } max && YearsAsked(posting.Description) is { } asked && asked >= max)
        {
            yield return new MatchReason("years", TooManyYears, $"Asks for {asked}+ years");
        }
    }

    internal static IEnumerable<MatchReason> Excluded(PostingSnapshot posting, Profile profile)
    {
        var word = profile.ExcludedTitleWords.FirstOrDefault(w => Mentions(posting.Title, w));
        if (word is not null)
        {
            yield return new MatchReason("excluded", ExcludedWord, $"Title mentions \"{word}\"");
        }
    }

    /// <summary>The largest "N+ years" / "at least N years" / "N-M years" figure in the text.</summary>
    public static int? YearsAsked(string text)
    {
        int? most = null;
        foreach (Match m in YearsPattern().Matches(text.Length > 8000 ? text[..8000] : text))
        {
            var raw = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            var n = int.Parse(raw, CultureInfo.InvariantCulture);
            if (n is > 0 and < 40 && (most is null || n > most))
            {
                most = n;
            }
        }

        return most;
    }

    /// <summary>Whole-token match that works for ".NET", "C#" and "ASP.NET Core" alike.</summary>
    internal static bool Mentions(string text, string term) =>
        Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(term)}(?![\p{{L}}\p{{N}}#+])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    private static bool PlaceMatches(string place, string wanted) =>
        place.Contains(wanted, StringComparison.OrdinalIgnoreCase) || wanted.Contains(place, StringComparison.OrdinalIgnoreCase);

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    [GeneratedRegex(@"(?:(?:at least|minimum(?: of)?|min\.?)\s*(\d{1,2})|(\d{1,2})\s*\+|(\d{1,2})\s*(?:-|–|to)\s*\d{1,2})\s*(?:\+\s*)?(?:years|yrs)", RegexOptions.IgnoreCase)]
    private static partial Regex YearsPattern();
}
