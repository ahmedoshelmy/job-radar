namespace JobRadar.Contracts;

/// <summary>
/// A posting as one feed returned it, already normalised to the common shape. The same opening can be
/// discovered many times (every poll, every board that mirrors it); the Catalog decides what is new.
/// </summary>
public sealed record JobPostingDiscovered
{
    public required string Source { get; init; }
    public required string ExternalId { get; init; }
    public required string Url { get; init; }
    public required string Title { get; init; }
    public required string Company { get; init; }
    public string Location { get; init; } = "";
    public WorkMode WorkMode { get; init; }
    public IReadOnlyList<string> Countries { get; init; } = [];
    public Seniority Seniority { get; init; }
    public string? SalaryText { get; init; }
    public int? SalaryMinUsd { get; init; }
    public int? SalaryMaxUsd { get; init; }
    public DateTimeOffset? PostedAt { get; init; }
    public string Description { get; init; } = "";
    public IReadOnlyList<string> Tags { get; init; } = [];
    public DateTimeOffset DiscoveredAt { get; init; }
}
