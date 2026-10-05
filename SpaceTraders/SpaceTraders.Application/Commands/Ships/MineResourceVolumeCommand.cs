using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

/// <summary>
/// Mines <see cref="TradeSymbol"/> at <see cref="SourceWaypoint"/> until the trip holds
/// <see cref="RequiredUnitsTotal"/> of it (at most a full hold): one extraction per call, with the best
/// survey of the waypoint for the good when there is one (slice 6.4), and other goods jettisoned, but for the
/// ores a mining trip keeps (<see cref="KeepOtherOres"/>, D71).
/// </summary>
public sealed record MineResourceVolumeCommand
{
    public required string ShipSymbol { get; init; }

    public required string TradeSymbol { get; init; }

    public required string SourceWaypoint { get; init; }

    public required int RequiredUnitsTotal { get; init; }

    /// <summary>
    /// Whether the other ores a market buys within one tank of the asteroid stay aboard (D71): the mining plan's trips,
    /// whose plan sells them on the trips after. Otherwise every other good is jettisoned: the contract's round trips.
    /// </summary>
    public bool KeepOtherOres { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public MineResourceVolumeCommand(string ShipSymbol, string TradeSymbol, string SourceWaypoint, int RequiredUnitsTotal)
    {
        this.ShipSymbol = ShipSymbol;
        this.TradeSymbol = TradeSymbol;
        this.SourceWaypoint = SourceWaypoint;
        this.RequiredUnitsTotal = RequiredUnitsTotal;
    }
}

public sealed class MineResourceVolumeHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IWaypointRepository waypoints,
    ISurveyRepository surveys,
    ISurveyKeeper surveyKeeper,
    ITradeContextReader tradeContexts,
    IRefuelSubCommand refuel,
    IOrbitSubCommand orbit,
    IDockSubCommand dock,
    IFlightModeSubCommand flightMode,
    INavigateSubCommand navigate,
    IMessageBus bus,
    IAutomationMetrics metrics,
    IGatheringRates rates,
    ILogger<MineResourceVolumeHandler> logger)
{
    public Task<ShipCommandResult> Handle(MineResourceVolumeCommand command, CancellationToken cancellationToken)
        => ExecuteAsync(command, cancellationToken);

    /// <summary>
    /// The units of the contract good one trip carries: what the contract still needs, at most a
    /// full hold. Mining stops there, and the tick sends the ship to deliver no sooner (B8).
    /// </summary>
    /// <param name="requiredUnitsTotal">The units the contract still needs.</param>
    /// <param name="cargoCapacity">The ship's cargo capacity; 0 when unknown.</param>
    /// <returns>The units to have aboard before delivering.</returns>
    public static int UnitsPerTrip(int requiredUnitsTotal, int cargoCapacity)
        => cargoCapacity > 0
            ? Math.Min(Math.Max(requiredUnitsTotal, 0), cargoCapacity)
            : Math.Max(requiredUnitsTotal, 0);

    public async Task<ShipCommandResult> ExecuteAsync(MineResourceVolumeCommand command, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            return ShipCommandResult.Rejected(command.ShipSymbol, ShipLocalStatus.None, string.Empty, string.Empty);
        }

        ship = await ApplyArrivalDeadReckoningIfDueAsync(ship, cancellationToken);

        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return ShipCommandResult.Rejected(
                ship.Symbol,
                ship.LocalStatus,
                ship.SystemSymbol ?? string.Empty,
                ship.WaypointSymbol ?? string.Empty);
        }

        var atSource = string.Equals(ship.WaypointSymbol, command.SourceWaypoint, StringComparison.OrdinalIgnoreCase);

        if (!atSource)
        {
            // The contract's flight to the asteroid, through refuelling stops (B47), in BURN where that strands nothing (D84).
            var map = (await tradeContexts.ReadAsync(ship.SystemSymbol ?? string.Empty, cancellationToken)).Map;
            if (CommandFlight.DocksToRefuel(map, ship, command.SourceWaypoint))
            {
                await dock.ExecuteAsync(ship.Symbol, cancellationToken);
                ship = await ships.FindAsync(ship.Symbol, cancellationToken) ?? ship;
            }

            if (ship.LocalStatus == ShipLocalStatus.Docked)
            {
                await TryRefuelBeforeUndockingAsync(ship, cancellationToken);
                await orbit.ExecuteAsync(ship.Symbol, cancellationToken);
                ship = await ships.FindAsync(ship.Symbol, cancellationToken) ?? ship;
            }

            if (ship.LocalStatus != ShipLocalStatus.InOrbit)
            {
                await bus.PublishMismatchAndTickAsync(
                    command.ShipSymbol,
                    nameof(MineResourceVolumeCommand),
                    "IN_ORBIT",
                    ship.Status ?? "UNKNOWN",
                    "Ship must be in orbit to navigate to source asteroid.");

                return ShipCommandResult.Rejected(
                    ship.Symbol,
                    ship.LocalStatus,
                    ship.SystemSymbol ?? string.Empty,
                    ship.WaypointSymbol ?? string.Empty);
            }

            await CommandFlight.TowardsAsync(map, ship, command.SourceWaypoint, flightMode, navigate, cancellationToken);

            return new ShipCommandResult(
                ship.Symbol,
                ShipLocalStatus.InTransit,
                ship.SystemSymbol ?? string.Empty,
                ship.WaypointSymbol ?? string.Empty,
                Accepted: true);
        }

        if (ship.LocalStatus == ShipLocalStatus.Docked)
        {
            await TryRefuelBeforeUndockingAsync(ship, cancellationToken);
            await orbit.ExecuteAsync(ship.Symbol, cancellationToken);
            ship = await ships.FindAsync(ship.Symbol, cancellationToken) ?? ship;
        }

        if (ship.LocalStatus != ShipLocalStatus.InOrbit)
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(MineResourceVolumeCommand),
                "IN_ORBIT",
                ship.Status ?? "UNKNOWN",
                "Ship must be in orbit before extraction.");

            return ShipCommandResult.Rejected(
                ship.Symbol,
                ship.LocalStatus,
                ship.SystemSymbol ?? string.Empty,
                ship.WaypointSymbol ?? string.Empty);
        }

        var now = TimeProvider.System.GetUtcNow();
        if (ship.CooldownExpiresAt.HasValue && ship.CooldownExpiresAt.Value > now)
        {
            return new ShipCommandResult(
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

        var cargoInventory = ship.CargoInventory ?? [];
        var targetUnitsInCargo = cargoInventory
            .FirstOrDefault(i => i.Symbol.Equals(command.TradeSymbol, StringComparison.OrdinalIgnoreCase))?
            .Units ?? 0;

        var maxTargetForTrip = UnitsPerTrip(command.RequiredUnitsTotal, ship.CargoCapacity);

        if (targetUnitsInCargo >= maxTargetForTrip || ship.CargoCurrent >= ship.CargoCapacity)
        {
            await JettisonWhatTheTripDoesNotKeepAsync(ship, cargoInventory, command, cancellationToken);

            return new ShipCommandResult(
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

        var waypoint = await waypoints.FindAsync(command.SourceWaypoint, cancellationToken);
        if (waypoint is not null && !AsteroidDeposits.IsExtractable(waypoint.Type))
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(MineResourceVolumeCommand),
                "ASTEROID, ASTEROID_FIELD or ENGINEERED_ASTEROID",
                waypoint.Type,
                $"Waypoint {command.SourceWaypoint} is type {waypoint.Type} which does not support resource extraction.");

            return ShipCommandResult.Rejected(
                ship.Symbol,
                ship.LocalStatus,
                ship.SystemSymbol ?? string.Empty,
                ship.WaypointSymbol ?? string.Empty);
        }

        // The best survey of the waypoint for the good, when there is one (slice 6.4).
        var stored = await surveys.GetActiveAsync(cancellationToken);
        var surveyed = SurveySelection.TryPickBest(stored.Select(s => s.Survey), command.SourceWaypoint, command.TradeSymbol, now, out var survey);

        ExtractionActionResult extractResult;
        try
        {
            extractResult = surveyed
                ? await port.ExtractWithSurveyAsync(ship.Symbol, survey, cancellationToken)
                : await port.ExtractResourcesAsync(ship.Symbol, cancellationToken);
        }
        catch (SurveyRefusedException refused)
        {
            // Exhausted, expired or not taken: the survey is dropped, and the next step extracts with
            // the next best one, or without.
            if (refused.ErrorCode == SurveyRefusedException.RejectedErrorCode)
            {
                // B51: the API couldn't read the survey; what it said is the only clue to why.
                logger.LogWarning(
                    refused,
                    "The API couldn't read survey {Signature} for ship {ShipSymbol}; it is dropped. The API said: {ApiResponse}",
                    refused.Signature,
                    ship.Symbol,
                    refused.Detail);
            }

            await surveyKeeper.RefusedAsync(refused, cancellationToken);
            return new ShipCommandResult(
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

        await ships.UpdateCargoAsync(ship.Symbol, extractResult.Cargo, cancellationToken);
        metrics.Extracted(ship.Symbol, extractResult.YieldSymbol, extractResult.YieldUnits);
        metrics.Extraction(ship.Symbol, surveyed);
        rates.Record(ship.Symbol, GatheringKind.Mining, extractResult.YieldUnits, extractResult.CooldownSeconds);
        if (surveyed)
        {
            await surveyKeeper.UsedAsync(survey.Signature, cancellationToken);
        }

        var cooldownAt = extractResult.CooldownExpiresAt ?? now.AddSeconds(extractResult.CooldownSeconds);
        await ships.UpdateCooldownAsync(ship.Symbol, cooldownAt, cancellationToken);

        var updatedShip = await ships.FindAsync(ship.Symbol, cancellationToken) ?? ship;
        await JettisonWhatTheTripDoesNotKeepAsync(ship, updatedShip.CargoInventory ?? [], command, cancellationToken);

        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} extracted {Units} {TradeSymbol} at {WaypointSymbol}, mining for {Target}, with survey {Signature}.",
            JournalEvents.Extracted,
            ship.Symbol,
            extractResult.YieldUnits,
            extractResult.YieldSymbol,
            command.SourceWaypoint,
            command.TradeSymbol,
            surveyed ? survey.Signature : string.Empty);

        return new ShipCommandResult(
            ship.Symbol,
            ShipLocalStatus.InOrbit,
            ship.SystemSymbol ?? string.Empty,
            ship.WaypointSymbol ?? string.Empty,
            FuelCurrent: ship.FuelCurrent,
            FuelCapacity: ship.FuelCapacity,
            CargoCurrent: extractResult.Cargo.Units,
            CargoCapacity: extractResult.Cargo.Capacity,
            Accepted: true);
    }

    private async Task TryRefuelBeforeUndockingAsync(ShipModel ship, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ship.WaypointSymbol)
            || ship.FuelCapacity <= 0
            || ship.FuelCurrent >= ship.FuelCapacity)
        {
            return;
        }

        var waypoint = await waypoints.FindAsync(ship.WaypointSymbol, cancellationToken);
        if (waypoint?.HasMarket != true)
        {
            return;
        }

        await refuel.ExecuteAsync(ship.Symbol, fromCargo: false, cancellationToken);
    }

    private async Task<ShipModel> ApplyArrivalDeadReckoningIfDueAsync(ShipModel ship, CancellationToken cancellationToken)
    {
        if (ship.LocalStatus != ShipLocalStatus.InTransit
            || !ship.ArrivesAt.HasValue
            || ship.ArrivesAt.Value > TimeProvider.System.GetUtcNow())
        {
            return ship;
        }

        var waypoint = ship.DestWaypointSymbol ?? ship.WaypointSymbol ?? string.Empty;
        var nav = new NavModel(
            Status: "IN_ORBIT",
            SystemSymbol: ship.SystemSymbol ?? string.Empty,
            WaypointSymbol: waypoint,
            FlightMode: ship.FlightMode ?? "CRUISE",
            DestWaypointSymbol: null,
            ArrivesAt: null);

        await ships.UpdateNavAsync(ship.Symbol, nav, null, cancellationToken);
        return await ships.FindAsync(ship.Symbol, cancellationToken) ?? ship;
    }

    /// <summary>
    /// Jettisons every good but the one mined for, unless the trip keeps it: a mining trip keeps the other ores a market buys
    /// within one tank of the asteroid (D71, <see cref="MiningPlanner.IsSellableWithinOneTank"/>), which the mining plan sells
    /// on the trips after. A contract round trip keeps only the contract's ore.
    /// </summary>
    private async Task JettisonWhatTheTripDoesNotKeepAsync(
        ShipModel ship,
        IReadOnlyList<CargoItemModel> cargo,
        MineResourceVolumeCommand command,
        CancellationToken cancellationToken)
    {
        var others = cargo.Where(i => i.Units > 0 && !i.Symbol.Equals(command.TradeSymbol, StringComparison.OrdinalIgnoreCase)).ToList();
        if (others.Count == 0)
        {
            return;
        }

        var map = command.KeepOtherOres
            ? (await tradeContexts.ReadAsync(ship.SystemSymbol ?? string.Empty, cancellationToken)).Map
            : null;
        foreach (var item in others.Where(item => map is null || !MiningPlanner.IsSellableWithinOneTank(map, ship, command.SourceWaypoint, item.Symbol)))
        {
            var result = await port.JettisonCargoAsync(ship.Symbol, item.Symbol, item.Units, cancellationToken);
            await ships.UpdateCargoAsync(ship.Symbol, result.Cargo, cancellationToken);
            metrics.Jettisoned(ship.Symbol, item.Symbol, item.Units);
        }
    }
}
