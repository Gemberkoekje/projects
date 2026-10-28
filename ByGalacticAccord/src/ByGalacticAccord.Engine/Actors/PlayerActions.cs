using System;
using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Engine.Actors;

/// <summary>Why the player can't take a contract right now. The interface words each one (design §11.5).</summary>
public enum DealBlock
{
    None,
    NotOpen,
    NoFreeShip,
    TooLarge,
    NoSource,
    NoRoute,
    PickupUnreachable,
    CannotAfford,
    TooLate,

    /// <summary>A counter-offer with no price, or a deadline already past.</summary>
    BadOffer,

    /// <summary>The haggle waits for the other side's reply, not the player's.</summary>
    NotYourTurn,

    /// <summary>Six counter-offers have been made: only accepting or declining is left.</summary>
    RoundsRanOut,
}

/// <summary>What the player posts (design §3.6): a wanted Sale delivered into its storage, a lot of its stored goods, or a haul of them.</summary>
public enum PostKind
{
    Wanted,
    Lot,
    Haul,
}

/// <summary>Why the player can't post a contract as drafted. The interface words each one (design §11.5).</summary>
public enum PostBlock
{
    None,
    BadQuantity,
    BadPrice,
    BadDeadline,
    NoStation,
    NoGoods,
    SamePlace,
    NoRoute,
    CannotAfford,
}

/// <summary>
/// A contract the player means to post: what kind, which goods and how many, where (a Sale's place, or a haul's origin and
/// <see cref="Destination"/>), its first offer (price, deadline, package and late fee), and where else to show it, for a fee
/// (<see cref="ShowAt"/>, design §7.1, D38).
/// </summary>
public sealed record PostDraft(PostKind Kind, GoodType Good, decimal Quantity, LocationId Place, LocationId? Destination, Offer Offer, IReadOnlyList<LocationId>? ShowAt = null)
{
    /// <summary>The post's places: a Sale's place, or a haul's origin and destination.</summary>
    public IReadOnlyList<LocationId> Places => Destination is { } to && Kind == PostKind.Haul ? new[] { Place, to } : new[] { Place };

    /// <summary>What showing it at <see cref="ShowAt"/> costs (<see cref="Advertising.FeeAt(SimulationContext, IReadOnlyList{LocationId}, Credits, LocationId)"/>).</summary>
    public Credits ShowFee(SimulationContext context)
    {
        decimal fee = 0m;
        foreach (LocationId at in (ShowAt ?? Array.Empty<LocationId>()).Distinct())
        {
            fee += Advertising.FeeAt(context, Places, Offer.Price, at).Amount;
        }

        return Credits.Of(fee);
    }
}

/// <summary>Why the player can't show its post further (design §7.1, D38). The interface words each one.</summary>
public enum AdBlock
{
    None,
    NotYours,
    NothingChosen,
    CannotAfford,
}

/// <summary>
/// The player's commands, through exactly the market commands an AI decision uses (plan §2, "symmetric by
/// construction"). The player is just an actor (§4.7.3); the only difference is that a human picks the contract, posts
/// and haggles where an AI scores, and answers the decisions the terms hand it (design §3.4) where an AI decides by
/// utility. Checks return codes, not sentences: the interface's templates turn them into text.
/// </summary>
public static class PlayerActions
{
    /// <summary>The longest a post may run: thirty days.</summary>
    public const long LongestPost = 30 * 24;

    /// <summary>Whether the player can take a haul, and with which ship from where.</summary>
    public sealed record HaulFeasibility(DealBlock Block, Ship? Ship)
    {
        public bool Ok => Block == DealBlock.None;
    }

    /// <summary>Whether the player can fill a delivered Sale, with which ship, and from which source.</summary>
    public sealed record FillFeasibility(DealBlock Block, Ship? Ship, FillSource? Source)
    {
        public bool Ok => Block == DealBlock.None;
    }

    /// <summary>Whether the player can buy a lot for sale and collect it with a ship.</summary>
    public sealed record BuyFeasibility(DealBlock Block, Ship? Ship)
    {
        public bool Ok => Block == DealBlock.None;
    }

