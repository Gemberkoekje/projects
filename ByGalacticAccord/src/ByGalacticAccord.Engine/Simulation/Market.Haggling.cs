using System;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;

namespace ByGalacticAccord.Engine.Simulation;

/// <summary>
/// Haggling (design §3.6, §16.5): counter-offers on an open contract, accepting the other side's offer, and ending a haggle.
/// The same commands for the player and an AI actor; an AI on the other side of the player's offer replies over game time
/// (<see cref="DecisionEngine.ExpectReply"/>). Between two AI actors a haggle resolves in closed form instead and never
/// comes here.
/// </summary>
public static partial class Market
{
    /// <summary>
    /// <paramref name="actor"/> makes a counter-offer on an open contract. On someone else's post it opens, or continues, its
    /// haggle with the poster; a poster counters inside a responder's haggle (<paramref name="thread"/>). It may only answer an
    /// offer that waits for it, and not after the last round. An AI on the other side replies in game time. Null if it may not.
    /// </summary>
    public static Negotiation? Counter(
        SimulationContext context, ActorState actor, Contract contract, Offer offer, Negotiation? thread = null, long? replyHours = null, Tell tell = Tell.None)
    {
        if (!contract.IsOpen || contract is StorageContract || offer.Price.Amount <= 0m || offer.Deadline <= context.CurrentTick)
        {
            return null;
        }

        if (thread is null)
        {
            if (contract.PostedBy == actor.Id)
            {
                return null; // a poster answers a responder's haggle
            }

            thread = context.NegotiationWith(contract.Id, actor.Id);
            if (thread is null)
            {
                thread = new Negotiation(context.NextNegotiationId(), contract.Id, contract.PostedBy, actor.Id, context.CurrentTick);
                thread.Add(new Bid(0, contract.PostedTick, contract.PostedBy, Offer.Of(contract)));
                context.Add(thread);
            }
        }

        if (thread.Contract != contract.Id || !thread.IsOpen || thread.AwaitingFrom != actor.Id || thread.Rounds >= context.Config.NegotiationMaxRounds)
        {
            return null;
        }

        int round = thread.Rounds + 1;
        ActorId other = thread.Other(actor.Id);
        thread.Add(new Bid(round, context.CurrentTick, actor.Id, offer, replyHours, tell));
        context.Record(new OfferMadeEntry(context.CurrentTick, contract.Id, thread.Id, actor.Id, other, offer, round, replyHours, tell));
        if (context.Actor(other) is { IsPlayer: false } ai)
        {
            DecisionEngine.ExpectReply(context, ai, thread);
        }

        return thread;
    }

    /// <summary>
    /// <paramref name="actor"/> accepts the other side's latest offer in a haggle: the responder takes the contract on it, by
    /// the same command it would take it as posted (<see cref="DecisionEngine.TakeOn"/>, <see cref="PlayerActions.TakeOn"/>).
    /// If the responder can no longer do what it offered, nothing is signed and the haggle ends.
    /// </summary>
    public static Signing AcceptOffer(SimulationContext context, ActorState actor, Negotiation thread, long? replyHours = null, Tell tell = Tell.None)
    {
        Contract contract = context.Contract(thread.Contract);
        if (!thread.IsOpen || thread.AwaitingFrom != actor.Id || !contract.IsOpen)
        {
            return Signing.NotOpen;
        }

        Offer offer = thread.Latest.Offer;
        ActorId other = thread.Other(actor.Id);
        context.Record(new OfferAcceptedEntry(context.CurrentTick, contract.Id, thread.Id, actor.Id, other, offer, replyHours, tell));
        ActorState responder = context.Actor(thread.Responder);
        Signing signed = responder.IsPlayer
            ? PlayerActions.TakeOn(context, responder, contract, offer)
            : DecisionEngine.TakeOn(context, responder, contract, offer);
        if (signed != Signing.Signed && thread.IsOpen)
        {
            thread.Close(NegotiationEnd.CannotPerform, responder.Id, context.CurrentTick);
            context.Record(new NegotiationEndedEntry(context.CurrentTick, contract.Id, thread.Id, responder.Id, thread.Other(responder.Id), NegotiationEnd.CannotPerform));
        }

        return signed;
    }

    /// <summary>
    /// <paramref name="actor"/> ends a haggle without a deal: declines the other side's offer, or walks away (the gap too wide,
    /// or the rounds run out). False if it isn't its turn.
    /// </summary>
    public static bool EndNegotiation(SimulationContext context, ActorState actor, Negotiation thread, NegotiationEnd end, long? replyHours = null)
    {
        if (!thread.IsOpen || !thread.Involves(actor.Id) || (end != NegotiationEnd.Declined && thread.AwaitingFrom != actor.Id))
        {
            return false;
        }

        thread.Close(end, actor.Id, context.CurrentTick);
        context.Record(new NegotiationEndedEntry(context.CurrentTick, thread.Contract, thread.Id, actor.Id, thread.Other(actor.Id), end, replyHours));
        return true;
    }

    /// <summary>
    /// Sign on an agreed offer: the contract's deadline and terms become the offer's (rebuilt from its price, design §16.5),
    /// then <paramref name="sign"/> runs the kind's signing command at the offer's price. If it doesn't sign, they go back.
    /// </summary>
    internal static Signing SignAgreed(Contract contract, Offer offer, Func<Signing> sign)
    {
        long deadline = contract.DeadlineTick;
        ContractTerms terms = contract.Terms;
        if (offer != Offer.Of(contract))
        {
            contract.Amend(offer.Deadline, Terms.For(contract, offer));
        }

        Signing signed = sign();
        if (signed != Signing.Signed && contract.IsOpen)
        {
            contract.Amend(deadline, terms);
        }

        return signed;
    }
}
