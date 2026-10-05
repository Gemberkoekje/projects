using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Commands.Ships.SubCommands;

/// <summary>
/// Subcommand that sets a ship's flight mode before it flies (PLAN.md slice 6.10c, slice 6.19): the mode the route planner
/// gives the leg (D84), BURN, CRUISE or DRIFT, so a ship left in DRIFT flies on in the mode asked for (B47).
/// </summary>
public interface IFlightModeSubCommand
{
    /// <summary>Sets the ship's flight mode, calling the API only when the cached mode differs.</summary>
    /// <param name="ship">The ship, as cached.</param>
    /// <param name="flightMode">The flight mode, as the API names it (<c>BURN</c>, <c>CRUISE</c>, <c>DRIFT</c>).</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>A task that completes when the ship flies in that mode.</returns>
    Task EnsureAsync(ShipModel ship, string flightMode, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class FlightModeSubCommand(
    ISpaceTradersPort port,
    IShipRepository ships,
    ILogger<FlightModeSubCommand> logger) : IFlightModeSubCommand
{
    /// <inheritdoc />
    public Task EnsureAsync(ShipModel ship, string flightMode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ship);

        return string.Equals(ship.FlightMode, flightMode, StringComparison.OrdinalIgnoreCase)
            ? Task.CompletedTask
            : SwitchAsync(ship, flightMode, cancellationToken);
    }

    private async Task SwitchAsync(ShipModel ship, string flightMode, CancellationToken cancellationToken)
    {
        var nav = await port.PatchShipNavAsync(ship.Symbol, flightMode, cancellationToken);
        await ships.UpdateNavAsync(ship.Symbol, nav, null, cancellationToken);
        logger.LogInformation(
            "FlightModeSubCommand: ship {ShipSymbol} switches from {OldFlightMode} to {FlightMode}.",
            ship.Symbol,
            ship.FlightMode ?? "an unknown mode",
            nav.FlightMode);
    }
}
