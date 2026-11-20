using FluentValidation;
using JobRadar.Tracker.Domain;

namespace JobRadar.Tracker.Application;

public sealed record StartApplicationRequest(Guid PostingId, string? Note);

public sealed record MoveStageRequest(ApplicationStage Stage, string? Note);

public sealed class StartApplicationRequestValidator : AbstractValidator<StartApplicationRequest>
{
    public StartApplicationRequestValidator()
    {
        RuleFor(r => r.PostingId).NotEmpty();
        RuleFor(r => r.Note).MaximumLength(1000);
    }
}

public sealed class MoveStageRequestValidator : AbstractValidator<MoveStageRequest>
{
    public MoveStageRequestValidator()
    {
        RuleFor(r => r.Stage).IsInEnum();
        RuleFor(r => r.Note).MaximumLength(1000);
    }
}
