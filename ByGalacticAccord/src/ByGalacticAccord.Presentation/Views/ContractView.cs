using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Session;
using ByGalacticAccord.Presentation.Text;

namespace ByGalacticAccord.Presentation.Views;

/// <summary>A line of the take-home breakdown: what it is, where the number comes from, and the amount.</summary>
public sealed record MoneyLine(string Label, string Note, string Amount, Tone Tone);

/// <summary>A labelled value with its explanation: a date, an odds figure, a bond.</summary>
public sealed record InfoLine(string Label, string Note, string Value, Tone Tone);

/// <summary>What the player can do with a contract besides taking it as posted.</summary>
public enum ContractCommand
{
    /// <summary>Fill a wanted Sale through a carrier.</summary>
    Broker,

    /// <summary>Walk away from a signed contract and pay the break fee.</summary>
    CancelAtWill,

    /// <summary>Cancel a contract whose counterparty is in default.</summary>
    CancelForDefault,

    /// <summary>Post a haul again for a Sale whose goods wait with nothing moving them.</summary>
    HaulAgain,

    /// <summary>Take one of the player's own open posts down.</summary>
    Withdraw,
}

/// <summary>One of those commands as a button: its label, what it does, and whether it can be done now.</summary>
public sealed record ContractAction(ContractCommand Command, string Label, string Note, bool Enabled);

/// <summary>
/// The contract as a document (design §11.3): parties, obligation, schedule, what it is worth to the player (or, for a
/// lot, what it costs) line by line, what can't be known for sure, and the terms as plain sentences. For a contract
/// that is no longer open, the status says what became of it and the estimate is left out. <see cref="Actions"/> are
/// what else the player can do with it (fill it through a carrier, walk away, cancel for default, withdraw its own post);
/// <see cref="Threads"/> are its haggles with the player (design §11.3, "Negotiations"), and <see cref="Counter"/> the
/// counter-offer the player can make on someone else's post now; <see cref="Why"/> is its record read back through its
/// causes (design §11.2: "why did this happen?"), newest first.
/// </summary>
public sealed record ContractViewModel(
    ContractId Id,
    string KindLine,
    string Head,
    string Sub,
    string? Status,
    Tone StatusTone,
    string CertainTitle,
    IReadOnlyList<MoneyLine> Lines,
    string TakeHomeLabel,
    string TakeHome,
    Tone TakeHomeTone,
    string TakeHomeNote,
    IReadOnlyList<InfoLine> Schedule,
    string UncertainTitle,
    IReadOnlyList<InfoLine> Uncertain,
    string TermsTitle,
    IReadOnlyList<string> Terms,
    string Party,
    string PartyRole,
    bool IsOpen,
    bool CanAccept,
    string AcceptLabel,
    string? Blocked,
    IReadOnlyList<ContractAction> Actions,
    string WhyTitle,
    IReadOnlyList<LogLine> Why,
    string? WhyEnd,
    string ThreadsTitle,
    IReadOnlyList<ThreadViewModel> Threads,
    string CounterTitle,
    OfferFormModel? Counter,
    string? CounterBlocked);

