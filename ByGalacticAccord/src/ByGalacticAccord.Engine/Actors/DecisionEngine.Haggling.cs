using System;
using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Events;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Engine.Actors;

/// <summary>
/// Haggling (design §3.6, §16.5, §16.6). Every AI actor prices an offer the same way: its reservation is the least it would
/// perform for, or the most it would pay, on the offer's terms (a trader from the costs and utility it already scores
/// contracts by, a producer from its ask, a consumer from what the goods are worth to it, a poster from its own hidden
/// reservation, adjusted for terms other than it posted). Two AI actors settle in closed form at once
/// (<see cref="Price"/>, <see cref="Haggling.Resolve"/>). With the player, offers go back and forth over game time: the
/// player's post reaches the actors that can see it and each answers after a while (<see cref="NoticePost"/>,
/// <see cref="Respond"/>); an offer to an AI gets its reply after 1–12 game hours (<see cref="ExpectReply"/>,
/// <see cref="Reply"/>).
/// </summary>
public static partial class DecisionEngine
{
    /// <summary>A ship's (or an actor's) take on a contract at an offer: its utility (design §4.3), the least price its costs allow, and when it would arrive.</summary>
    private readonly record struct Take(decimal Utility, decimal Floor, long Arrival);

    /// <summary>How an AI actor would perform what it agrees to.</summary>
    internal enum PlanKind
    {
        /// <summary>A producer hands goods over from its stock at the place.</summary>
        Supply,

        /// <summary>A producer sells delivered and posts a haul from its stock.</summary>
        SellViaHaul,

        /// <summary>A trader fills a wanted Sale with one of its ships, from a source.</summary>
        FillWithShip,

        /// <summary>A trader carries a haul with one of its ships.</summary>
        TakeHaul,

        /// <summary>A consumer buys a lot into its stock or storage, and has it brought home.</summary>
        BuyLot,
    }

    internal sealed record Plan(PlanKind Kind, Ship? Ship = null, FillSource? Source = null, Holding? Receipt = null);

    /// <summary>
    /// What an AI actor would agree to on a contract, if offered terms: its reservation (the most it would pay, or the least it
    /// would perform for), whether it pays, the terms it can meet (the offer's, or the nearest it can: a deadline it can make,
    /// a package whose warranty it can keep), and how it would do it (none when it is the poster: the other side performs or pays).
    /// </summary>
    internal sealed record Quote(Credits Reservation, bool Pays, Offer Terms, Plan? Plan);

    private enum AnswerKind
    {
        Accept,
        Counter,
        Walk,
        Pass,
    }

    private readonly record struct Answer(AnswerKind Kind, Offer? Offer, Tell Tell, NegotiationEnd End = NegotiationEnd.WalkedAway);

    /// <summary>The hours an actor adds to a deadline it proposes, so its counter still holds when the other side answers it later.</summary>
    private const long DeadlineSlack = 12;

    /// <summary>A contract's terms on an offer: as they stand when the offer is the contract's own, else rebuilt from it.</summary>
    private static ContractTerms TermsOn(Contract contract, Offer offer) => offer == Offer.Of(contract) ? contract.Terms : Terms.For(contract, offer);

    // How far a take clears both bars: the commit threshold on its utility, and the floor its costs set.
    private static decimal Margin(Take take, decimal price, decimal threshold) => Math.Min(take.Utility - threshold, price - take.Floor);

