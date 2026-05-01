using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Infrastructure.Persistence;

namespace SpaceTraders.API.Endpoints;

/// <summary>Maps extended automation and rate-limit health endpoints.</summary>
public static class HealthExtendedEndpoints
{
    /// <summary>Registers the extended health routes on the given <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapHealthExtendedEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/automation", async (
            ILeaderElection leaderElection,
            ISettingsRepository settings,
            CancellationToken ct) =>
        {
            var automationEnabled = await settings.GetAsync<bool>("Automation.Enabled", ct);
            return Results.Ok(new
            {
                AutomationEnabled = automationEnabled,
                IsLeader = leaderElection.IsLeader,
            });
        });

        app.MapGet("/health/rate-limit/history", async (
            SpaceTradersDbContext db,
            CancellationToken ct) =>
        {
            var usages = await db.ApiEndpointUsages
                .AsNoTracking()
                .OrderByDescending(u => u.LastCalledAt)
                .Select(u => new
                {
                    u.HttpMethod,
                    u.Endpoint,
                    u.Calls,
                    u.LastCalledAt,
                })
                .ToListAsync(ct);

            return Results.Ok(usages);
        });

        return app;
    }
}
