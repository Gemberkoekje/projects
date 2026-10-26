using System.Collections.Generic;
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

/// <summary>
/// The contract as a document (design §11.3): parties, obligation, schedule, what it is worth to the player (or, for a
/// lot, what it costs) line by line, what can't be known for sure, and the terms as plain sentences. For a contract
/// that is no longer open, the status says what became of it and the estimate is left out.
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
    string? Blocked);

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

        if (contract.IsOpen)
        {
            DealBlock block = Check(session, contract);
            canAccept = block == DealBlock.None;
            blocked = canAccept ? null : BlockText(session, contract, block);
            if (block is DealBlock.None or DealBlock.NoFreeShip && DealEstimate.For(context, session.Player, session.PlayerShip, contract) is { } e)
            {
                Estimate(session, contract, e, lines, schedule, uncertain);
                (takeHome, takeTone, takeHomeNote) = Total(session, contract, e);
            }
        }
        else
        {
            (status, statusTone) = Status(session, contract);
            if (contract.Status == ContractStatus.Active && contract.Involves(session.Player.Id))
            {
                YourSchedule(session, contract, schedule);
            }
        }

        return new ContractViewModel(
            id, kindLine, Head(context, contract), sub, status, statusTone,
            Templates.Get(buying ? "Contract.Cost" : "Contract.Certain"), lines, Templates.Get(buying ? "Cost" : "TakeHome"), takeHome, takeTone, takeHomeNote,
            schedule, Templates.Get("Contract.Uncertain"), uncertain,
            Templates.Get("Contract.Terms"), Terms(session, contract), poster.Name, partyRole,
            contract.IsOpen, canAccept, Templates.Get(AcceptKey(contract)), blocked);
    }

    /// <summary>Whether the player could take <paramref name="contract"/> now, and if not, why.</summary>
    public static DealBlock Check(GameSession session, Contract contract) => contract switch
    {
        HaulContract haul => PlayerActions.CheckHaul(session.Context, session.Player, haul).Block,
        SaleContract { Side: SaleSide.Wanted } wanted => PlayerActions.CheckFill(session.Context, session.Player, wanted).Block,
        SaleContract lot => PlayerActions.CheckBuy(session.Context, session.Player, lot).Block,
        _ => DealBlock.NotOpen,
    };

    /// <summary>What the contract is, in a line: the goods, and where.</summary>
    public static string Head(SimulationContext context, Contract contract) => contract switch
    {
        HaulContract haul => Templates.Fill(
            "Contract.Head.Haul", ("quantity", Format.Quantity(haul.Quantity)), ("good", Templates.Good(haul.Good)),
            ("origin", context.Location(haul.Origin).Name), ("destination", context.Location(haul.Destination).Name)),
        SaleContract sale => Templates.Fill(
            "Contract.Head.Sale." + sale.Side, ("quantity", Format.Quantity(sale.Quantity)), ("good", Templates.Good(sale.Good)),
            ("place", context.Location(sale.Place).Name)),
        StorageContract storage => Templates.Fill("Contract.Head.Storage", ("place", context.Location(storage.Location).Name)),
        _ => contract.Id.ToString(),
    };

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
        var facts = new[]
        {
            ("price", Format.Money(contract.Price)),
            ("taker", Name(contract.CounterpartyId)),
            ("performer", Name(contract.Performer)),
            ("payer", Name(contract.Payer)),
            ("destination", context.Location(Shape(contract).Destination).Name),
            ("place", context.Location(Shape(contract).Pickup).Name),
        };
        string Key(string key, ActorId? subject, ActorId? other) =>
            subject == player ? key + ".You" : other == player ? key + ".ToYou" : key;

        return contract.Status switch
        {
            ContractStatus.Active when contract.Involves(player) => (Templates.Fill("Status.Yours." + JournalText.Kind(contract), facts), Tone.Yours),
            ContractStatus.Active => (Templates.Fill("Status.Taken", facts), Tone.Muted),
            ContractStatus.Fulfilled => (Templates.Fill(Key("Status.Fulfilled", contract.Performer, contract.Payer), facts), contract.Involves(player) ? Tone.Gain : Tone.Muted),
            ContractStatus.Breached => (Templates.Fill(Key("Status.Breached", contract.Performer, contract.Payer), facts), contract.Involves(player) ? Tone.Loss : Tone.Muted),
            ContractStatus.Defaulted => (Templates.Fill(Key("Status.Defaulted", contract.Payer, contract.Performer), facts), contract.Involves(player) ? Tone.Loss : Tone.Muted),
            ContractStatus.Withdrawn => (Templates.Get("Status.Withdrawn"), Tone.Muted),
            _ => (Templates.Get("Status.Expired"), Tone.Muted),
        };
    }

    private static IReadOnlyList<string> Terms(GameSession session, Contract contract)
    {
        SimulationContext context = session.Context;
        string poster = context.Actor(contract.PostedBy).Name;
        (LocationId from, LocationId to, GoodType good, decimal quantity) = Shape(contract);
        var facts = new List<(string Name, string Value)>
        {
            ("poster", poster),
            ("quantity", Format.Quantity(quantity)),
            ("good", Templates.Good(good)),
            ("origin", context.Location(from).Name),
            ("destination", context.Location(to).Name),
            ("place", context.Location(to).Name),
            ("deadline", Format.Date(contract.DeadlineTick)),
            ("tax", Format.Percent(contract.TaxRate)),
            ("bond", Format.Money(System.Math.Min(contract.PerformanceBond.Amount, System.Math.Max(0m, session.Player.Credits.Amount)))),
        };

        string kind = JournalText.Kind(contract);
        var terms = new List<string> { Templates.Fill("Term." + kind, facts.ToArray()) };
        bool buying = contract is SaleContract { Side: SaleSide.ForSale };
        if (!buying && contract.LatePenalty is { } late)
        {
            terms.Add(Templates.Fill(
                contract.UsesEscrow ? "Term.Late" : "Term.Late.Frontier",
                ("perHour", Format.MoneyCents(late.PenaltyPerTickLate)), ("cap", Format.Money(late.MaxPenalty))));
        }

        if (contract.Warranty is { } warranty)
        {
            terms.Add(Templates.Fill(
                buying ? "Term.Warranty.Collect" : "Term.Warranty",
                ("good", Templates.Good(good)), ("min", Format.Percent(warranty.MinQuality)), ("maxRefund", Format.Percent(warranty.MaxRefundFraction))));
        }

        if (contract is SaleContract { Side: SaleSide.Wanted })
        {
            terms.Add(Templates.Get("Term.Partial"));
        }

        terms.Add(Templates.Fill((contract.UsesEscrow ? "Term.Escrow." : "Term.Frontier.") + kind, facts.ToArray()));
        return terms;
    }
}
