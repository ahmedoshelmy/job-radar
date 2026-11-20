using JobRadar.Contracts;

namespace JobRadar.Catalog.Domain;

/// <summary>A job opening, once, however many feeds list it. The first feed to report it is its <see cref="Source"/>.</summary>
public sealed class JobPosting
{
    private DateTimeOffset _firstSeenAt;

    public Guid Id { get; init; }
    public required string Fingerprint { get; init; }
    public required string CanonicalUrl { get; init; }
    public required string Source { get; init; }
    public required string ExternalId { get; init; }
    public required string Url { get; init; }
    public required string Title { get; init; }
    public required string Company { get; init; }
    public string Location { get; init; } = "";
    public WorkMode WorkMode { get; init; }
    public List<string> Countries { get; init; } = [];
    public Seniority Seniority { get; init; }
    public string? SalaryText { get; init; }
    public int? SalaryMinUsd { get; init; }
    public int? SalaryMaxUsd { get; init; }
    public string Description { get; init; } = "";
    public List<string> Tags { get; init; } = [];
    public DateTimeOffset? PostedAt { get; init; }

    /// <summary>Other feeds that listed the same opening. Credit for every source, one row for the user.</summary>
    public List<string> AlsoSeenIn { get; private set; } = [];

    public DateTimeOffset FirstSeenAt
    {
        get => _firstSeenAt;
        init
        {
            _firstSeenAt = value;
            LastSeenAt = value;
        }
    }

    public DateTimeOffset LastSeenAt { get; private set; }

    /// <summary>Another sighting of this opening. Returns true when it came from a feed not credited yet.</summary>
    public bool RecordSighting(string source, DateTimeOffset at)
    {
        if (at > LastSeenAt)
        {
            LastSeenAt = at;
        }

        if (string.Equals(source, Source, StringComparison.OrdinalIgnoreCase) || AlsoSeenIn.Contains(source, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        AlsoSeenIn = [.. AlsoSeenIn, source];
        return true;
    }
}
