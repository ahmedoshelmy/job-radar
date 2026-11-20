using FluentValidation;
using JobRadar.Tracker.Application;
using JobRadar.Tracker.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace JobRadar.Tracker.Infrastructure;

/// <summary>The <c>tracker</c> schema: applications and their stage history.</summary>
public sealed class TrackerDbContext(DbContextOptions<TrackerDbContext> options) : DbContext(options)
{
    public const string Schema = "tracker";

    public DbSet<JobApplication> Applications => Set<JobApplication>();
    public DbSet<InterviewQuestion> Questions => Set<InterviewQuestion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<JobApplication>(e =>
        {
            e.ToTable("applications");
            e.HasKey(a => a.Id);
            e.HasIndex(a => a.PostingId).IsUnique();
            e.Property(a => a.Title).HasMaxLength(300);
            e.Property(a => a.Company).HasMaxLength(120);
            e.Property(a => a.Url).HasMaxLength(1000);
            e.Property(a => a.Stage).HasConversion<string>().HasMaxLength(12);
            e.HasMany(a => a.History).WithOne().HasForeignKey("ApplicationId").OnDelete(DeleteBehavior.Cascade);
            e.Navigation(a => a.History).HasField("_history").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<StageEvent>(e =>
        {
            e.ToTable("stage_events");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).ValueGeneratedNever();
            e.Property(s => s.From).HasConversion<string>().HasMaxLength(12);
            e.Property(s => s.To).HasConversion<string>().HasMaxLength(12);
            e.Property(s => s.Note).HasMaxLength(1000);
        });

        modelBuilder.Entity<InterviewQuestion>(e =>
        {
            e.ToTable("interview_questions");
            e.HasKey(q => q.Id);
            e.HasIndex(q => q.ApplicationId);
            e.HasIndex(q => q.DueAt);
            e.Property(q => q.Prompt).HasMaxLength(500);
            e.Property(q => q.Notes).HasMaxLength(4000);
            e.HasOne<JobApplication>().WithMany().HasForeignKey(q => q.ApplicationId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

/// <summary>For <c>dotnet ef migrations add</c> only.</summary>
public sealed class TrackerDbContextFactory : IDesignTimeDbContextFactory<TrackerDbContext>
{
    public TrackerDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<TrackerDbContext>()
            .UseNpgsql("Host=localhost;Database=design_time", o => o.MigrationsHistoryTable("__ef_migrations", TrackerDbContext.Schema))
            .Options);
}

internal sealed class ApplicationRepository(TrackerDbContext db) : IApplicationRepository
{
    public Task<JobApplication?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Applications.Include(a => a.History).FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<bool> ExistsForPostingAsync(Guid postingId, CancellationToken cancellationToken) =>
        db.Applications.AnyAsync(a => a.PostingId == postingId, cancellationToken);

    public async Task<IReadOnlyList<JobApplication>> ListAsync(CancellationToken cancellationToken) =>
        await db.Applications.AsNoTracking().Include(a => a.History).OrderByDescending(a => a.UpdatedAt).ToListAsync(cancellationToken);

    public void Add(JobApplication application) => db.Applications.Add(application);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

internal sealed class QuestionRepository(TrackerDbContext db) : IQuestionRepository
{
    public Task<InterviewQuestion?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Questions.FirstOrDefaultAsync(q => q.Id == id, cancellationToken);

    public async Task<IReadOnlyList<InterviewQuestion>> ForApplicationAsync(Guid applicationId, CancellationToken cancellationToken) =>
        await db.Questions.AsNoTracking().Where(q => q.ApplicationId == applicationId).OrderBy(q => q.CreatedAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<InterviewQuestion>> DueAsync(DateTimeOffset now, int take, CancellationToken cancellationToken) =>
        await db.Questions.AsNoTracking().Where(q => q.DueAt <= now).OrderBy(q => q.DueAt).Take(Math.Clamp(take, 1, 100)).ToListAsync(cancellationToken);

    public Task<bool> AnyForApplicationAsync(Guid applicationId, CancellationToken cancellationToken) =>
        db.Questions.AnyAsync(q => q.ApplicationId == applicationId, cancellationToken);

    public void Add(InterviewQuestion question) => db.Questions.Add(question);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

public static class TrackerModule
{
    /// <summary>The host must also register an <see cref="IPostingLookup"/>.</summary>
    public static IServiceCollection AddTrackerModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<TrackerDbContext>(o => o.UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__ef_migrations", TrackerDbContext.Schema)));
        services.AddScoped<IApplicationRepository, ApplicationRepository>();
        services.AddScoped<TrackerService>();
        services.AddScoped<InterviewBankService>();
        services.AddScoped<IQuestionRepository, QuestionRepository>();
        services.AddSingleton<IValidator<AddQuestionRequest>, AddQuestionRequestValidator>();
        services.AddSingleton<IValidator<ReviewRequest>, ReviewRequestValidator>();
        services.AddSingleton<IValidator<StartApplicationRequest>, StartApplicationRequestValidator>();
        services.AddSingleton<IValidator<MoveStageRequest>, MoveStageRequestValidator>();
        return services;
    }
}
