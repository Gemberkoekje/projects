using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Presentation.Session;
using ByGalacticAccord.Presentation.Text;

namespace ByGalacticAccord.Presentation.Views;

/// <summary>
/// One open contract on the board: kind, jurisdiction, danger and term package at a glance, and what it is worth to the
/// player (design §11.3): the take-home of a haul or a delivered Sale, or the cost of a lot. One the player knows only from
/// The Daily Accord says so (<see cref="Seen"/>), and is drawn by the age of that issue (design §7.2).
/// </summary>
public sealed record BoardRow(
    ContractId Id,
    ContractType Type,
    string Kind,
    string Title,
    string Route,
    string TakeHome,
    Tone TakeHomeTone,
    string Rate,
    DangerBand Band,
    string Danger,
    string Jurisdiction,
    string Terms,
    string Due,
    string? Note,
    Tone NoteTone,
    bool Blocked,
    string? Seen,
    Age Age);

public sealed record BoardViewModel(string Title, string Hint, string Empty, IReadOnlyList<BoardRow> Rows);

/// <summary>
/// Builds the board: the Sales and Hauls the player believes open (design §7.1, D31), newest first: those it sees live, and
/// those The Daily Accord reported that it can't see, which may have been taken since.
/// </summary>
public static class BoardView
{
    public static BoardViewModel Build(GameSession session)
    {
        var rows = new List<BoardRow>();
        Sight sight = session.Sight;
        foreach (Contract contract in session.KnownOpen().Reverse())
        {
            if (contract is SaleContract or HaulContract && contract.PostedBy != session.Player.Id)
            {
                rows.Add(Row(session, contract, sight));
            }
        }

        return new BoardViewModel(
            Templates.Fill("Board.Title", ("count", rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            Templates.Get("Board.Hint"),
            Templates.Get("Board.Empty"),
            rows);
    }

    private static BoardRow Row(GameSession session, Contract contract, Sight sight)
    {
        var context = session.Context;
        long now = session.Now;
        DealBlock block = ContractView.Check(session, contract);
        DealEstimate? estimate = DealEstimate.For(context, session.Player, session.PlayerShip, contract);
        bool blocked = block is not (DealBlock.None or DealBlock.NoFreeShip);

        string title;
        string route;
        string due;
        switch (contract)
        {
            case HaulContract haul:
                title = Templates.Fill("Board.Title.Haul", ("quantity", Format.Quantity(haul.Quantity)), ("good", Templates.Good(haul.Good)));
                route = Templates.Fill("Route", ("origin", context.Location(haul.Origin).Name), ("destination", context.Location(haul.Destination).Name));
                due = Templates.Fill("Board.Due", ("when", Format.Relative(haul.DeadlineTick, now)));
                break;
            case SaleContract sale:
                title = Templates.Fill("Board.Title.Sale." + sale.Side, ("quantity", Format.Quantity(sale.Quantity)), ("good", Templates.Good(sale.Good)));
                string place = context.Location(sale.Place).Name;
                route = estimate is { Kind: DealKind.Fill } e && e.Pickup != sale.Place
                    ? Templates.Fill("Route", ("origin", context.Location(e.Pickup).Name), ("destination", place))
                    : Templates.Fill("Board.At", ("place", place));
                due = Templates.Fill(sale.Side == SaleSide.Wanted ? "Board.Due" : "Board.Until", ("when", Format.Relative(sale.DeadlineTick, now)));
                break;
            default:
                title = route = due = string.Empty;
                break;
        }

        string value = "—";
        string rate = string.Empty;
        Tone tone = Tone.Muted;
        if (estimate is not null && !blocked)
        {
            if (estimate.Kind == DealKind.Buy)
            {
                SaleContract lot = (SaleContract)contract;
                value = Format.Money(estimate.Cost);
                tone = Tone.Plain;
                rate = Templates.Fill("PerUnit", ("amount", Format.MoneyCents(lot.UnitPrice)));
            }
            else
            {
                value = Format.Money(estimate.TakeHome);
                tone = estimate.TakeHome.Amount >= 0m ? Tone.Gain : Tone.Loss;
                rate = Templates.Fill("PerDay", ("amount", Format.Money(estimate.PerDay(now))));
            }
        }

        DangerBand band = estimate?.Band ?? DangerBand.Green;
        (string? note, Tone noteTone) = Note(session, contract, block, estimate);
        (string? seen, Age age) = Seen(session, contract, sight);
        return new BoardRow(
            contract.Id, contract.Type, Templates.KindLine(contract), title, route, value, tone, rate,
            band, Templates.Danger(band), Templates.Jurisdiction(contract.Jurisdiction), Templates.Package(contract.Terms.Package), due, note, noteTone, blocked,
            seen, age);
    }

    /// <summary>How the player knows of a contract: live, or from an issue of The Daily Accord, with that issue's age.</summary>
    public static (string? Seen, Age Age) Seen(GameSession session, Contract contract, Sight sight)
    {
        if (sight.CanSee(contract) || session.Context.News.ReportedOpenAt(contract.Id) is not { } reported)
        {
            return (null, Age.Live);
        }

        return (Templates.Fill("Board.Seen.News", ("date", Format.Date(reported))), InfoText.AgeOf(reported, session.Now));
    }

    // The one thing a row most needs to say beyond its figures: why you can't take it, or what the figures already count against you.
    private static (string? Note, Tone Tone) Note(GameSession session, Contract contract, DealBlock block, DealEstimate? estimate)
    {
        if (block != DealBlock.None)
        {
            return (ContractView.BlockText(session, contract, block), Tone.Muted);
        }

        if (estimate is null)
        {
            return (null, Tone.Plain);
        }

        if (estimate.TicksLate > 0)
        {
            return (Templates.Fill("Board.Note.Late", ("late", Format.Duration(estimate.TicksLate))), Tone.Warn);
        }

        if (contract.Warranty is { RejectBelow: { } floor } warranty && warranty.Rejectable(estimate.ArrivalQuality))
        {
            GoodType spoiled = contract is SaleContract s0 ? s0.Good : ((HaulContract)contract).Good;
            return (Templates.Fill(
                "Board.Note.Reject", ("good", Templates.Good(spoiled)), ("quality", Format.Percent(estimate.ArrivalQuality)), ("floor", Format.Percent(floor)),
                ("poster", session.Context.Actor(contract.PostedBy).Name)), Tone.Loss);
        }

        if (estimate.QualityRefund.Amount > 0m)
        {
            GoodType good = contract is SaleContract s ? s.Good : ((HaulContract)contract).Good;
            return (Templates.Fill(
                "Board.Note.Refund", ("good", Templates.Good(good)), ("quality", Format.Percent(estimate.ArrivalQuality)), ("refund", Format.Money(estimate.QualityRefund))), Tone.Warn);
        }

        if (estimate is { Kind: DealKind.Fill, Goods.Amount: > 0m })
        {
            return (Templates.Fill(
                "Board.Note.Buys", ("source", session.Context.Location(estimate.Pickup).Name), ("cost", Format.Money(estimate.Goods + estimate.GoodsTax))), Tone.Muted);
        }

        return (null, Tone.Plain);
    }
}
