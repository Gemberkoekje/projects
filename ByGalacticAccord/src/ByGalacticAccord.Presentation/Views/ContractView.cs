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
/// The contract as a document (design §11.3): parties, obligation, schedule, the take-home breakdown, what
/// can't be known for sure, and the terms as plain sentences. For a contract that is no longer open, the
/// status says what became of it and the estimate is left out.
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
        long now = session.Now;
        string origin = context.Location(contract.OriginLocationId).Name;
        string destination = context.Location(contract.DestinationLocationId).Name;
        ActorState poster = context.Actor(contract.PostedBy);
        string good = Templates.Good(contract.Good);
        string quantity = Format.Quantity(contract.Quantity);
        string shipClass = Templates.ShipClass(session.PlayerShip.Type);

        string kindLine = Templates.Fill("Contract.Kind", ("kind", Templates.ContractKind(contract.Type)));
        string head = Templates.Fill("Contract.Head", ("quantity", quantity), ("good", good), ("origin", origin), ("destination", destination));
        string sub = Templates.Fill("Contract.Sub", ("poster", poster.Name), ("jurisdiction", Templates.JurisdictionLong(contract.Jurisdiction)));
        string partyRole = Templates.Fill("Party.Role", ("role", Templates.Role(poster.Role)), ("location", context.Location(poster.Home).Name));
        IReadOnlyList<string> terms = Terms(contract, poster.Name, good, quantity, destination, session.Player);

        bool open = contract.Status is ContractStatus.Open or ContractStatus.Negotiating;
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

        if (open)
        {
            PlayerActions.HaulFeasibility check = PlayerActions.CheckHaul(context, session.Player, contract);
            canAccept = check.Ok;
            blocked = check.Ok ? null : BlockText(session, contract, check);

            if (HaulEstimate.For(context, session.Player, session.PlayerShip, contract) is { } e && check.Block is HaulBlock.None or HaulBlock.NoFreeShip)
            {
                Estimate(session, contract, e, origin, destination, good, poster.Name, shipClass, lines, schedule, uncertain);
                takeHome = Format.Money(e.TakeHome);
                takeTone = e.TakeHome.Amount >= 0m ? Tone.Gain : Tone.Loss;
                takeHomeNote = Templates.Fill(
                    "TakeHome.Note",
                    ("rate", Format.Money(e.PerDay(now))),
                    ("shipClass", shipClass),
                    ("duration", Format.Duration(e.TiedUpTicks(now))));
            }
        }
        else
        {
            (status, statusTone) = Status(session, contract, destination);
            if (contract.Status == ContractStatus.Active && contract.CounterpartyId == session.Player.Id)
            {
                YourSchedule(session, contract, destination, schedule);
            }
        }

        return new ContractViewModel(
            id, kindLine, head, sub, status, statusTone,
            Templates.Get("Contract.Certain"), lines, Templates.Get("TakeHome"), takeHome, takeTone, takeHomeNote,
            schedule, Templates.Get("Contract.Uncertain"), uncertain,
            Templates.Get("Contract.Terms"), terms, poster.Name, partyRole,
            open, canAccept, Templates.Get("Action.Accept"), blocked);
    }

    /// <summary>Why the player can't take <paramref name="contract"/>, in words.</summary>
    public static string BlockText(GameSession session, Contract contract, PlayerActions.HaulFeasibility check)
    {
        SimulationContext context = session.Context;
        string origin = context.Location(contract.OriginLocationId).Name;
        string shipClass = Templates.ShipClass(session.PlayerShip.Type);
        switch (check.Block)
        {
            case HaulBlock.NoFreeShip:
            {
                (LocationId at, long tick) = HaulEstimate.NextFree(context, session.PlayerShip);
                return Templates.Fill("Block.NoFreeShip", ("shipClass", shipClass), ("when", Format.Date(tick)), ("where", context.Location(at).Name));
            }

            case HaulBlock.TooLarge:
                return Templates.Fill(
                    "Block.TooLarge",
                    ("quantity", Format.Quantity(contract.Quantity)), ("shipClass", shipClass), ("capacity", Format.Quantity(session.PlayerShip.CargoCapacity)));
            case HaulBlock.OriginShort:
                return Templates.Fill(
                    "Block.OriginShort",
                    ("origin", origin), ("quantity", Format.Quantity(contract.Quantity)), ("good", Templates.Good(contract.Good)));
            case HaulBlock.NoRoute:
                return Templates.Fill("Block.NoRoute", ("origin", origin), ("destination", context.Location(contract.DestinationLocationId).Name));
            case HaulBlock.PickupUnreachable:
                return Templates.Fill("Block.PickupUnreachable", ("shipClass", shipClass), ("origin", origin));
            case HaulBlock.NotOpen:
            default:
                return Templates.Get("Block.NotOpen");
        }
    }

    /// <summary>What came of the player's attempt to take <paramref name="contract"/>.</summary>
    public static string OutcomeText(GameSession session, Contract contract, HaulOutcome outcome, PlayerActions.HaulFeasibility? blockedCheck = null)
    {
        SimulationContext context = session.Context;
        if (outcome.Acceptance is null)
        {
            return BlockText(session, contract, blockedCheck ?? new PlayerActions.HaulFeasibility(outcome.Block, session.PlayerShip, 0));
        }

        string origin = context.Location(contract.OriginLocationId).Name;
        switch (outcome.Acceptance.Value)
        {
            case HaulAcceptance.Accepted:
            {
                Ship ship = session.PlayerShip;
                var facts = new[]
                {
                    ("fee", Format.Money(contract.Fee)),
                    ("shipClass", Templates.ShipClass(ship.Type)),
                    ("origin", origin),
                    ("when", Format.Date(ship.Course?.ArrivalTick ?? session.Now)),
                    ("bond", Format.Money(contract.LockedBond)),
                };
                string signed = Templates.Fill(ship.Course is null ? "Outcome.Signed.Here" : "Outcome.Signed.Away", facts);
                return contract.UsesEscrow ? signed + " " + Templates.Fill("Outcome.Signed.Bond", facts) : signed;
            }

            case HaulAcceptance.PosterCannotFund:
                return Templates.Fill("Outcome.PosterCannotFund", ("poster", context.Actor(contract.PostedBy).Name));
            case HaulAcceptance.CannotFrontGoods:
                return Templates.Fill(
                    "Outcome.CannotFrontGoods",
                    ("cost", Format.Money(contract.GoodsCost)), ("credits", Format.Money(session.Player.Credits)));
            case HaulAcceptance.NotOpen:
            default:
                return Templates.Get("Block.NotOpen");
        }
    }

    private static void Estimate(
        GameSession session, Contract contract, HaulEstimate e, string origin, string destination, string good, string poster, string shipClass,
        List<MoneyLine> lines, List<InfoLine> schedule, List<InfoLine> uncertain)
    {
        SimulationContext context = session.Context;
        long now = session.Now;

        lines.Add(new MoneyLine(
            Templates.Get("Line.Fee"),
            Templates.Fill("Line.Fee.Note", ("poster", poster), ("tax", Format.Percent(contract.TaxRate))),
            Format.MoneyCents(e.Fee), Tone.Gain));

        if (e.GoodsFronted.Amount > 0m)
        {
            string supplier = context.ProducerAt(contract.OriginLocationId, contract.Good)?.Name ?? origin;
            lines.Add(new MoneyLine(
                Templates.Get("Line.Goods"),
                Templates.Fill("Line.Goods.Note", ("supplier", supplier), ("good", good)),
                Format.MoneyCents(-e.GoodsFronted.Amount), Tone.Loss));
        }

        string rate = Format.MoneyCents(e.Ship.Class.RunningCostPerTick);
        string runningNote = e.RepoTicks > 0
            ? Templates.Fill(
                "Line.Running.Note.Repo",
                ("hours", Format.Hours(e.RepoTicks + e.LadenTicks)), ("rate", rate), ("shipClass", shipClass),
                ("repoHours", Format.Hours(e.RepoTicks)), ("origin", origin), ("ladenHours", Format.Hours(e.LadenTicks)))
            : Templates.Fill("Line.Running.Note", ("hours", Format.Hours(e.LadenTicks)), ("rate", rate), ("shipClass", shipClass));
        lines.Add(new MoneyLine(Templates.Get("Line.Running"), runningNote, Format.MoneyCents(-e.Running.Amount), Tone.Loss));

        lines.Add(new MoneyLine(
            Templates.Fill("Line.Docking", ("destination", destination)),
            Templates.Get("Line.Docking.Note"),
            Format.MoneyCents(-e.Docking.Amount), Tone.Loss));

        if (e.QualityRefund.Amount > 0m && contract.Warranty is { } warranty)
        {
            lines.Add(new MoneyLine(
                Templates.Get("Line.Refund"),
                Templates.Fill(
                    "Line.Refund.Note",
                    ("good", good), ("quality", Format.Percent(e.ArrivalQuality)), ("hours", Format.Hours(e.LadenTicks)), ("min", Format.Percent(warranty.MinQuality))),
                Format.MoneyCents(-e.QualityRefund.Amount), Tone.Loss));
        }

        if (e.LateFee.Amount > 0m && contract.LatePenalty is { } late)
        {
            lines.Add(new MoneyLine(
                Templates.Get("Line.LateFee"),
                Templates.Fill("Line.LateFee.Note", ("late", Format.Duration(e.TicksLate)), ("perHour", Format.MoneyCents(late.PenaltyPerTickLate))),
                Format.MoneyCents(-e.LateFee.Amount), Tone.Loss));
        }

        if (e.StartTick > now)
        {
            schedule.Add(new InfoLine(
                Templates.Fill("Eta.Free", ("shipClass", shipClass), ("where", context.Location(e.StartAt).Name)), string.Empty, Format.Date(e.StartTick), Tone.Plain));
        }

        if (e.RepoTicks > 0)
        {
            schedule.Add(new InfoLine(Templates.Fill("Eta.Pickup", ("origin", origin)), string.Empty, Format.Date(e.PickupTick), Tone.Plain));
        }

        bool lateArrival = e.TicksLate > 0;
        schedule.Add(new InfoLine(Templates.Fill("Eta.Arrive", ("destination", destination)), string.Empty, Format.Date(e.ArrivalTick), lateArrival ? Tone.Loss : Tone.Plain));
        schedule.Add(Deadline(contract.DeadlineTick, e.ArrivalTick));

        uncertain.Add(new InfoLine(
            Templates.Get("Uncertain.Incident"),
            Templates.Fill(
                "Uncertain.Incident.Note",
                ("band", Templates.Danger(e.Band)), ("danger", Format.Percent(e.Laden.MaxDanger)),
                ("delay", Format.Percent(IncidentResolver.Share(TransitIncidentKind.MinorDelay))),
                ("damage", Format.Percent(IncidentResolver.Share(TransitIncidentKind.CargoDamage))),
                ("partial", Format.Percent(IncidentResolver.Share(TransitIncidentKind.PartialLoss))),
                ("costs", Format.Percent(IncidentResolver.Share(TransitIncidentKind.CreditCost))),
                ("total", Format.Percent(IncidentResolver.Share(TransitIncidentKind.TotalLoss)))),
            Format.Percent(e.IncidentOdds), Tone.Plain));
        if (contract.UsesEscrow)
        {
            uncertain.Add(new InfoLine(Templates.Get("Uncertain.Bond"), Templates.Get("Uncertain.Bond.Note"), Format.Money(e.Bond), Tone.Plain));
        }
        else
        {
            uncertain.Add(new InfoLine(Templates.Get("Uncertain.Payment"), Templates.Get("Uncertain.Payment.Note"), Templates.Get("Uncertain.Unknown"), Tone.Warn));
        }
    }

    private static InfoLine Deadline(long deadline, long arrival)
    {
        string value = arrival > deadline
            ? Templates.Fill("Eta.Deadline.Late", ("date", Format.Date(deadline)), ("late", Format.Duration(arrival - deadline)))
            : Templates.Fill("Eta.Deadline.Spare", ("date", Format.Date(deadline)), ("spare", Format.Duration(deadline - arrival)));
        return new InfoLine(Templates.Get("Eta.Deadline"), string.Empty, value, arrival > deadline ? Tone.Loss : Tone.Plain);
    }

    private static void YourSchedule(GameSession session, Contract contract, string destination, List<InfoLine> schedule)
    {
        (_, long arrival) = HaulEstimate.NextFree(session.Context, session.PlayerShip);
        schedule.Add(new InfoLine(Templates.Fill("Eta.Arrive", ("destination", destination)), string.Empty, Format.Date(arrival), arrival > contract.DeadlineTick ? Tone.Loss : Tone.Plain));
        schedule.Add(Deadline(contract.DeadlineTick, arrival));
    }

    private static (string Text, Tone Tone) Status(GameSession session, Contract contract, string destination)
    {
        SimulationContext context = session.Context;
        string hauler = contract.CounterpartyId is { } h ? context.Actor(h).Name : string.Empty;
        bool yours = contract.CounterpartyId == session.Player.Id;
        return contract.Status switch
        {
            ContractStatus.Active when yours => (Templates.Fill("Status.Yours", ("fee", Format.Money(contract.Fee)), ("destination", destination)), Tone.Yours),
            ContractStatus.Active => (Templates.Fill("Status.Taken", ("hauler", hauler), ("fee", Format.Money(contract.Fee))), Tone.Muted),
            ContractStatus.Fulfilled when yours => (Templates.Get("Status.Fulfilled.You"), Tone.Gain),
            ContractStatus.Fulfilled => (Templates.Fill("Status.Fulfilled", ("hauler", hauler)), Tone.Muted),
            ContractStatus.Breached when yours => (Templates.Get("Status.Breached.You"), Tone.Loss),
            ContractStatus.Breached => (Templates.Fill("Status.Breached", ("hauler", hauler)), Tone.Muted),
            _ => (Templates.Get("Status.Expired"), Tone.Muted),
        };
    }

    private static IReadOnlyList<string> Terms(
        Contract contract, string poster, string good, string quantity, string destination, ActorState player)
    {
        var terms = new List<string>
        {
            Templates.Fill(
                "Term.Deliver",
                ("quantity", quantity), ("good", good), ("poster", poster), ("destination", destination), ("deadline", Format.Date(contract.DeadlineTick))),
        };

        if (contract.LatePenalty is { } late)
        {
            terms.Add(Templates.Fill(
                contract.UsesEscrow ? "Term.Late" : "Term.Late.Frontier",
                ("perHour", Format.MoneyCents(late.PenaltyPerTickLate)), ("cap", Format.Money(late.MaxPenalty))));
        }

        if (contract.Warranty is { } warranty)
        {
            terms.Add(Templates.Fill(
                "Term.Warranty",
                ("good", good), ("min", Format.Percent(warranty.MinQuality)), ("maxRefund", Format.Percent(warranty.MaxRefundFraction))));
        }

        decimal bond = System.Math.Min(contract.PerformanceBond.Amount, System.Math.Max(0m, player.Credits.Amount));
        terms.Add(contract.UsesEscrow
            ? Templates.Fill("Term.Escrow", ("poster", poster), ("bond", Format.Money(bond)))
            : Templates.Fill("Term.Frontier", ("good", good)));
        return terms;
    }
}
