using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Session;
using ByGalacticAccord.Presentation.Text;

namespace ByGalacticAccord.Presentation.Views;

/// <summary>One stat on a ship's sheet: its value, and how its parts make it (design §6.1: every stat explains itself).</summary>
public sealed record StatLine(string Label, string Value, string Explanation);

/// <summary>A line of a ship's history (design §6.4), worded, with the contract it names if any.</summary>
public sealed record HistoryLine(string When, string Text, ContractId? Contract);

/// <summary>
/// The ship sheet (design §6.1, §6.4, §11.3): what it is (a design, or a custom build by its parts), who owns it and where it is,
/// its parts with their grades, each stat with the parts that make it, its running cost and value, where and by whom it was built
/// and what was substituted, and its history: owners, incidents and the contracts it flew.
/// </summary>
public sealed record ShipSheetModel(
    ShipId Id,
    string Title,
    string Sub,
    string Where,
    string PartsTitle,
    IReadOnlyList<InfoLine> Parts,
    string StatsTitle,
    IReadOnlyList<StatLine> Stats,
    string Built,
    string? Substituted,
    string HistoryTitle,
    IReadOnlyList<HistoryLine> History,
    string Flown,
    ContractId? ForSale,
    string CloseLabel);

/// <summary>One line of the orders tree (design §3.3, §11.3): how deep it sits, what it says, its tone, and the contract it opens.</summary>
public sealed record OrderLine(int Depth, string Text, Tone Tone, ContractId? Contract);

/// <summary>The ship sheet, a Build's orders tree, and the words for Builds and ships' Sales.</summary>
public static class ShipView
{
    /// <summary>The ship sheet for <paramref name="id"/>.</summary>
    public static ShipSheetModel Build(GameSession session, ShipId id)
    {
        SimulationContext context = session.Context;
        Ship ship = context.Ship(id);
        string owner = ship.Owner == session.Player.Id ? Templates.Get("Ship.Owner.You") : context.Actor(ship.Owner).Name;
        string sub = Templates.Fill("Ship.Sub", ("owner", owner), ("value", Format.Money(ship.Stats.Value)), ("running", Format.MoneyCents(Credits.Of(ship.Stats.RunningCostPerTick.Amount * GameTime.HoursPerDay))));
        string where = ship.Scrapped
            ? Templates.Get("Ship.Where.Scrapped")
            : ship.Course is { } course
                ? Templates.Fill("Ship.Where.Moving", ("destination", context.Location(course.To).Name), ("when", Format.Date(course.ArrivalTick)))
                : Templates.Fill(ship.ListedFor is not null ? "Ship.Where.ForSale" : ship.IsFree ? "Ship.Where.Free" : "Ship.Where.Busy", ("place", context.Location(ship.Location).Name));

        var parts = ship.Parts.All.Select(p => new InfoLine(
            Templates.Slot(p.Slot), Templates.Fill("Ship.Part.Note", ("value", Format.Money(Credits.Of(p.Info.BasePrice.Amount * ShipPart.ValueShare(p.Grade))))),
            Templates.Part(p), p.Grade == Grade.A ? Tone.Plain : Tone.Warn)).ToList();

        var stats = new List<StatLine>
        {
            Stat(ship.Parts, ShipStat.Cargo, Format.Quantity(ship.Stats.CargoCapacity)),
            Stat(ship.Parts, ShipStat.Speed, Number(ship.Stats.Speed)),
            Stat(ship.Parts, ShipStat.Toughness, Number(ship.Stats.Toughness)),
            Stat(ship.Parts, ShipStat.Reliability, Number(ship.Stats.Reliability)),
        };
        if (ship.Stats.Preservation > 0m)
        {
            stats.Add(Stat(ship.Parts, ShipStat.Preservation, Format.Percent(ship.Stats.Preservation)));
        }

        stats.Add(Stat(ship.Parts, ShipStat.RunningCost, Format.MoneyCents(Credits.Of(ship.Stats.RunningCostPerTick.Amount * GameTime.HoursPerDay))));
        stats.Add(Stat(ship.Parts, ShipStat.Value, Format.Money(ship.Stats.Value)));

        ShipRecord commissioned = ship.History[0];
        string built = ship.BuiltBy is { } yard
            ? Templates.Fill("Ship.Built", ("yard", context.Actor(yard).Name), ("place", context.Location(ship.BuiltAt).Name), ("when", Format.Date(ship.BuiltTick)))
            : Templates.Get("Ship.Built.Start");
        string? substituted = commissioned.Wanted is { } wanted && wanted != ship.Parts ? Substitutions(wanted, ship.Parts) : null;

        var history = new List<HistoryLine>();
        foreach (ShipRecord record in ship.History.Reverse())
        {
            history.Add(new HistoryLine(Format.Date(record.Tick), Record(session, ship, record), record.Contract is { Value: > 0 } c ? c : null));
        }

        string flown = ship.ContractsFlown == 0
            ? Templates.Get("Ship.Flown.None")
            : Templates.Fill(
                "Ship.Flown", ("count", ship.ContractsFlown.ToString("N0", CultureInfo.InvariantCulture)),
                ("recent", string.Join("; ", ship.RecentlyFlown.Reverse().Take(4).Select(c => JournalText.Head(context, context.Contract(c))))));
        return new ShipSheetModel(
            id, JournalText.ShipLine(context, id), sub, where, Templates.Get("Ship.Parts"), parts, Templates.Get("Ship.Stats"), stats, built, substituted,
            Templates.Get("Ship.History"), history, flown, ship.ListedFor, Templates.Get("Ship.Close"));
    }