/// <summary>Builds the contract panel and words the outcomes of the player's commands.</summary>
public static class ContractView
{
    public static ContractViewModel Build(GameSession session, ContractId id)
    {
        SimulationContext context = session.Context;
        Contract contract = context.Contract(id);
        ActorState poster = context.Actor(contract.PostedBy);
        bool buying = contract is SaleContract { Side: SaleSide.ForSale };

        string kindLine = Templates.Fill("Contract.Kind", ("kind", Templates.KindLine(contract)));
        string sub = Templates.Fill("Contract.Sub", ("poster", poster.Name), ("jurisdiction", Templates.JurisdictionLong(contract.Jurisdiction)));
        string partyRole = Templates.Fill("Party.Role", ("role", Templates.Role(poster.Role)), ("location", context.Location(poster.Home).Name));

        var lines = new List<MoneyLine>();
        var schedule = new List<InfoLine>();
        var uncertain = new List<InfoLine>();
        string takeHome = string.Empty;
        string takeHomeNote = string.Empty;
        Tone takeTone = Tone.Plain;
        string? status = null;
        Tone statusTone = Tone.Muted;
        bool canAccept = false;
        string? blocked = null;

        var actions = new List<ContractAction>();
        var threads = new List<ThreadViewModel>();
        OfferFormModel? counter = null;
        string? counterBlocked = null;
        if (contract.IsOpen && contract.PostedBy == session.Player.Id)
        {
            // The player's own post: its answers, each a thread to accept, counter or decline; and it can take the post down.
            (status, statusTone) = Status(session, contract);
            threads.AddRange(Threads(session, contract));
            actions.Add(new ContractAction(ContractCommand.Withdraw, Templates.Get("Action.Withdraw"), Templates.Get("Action.Withdraw.Note"), true));
        }
        else if (contract.IsOpen)
        {
            DealBlock block = Check(session, contract);
            canAccept = block == DealBlock.None;
            blocked = canAccept ? null : BlockText(session, contract, block);
            if (block is DealBlock.None or DealBlock.NoFreeShip && DealEstimate.For(context, session.Player, session.PlayerShip, contract) is { } e)
            {
                Estimate(session, contract, e, lines, schedule, uncertain);
                (takeHome, takeTone, takeHomeNote) = Total(session, contract, e);
            }

            if (contract is SaleContract { Side: SaleSide.Wanted } wanted)
            {
                actions.Add(BrokerAction(session, wanted));
            }

            // A haggle with its poster: the thread, if one was started, and a counter-offer while it is the player's turn.
            threads.AddRange(Threads(session, contract));
            Negotiation? open = context.NegotiationWith(contract.Id, session.Player.Id);
            if (open is null)
            {
                Offer posted = Offer.Of(contract);
                DealBlock counterBlock = PlayerActions.CheckCounter(context, session.Player, contract, posted);
                counter = counterBlock == DealBlock.None
                    ? NegotiationView.Form(session, posted, Templates.Get("Thread.Counter.Send"), Templates.Get("Thread.Counter.Note"))
                    : null;
                counterBlocked = counterBlock == DealBlock.None ? null : BlockText(session, contract, counterBlock);
            }
        }
        else
        {
            (status, statusTone) = Status(session, contract);
            if (contract.Status == ContractStatus.Active && contract.Involves(session.Player.Id))
            {
                YourSchedule(session, contract, schedule);
                YourActions(session, contract, actions);
            }

            threads.AddRange(Threads(session, contract)); // how its haggles went
        }

        (List<LogLine> why, string? whyEnd) = Why(session, contract);
        return new ContractViewModel(
            id, kindLine, Head(context, contract), sub, status, statusTone,
            Templates.Get(buying ? "Contract.Cost" : "Contract.Certain"), lines, Templates.Get(buying ? "Cost" : "TakeHome"), takeHome, takeTone, takeHomeNote,
            schedule, Templates.Get("Contract.Uncertain"), uncertain,
            Templates.Fill("Contract.Terms.Package", ("package", Templates.Package(contract.Terms.Package))), TermsOf(session, contract), poster.Name, partyRole,
            contract.IsOpen && contract.PostedBy != session.Player.Id, canAccept, Templates.Get(AcceptKey(contract)), blocked,
            actions, Templates.Get("Contract.Why"), why, whyEnd,
            Templates.Get(contract.PostedBy == session.Player.Id ? "Contract.Answers" : "Contract.Haggle"), threads,
            Templates.Get("Contract.Counter"), counter, counterBlocked);
    }

    /// <summary>
    /// The player's haggles over a contract, as threads: on its own post every answer it drew, open ones first; on someone
    /// else's, its haggle with the poster, the latest first. Once the contract is no longer open they stay, as its history.
    /// </summary>
    private static IEnumerable<ThreadViewModel> Threads(GameSession session, Contract contract) =>
        session.PlayerNegotiations
            .Where(n => n.Contract == contract.Id)
            .OrderByDescending(n => n.IsOpen)
            .ThenByDescending(n => n.Id.Value)
            .Take(contract.PostedBy == session.Player.Id ? int.MaxValue : 1)
            .Select(n => NegotiationView.Thread(session, n));

    /// <summary>The most a "why" chain reads back.</summary>
    public const int WhyLines = 6;

    /// <summary>
    /// A contract's record read back through its causes (design §11.2, §16.2), newest first: from its latest entry to
    /// whatever led to it, while the journal still keeps it and the player can see it.
    /// </summary>
    public static (List<LogLine> Lines, string? End) Why(GameSession session, Contract contract)
    {
        SimulationContext context = session.Context;
        var lines = new List<LogLine>();
        long? seq = contract.LastEntry;
        while (seq is { } s && lines.Count < WhyLines)
        {
            if (context.Journal.Find(s) is not { } entry)
            {
                return (lines, Templates.Get("Why.Lost"));
            }

            ContractId? about = JournalText.ContractOf(entry);
            Contract? other = about is { } a ? context.Contract(a) : null;
            bool yours = JournalText.Subject(entry) == session.Player.Id || JournalText.Addressee(entry) == session.Player.Id || other?.Involves(session.Player.Id) == true;
            if (!yours && other is not null && !Engine.Economy.Visibility.CanSee(context, session.Player, other))
            {
                return (lines, Templates.Get("Why.Hidden"));
            }

            lines.Add(new LogLine(Format.Date(entry.Tick), JournalText.Describe(context, entry), yours, about));
            seq = entry.Cause;
        }

        return (lines, null);
    }

