using JobRadar.Contracts;
using JobRadar.Matching.Domain;
using MassTransit;

namespace JobRadar.Matching.Application;

/// <summary>Scores every new posting against the profile and publishes <see cref="JobMatched"/> for the good ones.</summary>
public sealed class JobPostingAddedConsumer(IMatchingStore store, TimeProvider clock) : IConsumer<JobPostingAdded>
{
    public async Task Consume(ConsumeContext<JobPostingAdded> context)
    {
        var added = context.Message;
        var ct = context.CancellationToken;
        if (await store.FindAsync(added.PostingId, ct) is not null)
        {
            return; // redelivery: already scored
        }

        var profile = await store.GetProfileAsync(ct);
        var now = clock.GetUtcNow();
        var scored = new ScoredPosting
        {
            PostingId = added.PostingId,
            Url = added.Url,
            AddedAt = added.AddedAt,
            Snapshot = new PostingSnapshot(
                added.Title, added.Company, added.Location, added.WorkMode, added.Countries, added.Seniority,
                added.SalaryMinUsd, added.SalaryMaxUsd, added.Description, added.Tags),
        };
        scored.Apply(MatchScorer.Score(scored.Snapshot, profile), now);
        var announce = scored.TryAnnounce(profile, now);
        store.Add(scored);
        await store.SaveChangesAsync(ct);

        if (announce)
        {
            await context.Publish(scored.ToMatched(now), ct);
        }
    }
}

/// <summary>
/// Fans <see cref="JobMatched"/> out to live clients. Its queue is per API instance (temporary), so with several replicas
/// every instance hears every match and pushes it to its own SignalR connections.
/// </summary>
public sealed class JobMatchedConsumer(IMatchNotifier notifier) : IConsumer<JobMatched>
{
    public Task Consume(ConsumeContext<JobMatched> context) => notifier.NotifyAsync(context.Message, context.CancellationToken);
}

public sealed class ProfileService(IMatchingStore store, IPublishEndpoint publisher, TimeProvider clock)
{
    public Task<Profile> GetAsync(CancellationToken cancellationToken) => store.GetProfileAsync(cancellationToken);

    /// <summary>Saves the profile and rescores every known posting locally; newly qualifying postings are announced.</summary>
    public async Task<(Profile Profile, int Rescored, int Announced)> UpdateAsync(ProfileDraft draft, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var profile = await store.GetProfileAsync(cancellationToken);
        profile.Update(draft, now);

        var postings = await store.AllAsync(cancellationToken);
        var announced = new List<JobMatched>();
        foreach (var posting in postings)
        {
            posting.Apply(MatchScorer.Score(posting.Snapshot, profile), now);
            if (posting.TryAnnounce(profile, now))
            {
                announced.Add(posting.ToMatched(now));
            }
        }

        await store.SaveChangesAsync(cancellationToken);
        await publisher.PublishBatch(announced, cancellationToken);
        return (profile, postings.Count, announced.Count);
    }
}

internal static class ScoredPostingMapping
{
    public static JobMatched ToMatched(this ScoredPosting p, DateTimeOffset at) => new()
    {
        PostingId = p.PostingId,
        Score = p.Score,
        Reasons = p.Reasons.Where(r => r.Points != 0).Select(r => $"{(r.Points > 0 ? "+" : "")}{r.Points} {r.Text}").ToList(),
        Title = p.Snapshot.Title,
        Company = p.Snapshot.Company,
        Url = p.Url,
        MatchedAt = at,
    };
}
