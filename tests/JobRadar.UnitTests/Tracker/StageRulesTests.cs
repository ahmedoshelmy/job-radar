using JobRadar.Tracker.Domain;

namespace JobRadar.UnitTests.Tracker;

public sealed class StageRulesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    private static JobApplication New() =>
        JobApplication.Start(Guid.NewGuid(), Guid.NewGuid(), "Backend Engineer", "Nilewave Labs", "https://example.com/1", T0, "found it");

    [Theory]
    [InlineData(ApplicationStage.Saved, ApplicationStage.Applied)]
    [InlineData(ApplicationStage.Saved, ApplicationStage.Withdrawn)]
    [InlineData(ApplicationStage.Applied, ApplicationStage.Interview)]
    [InlineData(ApplicationStage.Applied, ApplicationStage.Rejected)]
    [InlineData(ApplicationStage.Interview, ApplicationStage.Offer)]
    [InlineData(ApplicationStage.Interview, ApplicationStage.Rejected)]
    [InlineData(ApplicationStage.Offer, ApplicationStage.Withdrawn)]
    public void Legal_moves(ApplicationStage from, ApplicationStage to) => StageRules.CanMove(from, to).ShouldBeTrue();

    [Theory]
    [InlineData(ApplicationStage.Saved, ApplicationStage.Interview)]
    [InlineData(ApplicationStage.Saved, ApplicationStage.Offer)]
    [InlineData(ApplicationStage.Applied, ApplicationStage.Saved)]
    [InlineData(ApplicationStage.Interview, ApplicationStage.Applied)]
    [InlineData(ApplicationStage.Rejected, ApplicationStage.Applied)]
    [InlineData(ApplicationStage.Withdrawn, ApplicationStage.Saved)]
    [InlineData(ApplicationStage.Offer, ApplicationStage.Rejected)]
    [InlineData(ApplicationStage.Applied, ApplicationStage.Applied)]
    public void Illegal_moves(ApplicationStage from, ApplicationStage to) => StageRules.CanMove(from, to).ShouldBeFalse();

    [Fact]
    public void A_new_application_starts_saved_with_one_history_entry()
    {
        var application = New();

        application.Stage.ShouldBe(ApplicationStage.Saved);
        application.History.Count.ShouldBe(1);
        application.History[0].From.ShouldBeNull();
        application.History[0].Note.ShouldBe("found it");
    }

    [Fact]
    public void Moving_through_the_pipeline_records_every_step()
    {
        var application = New();

        application.MoveTo(ApplicationStage.Applied, T0.AddDays(1), null);
        var last = application.MoveTo(ApplicationStage.Interview, T0.AddDays(5), "tech screen");

        application.Stage.ShouldBe(ApplicationStage.Interview);
        application.UpdatedAt.ShouldBe(T0.AddDays(5));
        application.History.Select(h => h.To).ShouldBe([ApplicationStage.Saved, ApplicationStage.Applied, ApplicationStage.Interview]);
        last.From.ShouldBe(ApplicationStage.Applied);
        last.Note.ShouldBe("tech screen");
    }

    [Fact]
    public void An_illegal_move_is_rejected_and_changes_nothing()
    {
        var application = New();

        var error = Should.Throw<IllegalStageTransitionException>(() => application.MoveTo(ApplicationStage.Offer, T0.AddDays(1), null));

        error.From.ShouldBe(ApplicationStage.Saved);
        error.To.ShouldBe(ApplicationStage.Offer);
        application.Stage.ShouldBe(ApplicationStage.Saved);
        application.History.Count.ShouldBe(1);
    }

    [Fact]
    public void Final_stages_allow_nothing()
    {
        StageRules.AllowedFrom(ApplicationStage.Rejected).ShouldBeEmpty();
        StageRules.AllowedFrom(ApplicationStage.Withdrawn).ShouldBeEmpty();
    }
}