    // Fill a wanted Sale through a carrier: what it would take home, or why it can't.
    private static ContractAction BrokerAction(GameSession session, SaleContract wanted)
    {
        SimulationContext context = session.Context;
        PlayerActions.BrokerFeasibility check = PlayerActions.CheckBroker(context, session.Player, wanted);
        string note = check.Ok && DealEstimate.ForBroker(context, session.Player, session.PlayerShip, wanted) is { } e
            ? Templates.Fill(
                "Action.Broker.Note",
                ("source", context.Location(e.Pickup).Name), ("goods", Format.Money(e.Goods + e.GoodsTax)), ("place", context.Location(wanted.Place).Name),
                ("fee", Format.Money(e.HaulFee)), ("takeHome", Format.Money(e.TakeHome)),
                ("poster", context.Actor(wanted.PostedBy).Name), ("bond", Format.Money(e.Bond)))
            : BlockText(session, wanted, check.Block);
        return new ContractAction(ContractCommand.Broker, Templates.Get("Action.Broker"), note, check.Ok);
    }

    // On the player's signed contract: post a haul again, cancel for default, or walk away.
    private static void YourActions(GameSession session, Contract contract, List<ContractAction> actions)
    {
        SimulationContext context = session.Context;
        ActorState player = session.Player;
        if (contract is SaleContract sale && PlayerActions.CheckHaulAgain(context, player, sale) is { Block: not DealBlock.NotOpen } again)
        {
            string note = again.Ok
                ? Templates.Fill(
                    "Action.HaulAgain.Note", ("origin", context.Location(again.From!.Value.Location).Name), ("destination", context.Location(sale.Place).Name),
                    ("deadline", Format.Date(DecisionEngine.HaulDeadline(context, sale, context.FindPath(again.From!.Value.Location, sale.Place)!))))
                : BlockText(session, sale, again.Block);
            actions.Add(new ContractAction(ContractCommand.HaulAgain, Templates.Fill("Action.HaulAgain", ("fee", Format.Money(again.Fee))), note, again.Ok));
        }

        if (contract.Payer == player.Id && contract.InDefault(session.Now))
        {
            actions.Add(new ContractAction(
                ContractCommand.CancelForDefault, Templates.Get("Action.CancelForDefault"),
                contract.UsesEscrow ? Templates.Fill("Action.CancelForDefault.Note", ("bond", Format.Money(contract.LockedBond))) : Templates.Get("Action.CancelForDefault.Note.Frontier"),
                true));
        }

        actions.Add(new ContractAction(
            ContractCommand.CancelAtWill, Templates.Fill("Action.CancelAtWill", ("fee", Format.Money(contract.Terms.BreakFee))), Templates.Get("Action.CancelAtWill.Note"), true));
    }

    /// <summary>What came of one of the player's commands on <paramref name="contract"/>.</summary>
    public static string CommandText(GameSession session, Contract contract, ContractCommand command, bool done)
    {
        if (!done)
        {
            return Templates.Get("Block.NotOpen");
        }

        return command switch
        {
            ContractCommand.CancelAtWill => Templates.Fill("Outcome.CancelledAtWill", ("paid", Format.Money(contract.Cancellation?.Paid ?? Credits.Zero))),
            ContractCommand.CancelForDefault => Templates.Fill("Outcome.CancelledForDefault", ("paid", Format.Money(contract.Cancellation?.Paid ?? Credits.Zero))),
            ContractCommand.HaulAgain => Templates.Fill(
                "Outcome.HaulAgain",
                ("fee", Format.Money(session.Context.Contracts.Values.OfType<HaulContract>().Last(h => h.Serves == contract.Id).OfferedPrice))),
            ContractCommand.Withdraw => Templates.Get("Outcome.Withdrawn"),
            _ => string.Empty,
        };
    }

    /// <summary>What came of the player's attempt to fill <paramref name="wanted"/> through a carrier.</summary>
    public static string BrokerText(GameSession session, SaleContract wanted, DealOutcome outcome)
    {
        SimulationContext context = session.Context;
        if (outcome.Signing is not { } signing)
        {
            return BlockText(session, wanted, outcome.Block);
        }

        if (signing != Signing.Signed)
        {
            return OutcomeText(session, wanted, outcome);
        }

        HaulContract? haul = context.Contracts.Values.OfType<HaulContract>().LastOrDefault(h => h.Serves == wanted.Id);
        return Templates.Fill(
            "Outcome.Brokered", ("price", Format.Money(wanted.Price)), ("quantity", Format.Quantity(wanted.Quantity)), ("good", Templates.Good(wanted.Good)),
            ("source", context.Location(haul?.Origin ?? wanted.Place).Name), ("place", context.Location(wanted.Place).Name),
            ("fee", Format.Money(haul?.OfferedPrice ?? Credits.Zero)));
    }

    /// <summary>Whether the player could take <paramref name="contract"/> now, and if not, why.</summary>
    public static DealBlock Check(GameSession session, Contract contract) => PlayerActions.Check(session.Context, session.Player, contract);

    /// <summary>What the contract is, in a line: the goods, and where.</summary>
    public static string Head(SimulationContext context, Contract contract) => JournalText.Head(context, contract);