    /// <summary>
    /// What a trader would sign a contract for: as posted, if that clears its floor and its commit threshold (design §4.3);
    /// else, where an AI poster would pay more than it posted, the closed-form haggle (design §3.6) between the least the
    /// trader would take and the most the poster would pay. Null if there is no deal. The player haggles in game time instead.
    /// </summary>
    private static (Offer Offer, decimal Utility)? Price(SimulationContext context, ActorState trader, Contract contract, Offer posted, Func<decimal, Take?> takeAt)
    {
        if (takeAt(posted.Price.Amount) is not { } asPosted)
        {
            return null;
        }

        decimal threshold = Threshold(trader);
        if (Margin(asPosted, posted.Price.Amount, threshold) >= 0m)
        {
            return (posted, asPosted.Utility);
        }

        decimal ceiling = PosterReservation(context, contract);
        if (Market.HagglesInGameTime(context.Actor(contract.PostedBy)) || ceiling <= posted.Price.Amount
            || takeAt(ceiling) is not { } best || Margin(best, ceiling, threshold) < 0m)
        {
            return null;
        }

        decimal floor = LeastPrice(p => takeAt(p) is { } t ? Margin(t, p, threshold) : -1m, posted.Price.Amount, ceiling);
        if (ClosedForm(context, contract, ceiling, trader, floor) is not { } price)
        {
            return null;
        }

        return takeAt(price) is { } at && Margin(at, price, threshold) >= 0m ? (posted with { Price = Credits.Of(price) }, at.Utility) : null;
    }

    /// <summary>
    /// The price an AI poster and an AI responder agree on in closed form (design §3.6, <see cref="Haggling.Resolve"/>), or null
    /// if they don't: the poster with its hidden reservation (<paramref name="posterReservation"/>), the responder with
    /// <paramref name="responderReservation"/>. Who pays follows from the contract: the poster, except for a lot.
    /// </summary>
    private static decimal? ClosedForm(SimulationContext context, Contract contract, decimal posterReservation, ActorState responder, decimal responderReservation)
    {
        bool posterPays = contract is not SaleContract { Side: SaleSide.ForSale };
        ActorState poster = context.Actor(contract.PostedBy);
        NegotiationOutcome deal = Haggling.Resolve(
            new NegotiationParty(Credits.Of(Math.Round(posterReservation, 2)), poster.Personality.ConcessionRate, posterPays),
            contract.OfferedPrice,
            new NegotiationParty(Credits.Of(Math.Round(responderReservation, 2)), responder.Personality.ConcessionRate, !posterPays),
            context.Config.NegotiationMaxRounds);
        return deal.Agreed ? deal.Fee.Amount : null;
    }

    /// <summary>How long a post waits before its AI poster gives all the ground it ever will in a haggle.</summary>
    private const decimal RipeAfterHours = 48m;

    /// <summary>
    /// Where an AI poster stands in a haggle over its own post: the most it would pay, or the least it would sell a lot for.
    /// A fresh post it holds to, waiting for someone to take it as posted; the longer it waits, the more of the way from its
    /// post toward its hidden reservation it would go, up to a share set by personality (design §4.2: a liquidity-minded actor
    /// keeps more of the surplus), all of it once the post has waited two days. A haul for a Sale its poster has already
    /// signed is on that Sale's clock: its poster gives that ground from the start. A payer goes no further than it can pay,
    /// with its tax, out of what it hasn't already promised (<see cref="Uncommitted"/>). The same for a counter from the
    /// player as for an AI's in closed form.
    /// </summary>
    private static decimal PosterReservation(SimulationContext context, Contract contract)
    {
        ActorState poster = context.Actor(contract.PostedBy);
        decimal posted = contract.OfferedPrice.Amount;
        bool committed = contract is HaulContract { Serves: { } served } && context.Contract(served).Status == ContractStatus.Active;
        decimal ripe = committed ? 1m : Math.Clamp((context.CurrentTick - contract.PostedTick) / RipeAfterHours, 0m, 1m);
        decimal give = (0.25m + (0.5m * (1m - poster.Personality.LiquidityPreference))) * ripe;
        decimal reservation = posted + ((contract.Reservation.Amount - posted) * give);
        return contract is SaleContract { Side: SaleSide.ForSale }
            ? reservation
            : Math.Min(reservation, Uncommitted(context, poster) / (1m + contract.TaxRate));
    }

