using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Presentation.Session;
using ByGalacticAccord.Presentation.Text;

namespace ByGalacticAccord.Presentation.Views;

/// <summary>
/// One open contract on the board: kind, jurisdiction and danger at a glance, and what it is worth to the player
/// (design §11.3): the take-home of a haul or a delivered Sale, or the cost of a lot.
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
    string Due,
    string? Note,
    Tone NoteTone,
    bool Blocked);

public sealed record BoardViewModel(string Title, string Hint, string Empty, IReadOnlyList<BoardRow> Rows);

/// <summary>Builds the board: the open Sales and Hauls the player can see, newest first.</summary>
public static class BoardView
{
    public static BoardViewModel Build(GameSession session)
    {
        var rows = new List<BoardRow>();
        foreach (Contract contract in Visibility.OpenContractsVisibleTo(session.Context, session.Player).Reverse())
        {
            if (contract.Status == ContractStatus.Open && contract is SaleContract or HaulContract && contract.PostedBy != session.Player.Id)
            {
                rows.Add(Row(session, contract));
            }
        }

        return new BoardViewModel(
            Templates.Fill("Board.Title", ("count", rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            Templates.Get("Board.Hint"),
            Templates.Get("Board.Empty"),
            rows);
    }

    private static BoardRow Row(GameSession session, Contract contract)
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
        return new BoardRow(
            contract.Id, contract.Type, Templates.KindLine(contract), title, route, value, tone, rate,
            band, Templates.Danger(band), Templates.Jurisdiction(contract.Jurisdiction), due, note, noteTone, blocked);
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
