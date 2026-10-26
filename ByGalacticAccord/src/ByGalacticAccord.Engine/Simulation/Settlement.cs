using System;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;

namespace ByGalacticAccord.Engine.Simulation;

/// <summary>What a Sale or Haul settles at: the share of the price due, and what the terms take off it.</summary>
public readonly record struct SettlementTerms(Credits Gross, Credits QualityRefund, Credits LateFee, Credits Tax)
{
    /// <summary>What the performer is paid: the gross less the refund and the late fee.</summary>
    public Credits Net => Credits.Of(Math.Max(0m, Gross.Amount - QualityRefund.Amount - LateFee.Amount));
}

/// <summary>
/// Where contracts settle: goods handed over under a Sale, a Haul delivered, a performer's failure, a payer's
/// default. Kept in one place so the conservation invariants (design §20.2) are easy to audit: every branch
/// below moves goods through <see cref="SimulationContext"/>'s goods primitives and money through its
/// transfers and escrow, and a settled contract's escrow always nets to zero.
/// </summary>
public static class Settlement
{
    /// <summary>What <paramref name="contract"/> settles at for <paramref name="delivered"/> units of <paramref name="quality"/>, <paramref name="ticksLate"/> late.</summary>
    public static SettlementTerms Due(Contract contract, decimal quantity, decimal delivered, decimal quality, long ticksLate)
    {
        // A Sale is paid pro rata for what arrives; a Haul's fee is for the trip (cargo liability is S4's).
        decimal share = contract is SaleContract && quantity > 0m ? Math.Clamp(delivered / quantity, 0m, 1m) : 1m;
        Credits gross = Credits.Of(contract.Price.Amount * share);
        Credits refund = Credits.Of(gross.Amount * (contract.Warranty?.RefundFraction(quality) ?? 0m));
        Credits late = contract.LatePenalty?.PenaltyFor(ticksLate) ?? Credits.Zero;

        // In the Accord a late fee is drawn from the bond and can't exceed it; outside, it comes off the payment.
        decimal limit = contract.UsesEscrow ? contract.LockedBond.Amount : Math.Max(0m, gross.Amount - refund.Amount);
        late = Credits.Of(Math.Min(late.Amount, limit));
        Credits tax = Credits.Of(gross.Amount * contract.TaxRate);
        return new SettlementTerms(gross, refund, late, tax);
    }

    /// <summary>Whether the payer can settle <paramref name="terms"/> at handover. Accord escrow always can.</summary>
    public static bool CanPay(SimulationContext context, Contract contract, SettlementTerms terms) =>
        contract.UsesEscrow || context.Actor(contract.Payer!.Value).Credits >= terms.Net;

    /// <summary>
    /// Hand over the goods the seller set aside for <paramref name="sale"/> (its stock or storage at the place, or
    /// the stock a buyer's ship collects from). Returns false if a buyer outside the Accord can't pay: a default,
    /// and the seller keeps the goods.
    /// </summary>
    public static bool HandOverReserved(SimulationContext context, SaleContract sale)
    {
        ActorState seller = context.Actor(sale.Seller!.Value);
        Holding from = sale.Reserved ?? throw new InvalidOperationException($"{sale.Id} has no goods set aside to hand over.");
        decimal quality = from.Kind == HoldingKind.Storage
            ? context.StorageOf(seller.Id, from.Location)?.Peek(sale.Good, context.CurrentTick)?.Quality ?? 1m
            : 1m;
        // When the buyer's ship collects, the seller has had the goods ready all along: a late collection is the buyer's.
        long ticksLate = sale.Receipt?.Kind == HoldingKind.Ship ? 0 : Late(context, sale);
        SettlementTerms terms = Due(sale, sale.Quantity, sale.Quantity, quality, ticksLate);
        if (!CanPay(context, sale, terms))
        {
            PayerDefault(context, sale);
            return false;
        }

        Cargo goods = from.Kind == HoldingKind.Storage
            ? context.TakeFromStorage(seller, from.Location, sale.Good, sale.Quantity, reserved: true)
            : context.TakeFromStock(seller, sale.Good, sale.Quantity, reserved: true);
        sale.SetReserved(null);
        Complete(context, sale, goods, terms, ticksLate);
        return true;
    }

