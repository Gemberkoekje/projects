using System;
using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Engine.Actors;

public static partial class DecisionEngine
{
    /// <summary>How long a yard counts on a part taking to come when it can't say better: five days.</summary>
    private const long TypicalPartLead = 5 * 24;

    /// <summary>How long a stock ship stays posted before its yard reposts it, cheaper; and the span of each markdown.</summary>
    private const long LotPostHours = 60 * 24;

    private const long MarkdownHours = 14 * 24;

    /// <summary>A yard's stock ships are priced down this much of their value for every fortnight they sit unsold.</summary>
    private const decimal LotMarkdown = 0.05m;

    /// <summary>The least share of its value a yard asks for a stock ship.</summary>
    private const decimal LotFloorShare = 0.50m;

    /// <summary>A stock ship that sells within this long of being built tells its yard stock ships sell; one that sits longer, that they don't.</summary>
    public const long SpecSellsWithinHours = 60 * 24;

    /// <summary>
    /// Shipyard heartbeat (design §4.1, §6.3, §15.5): it builds one ship at a time (D35), for the first Build in its queue, or
    /// on spec when nothing waits and its lot has room; it orders the parts it lacks with delivered wanted Sales, assembles
    /// once every part is in, and hands the ship over, or puts it on its lot for sale. It bids on Builds it can see, and
    /// invokes the supply-failure term when a part can't come in time.
    /// </summary>
    public static void RunYard(SimulationContext context, ActorState yard)
    {
        if (yard.Yard is not { } profile)
        {
            return;
        }

        YardState works = yard.Works;
        works.Queue.RemoveAll(j => j.Build is { } queued && context.Contract(queued).Status != ContractStatus.Active);
        if (works.Current is { Build: { } id } job && context.Contract(id).Status != ContractStatus.Active)
        {
            job.Build = null; // its Build ended some other way: the yard finishes the ship on spec
        }

        if (works.Current is { Build: null, AssemblyDone: null } && works.Queue.Count > 0)
        {
            works.Current = null; // a signed Build comes before a ship on spec; what it bought for that stays in stock
        }

        if (works.Current is null && works.Queue.Count > 0)
        {
            works.Current = works.Queue[0];
            works.Queue.RemoveAt(0);
        }

        works.Current ??= SpecJob(context, yard, profile, works);

        // Every job gets its parts in, the ones ahead in line taking from stock first; only the current one assembles.
        var used = new Dictionary<GoodType, decimal>();
        bool first = true;
        foreach (YardJob each in works.Jobs.ToList())
        {
            Progress(context, yard, profile, works, each, used, first);
            first = false;
        }

        BidOnBuilds(context, yard, profile, works);
        SellLot(context, yard, profile);
    }

    /// <summary>
    /// What a yard can see of a part's makers (design §4.3, §6.3: a yard bids from what it can see of part prices and
    /// availability): of the factories that make it, the one whose ask, as the yard knows it, and delivery cost least, with how
    /// long a part takes to come: made first if it saw none in stock, then carried. Null if nobody makes it.
    /// </summary>
    internal static (ActorState Factory, Credits Ask, Credits Delivery, long Lead)? PartSource(SimulationContext context, ActorState yard, GoodType part, Sight sight)
    {
        (ActorState Factory, Credits Ask, Credits Delivery, long Lead)? best = null;
        foreach (ActorState factory in context.ProducersOf(part))
        {
            if (factory.IsPlayer || factory.Factory?.RecipeFor(ShipPart.Of(part).Kind) is not { } recipe)
            {
                continue;
            }

            PriceReading? reading = Knowledge.ReadingOf(context, yard, factory, part, sight);
            Credits ask = reading?.Ask ?? PriceModel.PartAsk(factory.ProfileFor(part)!, 0m);
            long making = reading?.CurrentStock is >= 1m ? 0 : recipe.Hours + GameTime.HoursPerDay;
            Credits delivery = Credits.Zero;
            long lead = making + GameTime.HoursPerDay;
            if (factory.Home != yard.Home)
            {
                if (Hours(context, factory.Home, yard.Home) is not { } hours)
                {
                    continue;
                }

                delivery = Credits.Of(Math.Round(HaulFeeEstimate(context, factory.Home, yard.Home).Amount * UsualHaulMarkup, 2));
                lead = making + (hours * 2) + 48;
            }

            if (best is null || ask.Amount + delivery.Amount < best.Value.Ask.Amount + best.Value.Delivery.Amount)
            {
                best = (factory, ask, delivery, lead);
            }
        }

        return best;
    }