    /// <summary>Why the player can't take <paramref name="contract"/>, in words.</summary>
    public static string BlockText(GameSession session, Contract contract, DealBlock block)
    {
        SimulationContext context = session.Context;
        Ship ship = session.PlayerShip;
        string shipClass = Templates.ShipClass(ship.Type);
        (LocationId pickup, LocationId destination, GoodType good, decimal quantity) = Shape(contract);
        switch (block)
        {
            case DealBlock.NoFreeShip:
            {
                (LocationId at, long tick) = Market.NextFree(context, ship);
                return Templates.Fill("Block.NoFreeShip", ("shipClass", shipClass), ("when", Format.Date(tick)), ("where", context.Location(at).Name));
            }

            case DealBlock.TooLarge:
                return Templates.Fill(
                    "Block.TooLarge", ("quantity", Format.Quantity(quantity)), ("shipClass", shipClass), ("capacity", Format.Quantity(ship.FreeSpace)));
            case DealBlock.NoSource:
                return Templates.Fill("Block.NoSource", ("quantity", Format.Quantity(quantity)), ("good", Templates.Good(good)));
            case DealBlock.NoRoute:
                return Templates.Fill("Block.NoRoute", ("origin", context.Location(pickup).Name), ("destination", context.Location(destination).Name));
            case DealBlock.PickupUnreachable:
                return Templates.Fill("Block.PickupUnreachable", ("shipClass", shipClass), ("origin", context.Location(pickup).Name));
            case DealBlock.CannotAfford:
            {
                Credits cost = contract switch
                {
                    SaleContract { Side: SaleSide.ForSale } lot => lot.OfferedPrice + Credits.Of(lot.OfferedPrice.Amount * lot.TaxRate),
                    SaleContract wanted when PlayerActions.CheckFill(context, session.Player, wanted).Source is { } source => source.PurchasePrice,
                    _ => Credits.Zero,
                };
                return Templates.Fill("Block.CannotAfford", ("cost", Format.Money(cost)), ("credits", Format.Money(session.Player.Credits)));
            }

            case DealBlock.TooLate:
                return Templates.Get("Block.TooLate");
            case DealBlock.BadOffer:
                return Templates.Get("Block.BadOffer");
            case DealBlock.NotYourTurn:
                return Templates.Get("Block.NotYourTurn");
            case DealBlock.RoundsRanOut:
                return Templates.Get("Block.RoundsRanOut");
            case DealBlock.NotOpen:
            default:
                return Templates.Get("Block.NotOpen");
        }
    }

    /// <summary>What came of the player's attempt to take <paramref name="contract"/>.</summary>
    public static string OutcomeText(GameSession session, Contract contract, DealOutcome outcome)
    {
        SimulationContext context = session.Context;
        if (outcome.Signing is not { } signing)
        {
            return BlockText(session, contract, outcome.Block);
        }

        switch (signing)
        {
            case Signing.Signed:
            {
                Ship ship = session.PlayerShip;
                string where = context.Location(ship.Job?.Pickup ?? ship.Location).Name;
                var facts = new[]
                {
                    ("price", Format.Money(contract.Price)),
                    ("shipClass", Templates.ShipClass(ship.Type)),
                    ("origin", where),
                    ("when", Format.Date(ship.Course?.ArrivalTick ?? session.Now)),
                    ("bond", Format.Money(contract.LockedBond)),
                };
                string kind = contract switch { HaulContract => "Haul", SaleContract { Side: SaleSide.Wanted } => "Fill", _ => "Buy" };
                string key = ship.Job is null ? "Outcome.Signed.Fill.Now" : ship.Course is null ? $"Outcome.Signed.{kind}.Here" : $"Outcome.Signed.{kind}.Away";
                string signed = Templates.Fill(key, facts);
                return contract.UsesEscrow && contract.LockedBond.Amount > 0m && contract.Performer == session.Player.Id
                    ? signed + " " + Templates.Fill("Outcome.Signed.Bond", facts)
                    : signed;
            }

            case Signing.PayerCannotFund:
                return Templates.Fill("Outcome.PayerCannotFund", ("poster", context.Actor(contract.PostedBy).Name));
            case Signing.SupplierDeclined:
                return Templates.Get("Outcome.SupplierDeclined");
            case Signing.NotOpen:
            default:
                return Templates.Get("Block.NotOpen");
        }
    }

    private static string AcceptKey(Contract contract) => contract switch
    {
        SaleContract { Side: SaleSide.Wanted } => "Action.Fill",
        SaleContract => "Action.Buy",
        _ => "Action.Accept",
    };

    // Where the goods are picked up, where they go, what and how many.
    private static (LocationId Pickup, LocationId Destination, GoodType Good, decimal Quantity) Shape(Contract contract) => contract switch
    {
        HaulContract haul => (haul.Origin, haul.Destination, haul.Good, haul.Quantity),
        SaleContract sale => (sale.Place, sale.Place, sale.Good, sale.Quantity),
        _ => (contract.Places[0], contract.Places[0], GoodType.Food, 0m),
    };

