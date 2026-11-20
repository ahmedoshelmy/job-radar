using JobRadar.Contracts;
using JobRadar.Tracker.Domain;
using MassTransit;

namespace JobRadar.Tracker.Application;

public sealed record PostingRef(Guid Id, string Title, string Company, string Url);

/// <summary>How the Tracker learns about a posting. The API wires it to the Catalog's read side; Tracker never sees Catalog tables.</summary>
public interface IPostingLookup
{
    Task<PostingRef?> FindAsync(Guid postingId, CancellationToken cancellationToken);
}

public interface IApplicationRepository
{
    Task<JobApplication?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsForPostingAsync(Guid postingId, CancellationToken cancellationToken);

    Task<IReadOnlyList<JobApplication>> ListAsync(CancellationToken cancellationToken);

    void Add(JobApplication application);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed class PostingNotFoundException(Guid postingId) : Exception($"Posting {postingId} does not exist.");

public sealed class AlreadyTrackedException(Guid postingId) : Exception($"Posting {postingId} is already in the pipeline.");

public sealed class TrackerService(
    IApplicationRepository applications,
    IPostingLookup postings,
    InterviewBankService interviewBank,
    IPublishEndpoint publisher,
    TimeProvider clock)
{
    public async Task<JobApplication> StartAsync(Guid postingId, string? note, CancellationToken cancellationToken)
    {
        var posting = await postings.FindAsync(postingId, cancellationToken) ?? throw new PostingNotFoundException(postingId);
        if (await applications.ExistsForPostingAsync(postingId, cancellationToken))
        {
            throw new AlreadyTrackedException(postingId);
        }

        var application = JobApplication.Start(NewId.NextSequentialGuid(), posting.Id, posting.Title, posting.Company, posting.Url, clock.GetUtcNow(), note);
        applications.Add(application);
        await applications.SaveChangesAsync(cancellationToken);
        await Publish(application, application.History[0], cancellationToken);
        return application;
    }

    /// <returns>null when the application does not exist.</returns>
    public async Task<JobApplication?> MoveAsync(Guid id, ApplicationStage to, string? note, CancellationToken cancellationToken)
    {
        var application = await applications.GetAsync(id, cancellationToken);
        if (application is null)
        {
            return null;
        }

        var change = application.MoveTo(to, clock.GetUtcNow(), note);
        if (to == ApplicationStage.Interview)
        {
            await interviewBank.SeedStartersAsync(application, cancellationToken); // same unit of work: saved together below
        }

        await applications.SaveChangesAsync(cancellationToken);
        await Publish(application, change, cancellationToken);
        return application;
    }

    public Task<IReadOnlyList<JobApplication>> ListAsync(CancellationToken cancellationToken) => applications.ListAsync(cancellationToken);

    /// <summary>The stored history is the source of truth; the event tells other contexts (stats, future notifications).</summary>
    private Task Publish(JobApplication application, StageEvent change, CancellationToken cancellationToken) =>
        publisher.Publish(new ApplicationStageChanged
        {
            ApplicationId = application.Id,
            PostingId = application.PostingId,
            From = change.From?.ToString(),
            To = change.To.ToString(),
            Note = change.Note,
            At = change.At,
        }, cancellationToken);
}
