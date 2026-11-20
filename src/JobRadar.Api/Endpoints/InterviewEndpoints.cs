using FluentValidation;
using JobRadar.Tracker.Application;
using JobRadar.Tracker.Domain;

namespace JobRadar.Api.Endpoints;

public sealed record QuestionDto(
    Guid Id, Guid ApplicationId, string Prompt, string? Notes, int Repetitions, double EaseFactor, int IntervalDays,
    DateTimeOffset DueAt, DateTimeOffset? LastReviewedAt);

public sealed record DueQuestionDto(QuestionDto Question, string Title, string Company);

/// <summary>Interview bank: questions per application, practised on an SM-2 schedule.</summary>
public static class InterviewEndpoints
{
    public static IEndpointRouteBuilder MapInterviewEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/applications/{id:guid}/questions", async (Guid id, InterviewBankService bank, CancellationToken ct) =>
            (await bank.ForApplicationAsync(id, ct)).Select(ToDto).ToList()).WithTags("Interview bank");

        app.MapPost("/api/applications/{id:guid}/questions", async (
            Guid id, AddQuestionRequest request, IValidator<AddQuestionRequest> validator, InterviewBankService bank, CancellationToken ct) =>
        {
            var check = await validator.ValidateAsync(request, ct);
            if (!check.IsValid)
            {
                return Results.ValidationProblem(check.ToDictionary());
            }

            var question = await bank.AddAsync(id, request, ct);
            return question is null ? Results.NotFound() : Results.Created($"/api/questions/{question.Id}", ToDto(question));
        }).WithTags("Interview bank");

        app.MapGet("/api/questions/due", async (int? take, InterviewBankService bank, TrackerService tracker, CancellationToken ct) =>
        {
            var due = await bank.DueAsync(take ?? 20, ct);
            var applications = (await tracker.ListAsync(ct)).ToDictionary(a => a.Id);
            return due.Select(q => new DueQuestionDto(
                ToDto(q),
                applications.GetValueOrDefault(q.ApplicationId)?.Title ?? "",
                applications.GetValueOrDefault(q.ApplicationId)?.Company ?? "")).ToList();
        }).WithTags("Interview bank");

        app.MapPost("/api/questions/{id:guid}/review", async (
            Guid id, ReviewRequest request, IValidator<ReviewRequest> validator, InterviewBankService bank, CancellationToken ct) =>
        {
            var check = await validator.ValidateAsync(request, ct);
            if (!check.IsValid)
            {
                return Results.ValidationProblem(check.ToDictionary());
            }

            var question = await bank.ReviewAsync(id, request, ct);
            return question is null ? Results.NotFound() : Results.Ok(ToDto(question));
        }).WithTags("Interview bank");

        return app;
    }

    private static QuestionDto ToDto(InterviewQuestion q) =>
        new(q.Id, q.ApplicationId, q.Prompt, q.Notes, q.Repetitions, q.EaseFactor, q.IntervalDays, q.DueAt, q.LastReviewedAt);
}
