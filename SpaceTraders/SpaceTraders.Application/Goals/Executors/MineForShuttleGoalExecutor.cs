using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Executor for <see cref="MineForShuttleGoal"/>: a drone parked at a far asteroid (PLAN.md slice 6.18, D83). It gets there
/// once, drifting to the market first when that is out of its CRUISE reach (D45), and filling its tank there. At the
/// asteroid it hands what it holds to a collecting shuttle in orbit there (<see cref="CollectOreGoal"/>), as much as the
/// shuttle has room for, one transfer per good; it extracts once per cooldown while its hold has room, with the best survey
/// for its ore (<see cref="MineResourceVolumeCommand"/>, keeping the ores a market buys within one tank, D71); and with its
/// hold full and no shuttle with room there, it waits, making no API call. The goal doesn't end on its own: the mining plan
/// ends it when the asteroid's collection closes.
/// </summary>
/// <remarks>
/// The API answers a transfer with the transferring ship's hold only, so the shuttle's cached hold is what it held and what
/// it was handed. A transfer the API refuses (the shuttle has less room than the cache says, or has just left) is logged at
/// Warning, and the shuttle's hold is fetched again.
/// </remarks>
public sealed class MineForShuttleGoalExecutor(
    IShipRepository ships,
    IShipGoalRepository goals,
    ISpaceTradersPort port,
    ITradeContextReader tradeContexts,
    IDockSubCommand dock,
    IOrbitSubCommand orbit,
    IMessageBus bus,
    ILogger<MineForShuttleGoalExecutor> logger) : IShipGoalExecutor
{
    /// <summary>How long a drone with a full hold and no shuttle waits before it looks again: the tick steps it anyway.</summary>
    internal static readonly TimeSpan WaitForShuttle = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public bool CanExecute(ShipGoal goal) => goal is MineForShuttleGoal;

    /// <inheritdoc />
    public async Task<GoalExecutionResult> ExecuteStepAsync(
        ShipModel ship,
        ShipGoal goal,
        ShipGoalContext ctx,
        CancellationToken ct)
    {
        var job = (MineForShuttleGoal)goal;

        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return GoalExecutionResult.WaitingForArrival("Drone is in transit to its collection point.");
        }

        if (job.Drifting)
        {
            return await DriftStepAsync(ship, job, ct);
        }

        if (!IsAt(ship, job.AsteroidWaypointSymbol))
        {
            var context = await tradeContexts.ReadAsync(ship.SystemSymbol ?? string.Empty, ct);
            return await GoalFlight.TowardsAsync(context.Map, ship, job.AsteroidWaypointSymbol, dock, bus, ct);
        }

        // A transfer needs both ships in the same state: the shuttle waits in orbit, where the drone extracts.
        if (ship.LocalStatus == ShipLocalStatus.Docked)
        {
            await orbit.ExecuteAsync(ship.Symbol, ct);
            return GoalExecutionResult.Progressing($"Orbiting {job.AsteroidWaypointSymbol} to mine for the shuttle.");
        }

        if (ship.CargoCurrent > 0 && await FindShuttleAsync(job, ct) is { } shuttle)
        {
            return await HandOverAsync(ship, shuttle, job, ct);
        }

        var now = TimeProvider.System.GetUtcNow();
        if (ship.CargoCapacity > 0 && ship.CargoCurrent >= ship.CargoCapacity)
        {
            return GoalExecutionResult.WaitingForCooldown(
                $"Hold full; waiting for the shuttle at {job.AsteroidWaypointSymbol}.",
                now + WaitForShuttle);
        }

        if (ship.CooldownExpiresAt.HasValue && ship.CooldownExpiresAt.Value > now)
        {
            return GoalExecutionResult.WaitingForCooldown("Waiting for extraction cooldown.", ship.CooldownExpiresAt);
        }

        var mined = await bus.InvokeAsync<ShipCommandResult>(
            new MineResourceVolumeCommand(ship.Symbol, job.TradeSymbol, job.AsteroidWaypointSymbol, Math.Max(1, ship.CargoCapacity)) { KeepOtherOres = true },
            ct);
        if (mined is null || !mined.Accepted)
        {
            // The mining plan gives the drone its place again on the next tick; one that keeps failing shows as RepeatingError.
            logger.LogWarning(
                "MineForShuttleGoalExecutor: ship {ShipSymbol} can't mine at {WaypointSymbol} (state {Status}); its place at the collection point is given up.",
                ship.Symbol,
                job.AsteroidWaypointSymbol,
                mined?.Status ?? ShipLocalStatus.None);
            await goals.ClearActiveGoalAsync(ship.Symbol, ct);
            return GoalExecutionResult.Blocked($"Mining rejected at {job.AsteroidWaypointSymbol}.");
        }

        return GoalExecutionResult.Progressing($"Mining at {job.AsteroidWaypointSymbol} for the shuttle.");
    }

    /// <summary>The drone's drift to the market, out of its CRUISE reach (D45); from there it flies on to the asteroid in CRUISE.</summary>
    private async Task<GoalExecutionResult> DriftStepAsync(ShipModel ship, MineForShuttleGoal job, CancellationToken ct)
    {
        if (!IsAt(ship, job.SellWaypointSymbol))
        {
            var drifting = await GoalFlight.DriftAsync(ship, job.SellWaypointSymbol, bus, ct);
            logger.LogInformation(
                "{EventKind:l}: ship {ShipSymbol} drifts from {WaypointSymbol} to {SellWaypoint}, out of its CRUISE reach, to mine {TradeSymbol} at {SourceWaypoint} for the shuttle there.",
                JournalEvents.DriftStarted,
                ship.Symbol,
                ship.WaypointSymbol ?? string.Empty,
                job.SellWaypointSymbol,
                job.TradeSymbol,
                job.AsteroidWaypointSymbol);
            return drifting;
        }

        await goals.SetActiveGoalAsync(ship.Symbol, job with { Drifting = false }, ct);
        return GoalExecutionResult.Progressing($"At {job.SellWaypointSymbol}: flying on to {job.AsteroidWaypointSymbol} in CRUISE.");
    }

    /// <summary>A shuttle collecting at the drone's asteroid now: there, in orbit, with room, and not on its way to sell.</summary>
    private async Task<ShipModel?> FindShuttleAsync(MineForShuttleGoal job, CancellationToken ct)
    {
        foreach (var candidate in (await ships.GetAllAsync(ct))
            .Where(other => IsAt(other, job.AsteroidWaypointSymbol)
                && other.LocalStatus == ShipLocalStatus.InOrbit
                && other.CargoCapacity > other.CargoCurrent)
            .OrderBy(other => other.Symbol, StringComparer.Ordinal))
        {
            if (await goals.GetActiveGoalAsync(candidate.Symbol, ct) is CollectOreGoal round
                && !round.Selling
                && round.AsteroidWaypointSymbol.Equals(job.AsteroidWaypointSymbol, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Hands the drone's hold to the shuttle, one good a transfer, as much as the shuttle has room for.</summary>
    private async Task<GoalExecutionResult> HandOverAsync(ShipModel drone, ShipModel shuttle, MineForShuttleGoal job, CancellationToken ct)
    {
        var room = shuttle.CargoCapacity - shuttle.CargoCurrent;
        List<CargoItemModel> held = [.. (shuttle.CargoInventory ?? []).Where(item => item.Units > 0)];
        var handed = 0;
        foreach (var item in (drone.CargoInventory ?? []).Where(item => item.Units > 0).ToList())
        {
            var units = Math.Min(item.Units, room - handed);
            if (units <= 0)
            {
                break;
            }

            try
            {
                var left = await port.TransferCargoAsync(drone.Symbol, shuttle.Symbol, item.Symbol, units, ct);
                await ships.UpdateCargoAsync(drone.Symbol, left, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(
                    ex,
                    "Couldn't hand {Units} {TradeSymbol} of ship {ShipSymbol} to shuttle {TargetShipSymbol} at {WaypointSymbol}; the shuttle's hold is fetched again.",
                    units,
                    item.Symbol,
                    drone.Symbol,
                    shuttle.Symbol,
                    job.AsteroidWaypointSymbol);
                await ships.UpdateCargoAsync(shuttle.Symbol, await port.GetShipCargoAsync(shuttle.Symbol, ct), ct);
                return GoalExecutionResult.Progressing($"The transfer to {shuttle.Symbol} was refused; looking again.");
            }

            // The API answers with the drone's hold only: the shuttle holds what it held and what it was handed.
            held = Added(held, item.Symbol, units);
            await ships.UpdateCargoAsync(shuttle.Symbol, new CargoModel(held.Sum(cargo => cargo.Units), shuttle.CargoCapacity, held), ct);
            handed += units;
            logger.LogInformation(
                "{EventKind:l}: ship {ShipSymbol} handed {Units} {TradeSymbol} to shuttle {TargetShipSymbol} at {WaypointSymbol}.",
                JournalEvents.CargoTransferred,
                drone.Symbol,
                units,
                item.Symbol,
                shuttle.Symbol,
                job.AsteroidWaypointSymbol);
        }

        return GoalExecutionResult.Progressing($"Handed {handed} units to {shuttle.Symbol} at {job.AsteroidWaypointSymbol}.");
    }

    private static List<CargoItemModel> Added(IReadOnlyList<CargoItemModel> hold, string tradeSymbol, int units)
    {
        var known = hold.Any(item => item.Symbol.Equals(tradeSymbol, StringComparison.OrdinalIgnoreCase));
        return known
            ? [.. hold.Select(item => item.Symbol.Equals(tradeSymbol, StringComparison.OrdinalIgnoreCase) ? item with { Units = item.Units + units } : item)]
            : [.. hold, new CargoItemModel(tradeSymbol, units)];
    }

    private static bool IsAt(ShipModel ship, string waypointSymbol)
        => string.Equals(ship.WaypointSymbol, waypointSymbol, StringComparison.OrdinalIgnoreCase);
}