    /// <summary>
    /// What an actor has that it hasn't already promised: its credits less what it owes at handover on signed contracts
    /// outside the Accord (in Accord space the price is in escrow already).
    /// </summary>
    internal static decimal Uncommitted(SimulationContext context, ActorState actor)
    {
        decimal owed = 0m;
        foreach (Contract contract in context.LiveContractsOf(actor.Id))
        {
            if (contract.Status == ContractStatus.Active && contract.Payer == actor.Id && !contract.UsesEscrow)
            {
                owed += contract.Price.Amount;
            }
        }

        return Math.Max(0m, actor.Credits.Amount - owed);
    }

    /// <summary>
    /// The least price in [<paramref name="lo"/>, <paramref name="hi"/>] at which <paramref name="margin"/> is not negative, for a
    /// margin that grows with the price and holds at <paramref name="hi"/>: a few steps of false position, to within half a
    /// credit, rounded up to the cent.
    /// </summary>
    private static decimal LeastPrice(Func<decimal, decimal> margin, decimal lo, decimal hi)
    {
        decimal fLo = margin(lo);
        if (fLo >= 0m)
        {
            return Math.Ceiling(lo * 100m) / 100m;
        }

        decimal fHi = margin(hi);
        int side = 0;
        for (int i = 0; i < 16 && hi - lo > 0.5m; i++)
        {
            decimal mid = fHi == fLo ? (lo + hi) / 2m : hi - (fHi * (hi - lo) / (fHi - fLo));
            if (mid <= lo || mid >= hi)
            {
                mid = (lo + hi) / 2m;
            }

            decimal fMid = margin(mid);
            if (fMid >= 0m)
            {
                hi = mid;
                fHi = fMid;
                if (side == 1)
                {
                    fLo /= 2m;
                }

                side = 1;
            }
            else
            {
                lo = mid;
                fLo = fMid;
                if (side == -1)
                {
                    fHi /= 2m;
                }

                side = -1;
            }
        }

        return Math.Ceiling(hi * 100m) / 100m;
    }

    /// <summary>The least price at which <paramref name="margin"/> holds, searching up from <paramref name="guess"/>; null if none within reason.</summary>
    private static decimal? LeastPriceFrom(Func<decimal, decimal> margin, decimal guess)
    {
        decimal hi = Math.Max(1m, guess);
        for (int i = 0; i < 10 && margin(hi) < 0m; i++)
        {
            hi *= 2m;
        }

        return margin(hi) < 0m ? null : LeastPrice(margin, 0.01m, hi);
    }

    /// <summary>Count a deal two AI actors signed at a price other than posted.</summary>
    private static void NoteHaggled(SimulationContext context, Contract contract, Signing signed)
    {
        if (signed == Signing.Signed && contract.Price != contract.OfferedPrice)
        {
            context.NoteHaggled();
        }
    }

    // ---- the player's posts reach those who can see them (design §3.6, §16.6) ----

    /// <summary>
    /// The player's new post reaches every AI actor that can see it and could answer it: after the hours its presence puts
    /// between them and the post (<see cref="Visibility.ReachHours"/>), and the hours its answer takes (the tell), it takes the
    /// post as posted, counters, or lets it pass (<see cref="PostReached"/>, <see cref="Respond"/>).
    /// </summary>
    public static void NoticePost(SimulationContext context, Contract post)
    {
        if (!context.Config.PostsDrawAnswers)
        {
            return;
        }

        Offer posted = Offer.Of(post);
        foreach (ActorState actor in context.ActorsOrdered)
        {
            if (actor.Id == post.PostedBy || !Answers(actor, post) || Visibility.ReachHours(context, actor, post) is not { } reach
                || QuoteFor(context, actor, post, posted) is not { } quote)
            {
                continue;
            }

            Answer answer = Decide(context, actor, quote, posted, standing: null, rounds: 0, responding: true);
            if (answer.Kind == AnswerKind.Pass)
            {
                continue;
            }

            long hours = Haggling.ReplyHours(actor.Personality.ConcessionRate, answer.Tell, post.Id.Value + actor.Id.Value);
            context.Schedule(new PostReached(context.CurrentTick + reach + hours, post.Id, actor.Id));
        }
    }