    /// <summary>
    /// How each part makes a stat, in words: "Light frame A 120, Refrigeration unit A −10"; speed is power over mass, toughness the
    /// frame's times armour's.
    /// </summary>
    public static StatLine Stat(ShipParts parts, ShipStat stat, string value)
    {
        string Terms(ShipStat s) => string.Join(", ", ShipStats.Terms(parts, s).Select(t =>
            Templates.Fill("Ship.Stat.Term", ("part", t.Part is { } p ? Templates.Part(p) : Templates.Get("Ship.Stat.Assembly")), ("value", TermValue(s, t.Value)))));
        string explanation = stat switch
        {
            ShipStat.Speed => Templates.Fill("Ship.Stat.Speed.Explain", ("power", Terms(ShipStat.Power)), ("mass", Terms(ShipStat.Mass))),
            ShipStat.Toughness => Templates.Fill("Ship.Stat.Toughness.Explain", ("terms", Terms(ShipStat.Toughness))),
            _ => Terms(stat),
        };
        return new StatLine(Templates.Get("Ship.Stat." + stat), value, explanation);
    }

    // A part's contribution, in the stat's own unit.
    private static string TermValue(ShipStat stat, decimal value) => stat switch
    {
        ShipStat.RunningCost => Format.MoneyCents(Credits.Of(value * GameTime.HoursPerDay)),
        ShipStat.Value => Format.Money(Credits.Of(value)),
        ShipStat.Preservation => Format.Percent(value),
        ShipStat.Cargo => Format.Quantity(value),
        ShipStat.Toughness => "×" + Number(value),
        _ => Number(value),
    };

