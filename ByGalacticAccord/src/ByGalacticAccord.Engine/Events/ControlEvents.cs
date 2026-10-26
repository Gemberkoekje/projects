using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Engine.Events;

/// <summary>
/// An actor re-evaluates (§4.5.3). Heartbeats are staggered across actors and recurring, so the sim
/// stays cheap (no global per-tick scan) while every actor still drifts forward on its own clock.
/// </summary>
public sealed class ActorHeartbeat : ISimEvent
{
    public ActorHeartbeat(long tick, ActorId actor)
    {
        Tick = tick;
        Actor = actor;
    }

    public long Tick { get; }

    public ActorId Actor { get; }

    public string Describe(SimulationContext context) => $"Heartbeat for {Actor}.";

    public void Process(SimulationContext context)
    {
        if (!context.Actors.TryGetValue(Actor, out ActorState? actor))
        {
            return;
        }

        // Ships cost money every day, earning or idle, whoever owns them: the player too (design §6.5).
        DecisionEngine.ChargeRunningCosts(context, actor);

        // The player's actor is driven by the human, not the decision engine (§4.7.3).
        if (!actor.IsPlayer)
        {
            DecisionEngine.Run(context, actor);
        }

        context.Schedule(new ActorHeartbeat(Tick + context.Config.HeartbeatInterval, Actor));
    }
}

/// <summary>Times out an open contract nobody took, keeping the board clean (design §16.2: expired).</summary>
public sealed class ContractExpiryCheck : ISimEvent
{
    public ContractExpiryCheck(long tick, ContractId contract)
    {
        Tick = tick;
        Contract = contract;
    }

    public long Tick { get; }

    public ContractId Contract { get; }

    public string Describe(SimulationContext context) => $"Expiry check for {Contract}.";

    public void Process(SimulationContext context)
    {
        if (context.Contracts.TryGetValue(Contract, out Contract? contract))
        {
            Market.Expire(context, contract);
        }
    }
}

/// <summary>
/// A signed contract's grace runs out (design §3.4, §19). If it still isn't performed it is in default: the journal
/// records it, and its payer may cancel for default or keep waiting while the late fee runs to its cap. An AI payer
/// decides by utility and, if it waits, reviews daily (<see cref="DefaultReview"/>); the player is asked, and the
/// world pauses until it answers (design §8.3).
/// </summary>
public sealed class GraceEnded : ISimEvent
{
    public GraceEnded(long tick, ContractId contract)
    {
        Tick = tick;
        Contract = contract;
    }

    public long Tick { get; }

    public ContractId Contract { get; }

    public string Describe(SimulationContext context) => $"Grace ends for {Contract}.";

    public void Process(SimulationContext context)
    {
        Contract contract = context.Contract(Contract);
        if (!contract.InDefault(Tick))
        {
            return; // performed, or ended some other way, in time
        }

        context.NoteFailure(FailureKind.InDefault);
        JournalEntry entry = context.Record(new ContractInDefaultEntry(Tick, contract.Id, contract.Performer!.Value, contract.Payer!.Value));
        ActorState payer = context.Actor(contract.Payer!.Value);
        if (payer.IsPlayer)
        {
            context.RaisePlayerDecision(new PlayerDecision(DecisionKind.CancelForDefault, contract.Id, Tick, entry.Seq));
            return;
        }

        DecisionEngine.ReviewDefault(context, payer, contract);
    }
}

/// <summary>An AI payer that chose to wait on a contract in default looks at it again (design §4.3: a deadline approaching).</summary>
public sealed class DefaultReview : ISimEvent
{
    public DefaultReview(long tick, ContractId contract)
    {
        Tick = tick;
        Contract = contract;
    }

    public long Tick { get; }

    public ContractId Contract { get; }

    public string Describe(SimulationContext context) => $"Default review for {Contract}.";

    public void Process(SimulationContext context)
    {
        Contract contract = context.Contract(Contract);
        if (contract.InDefault(Tick) && context.Actor(contract.Payer!.Value) is { IsPlayer: false } payer)
        {
            DecisionEngine.ReviewDefault(context, payer, contract);
        }
    }
}

/// <summary>The scheduled inflation sample (§5.8). Auditable: its adjustments are themselves journal events.</summary>
public sealed class InflationCheck : ISimEvent
{
    public InflationCheck(long tick) => Tick = tick;

    public long Tick { get; }

    public string Describe(SimulationContext context) => "Inflation check.";

    public void Process(SimulationContext context)
    {
        InflationMonitor.Evaluate(context);
        context.Schedule(new InflationCheck(Tick + context.Config.InflationCheckInterval));
    }
}

/// <summary>Slow regression of reputation toward neutral (§4.3): old sins fade, the player stays redeemable.</summary>
public sealed class ReputationDecay : ISimEvent
{
    public ReputationDecay(long tick) => Tick = tick;

    public long Tick { get; }

    public string Describe(SimulationContext context) => "Reputation decay.";

    public void Process(SimulationContext context)
    {
        context.Reputation.DecayTowardNeutral(context.Config.ReputationDecayRate);
        context.Schedule(new ReputationDecay(Tick + context.Config.ReputationDecayInterval));
    }
}
