using System.Text.Json;
using JobRadar.Matching.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobRadar.Matching.Infrastructure;

/// <summary>The <c>matching</c> schema: the profile and scored copies of postings.</summary>
public sealed class MatchingDbContext(DbContextOptions<MatchingDbContext> options) : DbContext(options)
{
    public const string Schema = "matching";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<ScoredPosting> ScoredPostings => Set<ScoredPosting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Profile>(e =>
        {
            e.ToTable("profiles");
            e.HasKey(p => p.Id);
            e.Property(p => p.Headline).HasMaxLength(200);
            e.PrimitiveCollection(p => p.Seniorities).ElementType().HasConversion<string>();
        });

        modelBuilder.Entity<ScoredPosting>(e =>
        {
            e.ToTable("scored_postings");
            e.HasKey(p => p.PostingId);
            e.HasIndex(p => p.Score);
            e.Property(p => p.Url).HasMaxLength(1000);
            e.Property(p => p.Snapshot).HasColumnType("jsonb").HasConversion(
                v => JsonSerializer.Serialize(v, Json),
                v => JsonSerializer.Deserialize<PostingSnapshot>(v, Json)!);
            e.Property(p => p.Reasons).HasColumnType("jsonb").HasConversion(
                v => JsonSerializer.Serialize(v, Json),
                v => JsonSerializer.Deserialize<List<MatchReason>>(v, Json) ?? new List<MatchReason>());
        });
    }
}

/// <summary>For <c>dotnet ef migrations add</c> only.</summary>
public sealed class MatchingDbContextFactory : IDesignTimeDbContextFactory<MatchingDbContext>
{
    public MatchingDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<MatchingDbContext>()
            .UseNpgsql("Host=localhost;Database=design_time", o => o.MigrationsHistoryTable("__ef_migrations", MatchingDbContext.Schema))
            .Options);
}
