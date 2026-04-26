using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Sync;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
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
    IShipAssignmentRepository assignments,
    IMessageBus bus,
    ILogger<SellCargoHandler> logger)
{
    public async Task Handle(SellCargoCommand command, CancellationToken cancellationToken)
    {
        var result = await port.SellCargoAsync(command.ShipSymbol, command.TradeSymbol, command.Units, cancellationToken);

        await ships.UpdateCargoAsync(command.ShipSymbol, result.Cargo, cancellationToken);

        var agent = await agents.GetAsync(cancellationToken);
        if (agent is not null)
        {
            await agents.UpsertAsync(agent with { Credits = result.AgentCredits }, cancellationToken);
        }

        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        if (ship?.SystemSymbol is not null && ship.WaypointSymbol is not null)
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
