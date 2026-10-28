using System;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;

namespace ByGalacticAccord.Engine.Simulation;

/// <summary>
/// What a Sale or Haul settles at: the share of the price due, the warranty's refund, and the late fee and cargo
/// liability the terms charge, each limited to what the jurisdiction can collect (see <see cref="Settlement.Collectable"/>).
/// </summary>
public readonly record struct SettlementTerms(Credits Gross, Credits QualityRefund, Credits LateFee, Credits Liability, Credits Tax)
{
    /// <summary>What the goods or the trip earn: the gross less the refund.</summary>
    public Credits Payment => Gross - QualityRefund;

    /// <summary>What the performer owes the payer under the terms: the late fee and any cargo liability.</summary>
    public Credits Penalties => LateFee + Liability;

    /// <summary>What the performer ends up with from the payment, outside the Accord where nothing else is held: never below zero.</summary>
    public Credits Net => Credits.Of(Math.Max(0m, Payment.Amount - Penalties.Amount));
}

/// <summary>How a handover went.</summary>
public enum HandOver
{
    /// <summary>The goods are the buyer's and the price is paid.</summary>
    Done,

    /// <summary>Outside the Accord the buyer couldn't pay: the seller keeps the goods.</summary>
    Defaulted,

    /// <summary>The terms failed it (rejected on inspection, or short under a strict term): the seller keeps the goods.</summary>
    Refused,

    /// <summary>The player must inspect the goods first; they wait, set aside for the sale, until it does.</summary>
    Pending,
}

/// <summary>
/// Where contracts settle: goods handed over under a Sale, a Haul delivered, a performer's failure, a cancellation, a
/// payer's default. Kept in one place so the conservation invariants (design §20.2) are easy to audit: every branch
/// below moves goods through <see cref="SimulationContext"/>'s goods primitives and money through its transfers and
/// escrow, and a settled contract's escrow always nets to zero. The terms decide (design §3.4); the jurisdiction
/// decides how far they are enforced (design §3.5): in the deep Accord in full, at its edge as far as escrow holds,
/// on the frontier only as far as the payer's payment at handover.
/// </summary>
public static class Settlement
{
    /// <summary>What <paramref name="contract"/> settles at for <paramref name="delivered"/> units of <paramref name="quality"/>, <paramref name="ticksLate"/> late.</summary>
    public static SettlementTerms Due(SimulationContext context, Contract contract, decimal quantity, decimal delivered, decimal quality, long ticksLate)
    {
        Credits performerCredits = contract.Performer is { } p ? context.Actor(p).Credits : Credits.Zero;
        return Due(contract, quantity, delivered, quality, ticksLate, contract.LockedBond, performerCredits);
    }

    /// <summary>
    /// The settlement for a bond and the performer's account as given: what a contract would settle at, for the
    /// interface's estimates as well as the engine. Paid pro rata for what arrived.
    /// </summary>
    public static SettlementTerms Due(Contract contract, decimal quantity, decimal delivered, decimal quality, long ticksLate, Credits bond, Credits performerCredits)
    {
        decimal share = quantity > 0m ? Math.Clamp(delivered / quantity, 0m, 1m) : 1m;
        Credits gross = Credits.Of(contract.Price.Amount * share);
        Credits refund = Credits.Of(gross.Amount * (contract.Warranty?.RefundFraction(quality) ?? 0m));
        decimal late = contract.LatePenalty?.PenaltyFor(ticksLate).Amount ?? 0m;
        decimal liability = contract is HaulContract && contract.Terms.Liability is { } l ? l.For(quantity - delivered).Amount : 0m;

        // The late fee is charged first, then the liability, as far as the jurisdiction can collect.
        decimal limit = Collectable(contract, gross - refund, bond, performerCredits).Amount;
        late = Math.Min(late, limit);
        liability = Math.Min(liability, limit - late);
        Credits tax = Credits.Of(gross.Amount * contract.TaxRate);
        return new SettlementTerms(gross, refund, Credits.Of(late), Credits.Of(liability), tax);
    }

    /// <summary>
    /// The most the payer can collect from a performer owed <paramref name="payment"/> (design §3.5): on the frontier,
    /// only what comes off that payment; in Accord space the payment and the bond in escrow; in the deep Accord, where
    /// the courts reach, the performer's account too.
    /// </summary>
    public static Credits Collectable(Contract contract, Credits payment, Credits bond, Credits performerCredits)
    {
        if (!contract.UsesEscrow)
        {
            return payment;
        }

        decimal held = payment.Amount + bond.Amount;
        return Credits.Of(contract.Arbitrated ? held + Math.Max(0m, performerCredits.Amount) : held);
    }

