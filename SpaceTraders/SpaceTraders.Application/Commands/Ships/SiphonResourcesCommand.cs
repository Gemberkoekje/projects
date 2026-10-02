using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Siphoning;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

/// <summary>
/// One siphon at the gas giant <see cref="SourceWaypoint"/> for a siphon trip (PLAN.md slice 6.7), as one
/// extraction of a mining trip (<see cref="MineResourceVolumeCommand"/>), without a survey: the API's siphon call
/// takes none. A docked ship orbits first (refuelling where fuel is sold), and a ship on cooldown or with a full
/// hold waits. Every good a market the ship can carry it to buys is kept (D33), whichever gas the trip is for;
/// only what no such market buys is jettisoned.
/// </summary>
public sealed record SiphonResourcesCommand
{
    /// <summary>The ship that siphons.</summary>
    public required string ShipSymbol { get; init; }

    /// <summary>The gas the trip is for, for the journal.</summary>
    public required string TradeSymbol { get; init; }

    /// <summary>The gas giant the ship is at.</summary>
    public required string SourceWaypoint { get; init; }

    /// <summary>Creates the command.</summary>
    /// <param name="ShipSymbol">The ship that siphons.</param>
    /// <param name="TradeSymbol">The gas the trip is for.</param>
    /// <param name="SourceWaypoint">The gas giant the ship is at.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public SiphonResourcesCommand(string ShipSymbol, string TradeSymbol, string SourceWaypoint)
    {
        this.ShipSymbol = ShipSymbol;
        this.TradeSymbol = TradeSymbol;
        this.SourceWaypoint = SourceWaypoint;
    }
}

