using JobRadar.Contracts;

namespace JobRadar.Feeds.Sources;

/// <summary>One configured feed: a board kind plus, for company boards, the company's board token.</summary>
public sealed record BoardTarget(string Kind, string? Board, string Label, TimeSpan Interval)
{
    /// <summary>Stable key for state and fixtures: <c>greenhouse-nilewave</c>, <c>remotive</c>.</summary>
    public string Key => string.IsNullOrWhiteSpace(Board) ? Kind : $"{Kind}-{Board}";
}

/// <summary>
/// A public job feed. Adapters are pure: <see cref="BuildUri"/> says where to read, <see cref="Parse"/> turns the
/// body into the common shape. Fetching, politeness and scheduling live elsewhere, so parsers test offline.
/// </summary>
public interface IJobBoardSource
{
    string Kind { get; }

    /// <summary>Company boards need a board token; aggregator feeds do not.</summary>
    bool NeedsBoard { get; }

    Uri BuildUri(BoardTarget target);

    IReadOnlyList<JobPostingDiscovered> Parse(string body, BoardTarget target, DateTimeOffset now);
}
