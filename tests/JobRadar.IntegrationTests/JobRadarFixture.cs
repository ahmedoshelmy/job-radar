using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace JobRadar.IntegrationTests;

/// <summary>Real Postgres and RabbitMQ in containers, the real API in memory. Started once for the test class.</summary>
public sealed class JobRadarFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-management-alpine").Build();

    public WebApplicationFactory<Program> Api { get; private set; } = null!;

    public string RabbitConnection => _rabbit.GetConnectionString();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());
        Api = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseEnvironment("Testing");
            web.UseSetting("ConnectionStrings:jobradar", _postgres.GetConnectionString());
            web.UseSetting("ConnectionStrings:rabbitmq", RabbitConnection);
            web.ConfigureServices(s => s.Configure<MassTransitHostOptions>(o => o.WaitUntilStarted = true));
        });
        _ = Api.Server; // boots the host: migrations, seed, bus
    }

    public async Task DisposeAsync()
    {
        await Api.DisposeAsync();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _rabbit.DisposeAsync().AsTask());
    }
}