    private static string Number(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    // What a build fitted against what it ordered: "Frame: Light frame, grade B for grade A".
    private static string Substitutions(ShipParts wanted, ShipParts fitted)
    {
        var lines = new List<string>();
        foreach (PartSlot slot in Enum.GetValues<PartSlot>())
        {
            if (wanted.In(slot) is { } w && fitted.In(slot) is { } f && w != f)
            {
                lines.Add(Templates.Fill("Ship.Substituted.Part", ("slot", Templates.Slot(slot)), ("fitted", Templates.Part(f)), ("wanted", Templates.Grade(w.Grade))));
            }
        }

        return Templates.Fill("Ship.Substituted", ("parts", string.Join("; ", lines)));
    }

    // A line of a ship's history, worded.
    private static string Record(GameSession session, Ship ship, ShipRecord record)
    {
        SimulationContext context = session.Context;
        string Name(ActorId? id) => id is { } a ? (a == session.Player.Id ? Templates.Get("Ship.Owner.You") : context.Actor(a).Name) : string.Empty;
        string at = record.At is { } l ? context.Location(l).Name : context.Location(ship.Location).Name;
        return record.Kind switch
        {
            ShipEventKind.Commissioned when record.From is null => Templates.Fill("Ship.Record.Registered", ("owner", Name(record.To)), ("place", at)),
            ShipEventKind.Commissioned => Templates.Fill(
                record.Contract is null ? "Ship.Record.BuiltOnSpec" : "Ship.Record.Built",
                ("yard", Name(record.From)), ("owner", Name(record.To)), ("place", at), ("parts", string.Join(", ", ship.Parts.All.Select(Templates.Part)))),
            ShipEventKind.ChangedHands => Templates.Fill("Ship.Record.ChangedHands", ("from", Name(record.From)), ("to", Name(record.To)), ("price", Format.Money(record.Price)), ("place", at)),
            ShipEventKind.Incident => Templates.Fill("Ship.Record.Incident", ("incident", Templates.Get("Ship.Incident." + (record.Incident ?? TransitIncidentKind.MinorDelay))), ("place", at)),
            _ => Templates.Fill("Ship.Record.Scrapped", ("owner", Name(record.From)), ("place", at)),
        };
    }

    /// <summary>
    /// A Build's orders tree (design §3.3, §6.3: "the yard's parts contracts appear on the board"): the Build, then each part it
    /// needs, what is in at the yard, the order posted for what isn't, who took that order and what is carrying it, orders that
    /// failed, and the assembly. A parts order shows the Build it is for above it. Empty for anything else.
    /// </summary>
    public static IReadOnlyList<OrderLine> Orders(GameSession session, Contract contract)
    {
        SimulationContext context = session.Context;
        var lines = new List<OrderLine>();
        BuildContract? build = contract switch
        {
            BuildContract b => b,
            SaleContract { ForBuild: { } id } => (BuildContract)context.Contract(id),
            HaulContract { Serves: { } served } when context.Contract(served) is SaleContract { ForBuild: { } id } => (BuildContract)context.Contract(id),
            _ => null,
        };
        if (build is null)
        {
            return lines;
        }

        string yard = build.Builder is { } y ? context.Actor(y).Name : Templates.Get("Party.Yard");
        string head = build.Client == session.Player.Id ? "Orders.Build.You" : "Orders.Build";
        lines.Add(new OrderLine(0, Templates.Fill(head, ("ship", Templates.ShipName(build.Spec)), ("client", context.Actor(build.Client).Name), ("yard", yard)), Tone.Plain, build.Id));
        var orders = context.Contracts.Values.OfType<SaleContract>().Where(s => s.ForBuild == build.Id).ToList();
        ActorState? builder = build.Builder is { } by ? context.Actor(by) : null;
        YardJob? job = builder?.Works.Jobs.FirstOrDefault(j => j.Build == build.Id);
        foreach (FittedPart part in build.Spec.All)
        {
            var mine = orders.Where(o => ShipPart.PartOf(o.Good) is { } p && p.Slot == part.Slot).ToList();
            bool fitted = build.Ship is not null || job?.Fitting?.In(part.Slot) is not null;
            string state = fitted
                ? Templates.Get("Orders.Part.Fitted")
                : mine.Any(o => o.Status == ContractStatus.Fulfilled) && builder is not null
                    ? Templates.Get("Orders.Part.In")
                    : mine.Any(o => o.IsLive) ? Templates.Get("Orders.Part.Ordered") : Templates.Get("Orders.Part.Waiting");
            lines.Add(new OrderLine(1, Templates.Fill("Orders.Part", ("slot", Templates.Slot(part.Slot)), ("part", Templates.Part(part)), ("state", state)), fitted ? Tone.Gain : Tone.Plain, null));
            foreach (SaleContract order in mine)
            {
                lines.Add(new OrderLine(2, OrderText(context, order), OrderTone(order), order.Id));
                foreach (HaulContract haul in context.Contracts.Values.OfType<HaulContract>().Where(h => h.Serves == order.Id))
                {
                    string carrier = haul.Carrier is { } c ? context.Actor(c).Name : Templates.Get("Orders.NoCarrier");
                    lines.Add(new OrderLine(3, Templates.Fill(
                        "Orders.Haul", ("origin", context.Location(haul.Origin).Name), ("carrier", carrier), ("status", Templates.Get("Orders.Status." + haul.Status))),
                        OrderTone(haul), haul.Id));
                }
            }
        }

        if (job?.AssemblyDone is { } done)
        {
            lines.Add(new OrderLine(1, Templates.Fill("Orders.Assembling", ("yard", yard), ("when", Format.Date(done))), Tone.Yours, null));
        }
        else if (build.Ship is { } ship)
        {
            lines.Add(new OrderLine(1, Templates.Fill("Orders.Built", ("ship", JournalText.ShipLine(context, ship))), Tone.Gain, null));
        }

        return lines;
    }

    private static string OrderText(SimulationContext context, SaleContract order)
    {
        string seller = order.Seller is { } s ? context.Actor(s).Name : Templates.Get("Orders.NoSeller");
        return Templates.Fill(
            "Orders.Order", ("good", Templates.Good(order.Good)), ("price", Format.Money(order.Price)), ("seller", seller),
            ("status", Templates.Get("Orders.Status." + order.Status)), ("deadline", Format.Date(order.DeadlineTick)));
    }

    private static Tone OrderTone(Contract order) => order.Status switch
    {
        ContractStatus.Fulfilled => Tone.Gain,
        ContractStatus.Open or ContractStatus.Active => Tone.Plain,
        _ => Tone.Loss,
    };

    /// <summary>
    /// A Build's terms as sentences (design §3.4, §16.4), from the client's side (the player is never a yard): what is built and where,
    /// the grace and the late fee, the substitution policy, the supply-failure remedies, the deposit and escrow, and the break fees.
    /// </summary>
    public static IReadOnlyList<string> BuildTerms(GameSession session, BuildContract build)
    {
        SimulationContext context = session.Context;
        bool yours = build.Client == session.Player.Id;
        string yard = build.Builder is { } y ? context.Actor(y).Name : Templates.Get("Party.Yard");
        string client = yours ? Templates.Get("Party.You") : context.Actor(build.Client).Name;
        Credits deposit = BuildContract.DepositFor(build.Price);
        decimal tax = build.Builder is null ? context.Config.TaxRate : build.TaxRate;
        var facts = new[]
        {
            ("yard", yard),
            ("Yard", build.Builder is null ? Templates.Get("Party.Yard.Start") : yard),
            ("client", client),
            ("ship", Templates.ShipName(build.Spec)),
            ("parts", string.Join(", ", build.Spec.All.Select(Templates.Part))),
            ("place", build.Yard is { } at ? context.Location(at).Name : Templates.Get("Party.YardsPlace")),
            ("deadline", Format.Date(build.DeadlineTick)),
            ("grace", Format.Duration(build.Terms.GraceHours)),
            ("perDay", Format.Money(Credits.Of((build.LatePenalty?.PenaltyPerTickLate ?? 0m) * GameTime.HoursPerDay))),
            ("cap", Format.Money(build.LatePenalty?.MaxPenalty ?? Credits.Zero)),
            ("deposit", Format.Money(deposit)),
            ("balance", Format.Money(build.Price - deposit)),
            ("tax", Format.Percent(tax)),
            ("bond", Format.Money(build.PerformanceBond)),
        };
        var terms = new List<string>
        {
            Templates.Fill("Term.Build", facts),
            Templates.Fill("Term.Build.Grace", facts),
            Templates.Fill("Term.Build.Substitution." + build.Terms.Package, facts),
            Templates.Fill("Term.Build.SupplyFailure", facts),
            Templates.Fill("Term.Build.Escrow", facts),
            Templates.Fill("Term.Build.BreakFee", facts),
        };
        terms.Add(Templates.Get(build.Builder is null || build.Arbitrated ? "Term.Courts.Build" : "Term.Courts.AccordEdge"));
        return terms;
    }

    /// <summary>A ship's Sale's terms as sentences: it changes hands as it is when signed; escrow and tax in Accord space, neither outside it.</summary>
    public static IReadOnlyList<string> ShipSaleTerms(GameSession session, ShipSaleContract sale)
    {
        SimulationContext context = session.Context;
        var facts = new[]
        {
            ("ship", JournalText.ShipLine(context, sale.Ship)),
            ("place", context.Location(sale.Place).Name),
            ("seller", sale.Seller == session.Player.Id ? Templates.Get("Party.You") : context.Actor(sale.Seller).Name),
            ("tax", Format.Percent(sale.TaxRate)),
        };
        return new[]
        {
            Templates.Fill("Term.Ship", facts),
            Templates.Fill(sale.UsesEscrow ? "Term.Ship.Escrow" : "Term.Ship.Frontier", facts),
        };
    }

    /// <summary>A Build's status for the player, where it is its client: whom it is with, where it stands, the ship built.</summary>
    public static string BuildStatus(GameSession session, BuildContract build)
    {
        SimulationContext context = session.Context;
        string yard = build.Builder is { } y ? context.Actor(y).Name : string.Empty;
        return Templates.Fill(
            "Status.Yours.Build", ("performer", yard), ("price", Format.Money(build.Price)), ("place", build.Yard is { } at ? context.Location(at).Name : string.Empty),
            ("deadline", Format.Date(build.DeadlineTick)));
    }

    /// <summary>
    /// The prompt for a decision on a Build (design §3.4, §8.3): the yard's supply-failure proposal, each remedy with its figures; or a
    /// ship built outside the Build's policy, to accept with the refund or reject.
    /// </summary>
    public static (string Text, IReadOnlyList<DecisionOption> Options) BuildDecision(GameSession session, PlayerDecision decision, BuildContract build)
    {
        SimulationContext context = session.Context;
        string yard = context.Actor(build.Builder!.Value).Name;
        var options = new List<DecisionOption>();
        if (decision.Kind == DecisionKind.SupplyFailure && build.Proposal is { } p)
        {
            FittedPart part = build.Spec.In(p.Slot)!.Value;
            var facts = new[]
            {
                ("yard", yard), ("ship", Templates.ShipName(build.Spec)), ("part", Templates.Part(part)), ("deadline", Format.Date(build.DeadlineTick)),
                ("extended", Format.Date(p.ExtendedDeadline)), ("compensation", Format.Money(p.Compensation)),
                ("substitute", Templates.Part(part with { Grade = p.SubstituteGrade })), ("discount", Format.Money(p.SubstituteDiscount)), ("sooner", Format.Date(p.SubstituteDeadline)),
                ("cancel", Format.Money(p.CancelCompensation)), ("deposit", Format.Money(build.DepositPaid)), ("escrow", Format.Money(build.EscrowHeld - build.LockedBond)),
            };
            options.Add(new DecisionOption(DecisionChoice.Extend, Templates.Get("Decision.Extend"), Templates.Fill("Decision.Extend.Note", facts)));
            if (p.SubstituteDiscount.Amount > 0m)
            {
                options.Add(new DecisionOption(DecisionChoice.Substitute, Templates.Get("Decision.Substitute"), Templates.Fill("Decision.Substitute.Note", facts)));
            }

            options.Add(new DecisionOption(DecisionChoice.TakeRefund, Templates.Get("Decision.TakeRefund"), Templates.Fill("Decision.TakeRefund.Note", facts)));
            return (Templates.Fill("Decision.SupplyFailure", facts), options);
        }

        Ship ship = context.Ship(build.Ship!.Value);
        Credits refund = Terms.Substitution(TermPackage.Lenient, build.Spec, ship.Parts) ?? Credits.Zero;
        var inspect = new[]
        {
            ("yard", yard), ("ship", JournalText.ShipLine(context, ship.Id)),
            ("fitted", string.Join(", ", ship.Parts.All.Select(Templates.Part))), ("ordered", string.Join(", ", build.Spec.All.Select(Templates.Part))),
            ("refund", Format.Money(refund)), ("bond", Format.Money(build.LockedBond)), ("deposit", Format.Money(build.DepositPaid)),
        };
        options.Add(new DecisionOption(DecisionChoice.Accept, Templates.Get("Decision.Accept"), Templates.Fill("Decision.AcceptShip.Note", inspect)));
        options.Add(new DecisionOption(DecisionChoice.Reject, Templates.Get("Decision.Reject"), Templates.Fill("Decision.RejectShip.Note", inspect)));
        return (Templates.Fill("Decision.InspectShip", inspect), options);
    }
}
