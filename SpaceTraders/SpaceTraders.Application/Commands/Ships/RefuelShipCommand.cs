using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record RefuelShipCommand
{
    public required string ShipSymbol { get; init; }

    public bool FromCargo { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public RefuelShipCommand(string ShipSymbol, bool FromCargo = false)
    {
        this.ShipSymbol = ShipSymbol;
        this.FromCargo = FromCargo;
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
    public Task Handle(RefuelShipCommand command, CancellationToken cancellationToken)
        => ExecuteAsync(command, cancellationToken);

    public async Task<ShipCommandResult> ExecuteAsync(RefuelShipCommand command, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        var currentStatus = ship?.LocalStatus ?? ShipLocalStatus.None;

        if (currentStatus != ShipLocalStatus.Docked)
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(RefuelShipCommand),
                "DOCKED_AT_FUEL_MARKET",
                ship?.Status ?? "UNKNOWN",
                "Ship must be docked before refueling.");

            logger.LogWarning("Skipping refuel for ship {Symbol}: expected DOCKED but was {Status}.", command.ShipSymbol, ship?.Status ?? "UNKNOWN");
            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship?.SystemSymbol ?? string.Empty,
                ship?.WaypointSymbol ?? string.Empty);
        }

        if (string.IsNullOrWhiteSpace(ship!.WaypointSymbol))
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(RefuelShipCommand),
                "DOCKED",
                "DOCKED_AT_UNKNOWN_WAYPOINT",
                "Ship waypoint is unknown, cannot validate refuel preconditions.");

            logger.LogWarning("Skipping refuel for ship {Symbol}: ship waypoint unknown.", command.ShipSymbol);
            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship.SystemSymbol ?? string.Empty,
                string.Empty);
        }

        if (!command.FromCargo)
        {
            var waypoint = await waypoints.FindAsync(ship.WaypointSymbol, cancellationToken);
            if (waypoint?.HasMarket != true)
            {
                await bus.PublishMismatchAndTickAsync(
                    command.ShipSymbol,
                    nameof(RefuelShipCommand),
                    "DOCKED_AT_FUEL_MARKET",
                    "DOCKED_AT_NON_MARKET_WAYPOINT",
                    "Ship is not docked at a market waypoint and cargo-refuel was not requested.");

                logger.LogWarning("Skipping refuel for ship {Symbol}: waypoint {Waypoint} is not a market.", command.ShipSymbol, ship.WaypointSymbol);
                return ShipCommandResult.Rejected(
                    command.ShipSymbol,
                    currentStatus,
                    ship.SystemSymbol ?? string.Empty,
                    ship.WaypointSymbol);
            }
        }

        var result = await port.RefuelShipAsync(command.ShipSymbol, command.FromCargo, cancellationToken);

        await ships.UpdateFuelAsync(command.ShipSymbol, result.Fuel, cancellationToken);

        var agent = await agents.GetAsync(cancellationToken);
        if (agent is not null)
        {
            await agents.UpsertAsync(agent with { Credits = result.AgentCredits }, cancellationToken);
        }

        var nowRefueled = TimeProvider.System.GetUtcNow();

        await bus.PublishAsync(new ShipRefueledEvent(
            command.ShipSymbol,
            result.Cost,
            result.AgentCredits,
            ship.WaypointSymbol ?? string.Empty));

        // Phase 7b: ShipRefueledEvent deleted; publish ShipAutomationTickEvent so the planner
        // re-evaluates the ship with its updated fuel state immediately.
        await bus.PublishAsync(new ShipAutomationTickEvent(
            command.ShipSymbol,
            "Refueled",
            nowRefueled,
            Guid.Empty,
            Guid.Empty));

        logger.LogInformation("Ship {Symbol} refuelled ({Mode}). Fuel: {Current}/{Capacity}. Cost: {Cost} credits.",
            command.ShipSymbol,
            command.FromCargo ? "from cargo" : "market",
            result.Fuel.Current,
            result.Fuel.Capacity,
            result.Cost);

        return new ShipCommandResult(
            command.ShipSymbol,
            currentStatus,
            ship.SystemSymbol ?? string.Empty,
            ship.WaypointSymbol,
            FuelCurrent: result.Fuel.Current,
            FuelCapacity: result.Fuel.Capacity,
            CargoCurrent: ship.CargoCurrent,
            CargoCapacity: ship.CargoCapacity);
    }
}
