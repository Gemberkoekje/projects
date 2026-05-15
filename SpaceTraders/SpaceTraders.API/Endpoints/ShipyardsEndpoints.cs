using System.Text.Json;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Shipyards;

namespace SpaceTraders.API.Endpoints;

public static class ShipyardsEndpoints
{
    public static IEndpointRouteBuilder MapShipyardsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/shipyards");

        group.MapGet("/waypoints", async (IShipyardRepository repo, CancellationToken ct) =>
        {
            var shipyards = await repo.GetAllAsync(ct);
            return Results.Ok(shipyards);
        });

        group.MapGet("/waypoints/{symbol}", async (string symbol, IShipyardRepository repo, CancellationToken ct) =>
        {
            var shipyard = await repo.FindByWaypointAsync(symbol, ct);
            return shipyard is null ? Results.NotFound() : Results.Ok(shipyard);
        });

        group.MapGet("/freshness", async (IShipyardRepository repo, CancellationToken ct) =>
        {
            var freshness = await repo.GetAllFreshnessAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var result = freshness
                .Select(f => new ShipyardFreshnessDto
                {
                    WaypointSymbol = f.WaypointSymbol,
                    SystemSymbol = f.SystemSymbol,
                    LastObservedAt = f.LastObservedAt,
                    AgeMinutes = (int)Math.Round((now - f.LastObservedAt).TotalMinutes)
                })
                .OrderBy(f => f.LastObservedAt)
                .ToList();
            return Results.Ok(result);
        });

        return app;
    }
}
