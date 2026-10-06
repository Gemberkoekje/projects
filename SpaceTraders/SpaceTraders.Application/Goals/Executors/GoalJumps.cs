using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Exploring;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>What a step of a flight between systems did (PLAN.md slice 6.28, D101).</summary>
public enum JumpStepOutcome
{
    /// <summary>Not judged.</summary>
    None = 0,

    /// <summary>The ship flies a leg towards its system's gate, or docks to refuel before it does (<see cref="GoalFlight"/>).</summary>
    Flying = 1,

    /// <summary>The ship jumped to the next gate.</summary>
    Jumped = 2,

    /// <summary>The ship is at the gate and waits out its cooldown: the API refuses a jump during one.</summary>
    WaitingForCooldown = 3,

    /// <summary>The antimatter would leave less than the credit floor (D63): the ship doesn't jump.</summary>
    ShortOfCredits = 4,

    /// <summary>The API refused the jump: no way goes through that gate for an hour (<see cref="JumpRefusals"/>).</summary>
    Refused = 5,

    /// <summary>No way through built gates is known to the destination's system.</summary>
    NoWay = 6,
}

/// <summary>One step of a flight between systems: what it did, and the outcome the executor's step returns.</summary>
public sealed record JumpStep
{
    /// <summary>Creates a step.</summary>
    /// <param name="Outcome">What the step did.</param>
    /// <param name="Result">The outcome for the goal's step.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public JumpStep(JumpStepOutcome Outcome, GoalExecutionResult Result)
    {
        this.Outcome = Outcome;
        this.Result = Result;
    }

    /// <summary>What the step did.</summary>
    public required JumpStepOutcome Outcome { get; init; }

    /// <summary>The outcome for the goal's step.</summary>
    public required GoalExecutionResult Result { get; init; }
}

