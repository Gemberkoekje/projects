using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.API.Endpoints;

/// <summary>Maps the <c>/universe</c> read-only API endpoints.</summary>
public static class UniverseEndpoints
{
    /// <summary>Registers the universe route group on the given <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapUniverseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/universe");

        group.MapGet("/systems", async (ISystemRepository repo, CancellationToken ct) =>
        {
            var result = await repo.GetAllAsync(ct);
            return Results.Ok(result);
        });

        group.MapGet("/systems/{symbol}/map", async (
            string symbol,
            ISystemRepository systemRepo,
            IWaypointRepository waypointRepo,
            CancellationToken ct) =>
        {
            var system = await systemRepo.FindAsync(symbol, ct);
            if (system is null)
            {
                return Results.NotFound();
            }

            var waypoints = await waypointRepo.GetBySystemAsync(symbol, ct);
            return Results.Ok(new { System = system, Waypoints = waypoints });
        });

        return app;
    }
}