    /// <summary>Whether the payer can settle <paramref name="terms"/> at handover. Accord escrow always can.</summary>
    public static bool CanPay(SimulationContext context, Contract contract, SettlementTerms terms) =>
        contract.UsesEscrow || context.Actor(contract.Payer!.Value).Credits >= terms.Net;

    /// <summary>
    /// Hand over the goods the seller set aside for <paramref name="sale"/> (its stock or storage at the place, or the
    /// stock a buyer's ship collects from). The buyer inspects them first: below the warranty's floor it may reject
    /// them (<paramref name="inspected"/> is the quality the player already accepted them at). Outside the Accord a
    /// buyer who can't pay defaults, and the seller keeps the goods.
    /// </summary>
    public static HandOver HandOverReserved(SimulationContext context, SaleContract sale, decimal? inspected = null)
    {
        ActorState seller = context.Actor(sale.Seller!.Value);
        Holding from = sale.Reserved ?? throw new InvalidOperationException($"{sale.Id} has no goods set aside to hand over.");
        decimal quality = inspected
                          ?? (from.Kind == HoldingKind.Storage ? context.StorageOf(seller.Id, from.Location)?.Peek(sale.Good, context.CurrentTick)?.Quality ?? 1m : 1m);

        // When the buyer's ship collects, the seller has had the goods ready all along: a late collection is the buyer's.
        long ticksLate = sale.Collected ? 0 : Late(context, sale);
        if (inspected is null)
        {
            switch (Inspect(context, sale, sale.Quantity, quality))
            {
                case null:
                    return HandOver.Pending;
                case false:
                    Fail(context, sale, FailureReason.Rejected, sale.Quantity, quality);
                    return HandOver.Refused;
            }
        }

        SettlementTerms terms = Due(context, sale, sale.Quantity, sale.Quantity, quality, ticksLate);
        if (!CanPay(context, sale, terms))
        {
            PayerDefault(context, sale);
            return HandOver.Defaulted;
        }

        Cargo goods = from.Kind == HoldingKind.Storage
            ? context.TakeFromStorage(seller, from.Location, sale.Good, sale.Quantity, reserved: true)
            : context.TakeFromStock(seller, sale.Good, sale.Quantity, reserved: true);
        sale.SetReserved(null);
        Complete(context, sale, goods with { Quality = quality }, terms, ticksLate);
        return HandOver.Done;
    }

    /// <summary>
    /// Hand over goods already taken out of a hold for <paramref name="sale"/>. A short delivery under terms that treat it
    /// as a failure, or goods the buyer rejects, fail the sale; outside the Accord a buyer who can't pay defaults. Either
    /// way the caller gives the goods back to the seller. Goods the player must inspect are set aside in the seller's
    /// storage at the place until it decides.
    /// </summary>
    public static HandOver HandOverCargo(SimulationContext context, SaleContract sale, Cargo goods)
    {
        long ticksLate = Late(context, sale);
        if (goods.Quantity < sale.Quantity && sale.Terms.Partial == PartialDelivery.Failure)
        {
            Fail(context, sale, FailureReason.ShortDelivery, goods.Quantity, goods.Quality);
            return HandOver.Refused;
        }

        switch (Inspect(context, sale, goods.Quantity, goods.Quality))
        {
            case null:
                ActorState seller = context.Actor(sale.Seller!.Value);
                StorageContract storage = context.PutInStorage(seller, sale.Place, goods);
                context.ReserveStorage(storage, goods.Good, goods.Quantity);
                sale.SetReserved(Holding.StorageAt(sale.Place));
                return HandOver.Pending;
            case false:
                Fail(context, sale, FailureReason.Rejected, goods.Quantity, goods.Quality);
                return HandOver.Refused;
        }

        SettlementTerms terms = Due(context, sale, sale.Quantity, goods.Quantity, goods.Quality, ticksLate);
        if (!CanPay(context, sale, terms))
        {
            PayerDefault(context, sale);
            return HandOver.Defaulted;
        }

        Complete(context, sale, goods, terms, ticksLate);
        return HandOver.Done;
    }