    /// <summary>Whether an actor's habits (design §4.1) make it answer a kind of post: carriers take hauls; producers and carriers fill wanted Sales; consumers buy lots.</summary>
    private static bool Answers(ActorState actor, Contract post) => !actor.IsPlayer && post switch
    {
        HaulContract => actor.MovesCargo && actor.Ships.Count > 0,
        SaleContract { Side: SaleSide.Wanted } wanted => actor.Producer?.Good == wanted.Good || (actor.MovesCargo && actor.Ships.Count > 0),
        SaleContract lot => actor.Demands.Any(d => d.Good == lot.Good),
        _ => false,
    };

    /// <summary>
    /// An AI actor answers the player's post that has reached it (design §16.6): takes it as posted, which signs it at once;
    /// or counters, which opens a haggle; or, too far apart, lets it pass.
    /// </summary>
    public static void Respond(SimulationContext context, ActorState actor, Contract post)
    {
        Offer posted = Offer.Of(post);
        if (!post.IsOpen || context.NegotiationWith(post.Id, actor.Id) is not null || QuoteFor(context, actor, post, posted) is not { } quote)
        {
            return;
        }

        Answer answer = Decide(context, actor, quote, posted, standing: null, rounds: 0, responding: true);
        long hours = context.CurrentTick - post.PostedTick;
        switch (answer.Kind)
        {
            case AnswerKind.Accept:
                TakeOn(context, actor, post, posted);
                break;
            case AnswerKind.Counter:
                Market.Counter(context, actor, post, answer.Offer!, thread: null, hours, answer.Tell);
                break;
        }
    }

    /// <summary>
    /// The player made an offer to an AI actor: schedule its reply. How long it takes follows from what it will say (an
    /// eager actor answers quickly, one near its limit slowly, design §3.6); what it says is decided again when it comes.
    /// </summary>
    public static void ExpectReply(SimulationContext context, ActorState ai, Negotiation thread)
    {
        Answer answer = Prospect(context, ai, thread);
        long hours = Haggling.ReplyHours(ai.Personality.ConcessionRate, answer.Tell, (thread.Id.Value * 31) + thread.Rounds);
        long due = context.CurrentTick + hours;
        thread.AwaitReply(due);
        context.Schedule(new ReplyDue(due, thread.Id, thread.Rounds));
    }

    /// <summary>
    /// An AI actor replies to the player's latest offer (design §16.5): accepts it if it crosses its reservation, walks away
    /// if the gap is too wide or the rounds have run out, or else counters, conceding part of the way toward its reservation.
    /// </summary>
    public static void Reply(SimulationContext context, ActorState ai, Negotiation thread)
    {
        Answer answer = Prospect(context, ai, thread);
        long hours = context.CurrentTick - thread.Latest.Tick;
        Contract contract = context.Contract(thread.Contract);
        switch (answer.Kind)
        {
            case AnswerKind.Accept:
                Market.AcceptOffer(context, ai, thread, hours, answer.Tell);
                break;
            case AnswerKind.Counter:
                Market.Counter(context, ai, contract, answer.Offer!, thread, hours, answer.Tell);
                break;
            default:
                Market.EndNegotiation(context, ai, thread, answer.End, hours);
                break;
        }
    }

    // What an AI would answer to the latest offer in a haggle, as things stand.
    private static Answer Prospect(SimulationContext context, ActorState ai, Negotiation thread)
    {
        Contract contract = context.Contract(thread.Contract);
        Offer theirs = thread.Latest.Offer;
        if (!contract.IsOpen || QuoteFor(context, ai, contract, theirs) is not { } quote)
        {
            return new Answer(AnswerKind.Walk, null, Tell.None, NegotiationEnd.CannotPerform); // no free ship, no goods: it can't do it at any price now
        }

        return Decide(context, ai, quote, theirs, thread.LatestBy(ai.Id)?.Offer.Price, thread.Rounds, responding: false);
    }

