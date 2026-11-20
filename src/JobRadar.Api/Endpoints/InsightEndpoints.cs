using JobRadar.Api.Stats;
using JobRadar.Matching.Application;
using JobRadar.Tracker.Application;
using Microsoft.EntityFrameworkCore;

namespace JobRadar.Api.Endpoints;

public sealed record ExportBasics(string Label, IReadOnlyList<string> Locations, bool RemoteOnly, int? SalaryFloorUsd);

public sealed record ExportSkill(string Name);

public sealed record ExportMatch(
    Guid PostingId, string Title, string Company, string Url, int Score, IReadOnlyList<string> Reasons, string? Stage, DateTimeOffset AddedAt);

/// <summary>
/// JSON Resume-flavoured export (<c>basics</c>, <c>skills</c> sections) plus the matches, so another tool
/// (a CV tailor, an AI career assistant) can read what this radar found without touching its database.
/// </summary>
public sealed record MatchesExport(string Schema, DateTimeOffset ExportedAt, ExportBasics Basics, IReadOnlyList<ExportSkill> Skills, IReadOnlyList<ExportMatch> Matches);

public sealed record DigestMatch(string Title, string Company, int Score, string Url);

public sealed record WeeklyDigest(
    DateTimeOffset From,
    DateTimeOffset To,
    int NewPostings,
    int NewMatches,
    IReadOnlyList<DigestMatch> TopMatches,
    IReadOnlyDictionary<string, int> StageMoves,
    int QuestionsDue,
    string Summary);

public static class InsightEndpoints
{
    public const string ExportSchema = "https://jsonresume.org/schema (basics, skills) + job-radar matches v1";

    public static IEndpointRouteBuilder MapInsightEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/matches/export", ExportAsync).WithTags("Insights");
        app.MapGet("/api/digest", DigestAsync).WithTags("Insights");
        return app;
    }

    private static async Task<MatchesExport> ExportAsync(ProfileService profiles, IMatchQueries matches, TrackerService tracker, TimeProvider clock, CancellationToken ct)
    {
        var profile = await profiles.GetAsync(ct);
        var stages = (await tracker.ListAsync(ct)).ToDictionary(a => a.PostingId, a => a.Stage.ToString());
        var recent = await matches.RecentMatchesAsync(200, ct);
        return new MatchesExport(
            ExportSchema,
            clock.GetUtcNow(),
            new ExportBasics(profile.Headline, profile.Locations, profile.RemoteOnly, profile.SalaryFloorUsd),
            [.. profile.Skills.Select(s => new ExportSkill(s))],
            [.. recent.Select(m => new ExportMatch(
                m.PostingId, m.Title, m.Company, m.Url, m.Score,
                [.. m.Reasons.Where(r => r.Points != 0).Select(r => $"{(r.Points > 0 ? "+" : "")}{r.Points} {r.Text}")],
                stages.GetValueOrDefault(m.PostingId), m.AddedAt))]);
    }

    /// <summary>What happened in the last <paramref name="days"/> days, composed from three modules' read sides.</summary>
    private static async Task<WeeklyDigest> DigestAsync(
        int? days, StatsDbContext stats, IMatchQueries matches, TrackerService tracker, InterviewBankService bank, TimeProvider clock, CancellationToken ct)
    {
        var to = clock.GetUtcNow();
        var from = to.AddDays(-Math.Clamp(days ?? 7, 1, 90));
        var newPostings = await stats.Postings.CountAsync(p => p.AddedAt >= from, ct);
        var recent = (await matches.RecentMatchesAsync(200, ct)).Where(m => m.AddedAt >= from).ToList();
        var moves = (await tracker.ListAsync(ct))
            .SelectMany(a => a.History)
            .Where(h => h.At >= from && h.From is not null)
            .GroupBy(h => h.To.ToString())
            .ToDictionary(g => g.Key, g => g.Count());
        var due = (await bank.DueAsync(100, ct)).Count;
        var top = recent.OrderByDescending(m => m.Score).Take(5).Select(m => new DigestMatch(m.Title, m.Company, m.Score, m.Url)).ToList();
        var summary = $"{newPostings} new postings, {recent.Count} matches, {moves.Values.Sum()} pipeline moves, {due} questions to practise.";
        return new WeeklyDigest(from, to, newPostings, recent.Count, top, moves, due, summary);
    }
}