    /// <summary>
    /// Whether the player can fill a delivered Sale through a carrier, as a trader with no free ship does: buy the goods
    /// into storage at the source and post a Haul to the place at <see cref="Fee"/>, the usual offer for the trip.
    /// </summary>
    public sealed record BrokerFeasibility(DealBlock Block, FillSource? Source, Credits Fee)
    {
        public bool Ok => Block == DealBlock.None;
    }

    /// <summary>Whether the player can post a haul for a Sale it sold whose goods wait with nothing moving them, and at what fee.</summary>
    public sealed record HaulAgainFeasibility(DealBlock Block, Holding? From, Credits Fee)
    {
        public bool Ok => Block == DealBlock.None;
    }

    /// <summary>
    /// Whether the player believes a contract open (design §7.1, D31): as it stands, where the player sees it live; as the news
    /// left it, where it doesn't. Whether one known only from the news is still open, it finds out by trying.
    /// </summary>
    public static bool Believed(SimulationContext context, ActorState player, Contract contract) =>
        Knowledge.BelievesOpen(context, player, Visibility.Of(context, player), contract);

    /// <summary>The player's ship that would do the job: a free one, else the first (busy, for estimates).</summary>
    public static Ship? ShipFor(SimulationContext context, ActorState player) =>
        player.Ships.Select(context.Ship).FirstOrDefault(s => s.IsFree)
        ?? player.Ships.Select(context.Ship).FirstOrDefault();

    /// <summary>Can the player take this haul right now?</summary>
    public static HaulFeasibility CheckHaul(SimulationContext context, ActorState player, HaulContract haul)
    {
        if (!Believed(context, player, haul) || haul.PostedBy == player.Id)
        {
            return new HaulFeasibility(DealBlock.NotOpen, null);
        }

        Ship? ship = ShipFor(context, player);
        if (ship is null)
        {
            return new HaulFeasibility(DealBlock.NoFreeShip, null);
        }

        if (context.FindPath(haul.Origin, haul.Destination) is null)
        {
            return new HaulFeasibility(DealBlock.NoRoute, ship);
        }

        (LocationId from, _) = Market.NextFree(context, ship);
        if (context.FindPath(from, haul.Origin) is null)
        {
            return new HaulFeasibility(DealBlock.PickupUnreachable, ship);
        }

        if (haul.Quantity > ship.FreeSpace)
        {
            return new HaulFeasibility(DealBlock.TooLarge, ship);
        }

        return new HaulFeasibility(ship.IsFree ? DealBlock.None : DealBlock.NoFreeShip, ship);
    }

    /// <summary>Take a haul as posted.</summary>
    public static Signing TakeHaul(SimulationContext context, ActorState player, HaulContract haul, Ship ship) =>
        Market.TakeHaul(context, player, haul, ship, haul.OfferedPrice);

    /// <summary>
    /// Can the player fill this delivered Sale? The source is the one a trader would pick: its own goods if it has them,
    /// else the producer that gets the goods to the place for least (the price plus the running cost of the trip).
    /// </summary>
    public static FillFeasibility CheckFill(SimulationContext context, ActorState player, SaleContract wanted)
    {
        if (!Believed(context, player, wanted) || wanted.Side != SaleSide.Wanted || wanted.PostedBy == player.Id)
        {
            return new FillFeasibility(DealBlock.NotOpen, null, null);
        }

        Ship? ship = ShipFor(context, player);
        if (ship is null)
        {
            return new FillFeasibility(DealBlock.NoFreeShip, null, null);
        }

        (LocationId from, _) = Market.NextFree(context, ship);
        FillSource? best = null;
        decimal bestCost = decimal.MaxValue;
        foreach (FillSource source in DecisionEngine.FillSources(context, player, wanted, ship.IsFree ? ship : null))
        {
            LocationId pickup = source.Kind == FillSourceKind.Aboard ? ship.Location : source.At;
            if (context.FindPath(from, pickup) is not { } repo || context.FindPath(pickup, wanted.Place) is not { } laden)
            {
                continue;
            }

            long hours = PriceModel.TravelTicks(repo.DistanceTicks, ship.Class.SpeedMultiplier) + PriceModel.TravelTicks(laden.DistanceTicks, ship.Class.SpeedMultiplier);
            decimal cost = source.PurchasePrice.Amount + (ship.Class.RunningCostPerTick.Amount * hours);
            if (cost < bestCost)
            {
                best = source;
                bestCost = cost;
            }
        }

        if (best is null)
        {
            return new FillFeasibility(DealBlock.NoSource, ship, null);
        }

        if (best.Kind != FillSourceKind.Aboard && wanted.Quantity > ship.FreeSpace)
        {
            return new FillFeasibility(DealBlock.TooLarge, ship, best);
        }

        decimal purchase = best.PurchasePrice.Amount * (1m + Terms.TaxRate(context, context.Location(best.At).Reach));
        if (player.Credits.Amount < purchase)
        {
            return new FillFeasibility(DealBlock.CannotAfford, ship, best);
        }

        return new FillFeasibility(ship.IsFree ? DealBlock.None : DealBlock.NoFreeShip, ship, best);
    }