    /// <summary>
    /// The haggling rule (design §3.6, §16.5) on one offer: accept it if its terms are ones the actor can meet and its price
    /// crosses the actor's reservation; let it pass (a post) or walk away (an offer) if the gap is too wide; walk away when the
    /// rounds have run out; else counter, on the terms it can meet, at the offer's price if only the terms needed changing,
    /// else at its opening or its standing offer moved part of the way toward its reservation.
    /// </summary>
    private static Answer Decide(SimulationContext context, ActorState ai, Quote quote, Offer theirs, Credits? standing, int rounds, bool responding)
    {
        bool sameTerms = quote.Terms.SameTermsAs(theirs);
        bool crosses = Haggling.Crosses(theirs.Price, quote.Reservation, quote.Pays);
        if (sameTerms && crosses)
        {
            return new Answer(AnswerKind.Accept, theirs, Haggling.TellFor(theirs.Price, quote.Reservation));
        }

        if (sameTerms && Haggling.TooWide(theirs.Price, quote.Reservation, quote.Pays))
        {
            return new Answer(responding ? AnswerKind.Pass : AnswerKind.Walk, null, Tell.Readily, NegotiationEnd.WalkedAway);
        }

        if (rounds >= context.Config.NegotiationMaxRounds)
        {
            return new Answer(AnswerKind.Walk, null, Tell.AtLimit, NegotiationEnd.RoundsRanOut);
        }

        decimal concession = ai.Personality.ConcessionRate;
        Credits price;
        if (crosses)
        {
            price = theirs.Price; // only the terms needed changing
        }
        else if (standing is { } s && Haggling.Crosses(s, quote.Reservation, quote.Pays))
        {
            price = Haggling.Concede(s, quote.Reservation, concession);
        }
        else
        {
            price = Haggling.Opening(quote.Reservation, quote.Pays, concession);
        }

        return new Answer(AnswerKind.Counter, quote.Terms with { Price = price }, Haggling.TellFor(price, quote.Reservation));
    }

    /// <summary>
    /// An AI responder takes a contract on an agreed offer, as it would perform it: its quote's plan, re-made for the world as
    /// it is now. Not if it can no longer do it on those terms at that price.
    /// </summary>
    public static Signing TakeOn(SimulationContext context, ActorState ai, Contract contract, Offer offer)
    {
        if (QuoteFor(context, ai, contract, offer) is not { Plan: { } plan } quote || !quote.Terms.SameTermsAs(offer)
            || !Haggling.Crosses(offer.Price, quote.Reservation, quote.Pays))
        {
            return Signing.CannotPerform;
        }

        Credits price = offer.Price;
        switch (plan.Kind)
        {
            case PlanKind.Supply:
                return Market.SignAgreed(contract, offer, () => Market.Supply(context, ai, (SaleContract)contract, price, Holding.StockAt(ai.Home)));
            case PlanKind.SellViaHaul:
            {
                var wanted = (SaleContract)contract;
                Credits estimate = HaulFeeEstimate(context, ai.Home, wanted.Place);
                (Credits fee, Credits reservation) = HaulTerms(context, ai, wanted.Good, estimate, price.Amount - (Ask(ai).Amount * wanted.Quantity));
                HaulContract? haul = null;
                Signing signed = Market.SignAgreed(
                    contract, offer, () => Market.FillViaHaul(context, ai, wanted, price, FillSource.FromStock(ai), fee, reservation, out haul));
                if (signed == Signing.Signed && haul is not null)
                {
                    ai.Prices(new PriceKey(PriceKind.Haul, wanted.Good), UsualHaulMarkup).Posted(haul.Id);
                }

                return signed;
            }

            case PlanKind.FillWithShip:
                return Market.SignAgreed(contract, offer, () => Market.FillWithShip(context, ai, (SaleContract)contract, price, plan.Ship!, plan.Source!));
            case PlanKind.TakeHaul:
                return Market.SignAgreed(contract, offer, () => Market.TakeHaul(context, ai, (HaulContract)contract, plan.Ship!, price));
            case PlanKind.BuyLot:
            {
                Signing signed = Market.SignAgreed(contract, offer, () => Market.BuyLot(context, ai, (SaleContract)contract, price, plan.Receipt!.Value));
                if (signed == Signing.Signed)
                {
                    BringHome(context, ai);
                }

                return signed;
            }

            default:
                return Signing.CannotPerform;
        }
    }