    private static void Estimate(
        GameSession session, Contract contract, DealEstimate e, List<MoneyLine> lines, List<InfoLine> schedule, List<InfoLine> uncertain)
    {
        SimulationContext context = session.Context;
        string shipClass = Templates.ShipClass(e.Ship.Type);
        string poster = context.Actor(contract.PostedBy).Name;
        string pickup = context.Location(e.Pickup).Name;
        (_, LocationId to, GoodType good, decimal quantity) = Shape(contract);
        string destination = context.Location(to).Name;
        string tax = Format.Percent(contract.TaxRate);
        string unit = Format.MoneyCents(quantity <= 0m ? 0m : contract.OfferedPrice.Amount / quantity);

        switch (e.Kind)
        {
            case DealKind.Haul:
                lines.Add(new MoneyLine(
                    Templates.Get("Line.Fee"),
                    contract.UsesEscrow ? Templates.Fill("Line.Fee.Note", ("poster", poster), ("tax", tax)) : Templates.Get("Line.Fee.Note.Frontier"),
                    Format.MoneyCents(e.Income), Tone.Gain));
                break;
            case DealKind.Fill:
                lines.Add(new MoneyLine(
                    Templates.Get("Line.Price"),
                    contract.UsesEscrow
                        ? Templates.Fill("Line.Price.Note", ("poster", poster), ("tax", tax), ("unit", unit))
                        : Templates.Fill("Line.Price.Note.Frontier", ("unit", unit)),
                    Format.MoneyCents(e.Income), Tone.Gain));
                if (e.Goods.Amount > 0m && e.Source?.Producer is { } supplier)
                {
                    lines.Add(new MoneyLine(
                        Templates.Fill("Line.Goods", ("source", pickup)),
                        Templates.Fill(
                            "Line.Goods.Note", ("quantity", Format.Quantity(quantity)), ("good", Templates.Good(good)),
                            ("supplier", context.Actor(supplier).Name), ("unit", Format.MoneyCents(e.Goods.Amount / quantity))),
                        Format.MoneyCents(-e.Goods.Amount), Tone.Loss));
                }
                else
                {
                    lines.Add(new MoneyLine(
                        Templates.Get("Line.Goods.Own"),
                        Templates.Fill(e.Source?.Kind == FillSourceKind.Storage ? "Line.Goods.Own.Storage" : "Line.Goods.Own.Hold", ("place", pickup), ("good", Templates.Good(good))),
                        Format.MoneyCents(0m), Tone.Muted));
                }

                if (e.GoodsTax.Amount > 0m)
                {
                    lines.Add(new MoneyLine(
                        Templates.Get("Line.GoodsTax"), Templates.Fill("Line.GoodsTax.Note", ("tax", Format.Percent(e.GoodsTax.Amount / e.Goods.Amount)), ("source", pickup)),
                        Format.MoneyCents(-e.GoodsTax.Amount), Tone.Loss));
                }

                break;
            case DealKind.Buy:
                lines.Add(new MoneyLine(
                    Templates.Get("Line.LotPrice"), Templates.Fill("Line.LotPrice.Note", ("unit", unit), ("poster", poster)),
                    Format.MoneyCents(-e.Goods.Amount), Tone.Loss));
                if (e.GoodsTax.Amount > 0m)
                {
                    lines.Add(new MoneyLine(
                        Templates.Get("Line.LotTax"), Templates.Fill("Line.LotTax.Note", ("tax", tax)), Format.MoneyCents(-e.GoodsTax.Amount), Tone.Loss));
                }

                break;
        }

        string rate = Format.MoneyCents(e.Ship.Class.RunningCostPerTick);
        if (e.Running.Amount > 0m || e.Kind != DealKind.Buy)
        {
            string runningNote = e.Kind == DealKind.Buy
                ? Templates.Fill("Line.Running.Note.Collect", ("hours", Format.Hours(e.RepoTicks)), ("rate", rate), ("shipClass", shipClass), ("place", pickup))
                : e.RepoTicks > 0
                    ? Templates.Fill(
                        "Line.Running.Note.Repo",
                        ("hours", Format.Hours(e.RepoTicks + e.LadenTicks)), ("rate", rate), ("shipClass", shipClass),
                        ("repoHours", Format.Hours(e.RepoTicks)), ("origin", pickup), ("ladenHours", Format.Hours(e.LadenTicks)))
                    : Templates.Fill("Line.Running.Note", ("hours", Format.Hours(e.LadenTicks)), ("rate", rate), ("shipClass", shipClass));
            lines.Add(new MoneyLine(Templates.Get("Line.Running"), runningNote, Format.MoneyCents(-e.Running.Amount), Tone.Loss));
        }

        if (e.Docking.Amount > 0m)
        {
            lines.Add(new MoneyLine(
                Templates.Fill("Line.Docking", ("destination", destination)), Templates.Get("Line.Docking.Note"), Format.MoneyCents(-e.Docking.Amount), Tone.Loss));
        }

        if (e.QualityRefund.Amount > 0m && contract.Warranty is { } warranty)
        {
            lines.Add(new MoneyLine(
                Templates.Get("Line.Refund"),
                Templates.Fill(
                    "Line.Refund.Note",
                    ("good", Templates.Good(good)), ("quality", Format.Percent(e.ArrivalQuality)), ("hours", Format.Hours(e.LadenTicks)), ("min", Format.Percent(warranty.MinQuality))),
                Format.MoneyCents(-e.QualityRefund.Amount), Tone.Loss));
        }

        if (e.LateFee.Amount > 0m && contract.LatePenalty is { } late)
        {
            lines.Add(new MoneyLine(
                Templates.Get("Line.LateFee"),
                Templates.Fill(
                    contract.UsesEscrow ? "Line.LateFee.Note" : "Line.LateFee.Note.Frontier",
                    ("late", Format.Duration(e.TicksLate)), ("perHour", Format.MoneyCents(late.PenaltyPerTickLate))),
                Format.MoneyCents(-e.LateFee.Amount), Tone.Loss));
        }

        long now = session.Now;
        if (e.StartTick > now)
        {
            schedule.Add(new InfoLine(
                Templates.Fill("Eta.Free", ("shipClass", shipClass), ("where", context.Location(e.StartAt).Name)), string.Empty, Format.Date(e.StartTick), Tone.Plain));
        }

        if (e.Kind == DealKind.Buy)
        {
            schedule.Add(new InfoLine(Templates.Fill("Eta.Collect", ("place", pickup)), string.Empty, Format.Date(e.PickupTick), Tone.Plain));
            schedule.Add(new InfoLine(Templates.Get("Eta.Until"), string.Empty, Format.Date(contract.DeadlineTick), e.PickupTick > contract.DeadlineTick ? Tone.Loss : Tone.Plain));
            return;
        }

        if (e.RepoTicks > 0 || e.Pickup != to)
        {
            schedule.Add(new InfoLine(Templates.Fill("Eta.Pickup", ("origin", pickup)), string.Empty, Format.Date(e.PickupTick), Tone.Plain));
        }

        schedule.Add(new InfoLine(
            Templates.Fill("Eta.Arrive", ("destination", destination)), string.Empty, Format.Date(e.ArrivalTick), e.TicksLate > 0 ? Tone.Loss : Tone.Plain));
        schedule.Add(Deadline(contract.DeadlineTick, e.ArrivalTick));

        if (e.Laden is { } laden)
        {
            uncertain.Add(new InfoLine(
                Templates.Get("Uncertain.Incident"),
                Templates.Fill(
                    "Uncertain.Incident.Note",
                    ("band", Templates.Danger(laden.Band)), ("danger", Format.Percent(laden.MaxDanger)),
                    ("delay", Format.Percent(IncidentResolver.Share(TransitIncidentKind.MinorDelay))),
                    ("damage", Format.Percent(IncidentResolver.Share(TransitIncidentKind.CargoDamage))),
                    ("partial", Format.Percent(IncidentResolver.Share(TransitIncidentKind.PartialLoss))),
                    ("costs", Format.Percent(IncidentResolver.Share(TransitIncidentKind.CreditCost))),
                    ("total", Format.Percent(IncidentResolver.Share(TransitIncidentKind.TotalLoss)))),
                Format.Percent(e.IncidentOdds), Tone.Plain));
        }

        if (contract.UsesEscrow)
        {
            uncertain.Add(new InfoLine(Templates.Get("Uncertain.Bond"), Templates.Get("Uncertain.Bond.Note"), Format.Money(e.Bond), Tone.Plain));
        }
        else
        {
            uncertain.Add(new InfoLine(Templates.Get("Uncertain.Payment"), Templates.Get("Uncertain.Payment.Note"), Templates.Get("Uncertain.Unknown"), Tone.Warn));
        }
    }