    /// <summary>Fill a delivered Sale as posted, from the source the check chose.</summary>
    public static Signing Fill(SimulationContext context, ActorState player, SaleContract wanted, Ship ship, FillSource source) =>
        Market.FillWithShip(context, player, wanted, wanted.OfferedPrice, ship, source);

    /// <summary>Can the player buy this lot as posted and collect it with a ship?</summary>
    public static BuyFeasibility CheckBuy(SimulationContext context, ActorState player, SaleContract lot)
    {
        if (!Believed(context, player, lot) || lot.Side != SaleSide.ForSale || lot.PostedBy == player.Id)
        {
            return new BuyFeasibility(DealBlock.NotOpen, null);
        }

        Ship? ship = ShipFor(context, player);
        if (ship is null)
        {
            return new BuyFeasibility(DealBlock.NoFreeShip, null);
        }

        (LocationId from, _) = Market.NextFree(context, ship);
        if (context.FindPath(from, lot.Place) is null)
        {
            return new BuyFeasibility(DealBlock.PickupUnreachable, ship);
        }

        if (lot.Quantity > ship.FreeSpace)
        {
            return new BuyFeasibility(DealBlock.TooLarge, ship);
        }

        if (!Market.CanFund(lot, player, lot.OfferedPrice) || player.Credits < lot.OfferedPrice)
        {
            return new BuyFeasibility(DealBlock.CannotAfford, ship);
        }

        return new BuyFeasibility(ship.IsFree ? DealBlock.None : DealBlock.NoFreeShip, ship);
    }

    /// <summary>Buy a lot as posted; the ship goes to collect it.</summary>
    public static Signing Buy(SimulationContext context, ActorState player, SaleContract lot, Ship ship) =>
        Market.BuyLot(context, player, lot, lot.OfferedPrice, Holding.Aboard(ship.Id, lot.Place));

    /// <summary>
    /// Can the player fill this delivered Sale through a carrier? The source is the producer that gets the goods to the
    /// place for least, the goods' price and the usual haul fee together. A carrier needs two days to find the haul and
    /// the trip at standard speed, as a trader brokering allows. The player must be able to pay for the goods and
    /// their tax, fund the haul, and post its bond on the sale.
    /// </summary>
    public static BrokerFeasibility CheckBroker(SimulationContext context, ActorState player, SaleContract wanted)
    {
        if (!Believed(context, player, wanted) || wanted.Side != SaleSide.Wanted || wanted.PostedBy == player.Id)
        {
            return new BrokerFeasibility(DealBlock.NotOpen, null, Credits.Zero);
        }

        FillSource? best = null;
        Credits bestFee = Credits.Zero;
        decimal bestCost = decimal.MaxValue;
        bool late = false;
        foreach (FillSource source in DecisionEngine.FillSources(context, player, wanted, ship: null))
        {
            if (source.At == wanted.Place || context.FindPath(source.At, wanted.Place) is not { } laden || context.StationOperatorAt(source.At) is null)
            {
                continue;
            }

            if (context.CurrentTick + 48 + laden.DistanceTicks > wanted.DeadlineTick)
            {
                late = true;
                continue;
            }

            Credits fee = DecisionEngine.UsualHaulOffer(context, source.At, wanted.Place);
            decimal cost = source.PurchasePrice.Amount + fee.Amount;
            if (cost < bestCost)
            {
                best = source;
                bestFee = fee;
                bestCost = cost;
            }
        }

        if (best is null)
        {
            return new BrokerFeasibility(late ? DealBlock.TooLate : DealBlock.NoSource, null, Credits.Zero);
        }

        decimal goods = best.PurchasePrice.Amount * (1m + Terms.TaxRate(context, context.Location(best.At).Reach));
        decimal haul = bestFee.Amount * (1m + wanted.TaxRate);
        decimal bond = wanted.UsesEscrow ? wanted.PerformanceBond.Amount : 0m;
        if (player.Credits.Amount < goods + haul + bond)
        {
            return new BrokerFeasibility(DealBlock.CannotAfford, best, bestFee);
        }

        return new BrokerFeasibility(DealBlock.None, best, bestFee);
    }

