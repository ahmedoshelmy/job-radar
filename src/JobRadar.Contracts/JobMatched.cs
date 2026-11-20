namespace JobRadar.Contracts;

/// <summary>A new posting scored at or above the profile's threshold.</summary>
public sealed record JobMatched
{
    public required Guid PostingId { get; init; }
    public required int Score { get; init; }
    public required IReadOnlyList<string> Reasons { get; init; }
    public required string Title { get; init; }
    public required string Company { get; init; }
    public required string Url { get; init; }
    public DateTimeOffset MatchedAt { get; init; }
}
