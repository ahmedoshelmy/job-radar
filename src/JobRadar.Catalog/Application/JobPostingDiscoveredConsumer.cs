using JobRadar.Catalog.Domain;
using JobRadar.Contracts;
using MassTransit;

namespace JobRadar.Catalog.Application;

/// <summary>
/// Dedupe at the door: a discovery either credits another feed on an existing posting or creates the posting and
/// publishes <see cref="JobPostingAdded"/>. The publish goes through the transactional outbox, so the row and the
/// event commit together, and the inbox makes a redelivered message a no-op.
/// </summary>
public sealed class JobPostingDiscoveredConsumer(IPostingRepository postings) : IConsumer<JobPostingDiscovered>
{
    public async Task Consume(ConsumeContext<JobPostingDiscovered> context)
    {
        var found = context.Message;
        var fingerprint = Fingerprint.For(found.Company, found.Title, found.WorkMode, found.Location);
        var canonicalUrl = Fingerprint.CanonicalUrl(found.Url);

        var existing = await postings.FindDuplicateAsync(fingerprint, canonicalUrl, context.CancellationToken);
        if (existing is not null)
        {
            existing.RecordSighting(found.Source, found.DiscoveredAt);
            await postings.SaveChangesAsync(context.CancellationToken);
            return;
        }

        var posting = ToPosting(found, fingerprint, canonicalUrl);
        postings.Add(posting);
        await context.Publish(ToAdded(posting), context.CancellationToken);
        await postings.SaveChangesAsync(context.CancellationToken);
    }

    private static JobPosting ToPosting(JobPostingDiscovered d, string fingerprint, string canonicalUrl) => new()
    {
        Id = NewId.NextSequentialGuid(),
        Fingerprint = fingerprint,
        CanonicalUrl = canonicalUrl,
        Source = d.Source,
        ExternalId = d.ExternalId,
        Url = d.Url,
        Title = d.Title,
        Company = d.Company,
        Location = d.Location,
        WorkMode = d.WorkMode,
        Countries = [.. d.Countries],
        Seniority = d.Seniority,
        SalaryText = d.SalaryText,
        SalaryMinUsd = d.SalaryMinUsd,
        SalaryMaxUsd = d.SalaryMaxUsd,
        Description = d.Description,
        Tags = [.. d.Tags],
        PostedAt = d.PostedAt,
        FirstSeenAt = d.DiscoveredAt,
    };

    private static JobPostingAdded ToAdded(JobPosting p) => new()
    {
        PostingId = p.Id,
        Fingerprint = p.Fingerprint,
        Source = p.Source,
        Url = p.Url,
        Title = p.Title,
        Company = p.Company,
        Location = p.Location,
        WorkMode = p.WorkMode,
        Countries = p.Countries,
        Seniority = p.Seniority,
        SalaryMinUsd = p.SalaryMinUsd,
        SalaryMaxUsd = p.SalaryMaxUsd,
        Description = p.Description,
        Tags = p.Tags,
        AddedAt = p.FirstSeenAt,
    };
}