    /// <summary>
    /// What a yard would build a ship of <paramref name="spec"/> for, on <paramref name="package"/>, and when it could hand it
    /// over (design §6.3: its bid comes from what it can see of part prices, its queue and its character): the parts at the asks
    /// it knows with their delivery, and its labour, with its margin, a little more for terms stricter than its habit and a
    /// little less for looser; ready after the ships ahead of it, the slowest part's lead, and its assembly.
    /// </summary>
    internal static (decimal Cost, decimal Reservation, long Ready)? BuildCost(SimulationContext context, ActorState yard, ShipParts spec, TermPackage package)
    {
        YardProfile profile = yard.Yard!;
        Sight sight = Visibility.Of(context, yard);
        decimal parts = 0m;
        long lead = 0;
        foreach (FittedPart part in spec.All)
        {
            if (PartSource(context, yard, part.Good, sight) is not { } source)
            {
                return null;
            }

            parts += source.Ask.Amount + source.Delivery.Amount;
            lead = Math.Max(lead, source.Lead);
        }

        decimal cost = parts + profile.Labour.Amount;
        int steps = (int)package - (int)profile.Habit; // below zero: stricter than its habit
        decimal terms = steps < 0 ? 1m + (0.04m * -steps) : 1m - (0.02m * steps);
        decimal reservation = cost * (1m + profile.Margin) * terms;
        long ready = context.CurrentTick + QueueHours(context, yard, profile) + lead + profile.AssemblyHours;
        return (cost, Math.Round(reservation, 2), ready);
    }

    /// <summary>A yard's stock ship sold: whether it sold soon after it was built tells the yard whether to build another on spec.</summary>
    internal static void NoteLotSale(ActorState yard, Ship ship, long tick)
    {
        if (yard.Yard is not null && ship.BuiltBy == yard.Id)
        {
            yard.Works.SpecSells = tick - ship.BuiltTick <= SpecSellsWithinHours;
        }
    }

    // How many signed Builds a yard has: the one it is building and those queued behind it.
    private static int Load(YardState works) => works.Queue.Count + (works.Current is { Build: not null } ? 1 : 0);

    // How long before a yard is free to start a new build: the ship it is building, then each Build queued behind it.
    private static long QueueHours(SimulationContext context, ActorState yard, YardProfile profile)
    {
        YardState works = yard.Works;
        long hours = 0;
        if (works.Current is { Build: not null } job)
        {
            hours += job.AssemblyDone is { } done ? Math.Max(0, done - context.CurrentTick) : TypicalPartLead + profile.AssemblyHours;
        }

        return hours + (works.Queue.Count * profile.AssemblyHours);
    }

    // With nothing signed waiting, room on its lot and the money for it: the next design it builds on spec.
    private static YardJob? SpecJob(SimulationContext context, ActorState yard, YardProfile profile, YardState works)
    {
        if (yard.Ships.Count >= profile.LotLimit || profile.SpecDesigns.Count == 0 || works.SpecSells == false)
        {
            return null;
        }

        Design design = profile.SpecDesigns[works.NextSpec % profile.SpecDesigns.Count];
        ShipParts parts = Designs.Parts(design);
        if (BuildCost(context, yard, parts, profile.Habit) is not { } cost || yard.Credits.Amount < cost.Cost * 2m)
        {
            return null; // it builds on spec only with money to spare
        }

        works.NextSpec++;
        return new YardJob(null, parts, context.CurrentTick);
    }

