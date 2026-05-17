using Microsoft.Extensions.Logging;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Services;

public sealed class ShipPurchaseService(
    ISpaceTradersPort port,
    IAgentRepository agents,
    IShipRepository ships,
    IShipyardRepository shipyards,
    IBudgetPolicy budget,
    ILogger<ShipPurchaseService> logger) : IShipPurchaseService
{
    public async Task<ShipPurchaseResult> TryPurchaseAsync(
        string shipType,
        string shipyardWaypoint,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shipType) || string.IsNullOrWhiteSpace(shipyardWaypoint))
        {
            return new ShipPurchaseResult
            {
                IsSuccess = false,
                FailureReason = "Ship type or shipyard waypoint was empty.",
            };
        }

        var cached = await shipyards.FindByWaypointAsync(shipyardWaypoint, cancellationToken);
        var estimatedCost = ResolveShipPurchasePrice(cached, shipType);

        if (estimatedCost <= 0)
        {
            return new ShipPurchaseResult
            {
                IsSuccess = false,
                FailureReason = "Purchase price unknown (shipyard not yet visited or stale cache).",
                EstimatedCost = estimatedCost,
            };
        }

        var decision = await budget.EvaluateAsync(estimatedCost, cancellationToken);
        if (!decision.CanAfford)
        {
            return new ShipPurchaseResult
            {
                IsSuccess = false,
                FailureReason = decision.Reason,
                EstimatedCost = estimatedCost,
            };
        }

        var result = await port.PurchaseShipAsync(shipType, shipyardWaypoint, cancellationToken);

        await agents.UpsertAsync(result.Agent, cancellationToken);

        var newShip = new ShipModel(
            result.ShipSymbol,
            result.ShipNav.SystemSymbol,
            result.ShipNav.WaypointSymbol,
            result.ShipNav.Status,
            result.ShipNav.FlightMode,
            result.ShipFuel.Current,
            result.ShipFuel.Capacity,
            result.ShipNav.ArrivesAt,
            result.ShipNav.DestWaypointSymbol,
            result.ShipCargo.Units,
            result.ShipCargo.Capacity,
            ShipType: shipType,
            CargoInventory: result.ShipCargo.Inventory);

        await ships.UpsertAsync(newShip, cancellationToken);

        logger.LogInformation(
            "ShipPurchaseService: purchased {Symbol} ({Type}) at {Shipyard} for {Cost} credits.",
            result.ShipSymbol,
            shipType,
            shipyardWaypoint,
            result.Cost);

        return new ShipPurchaseResult
        {
            IsSuccess = true,
            EstimatedCost = estimatedCost,
            ActualCost = result.Cost,
            PurchasedShip = newShip,
        };
    }

    private static long ResolveShipPurchasePrice(ShipyardWaypointDto? dto, string shipType)
    {
        if (dto is null)
        {
            return 0;
        }

        var match = dto.Ships.FirstOrDefault(s => shipType.Equals(s.Type, StringComparison.OrdinalIgnoreCase));
        return match?.PurchasePrice ?? 0;
    }
}
