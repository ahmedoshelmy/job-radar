using System.Text.Json.Serialization;
using JobRadar.Api;
using JobRadar.Api.Endpoints;
using JobRadar.Api.Stats;
using JobRadar.Catalog;
using JobRadar.Catalog.Infrastructure;
using JobRadar.Matching;
using JobRadar.Matching.Infrastructure;
using JobRadar.ServiceDefaults;
using JobRadar.Tracker.Application;
using JobRadar.Tracker.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("jobradar")
    ?? throw new InvalidOperationException("Connection string 'jobradar' is missing (ConnectionStrings__jobradar).");

builder.AddServiceDefaults();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddOpenApi();

// Three bounded contexts, one deployable, one database, three schemas (plus the stats projection).
builder.Services.AddCatalogModule(connectionString);
builder.Services.AddMatchingModule(connectionString);
builder.Services.AddTrackerModule(connectionString);
builder.Services.AddScoped<IPostingLookup, CatalogPostingLookup>();
builder.Services.AddDbContext<StatsDbContext>(o => o.UseNpgsql(connectionString, n => n.MigrationsHistoryTable("__ef_migrations", StatsDbContext.Schema)));
builder.Services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHealthChecks().AddDbContextCheck<CatalogDbContext>("postgres");

builder.AddMessaging(bus =>
{
    bus.AddCatalogConsumers();
    bus.AddMatchingConsumers();
    bus.AddConsumer<StatsProjectionConsumer, StatsProjectionConsumerDefinition>();
});

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    await app.MigrateAndSeedAsync();
}

app.MapOpenApi();
app.MapScalarApiReference();
app.MapDefaultEndpoints();
app.MapPostingEndpoints();
app.MapProfileEndpoints();
app.MapTrackerEndpoints();
app.MapStatsEndpoints();
app.MapHub<MatchesHub>(MatchesHub.Path);

await app.RunAsync();

/// <summary>Entry point; public so integration tests can host it with WebApplicationFactory.</summary>
public partial class Program;
