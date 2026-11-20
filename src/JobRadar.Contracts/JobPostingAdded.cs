namespace JobRadar.Contracts;

/// <summary>A posting the Catalog had not seen before (by fingerprint). Published once per opening.</summary>
public sealed record JobPostingAdded
{
    public required Guid PostingId { get; init; }
    public required string Fingerprint { get; init; }
    public required string Source { get; init; }
    public required string Url { get; init; }
    public required string Title { get; init; }
    public required string Company { get; init; }
    public string Location { get; init; } = "";
    public WorkMode WorkMode { get; init; }
    public IReadOnlyList<string> Countries { get; init; } = [];
    public Seniority Seniority { get; init; }
    public int? SalaryMinUsd { get; init; }
    public int? SalaryMaxUsd { get; init; }
    public string Description { get; init; } = "";
    public IReadOnlyList<string> Tags { get; init; } = [];
    public DateTimeOffset AddedAt { get; init; }
}