    /// <summary>Fill a delivered Sale as posted through a carrier: buy the goods into storage at the source and post the haul.</summary>
    public static Signing Broker(SimulationContext context, ActorState player, SaleContract wanted, FillSource source, Credits fee) =>
        Market.FillViaHaul(context, player, wanted, wanted.OfferedPrice, source, fee, fee);

    /// <summary>
    /// Can the player post a haul again for a Sale it sold whose goods wait away from the place with nothing moving them?
    /// Its untaken haul is withdrawn and reposted a quarter dearer, as an AI seller raises its offer; one that expired
    /// or was cancelled is replaced the same way. While a carrier near the goods could still deliver inside the sale's grace.
    /// </summary>
    public static HaulAgainFeasibility CheckHaulAgain(SimulationContext context, ActorState player, SaleContract sale)
    {
        if (sale.Status != ContractStatus.Active || sale.Seller != player.Id || sale.Reserved is not { } from || from.Location == sale.Place
            || Market.UnderWay(context, sale))
        {
            return new HaulAgainFeasibility(DealBlock.NotOpen, null, Credits.Zero);
        }

        if (context.FindPath(from.Location, sale.Place) is not { } plan)
        {
            return new HaulAgainFeasibility(DealBlock.NoRoute, from, Credits.Zero);
        }

        HaulContract? last = context.Contracts.Values.OfType<HaulContract>().LastOrDefault(h => h.Serves == sale.Id);
        Credits fee = last is not null ? DecisionEngine.RaisedHaulOffer(last) : DecisionEngine.UsualHaulOffer(context, from.Location, sale.Place);
        if (context.CurrentTick + plan.DistanceTicks + 24 > sale.GraceEndsTick)
        {
            return new HaulAgainFeasibility(DealBlock.TooLate, from, fee);
        }

        if (!Market.CanFundHaul(context, player, from.Location, sale.Place, fee))
        {
            return new HaulAgainFeasibility(DealBlock.CannotAfford, from, fee);
        }

        return new HaulAgainFeasibility(DealBlock.None, from, fee);
    }

    /// <summary>Post a haul again for a Sale the player sold, as the check found it: any untaken one is withdrawn first.</summary>
    public static HaulContract HaulAgain(SimulationContext context, ActorState player, SaleContract sale, Holding from, Credits fee)
    {
        foreach (Contract open in context.LiveContractsOf(player.Id))
        {
            if (open is HaulContract { IsOpen: true } haul && haul.Serves == sale.Id)
            {
                Market.Withdraw(context, haul);
            }
        }

        RoutePlan plan = context.FindPath(from.Location, sale.Place)!;
        return Market.PostHaul(context, player, sale.Good, sale.Quantity, from, sale.Place, fee, fee, DecisionEngine.HaulDeadline(context, sale, plan), sale.Id);
    }

    // ---- posting and haggling (design §3.6) ----