    // ---- quotes ----

    /// <summary>What <paramref name="ai"/> would agree to on <paramref name="contract"/> if offered <paramref name="offer"/>; null if it wouldn't deal at all.</summary>
    internal static Quote? QuoteFor(SimulationContext context, ActorState ai, Contract contract, Offer offer)
    {
        if (contract.PostedBy == ai.Id)
        {
            return PosterQuote(context, ai, contract, offer);
        }

        return contract switch
        {
            HaulContract haul when ai.MovesCargo => CarrierQuote(context, ai, haul, offer),
            SaleContract { Side: SaleSide.Wanted } wanted when ai.Producer?.Good == wanted.Good => ProducerQuote(context, ai, wanted, offer),
            SaleContract { Side: SaleSide.Wanted } wanted when ai.MovesCargo => FillerQuote(context, ai, wanted, offer),
            SaleContract { Side: SaleSide.ForSale } lot when ai.Demands.Any(d => d.Good == lot.Good) => BuyerQuote(context, ai, lot, offer),
            _ => null,
        };
    }

    /// <summary>
    /// An AI poster facing another's offer on its own post: where it stands (<see cref="PosterReservation"/>), worth more or
    /// less on terms other than it posted (<see cref="TermsValue"/>), and no later deadline than it can live with.
    /// </summary>
    private static Quote PosterQuote(SimulationContext context, ActorState poster, Contract contract, Offer offer)
    {
        Offer posted = Offer.Of(contract);
        bool pays = contract is not SaleContract { Side: SaleSide.ForSale };
        long limit = DeadlineLimit(context, contract);
        Offer terms = offer.Deadline > limit ? offer with { Deadline = limit } : offer;
        decimal reservation = PosterReservation(context, contract) * TermsValue(posted, terms, pays);
        return new Quote(Credits.Of(Math.Round(reservation, 2)), pays, terms, null);
    }

    /// <summary>
    /// The latest deadline a poster accepts: a haul for a Sale its seller signed must arrive inside that Sale's grace; anything
    /// else may run three days past the posted deadline.
    /// </summary>
    private static long DeadlineLimit(SimulationContext context, Contract contract)
    {
        long limit = contract.PostedDeadline + 72;
        if (contract is HaulContract { Serves: { } served } && context.Contract(served) is SaleContract { Status: ContractStatus.Active } sale)
        {
            limit = Math.Min(limit, sale.GraceEndsTick);
        }

        return Math.Max(limit, contract.PostedDeadline);
    }

    /// <summary>
    /// What an offer's terms are worth to a poster against its own, as a multiple of its reservation (first-pass, tuned with the
    /// soak). To a payer each package step looser costs 6% and each stricter adds 2%; a lower late fee costs 2% and a higher
    /// adds 2%; each 12 hours later than posted costs 1%. To the seller of a lot, each step stricter costs it 3%, and holding
    /// the goods longer 0.5% per 12 hours.
    /// </summary>
    private static decimal TermsValue(Offer posted, Offer offer, bool pays)
    {
        int steps = (int)offer.Package - (int)posted.Package; // above zero: looser
        decimal later = Math.Max(0, offer.Deadline - posted.Deadline) / 12m;
        if (!pays)
        {
            return (1m + (0.03m * Math.Max(0, -steps))) * (1m + (0.005m * later));
        }

        decimal value = 1m - (0.06m * Math.Max(0, steps)) + (0.02m * Math.Max(0, -steps));
        value *= offer.LateFeeScale < posted.LateFeeScale ? 0.98m : offer.LateFeeScale > posted.LateFeeScale ? 1.02m : 1m;
        return value * Math.Max(0.5m, 1m - (0.01m * later));
    }

