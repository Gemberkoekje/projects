using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Exploring;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>The ways between systems of a ship with a warp drive, and a warp's steps (PLAN.md slice 6.31, D101).</summary>
public interface IGoalWarps
{
    /// <summary>
    /// The fastest way from where the ship is to a waypoint of another system, through the gates and by warps
    /// (<see cref="SystemWays"/>).
    /// </summary>
    /// <param name="ship">The ship, with a warp drive, where it is now.</param>
    /// <param name="destination">The waypoint it is going to.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The way; <see cref="SystemWay.None"/> when there is none.</returns>
    Task<SystemWay> FindAsync(ShipModel ship, string destination, CancellationToken cancellationToken);

    /// <summary>
    /// Takes the ship one step of a warp to <paramref name="destination"/>, a waypoint of another system: the refuel before it,
    /// a flight to a market of its system first where it lacks the fuel and can't refuel where it is, or the warp.
    /// </summary>
    /// <param name="ship">The ship, with a warp drive, where it is now.</param>
    /// <param name="destination">The waypoint it warps to.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>What the step did.</returns>
    Task<JumpStep> WarpAsync(ShipModel ship, string destination, CancellationToken cancellationToken);
}

/// <summary>
/// How a ship warps (PLAN.md slice 6.31, D100, D101, D104), every executor alike: <see cref="WarpGoalExecutor"/> for the explore
/// plan's warps, and <see cref="GoalJumps"/> for every flight to another system where a warp is the fastest way.
/// <list type="bullet">
///   <item>It warps from wherever it is in its system, within its drive's range (<see cref="Warps"/>).</item>
///   <item>Fuel-safe (D100): it lands where it can refuel, or keeps the fuel to warp back to the market it leaves from.</item>
///   <item>At a market it fills its tank first where a full tank warps where this one can't, or in BURN: one in orbit docks,
///   one docked refuels and orbits. Short of the fuel where it can't refuel, it flies to its system's nearest market first
///   (<see cref="GoalFlight"/>).</item>
///   <item>It warps in BURN where the fuel pays for it, in CRUISE otherwise (D104: never a drift); a BURN warp the API refuses
///   for its fuel goes in CRUISE.</item>
///   <item>A warp the API refuses (<see cref="WarpRefusedException"/>) leaves that system alone for an hour
///   (<see cref="WarpRefusals"/>).</item>
/// </list>
/// The warp (<see cref="IWarpSubCommand"/>) carries the ship's goal, so its arrival wakes it, as a flight's does (B17), and it
/// is journalled (<c>Warped</c>)
/// with the fuel and the seconds it took against those <see cref="Warps"/> reckons: the first warp measures the research note
/// (D100), and one that differs logs a warning.
/// </summary>
public sealed class GoalWarps(
    IShipRepository ships,
    IShipGoalRepository goals,
    ISystemRepository systems,
    IWaypointRepository waypoints,
    IGateNetwork gates,
    WarpRefusals refusals,
    ITradeContextReader tradeContexts,
    IDockSubCommand dock,
    IOrbitSubCommand orbit,
    IRefuelSubCommand refuel,
    IFlightModeSubCommand flightMode,
    IWarpSubCommand warp,
    IMessageBus bus,
    ILogger<GoalWarps> logger) : IGoalWarps
{
    /// <summary>The <see cref="Domain.Goals.ShipGoal.StatusReason"/> of a goal whose warp the API refused.</summary>
    public const string RefusedReason = "warp_refused";

    /// <summary>How far a warp's seconds may differ from those reckoned before the warning says so: the call's own round trip.</summary>
    private const double SecondsTolerance = 3;

    private static readonly ExplorePlanState NoNetwork = new()
    {
        ShipSymbol = string.Empty,
        HomeSystemSymbol = string.Empty,
        Status = ExploreStatus.Waiting,
        UpdatedAt = DateTimeOffset.MinValue,
    };

    /// <inheritdoc />
    public async Task<SystemWay> FindAsync(ShipModel ship, string destination, CancellationToken cancellationToken)
    {
        var now = TimeProvider.System.GetUtcNow();
        var chart = await ChartAsync(now, cancellationToken);
        return SystemWays.TryFind(chart, WayShip.Of(ship, now), destination, out var way) ? way : SystemWay.None;
    }

    /// <inheritdoc />
    public async Task<JumpStep> WarpAsync(ShipModel ship, string destination, CancellationToken cancellationToken)
    {
        var now = TimeProvider.System.GetUtcNow();
        var here = ship.SystemSymbol ?? string.Empty;
        var system = WaypointSymbols.SystemOf(destination);
        var chart = await ChartAsync(now, cancellationToken);
        if (!chart.TryGetPosition(here, out var from) || !chart.TryGetPosition(system, out var to))
        {
            return NoWay($"Where {here} or {system} lies isn't known, so no warp goes there.");
        }

        var distance = Warps.Distance(from, to);
        var range = Warps.Range(ship);
        if (distance > range)
        {
            return NoWay($"{system} lies {distance:0} from {here}, beyond the warp drive's range of {range}.");
        }

        // Fuel-safe (D100): it lands where it can refuel, or keeps the fuel to warp back to the market it leaves from.
        var landsAtMarket = chart.TryGetWaypoint(destination, out var landing) && landing.Refuels;
        var atMarket = chart.TryGetWaypoint(ship.WaypointSymbol ?? string.Empty, out var at) && at.Refuels;
        var keep = landsAtMarket ? 0 : Warps.Fuel(TradeRoutePlanner.CruiseMode, distance);
        var asIs = Warps.TryChooseMode(distance, ship.FuelCurrent, keep, out var mode);
        if (atMarket
            && ship.FuelCurrent < ship.FuelCapacity
            && Warps.TryChooseMode(distance, ship.FuelCapacity, keep, out var filled)
            && (!asIs || filled != mode))
        {
            // A full tank warps where this one can't, or in BURN (as GoalFlight docks for a leg, D84). Only a docked ship
            // refuels: one in orbit docks first, and the orbit after it fills the tank at a market (OrbitSubCommand).
            if (ship.LocalStatus == ShipLocalStatus.InOrbit)
            {
                await dock.ExecuteAsync(ship.Symbol, cancellationToken);
                return new JumpStep(JumpStepOutcome.Flying, GoalExecutionResult.Progressing($"Docking at {ship.WaypointSymbol} to refuel before warping to {destination}."));
            }

            (asIs, mode) = (true, filled);
        }

        if (!asIs || !(landsAtMarket || atMarket))
        {
            // Short of the fuel where it can't refuel, or leaving where the way back can't land: its system's nearest market first.
            if (!atMarket && NearestMarket(chart, ship) is { } market && Warps.TryChooseMode(distance, ship.FuelCapacity, keep, out _))
            {
                var context = await tradeContexts.ReadAsync(here, cancellationToken);
                return new JumpStep(JumpStepOutcome.Flying, await GoalFlight.TowardsAsync(context.Map, ship, market, dock, bus, cancellationToken));
            }

            return NoWay($"A tank of {ship.FuelCapacity} doesn't take the ship from {here} to {destination}, {distance:0} away, fuel-safe.");
        }

        if (ship.LocalStatus == ShipLocalStatus.Docked)
        {
            // Docked where it can refuel, it fills the tank first, as a jump does at its gate; a warp leaves from orbit.
            if (atMarket && ship.FuelCurrent < ship.FuelCapacity)
            {
                await refuel.ExecuteAsync(ship.Symbol, fromCargo: false, cancellationToken);
            }

            await orbit.ExecuteAsync(ship.Symbol, cancellationToken);
        }

        // The mode for the fuel aboard now: a tank the refuel didn't fill doesn't ask the API for a warp it can't pay for.
        var current = await ships.FindAsync(ship.Symbol, cancellationToken) ?? ship;
        if (!Warps.TryChooseMode(distance, current.FuelCurrent, keep, out mode))
        {
            return NoWay($"Ship {ship.Symbol} has {current.FuelCurrent} fuel, short of the warp to {destination}, {distance:0} away, fuel-safe.");
        }

        var goalId = (await goals.GetActiveGoalAsync(ship.Symbol, cancellationToken))?.GoalId ?? Guid.Empty;
        await flightMode.EnsureAsync(current, mode, cancellationToken);
        var sent = TimeProvider.System.GetUtcNow();
        WarpActionResult result;
        try
        {
            result = await warp.ExecuteAsync(ship.Symbol, destination, goalId, cancellationToken);
        }
        catch (WarpRefusedException refused) when (refused.ErrorCode == WarpRefusedException.InsufficientFuel
            && mode == TradeRoutePlanner.BurnMode
            && Warps.Fuel(TradeRoutePlanner.CruiseMode, distance) + keep <= current.FuelCurrent)
        {
            // A BURN warp's fuel isn't confirmed since API 2.1 (D104): the API's answer decides, and it warps in CRUISE.
            mode = TradeRoutePlanner.CruiseMode;
            await flightMode.EnsureAsync(await ships.FindAsync(ship.Symbol, cancellationToken) ?? current, mode, cancellationToken);
            sent = TimeProvider.System.GetUtcNow();
            try
            {
                result = await warp.ExecuteAsync(ship.Symbol, destination, goalId, cancellationToken);
            }
            catch (WarpRefusedException again)
            {
                return Refuse(ship, destination, again, sent);
            }
        }
        catch (WarpRefusedException refused)
        {
            return Refuse(ship, destination, refused, sent);
        }

        Measure(current, destination, mode, distance, result, sent, result.Nav.ArrivesAt ?? sent);
        return new JumpStep(JumpStepOutcome.Warped, GoalExecutionResult.WaitingForArrival($"Warping to {destination} in {mode}."));
    }

    private static JumpStep NoWay(string reason) => new(JumpStepOutcome.NoWay, GoalExecutionResult.Progressing(reason));

    /// <summary>The market of the ship's system nearest to it, where it can fill its tank before it warps; null where there is none.</summary>
    private static string? NearestMarket(WayChart chart, ShipModel ship)
    {
        var (x, y) = chart.TryGetWaypoint(ship.WaypointSymbol ?? string.Empty, out var at) ? (at.X, at.Y) : (0, 0);
        return chart.WaypointsOf(ship.SystemSymbol ?? string.Empty)
            .Where(waypoint => waypoint.Refuels)
            .OrderBy(waypoint => ((long)(waypoint.X - x) * (waypoint.X - x)) + ((long)(waypoint.Y - y) * (waypoint.Y - y)))
            .ThenBy(waypoint => waypoint.Symbol, StringComparer.Ordinal)
            .Select(waypoint => waypoint.Symbol)
            .FirstOrDefault();
    }

    private async Task<WayChart> ChartAsync(DateTimeOffset now, CancellationToken ct)
        => await WayChart.ReadAsync(await gates.ReadAsync(ct) ?? NoNetwork, systems, waypoints, refusals, now, ct);

    /// <summary>Records a warp the API refused: no warp goes to that system for an hour, and the caller's plan chooses again.</summary>
    private JumpStep Refuse(ShipModel ship, string destination, WarpRefusedException refused, DateTimeOffset at)
    {
        refusals.Record(WaypointSymbols.SystemOf(destination), at);
        logger.LogWarning(
            refused,
            "{EventKind:l}: ship {ShipSymbol} can't warp from {WaypointSymbol} to {Destination} ({Reason}, error {ErrorCode}); no ship warps to {SystemSymbol} for an hour.",
            JournalEvents.ShipBlocked,
            ship.Symbol,
            ship.WaypointSymbol ?? string.Empty,
            destination,
            RefusedReason,
            refused.ErrorCode,
            WaypointSymbols.SystemOf(destination));
        return new JumpStep(JumpStepOutcome.Refused, GoalExecutionResult.Blocked($"{RefusedReason}: {refused.Message}"));
    }

    /// <summary>
    /// Journals the warp with the fuel and the seconds it took, against those the research note reckons (D100: "then measure");
    /// a warp that differs logs a warning, for the note to take the API's numbers.
    /// </summary>
    private void Measure(ShipModel ship, string destination, string mode, double distance, WarpActionResult result, DateTimeOffset sent, DateTimeOffset arrival)
    {
        var fuel = ship.FuelCurrent - result.Fuel.Current;
        var seconds = Math.Round((arrival - sent).TotalSeconds);
        var reckonedFuel = Warps.Fuel(mode, distance);
        var reckonedSeconds = Warps.Seconds(mode, distance, FleetRoles.EngineSpeed(ship, TripTime.DefaultEngineSpeed));
        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} warps from {WaypointSymbol} to {Destination} in {FlightMode}, {Distance} apart: {Fuel} fuel and {Seconds} seconds, reckoned at {ReckonedFuel} and {ReckonedSeconds}.",
            JournalEvents.Warped,
            ship.Symbol,
            ship.WaypointSymbol ?? string.Empty,
            destination,
            mode,
            Math.Round(distance),
            fuel,
            seconds,
            reckonedFuel,
            reckonedSeconds);
        if (fuel != reckonedFuel || Math.Abs(seconds - reckonedSeconds) > SecondsTolerance)
        {
            logger.LogWarning(
                "GoalWarps: ship {ShipSymbol}'s warp to {Destination} in {FlightMode}, {Distance} apart, took {Fuel} fuel and {Seconds} seconds against the {ReckonedFuel} and {ReckonedSeconds} the research note reckons (PLAN.md slice 6.31): the note needs the API's numbers.",
                ship.Symbol,
                destination,
                mode,
                Math.Round(distance),
                fuel,
                seconds,
                reckonedFuel,
                reckonedSeconds);
        }
    }
}