/// <summary>
/// How a ship gets to another system (PLAN.md slice 6.28, D101: "One planner for every way"): through the built jump gates
/// the explore plan knows, the fewest jumps first (<see cref="ExploreAtlas.TryFindJumps"/>). In each system it flies to the
/// gate as every flight flies (<see cref="GoalFlight"/>, D84), and at the gate it jumps, as the command ship's exploring did
/// (the jump goal's steps, moved here so that every executor jumps alike):
/// <list type="bullet">
///   <item>only while the credits after the antimatter, one unit at the gate's market, stay at or above the floor every ship
///   purchase keeps (<see cref="CreditReserve.FloorSetting"/>, D63);</item>
///   <item>after the ship's cooldown: the API refuses a jump during one, and a jump starts one;</item>
///   <item>docked at a gate whose market sells fuel, it fills the tank first, for the system it goes to;</item>
///   <item>a jump the API refuses leaves that gate alone for an hour (<see cref="JumpRefusals"/>).</item>
/// </list>
/// The jump is booked as the antimatter's purchase (<see cref="ShipJumpedEvent"/>) and journalled (<c>Jumped</c>). What a
/// step that can't go on does to the goal is the executor's to say.
/// </summary>
public sealed class GoalJumps(
    ISpaceTradersPort port,
    IShipRepository ships,
    IAgentRepository agents,
    IMarketRepository markets,
    IMarketRefresher marketRefresher,
    ISettingsRepository settings,
    IGateNetwork gates,
    JumpRefusals refusals,
    ITradeContextReader tradeContexts,
    IDockSubCommand dock,
    IOrbitSubCommand orbit,
    IRefuelSubCommand refuel,
    IMessageBus bus,
    ILogger<GoalJumps> logger)
{
    /// <summary>The <see cref="Domain.Goals.ShipGoal.StatusReason"/> of a goal whose jump the API refused.</summary>
    public const string RefusedReason = "jump_refused";

    private const string Antimatter = "ANTIMATTER";

    /// <summary>
    /// Takes the ship one step towards a waypoint in another system: a leg towards its system's gate, or at the gate, the jump
    /// to the next one.
    /// </summary>
    /// <param name="ship">The ship, where it is now.</param>
    /// <param name="destination">The waypoint it is going to, in another system.</param>
    /// <param name="ct">Stops the work.</param>
    /// <returns>What the step did.</returns>
    public async Task<JumpStep> TowardsAsync(ShipModel ship, string destination, CancellationToken ct)
    {
        var here = ship.SystemSymbol ?? string.Empty;
        var system = WaypointSymbols.SystemOf(destination);
        var network = await gates.ReadAsync(ct);
        if (network is null
            || !ExploreAtlas.TryFindJumps(network, here, system, TimeProvider.System.GetUtcNow(), out var jumps)
            || jumps.Count == 0)
        {
            return new JumpStep(JumpStepOutcome.NoWay, GoalExecutionResult.Progressing($"No way through built gates is known from {here} to {system}."));
        }

        var next = jumps[0];
        if (!string.Equals(ship.WaypointSymbol, next.GateWaypointSymbol, StringComparison.OrdinalIgnoreCase))
        {
            var context = await tradeContexts.ReadAsync(here, ct);
            return new JumpStep(JumpStepOutcome.Flying, await GoalFlight.TowardsAsync(context.Map, ship, next.GateWaypointSymbol, dock, bus, ct));
        }

        return await JumpAsync(ship, next.GateWaypointSymbol, next.DestinationGateWaypointSymbol, ct);
    }

    /// <summary>The jump of a ship at <paramref name="gate"/> to <paramref name="destinationGate"/>, once the cooldown and the credits allow it.</summary>
    /// <param name="ship">The ship, at the gate.</param>
    /// <param name="gate">The gate it is at.</param>
    /// <param name="destinationGate">A gate that gate connects to.</param>
    /// <param name="ct">Stops the work.</param>
    /// <returns>What the step did.</returns>
    public async Task<JumpStep> JumpAsync(ShipModel ship, string gate, string destinationGate, CancellationToken ct)
    {
        var now = TimeProvider.System.GetUtcNow();
        if (ship.CooldownExpiresAt is { } cooldown && cooldown > now)
        {
            return new JumpStep(
                JumpStepOutcome.WaitingForCooldown,
                GoalExecutionResult.WaitingForCooldown($"Waiting for the cooldown before jumping to {destinationGate}.", cooldown));
        }

        var price = await AntimatterPriceAsync(ship, gate, ct);
        var credits = (await agents.GetAsync(ct))?.Credits ?? 0;
        var floor = Math.Max(0, await settings.GetAsync<long>(CreditReserve.FloorSetting, ct));
        if (credits - price < floor)
        {
            logger.LogDebug(
                "GoalJumps: ship {ShipSymbol} doesn't jump to {Destination}: the antimatter costs {Price}, and the jump must leave {Floor} of the {Credits} credits.",
                ship.Symbol,
                destinationGate,
                price,
                floor,
                credits);
            return new JumpStep(JumpStepOutcome.ShortOfCredits, GoalExecutionResult.Progressing($"Not enough credits to jump to {destinationGate}."));
        }

        if (ship.LocalStatus == ShipLocalStatus.Docked)
        {
            if (ship.FuelCapacity > 0
                && ship.FuelCurrent < ship.FuelCapacity
                && (await tradeContexts.ReadAsync(ship.SystemSymbol ?? WaypointSymbols.SystemOf(gate), ct)).Map.SellsFuel(gate))
            {
                await refuel.ExecuteAsync(ship.Symbol, fromCargo: false, ct);
            }

            await orbit.ExecuteAsync(ship.Symbol, ct);
        }

        JumpActionResult result;
        try
        {
            result = await port.JumpShipAsync(ship.Symbol, destinationGate, ct);
        }
        catch (JumpRefusedException refused)
        {
            // Sent again, the jump would be refused again on every step: no way goes through that gate for a while.
            refusals.Record(destinationGate, now);
            logger.LogWarning(
                refused,
                "{EventKind:l}: ship {ShipSymbol} can't jump from {WaypointSymbol} to {Destination} ({Reason}); no ship is sent through that gate for an hour.",
                JournalEvents.ShipBlocked,
                ship.Symbol,
                gate,
                destinationGate,
                RefusedReason);
            return new JumpStep(JumpStepOutcome.Refused, GoalExecutionResult.Blocked($"{RefusedReason}: {refused.Message}"));
        }

        await ships.UpdateNavAsync(ship.Symbol, result.Nav, null, ct);
        await ships.UpdateCooldownAsync(ship.Symbol, result.CooldownExpiresAt ?? now.AddSeconds(result.CooldownSeconds), ct);
        if (result.AgentCredits is { } after)
        {
            await agents.SetCreditsAsync(bus, after, ct);
        }

        await bus.PublishAsync(new ShipJumpedEvent(ship.Symbol, gate, destinationGate, result.Cost));
        logger.LogInformation(
            "{EventKind:l}: ship {ShipSymbol} jumped from {WaypointSymbol} to {Destination} in {SystemSymbol}; the antimatter cost {Cost}.",
            JournalEvents.Jumped,
            ship.Symbol,
            gate,
            destinationGate,
            result.Nav.SystemSymbol,
            result.Cost);
        return new JumpStep(JumpStepOutcome.Jumped, GoalExecutionResult.Progressing($"Jumped to {destinationGate}."));
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
                logger.LogDebug(ex, "GoalJumps: couldn't fetch the market at {WaypointSymbol} for the antimatter's price.", gate);
            }
        }

        return market?.TradeGoods
            .FirstOrDefault(good => good.Symbol.Equals(Antimatter, StringComparison.OrdinalIgnoreCase))?
            .PurchasePrice ?? 0;
    }
}