    /// <summary>
    /// Can the player post this? A wanted Sale is delivered into its storage at the place, so the place needs a station, and
    /// the player must be able to pay the price and its tax. A lot or a haul is of goods it keeps free in storage there; a
    /// haul's destination must be another place a route reaches, and the player able to pay the fee.
    /// </summary>
    public static PostBlock CheckPost(SimulationContext context, ActorState player, PostDraft draft)
    {
        Offer offer = draft.Offer;
        if (draft.Quantity <= 0m)
        {
            return PostBlock.BadQuantity;
        }

        if (offer.Price.Amount <= 0m)
        {
            return PostBlock.BadPrice;
        }

        if (offer.Deadline <= context.CurrentTick || offer.Deadline > context.CurrentTick + LongestPost)
        {
            return PostBlock.BadDeadline;
        }

        if (context.StationOperatorAt(draft.Place) is null)
        {
            return PostBlock.NoStation;
        }

        decimal showing = draft.ShowFee(context).Amount;
        if (player.Credits.Amount < showing)
        {
            return PostBlock.CannotAfford;
        }

        if (draft.Kind == PostKind.Wanted)
        {
            decimal tax = Terms.TaxRate(context, context.Location(draft.Place).Reach);
            return player.Credits.Amount - showing < offer.Price.Amount * (1m + tax) ? PostBlock.CannotAfford : PostBlock.None;
        }

        if ((context.StorageOf(player.Id, draft.Place)?.Available(draft.Good) ?? 0m) < draft.Quantity)
        {
            return PostBlock.NoGoods;
        }

        if (draft.Kind == PostKind.Lot)
        {
            return PostBlock.None;
        }

        if (draft.Destination is not { } to || to == draft.Place)
        {
            return PostBlock.SamePlace;
        }

        if (context.FindPath(draft.Place, to) is null)
        {
            return PostBlock.NoRoute;
        }

        decimal haulTax = Terms.TaxRate(context, context.Location(to).Reach);
        return player.Credits.Amount - showing >= offer.Price.Amount * (1m + haulTax) ? PostBlock.None : PostBlock.CannotAfford;
    }

    /// <summary>
    /// Post it, through the market command any poster uses. The player has no hidden reservation: its price is all the engine
    /// knows. The actors that can see it answer over game time.
    /// </summary>
    public static Contract? Post(SimulationContext context, ActorState player, PostDraft draft)
    {
        if (CheckPost(context, player, draft) != PostBlock.None)
        {
            return null;
        }

        Offer offer = draft.Offer;
        Contract posted = draft.Kind switch
        {
            PostKind.Wanted => Market.PostSale(
                context, player, SaleSide.Wanted, draft.Good, draft.Quantity, draft.Place, offer.Price, offer.Price, offer.Deadline,
                Holding.StorageAt(draft.Place), offer.Package, offer.LateFeeScale),
            PostKind.Lot => Market.PostSale(
                context, player, SaleSide.ForSale, draft.Good, draft.Quantity, draft.Place, offer.Price, offer.Price, offer.Deadline,
                Holding.StorageAt(draft.Place), offer.Package, offer.LateFeeScale),
            _ => Market.PostHaul(
                context, player, draft.Good, draft.Quantity, Holding.StorageAt(draft.Place), draft.Destination!.Value, offer.Price, offer.Price,
                offer.Deadline, serves: null, offer.Package, cause: null, offer.LateFeeScale),
        };

        // Shown further at once, for the fee (design §7.1, D38); its own places it is shown at anyway.
        List<LocationId> further = (draft.ShowAt ?? Array.Empty<LocationId>()).Distinct().Where(at => posted.ShownFrom(at) is null).ToList();
        if (further.Count > 0 && posted.IsOpen)
        {
            Advertising.Show(context, player, posted, further);
        }

        return posted;
    }

    /// <summary>Can the player show its open post at these further locations (design §7.1, D38)?</summary>
    public static AdBlock CheckAdvertise(SimulationContext context, ActorState player, Contract contract, IReadOnlyCollection<LocationId> locations)
    {
        if (contract.PostedBy != player.Id || !contract.IsOpen || contract is StorageContract)
        {
            return AdBlock.NotYours;
        }

        if (locations.Count == 0 || Advertising.Quote(context, contract, locations) is not { } fee)
        {
            return AdBlock.NothingChosen;
        }

        return player.Credits.Amount < fee.Amount ? AdBlock.CannotAfford : AdBlock.None;
    }