    // The bottom line: take-home and its rate for a haul or a fill; the cost, and the cost per unit, for a lot.
    private static (string Value, Tone Tone, string Note) Total(GameSession session, Contract contract, DealEstimate e)
    {
        string shipClass = Templates.ShipClass(e.Ship.Type);
        string duration = Format.Duration(e.TiedUpTicks(session.Now));
        if (e.Kind == DealKind.Buy)
        {
            decimal quantity = ((SaleContract)contract).Quantity;
            return (
                Format.Money(e.Cost),
                Tone.Plain,
                Templates.Fill("Cost.Note", ("unit", Format.MoneyCents(e.Cost.Amount / quantity)), ("shipClass", shipClass), ("duration", duration)));
        }

        return (
            Format.Money(e.TakeHome),
            e.TakeHome.Amount >= 0m ? Tone.Gain : Tone.Loss,
            Templates.Fill("TakeHome.Note", ("rate", Format.Money(e.PerDay(session.Now))), ("shipClass", shipClass), ("duration", duration)));
    }

    private static InfoLine Deadline(long deadline, long arrival)
    {
        string value = arrival > deadline
            ? Templates.Fill("Eta.Deadline.Late", ("date", Format.Date(deadline)), ("late", Format.Duration(arrival - deadline)))
            : Templates.Fill("Eta.Deadline.Spare", ("date", Format.Date(deadline)), ("spare", Format.Duration(deadline - arrival)));
        return new InfoLine(Templates.Get("Eta.Deadline"), string.Empty, value, arrival > deadline ? Tone.Loss : Tone.Plain);
    }

