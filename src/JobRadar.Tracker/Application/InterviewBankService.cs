using FluentValidation;
using JobRadar.Tracker.Domain;
using MassTransit;

namespace JobRadar.Tracker.Application;

public interface IQuestionRepository
{
    Task<InterviewQuestion?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<InterviewQuestion>> ForApplicationAsync(Guid applicationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<InterviewQuestion>> DueAsync(DateTimeOffset now, int take, CancellationToken cancellationToken);

    Task<bool> AnyForApplicationAsync(Guid applicationId, CancellationToken cancellationToken);

    void Add(InterviewQuestion question);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record AddQuestionRequest(string Prompt, string? Notes);

public sealed record ReviewRequest(int Grade, string? Notes);

public sealed class AddQuestionRequestValidator : AbstractValidator<AddQuestionRequest>
{
    public AddQuestionRequestValidator()
    {
        RuleFor(r => r.Prompt).NotEmpty().MaximumLength(500);
        RuleFor(r => r.Notes).MaximumLength(4000);
    }
}

public sealed class ReviewRequestValidator : AbstractValidator<ReviewRequest>
{
    public ReviewRequestValidator()
    {
        RuleFor(r => r.Grade).InclusiveBetween(0, 5);
        RuleFor(r => r.Notes).MaximumLength(4000);
    }
}

/// <summary>Per-application interview question bank with SM-2 practice scheduling.</summary>
public sealed class InterviewBankService(IQuestionRepository questions, IApplicationRepository applications, TimeProvider clock)
{
    /// <returns>null when the application does not exist.</returns>
    public async Task<InterviewQuestion?> AddAsync(Guid applicationId, AddQuestionRequest request, CancellationToken cancellationToken)
    {
        if (await applications.GetAsync(applicationId, cancellationToken) is null)
        {
            return null;
        }

        var question = InterviewQuestion.Create(NewId.NextSequentialGuid(), applicationId, request.Prompt, request.Notes, clock.GetUtcNow());
        questions.Add(question);
        await questions.SaveChangesAsync(cancellationToken);
        return question;
    }

    public Task<IReadOnlyList<InterviewQuestion>> ForApplicationAsync(Guid applicationId, CancellationToken cancellationToken) =>
        questions.ForApplicationAsync(applicationId, cancellationToken);

    public Task<IReadOnlyList<InterviewQuestion>> DueAsync(int take, CancellationToken cancellationToken) =>
        questions.DueAsync(clock.GetUtcNow(), take, cancellationToken);

    /// <returns>null when the question does not exist.</returns>
    public async Task<InterviewQuestion?> ReviewAsync(Guid questionId, ReviewRequest request, CancellationToken cancellationToken)
    {
        var question = await questions.GetAsync(questionId, cancellationToken);
        if (question is null)
        {
            return null;
        }

        question.Review(request.Grade, clock.GetUtcNow(), request.Notes);
        await questions.SaveChangesAsync(cancellationToken);
        return question;
    }

    /// <summary>The first time an application reaches Interview, start its bank with the standard questions.</summary>
    internal async Task SeedStartersAsync(JobApplication application, CancellationToken cancellationToken)
    {
        if (await questions.AnyForApplicationAsync(application.Id, cancellationToken))
        {
            return;
        }

        var now = clock.GetUtcNow();
        foreach (var prompt in InterviewQuestion.Starters)
        {
            questions.Add(InterviewQuestion.Create(NewId.NextSequentialGuid(), application.Id, prompt, null, now));
        }
    }
}
