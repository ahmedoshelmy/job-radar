using JobRadar.Catalog.Application;
using JobRadar.Catalog.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobRadar.Catalog;

public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<CatalogDbContext>(o => o.UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__ef_migrations", CatalogDbContext.Schema)));
        services.AddScoped<IPostingRepository, PostingRepository>();
        services.AddScoped<ICatalogQueries, CatalogQueries>();
        return services;
    }

    public static void AddCatalogConsumers(this IBusRegistrationConfigurator bus)
    {
        bus.AddEntityFrameworkOutbox<CatalogDbContext>(o =>
        {
            o.UsePostgres();
            o.QueryDelay = TimeSpan.FromSeconds(1);
        });
        bus.AddConsumer<JobPostingDiscoveredConsumer, JobPostingDiscoveredConsumerDefinition>();
    }
}

/// <summary>
/// Queue <c>catalog-job-posting-discovered</c>: retries first, then the EF inbox/outbox. One message at a time keeps
/// the fingerprint check-then-insert race-free without distributed locks (the unique index is the backstop).
/// </summary>
public sealed class JobPostingDiscoveredConsumerDefinition : ConsumerDefinition<JobPostingDiscoveredConsumer>
{
    public JobPostingDiscoveredConsumerDefinition()
    {
        EndpointName = "catalog-job-posting-discovered";
        ConcurrentMessageLimit = 1;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<JobPostingDiscoveredConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r => r.Intervals(200, 1000, 5000));
        endpointConfigurator.UseEntityFrameworkOutbox<CatalogDbContext>(context);
    }
}