    /// <summary>
    /// A carrier hands a shipper's goods back at the destination. The haul settles by its terms: paid pro rata for what
    /// arrived, less the warranty's refund, the late fee and liability for goods lost; or failed, if what arrived is
    /// below the warranty's floor or short under a strict term. The carrier answers only for its hold: the warranty
    /// judges the goods as a share of their quality when loaded. Either way the goods are the shipper's, and go on to
    /// a Sale the haul serves.
    /// </summary>
    public static void DeliverHaul(SimulationContext context, HaulContract haul, Ship ship)
    {
        ActorState shipper = context.Actor(haul.Shipper);
        ActorState carrier = context.Actor(haul.Carrier!.Value);
        Cargo goods = context.Unload(ship, shipper.Id, haul.Good, haul.Quantity);
        long ticksLate = Late(context, haul);
        decimal kept = haul.InTransitQuality(goods.Quality);

        if (haul.Warranty?.Rejectable(kept) == true)
        {
            // The shipper rejects the carriage: the goods are its own whatever it decides, so refusing to pay is no loss to it.
            Fail(context, haul, FailureReason.Rejected, goods.Quantity, kept);
        }
        else if (goods.Quantity < haul.Quantity && haul.Terms.Partial == PartialDelivery.Failure)
        {
            Fail(context, haul, FailureReason.ShortDelivery, goods.Quantity, kept);
        }
        else
        {
            SettlementTerms terms = Due(context, haul, haul.Quantity, goods.Quantity, kept, ticksLate);
            if (!CanPay(context, haul, terms))
            {
                PayerDefault(context, haul);
            }
            else
            {
                Pay(context, haul, shipper, carrier, terms);
                Reward(context, carrier, haul, ticksLate, goods.Quality);
                Settled(context, haul, goods.Quantity, terms, ticksLate);
                context.Record(new HaulDeliveredEntry(
                    context.CurrentTick, haul.Id, carrier.Id, goods.Quantity, kept, ticksLate, terms.Gross, terms.LateFee, terms.QualityRefund, terms.Liability));
            }
        }

        HandToShipper(context, haul, shipper, goods);
    }

    /// <summary>
    /// The performer failed by the terms: the goods were lost, came short under a strict term, were rejected, or never
    /// came. In the Accord its bond goes to the payer and the payer's escrow comes back; in the deep Accord a carrier
    /// also owes liability for lost goods beyond its bond. A Sale's set-aside goods are freed and any open haul for it
    /// is withdrawn. Lost goods take down the Sale a haul was delivering.
    /// </summary>
    public static void Fail(SimulationContext context, Contract contract, FailureReason reason, decimal delivered = 0m, decimal quality = 1m, long? cause = null)
    {
        if (contract.Status != ContractStatus.Active)
        {
            return;
        }

        ActorState payer = context.Actor(contract.Payer!.Value);
        ActorState performer = context.Actor(contract.Performer!.Value);
        Credits bond = contract.LockedBond;
        if (contract.UsesEscrow)
        {
            context.ForfeitFromEscrow(contract, payer, bond);
            context.ReleaseFromEscrow(contract, payer, contract.EscrowHeld);
        }

        if (contract.Arbitrated && contract is HaulContract haul && haul.Terms.Liability is { } liability)
        {
            Credits owed = liability.For(haul.Quantity - delivered) - bond;
            if (owed.Amount > 0m)
            {
                context.TransferUpTo(performer, payer, owed);
            }
        }

        FreeSetAside(context, contract, performer);
        Blame(context, performer, contract, Late(context, contract));
        context.Records.Failed(contract);
        contract.MarkBreached();
        context.NoteBreached();
        context.NoteFailure(reason switch
        {
            FailureReason.ShortDelivery => FailureKind.ShortFailed,
            FailureReason.Rejected => FailureKind.Rejected,
            _ => FailureKind.CargoLost,
        });
        JournalEntry failed = context.Record(new DeliveryFailedEntry(context.CurrentTick, contract.Id, performer.Id, reason, delivered, quality) { Cause = cause });
        WithdrawHaulsFor(context, contract, performer, failed.Seq);

        // A lost haul takes down the Sale it was delivering: the shipper can't hand over goods it no longer has.
        if (reason == FailureReason.CargoLost && contract is HaulContract lost && lost.Serves is { } served && context.Contract(served) is SaleContract downstream)
        {
            Fail(context, downstream, reason, cause: failed.Seq);
        }
    }

