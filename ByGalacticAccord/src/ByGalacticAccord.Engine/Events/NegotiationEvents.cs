using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Engine.Events;

/// <summary>
/// The player's post reaches an AI actor that can see it, and it answers (design §3.6, §16.6): it takes it as posted,
/// counters, or lets it pass. Scheduled at posting for each actor that could answer, after the post reaches it (by its
/// presence) and the actor has taken the time its answer takes.
/// </summary>
public sealed class PostReached : ISimEvent
{
    public PostReached(long tick, ContractId contract, ActorId actor)
    {
        Tick = tick;
        Contract = contract;
        Actor = actor;
    }

    public long Tick { get; }

    public ContractId Contract { get; }

    public ActorId Actor { get; }

    public string Describe(SimulationContext context) => $"{Actor} answers {Contract}.";

    public void Process(SimulationContext context)
    {
        if (context.Contract(Contract) is { IsOpen: true } contract)
        {
            DecisionEngine.Respond(context, context.Actor(Actor), contract);
        }
    }
}

/// <summary>
/// An AI actor's reply to the player's offer is due (design §16.5: after 1–12 game hours). It accepts, counters or walks
/// away, unless the haggle has moved on or ended since.
/// </summary>
public sealed class ReplyDue : ISimEvent
{
    public ReplyDue(long tick, NegotiationId negotiation, int round)
    {
        Tick = tick;
        Negotiation = negotiation;
        Round = round;
    }

    public long Tick { get; }

    public NegotiationId Negotiation { get; }

    /// <summary>The round it answers: a reply to an offer since superseded is dropped.</summary>
    public int Round { get; }

    public string Describe(SimulationContext context) => $"Reply due in {Negotiation}, round {Round}.";

    public void Process(SimulationContext context)
    {
        Negotiation thread = context.Negotiation(Negotiation);
        if (thread.IsOpen && thread.Rounds == Round && context.Actor(thread.AwaitingFrom) is { IsPlayer: false } ai)
        {
            DecisionEngine.Reply(context, ai, thread);
        }
    }
}
