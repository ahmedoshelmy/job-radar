using FluentValidation;
using JobRadar.Matching.Domain;

namespace JobRadar.Matching.Application;

/// <summary>Shape checks at the boundary (400 with field errors); the domain still guards its own invariants.</summary>
public sealed class ProfileDraftValidator : AbstractValidator<ProfileDraft>
{
    public ProfileDraftValidator()
    {
        RuleFor(p => p.Headline).MaximumLength(200);
        RuleFor(p => p.Skills).NotEmpty().Must(s => s.Count <= 50).WithMessage("At most 50 skills.");
        RuleForEach(p => p.Skills).NotEmpty().MaximumLength(40);
        RuleForEach(p => p.Seniorities).IsInEnum();
        RuleFor(p => p.Locations).Must(l => l.Count <= 30).WithMessage("At most 30 locations.");
        RuleForEach(p => p.Locations).NotEmpty().MaximumLength(60);
        RuleFor(p => p.SalaryFloorUsd).InclusiveBetween(0, 1_000_000).When(p => p.SalaryFloorUsd is not null);
        RuleFor(p => p.MaxYearsAsked).InclusiveBetween(1, 40).When(p => p.MaxYearsAsked is not null);
        RuleForEach(p => p.ExcludedTitleWords).NotEmpty().MaximumLength(40);
        RuleFor(p => p.NotifyThreshold).InclusiveBetween(0, 100);
    }
}
