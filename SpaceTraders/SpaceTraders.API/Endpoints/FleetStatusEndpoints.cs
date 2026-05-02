using SpaceTraders.API.Dtos;
using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.API.Endpoints;

/// <summary>Maps read-only fleet status endpoints backed by <see cref="IFleetStatusQueryService"/>.</summary>
/// <remarks>Phase 16a: controller and route definitions. Phase 16b: responses mapped to stable DTOs.</remarks>
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
            return Results.Ok(chains.Select(FleetStatusMapper.ToDto).ToList());
        });

        group.MapGet("/assignments", async (
            IFleetStatusQueryService fleetStatus,
            CancellationToken ct) =>
        {
            var assignments = await fleetStatus.GetAssignmentsAsync(ct);
            return Results.Ok(assignments.Select(FleetStatusMapper.ToDto).ToList());
        });

        group.MapGet("/activity", async (
            IFleetStatusQueryService fleetStatus,
            CancellationToken ct) =>
        {
            var activities = await fleetStatus.GetShipActivitiesAsync(ct);
            return Results.Ok(activities.Select(FleetStatusMapper.ToDto).ToList());
        });

        group.MapGet("/activity/{shipSymbol}", async (
            string shipSymbol,
            IFleetStatusQueryService fleetStatus,
            CancellationToken ct) =>
        {
            var activity = await fleetStatus.GetShipActivityAsync(shipSymbol, ct);
            return activity is null ? Results.NotFound() : Results.Ok(FleetStatusMapper.ToDto(activity));
        });

        return app;
    }
}