    // Get the parts in, set aside the ones in stock for this job (ahead of the jobs behind it), assemble once all are in if it is
    // the current job, and finish the ship when the assembly is done.
    private static void Progress(SimulationContext context, ActorState yard, YardProfile profile, YardState works, YardJob job, Dictionary<GoodType, decimal> used, bool current)
    {
        BuildContract? build = job.Build is { } id ? (BuildContract)context.Contract(id) : null;
        if (job.AssemblyDone is { } done)
        {
            if (current && context.CurrentTick >= done)
            {
                Finish(context, yard, works, job, build);
            }

            return;
        }

        if (build is not null)
        {
            job.Wanted = build.Spec;
        }

        // Past its deadline a yard builds with what it has, inside the Build's policy or not, and lets the client judge the ship.
        bool late = build is not null && context.CurrentTick > build.DeadlineTick;
        var fitted = new Dictionary<PartSlot, FittedPart>();
        var missing = new List<FittedPart>();
        foreach (FittedPart want in job.Wanted.All)
        {
            if (InStock(yard, want, build, late, used) is { } have)
            {
                fitted[want.Slot] = have;
                used[have.Good] = used.GetValueOrDefault(have.Good) + 1m;
            }
            else
            {
                missing.Add(want);
            }
        }

        if (build?.Proposal is not null)
        {
            return; // waiting on the client's choice of remedy
        }

        foreach (FittedPart want in missing)
        {
            if (!Order(context, yard, profile, job, build, want))
            {
                return; // it proposed remedies for a supply failure: the client chooses first
            }
        }

        if (missing.Count > 0 || !current)
        {
            return;
        }

        context.ApplySink(yard, profile.Labour, SinkKind.Labour);
        job.Fitting = new ShipParts(fitted[PartSlot.Frame], fitted[PartSlot.Drive], fitted.TryGetValue(PartSlot.Module, out FittedPart module) ? module : null);
        job.AssemblyDone = context.CurrentTick + profile.AssemblyHours;
    }

    /// <summary>
    /// The part of the kind wanted that a yard has in stock and may fit: the grade wanted, else a better one, else one a grade or
    /// more lower that the Build's substitution policy allows without asking (design §3.4; on spec, grade B at worst); a yard
    /// past its deadline fits any grade it has.
    /// </summary>
    private static FittedPart? InStock(ActorState yard, FittedPart want, BuildContract? build, bool late, Dictionary<GoodType, decimal> used)
    {
        foreach (Grade grade in GradesFor(want.Grade))
        {
            var part = new FittedPart(want.Kind, grade);
            bool allowed = grade <= want.Grade || late
                           || (build is null ? grade <= Grade.B : Terms.Allows(build.Terms.Package, want, part));
            if (allowed && yard.Available(part.Good) - used.GetValueOrDefault(part.Good) >= 1m)
            {
                return part;
            }
        }

        return null;
    }

    // The grade wanted first, then better grades, then lower ones.
    private static IEnumerable<Grade> GradesFor(Grade wanted)
    {
        yield return wanted;
        for (Grade better = wanted - 1; better >= Grade.A; better--)
        {
            yield return better;
        }

        for (Grade lower = wanted + 1; lower <= Grade.C; lower++)
        {
            yield return lower;
        }
    }

    /// <summary>
    /// Keep a part on order: an order still open or signed is on its way; one that ended without the part is a failure upstream
    /// (design §3.3). The yard reorders if it can still make its deadline, and otherwise invokes the supply-failure term
    /// (returns false: the client chooses before it goes on). It orders the grade wanted, or, where the policy lets it and nobody
    /// it knows of holds that grade but someone holds a lower one, the lower one.
    /// </summary>
    private static bool Order(SimulationContext context, ActorState yard, YardProfile profile, YardJob job, BuildContract? build, FittedPart want)
    {
        PartSlot slot = want.Slot;
        Sight sight = Visibility.Of(context, yard);
        if (job.Orders.TryGetValue(slot, out ContractId orderId))
        {
            Contract order = context.Contract(orderId);
            if (order.IsLive)
            {
                return true;
            }

            job.Orders.Remove(slot);
            if (order.Status is not (ContractStatus.Fulfilled or ContractStatus.PartlyFulfilled))
            {
                job.Failures[slot] = job.Failures.GetValueOrDefault(slot) + 1;
                long lead = PartSource(context, yard, want.Good, sight)?.Lead ?? TypicalPartLead;
                if (build is not null && context.CurrentTick + lead + profile.AssemblyHours > build.DeadlineTick)
                {
                    ProposeSupplyFailure(context, yard, profile, build, slot, order);
                    return false;
                }
            }
        }

        GoodType good = ChooseGrade(context, yard, want, build, sight);
        if (PartSource(context, yard, good, sight) is not { } source)
        {
            return true;
        }

        // Wanted as soon as it can come, with three days to spare (a week for a ship on spec), but in time to assemble before the deadline.
        long now = context.CurrentTick;
        long soon = now + source.Lead + (build is null ? 7 * GameTime.HoursPerDay : 72);
        long deadline = build is null ? soon : Math.Max(now + source.Lead + GameTime.HoursPerDay, Math.Min(soon, build.DeadlineTick - profile.AssemblyHours - GameTime.HoursPerDay));
        Credits price = Credits.Of(Math.Round((source.Ask.Amount * 1.02m) + source.Delivery.Amount, 2));
        Credits reservation = Credits.Of(Math.Round((source.Ask.Amount * 1.3m) + (source.Delivery.Amount * 2m), 2));
        if (yard.Credits.Amount < price.Amount * (1m + Terms.TaxRate(context, context.Location(yard.Home).Reach)))
        {
            return true; // it can't fund the order now; it tries again tomorrow
        }

        SaleContract posted = Market.PostPartsOrder(context, yard, good, price, reservation, deadline, profile.Habit, build);
        job.Orders[slot] = posted.Id;
        return true;
    }

