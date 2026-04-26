using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record RefuelShipCommand
{
    public required string ShipSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public RefuelShipCommand(string ShipSymbol)
    {
        this.ShipSymbol = ShipSymbol;
    }
}

public sealed class RefuelShipHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IAgentRepository agents,
    IWaypointRepository waypoints,
    IMessageBus bus,
    ILogger<RefuelShipHandler> logger)
{
    public async Task Handle(RefuelShipCommand command, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        if (!string.Equals(ship?.Status, "DOCKED", StringComparison.OrdinalIgnoreCase))
        {
            var now = TimeProvider.System.GetUtcNow();
            await bus.PublishAsync(new ShipStateMismatchEvent(
                command.ShipSymbol,
                nameof(RefuelShipCommand),
                "DOCKED_AT_FUEL_MARKET",
                ship?.Status ?? "UNKNOWN",
                "Ship must be docked before refueling.",
                Guid.Empty,
                Guid.Empty,
                now));

            logger.LogWarning("Skipping refuel for ship {Symbol}: expected DOCKED but was {Status}.", command.ShipSymbol, ship?.Status ?? "UNKNOWN");
            return;
        }

        if (string.IsNullOrWhiteSpace(ship.WaypointSymbol))
        {
            var now = TimeProvider.System.GetUtcNow();
            await bus.PublishAsync(new ShipStateMismatchEvent(
                command.ShipSymbol,
                nameof(RefuelShipCommand),
                "DOCKED_AT_FUEL_MARKET",
                "DOCKED_AT_UNKNOWN_WAYPOINT",
                "Ship waypoint is unknown, cannot validate fuel market.",
                Guid.Empty,
                Guid.Empty,
                now));

            logger.LogWarning("Skipping refuel for ship {Symbol}: ship waypoint unknown.", command.ShipSymbol);
            return;
        }

        var waypoint = await waypoints.FindAsync(ship.WaypointSymbol, cancellationToken);
        if (waypoint?.HasMarket != true)
        {
            var now = TimeProvider.System.GetUtcNow();
            await bus.PublishAsync(new ShipStateMismatchEvent(
                command.ShipSymbol,
                nameof(RefuelShipCommand),
                "DOCKED_AT_FUEL_MARKET",
                "DOCKED_AT_NON_MARKET_WAYPOINT",
                "Ship is not docked at a market waypoint.",
                Guid.Empty,
                Guid.Empty,
                now));

            logger.LogWarning("Skipping refuel for ship {Symbol}: waypoint {Waypoint} is not a market.", command.ShipSymbol, ship.WaypointSymbol);
            return;
        }

        var result = await port.RefuelShipAsync(command.ShipSymbol, cancellationToken);

        await ships.UpdateFuelAsync(command.ShipSymbol, result.Fuel, cancellationToken);

        var agent = await agents.GetAsync(cancellationToken);
        if (agent is not null)
        {
            await agents.UpsertAsync(agent with { Credits = result.AgentCredits }, cancellationToken);
        }

        var nowRefueled = TimeProvider.System.GetUtcNow();
        await bus.PublishAsync(new ShipRefueledEvent(
            command.ShipSymbol,
            result.Fuel.Current,
            result.Fuel.Capacity,
            result.Cost,
            Guid.Empty,
            Guid.Empty,
            nowRefueled));

        logger.LogInformation("Ship {Symbol} refuelled. Fuel: {Current}/{Capacity}. Cost: {Cost} credits.",
            command.ShipSymbol, result.Fuel.Current, result.Fuel.Capacity, result.Cost);
    }
}