    /// <summary>
    /// End a signed contract by cancellation (design §3.4). For default, by the payer once the grace has passed: its
    /// escrow comes back and the performer's bond is its. At will, by either party: the canceller pays the break fee,
    /// from what escrow holds of its money first, then from its account. Anything already moving finishes its trip and
    /// is set down for its owner; goods set aside for it are free again.
    /// </summary>
    public static void Cancel(SimulationContext context, Contract contract, ActorState by, CancelReason reason)
    {
        if (contract.Status != ContractStatus.Active)
        {
            return;
        }

        ActorState payer = context.Actor(contract.Payer!.Value);
        ActorState performer = context.Actor(contract.Performer!.Value);
        Credits bond = contract.LockedBond;
        Credits paid;
        if (reason == CancelReason.ForDefault)
        {
            if (contract.UsesEscrow)
            {
                context.ForfeitFromEscrow(contract, payer, bond);
                context.ReleaseFromEscrow(contract, payer, contract.EscrowHeld);
            }

            paid = bond;
            Blame(context, performer, contract, Late(context, contract));
            context.Records.CancelledForDefault(contract);
            context.NoteFailure(FailureKind.CancelledForDefault);
        }
        else
        {
            Credits fee = contract.Terms.BreakFee;
            ActorState other = by.Id == payer.Id ? performer : payer;
            decimal fromEscrow = 0m;
            if (contract.UsesEscrow)
            {
                // What escrow holds of the canceller's money pays the fee first: the payer's price, or the performer's bond.
                fromEscrow = Math.Min(fee.Amount, by.Id == payer.Id ? contract.EscrowHeld.Amount - bond.Amount : bond.Amount);
                context.ForfeitFromEscrow(contract, other, Credits.Of(fromEscrow));
                context.ReleaseFromEscrow(contract, performer, by.Id == payer.Id ? bond : bond - Credits.Of(fromEscrow));
                context.ReleaseFromEscrow(contract, payer, contract.EscrowHeld);
            }

            paid = Credits.Of(fromEscrow) + context.TransferUpTo(by, other, Credits.Of(fee.Amount - fromEscrow));
            Blame(context, by, contract, 0, 0.3m);
            context.Records.WalkedAway(contract, by.Id);
            context.NoteFailure(FailureKind.CancelledAtWill);
        }

        FreeSetAside(context, contract, performer);
        contract.MarkCancelled(new Cancellation(by.Id, reason, context.CurrentTick, paid));
        context.NoteCancelled();
        JournalEntry cancelled = context.Record(new ContractCancelledEntry(context.CurrentTick, contract.Id, by.Id, reason, paid));
        WithdrawHaulsFor(context, contract, performer, cancelled.Seq);
    }

    /// <summary>Outside the Accord the payer couldn't pay at handover. Nobody enforces anything; its record with the payee suffers.</summary>
    public static void PayerDefault(SimulationContext context, Contract contract)
    {
        ActorState payer = context.Actor(contract.Payer!.Value);
        ActorState payee = context.Actor(contract.Performer!.Value);
        if (contract is SaleContract sale)
        {
            // The seller keeps its goods: anything it set aside is free again.
            Unreserve(context, payee, sale.Reserved, sale.Good, sale.Quantity);
            sale.SetReserved(null);
        }

        Blame(context, payer, contract, 0);
        context.Records.Defaulted(contract);
        contract.MarkDefaulted();
        context.NoteDefaulted();
        context.NoteFailure(FailureKind.PayerDefault);
        context.Record(new PaymentDefaultedEntry(context.CurrentTick, contract.Id, payer.Id, payee.Id));
    }

    /// <summary>Put goods where an actor keeps them at a location: its own stock at home, else storage there.</summary>
    public static void Keep(SimulationContext context, ActorState owner, LocationId location, Cargo goods)
    {
        if (goods.Quantity <= 0m)
        {
            return;
        }

        if (owner.Home == location && owner.KeepsStock)
        {
            context.PutInStock(owner, goods);
        }
        else
        {
            context.PutInStorage(owner, location, goods);
        }
    }