    /// <summary>
    /// Hand over goods already taken out of a hold for <paramref name="sale"/>. Returns false if a buyer outside the
    /// Accord can't pay: a default, and the caller gives the goods back to the seller.
    /// </summary>
    public static bool HandOverCargo(SimulationContext context, SaleContract sale, Cargo goods)
    {
        long ticksLate = Late(context, sale);
        SettlementTerms terms = Due(sale, sale.Quantity, goods.Quantity, goods.Quality, ticksLate);
        if (!CanPay(context, sale, terms))
        {
            PayerDefault(context, sale);
            return false;
        }

        Complete(context, sale, goods, terms, ticksLate);
        return true;
    }

    /// <summary>A carrier hands a shipper's goods back at the destination and is paid; the goods go on to a Sale the haul serves.</summary>
    public static void DeliverHaul(SimulationContext context, HaulContract haul, Ship ship)
    {
        ActorState shipper = context.Actor(haul.Shipper);
        ActorState carrier = context.Actor(haul.Carrier!.Value);
        Cargo goods = context.Unload(ship, shipper.Id, haul.Good, haul.Quantity);
        long ticksLate = Late(context, haul);
        SettlementTerms terms = Due(haul, haul.Quantity, goods.Quantity, goods.Quality, ticksLate);

        // The goods go to the shipper: handed over under the Sale it sold them under, or into its own place here.
        bool handedOver = haul.Serves is { } id
                          && context.Contract(id) is SaleContract sale
                          && sale.Status == ContractStatus.Active
                          && sale.Place == haul.Destination
                          && HandOverCargo(context, sale, goods);
        if (!handedOver)
        {
            Keep(context, shipper, haul.Destination, goods);
        }

        if (!CanPay(context, haul, terms))
        {
            PayerDefault(context, haul);
            return;
        }

        Pay(context, haul, shipper, carrier, terms);
        Reward(context, carrier, haul, ticksLate, goods.Quality);
        haul.MarkFulfilled();
        context.NoteFulfilled();
        context.Record(new HaulDeliveredEntry(
            context.CurrentTick, haul.Id, carrier.Id, goods.Quantity, goods.Quality, ticksLate, terms.Gross, terms.LateFee, terms.QualityRefund));
    }

