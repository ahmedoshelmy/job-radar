namespace JobRadar.Tracker.Domain;

public enum ApplicationStage
{
    Saved,
    Applied,
    Interview,
    Offer,
    Rejected,
    Withdrawn,
}

public sealed class IllegalStageTransitionException(ApplicationStage from, ApplicationStage to)
    : Exception($"An application cannot move from {from} to {to}.")
{
    public ApplicationStage From { get; } = from;
    public ApplicationStage To { get; } = to;
}

/// <summary>
/// The pipeline as a state machine: Saved → Applied → Interview → Offer, with Rejected and Withdrawn as exits.
/// Rejected and Withdrawn are final; an offer can only be withdrawn from (declined).
/// </summary>
public static class StageRules
{
    private static readonly Dictionary<ApplicationStage, ApplicationStage[]> Next = new()
    {
        [ApplicationStage.Saved] = [ApplicationStage.Applied, ApplicationStage.Withdrawn],
        [ApplicationStage.Applied] = [ApplicationStage.Interview, ApplicationStage.Rejected, ApplicationStage.Withdrawn],
        [ApplicationStage.Interview] = [ApplicationStage.Offer, ApplicationStage.Rejected, ApplicationStage.Withdrawn],
        [ApplicationStage.Offer] = [ApplicationStage.Withdrawn],
        [ApplicationStage.Rejected] = [],
        [ApplicationStage.Withdrawn] = [],
    };

    public static IReadOnlyList<ApplicationStage> AllowedFrom(ApplicationStage stage) => Next[stage];

    public static bool CanMove(ApplicationStage from, ApplicationStage to) => Next[from].Contains(to);
}

/// <summary>One step in an application's history. The list of these is the audit trail shown in the UI.</summary>
public sealed class StageEvent
{
    public Guid Id { get; init; }
    public ApplicationStage? From { get; init; }
    public ApplicationStage To { get; init; }
    public DateTimeOffset At { get; init; }
    public string? Note { get; init; }
}

/// <summary>The aggregate: a posting the user is pursuing. Every stage change is validated here and recorded.</summary>
public sealed class JobApplication
{
    private readonly List<StageEvent> _history = [];

    private JobApplication()
    {
    }

    public Guid Id { get; private init; }
    public Guid PostingId { get; private init; }
    public string Title { get; private init; } = "";
    public string Company { get; private init; } = "";
    public string Url { get; private init; } = "";
    public ApplicationStage Stage { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyList<StageEvent> History => _history;

    public static JobApplication Start(Guid id, Guid postingId, string title, string company, string url, DateTimeOffset at, string? note)
    {
        var application = new JobApplication
        {
            Id = id,
            PostingId = postingId,
            Title = title,
            Company = company,
            Url = url,
            Stage = ApplicationStage.Saved,
            CreatedAt = at,
            UpdatedAt = at,
        };
        application._history.Add(new StageEvent { Id = Guid.NewGuid(), From = null, To = ApplicationStage.Saved, At = at, Note = note });
        return application;
    }

    /// <returns>The recorded event.</returns>
    /// <exception cref="IllegalStageTransitionException">The pipeline does not allow this move.</exception>
    public StageEvent MoveTo(ApplicationStage to, DateTimeOffset at, string? note)
    {
        if (!StageRules.CanMove(Stage, to))
        {
            throw new IllegalStageTransitionException(Stage, to);
        }

        var change = new StageEvent { Id = Guid.NewGuid(), From = Stage, To = to, At = at, Note = note };
        _history.Add(change);
        Stage = to;
        UpdatedAt = at;
        return change;
    }
}