    /// <summary>Free goods an actor set aside at a holding.</summary>
    public static void Unreserve(SimulationContext context, ActorState owner, Holding? holding, GoodType good, decimal quantity)
    {
        if (holding is not { } h)
        {
            return;
        }

        if (h.Kind == HoldingKind.Stock)
        {
            context.UnreserveStock(owner, good, quantity);
        }
        else if (h.Kind == HoldingKind.Storage && context.StorageOf(owner.Id, h.Location) is { } storage)
        {
            context.UnreserveStorage(storage, good, quantity);
        }
    }

    /// <summary>How many hours past its deadline a contract is now.</summary>
    public static long Late(SimulationContext context, Contract contract) => Math.Max(0, context.CurrentTick - contract.DeadlineTick);

    // Whether the buyer takes goods of this quality: true below no floor; else an AI decides by utility and the player
    // is asked (null: the decision is raised and the world waits for it).
    private static bool? Inspect(SimulationContext context, SaleContract sale, decimal quantity, decimal quality)
    {
        if (sale.Warranty?.Rejectable(quality) != true)
        {
            return true;
        }

        ActorState buyer = context.Actor(sale.Buyer!.Value);
        if (buyer.IsPlayer)
        {
            context.RaisePlayerDecision(new PlayerDecision(DecisionKind.Inspection, sale.Id, context.CurrentTick, sale.LastEntry ?? -1, quantity, quality));
            return null;
        }

        return !DecisionEngine.WouldReject(context, buyer, sale, quantity, quality);
    }

    // The goods reach the buyer and the price is paid.
    private static void Complete(SimulationContext context, SaleContract sale, Cargo goods, SettlementTerms terms, long ticksLate)
    {
        ActorState buyer = context.Actor(sale.Buyer!.Value);
        ActorState seller = context.Actor(sale.Seller!.Value);
        Receive(context, sale, buyer, goods);
        Pay(context, sale, buyer, seller, terms);
        sale.NoteDelivered(goods.Quantity);
        context.NoteSold(goods.Quantity);
        Reward(context, seller, sale, ticksLate, goods.Quality);
        Settled(context, sale, goods.Quantity, terms, ticksLate);
        context.Record(new GoodsHandedOverEntry(
            context.CurrentTick, sale.Id, seller.Id, buyer.Id, goods.Quantity, goods.Quality, ticksLate, terms.Gross, terms.LateFee, terms.QualityRefund));
    }

    // Fulfilled, or partly for a short delivery paid pro rata; and the failure paths it went through on the way.
    private static void Settled(SimulationContext context, Contract contract, decimal delivered, SettlementTerms terms, long ticksLate)
    {
        decimal quantity = contract switch
        {
            SaleContract s => s.Quantity,
            HaulContract h => h.Quantity,
            _ => delivered,
        };

        if (delivered < quantity)
        {
            contract.MarkPartlyFulfilled();
            context.NoteFailure(FailureKind.ShortProRata);
        }
        else
        {
            contract.MarkFulfilled();
        }

        context.NoteFulfilled();
        context.Records.Delivered(contract, ticksLate, delivered < quantity);
        if (ticksLate > 0)
        {
            context.NoteFailure(FailureKind.LateDelivery);
        }

        if (terms.QualityRefund.Amount > 0m)
        {
            context.NoteFailure(FailureKind.QualityRefund);
        }

        if (terms.Liability.Amount > 0m)
        {
            context.NoteFailure(FailureKind.CargoLiability);
        }
    }

    private static void Receive(SimulationContext context, SaleContract sale, ActorState buyer, Cargo goods)
    {
        Holding receipt = sale.Receipt ?? Holding.StockAt(sale.Place);
        switch (receipt.Kind)
        {
            case HoldingKind.Ship:
                context.Load(context.Ship(receipt.Ship!.Value), buyer.Id, goods);
                break;
            case HoldingKind.Storage:
                context.PutInStorage(buyer, sale.Place, goods);
                break;
            case HoldingKind.Stock:
            default:
                Keep(context, buyer, sale.Place, goods);
                break;
        }
    }

