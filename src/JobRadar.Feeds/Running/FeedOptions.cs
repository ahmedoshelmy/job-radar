using JobRadar.Feeds.Sources;

namespace JobRadar.Feeds.Running;

public enum FeedMode
{
    /// <summary>Recorded JSON under <see cref="FeedOptions.FixturesPath"/>; the default, so nothing calls the internet by accident.</summary>
    Fixtures,

    /// <summary>The real public APIs, politely (see ADR 0003).</summary>
    Live,
}

public sealed class FeedOptions
{
    public const string Section = "Feeds";

    public FeedMode Mode { get; set; } = FeedMode.Fixtures;
    public string FixturesPath { get; set; } = "fixtures";
    public string StatePath { get; set; } = "state/feeds.json";

    /// <summary>How often the scheduler wakes to see which feeds are due. Each feed keeps its own interval.</summary>
    public int TickSeconds { get; set; } = 60;

    public bool SchedulerEnabled { get; set; } = true;

    /// <summary>Identifies the client to feed owners. No personal contact details.</summary>
    public string UserAgent { get; set; } = "JobRadar/0.1 (self-hosted job tracker)";

    public List<FeedSourceOptions> Sources { get; set; } = [];
}

public sealed class FeedSourceOptions
{
    public string Kind { get; set; } = "";
    public string? Board { get; set; }
    public string Label { get; set; } = "";

    /// <summary>Company boards: once a day. Aggregators: as their terms allow (Remotive: a few calls a day).</summary>
    public double IntervalHours { get; set; } = 24;

    public BoardTarget ToTarget() => new(Kind.ToLowerInvariant(), Board, Label.Length > 0 ? Label : Board ?? Kind, TimeSpan.FromHours(IntervalHours));
}
