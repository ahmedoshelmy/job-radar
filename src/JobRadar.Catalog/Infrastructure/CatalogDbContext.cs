using JobRadar.Catalog.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobRadar.Catalog.Infrastructure;

/// <summary>The Catalog owns the <c>catalog</c> schema, including its outbox and inbox tables. No other module touches it.</summary>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public const string Schema = "catalog";

    public DbSet<JobPosting> Postings => Set<JobPosting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<JobPosting>(e =>
        {
            e.ToTable("postings");
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.Fingerprint).IsUnique();
            e.HasIndex(p => p.CanonicalUrl);
            e.HasIndex(p => p.FirstSeenAt);
            e.Property(p => p.Fingerprint).HasMaxLength(300);
            e.Property(p => p.CanonicalUrl).HasMaxLength(1000);
            e.Property(p => p.Url).HasMaxLength(1000);
            e.Property(p => p.Source).HasMaxLength(40);
            e.Property(p => p.ExternalId).HasMaxLength(400);
            e.Property(p => p.Title).HasMaxLength(300);
            e.Property(p => p.Company).HasMaxLength(120);
            e.Property(p => p.Location).HasMaxLength(400);
            e.Property(p => p.WorkMode).HasConversion<string>().HasMaxLength(10);
            e.Property(p => p.Seniority).HasConversion<string>().HasMaxLength(10);
        });

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}

/// <summary>For <c>dotnet ef migrations add</c> only; never used at runtime.</summary>
public sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql("Host=localhost;Database=design_time", o => o.MigrationsHistoryTable("__ef_migrations", CatalogDbContext.Schema))
            .Options);
}
