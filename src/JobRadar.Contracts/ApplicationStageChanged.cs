namespace JobRadar.Contracts;

/// <summary>An application moved through the pipeline. <see cref="From"/> is null when it was created.</summary>
public sealed record ApplicationStageChanged
{
    public required Guid ApplicationId { get; init; }
    public required Guid PostingId { get; init; }
    public string? From { get; init; }
    public required string To { get; init; }
    public string? Note { get; init; }
    public DateTimeOffset At { get; init; }
}