    /// <summary>Show the player's open post at further locations, paying each one's fee to that system's station operator; the actors there that can answer it hear of it.</summary>
    public static bool Advertise(SimulationContext context, ActorState player, Contract contract, IReadOnlyCollection<LocationId> locations) =>
        CheckAdvertise(context, player, contract, locations) == AdBlock.None && Advertising.Show(context, player, contract, locations);

    /// <summary>
    /// Can the player make this counter-offer? On someone else's post it must be able to take the contract now, as it would
    /// accept it as posted; on its own post (in a responder's <paramref name="thread"/>) it must be able to pay what it offers.
    /// Either way the haggle must wait on the player, and have rounds left.
    /// </summary>
    public static DealBlock CheckCounter(SimulationContext context, ActorState player, Contract contract, Offer offer, Negotiation? thread = null)
    {
        if (!Believed(context, player, contract) || contract is StorageContract)
        {
            return DealBlock.NotOpen;
        }

        thread ??= contract.PostedBy == player.Id ? null : context.NegotiationWith(contract.Id, player.Id);
        if (thread is null && contract.PostedBy == player.Id)
        {
            return DealBlock.NotOpen;
        }

        if (thread is not null)
        {
            if (thread.Contract != contract.Id || !thread.Involves(player.Id) || !thread.IsOpen)
            {
                return DealBlock.NotOpen;
            }

            if (thread.AwaitingFrom != player.Id)
            {
                return DealBlock.NotYourTurn;
            }

            if (thread.Rounds >= context.Config.NegotiationMaxRounds)
            {
                return DealBlock.RoundsRanOut;
            }
        }

        if (offer.Price.Amount <= 0m || offer.Deadline <= context.CurrentTick)
        {
            return DealBlock.BadOffer;
        }

        if (contract.PostedBy != player.Id)
        {
            return Check(context, player, contract);
        }

        return contract switch
        {
            SaleContract { Side: SaleSide.Wanted } when !Market.CanFund(contract, player, offer.Price) || player.Credits.Amount < offer.Price.Amount * (1m + contract.TaxRate) =>
                DealBlock.CannotAfford,
            HaulContract haul when !Market.CanFundHaul(context, player, haul.Origin, haul.Destination, offer.Price) => DealBlock.CannotAfford,
            _ => DealBlock.None,
        };
    }

    /// <summary>Make a counter-offer: on someone else's post to its poster, or on the player's own post to a responder.</summary>
    public static Negotiation? Counter(SimulationContext context, ActorState player, Contract contract, Offer offer, Negotiation? thread = null) =>
        CheckCounter(context, player, contract, offer, thread) == DealBlock.None ? Market.Counter(context, player, contract, offer, thread) : null;

    /// <summary>Accept the other side's latest offer in a haggle: the contract is signed on it.</summary>
    public static Signing AcceptOffer(SimulationContext context, ActorState player, Negotiation thread) => Market.AcceptOffer(context, player, thread);

    /// <summary>Decline the other side's latest offer, ending the haggle.</summary>
    public static bool Decline(SimulationContext context, ActorState player, Negotiation thread) =>
        thread.IsOpen && thread.AwaitingFrom == player.Id && Market.EndNegotiation(context, player, thread, NegotiationEnd.Declined);

    /// <summary>Take one of the player's open posts down; every haggle over it ends.</summary>
    public static bool Withdraw(SimulationContext context, ActorState player, Contract post)
    {
        if (!post.IsOpen || post.PostedBy != player.Id)
        {
            return false;
        }

        Market.Withdraw(context, post);
        return true;
    }

