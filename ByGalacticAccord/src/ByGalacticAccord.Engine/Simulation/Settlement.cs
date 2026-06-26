using System;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;

namespace ByGalacticAccord.Engine.Simulation;

/// <summary>
/// The money- and goods-moving heart of a haul. Kept in one place so the conservation invariants
/// (§5.6) are easy to audit: every branch below either transfers (conserves), faucets/sinks (the
/// only create/destroy), or moves escrow — and the escrow always nets to zero by contract close.
/// </summary>
public static class Settlement
{
    /// <summary>At departure: goods leave the origin producer and the producer is paid (from escrow, or by the pilot on the frontier).</summary>
    public static void Pickup(SimulationContext context, Contract contract, Ship ship, ActorState pilot)
    {
        decimal taken = 0m;
        ActorState? producer = context.ProducerAt(contract.OriginLocationId, contract.Good);
        if (producer is not null)
        {
            decimal available = producer.Stock(contract.Good);
            taken = Math.Min(available, contract.Quantity);
            producer.RemoveStock(contract.Good, taken);
            context.NotePickup(taken);

            if (contract.UsesEscrow)
            {
                context.ReleaseFromEscrow(contract, producer, contract.GoodsCost);
            }
            else
            {
                context.TryTransfer(pilot, producer, contract.GoodsCost);
            }
        }

        // The ship carries exactly what was loaded — never more than the origin could supply.
        ship.Depart(context.CurrentTick, Cargo.Fresh(contract.Good, taken), contract.Id);
    }

    /// <summary>On a successful delivery: pay the pilot, settle warranty/penalty clauses, tax the fee, deliver the goods, update reputation.</summary>
    public static void Fulfill(SimulationContext context, Contract contract, Ship ship, long arrivalTick)
    {
        ActorState consumer = context.Actor(contract.PostedBy);
        ActorState pilot = context.Actor(contract.CounterpartyId!.Value);
        Cargo cargo = ship.Manifest ?? Cargo.Fresh(contract.Good, 0m);

        long ticksLate = Math.Max(0, arrivalTick - contract.DeadlineTick);
        Credits fee = contract.Fee;
        Credits tax = Credits.Of(fee.Amount * contract.TaxRate);

        decimal warrantyFraction = contract.Warranty?.RefundFraction(cargo.Quality) ?? 0m;
        Credits warrantyRefund = Credits.Of(fee.Amount * warrantyFraction);
        Credits penalty = contract.LatePenalty?.PenaltyFor(ticksLate) ?? Credits.Zero;
        if (penalty.Amount > contract.LockedBond.Amount)
        {
            penalty = contract.LockedBond; // a penalty can only ever draw the bond that was actually posted
        }

        if (contract.UsesEscrow)
        {
            Credits bond = contract.LockedBond;
            context.ReleaseFromEscrow(contract, pilot, fee - warrantyRefund);
            context.ReleaseFromEscrow(contract, pilot, bond - penalty);
            context.ReleaseFromEscrow(contract, consumer, warrantyRefund);
            context.ForfeitFromEscrow(contract, consumer, penalty);
            context.TaxFromEscrow(contract, tax);
        }
        else
        {
            Credits effectiveFee = Credits.Of(Math.Max(0m, fee.Amount - warrantyRefund.Amount - penalty.Amount));
            if (!context.TryTransfer(consumer, pilot, effectiveFee))
            {
                // Consumer can't pay on the lawless frontier: a default. The goods never change hands —
                // write them off (cargo conservation) and take it out of the consumer's reputation.
                context.NoteLost(cargo.Quantity);
                BreachReputation(context, contract, consumer, ticksLate, blameConsumer: true);
                context.NoteBreached();
                Close(context, contract, ship, breached: true);
                context.Record("Breach", $"{contract.Id}: frontier consumer {consumer.Name} defaulted on payment.");
                return;
            }

            context.TaxDirect(consumer, tax);
        }

        consumer.AddStock(contract.Good, cargo.Quantity);
        context.NoteDelivered(cargo.Quantity);

        RewardReputation(context, contract, pilot, ticksLate, cargo.Quality);
        context.NoteFulfilled();
        Close(context, contract, ship, breached: false);

        string late = ticksLate > 0 ? $" ({ticksLate} ticks late)" : string.Empty;
        context.Record("Fulfilled", $"{contract.Id}: {pilot.Name} delivered {cargo.Quantity:0} {contract.Good} to {consumer.Name}{late}. Fee {fee}.");
    }

    /// <summary>On a failed delivery (total cargo loss): the pilot forfeits its bond, the consumer is refunded, reputation takes a hit.</summary>
    public static void Breach(SimulationContext context, Contract contract, Ship ship, long arrivalTick, string reason)
    {
        ActorState consumer = context.Actor(contract.PostedBy);
        ActorState pilot = context.Actor(contract.CounterpartyId!.Value);
        long ticksLate = Math.Max(0, arrivalTick - contract.DeadlineTick);

        if (contract.UsesEscrow)
        {
            context.ForfeitFromEscrow(contract, consumer, contract.LockedBond);
            context.ReleaseFromEscrow(contract, consumer, contract.Fee + Credits.Of(contract.Fee.Amount * contract.TaxRate));
        }

        BreachReputation(context, contract, pilot, ticksLate, blameConsumer: false);
        context.NoteBreached();
        Close(context, contract, ship, breached: true);
        context.Record("Breach", $"{contract.Id}: {pilot.Name} failed delivery to {consumer.Name} — {reason}.");
    }

    private static void Close(SimulationContext context, Contract contract, Ship ship, bool breached)
    {
        if (breached)
        {
            contract.MarkBreached();
        }
        else
        {
            contract.MarkFulfilled();
        }

        ship.Unload(context.CurrentTick);
    }

    private static decimal ValueScale(Credits fee) => Math.Clamp(fee.Amount / 200m, 0.4m, 3m);

    private static void RewardReputation(SimulationContext context, Contract contract, ActorState pilot, long ticksLate, decimal quality)
    {
        decimal scale = ValueScale(contract.Fee);
        decimal latePenalty = ticksLate > 0 ? 0.5m : 1m;
        decimal qualityFactor = quality < 0.7m ? 0.4m : 1m;
        decimal gain = context.Config.ReputationGainBase * scale * latePenalty * qualityFactor;

        ApplyAcross(context, pilot.Id, contract, gain);
    }

    private static void BreachReputation(SimulationContext context, Contract contract, ActorState blamed, long ticksLate, bool blameConsumer)
    {
        decimal scale = ValueScale(contract.Fee);
        decimal lateness = 1m + Math.Min(2m, ticksLate / 50m);
        decimal loss = -context.Config.ReputationLossBase * scale * lateness;
        ApplyAcross(context, blamed.Id, contract, loss);
    }

    private static void ApplyAcross(SimulationContext context, ActorId actor, Contract contract, decimal delta)
    {
        if (contract.AccordCompliant)
        {
            context.Reputation.Adjust(actor, ReputationContext.Accord, delta);
        }

        context.Reputation.Adjust(actor, ReputationContext.ForRegion(contract.Region), delta);
        context.Reputation.Adjust(actor, ReputationContext.ForGood(Goods.Info(contract.Good).Category), delta);
        context.Reputation.Adjust(actor, ReputationContext.With(contract.PostedBy), delta);
    }
}
