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

        // The player's actor is driven by the human, not the decision engine (§4.7.3).
        if (!actor.IsPlayer)
        {
            if (actor.Producer is not null)
            {
                DecisionEngine.RunProducer(context, actor);
            }

            if (actor.Demands.Count > 0)
            {
                DecisionEngine.RunConsumer(context, actor);
            }

            if (actor.MovesCargo)
            {
                DecisionEngine.RunHauler(context, actor);
            }
        }

        context.Schedule(new ActorHeartbeat(Tick + context.Config.HeartbeatInterval, Actor));
    }
}

/// <summary>Times out an open contract nobody took, keeping the board clean and avoiding orphans (§5.6).</summary>
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
        if (!context.Contracts.TryGetValue(Contract, out Contract? contract))
        {
            return;
        }

        if (contract.Status is ContractStatus.Open or ContractStatus.Negotiating)
        {
            contract.MarkExpired();
            context.Record("Expired", $"{Contract}: no hauler took it before the deadline.");
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