    // In the Accord, escrow pays out: the performer gets the payment and its bond less what it owes; the payer what it is
    // owed and whatever else is left (the undelivered share, the refund, unused tax); the tax is collected. In the deep
    // Accord what escrow can't cover comes from the performer's account. Outside it, the payer pays the net directly.
    private static void Pay(SimulationContext context, Contract contract, ActorState payer, ActorState performer, SettlementTerms terms)
    {
        if (contract.UsesEscrow)
        {
            Credits held = terms.Payment + contract.LockedBond;
            Credits covered = Credits.Of(Math.Min(terms.Penalties.Amount, held.Amount));
            context.ReleaseFromEscrow(contract, performer, held - covered);
            context.ForfeitFromEscrow(contract, payer, covered);
            context.TaxFromEscrow(contract, terms.Tax);
            context.ReleaseFromEscrow(contract, payer, contract.EscrowHeld);
            if (terms.Penalties > covered)
            {
                context.TransferUpTo(performer, payer, terms.Penalties - covered);
            }
        }
        else
        {
            context.TryTransfer(payer, performer, terms.Net);
        }
    }

    // The goods a haul carried are the shipper's: handed over under the Sale the haul serves, if that is still on, else kept here.
    private static void HandToShipper(SimulationContext context, HaulContract haul, ActorState shipper, Cargo goods)
    {
        if (goods.Quantity <= 0m)
        {
            return;
        }

        HandOver handed = haul.Serves is { } id && context.Contract(id) is SaleContract { Status: ContractStatus.Active } sale && sale.Place == haul.Destination
            ? HandOverCargo(context, sale, goods)
            : HandOver.Refused;
        if (handed is HandOver.Refused or HandOver.Defaulted)
        {
            Keep(context, shipper, haul.Destination, goods);
        }
    }

    // A failed or cancelled contract frees what it held: a Sale's set-aside goods; a haul's goods not yet picked up, unless
    // they were set aside for the Sale it serves, which still wants them.
    private static void FreeSetAside(SimulationContext context, Contract contract, ActorState performer)
    {
        switch (contract)
        {
            case SaleContract sale:
                Unreserve(context, performer, sale.Reserved, sale.Good, sale.Quantity);
                sale.SetReserved(null);
                break;
            case HaulContract { PickedUp: false, Serves: null } haul:
                Unreserve(context, context.Actor(haul.Shipper), haul.PickupFrom, haul.Good, haul.Quantity);
                break;
        }
    }

    // An open haul posted to deliver a Sale that has ended is no longer needed.
    private static void WithdrawHaulsFor(SimulationContext context, Contract contract, ActorState performer, long cause)
    {
        if (contract is not SaleContract sale)
        {
            return;
        }

        foreach (Contract other in context.LiveContractsOf(performer.Id))
        {
            if (other is HaulContract haul && haul.Serves == sale.Id && haul.IsOpen)
            {
                Market.Withdraw(context, haul, cause);
            }
        }
    }

    private static decimal ValueScale(Contract contract)
    {
        // Reputation scales with what the performer earns from the contract: a Sale's goods are mostly passed through.
        decimal value = contract is SaleContract ? contract.Price.Amount * 0.25m : contract.Price.Amount;
        return Math.Clamp(value / 200m, 0.4m, 3m);
    }

    private static void Reward(SimulationContext context, ActorState performer, Contract contract, long ticksLate, decimal quality)
    {
        decimal latePenalty = ticksLate > 0 ? 0.5m : 1m;
        decimal qualityFactor = quality < Goods.StandardWarranty ? 0.4m : 1m;
        decimal gain = context.Config.ReputationGainBase * ValueScale(contract) * latePenalty * qualityFactor;
        ApplyAcross(context, performer.Id, contract, gain);
    }

    private static void Blame(SimulationContext context, ActorState blamed, Contract contract, long ticksLate, decimal weight = 1m)
    {
        decimal lateness = 1m + Math.Min(2m, ticksLate / 50m);
        decimal loss = -context.Config.ReputationLossBase * ValueScale(contract) * lateness * weight;
        ApplyAcross(context, blamed.Id, contract, loss);
    }

    private static void ApplyAcross(SimulationContext context, ActorId actor, Contract contract, decimal delta)
    {
        if (contract.AccordCompliant)
        {
            context.Reputation.Adjust(actor, ReputationContext.Accord, delta);
        }

        context.Reputation.Adjust(actor, ReputationContext.ForRegion(contract.Region), delta);
        if (GoodOf(contract) is { } good)
        {
            context.Reputation.Adjust(actor, ReputationContext.ForGood(Goods.Info(good).Category), delta);
        }

        if (contract.Other(actor) is { } other)
        {
            context.Reputation.Adjust(actor, ReputationContext.With(other), delta);
        }
    }

