using JobRadar.Catalog.Domain;
using JobRadar.Contracts;

namespace JobRadar.Catalog.Application;

public interface IPostingRepository
{
    /// <summary>The posting with this fingerprint, or else one whose canonical URL matches.</summary>
    Task<JobPosting?> FindDuplicateAsync(string fingerprint, string canonicalUrl, CancellationToken cancellationToken);

    void Add(JobPosting posting);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record PostingSearch(string? Query, WorkMode? WorkMode, int Take = 300);

public sealed record PostingView(
    Guid Id,
    string Title,
    string Company,
    string Location,
    WorkMode WorkMode,
    Seniority Seniority,
    string Url,
    string Source,
    IReadOnlyList<string> AlsoSeenIn,
    string? SalaryText,
    DateTimeOffset? PostedAt,
    DateTimeOffset FirstSeenAt);

/// <summary>Read side of the Catalog, used by the API composition and by the Tracker (in-process, no shared tables).</summary>
public interface ICatalogQueries
{
    Task<IReadOnlyList<PostingView>> SearchAsync(PostingSearch search, CancellationToken cancellationToken);

    Task<PostingView?> GetAsync(Guid id, CancellationToken cancellationToken);
}
