using Microsoft.Extensions.Logging;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Commands.Fleet;

/// <summary>
/// Purchases a ship of a given type at a specific shipyard, subject to a credit reserve check.
/// </summary>
public sealed record PurchaseShipCommand
{
    public required string ShipType { get; init; }

    public required string ShipyardWaypoint { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public PurchaseShipCommand(string ShipType, string ShipyardWaypoint)
    {
        this.ShipType = ShipType;
        this.ShipyardWaypoint = ShipyardWaypoint;
    }
}

public sealed class PurchaseShipHandler(
    ISpaceTradersPort port,
    IAgentRepository agents,
    IShipRepository ships,
    IShipyardRepository shipyards,
    IBudgetPolicy budget,
    IMessageBus bus,
    ILogger<PurchaseShipHandler> logger)
{
    public async Task Handle(PurchaseShipCommand command, CancellationToken cancellationToken)
    {
        var cached = await shipyards.FindByWaypointAsync(command.ShipyardWaypoint, cancellationToken);
        var estimatedCost = ResolveShipPurchasePrice(cached, command.ShipType);

        var decision = await budget.EvaluateAsync(estimatedCost, cancellationToken);
        if (!decision.CanAfford)
        {
            logger.LogInformation(
                "PurchaseShipHandler: skipping {ShipType} at {Shipyard} — {Reason}",
                command.ShipType,
                command.ShipyardWaypoint,
                decision.Reason);
            return;
        }

        var result = await port.PurchaseShipAsync(command.ShipType, command.ShipyardWaypoint, cancellationToken);

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
            result.ShipNav.DestWaypointSymbol);

        await ships.UpsertAsync(newShip, cancellationToken);

        if (Enum.TryParse<ShipType>(command.ShipType, true, out var shipTypeEnum))
        {
            await bus.PublishAsync(new NewShipPurchasedEvent(result.ShipSymbol, shipTypeEnum, result.Cost));
        }

        logger.LogInformation(
            "PurchaseShipHandler: purchased {Symbol} ({Type}) for {Cost} credits.",
            result.ShipSymbol,
            command.ShipType,
            result.Cost);
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