    // The grade a yard orders: the one wanted, unless nobody it knows holds it and someone holds a lower grade its policy allows.
    private static GoodType ChooseGrade(SimulationContext context, ActorState yard, FittedPart want, BuildContract? build, Sight sight)
    {
        if (Held(context, yard, want.Good, sight))
        {
            return want.Good;
        }

        for (Grade lower = want.Grade + 1; lower <= Grade.C; lower++)
        {
            var part = new FittedPart(want.Kind, lower);
            bool allowed = build is null ? lower <= Grade.B : Terms.Allows(build.Terms.Package, want, part);
            if (allowed && Held(context, yard, part.Good, sight))
            {
                return part.Good;
            }
        }

        return want.Good;
    }

    // Whether a yard knows of a factory holding a part in stock.
    private static bool Held(SimulationContext context, ActorState yard, GoodType good, Sight sight) =>
        context.ProducersOf(good).Any(f => Knowledge.ReadingOf(context, yard, f, good, sight)?.CurrentStock is >= 1m);

    // The ship is built: handed over for its Build, or onto the lot.
    private static void Finish(SimulationContext context, ActorState yard, YardState works, YardJob job, BuildContract? build)
    {
        works.Current = null;
        ShipParts parts = job.Fitting!;
        if (build is { Status: ContractStatus.Active })
        {
            Ship ship = context.BuildShip(yard, parts, yard, build.Id, build.Ordered);
            build.NoteShip(ship.Id);
            context.Record(new ShipBuiltEntry(context.CurrentTick, build.Id, yard.Id, ship.Id));
            Settlement.HandOverBuild(context, build, ship);
            return;
        }

        Ship built = context.BuildShip(yard, parts, yard, null, job.Wanted);
        context.Record(new StockShipBuiltEntry(context.CurrentTick, yard.Id, built.Id));
    }

    /// <summary>
    /// A yard bids on the AI's Builds it can see (design §6.3), while its queue has room: it takes one as posted if the price
    /// covers what it would build it for and it can make the deadline, or else settles it in closed form with the poster (design
    /// §3.6). One a day. The player's Builds it answers over game time instead (<see cref="Respond"/>, <see cref="BuilderQuote"/>).
    /// </summary>
    private static void BidOnBuilds(SimulationContext context, ActorState yard, YardProfile profile, YardState works)
    {
        if (Load(works) >= profile.QueueLimit)
        {
            return;
        }

        Sight sight = Visibility.Of(context, yard);
        foreach (BuildContract build in Known<BuildContract>(context, yard))
        {
            if (!Knowledge.BelievesOpen(context, yard, sight, build) || Market.HagglesInGameTime(context.Actor(build.PostedBy))
                || BuildCost(context, yard, build.Spec, build.Terms.Package) is not { } cost || cost.Ready > build.DeadlineTick)
            {
                continue;
            }

            decimal price = build.OfferedPrice.Amount;
            if (price < cost.Reservation)
            {
                if (ClosedForm(context, build, PosterReservation(context, build), yard, cost.Reservation) is not { } agreed)
                {
                    continue;
                }

                price = agreed;
            }

            Offer offer = Offer.Of(build) with { Price = Credits.Of(price) };
            Signing signed = Market.SignAgreed(build, offer, () => Market.TakeBuild(context, yard, build, offer.Price));
            NoteHaggled(context, build, signed);
            if (signed == Signing.Signed)
            {
                return;
            }
        }
    }