    /// <summary>
    /// The performer failed: the goods were lost, or never came. In the Accord the performer's bond goes to the payer
    /// and the payer's escrow comes back; a Sale's set-aside goods are freed and any open haul for it is withdrawn.
    /// </summary>
    public static void Fail(SimulationContext context, Contract contract, FailureReason reason)
    {
        if (contract.Status != ContractStatus.Active)
        {
            return;
        }

        ActorState payer = context.Actor(contract.Payer!.Value);
        ActorState performer = context.Actor(contract.Performer!.Value);
        if (contract.UsesEscrow)
        {
            context.ForfeitFromEscrow(contract, payer, contract.LockedBond);
            context.ReleaseFromEscrow(contract, payer, contract.EscrowHeld);
        }

        if (contract is SaleContract sale)
        {
            Unreserve(context, performer, sale.Reserved, sale.Good, sale.Quantity);
            sale.SetReserved(null);
            foreach (Contract other in context.LiveContractsOf(performer.Id))
            {
                if (other is HaulContract haul && haul.Serves == sale.Id && haul.IsOpen)
                {
                    Market.Withdraw(context, haul);
                }
            }
        }

        Blame(context, performer, contract, Late(context, contract));
        contract.MarkBreached();
        context.NoteBreached();
        context.Record(new DeliveryFailedEntry(context.CurrentTick, contract.Id, performer.Id, reason));

        // A lost haul takes down the Sale it was delivering: the shipper can't hand over goods it no longer has.
        if (contract is HaulContract lost && lost.Serves is { } served && context.Contract(served) is SaleContract downstream)
        {
            Fail(context, downstream, reason);
        }
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
        contract.MarkDefaulted();
        context.NoteDefaulted();
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
        sale.MarkFulfilled();
        context.NoteFulfilled();
        context.Record(new GoodsHandedOverEntry(
            context.CurrentTick, sale.Id, seller.Id, buyer.Id, goods.Quantity, goods.Quality, ticksLate, terms.Gross, terms.LateFee, terms.QualityRefund));
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

    // In the Accord, escrow pays out: the performer gets the net and its bond less the late fee, the payer the
    // late fee and whatever else is left (the undelivered share, the refund, unused tax), and the tax is
    // collected. Outside it, the payer pays the net directly; nothing is taxed.
    private static void Pay(SimulationContext context, Contract contract, ActorState payer, ActorState performer, SettlementTerms terms)
    {
        if (contract.UsesEscrow)
        {
            context.ReleaseFromEscrow(contract, performer, terms.Gross - terms.QualityRefund);
            context.ReleaseFromEscrow(contract, performer, contract.LockedBond - terms.LateFee);
            context.ForfeitFromEscrow(contract, payer, terms.LateFee);
            context.TaxFromEscrow(contract, terms.Tax);
            context.ReleaseFromEscrow(contract, payer, contract.EscrowHeld);
        }
        else
        {
            context.TryTransfer(payer, performer, terms.Net);
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

    private static void Blame(SimulationContext context, ActorState blamed, Contract contract, long ticksLate)
    {
        decimal lateness = 1m + Math.Min(2m, ticksLate / 50m);
        decimal loss = -context.Config.ReputationLossBase * ValueScale(contract) * lateness;
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

    /// <summary>At a job's pickup the goods come aboard. Returns how much of the job's good its owner then has aboard.</summary>
    public static decimal Load(SimulationContext context, Ship ship, ShipJob job)
    {
        switch (job.Source)
        {
            case PickupSource.Haul:
            {
                var haul = (HaulContract)context.Contract(job.Haul!.Value);
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

                context.Load(ship, shipper.Id, goods);
                break;
            }

            case PickupSource.Purchase:
            {
                var sale = (SaleContract)context.Contract(job.Purchase!.Value);
                if (sale.Status == ContractStatus.Active)
                {
                    HandOverReserved(context, sale); // into this ship; outside the Accord the buyer may default
                }

                break;
            }

            case PickupSource.Storage:
            {
                ActorState owner = context.Actor(job.GoodsOwner);
                Cargo goods = context.TakeFromStorage(owner, job.Pickup, job.Good, job.Quantity, reserved: true);
                if (job.Delivery is { } delivery && context.Contract(delivery) is SaleContract sale)
                {
                    sale.SetReserved(null);
                }

                context.Load(ship, owner.Id, goods);
                break;
            }

            case PickupSource.Aboard:
            default:
                break;
        }

        return Aboard(ship, job);
    }

    /// <summary>At a job's destination the goods are delivered under its haul, or handed over under its sale; if none survived the trip, that contract fails.</summary>
    public static void Unload(SimulationContext context, Ship ship, ShipJob job)
    {
        bool anything = Aboard(ship, job) > 0m;
        if (job.Haul is { } haulId)
        {
            var haul = (HaulContract)context.Contract(haulId);
            if (haul.Status != ContractStatus.Active)
            {
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
            if (!HandOverCargo(context, sale, goods))
            {
                context.Load(ship, job.GoodsOwner, goods); // a default: the seller keeps its goods aboard
            }
        }
    }

    /// <summary>The goods a ship's job carries that belong to its goods owner, as they stand now.</summary>
    internal static decimal Aboard(Ship ship, ShipJob job) =>
        ship.Hold.Where(l => l.Owner == job.GoodsOwner && l.Good == job.Good).Sum(l => l.Quantity);
}
