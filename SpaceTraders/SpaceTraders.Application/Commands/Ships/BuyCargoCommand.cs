using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Sync;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record BuyCargoCommand
{
    public required string ShipSymbol { get; init; }

    public required string TradeSymbol { get; init; }

    public required int Units { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public BuyCargoCommand(string ShipSymbol, string TradeSymbol, int Units)
    {
        this.ShipSymbol = ShipSymbol;
        this.TradeSymbol = TradeSymbol;
        this.Units = Units;
    }
}

public sealed class BuyCargoHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IAgentRepository agents,
    IWaypointRepository waypoints,
    IMessageBus bus,
    ILogger<BuyCargoHandler> logger)
{
    public Task Handle(BuyCargoCommand command, CancellationToken cancellationToken)
        => ExecuteAsync(command, cancellationToken);

    public async Task<ShipCommandResult> ExecuteAsync(BuyCargoCommand command, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        var currentStatus = ship?.LocalStatus ?? ShipLocalStatus.None;

        if (currentStatus != ShipLocalStatus.Docked)
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(BuyCargoCommand),
                "DOCKED_AT_MARKET",
                ship?.Status ?? "UNKNOWN",
                "Ship must be docked before buying cargo.");

            logger.LogWarning("Skipping buy for ship {Symbol}: expected DOCKED but was {Status}.", command.ShipSymbol, ship?.Status ?? "UNKNOWN");
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
                nameof(BuyCargoCommand),
                "DOCKED_AT_MARKET",
                "DOCKED_AT_UNKNOWN_WAYPOINT",
                "Ship waypoint is unknown, cannot validate market.");

            logger.LogWarning("Skipping buy for ship {Symbol}: ship waypoint unknown.", command.ShipSymbol);
            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship.SystemSymbol ?? string.Empty,
                string.Empty);
        }

        var waypoint = await waypoints.FindAsync(ship.WaypointSymbol, cancellationToken);
        if (waypoint?.HasMarket != true)
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(BuyCargoCommand),
                "DOCKED_AT_MARKET",
                "DOCKED_AT_NON_MARKET_WAYPOINT",
                "Ship is not docked at a market waypoint.");

            logger.LogWarning("Skipping buy for ship {Symbol}: waypoint {Waypoint} is not a market.", command.ShipSymbol, ship.WaypointSymbol);
            return ShipCommandResult.Rejected(
                command.ShipSymbol,
                currentStatus,
                ship.SystemSymbol ?? string.Empty,
                ship.WaypointSymbol);
        }

        var result = await port.BuyCargoAsync(command.ShipSymbol, command.TradeSymbol, command.Units, cancellationToken);

        await ships.UpdateCargoAsync(command.ShipSymbol, result.Cargo, cancellationToken);

        var agent = await agents.GetAsync(cancellationToken);
        if (agent is not null)
        {
            await agents.UpsertAsync(agent with { Credits = result.AgentCredits }, cancellationToken);
        }

        if (ship.SystemSymbol is not null && ship.WaypointSymbol is not null)
        {
            await bus.SendAsync(new RefreshMarketDataCommand(ship.SystemSymbol, ship.WaypointSymbol, ForceRefresh: true));
        }

        logger.LogInformation("Ship {Symbol} bought {Units}x {Good} for {Cost} credits.",
            command.ShipSymbol, command.Units, command.TradeSymbol, result.Revenue);

        return new ShipCommandResult(
            command.ShipSymbol,
            currentStatus,
            ship.SystemSymbol ?? string.Empty,
            ship.WaypointSymbol,
            FuelCurrent: ship.FuelCurrent,
            FuelCapacity: ship.FuelCapacity,
            CargoCurrent: result.Cargo.Units,
            CargoCapacity: result.Cargo.Capacity);
    }
}
