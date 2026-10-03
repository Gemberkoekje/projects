using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.SpareTime;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

/// <summary>
/// One extraction at the asteroid <see cref="SourceWaypoint"/> for a spare-time trip (PLAN.md slice 6.8), as one siphon
/// of a siphon trip (<see cref="SiphonResourcesCommand"/>): without a survey, as the surveys stay for the drones (D35).
/// A docked ship orbits first, and a ship on cooldown or with a full hold waits. Every good a market the ship can carry
/// it to buys is kept, whatever it is; only what no such market buys is jettisoned. The yield counts in the mined
/// units, but not as an extraction in the survey statistics, which would read a spare-time extraction, unsurveyed by
/// design, as a sign of too few surveys.
/// </summary>
public sealed record ExtractResourcesCommand
{
    /// <summary>The ship that extracts.</summary>
    public required string ShipSymbol { get; init; }

    /// <summary>The asteroid the ship is at.</summary>
    public required string SourceWaypoint { get; init; }

    /// <summary>Creates the command.</summary>
    /// <param name="ShipSymbol">The ship that extracts.</param>
    /// <param name="SourceWaypoint">The asteroid the ship is at.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ExtractResourcesCommand(string ShipSymbol, string SourceWaypoint)
    {
        this.ShipSymbol = ShipSymbol;
        this.SourceWaypoint = SourceWaypoint;
    }
}

/// <summary>Handles <see cref="ExtractResourcesCommand"/>.</summary>
public sealed class ExtractResourcesHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IWaypointRepository waypoints,
    ITradeContextReader tradeContexts,
    IOrbitSubCommand orbit,
    IMessageBus bus,
    IAutomationMetrics metrics,
    IGatheringRates rates,
    ILogger<ExtractResourcesHandler> logger)
{
    /// <summary>Wolverine's entry point.</summary>
    /// <param name="command">The extraction to make.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The ship's state afterwards; not accepted when it can't extract there.</returns>
    public Task<ShipCommandResult> Handle(ExtractResourcesCommand command, CancellationToken cancellationToken)
        => ExecuteAsync(command, cancellationToken);

    /// <summary>Makes one extraction, when the ship is in orbit at the asteroid and off cooldown.</summary>
    /// <param name="command">The extraction to make.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The ship's state afterwards; not accepted when it can't extract there.</returns>
    public async Task<ShipCommandResult> ExecuteAsync(ExtractResourcesCommand command, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            return ShipCommandResult.Rejected(command.ShipSymbol, ShipLocalStatus.None, string.Empty, string.Empty);
        }

        // The trip flies the ship there (GoalFlight); this command only extracts where it is.
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
                nameof(ExtractResourcesCommand),
                "IN_ORBIT",
                ship.Status ?? "UNKNOWN",
                "Ship must be in orbit before extraction.");
            return Rejected(ship);
        }

        var now = TimeProvider.System.GetUtcNow();
        if ((ship.CooldownExpiresAt.HasValue && ship.CooldownExpiresAt.Value > now)
            || (ship.CargoCapacity > 0 && ship.CargoCurrent >= ship.CargoCapacity))
        {
            return Unchanged(ship);
        }

        var waypoint = await waypoints.FindAsync(command.SourceWaypoint, cancellationToken);
        if (waypoint is not null && !AsteroidDeposits.IsExtractable(waypoint.Type))
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(ExtractResourcesCommand),
                "ASTEROID, ASTEROID_FIELD or ENGINEERED_ASTEROID",
                waypoint.Type,
                $"Waypoint {command.SourceWaypoint} is type {waypoint.Type}, which can't be mined.");
            return Rejected(ship);
        }

        var extracted = await port.ExtractResourcesAsync(ship.Symbol, cancellationToken);
        await ships.UpdateCargoAsync(ship.Symbol, extracted.Cargo, cancellationToken);
        metrics.Extracted(ship.Symbol, extracted.YieldSymbol, extracted.YieldUnits);
        rates.Record(ship.Symbol, GatheringKind.Mining, extracted.YieldUnits, extracted.CooldownSeconds);
        await ships.UpdateCooldownAsync(ship.Symbol, extracted.CooldownExpiresAt ?? now.AddSeconds(extracted.CooldownSeconds), cancellationToken);

        // The template MineResourceVolumeCommand logs, so the journal reads every extraction alike.
        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} extracted {Units} {TradeSymbol} at {WaypointSymbol}, mining for {Target}, with survey {Signature}.",
            JournalEvents.Extracted,
            ship.Symbol,
            extracted.YieldUnits,
            extracted.YieldSymbol,
            command.SourceWaypoint,
            GatherPlanner.AnyGood,
            string.Empty);

        var cargo = await JettisonUnsellableAsync(ship, extracted.Cargo, cancellationToken);
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
    /// Keeps every good a market the ship can carry it to from here buys, whatever it is (slice 6.8, the rule of a
    /// siphon, D33); what no such market buys would fill the hold for good, so it goes overboard.
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
        foreach (var item in aboard.Where(item => !MiningPlanner.IsSellableFrom(map, ship, here, item.Symbol)))
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
