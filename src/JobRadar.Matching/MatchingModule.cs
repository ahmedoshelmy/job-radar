using FluentValidation;
using JobRadar.Matching.Application;
using JobRadar.Matching.Domain;
using JobRadar.Matching.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobRadar.Matching;

public static class MatchingModule
{
    public static IServiceCollection AddMatchingModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<MatchingDbContext>(o => o.UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__ef_migrations", MatchingDbContext.Schema)));
        services.AddScoped<IMatchingStore, MatchingStore>();
        services.AddScoped<IMatchQueries, MatchQueries>();
        services.AddScoped<ProfileService>();
        services.AddSingleton<IValidator<ProfileDraft>, ProfileDraftValidator>();
        services.AddSingleton<IMatchNotifier, SignalRMatchNotifier>();
        services.AddSignalR();
        return services;
    }

    public static void AddMatchingConsumers(this IBusRegistrationConfigurator bus)
    {
        bus.AddConsumer<JobPostingAddedConsumer, JobPostingAddedConsumerDefinition>();
        bus.AddConsumer<JobMatchedConsumer, JobMatchedConsumerDefinition>();
    }
}

public sealed class JobPostingAddedConsumerDefinition : ConsumerDefinition<JobPostingAddedConsumer>
{
    public JobPostingAddedConsumerDefinition()
    {
        EndpointName = "matching-job-posting-added";
        ConcurrentMessageLimit = 1;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<JobPostingAddedConsumer> consumerConfigurator,
        IRegistrationContext context) =>
        endpointConfigurator.UseMessageRetry(r => r.Intervals(200, 1000, 5000));
}

/// <summary>A temporary, per-instance queue: every API replica receives every match for its own SignalR clients.</summary>
public sealed class JobMatchedConsumerDefinition : ConsumerDefinition<JobMatchedConsumer>
{
    public JobMatchedConsumerDefinition() =>
        Endpoint(e =>
        {
            e.Name = "matching-live";
            e.InstanceId = Guid.NewGuid().ToString("N")[..8];
            e.Temporary = true;
        });
}