    /// <summary>
    /// A yard answering the player's Build (design §6.3: yards respond with bids): what it would build it for, on the terms it
    /// bids on by habit and a deadline it can make (design §15.5: Tycho Yards lenient by habit, Helios Orbital strict). Its
    /// counter is its bid; null if its queue is full or it can't get the parts.
    /// </summary>
    private static Quote? BuilderQuote(SimulationContext context, ActorState yard, BuildContract build, Offer offer)
    {
        YardProfile profile = yard.Yard!;
        YardState works = yard.Works;
        if (Load(works) >= profile.QueueLimit || BuildCost(context, yard, build.Spec, profile.Habit) is not { } cost)
        {
            return null;
        }

        Offer terms = offer with { Package = profile.Habit, Deadline = Math.Max(offer.Deadline, cost.Ready + DeadlineSlack) };
        return new Quote(Credits.Of(cost.Reservation), Pays: false, terms, new Plan(PlanKind.Build));
    }

    /// <summary>
    /// A yard's ships for sale (design §6.3: stock, "the yard's asking price, haggled"): each ship it owns that nobody is waiting
    /// on goes up at its value plus half its margin, a little cheaper for each fortnight it has sat, never under half its value.
    /// </summary>
    /// <summary>What a yard in debt asks for a ship on its lot, as a share of its value: what Edge Salvage pays (S8).</summary>
    private const decimal InDebtLotShare = 0.45m;

    private static void SellLot(SimulationContext context, ActorState yard, YardProfile profile)
    {
        foreach (ShipId id in yard.Ships.ToList())
        {
            Ship ship = context.Ship(id);
            if (!ship.IsFree || ship.HoldQuantity > 0m || AwaitedByBuild(context, yard, ship))
            {
                continue;
            }

            decimal value = ship.Stats.Value.Amount;
            decimal fortnights = (context.CurrentTick - ship.BuiltTick) / (decimal)MarkdownHours;
            decimal share = Math.Max(LotFloorShare, 1m + (profile.Margin / 2m) - (LotMarkdown * Math.Floor(fortnights)));
            if (yard.Credits.Amount < 0m)
            {
                // In debt (design §16.8) it sells what it can to pay it off: down to what a salvage yard pays, which one will.
                share = Math.Min(share, InDebtLotShare);
            }

            Credits price = Credits.Of(Math.Round(value * share, 2));
            Credits reservation = Credits.Of(Math.Round(value * Math.Min(share, 0.92m), 2));
            Market.PostShipSale(context, yard, ship, price, reservation, context.CurrentTick + LotPostHours);
        }
    }

    // A ship built for a Build still waiting on its client's inspection.
    private static bool AwaitedByBuild(SimulationContext context, ActorState yard, Ship ship) =>
        context.LiveContractsOf(yard.Id).Any(c => c is BuildContract { Status: ContractStatus.Active } b && b.Ship == ship.Id);