/// <summary>Handles <see cref="SiphonResourcesCommand"/>.</summary>
public sealed class SiphonResourcesHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IWaypointRepository waypoints,
    ITradeContextReader tradeContexts,
    IOrbitSubCommand orbit,
    IMessageBus bus,
    IAutomationMetrics metrics,
    ILogger<SiphonResourcesHandler> logger)
{
    /// <summary>Wolverine's entry point.</summary>
    /// <param name="command">The siphon to make.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The ship's state afterwards; not accepted when it can't siphon there.</returns>
    public Task<ShipCommandResult> Handle(SiphonResourcesCommand command, CancellationToken cancellationToken)
        => ExecuteAsync(command, cancellationToken);

    /// <summary>Makes one siphon, when the ship is in orbit at the gas giant and off cooldown.</summary>
    /// <param name="command">The siphon to make.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The ship's state afterwards; not accepted when it can't siphon there.</returns>
    public async Task<ShipCommandResult> ExecuteAsync(SiphonResourcesCommand command, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            return ShipCommandResult.Rejected(command.ShipSymbol, ShipLocalStatus.None, string.Empty, string.Empty);
        }

        // The trip flies the ship there (GoalFlight); this command only siphons where it is.
        if (ship.LocalStatus == ShipLocalStatus.InTransit
            || !string.Equals(ship.WaypointSymbol, command.SourceWaypoint, StringComparison.OrdinalIgnoreCase))
        {
            return Rejected(ship);
        }

        if (ship.LocalStatus == ShipLocalStatus.Docked)
        {
            await orbit.ExecuteAsync(ship.Symbol, cancellationToken);
            ship = await ships.FindAsync(ship.Symbol, cancellationToken) ?? ship;
        }

        if (ship.LocalStatus != ShipLocalStatus.InOrbit)
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(SiphonResourcesCommand),
                "IN_ORBIT",
                ship.Status ?? "UNKNOWN",
                "Ship must be in orbit before siphoning.");
            return Rejected(ship);
        }

        var now = TimeProvider.System.GetUtcNow();
        if ((ship.CooldownExpiresAt.HasValue && ship.CooldownExpiresAt.Value > now)
            || (ship.CargoCapacity > 0 && ship.CargoCurrent >= ship.CargoCapacity))
        {
            return Unchanged(ship);
        }

        var waypoint = await waypoints.FindAsync(command.SourceWaypoint, cancellationToken);
        if (waypoint is not null && !GasGiants.IsSiphonable(waypoint.Type))
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(SiphonResourcesCommand),
                "GAS_GIANT",
                waypoint.Type,
                $"Waypoint {command.SourceWaypoint} is type {waypoint.Type}, which can't be siphoned.");
            return Rejected(ship);
        }

        var siphoned = await port.SiphonResourcesAsync(ship.Symbol, cancellationToken);
        await ships.UpdateCargoAsync(ship.Symbol, siphoned.Cargo, cancellationToken);
        metrics.Extracted(ship.Symbol, siphoned.YieldSymbol, siphoned.YieldUnits);
        await ships.UpdateCooldownAsync(ship.Symbol, siphoned.CooldownExpiresAt ?? now.AddSeconds(siphoned.CooldownSeconds), cancellationToken);

        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} siphoned {Units} {TradeSymbol} at {WaypointSymbol}, siphoning for {Target}.",
            JournalEvents.Siphoned,
            ship.Symbol,
            siphoned.YieldUnits,
            siphoned.YieldSymbol,
            command.SourceWaypoint,
            command.TradeSymbol);

        var cargo = await JettisonUnsellableAsync(ship, siphoned.Cargo, cancellationToken);
        return new ShipCommandResult(
            ship.Symbol,
            ShipLocalStatus.InOrbit,
            ship.SystemSymbol ?? string.Empty,
            ship.WaypointSymbol ?? string.Empty,
            FuelCurrent: ship.FuelCurrent,
            FuelCapacity: ship.FuelCapacity,
            CargoCurrent: cargo.Units,
            CargoCapacity: cargo.Capacity,
            Accepted: true);
    }

    /// <summary>
    /// Keeps every good a market the ship can carry it to from here buys (D33): the siphon plan sells the other
    /// gases after the trip's. What no such market buys would fill the hold for good, so it goes overboard.
    /// </summary>
    private async Task<CargoModel> JettisonUnsellableAsync(ShipModel ship, CargoModel cargo, CancellationToken cancellationToken)
    {
        var aboard = (cargo.Inventory ?? []).Where(item => item.Units > 0).ToList();
        if (aboard.Count == 0)
        {
            return cargo;
        }

        var map = (await tradeContexts.ReadAsync(ship.SystemSymbol ?? string.Empty, cancellationToken)).Map;
        var here = ship.WaypointSymbol ?? string.Empty;
        bool Sellable(string tradeSymbol) => map.MarketWaypoints.Any(market =>
            map.TryGetGood(market, tradeSymbol, out var good)
            && good.SellPrice > 0
            && MiningPlanner.CanSellFrom(map, ship, here, market));

        foreach (var item in aboard.Where(item => !Sellable(item.Symbol)))
        {
            var jettisoned = await port.JettisonCargoAsync(ship.Symbol, item.Symbol, item.Units, cancellationToken);
            await ships.UpdateCargoAsync(ship.Symbol, jettisoned.Cargo, cancellationToken);
            metrics.Jettisoned(ship.Symbol, item.Symbol, item.Units);
            cargo = jettisoned.Cargo;
        }

        return cargo;
    }

    private static ShipCommandResult Rejected(ShipModel ship)
        => ShipCommandResult.Rejected(ship.Symbol, ship.LocalStatus, ship.SystemSymbol ?? string.Empty, ship.WaypointSymbol ?? string.Empty);

    private static ShipCommandResult Unchanged(ShipModel ship)
        => new(
            ship.Symbol,
            ship.LocalStatus,
            ship.SystemSymbol ?? string.Empty,
            ship.WaypointSymbol ?? string.Empty,
            FuelCurrent: ship.FuelCurrent,
            FuelCapacity: ship.FuelCapacity,
            CargoCurrent: ship.CargoCurrent,
            CargoCapacity: ship.CargoCapacity,
            Accepted: true);
}