    // A signed contract of the player's that a ship of its is working on: when the job ends, against the deadline.
    private static void YourSchedule(GameSession session, Contract contract, List<InfoLine> schedule)
    {
        Ship ship = session.PlayerShip;
        if (ship.Job is not { } job || !job.Serves(contract.Id))
        {
            return;
        }

        (LocationId at, long arrival) = Market.NextFree(session.Context, ship);
        schedule.Add(new InfoLine(
            Templates.Fill(job.Destination is null ? "Eta.Collect" : "Eta.Arrive", ("destination", session.Context.Location(at).Name), ("place", session.Context.Location(at).Name)),
            string.Empty, Format.Date(arrival), job.Destination is not null && arrival > contract.DeadlineTick ? Tone.Loss : Tone.Plain));
        if (job.Destination is not null && contract is not SaleContract { Side: SaleSide.ForSale })
        {
            schedule.Add(Deadline(contract.DeadlineTick, arrival));
        }
    }

    private static (string Text, Tone Tone) Status(GameSession session, Contract contract)
    {
        SimulationContext context = session.Context;
        ActorId player = session.Player.Id;
        string Name(ActorId? id) => id is { } a ? context.Actor(a).Name : string.Empty;
        Cancellation? cancelled = contract.Cancellation;
        var facts = new[]
        {
            ("price", Format.Money(contract.Price)),
            ("taker", Name(contract.CounterpartyId)),
            ("performer", Name(contract.Performer)),
            ("payer", Name(contract.Payer)),
            ("by", Name(cancelled?.By)),
            ("paid", Format.Money(cancelled?.Paid ?? Credits.Zero)),
            ("destination", context.Location(Shape(contract).Destination).Name),
            ("place", context.Location(Shape(contract).Pickup).Name),
            ("deadline", Format.Date(contract.DeadlineTick)),
            ("when", Format.Date(contract.GraceEndsTick)),
        };
        string Key(string key, ActorId? subject, ActorId? other) =>
            subject == player ? key + ".You" : other == player ? key + ".ToYou" : key;
        Tone Yours(Tone tone) => contract.Involves(player) ? tone : Tone.Muted;

        return contract.Status switch
        {
            ContractStatus.Open when contract.PostedBy == player => (Templates.Fill("Status.Posted", facts), Tone.Yours),
            ContractStatus.Open => (Templates.Fill("Status.Open", facts), Tone.Muted),
            ContractStatus.Active when contract.Involves(player) && contract.InDefault(session.Now) =>
                (Templates.Fill(contract.Performer == player ? "Status.InDefault.You" : "Status.InDefault.ToYou", facts), Tone.Warn),
            ContractStatus.Active when contract is HaulContract && contract.Payer == player => (Templates.Fill("Status.Yours.Haul.Shipper", facts), Tone.Yours),
            ContractStatus.Active when contract.Involves(player) => (Templates.Fill("Status.Yours." + JournalText.Kind(contract), facts), Tone.Yours),
            ContractStatus.Active => (Templates.Fill("Status.Taken", facts), Tone.Muted),
            ContractStatus.Fulfilled => (Templates.Fill(Key("Status.Fulfilled", contract.Performer, contract.Payer), facts), Yours(Tone.Gain)),
            ContractStatus.PartlyFulfilled => (Templates.Fill(Key("Status.PartlyFulfilled", contract.Performer, contract.Payer), facts), Yours(Tone.Warn)),
            ContractStatus.Breached => (Templates.Fill(Key("Status.Breached", contract.Performer, contract.Payer), facts), Yours(Tone.Loss)),
            ContractStatus.Defaulted => (Templates.Fill(Key("Status.Defaulted", contract.Payer, contract.Performer), facts), Yours(Tone.Loss)),
            ContractStatus.Cancelled when cancelled is { Reason: CancelReason.ForDefault } =>
                (Templates.Fill(Key("Status.Cancelled.ForDefault", contract.Performer, contract.Payer), facts), Yours(Tone.Loss)),
            ContractStatus.Cancelled when cancelled is { } c =>
                (Templates.Fill(Key("Status.Cancelled.AtWill", c.By, contract.Other(c.By)), facts), Yours(Tone.Warn)),
            ContractStatus.Withdrawn => (Templates.Get("Status.Withdrawn"), Tone.Muted),
            _ => (Templates.Get("Status.Expired"), Tone.Muted),
        };
    }

