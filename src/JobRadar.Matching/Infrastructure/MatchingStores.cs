using JobRadar.Contracts;
using JobRadar.Matching.Application;
using JobRadar.Matching.Domain;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace JobRadar.Matching.Infrastructure;

internal sealed class MatchingStore(MatchingDbContext db, TimeProvider clock) : IMatchingStore
{
    public async Task<Profile> GetProfileAsync(CancellationToken cancellationToken)
    {
        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.Id == Profile.DefaultId, cancellationToken);
        if (profile is not null)
        {
            return profile;
        }

        profile = Profile.Demo(clock.GetUtcNow());
        db.Profiles.Add(profile);
        return profile;
    }

    public Task<ScoredPosting?> FindAsync(Guid postingId, CancellationToken cancellationToken) =>
        db.ScoredPostings.FirstOrDefaultAsync(p => p.PostingId == postingId, cancellationToken);

    public async Task<IReadOnlyList<ScoredPosting>> AllAsync(CancellationToken cancellationToken) =>
        await db.ScoredPostings.ToListAsync(cancellationToken);

    public void Add(ScoredPosting posting) => db.ScoredPostings.Add(posting);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

internal sealed class MatchQueries(MatchingDbContext db) : IMatchQueries
{
    public async Task<IReadOnlyDictionary<Guid, ScoreView>> ScoresForAsync(IReadOnlyCollection<Guid> postingIds, CancellationToken cancellationToken)
    {
        var rows = await db.ScoredPostings.AsNoTracking()
            .Where(p => postingIds.Contains(p.PostingId))
            .Select(p => new { p.PostingId, p.Score, p.Reasons })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.PostingId, r => new ScoreView(r.Score, r.Reasons));
    }

    public async Task<IReadOnlyList<MatchView>> RecentMatchesAsync(int take, CancellationToken cancellationToken)
    {
        var rows = await db.ScoredPostings.AsNoTracking()
            .Where(p => p.AnnouncedAt != null)
            .OrderByDescending(p => p.AnnouncedAt)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(cancellationToken);
        return rows.Select(p => new MatchView(p.PostingId, p.Snapshot.Title, p.Snapshot.Company, p.Url, p.Score, p.Reasons, p.AddedAt)).ToList();
    }
}

/// <summary>Server-to-client only: the browser listens for <c>match</c> messages.</summary>
public sealed class MatchesHub : Hub
{
    public const string Path = "/hubs/matches";
    public const string MatchMethod = "match";
}

internal sealed class SignalRMatchNotifier(IHubContext<MatchesHub> hub) : IMatchNotifier
{
    public Task NotifyAsync(JobMatched match, CancellationToken cancellationToken) =>
        hub.Clients.All.SendAsync(MatchesHub.MatchMethod, match, cancellationToken);
}