    /// <summary>
    /// The player takes someone else's contract on an agreed offer, by the command it would take it as posted with (its ship
    /// carries the haul, fills the Sale from the source the check picks, or collects the lot), on the agreed terms. Not if it
    /// can no longer do it.
    /// </summary>
    public static Signing TakeOn(SimulationContext context, ActorState player, Contract contract, Offer offer)
    {
        switch (contract)
        {
            case HaulContract haul when CheckHaul(context, player, haul) is { Ok: true } check:
                return Market.SignAgreed(haul, offer, () => Market.TakeHaul(context, player, haul, check.Ship!, offer.Price));
            case SaleContract { Side: SaleSide.Wanted } wanted when CheckFill(context, player, wanted) is { Ok: true } check:
                return Market.SignAgreed(wanted, offer, () => Market.FillWithShip(context, player, wanted, offer.Price, check.Ship!, check.Source!));
            case SaleContract { Side: SaleSide.ForSale } lot when CheckBuy(context, player, lot) is { Ok: true } check:
                return Market.SignAgreed(lot, offer, () => Market.BuyLot(context, player, lot, offer.Price, Holding.Aboard(check.Ship!.Id, lot.Place)));
            default:
                return Signing.CannotPerform;
        }
    }

    /// <summary>Whether the player could take someone else's contract as posted now, and if not, why.</summary>
    public static DealBlock Check(SimulationContext context, ActorState player, Contract contract) => contract switch
    {
        HaulContract haul => CheckHaul(context, player, haul).Block,
        SaleContract { Side: SaleSide.Wanted } wanted => CheckFill(context, player, wanted).Block,
        SaleContract lot => CheckBuy(context, player, lot).Block,
        _ => DealBlock.NotOpen,
    };

    /// <summary>Walk away from a signed contract and pay the break fee.</summary>
    public static bool CancelAtWill(SimulationContext context, ActorState player, Contract contract) =>
        Market.CancelAtWill(context, contract, player);

    /// <summary>Cancel a contract whose counterparty is in default: the player's escrow back and their bond.</summary>
    public static bool CancelForDefault(SimulationContext context, ActorState player, Contract contract) =>
        Market.CancelForDefault(context, contract, player);

    /// <summary>
    /// Answer a decision the terms handed the player, through the same commands an AI's choice runs: cancel for default
    /// or wait; accept goods below the warranty's floor, with the refund, or reject them. False if the answer doesn't fit.
    /// </summary>
    public static bool Decide(SimulationContext context, ActorState player, PlayerDecision decision, DecisionChoice choice)
    {
        if (!decision.Choices.Contains(choice) || context.Contract(decision.Contract) is not { Status: ContractStatus.Active } contract
            || !context.ResolvePlayerDecision(decision))
        {
            return false;
        }

        switch (choice)
        {
            case DecisionChoice.Cancel:
                Market.CancelForDefault(context, contract, player);
                break;
            case DecisionChoice.Accept:
                Settlement.HandOverReserved(context, (SaleContract)contract, inspected: decision.Quality);
                break;
            case DecisionChoice.Reject:
                Settlement.Fail(context, contract, FailureReason.Rejected, decision.Quantity, decision.Quality);
                break;
            case DecisionChoice.Wait:
            default:
                break; // the right to cancel stays; the late fee keeps running to its cap
        }

        return true;
    }

    /// <summary>Put the player's goods aboard a free ship into storage where it is docked.</summary>
    public static bool Store(SimulationContext context, ActorState player, Ship ship, GoodType good)
    {
        if (!ship.IsFree || ship.Owner != player.Id || ship.LotOf(player.Id, good) is not { } lot || context.StationOperatorAt(ship.Location) is null)
        {
            return false;
        }

        Cargo goods = context.Unload(ship, player.Id, good, lot.Quantity);
        context.PutInStorage(player, ship.Location, goods);
        return true;
    }

    /// <summary>Load the player's free goods in storage where a free ship is docked, as much as the hold takes.</summary>
    public static bool Retrieve(SimulationContext context, ActorState player, Ship ship, GoodType good)
    {
        if (!ship.IsFree || ship.Owner != player.Id || context.StorageOf(player.Id, ship.Location) is not { } storage)
        {
            return false;
        }

        decimal quantity = Math.Min(storage.Available(good), ship.FreeSpace);
        if (quantity <= 0m)
        {
            return false;
        }

        Cargo goods = context.TakeFromStorage(player, ship.Location, good, quantity, reserved: false);
        context.Load(ship, player.Id, goods);
        return true;
    }
}
