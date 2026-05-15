using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships.SubCommands;

/// <summary>
/// Subcommand that refuels a docked ship by calling the SpaceTraders API.
/// Must only be called when the ship is docked at a waypoint that sells fuel (or when fromCargo is true).
/// </summary>
public interface IRefuelSubCommand
{
    Task ExecuteAsync(string shipSymbol, bool fromCargo, CancellationToken cancellationToken);
}

public sealed class RefuelSubCommand(
    ISpaceTradersPort port,
    IShipRepository ships,
    IAgentRepository agents,
    IMessageBus bus,
    ILogger<RefuelSubCommand> logger) : IRefuelSubCommand
{
    public async Task ExecuteAsync(string shipSymbol, bool fromCargo, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "RefuelSubCommand: refueling ship {Symbol} (fromCargo={FromCargo}).",
            shipSymbol,
            fromCargo);

        var ship = await ships.FindAsync(shipSymbol, cancellationToken);
        var result = await port.RefuelShipAsync(shipSymbol, fromCargo, cancellationToken);

        await ships.UpdateFuelAsync(shipSymbol, result.Fuel, cancellationToken);

        var agent = await agents.GetAsync(cancellationToken);
        if (agent is not null)
        {
            await agents.UpsertAsync(agent with { Credits = result.AgentCredits }, cancellationToken);
        }

        await bus.PublishAsync(new ShipRefueledEvent(
            shipSymbol,
            result.Cost,
            result.AgentCredits,
            ship?.WaypointSymbol ?? string.Empty));

        logger.LogInformation(
            "RefuelSubCommand: ship {Symbol} refueled to {Current}/{Capacity}, cost {Cost}.",
            shipSymbol,
            result.Fuel.Current,
            result.Fuel.Capacity,
            result.Cost);
    }
}
