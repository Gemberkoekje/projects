using SpaceTraders.API.Dtos;
using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.API.Endpoints;

/// <summary>Maps read-only fleet status endpoints backed by <see cref="IFleetStatusQueryService"/>.</summary>
/// <remarks>Phase 16a: controller and route definitions. Phase 16b: responses mapped to stable DTOs. Phase 16c: caching headers.</remarks>
public static class FleetStatusEndpoints
{
    private const string CacheControlValue = "public, max-age=5";

    /// <summary>Registers the fleet status route group on the given <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapFleetStatusEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/fleet");

        group.MapGet("/goal-chains", async (
            IFleetStatusQueryService fleetStatus,
            HttpContext http,
            CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = CacheControlValue;
            var chains = await fleetStatus.GetGoalChainsAsync(ct);
            return Results.Ok(chains.Select(FleetStatusMapper.ToDto).ToList());
        });

        group.MapGet("/assignments", async (
            IFleetStatusQueryService fleetStatus,
            HttpContext http,
            CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = CacheControlValue;
            var assignments = await fleetStatus.GetAssignmentsAsync(ct);
            return Results.Ok(assignments.Select(FleetStatusMapper.ToDto).ToList());
        });

        group.MapGet("/activity", async (
            IFleetStatusQueryService fleetStatus,
            HttpContext http,
            CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = CacheControlValue;
            var activities = await fleetStatus.GetShipActivitiesAsync(ct);
            return Results.Ok(activities.Select(FleetStatusMapper.ToDto).ToList());
        });

        group.MapGet("/activity/{shipSymbol}", async (
            string shipSymbol,
            IFleetStatusQueryService fleetStatus,
            HttpContext http,
            CancellationToken ct) =>
        {
            var activity = await fleetStatus.GetShipActivityAsync(shipSymbol, ct);
            if (activity is null)
            {
                return Results.NotFound();
            }

            http.Response.Headers.CacheControl = CacheControlValue;
            return Results.Ok(FleetStatusMapper.ToDto(activity));
        });

        return app;
    }
}
