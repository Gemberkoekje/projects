using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Executor for <see cref="JumpGoal"/> (exploring, asked on 2026-10-04): the ship flies to the gate of the system it is in,
/// and jumps from there to the destination gate, which buys one ANTIMATTER at the gate's market.
/// <list type="bullet">
///   <item>It jumps only while the credits after the jump stay at or above the floor every ship purchase keeps
///   (<see cref="CreditReserve.FloorSetting"/>, 60,000 seeded). The explore plan gives the goal only then; should the
///   price have risen since, the goal ends, and the plan holds the jump until the credits allow it.</item>
///   <item>It waits out the ship's cooldown: the API refuses a jump during one.</item>
///   <item>Docked at a gate whose market sells fuel, it fills the tank first, for the system it goes to.</item>
///   <item>A jump the API refuses (<see cref="JumpRefusedException"/>) blocks the goal with <see cref="RefusedReason"/>: the
///   explore plan then leaves that gate alone for a while and chooses again.</item>
/// </list>
/// The goal ends in the destination's system.
/// </summary>
public sealed class JumpGoalExecutor(
    ISpaceTradersPort port,
    IShipRepository ships,
    IShipGoalRepository goals,
    IAgentRepository agents,
    IMarketRepository markets,
    IMarketRefresher marketRefresher,
    ISettingsRepository settings,
    ITradeContextReader tradeContexts,
    IDockSubCommand dock,
    IOrbitSubCommand orbit,
    IRefuelSubCommand refuel,
    IMessageBus bus,
    ILogger<JumpGoalExecutor> logger) : IShipGoalExecutor
{
    /// <summary>The <see cref="ShipGoal.StatusReason"/> of a jump the API refused.</summary>
    public const string RefusedReason = "jump_refused";

    private const string Antimatter = "ANTIMATTER";

    /// <inheritdoc />
    public bool CanExecute(ShipGoal goal) => goal is JumpGoal;

    /// <inheritdoc />
    public async Task<GoalExecutionResult> ExecuteStepAsync(ShipModel ship, ShipGoal goal, ShipGoalContext ctx, CancellationToken ct)
    {
        var jump = (JumpGoal)goal;
        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return GoalExecutionResult.WaitingForArrival($"In transit to {jump.GateWaypointSymbol}.");
        }

        var destinationSystem = WaypointSymbols.SystemOf(jump.DestinationGateWaypointSymbol);
        if (string.Equals(ship.SystemSymbol, destinationSystem, StringComparison.OrdinalIgnoreCase))
        {
            await goals.ClearActiveGoalAsync(ship.Symbol, ct);
            return GoalExecutionResult.Completed($"In {destinationSystem}.");
        }

        var context = await tradeContexts.ReadAsync(ship.SystemSymbol ?? string.Empty, ct);
        if (!string.Equals(ship.WaypointSymbol, jump.GateWaypointSymbol, StringComparison.OrdinalIgnoreCase))
        {
            return await GoalFlight.TowardsAsync(context.Map, ship, jump.GateWaypointSymbol, dock, bus, ct);
        }

        var now = TimeProvider.System.GetUtcNow();
        if (ship.CooldownExpiresAt is { } cooldown && cooldown > now)
        {
            return GoalExecutionResult.WaitingForCooldown($"Waiting for the cooldown before jumping to {jump.DestinationGateWaypointSymbol}.", cooldown);
        }

        var price = await AntimatterPriceAsync(ship, jump.GateWaypointSymbol, ct);
        var credits = (await agents.GetAsync(ct))?.Credits ?? 0;
        var floor = Math.Max(0, await settings.GetAsync<long>(CreditReserve.FloorSetting, ct));
        if (credits - price < floor)
        {
            // A goal that waited here would look stuck (ShipStuck): the explore plan holds the jump instead, and says so.
            await goals.ClearActiveGoalAsync(ship.Symbol, ct);
            logger.LogDebug(
                "JumpGoalExecutor: ship {ShipSymbol} doesn't jump to {Destination}: the antimatter costs {Price}, and the jump must leave {Floor} of the {Credits} credits.",
                ship.Symbol,
                jump.DestinationGateWaypointSymbol,
                price,
                floor,
                credits);
            return GoalExecutionResult.Progressing($"Not enough credits to jump to {jump.DestinationGateWaypointSymbol}; the explore plan waits for them.");
        }

        if (ship.LocalStatus == ShipLocalStatus.Docked)
        {
            if (ship.FuelCapacity > 0 && ship.FuelCurrent < ship.FuelCapacity && context.Map.SellsFuel(jump.GateWaypointSymbol))
            {
                await refuel.ExecuteAsync(ship.Symbol, fromCargo: false, ct);
            }

            await orbit.ExecuteAsync(ship.Symbol, ct);
        }

        JumpActionResult result;
        try
        {
            result = await port.JumpShipAsync(ship.Symbol, jump.DestinationGateWaypointSymbol, ct);
        }
        catch (JumpRefusedException refused)
        {
            // Sent again, the jump would be refused again on every step: the explore plan chooses again.
            await goals.BlockGoalAsync(ship.Symbol, jump.GoalId, RefusedReason, ct);
            logger.LogWarning(
                refused,
                "{EventKind:l}: ship {ShipSymbol} can't jump from {WaypointSymbol} to {Destination} ({Reason}); the explore plan leaves that gate alone for an hour.",
                JournalEvents.ShipBlocked,
                ship.Symbol,
                jump.GateWaypointSymbol,
                jump.DestinationGateWaypointSymbol,
                RefusedReason);
            return GoalExecutionResult.Blocked($"{RefusedReason}: {refused.Message}");
        }

        await ships.UpdateNavAsync(ship.Symbol, result.Nav, null, ct);
        await ships.UpdateCooldownAsync(ship.Symbol, result.CooldownExpiresAt ?? now.AddSeconds(result.CooldownSeconds), ct);
        if (result.AgentCredits is { } after)
        {
            await agents.SetCreditsAsync(bus, after, ct);
        }

        await bus.PublishAsync(new ShipJumpedEvent(ship.Symbol, jump.GateWaypointSymbol, jump.DestinationGateWaypointSymbol, result.Cost));
        await goals.ClearActiveGoalAsync(ship.Symbol, ct);
        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} jumped from {WaypointSymbol} to {Destination} in {SystemSymbol}; the antimatter cost {Cost}.",
            JournalEvents.Jumped,
            ship.Symbol,
            jump.GateWaypointSymbol,
            jump.DestinationGateWaypointSymbol,
            result.Nav.SystemSymbol,
            result.Cost);
        return GoalExecutionResult.Completed($"Jumped to {jump.DestinationGateWaypointSymbol}.");
    }

    /// <summary>
    /// What a unit of ANTIMATTER costs at the gate's market, as last seen; a gate whose market was never seen is fetched
    /// once, the ship being there. 0 when its market doesn't list it: the API then says what a jump costs, if anything.
    /// </summary>
    private async Task<long> AntimatterPriceAsync(ShipModel ship, string gate, CancellationToken ct)
    {
        var market = await markets.FindSnapshotByWaypointAsync(gate, ct);
        if (market is null)
        {
            try
            {
                await marketRefresher.RefreshAsync(ship.SystemSymbol ?? WaypointSymbols.SystemOf(gate), gate, ct);
                market = await markets.FindSnapshotByWaypointAsync(gate, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogDebug(ex, "JumpGoalExecutor: couldn't fetch the market at {WaypointSymbol} for the antimatter's price.", gate);
            }
        }

        return market?.TradeGoods
            .FirstOrDefault(good => good.Symbol.Equals(Antimatter, StringComparison.OrdinalIgnoreCase))?
            .PurchasePrice ?? 0;
    }
}
