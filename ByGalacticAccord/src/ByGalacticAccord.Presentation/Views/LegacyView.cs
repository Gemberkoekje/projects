using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Session;
using ByGalacticAccord.Presentation.Text;

namespace ByGalacticAccord.Presentation.Views;

/// <summary>A ship the player flew (design §6.4, §10): what became of it, and its history.</summary>
public sealed record LegacyShip(ShipId Id, string Name, string Fate, IReadOnlyList<HistoryLine> History);

/// <summary>
/// The legacy screen (design §10): when insolvency ends the game, the story told from the player's record. How it ended and the
/// failures that led there, the contracts it honoured and broke, the markets it moved, the companies it outlasted, and the ships it
/// flew with their histories. Every line is a template over the record (design §11.5): nothing is judged, only told.
/// </summary>
public sealed record LegacyViewModel(
    string Title,
    string Ending,
    string Span,
    string RecordTitle,
    IReadOnlyList<string> Record,
    string CascadeTitle,
    IReadOnlyList<LogLine> Cascade,
    string MarketsTitle,
    IReadOnlyList<string> Markets,
    string RivalsTitle,
    IReadOnlyList<string> Rivals,
    string ShipsTitle,
    IReadOnlyList<LegacyShip> Ships,
    string NewGame);

public static class LegacyView
{
    /// <summary>How far before the end the failures that led there are looked for.</summary>
    public const long CascadeHours = 30 * 24;

    /// <summary>The most failures and markets the legacy lists.</summary>
    public const int MostLines = 6;

    public static LegacyViewModel Build(GameSession session)
    {
        SimulationContext context = session.Context;
        ActorState player = session.Player;
        long end = player.InsolventSince ?? session.Now;

        string ending = player.SolvencyEntry is { } seq && context.Journal.Find(seq) is InsolventEntry insolvent
            ? JournalText.Describe(context, insolvent)
            : Templates.Fill("Legacy.Ending", ("debt", Format.Money(player.DebtAtInsolvency)));
        string span = Templates.Fill("Legacy.Span", ("days", Format.Duration(end)), ("date", Format.Date(end)));

        return new LegacyViewModel(
            Templates.Get("Legacy.Title"),
            ending,
            span,
            Templates.Get("Legacy.Record"),
            RecordLines(context, player),
            Templates.Get("Legacy.Cascade"),
            Cascade(session, end),
            Templates.Get("Legacy.Markets"),
            Markets(context, player),
            Templates.Get("Legacy.Rivals"),
            Rivals(context, player, end),
            Templates.Get("Legacy.Ships"),
            Ships(session),
            Templates.Get("Legacy.NewGame"));
    }

    // Contracts honoured and broken, as every counterparty remembers them, frontier ones too.
    private static IReadOnlyList<string> RecordLines(SimulationContext context, ActorState player)
    {
        TrackRecord record = context.Records.WholeRecordOf(player.Id);
        if (record.Total == 0)
        {
            return new[] { Templates.Get("Legacy.Record.None") };
        }

        return new[]
        {
            Templates.Fill(
                "Legacy.Record.Performed", ("total", Count(record.AsPerformer)), ("onTime", Count(record.OnTime)), ("late", Count(record.Late)),
                ("short", Count(record.Short)), ("failed", Count(record.Failed)), ("cancelled", Count(record.CancelledForDefault)),
                ("walked", Count(record.WalkedAway))),
            Templates.Fill(
                "Legacy.Record.Paid", ("total", Count(record.AsPayer)), ("paid", Count(record.Paid)), ("defaulted", Count(record.Defaulted)),
                ("walked", Count(record.PayerWalkedAway))),
        };
    }