    /// <summary>
    /// The terms nearest an offer that <paramref name="take"/> can meet: the offer itself; else a deadline it can make, with
    /// half a day to spare so the offer still holds a while; else, if the goods would arrive below a package's floor, the next
    /// looser package.
    /// </summary>
    private static Offer? Fit(Offer offer, long arrival, Func<Offer, Take?> take)
    {
        for (TermPackage package = offer.Package; package <= TermPackage.Lenient; package++)
        {
            Offer candidate = offer with { Package = package };
            if (take(candidate) is not null)
            {
                return candidate;
            }

            Offer later = candidate with { Deadline = Math.Max(candidate.Deadline, arrival + DeadlineSlack) };
            if (later != candidate && take(later) is not null)
            {
                return later;
            }
        }

        return null;
    }

    /// <summary>A carrier answering a haul: its cheapest free ship, at the least fee that ship clears its floor and threshold at.</summary>
    private static Quote? CarrierQuote(SimulationContext context, ActorState trader, HaulContract haul, Offer offer)
    {
        Quote? best = null;
        decimal threshold = Threshold(trader);
        foreach (Ship ship in trader.Ships.Select(context.Ship).Where(s => s.IsFree))
        {
            if (context.FindPath(ship.Location, haul.Origin) is not { } repo || context.FindPath(haul.Origin, haul.Destination) is not { } laden)
            {
                continue;
            }

            long arrival = context.CurrentTick + PriceModel.TravelTicks(repo.DistanceTicks, ship.Class.SpeedMultiplier)
                           + Math.Max(1, PriceModel.TravelTicks(laden.DistanceTicks, ship.Class.SpeedMultiplier));
            if (Fit(offer, arrival, o => HaulTake(context, trader, ship, haul, o)) is not { } terms
                || LeastPriceFrom(p => HaulTake(context, trader, ship, haul, terms with { Price = Credits.Of(p) }) is { } t ? Margin(t, p, threshold) : -1m, terms.Price.Amount)
                    is not { } reservation)
            {
                continue;
            }

            if (best is null || reservation < best.Reservation.Amount)
            {
                best = new Quote(Credits.Of(reservation), Pays: false, terms, new Plan(PlanKind.TakeHaul, ship));
            }
        }

        return best;
    }

    /// <summary>A trader answering a wanted Sale: its cheapest free ship and source, at the least price it clears its floor and threshold at.</summary>
    private static Quote? FillerQuote(SimulationContext context, ActorState trader, SaleContract wanted, Offer offer)
    {
        Quote? best = null;
        decimal threshold = Threshold(trader);
        foreach (Ship ship in trader.Ships.Select(context.Ship).Where(s => s.IsFree))
        {
            if (wanted.Quantity > ship.CargoCapacity)
            {
                continue;
            }

            foreach (FillSource source in FillSources(context, trader, wanted, ship))
            {
                LocationId pickup = source.Kind == FillSourceKind.Aboard ? ship.Location : source.At;
                if (context.FindPath(ship.Location, pickup) is not { } repo || context.FindPath(pickup, wanted.Place) is not { } laden)
                {
                    continue;
                }

                long arrival = context.CurrentTick + PriceModel.TravelTicks(repo.DistanceTicks, ship.Class.SpeedMultiplier)
                               + (pickup == wanted.Place ? 0 : Math.Max(1, PriceModel.TravelTicks(laden.DistanceTicks, ship.Class.SpeedMultiplier)));
                if (Fit(offer, arrival, o => FillTake(context, trader, ship, wanted, source, o)) is not { } terms
                    || LeastPriceFrom(p => FillTake(context, trader, ship, wanted, source, terms with { Price = Credits.Of(p) }) is { } t ? Margin(t, p, threshold) : -1m, terms.Price.Amount)
                        is not { } reservation)
                {
                    continue;
                }

                if (best is null || reservation < best.Reservation.Amount)
                {
                    best = new Quote(Credits.Of(reservation), Pays: false, terms, new Plan(PlanKind.FillWithShip, ship, source));
                }
            }
        }

        return best;
    }

