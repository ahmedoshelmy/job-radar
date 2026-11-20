namespace JobRadar.Matching.Domain;

/// <summary>Matching's own copy of a posting plus its current score, so a profile change can rescore everything locally.</summary>
public sealed class ScoredPosting
{
    public Guid PostingId { get; init; }
    public required PostingSnapshot Snapshot { get; init; }
    public required string Url { get; init; }
    public int Score { get; private set; }
    public List<MatchReason> Reasons { get; private set; } = [];
    public DateTimeOffset AddedAt { get; init; }
    public DateTimeOffset ScoredAt { get; private set; }

    /// <summary>Set once the user has been told about it; a rescore never re-announces a posting.</summary>
    public DateTimeOffset? AnnouncedAt { get; private set; }

    public void Apply(MatchResult result, DateTimeOffset at)
    {
        Score = result.Score;
        Reasons = [.. result.Reasons];
        ScoredAt = at;
    }

    /// <summary>True the first time the score reaches the profile's threshold.</summary>
    public bool TryAnnounce(Profile profile, DateTimeOffset at)
    {
        if (AnnouncedAt is not null || Score < profile.NotifyThreshold)
        {
            return false;
        }

        AnnouncedAt = at;
        return true;
    }
}
