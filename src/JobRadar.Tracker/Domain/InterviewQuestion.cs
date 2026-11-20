namespace JobRadar.Tracker.Domain;

/// <summary>Spaced-repetition state for one card.</summary>
public sealed record Sm2State(int Repetitions, double EaseFactor, int IntervalDays)
{
    public static readonly Sm2State New = new(0, 2.5, 0);
}

/// <summary>
/// SuperMemo-2 (Wozniak, 1990). Grade 0..5: below 3 restarts the card tomorrow without touching the ease factor;
/// 3..5 grows the interval (1 day, 6 days, then previous interval × ease) and nudges the ease, never below 1.3.
/// </summary>
public static class Sm2
{
    public const double MinEase = 1.3;

    public static Sm2State Next(Sm2State state, int grade)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(grade, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(grade, 5);

        if (grade < 3)
        {
            return state with { Repetitions = 0, IntervalDays = 1 };
        }

        var interval = state.Repetitions switch
        {
            0 => 1,
            1 => 6,
            _ => (int)Math.Round(state.IntervalDays * state.EaseFactor, MidpointRounding.AwayFromZero),
        };
        var miss = 5 - grade;
        var ease = Math.Max(MinEase, state.EaseFactor + (0.1 - (miss * (0.08 + (miss * 0.02)))));
        return new Sm2State(state.Repetitions + 1, Math.Round(ease, 2), interval);
    }
}

/// <summary>A question to practise for an application's interviews, scheduled with SM-2.</summary>
public sealed class InterviewQuestion
{
    /// <summary>Seeded when an application first reaches Interview; the user adds role-specific ones.</summary>
    public static readonly IReadOnlyList<string> Starters =
    [
        "Walk me through a project you are proud of: the problem, your part, the trade-offs.",
        "Tell me about a production incident you handled. What changed afterwards?",
        "Why this company and this role?",
    ];

    private InterviewQuestion()
    {
    }

    public Guid Id { get; private init; }
    public Guid ApplicationId { get; private init; }
    public string Prompt { get; private init; } = "";
    public string? Notes { get; private set; }
    public int Repetitions { get; private set; }
    public double EaseFactor { get; private set; }
    public int IntervalDays { get; private set; }
    public DateTimeOffset DueAt { get; private set; }
    public DateTimeOffset? LastReviewedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }

    public static InterviewQuestion Create(Guid id, Guid applicationId, string prompt, string? notes, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("A question needs a prompt.", nameof(prompt));
        }

        return new InterviewQuestion
        {
            Id = id,
            ApplicationId = applicationId,
            Prompt = prompt.Trim(),
            Notes = notes,
            Repetitions = Sm2State.New.Repetitions,
            EaseFactor = Sm2State.New.EaseFactor,
            IntervalDays = Sm2State.New.IntervalDays,
            DueAt = now,
            CreatedAt = now,
        };
    }

    public bool IsDue(DateTimeOffset now) => DueAt <= now;

    /// <summary>Records a practice attempt graded 0 (blank) to 5 (perfect) and schedules the next one.</summary>
    public void Review(int grade, DateTimeOffset now, string? notes = null)
    {
        var next = Sm2.Next(new Sm2State(Repetitions, EaseFactor, IntervalDays), grade);
        Repetitions = next.Repetitions;
        EaseFactor = next.EaseFactor;
        IntervalDays = next.IntervalDays;
        DueAt = now.AddDays(next.IntervalDays);
        LastReviewedAt = now;
        Notes = notes ?? Notes;
    }
}
