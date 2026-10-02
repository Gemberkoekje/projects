using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.SpareTime;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Automation;

/// <summary>The spare-time plan (PLAN.md slice 6.8).</summary>
public interface ISpareTimePlanService
{
    /// <summary>One pass of the plan: gives every ship that gathers in its spare time, and has nothing else to do, a trip.</summary>
    /// <param name="cancellationToken">Stops the pass.</param>
    /// <returns>A task that completes when the pass is done.</returns>
    Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The spare-time plan (PLAN.md slice 6.8), asked for on 2026-10-02: "I'd like my command ship not to be idle." It is
/// bootstrapped last, so it gets only the ships every other plan left free: a ship that gathers in its spare time
/// (<see cref="FleetRoles.GathersInSpareTime"/>, the command ship while the survey plan is on) with nothing to survey
/// and no trade (D34). Each tick every such free ship gets one trip (<see cref="GatherAndSellGoal"/>):
/// <list type="bullet">
///   <item>at the nearest asteroid or gas giant where it can mine or siphon something a market buys (D35,
///   <see cref="GatherPlanner.TryFindSource"/>): it keeps whatever sells, without surveys, which stay for the
///   drones;</item>
///   <item>with a full hold it sells each good where it fetches most after fuel (D36), and the trip ends; the plans
///   choose again.</item>
/// </list>
/// A survey takes the ship off its trip at once, with the hold aboard (D37); a trade makes it sell its hold first (D34):
/// the survey and trading plans, bootstrapped earlier, do that. Its state lists the ships and what they do, for the
/// <c>ShipLeftIdle</c> rule (D13), and is written only when it changes.
/// </summary>
public sealed class SpareTimePlanService(
    IShipRepository ships,
    IShipGoalRepository goals,
    IShipAssignmentRepository assignments,
    ITradeContextReader tradeContexts,
    IPlanRepository plans,
    ISettingsRepository settings,
    ILogger<SpareTimePlanService> logger) : ISpareTimePlanService
{
    private static readonly JsonSerializerOptions CompareOptions = new();

    /// <inheritdoc />
    public async Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default)
    {
        var surveyOn = await settings.IsPlanEnabledAsync(AutomationPlan.Survey, cancellationToken);
        var fleet = await ships.GetAllAsync(cancellationToken);
        var withAssignment = (await assignments.GetAllActiveAsync(cancellationToken))
            .Where(assignment => !assignment.CompletedAt.HasValue)
            .Select(assignment => assignment.ShipSymbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var maps = new Dictionary<string, TradeMarketMap>(StringComparer.OrdinalIgnoreCase);
        var states = new List<SpareTimeShipState>();
        foreach (var ship in fleet.Where(ship => FleetRoles.GathersInSpareTime(ship, surveyOn)))
        {
            var goal = await goals.GetActiveGoalAsync(ship.Symbol, cancellationToken);
            if (goal is GatherAndSellGoal { Status: not GoalStatus.Blocked and not GoalStatus.Completed } trip)
            {
                states.Add(new SpareTimeShipState
                {
                    ShipSymbol = ship.Symbol,
                    Activity = trip.Selling ? SpareTimeActivity.Selling : SpareTimeActivity.Gathering,
                    SourceWaypointSymbol = trip.SourceWaypointSymbol,
                });
            }
            else if (!FleetRoles.IsFree(ship, goal, withAssignment.Contains(ship.Symbol)))
            {
                states.Add(new SpareTimeShipState { ShipSymbol = ship.Symbol, Activity = SpareTimeActivity.Busy });
            }
            else
            {
                var systemSymbol = ship.SystemSymbol ?? string.Empty;
                if (!maps.TryGetValue(systemSymbol, out var map))
                {
                    map = (await tradeContexts.ReadAsync(systemSymbol, cancellationToken)).Map;
                    maps[systemSymbol] = map;
                }

                states.Add(await GiveTripAsync(map, ship, cancellationToken));
            }
        }

        await SaveStateAsync(states, cancellationToken);
    }

    /// <summary>Gives a free ship its trip, at the nearest place it can gather and sell (D35).</summary>
    private async Task<SpareTimeShipState> GiveTripAsync(TradeMarketMap map, ShipModel ship, CancellationToken cancellationToken)
    {
        // A trip would turn to selling at once, find nothing to sell and end, on every tick.
        if (ship.CargoCapacity > 0
            && ship.CargoCurrent >= ship.CargoCapacity
            && !TradeRoutePlanner.TryFindBestCargoSale(map, ship, mustSell: true, out _, out _))
        {
            logger.LogDebug("Spare-time plan: ship {ShipSymbol} has a full hold that no market it can reach buys.", ship.Symbol);
            return new SpareTimeShipState { ShipSymbol = ship.Symbol, Activity = SpareTimeActivity.Waiting };
        }

        if (!GatherPlanner.TryFindSource(map, ship, out var source))
        {
            logger.LogDebug("Spare-time plan: nowhere ship {ShipSymbol} can reach yields anything a market buys.", ship.Symbol);
            return new SpareTimeShipState { ShipSymbol = ship.Symbol, Activity = SpareTimeActivity.Waiting };
        }

        var trip = new GatherAndSellGoal { SourceWaypointSymbol = source.WaypointSymbol, Siphoning = source.Siphoning };
        await goals.SetActiveGoalAsync(ship.Symbol, trip, cancellationToken);
        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} has nothing to survey or trade, so it {Method} whatever {WaypointSymbol} yields and sells it where each good fetches most.",
            JournalEvents.GatheringStarted,
            ship.Symbol,
            source.Siphoning ? "siphons" : "mines",
            source.WaypointSymbol);
        return new SpareTimeShipState { ShipSymbol = ship.Symbol, Activity = SpareTimeActivity.Gathering, SourceWaypointSymbol = source.WaypointSymbol };
    }

    /// <summary>Records the ships and what they do. Only a change is written: the tick runs every 5 seconds.</summary>
    private async Task SaveStateAsync(IReadOnlyList<SpareTimeShipState> states, CancellationToken cancellationToken)
    {
        var now = TimeProvider.System.GetUtcNow();
        var existing = await plans.GetAsync<SpareTimePlanState>(PlanTypes.SpareTime, cancellationToken);
        List<SpareTimeShipState> ordered = [.. states.OrderBy(state => state.ShipSymbol, StringComparer.Ordinal)];
        if (existing is not null
            && JsonSerializer.Serialize(existing.Ships, CompareOptions) == JsonSerializer.Serialize(ordered, CompareOptions))
        {
            return;
        }

        await plans.UpsertAsync(
            PlanTypes.SpareTime,
            new SpareTimePlanState
            {
                PlanId = existing?.PlanId ?? Guid.NewGuid(),
                Ships = ordered,
                CreatedAt = existing?.CreatedAt ?? now,
                UpdatedAt = now,
            },
            cancellationToken);
    }
}
