using JobRadar.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobRadar.Api.Stats;

/// <summary>
/// A second, independent subscriber to <see cref="JobPostingAdded"/> (its own queue, its own schema): the fan-out half
/// of pub/sub. Matching can be down and the stats still count, and vice versa.
/// </summary>
public sealed class StatsDbContext(DbContextOptions<StatsDbContext> options) : DbContext(options)
{
    public const string Schema = "stats";

    public DbSet<CountedPosting> Postings => Set<CountedPosting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<CountedPosting>(e =>
        {
            e.ToTable("counted_postings");
            e.HasKey(p => p.PostingId);
            e.Property(p => p.Source).HasMaxLength(40);
            e.Property(p => p.WorkMode).HasConversion<string>().HasMaxLength(10);
        });
    }
}

public sealed class CountedPosting
{
    public Guid PostingId { get; init; }
    public required string Source { get; init; }
    public WorkMode WorkMode { get; init; }
    public DateTimeOffset AddedAt { get; init; }
}

public sealed class StatsDbContextFactory : IDesignTimeDbContextFactory<StatsDbContext>
{
    public StatsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<StatsDbContext>()
            .UseNpgsql("Host=localhost;Database=design_time", o => o.MigrationsHistoryTable("__ef_migrations", StatsDbContext.Schema))
            .Options);
}

/// <summary>Idempotent by posting id: a redelivered event counts once.</summary>
public sealed class StatsProjectionConsumer(StatsDbContext db) : IConsumer<JobPostingAdded>
{
    public async Task Consume(ConsumeContext<JobPostingAdded> context)
    {
        var e = context.Message;
        if (await db.Postings.AnyAsync(p => p.PostingId == e.PostingId, context.CancellationToken))
        {
            return;
        }

        db.Postings.Add(new CountedPosting { PostingId = e.PostingId, Source = e.Source, WorkMode = e.WorkMode, AddedAt = e.AddedAt });
        await db.SaveChangesAsync(context.CancellationToken);
    }
}

public sealed class StatsProjectionConsumerDefinition : ConsumerDefinition<StatsProjectionConsumer>
{
    public StatsProjectionConsumerDefinition() => EndpointName = "stats-job-posting-added";
}

public sealed record SourceStat(string Source, int Postings, DateTimeOffset LastAddedAt);

public sealed record StatsView(int TotalPostings, int RemotePostings, IReadOnlyList<SourceStat> BySource);

public static class StatsEndpoints
{
    public static IEndpointRouteBuilder MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/stats", async (StatsDbContext db, CancellationToken ct) =>
        {
            var bySource = await db.Postings.GroupBy(p => p.Source)
                .Select(g => new SourceStat(g.Key, g.Count(), g.Max(p => p.AddedAt)))
                .ToListAsync(ct);
            var remote = await db.Postings.CountAsync(p => p.WorkMode == WorkMode.Remote, ct);
            return new StatsView(bySource.Sum(s => s.Postings), remote, [.. bySource.OrderByDescending(s => s.Postings)]);
        }).WithTags("Stats");
        return app;
    }
}