    // The road there (design §10: "usually a cascade"): the player's contracts that failed, were cancelled or went unpaid in the
    // month before the end, newest first, each in its last sentence.
    private static IReadOnlyList<LogLine> Cascade(GameSession session, long end)
    {
        SimulationContext context = session.Context;
        var lines = new List<LogLine>();
        foreach (Contract contract in context.Contracts.Values
                     .Where(c => c.Involves(session.Player.Id) && c is not StorageContract
                                 && c.Status is ContractStatus.Breached or ContractStatus.Cancelled or ContractStatus.Defaulted)
                     .OrderByDescending(c => c.LastEntry ?? -1))
        {
            if (lines.Count >= MostLines || contract.LastEntry is not { } last || context.Journal.Find(last) is not { } entry || entry.Tick < end - CascadeHours)
            {
                continue;
            }

            lines.Add(new LogLine(Format.Date(entry.Tick), JournalText.Describe(context, entry), true, contract.Id));
        }

        return lines;
    }

    // The markets it moved: the goods it delivered, by where they went, the most valuable first.
    private static IReadOnlyList<string> Markets(SimulationContext context, ActorState player)
    {
        var delivered = new Dictionary<(LocationId Place, GoodType Good), decimal>();
        foreach (Contract contract in context.Contracts.Values)
        {
            if (contract.Status is not (ContractStatus.Fulfilled or ContractStatus.PartlyFulfilled) || contract.Performer != player.Id)
            {
                continue;
            }

            (LocationId place, GoodType good, decimal quantity)? moved = contract switch
            {
                SaleContract { Side: SaleSide.Wanted } sale => (sale.Place, sale.Good, sale.Quantity),
                HaulContract haul => (haul.Destination, haul.Good, haul.Quantity),
                _ => null,
            };
            if (moved is { } m)
            {
                delivered[(m.place, m.good)] = delivered.GetValueOrDefault((m.place, m.good)) + m.quantity;
            }
        }

        if (delivered.Count == 0)
        {
            return new[] { Templates.Get("Legacy.Markets.None") };
        }

        return delivered
            .OrderByDescending(d => d.Value * Goods.Info(d.Key.Good).BaseValue)
            .ThenBy(d => d.Key.Place.Value)
            .Take(MostLines)
            .Select(d => Templates.Fill(
                "Legacy.Market", ("quantity", Format.Quantity(d.Value)), ("good", Templates.Good(d.Key.Good)), ("place", context.Location(d.Key.Place).Name)))
            .ToList();
    }

    // The companies it outlasted: every actor dissolved before the player's end.
    private static IReadOnlyList<string> Rivals(SimulationContext context, ActorState player, long end)
    {
        List<string> lines = context.Dissolutions
            .Where(d => d.Actor != player.Id && d.Tick <= end)
            .Select(d => context.Actor(d.Actor))
            .Select(a => Templates.Fill("Legacy.Rival", ("name", a.Name), ("role", Templates.Role(a.Role)), ("date", Format.Date(a.InsolventSince ?? 0))))
            .ToList();
        return lines.Count > 0 ? lines : new List<string> { Templates.Get("Legacy.Rivals.None") };
    }

    // Every ship the player owned, with what became of it and its history.
    private static IReadOnlyList<LegacyShip> Ships(GameSession session)
    {
        SimulationContext context = session.Context;
        ActorId you = session.Player.Id;
        var ships = new List<LegacyShip>();
        foreach (Ship ship in context.Ships.Values.Where(s => s.Owner == you || s.History.Any(r => r.To == you)).OrderBy(s => s.Id.Value))
        {
            string fate = ship.Scrapped ? Templates.Get("Legacy.Ship.Scrapped")
                : ship.Owner == you ? Templates.Get("Legacy.Ship.Yours")
                : Templates.Fill("Legacy.Ship.Sold", ("owner", context.Actor(ship.Owner).Name));
            ships.Add(new LegacyShip(ship.Id, JournalText.ShipLine(context, ship.Id), fate, ShipView.Build(session, ship.Id).History));
        }

        return ships;
    }

    private static string Count(int n) => n.ToString("N0", CultureInfo.InvariantCulture);
}
