using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Sync;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Events.Ships;
using SpaceTraders.Domain.ValueObjects;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record SellCargoCommand
{
    public required string ShipSymbol { get; init; }

    public required string TradeSymbol { get; init; }

    public required int Units { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public SellCargoCommand(string ShipSymbol, string TradeSymbol, int Units)
    {
        this.ShipSymbol = ShipSymbol;
        this.TradeSymbol = TradeSymbol;
        this.Units = Units;
    }
}

public sealed class SellCargoHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IAgentRepository agents,
    IWaypointRepository waypoints,
    IShipAssignmentRepository assignments,
    IMessageBus bus,
    ILogger<SellCargoHandler> logger)
{
    public async Task Handle(SellCargoCommand command, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        if (!string.Equals(ship?.Status, "DOCKED", StringComparison.OrdinalIgnoreCase))
        {
            var now = TimeProvider.System.GetUtcNow();
            await bus.PublishAsync(new ShipStateMismatchEvent(
                command.ShipSymbol,
                nameof(SellCargoCommand),
                "DOCKED_AT_MARKET",
                ship?.Status ?? "UNKNOWN",
                "Ship must be docked before selling cargo.",
                Guid.Empty,
                Guid.Empty,
                now));

            logger.LogWarning("Skipping sell for ship {Symbol}: expected DOCKED but was {Status}.", command.ShipSymbol, ship?.Status ?? "UNKNOWN");
            return;
        }

        if (string.IsNullOrWhiteSpace(ship.WaypointSymbol))
        {
            var now = TimeProvider.System.GetUtcNow();
            await bus.PublishAsync(new ShipStateMismatchEvent(
                command.ShipSymbol,
                nameof(SellCargoCommand),
                "DOCKED_AT_MARKET",
                "DOCKED_AT_UNKNOWN_WAYPOINT",
                "Ship waypoint is unknown, cannot validate market.",
                Guid.Empty,
                Guid.Empty,
                now));

            logger.LogWarning("Skipping sell for ship {Symbol}: ship waypoint unknown.", command.ShipSymbol);
            return;
        }

        var waypoint = await waypoints.FindAsync(ship.WaypointSymbol, cancellationToken);
        if (waypoint?.HasMarket != true)
        {
            var now = TimeProvider.System.GetUtcNow();
            await bus.PublishAsync(new ShipStateMismatchEvent(
                command.ShipSymbol,
                nameof(SellCargoCommand),
                "DOCKED_AT_MARKET",
                "DOCKED_AT_NON_MARKET_WAYPOINT",
                "Ship is not docked at a market waypoint.",
                Guid.Empty,
                Guid.Empty,
                now));

            logger.LogWarning("Skipping sell for ship {Symbol}: waypoint {Waypoint} is not a market.", command.ShipSymbol, ship.WaypointSymbol);
            return;
        }

        var result = await port.SellCargoAsync(command.ShipSymbol, command.TradeSymbol, command.Units, cancellationToken);

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

        await bus.PublishAsync(new ShipCargoSoldEvent(
            command.ShipSymbol,
            new TradeSymbol(command.TradeSymbol),
            command.Units,
            result.Revenue,
            result.AgentCredits));

        var assignment = await assignments.FindAsync(command.ShipSymbol, cancellationToken);
        if (assignment is not null && assignment.CompletedAt.HasValue)
        {
            var completedType = ResolveAssignmentType(assignment.AssignmentType);
            if (completedType != AssignmentType.None)
            {
                await bus.PublishAsync(new ShipAssignmentCompletedEvent(command.ShipSymbol, completedType));
            }
        }

        logger.LogInformation("Ship {Symbol} sold {Units}x {Good} for {Revenue} credits.",
            command.ShipSymbol, command.Units, command.TradeSymbol, result.Revenue);
    }

    private static AssignmentType ResolveAssignmentType(string assignmentType)
    {
        if (assignmentType.Equals("Trade", StringComparison.OrdinalIgnoreCase))
        {
            return AssignmentType.Trade;
        }

        if (assignmentType.Equals("Mine", StringComparison.OrdinalIgnoreCase))
        {
            return AssignmentType.Mine;
        }

        return AssignmentType.None;
    }
}
