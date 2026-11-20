using JobRadar.Catalog.Application;
using JobRadar.Contracts;
using JobRadar.Matching.Application;
using JobRadar.Matching.Domain;

namespace JobRadar.Api.Endpoints;

public sealed record PostingDto(
    Guid Id,
    string Title,
    string Company,
    string Location,
    WorkMode WorkMode,
    Seniority Seniority,
    string Url,
    string Source,
    IReadOnlyList<string> AlsoSeenIn,
    string? SalaryText,
    DateTimeOffset? PostedAt,
    DateTimeOffset FirstSeenAt,
    int? Score,
    IReadOnlyList<MatchReason> Reasons);

/// <summary>
/// API composition: Catalog answers "which postings", Matching answers "how good". The join happens here, in memory,
/// so neither module reads the other's schema.
/// </summary>
public static class PostingEndpoints
{
    public static IEndpointRouteBuilder MapPostingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/postings", SearchAsync).WithTags("Postings");
        app.MapGet("/api/postings/{id:guid}/explanation", async (Guid id, ExplainMatch explain, CancellationToken ct) =>
            await explain.HandleAsync(id, ct) is { } explanation
                ? Results.Ok(explanation)
                : Results.Problem("No score for this posting yet.", statusCode: StatusCodes.Status404NotFound))
            .WithTags("Postings");
        app.MapGet("/api/matches", async (IMatchQueries matches, int? take, CancellationToken ct) =>
            await matches.RecentMatchesAsync(take ?? 50, ct)).WithTags("Postings");
        return app;
    }

    private static async Task<IReadOnlyList<PostingDto>> SearchAsync(
        ICatalogQueries catalog, IMatchQueries matches, string? query, bool? remote, int? minScore, CancellationToken ct)
    {
        var postings = await catalog.SearchAsync(new PostingSearch(query, remote == true ? WorkMode.Remote : null), ct);
        var scores = await matches.ScoresForAsync([.. postings.Select(p => p.Id)], ct);
        return [.. postings
            .Select(p => (Posting: p, Score: scores.GetValueOrDefault(p.Id)))
            .Where(x => minScore is null || (x.Score?.Score ?? 0) >= minScore)
            .OrderByDescending(x => x.Score?.Score ?? -1)
            .ThenByDescending(x => x.Posting.FirstSeenAt)
            .Select(x => new PostingDto(
                x.Posting.Id, x.Posting.Title, x.Posting.Company, x.Posting.Location, x.Posting.WorkMode, x.Posting.Seniority,
                x.Posting.Url, x.Posting.Source, x.Posting.AlsoSeenIn, x.Posting.SalaryText, x.Posting.PostedAt, x.Posting.FirstSeenAt,
                x.Score?.Score, x.Score?.Reasons ?? []))];
    }
}
