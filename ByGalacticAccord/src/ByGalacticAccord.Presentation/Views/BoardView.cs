using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Presentation.Session;
using ByGalacticAccord.Presentation.Text;

namespace ByGalacticAccord.Presentation.Views;

/// <summary>One open contract on the board: kind, jurisdiction and danger at a glance, and its take-home (design §11.3).</summary>
public sealed record BoardRow(
    ContractId Id,
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

/// <summary>Builds the board: the open contracts the player can see, newest first.</summary>
public static class BoardView
{
    public static BoardViewModel Build(GameSession session)
    {
        var rows = new List<BoardRow>();
        foreach (Contract contract in Visibility.OpenContractsVisibleTo(session.Context, session.Player).Reverse())
        {
            if (contract.Status == ContractStatus.Open && contract.Type == ContractType.Haul)
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
        PlayerActions.HaulFeasibility check = PlayerActions.CheckHaul(context, session.Player, contract);
        HaulEstimate? estimate = HaulEstimate.For(context, session.Player, session.PlayerShip, contract);

        string title = Templates.Fill("Board.Title.Haul", ("quantity", Format.Quantity(contract.Quantity)), ("good", Templates.Good(contract.Good)));
        string route = Templates.Fill("Route", ("origin", context.Location(contract.OriginLocationId).Name), ("destination", context.Location(contract.DestinationLocationId).Name));
        string due = Templates.Fill("Board.Due", ("when", Format.Relative(contract.DeadlineTick, now)));
        DangerBand band = estimate?.Band ?? context.FindPath(contract.OriginLocationId, contract.DestinationLocationId)?.Band ?? DangerBand.Green;

        bool blocked = !check.Ok && check.Block != HaulBlock.NoFreeShip;
        string takeHome = "—";
        string rate = string.Empty;
        Tone takeTone = Tone.Muted;
        if (estimate is not null && !blocked)
        {
            takeHome = Format.Money(estimate.TakeHome);
            takeTone = estimate.TakeHome.Amount >= 0m ? Tone.Gain : Tone.Loss;
            rate = Templates.Fill("PerDay", ("amount", Format.Money(estimate.PerDay(now))));
        }

        (string? note, Tone noteTone) = Note(session, contract, check, estimate);
        return new BoardRow(
            contract.Id, Templates.ContractKind(contract.Type), title, route, takeHome, takeTone, rate,
            band, Templates.Danger(band), Templates.Jurisdiction(contract.Jurisdiction), due, note, noteTone, blocked);
    }

    // The one thing a row most needs to say beyond its figures: why you can't fly it, or what the figures already count against you.
    private static (string? Note, Tone Tone) Note(GameSession session, Contract contract, PlayerActions.HaulFeasibility check, HaulEstimate? estimate)
    {
        if (!check.Ok)
        {
            return (ContractView.BlockText(session, contract, check), Tone.Muted);
        }

        if (estimate is null)
        {
            return (null, Tone.Plain);
        }

        if (estimate.GoodsFronted.Amount > 0m)
        {
            return (Templates.Fill("Board.Note.Goods", ("cost", Format.Money(estimate.GoodsFronted))), Tone.Warn);
        }

        if (estimate.TicksLate > 0)
        {
            return (Templates.Fill("Board.Note.Late", ("late", Format.Duration(estimate.TicksLate))), Tone.Warn);
        }

        if (estimate.QualityRefund.Amount > 0m)
        {
            return (Templates.Fill("Board.Note.Refund", ("good", Templates.Good(contract.Good)), ("quality", Format.Percent(estimate.ArrivalQuality)), ("refund", Format.Money(estimate.QualityRefund))), Tone.Warn);
        }

        return (null, Tone.Plain);
    }
}