    /// <summary>
    /// The terms as sentences (design §3.4, §11.3), from the player's side: as the performer it would be (or is), or as the
    /// payer, for a lot it would buy or a haul or wanted Sale it posted; or as the seller of its own lot.
    /// </summary>
    public static IReadOnlyList<string> TermsOf(GameSession session, Contract contract)
    {
        SimulationContext context = session.Context;
        ActorId player = session.Player.Id;
        string poster = context.Actor(contract.PostedBy).Name;
        bool lot = contract is SaleContract { Side: SaleSide.ForSale };
        bool ownLot = lot && contract.PostedBy == player;
        bool buying = lot && !ownLot;
        bool youPerform = contract.Performer is { } p ? p == player : ownLot || (contract.PostedBy != player && !lot);
        bool youPay = contract.Payer is { } y ? y == player : !lot && contract.PostedBy == player;
        string taker = Templates.Get("Party.Taker");
        string performer = contract.Performer is { } q ? context.Actor(q).Name : youPerform || buying ? poster : taker;
        string payer = contract.Payer is { } r ? context.Actor(r).Name : ownLot ? taker : poster;
        (LocationId from, LocationId to, GoodType good, decimal quantity) = Shape(contract);
        decimal bond = contract.Status != ContractStatus.Open
            ? contract.LockedBond.Amount
            : youPerform ? System.Math.Min(contract.PerformanceBond.Amount, System.Math.Max(0m, session.Player.Credits.Amount)) : contract.PerformanceBond.Amount;
        var facts = new List<(string Name, string Value)>
        {
            ("poster", poster),
            ("performer", performer),
            ("payer", payer),
            ("Performer", performer == taker ? Templates.Get("Party.Taker.Start") : performer),
            ("Payer", payer == taker ? Templates.Get("Party.Taker.Start") : payer),
            ("quantity", Format.Quantity(quantity)),
            ("good", Templates.Good(good)),
            ("origin", context.Location(from).Name),
            ("destination", context.Location(to).Name),
            ("place", context.Location(to).Name),
            ("deadline", Format.Date(contract.DeadlineTick)),
            ("grace", Format.Duration(contract.Terms.GraceHours)),
            ("tax", Format.Percent(contract.TaxRate)),
            ("bond", Format.Money(bond)),
            ("fee", Format.Money(contract.Terms.BreakFee)),
        };
        string Fill(string key, params (string Name, string Value)[] extra)
        {
            var all = new List<(string Name, string Value)>(extra);
            all.AddRange(facts);
            return Templates.Fill(key, all.ToArray());
        }

        string kind = JournalText.Kind(contract);
        string them = youPerform ? string.Empty : ".Them";
        string frontier = contract.UsesEscrow ? string.Empty : ".Frontier";

        // Whose side the first and the escrow sentences take: a payer's own haul or wanted Sale, a seller's own lot.
        string side = contract switch
        {
            HaulContract => them,
            SaleContract { Side: SaleSide.Wanted } when youPay && !youPerform => ".Them",
            _ when ownLot => ".Seller",
            _ => string.Empty,
        };
        var terms = new List<string> { Fill("Term." + kind + side) };
        if (!lot)
        {
            if (contract.Terms.GraceHours > 0)
            {
                terms.Add(Fill("Term.Grace"));
            }

            if (contract.LatePenalty is { } late)
            {
                var lateFacts = new[] { ("perHour", Format.MoneyCents(late.PenaltyPerTickLate)), ("cap", Format.Money(late.MaxPenalty)) };
                terms.Add(Fill(youPerform ? "Term.Late" + frontier : "Term.Late.Them", lateFacts));
            }

            terms.Add(Fill("Term.Default" + them + frontier));
        }

        terms.Add(Fill("Term.BreakFee"));
        if (!lot)
        {
            terms.Add(Fill(contract.Terms.Partial == PartialDelivery.ProRata ? "Term.Partial" : "Term.Partial.Failure"));
        }

        if (contract.Warranty is { } warranty)
        {
            var w = new[]
            {
                ("min", Format.Percent(warranty.MinQuality)),
                ("maxRefund", Format.Percent(warranty.MaxRefundFraction)),
                ("floor", Format.Percent(warranty.RejectBelow ?? 0m)),
            };
            string key = lot ? "Term.Warranty.Collect" + (ownLot ? ".Seller" : string.Empty) : contract is HaulContract ? "Term.Warranty.Haul" : "Term.Warranty";
            terms.Add(Fill(key, w));
            if (warranty.RejectBelow is not null)
            {
                terms.Add(Fill(contract is HaulContract ? "Term.Warranty.Haul.Reject" : youPerform ? "Term.Warranty.Reject" : "Term.Warranty.Reject.You", w));
            }
        }

        if (contract.Terms.Liability is { } liability)
        {
            terms.Add(Fill("Term.Liability" + them, ("perUnit", Format.MoneyCents(liability.PerUnit)), ("cap", Format.Money(liability.Cap))));
        }

        terms.Add(Fill((contract.UsesEscrow ? "Term.Escrow." : "Term.Frontier.") + kind + side));
        if (contract.UsesEscrow)
        {
            terms.Add(Templates.Get(contract.Arbitrated ? "Term.Courts.DeepAccord" : "Term.Courts.AccordEdge"));
        }

        return terms;
    }
}
