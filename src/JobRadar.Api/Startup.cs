using JobRadar.Api.Stats;
using JobRadar.Catalog.Infrastructure;
using JobRadar.Matching.Application;
using JobRadar.Matching.Domain;
using JobRadar.Matching.Infrastructure;
using JobRadar.Tracker.Application;
using JobRadar.Tracker.Domain;
using JobRadar.Tracker.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobRadar.Api;

internal static class Startup
{
    /// <summary>Each module migrates its own schema with its own history table; then the demo profile is seeded once.</summary>
    public static async Task MigrateAndSeedAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await services.GetRequiredService<CatalogDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<MatchingDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<TrackerDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<StatsDbContext>().Database.MigrateAsync();

        var store = services.GetRequiredService<IMatchingStore>();
        await store.GetProfileAsync(CancellationToken.None);
        await store.SaveChangesAsync(CancellationToken.None);
    }
}

/// <summary>Domain and application refusals become RFC 7807 problem details with the right status code.</summary>
internal sealed class DomainExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            IllegalStageTransitionException => (StatusCodes.Status409Conflict, "Illegal stage transition"),
            AlreadyTrackedException => (StatusCodes.Status409Conflict, "Already tracked"),
            PostingNotFoundException => (StatusCodes.Status404NotFound, "Posting not found"),
            DomainRuleException => (StatusCodes.Status400BadRequest, "Profile rule broken"),
            _ => (0, ""),
        };
        if (status == 0)
        {
            return false;
        }

        httpContext.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = exception.Message },
        });
    }
}
