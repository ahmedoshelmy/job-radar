using FluentValidation;
using JobRadar.Catalog.Application;
using JobRadar.Matching.Application;
using JobRadar.Matching.Domain;
using JobRadar.Tracker.Application;
using JobRadar.Tracker.Domain;

namespace JobRadar.Api.Endpoints;

public sealed record ProfileDto(ProfileDraft Profile, DateTimeOffset UpdatedAt);

public sealed record ProfileUpdated(ProfileDto Profile, int Rescored, int Announced);

public sealed record StageEventDto(string? From, string To, DateTimeOffset At, string? Note);

public sealed record ApplicationDto(
    Guid Id, Guid PostingId, string Title, string Company, string Url, string Stage,
    IReadOnlyList<string> AllowedNext, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, IReadOnlyList<StageEventDto> History);

public static class ProfileAndTrackerEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var profile = app.MapGroup("/api/profile").WithTags("Profile");
        profile.MapGet("", async (ProfileService service, CancellationToken ct) => ToDto(await service.GetAsync(ct)));
        profile.MapPut("", async (ProfileDraft draft, IValidator<ProfileDraft> validator, ProfileService service, CancellationToken ct) =>
        {
            var check = await validator.ValidateAsync(draft, ct);
            if (!check.IsValid)
            {
                return Results.ValidationProblem(check.ToDictionary());
            }

            var (saved, rescored, announced) = await service.UpdateAsync(draft, ct);
            return Results.Ok(new ProfileUpdated(ToDto(saved), rescored, announced));
        });
        return app;
    }

    public static IEndpointRouteBuilder MapTrackerEndpoints(this IEndpointRouteBuilder app)
    {
        var applications = app.MapGroup("/api/applications").WithTags("Applications");
        applications.MapGet("", async (TrackerService tracker, CancellationToken ct) =>
            (await tracker.ListAsync(ct)).Select(ToDto).ToList());

        applications.MapPost("", async (StartApplicationRequest request, IValidator<StartApplicationRequest> validator, TrackerService tracker, CancellationToken ct) =>
        {
            var check = await validator.ValidateAsync(request, ct);
            if (!check.IsValid)
            {
                return Results.ValidationProblem(check.ToDictionary());
            }

            var created = await tracker.StartAsync(request.PostingId, request.Note, ct);
            return Results.Created($"/api/applications/{created.Id}", ToDto(created));
        });

        applications.MapPost("/{id:guid}/stage", async (Guid id, MoveStageRequest request, IValidator<MoveStageRequest> validator, TrackerService tracker, CancellationToken ct) =>
        {
            var check = await validator.ValidateAsync(request, ct);
            if (!check.IsValid)
            {
                return Results.ValidationProblem(check.ToDictionary());
            }

            var moved = await tracker.MoveAsync(id, request.Stage, request.Note, ct);
            return moved is null ? Results.NotFound() : Results.Ok(ToDto(moved));
        });
        return app;
    }

    private static ProfileDto ToDto(Profile p) => new(
        new ProfileDraft(p.Headline, p.Skills, p.Seniorities, p.RemoteOnly, p.Locations, p.SalaryFloorUsd, p.MaxYearsAsked, p.ExcludedTitleWords, p.NotifyThreshold),
        p.UpdatedAt);

    private static ApplicationDto ToDto(JobApplication a) => new(
        a.Id, a.PostingId, a.Title, a.Company, a.Url, a.Stage.ToString(),
        [.. StageRules.AllowedFrom(a.Stage).Select(s => s.ToString())],
        a.CreatedAt, a.UpdatedAt,
        [.. a.History.OrderBy(h => h.At).Select(h => new StageEventDto(h.From?.ToString(), h.To.ToString(), h.At, h.Note))]);
}

/// <summary>Adapter: the Tracker's port answered by the Catalog's read side, in-process.</summary>
internal sealed class CatalogPostingLookup(ICatalogQueries catalog) : IPostingLookup
{
    public async Task<PostingRef?> FindAsync(Guid postingId, CancellationToken cancellationToken) =>
        await catalog.GetAsync(postingId, cancellationToken) is { } p ? new PostingRef(p.Id, p.Title, p.Company, p.Url) : null;
}