    private static GoodType? GoodOf(Contract contract) => contract switch
    {
        SaleContract s => s.Good,
        HaulContract h => h.Good,
        _ => null,
    };

    /// <summary>
    /// At a job's pickup the goods come aboard: a haul's (if it is still on), a purchase handed over into the hold, or the
    /// owner's own from storage for a sale still on. Returns how much of the job's good its owner then has aboard.
    /// </summary>
    public static decimal Load(SimulationContext context, Ship ship, ShipJob job)
    {
        switch (job.Source)
        {
            case PickupSource.Haul:
            {
                var haul = (HaulContract)context.Contract(job.Haul!.Value);
                if (haul.Status != ContractStatus.Active)
                {
                    break; // cancelled before the carrier got there: nothing to carry
                }

                ActorState shipper = context.Actor(haul.Shipper);
                Cargo goods = haul.PickupFrom.Kind == HoldingKind.Storage
                    ? context.TakeFromStorage(shipper, haul.Origin, haul.Good, haul.Quantity, reserved: true)
                    : context.TakeFromStock(shipper, haul.Good, haul.Quantity, reserved: true);
                if (goods.Quantity < haul.Quantity)
                {
                    // Goods for a haul are set aside when it is posted (or when the Sale it serves is signed).
                    throw new InvalidOperationException($"{haul.Id}: only {goods.Quantity} of {haul.Quantity} {haul.Good} at the pickup.");
                }

                if (haul.Serves is { } served && context.Contract(served) is SaleContract sale)
                {
                    sale.SetReserved(null);
                }

                haul.MarkPickedUp(goods.Quality);
                context.Load(ship, shipper.Id, goods);
                break;
            }

            case PickupSource.Purchase:
            {
                var sale = (SaleContract)context.Contract(job.Purchase!.Value);
                if (sale.Status == ContractStatus.Active)
                {
                    HandOverReserved(context, sale); // into this ship; it may wait on the player's inspection
                }

                break;
            }

            case PickupSource.Storage:
            {
                if (job.Delivery is { } delivery && context.Contract(delivery) is SaleContract sale)
                {
                    if (sale.Status != ContractStatus.Active)
                    {
                        break; // the sale ended before the ship got there: the goods stay in storage
                    }

                    sale.SetReserved(null);
                }

                ActorState owner = context.Actor(job.GoodsOwner);
                Cargo goods = context.TakeFromStorage(owner, job.Pickup, job.Good, job.Quantity, reserved: true);
                context.Load(ship, owner.Id, goods);
                break;
            }

            case PickupSource.Aboard:
            default:
                break;
        }

        return Aboard(ship, job);
    }

    /// <summary>
    /// At a job's destination the goods are delivered under its haul, or handed over under its sale; if none survived the
    /// trip, that contract fails. A haul cancelled on the way still sets the shipper's goods down here.
    /// </summary>
    public static void Unload(SimulationContext context, Ship ship, ShipJob job)
    {
        bool anything = Aboard(ship, job) > 0m;
        if (job.Haul is { } haulId)
        {
            var haul = (HaulContract)context.Contract(haulId);
            if (haul.Status != ContractStatus.Active)
            {
                HandToShipper(context, haul, context.Actor(haul.Shipper), context.Unload(ship, haul.Shipper, haul.Good, haul.Quantity));
                return;
            }

            if (anything)
            {
                DeliverHaul(context, haul, ship);
            }
            else
            {
                Fail(context, haul, FailureReason.CargoLost);
            }

            return;
        }

        if (job.Delivery is { } saleId && context.Contract(saleId) is SaleContract { Status: ContractStatus.Active } sale)
        {
            if (!anything)
            {
                Fail(context, sale, FailureReason.CargoLost);
                return;
            }

            Cargo goods = context.Unload(ship, job.GoodsOwner, job.Good, sale.Quantity);
            if (HandOverCargo(context, sale, goods) is HandOver.Defaulted or HandOver.Refused)
            {
                context.Load(ship, job.GoodsOwner, goods); // the seller keeps its goods aboard
            }
        }
    }

    /// <summary>The goods a ship's job carries that belong to its goods owner, as they stand now.</summary>
    internal static decimal Aboard(Ship ship, ShipJob job) =>
        ship.Hold.Where(l => l.Owner == job.GoodsOwner && l.Good == job.Good).Sum(l => l.Quantity);
}