    /// <summary>
    /// A producer answering a wanted Sale for its good: at its door, its ask for the goods, handed over from its stock at once;
    /// elsewhere, as it sells delivered (<see cref="SellDelivered"/>): its ask and a carrier's fee with room, on a deadline a
    /// carrier can make and a warranty a standard hold keeps.
    /// </summary>
    private static Quote? ProducerQuote(SimulationContext context, ActorState producer, SaleContract wanted, Offer offer)
    {
        ProducerProfile profile = producer.Producer!;
        if (producer.Available(wanted.Good) < wanted.Quantity)
        {
            return null;
        }

        decimal goods = Ask(producer).Amount * wanted.Quantity;
        if (wanted.Place == producer.Home)
        {
            return new Quote(Credits.Of(Math.Round(goods, 2)), Pays: false, offer, new Plan(PlanKind.Supply));
        }

        int outstanding = context.LiveContractsOf(producer.Id).Count(c => c is SaleContract s && s.Seller == producer.Id && s.Place != producer.Home);
        if (profile.Fullness(producer.Stock(profile.Good)) < SellDeliveredFullness || outstanding >= MaxRemoteSales
            || Hours(context, producer.Home, wanted.Place) is not { } hours)
        {
            return null;
        }

        Credits fee = HaulFeeEstimate(context, producer.Home, wanted.Place);
        if (!Market.CanFundHaul(context, producer, producer.Home, wanted.Place, Credits.Of(fee.Amount * UsualHaulMarkup)))
        {
            return null;
        }

        // A carrier must come for the goods and fly them; and a standard hold must get them there before the buyer could reject them.
        long needs = context.CurrentTick + (hours * 2) + 48;
        Offer terms = offer.Deadline >= needs ? offer : offer with { Deadline = needs + DeadlineSlack };
        decimal arrival = new Cargo(wanted.Good, 1m, 1m).Decayed(hours, 0m).Quality;
        while (Terms.For(wanted, terms).Warranty?.Rejectable(arrival) == true && terms.Package < TermPackage.Lenient)
        {
            terms = terms with { Package = terms.Package + 1 };
        }

        return new Quote(Credits.Of(Math.Round(goods + (fee.Amount * 1.3m) + 1m, 2)), Pays: false, terms, new Plan(PlanKind.SellViaHaul));
    }

    /// <summary>
    /// A consumer answering a lot of a good it wants: what the goods are worth to it, less a carrier's fee home and a little
    /// storage, as it judges lots it buys (<see cref="TryBuyLot"/>); no more than it can pay, and for no more than it has room for.
    /// </summary>
    private static Quote? BuyerQuote(SimulationContext context, ActorState consumer, SaleContract lot, Offer offer)
    {
        ConsumerDemand demand = consumer.Demands.First(d => d.Good == lot.Good);
        decimal room = demand.TargetStock - consumer.Stock(lot.Good) - Inbound(context, consumer, lot.Good);
        if (room < 1m || lot.Quantity > room * 1.2m)
        {
            return null;
        }

        decimal value = Willingness(consumer, lot.Good, demand.TargetStock).Amount * lot.Quantity * 0.85m;
        Holding receipt = Holding.StockAt(consumer.Home);
        if (lot.Place != consumer.Home)
        {
            Credits estimate = HaulFeeEstimate(context, lot.Place, consumer.Home);
            if (estimate.Amount <= 0m || context.StationOperatorAt(lot.Place) is not { } station)
            {
                return null;
            }

            value -= (estimate.Amount * 1.3m) + (station.Station!.StorageRatePerUnitDay.Amount * lot.Quantity * 2m);
            receipt = Holding.StorageAt(lot.Place);
        }

        decimal ceiling = Math.Min(value, Math.Max(0m, consumer.Credits.Amount)) / (1m + lot.TaxRate);
        return ceiling <= 0m ? null : new Quote(Credits.Of(Math.Round(ceiling, 2)), Pays: true, offer, new Plan(PlanKind.BuyLot, Receipt: receipt));
    }
}
