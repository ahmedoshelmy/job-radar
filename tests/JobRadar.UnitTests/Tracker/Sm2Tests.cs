using JobRadar.Tracker.Domain;

namespace JobRadar.UnitTests.Tracker;

public sealed class Sm2Tests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Intervals_grow_one_six_then_by_ease()
    {
        var s = Sm2.Next(Sm2State.New, 4);
        s.ShouldBe(new Sm2State(1, 2.5, 1));

        s = Sm2.Next(s, 4);
        s.ShouldBe(new Sm2State(2, 2.5, 6));

        s = Sm2.Next(s, 4);
        s.ShouldBe(new Sm2State(3, 2.5, 15));
    }

    [Theory]
    [InlineData(5, 2.6)]
    [InlineData(4, 2.5)]
    [InlineData(3, 2.36)]
    public void Ease_moves_with_the_grade(int grade, double ease) => Sm2.Next(Sm2State.New, grade).EaseFactor.ShouldBe(ease);

    [Fact]
    public void A_lapse_restarts_tomorrow_and_keeps_the_ease()
    {
        var learned = new Sm2State(4, 2.2, 30);

        Sm2.Next(learned, 2).ShouldBe(new Sm2State(0, 2.2, 1));
    }

    [Fact]
    public void Ease_never_drops_below_the_floor()
    {
        var s = new Sm2State(2, 1.32, 6);

        Sm2.Next(s, 3).EaseFactor.ShouldBe(Sm2.MinEase);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void Grades_outside_zero_to_five_are_refused(int grade) =>
        Should.Throw<ArgumentOutOfRangeException>(() => Sm2.Next(Sm2State.New, grade));

    [Fact]
    public void A_new_question_is_due_now_and_a_review_schedules_the_next()
    {
        var question = InterviewQuestion.Create(Guid.NewGuid(), Guid.NewGuid(), "  Design a rate limiter  ", null, T0);
        question.Prompt.ShouldBe("Design a rate limiter");
        question.IsDue(T0).ShouldBeTrue();

        question.Review(5, T0, "token bucket per host");

        question.DueAt.ShouldBe(T0.AddDays(1));
        question.IsDue(T0.AddHours(23)).ShouldBeFalse();
        question.Notes.ShouldBe("token bucket per host");
        question.LastReviewedAt.ShouldBe(T0);
    }

    [Fact]
    public void A_question_needs_a_prompt() =>
        Should.Throw<ArgumentException>(() => InterviewQuestion.Create(Guid.NewGuid(), Guid.NewGuid(), " ", null, T0));
}
