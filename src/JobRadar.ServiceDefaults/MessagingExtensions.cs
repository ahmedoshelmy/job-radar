using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace JobRadar.ServiceDefaults;

public enum MessageTransport
{
    RabbitMq,
    AzureServiceBus,
}

public static class MessagingExtensions
{
    /// <summary>
    /// One switch for the broker: <c>Messaging:Transport</c> = <c>RabbitMq</c> (local, compose, CI) or <c>AzureServiceBus</c>
    /// (Container Apps). Consumers and endpoints are identical on both; only the host line changes.
    /// </summary>
    public static IHostApplicationBuilder AddMessaging(
        this IHostApplicationBuilder builder,
        Action<IBusRegistrationConfigurator> registerConsumers,
        Action<IBusRegistrationContext, IBusFactoryConfigurator>? configureBus = null)
    {
        var transport = builder.Configuration.GetValue("Messaging:Transport", MessageTransport.RabbitMq);

        builder.Services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();
            registerConsumers(x);

            if (transport == MessageTransport.AzureServiceBus)
            {
                var connection = Required(builder.Configuration, "servicebus");
                x.UsingAzureServiceBus((ctx, cfg) =>
                {
                    cfg.Host(connection);
                    configureBus?.Invoke(ctx, cfg);
                    cfg.ConfigureEndpoints(ctx);
                });
            }
            else
            {
                var connection = Required(builder.Configuration, "rabbitmq");
                x.UsingRabbitMq((ctx, cfg) =>
                {
                    cfg.Host(new Uri(connection));
                    configureBus?.Invoke(ctx, cfg);
                    cfg.ConfigureEndpoints(ctx);
                });
            }
        });

        return builder;
    }

    private static string Required(IConfiguration configuration, string name) =>
        configuration.GetConnectionString(name)
        ?? throw new InvalidOperationException($"Connection string '{name}' is missing (ConnectionStrings__{name}).");
}
