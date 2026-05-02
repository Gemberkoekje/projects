using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.API.Endpoints;

/// <summary>Maps read-only fleet status endpoints backed by <see cref="IFleetStatusQueryService"/>.</summary>
/// <remarks>Phase 16a: controller and route definitions.</remarks>
public static class FleetStatusEndpoints
{
    /// <summary>Registers the fleet status route group on the given <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapFleetStatusEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/fleet");

        group.MapGet("/goal-chains", async (
            IFleetStatusQueryService fleetStatus,
            CancellationToken ct) =>
        {
            var chains = await fleetStatus.GetGoalChainsAsync(ct);
            return Results.Ok(chains);
        });

        group.MapGet("/assignments", async (
            IFleetStatusQueryService fleetStatus,
            CancellationToken ct) =>
        {
            var assignments = await fleetStatus.GetAssignmentsAsync(ct);
            return Results.Ok(assignments);
        });

        group.MapGet("/activity", async (
            IFleetStatusQueryService fleetStatus,
            CancellationToken ct) =>
        {
            var activities = await fleetStatus.GetShipActivitiesAsync(ct);
            return Results.Ok(activities);
        });

        group.MapGet("/activity/{shipSymbol}", async (
            string shipSymbol,
            IFleetStatusQueryService fleetStatus,
            CancellationToken ct) =>
        {
            var activity = await fleetStatus.GetShipActivityAsync(shipSymbol, ct);
            return activity is null ? Results.NotFound() : Results.Ok(activity);
        });

        return app;
    }
}
