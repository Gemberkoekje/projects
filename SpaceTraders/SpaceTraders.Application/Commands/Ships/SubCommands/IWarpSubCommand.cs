using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships.SubCommands;

/// <summary>
/// Subcommand that warps an in-orbit ship with a warp drive to a waypoint of another system (PLAN.md slice 6.31), as
/// <see cref="NavigateSubCommand"/> flies one within a system: it calls the API, keeps the nav and fuel it answers with,
/// publishes <see cref="ShipInTransitEvent"/>, and schedules the <see cref="ShipArrivedEvent"/> for the goal, so the arrival
/// wakes it (B17). A refusal is the API's <see cref="WarpRefusedException"/>, left to the caller.
/// </summary>
public interface IWarpSubCommand
{
    /// <summary>Warps the ship.</summary>
    /// <param name="shipSymbol">The ship, in orbit, in the flight mode to warp in.</param>
    /// <param name="destinationWaypoint">The waypoint of another system it warps to.</param>
    /// <param name="goalId">The goal its arrival wakes.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>Where the ship is going, when it arrives, and the fuel left.</returns>
    Task<WarpActionResult> ExecuteAsync(string shipSymbol, string destinationWaypoint, Guid goalId, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class WarpSubCommand(
    ISpaceTradersPort port,
    IShipRepository ships,
    IShipEventScheduler scheduler,
    IDashboardNotifier dashboardNotifier,
    IMessageBus bus) : IWarpSubCommand
{
    /// <inheritdoc />
    public async Task<WarpActionResult> ExecuteAsync(string shipSymbol, string destinationWaypoint, Guid goalId, CancellationToken cancellationToken)
    {
        var now = TimeProvider.System.GetUtcNow();
        var ship = await ships.FindAsync(shipSymbol, cancellationToken);
        var result = await port.WarpShipAsync(shipSymbol, destinationWaypoint, cancellationToken);
        await ships.UpdateNavAsync(shipSymbol, result.Nav, result.Fuel, cancellationToken);

        var arrival = result.Nav.ArrivesAt ?? now;
        await bus.PublishAsync(new ShipInTransitEvent(
            shipSymbol,
            ship?.WaypointSymbol ?? string.Empty,
            destinationWaypoint,
            arrival,
            Guid.Empty,
            Guid.Empty,
            now));

        dashboardNotifier.Notify("ships", shipSymbol);
        dashboardNotifier.Notify("fleet-activity", shipSymbol);
        dashboardNotifier.Notify("activity", shipSymbol);

        await scheduler.ScheduleArrivalAsync(shipSymbol, goalId, arrival, cancellationToken);
        return result;
    }
}