    /// <summary>
    /// A yard invokes the supply-failure term (design §3.4, §16.3): a part's order failed upstream and it can't get another in
    /// time. It proposes more time (as long as a new part takes, and three days) with compensation of 2% of the price a week; a
    /// part a grade lower, at a standard substitution's discount, by a sooner deadline, a factory making do faster; or cancelling,
    /// the deposit and escrow back and 5% of the price. The client chooses: an AI by utility (<see cref="ChooseRemedy"/>), the
    /// player at a hard pause.
    /// </summary>
    private static void ProposeSupplyFailure(SimulationContext context, ActorState yard, YardProfile profile, BuildContract build, PartSlot slot, Contract failed)
    {
        long now = context.CurrentTick;
        FittedPart want = build.Spec.In(slot)!.Value;
        long lead = PartSource(context, yard, want.Good, Visibility.Of(context, yard))?.Lead ?? TypicalPartLead;
        long extended = Math.Max(build.DeadlineTick, now + lead + profile.AssemblyHours + 72);
        decimal weeks = Math.Max(1m, Math.Ceiling((extended - build.DeadlineTick) / 168m));
        Credits compensation = Credits.Of(Math.Round(build.Price.Amount * 0.02m * weeks, 2));
        bool canSubstitute = want.Grade < Grade.C;
        Grade substitute = canSubstitute ? want.Grade + 1 : want.Grade;
        Credits discount = canSubstitute ? Terms.SubstituteDiscount(TermPackage.Standard, want, 1)!.Value : Credits.Zero;
        long sooner = Math.Max(build.DeadlineTick, now + (lead / 2) + profile.AssemblyHours + 48);
        Credits cancel = Credits.Of(Math.Round(build.Price.Amount * 0.05m, 2));

        JournalEntry entry = context.Record(new SupplyFailureEntry(now, build.Id, yard.Id, failed.Id, slot) { Cause = failed.LastEntry });
        var proposal = new SupplyFailure(now, slot, failed.Id, extended, compensation, substitute, discount, sooner, cancel, entry.Seq);
        build.Propose(proposal);
        context.NoteFailure(FailureKind.SupplyFailure);

        ActorState client = context.Actor(build.Client);
        if (client.IsPlayer)
        {
            context.RaisePlayerDecision(new PlayerDecision(DecisionKind.SupplyFailure, build.Id, now, entry.Seq));
            return;
        }

        Market.ChooseRemedy(context, client, build, ChooseRemedy(context, client, build, proposal));
    }

    /// <summary>
    /// An AI client's choice of remedy (design §4.3): what the ship is worth to it, less what it still pays, and the compensation
    /// or discount, less what waiting costs it (a share of the ship's worth a day, more for an ambitious actor) and, for a
    /// substitute, the grade it loses (weighed by its quality bias), against cancelling's compensation.
    /// </summary>
    public static Remedy ChooseRemedy(SimulationContext context, ActorState client, BuildContract build, SupplyFailure proposal)
    {
        decimal worth = (build.PostedBy == client.Id ? build.Reservation : build.Price).Amount;
        decimal surplus = worth - build.Price.Amount;
        decimal perDay = worth * 0.002m * (0.5m + client.Personality.GrowthAmbition);
        decimal extend = surplus + proposal.Compensation.Amount - (perDay * Math.Max(0, proposal.ExtendedDeadline - build.DeadlineTick) / 24m);
        FittedPart part = build.Spec.In(proposal.Slot)!.Value;
        decimal gradeLoss = part.Info.BasePrice.Amount * (ShipPart.ValueShare(part.Grade) - ShipPart.ValueShare(proposal.SubstituteGrade)) * (0.5m + client.Personality.QualityBias);
        decimal substitute = proposal.SubstituteDiscount.Amount > 0m
            ? surplus + proposal.SubstituteDiscount.Amount - gradeLoss - (perDay * Math.Max(0, proposal.SubstituteDeadline - build.DeadlineTick) / 24m)
            : decimal.MinValue;
        decimal cancel = proposal.CancelCompensation.Amount;
        return substitute > extend && substitute > cancel ? Remedy.Substitute : cancel > extend ? Remedy.Cancel : Remedy.Extend;
    }

    /// <summary>
    /// Whether an AI client rejects a ship built outside its Build's policy (design §16.3): accepting gets a ship worth to it its
    /// worth scaled by what the parts fitted are worth against those ordered, for the price less the refund; rejecting gets its
    /// money back, the deposit and the yard's bond.
    /// </summary>
    public static bool WouldRejectShip(SimulationContext context, ActorState client, BuildContract build, Ship ship)
    {
        decimal worth = (build.PostedBy == client.Id ? build.Reservation : build.Price).Amount;
        decimal ordered = ShipStats.From(build.Spec).Value.Amount;
        decimal scaled = worth * (ship.Stats.Value.Amount / Math.Max(1m, ordered));
        decimal refund = (Terms.Substitution(TermPackage.Lenient, build.Spec, ship.Parts) ?? Credits.Zero).Amount;
        decimal accept = scaled - (build.Price.Amount - refund);
        decimal reject = build.UsesEscrow ? build.LockedBond.Amount : 0m;
        return reject > accept;
    }
}
