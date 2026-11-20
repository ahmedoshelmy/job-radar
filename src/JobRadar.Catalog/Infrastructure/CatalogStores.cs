using JobRadar.Catalog.Application;
using JobRadar.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobRadar.Catalog.Infrastructure;

internal sealed class PostingRepository(CatalogDbContext db) : IPostingRepository
{
    public async Task<JobPosting?> FindDuplicateAsync(string fingerprint, string canonicalUrl, CancellationToken cancellationToken) =>
        await db.Postings.FirstOrDefaultAsync(p => p.Fingerprint == fingerprint, cancellationToken)
        ?? await db.Postings.FirstOrDefaultAsync(p => p.CanonicalUrl == canonicalUrl, cancellationToken);

    public void Add(JobPosting posting) => db.Postings.Add(posting);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

internal sealed class CatalogQueries(CatalogDbContext db) : ICatalogQueries
{
    public async Task<IReadOnlyList<PostingView>> SearchAsync(PostingSearch search, CancellationToken cancellationToken)
    {
        var query = db.Postings.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search.Query))
        {
            var pattern = $"%{search.Query.Trim().Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)}%";
            query = query.Where(p => EF.Functions.ILike(p.Title, pattern) || EF.Functions.ILike(p.Company, pattern) || EF.Functions.ILike(p.Description, pattern));
        }

        if (search.WorkMode is { } mode)
        {
            query = query.Where(p => p.WorkMode == mode);
        }

        var rows = await query.OrderByDescending(p => p.FirstSeenAt).Take(Math.Clamp(search.Take, 1, 1000)).ToListAsync(cancellationToken);
        return rows.Select(ToView).ToList();
    }

    public async Task<PostingView?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await db.Postings.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        return row is null ? null : ToView(row);
    }

    private static PostingView ToView(JobPosting p) => new(
        p.Id, p.Title, p.Company, p.Location, p.WorkMode, p.Seniority, p.Url, p.Source, p.AlsoSeenIn, p.SalaryText, p.PostedAt, p.FirstSeenAt);
}
