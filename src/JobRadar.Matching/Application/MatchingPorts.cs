using JobRadar.Contracts;
using JobRadar.Matching.Domain;

namespace JobRadar.Matching.Application;

public interface IMatchingStore
{
    Task<Profile> GetProfileAsync(CancellationToken cancellationToken);

    Task<ScoredPosting?> FindAsync(Guid postingId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ScoredPosting>> AllAsync(CancellationToken cancellationToken);

    void Add(ScoredPosting posting);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Pushes a match to whoever is watching (the SignalR hub in production, a fake in tests).</summary>
public interface IMatchNotifier
{
    Task NotifyAsync(JobMatched match, CancellationToken cancellationToken);
}

public sealed record ScoreView(int Score, IReadOnlyList<MatchReason> Reasons);

public sealed record MatchView(Guid PostingId, string Title, string Company, string Url, int Score, IReadOnlyList<MatchReason> Reasons, DateTimeOffset AddedAt);

public interface IMatchQueries
{
    Task<IReadOnlyDictionary<Guid, ScoreView>> ScoresForAsync(IReadOnlyCollection<Guid> postingIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<MatchView>> RecentMatchesAsync(int take, CancellationToken cancellationToken);
}
