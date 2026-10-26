using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Session;
using ByGalacticAccord.Presentation.Views;

namespace ByGalacticAccord.Tests;

/// <summary>
/// The view models over a played game: every panel builds in every state the game passes through, every
/// template fills, the map keeps everything inside its frame, and the ledger shows what the player owns and where.
/// </summary>
public sealed class ViewTests
{
    [Theory]
    [InlineData(42L)]
    [InlineData(20260619L)]
    public void Every_view_builds_while_the_player_trades(long seed)
    {
        var session = new GameSession(seed, startTick: 30);
        session.SetSpeed(ClockSpeed.X16);
        var seen = new HashSet<ContractId>();
        var kinds = new HashSet<ContractType>();
        int signed = 0;
        int stored = 0;
        bool boughtLot = false;
        int frame = 0;
        while (session.Now < 2_400)
        {
            session.Advance(0.25);
            frame++;
            if (session.Speed == ClockSpeed.Paused)
            {
                AssertFilled(HeaderView.Build(session).Alerts);
                session.SetSpeed(ClockSpeed.X16);
            }

            if (frame % 6 != 0)
            {
                continue; // look every game day: six frames of four hours
            }

            HeaderViewModel header = HeaderView.Build(session);
            AssertFilled(header.Who, header.Date, header.Credits, header.Fleet, header.Running);
            BoardViewModel board = BoardView.Build(session);
            AssertFilled(board.Title);
            foreach (BoardRow row in board.Rows)
            {
                AssertFilled(row.Kind, row.Title, row.Route, row.TakeHome, row.Rate, row.Danger, row.Jurisdiction, row.Due, row.Note ?? string.Empty);
                Assert.Equal(session.Context.Contract(row.Id).Type, row.Type);
                seen.Add(row.Id);
                kinds.Add(row.Type);
                CheckContract(session, row.Id);
            }

            // Contracts seen earlier, now taken, settled or expired.
            foreach (ContractId id in seen.Take(10))
            {
                CheckContract(session, id);
            }

            foreach (LogLine line in LogView.Build(session).Lines)
            {
                AssertFilled(line.When, line.Text);
            }

            CheckShips(session);
            LedgerViewModel ledger = CheckLedger(session);

            // Put anything aboard into storage when the ship is free, so the hold is clear for the next deal.
            if (ledger.Possessions.FirstOrDefault(l => l.Action is { Store: true }) is { Action: { } store } && session.Store(store.Good))
            {
                stored++;
                CheckLedger(session);
            }

            // Buy one lot, once, to have goods of the player's own; otherwise take the best-paying deal whenever the ship is free.
            BoardRow? lot = boughtLot ? null : board.Rows.FirstOrDefault(r => !r.Blocked && session.Context.Contract(r.Id) is SaleContract { Side: SaleSide.ForSale });
            BoardRow? best = lot ?? board.Rows
                .Where(r => !r.Blocked && r.TakeHomeTone == Tone.Gain)
                .OrderByDescending(r => decimal.Parse(r.TakeHome.Replace("₵", string.Empty).Replace(",", string.Empty), System.Globalization.CultureInfo.InvariantCulture))
                .FirstOrDefault();
            if (best is not null && session.PlayerShip.IsFree && session.Accept(best.Id).Signed)
            {
                signed++;
                boughtLot |= lot is not null;
                CheckContract(session, best.Id);
                CheckLedger(session);
            }
        }

        Assert.True(signed >= 3, $"The player signed {signed} deals.");
        Assert.True(boughtLot && stored > 0, $"Bought a lot: {boughtLot}; stored goods {stored} times.");
        Assert.Contains(ContractType.Sale, kinds);
        Assert.Contains(ContractType.Haul, kinds);
        Assert.Contains(LogView.Build(session, 400).Lines, l => l.Yours);
    }

    [Fact]
    public void The_board_lists_only_open_contracts_the_player_can_see_newest_first_each_with_its_kind()
    {
        var session = new GameSession(42L, startTick: 48);
        HaulContract haul = PlayerActionsTests.FarmHaul(session.Context);
        BoardViewModel board = BoardView.Build(session);

        Assert.NotEmpty(board.Rows);
        Assert.All(board.Rows, r => Assert.Equal(ContractStatus.Open, session.Context.Contract(r.Id).Status));
        Assert.All(board.Rows, r => Assert.Contains(r.Type, new[] { ContractType.Sale, ContractType.Haul }));
        Assert.Equal(board.Rows.Select(r => r.Id.Value).OrderByDescending(v => v), board.Rows.Select(r => r.Id.Value));
        BoardRow row = board.Rows.Single(r => r.Id == haul.Id);
        Assert.Equal((ContractType.Haul, "Haul"), (row.Type, row.Kind));
    }

    [Fact]
    public void The_ledger_shows_goods_aboard_and_in_storage_and_the_contracts_in_progress()
    {
        var session = new GameSession(42L, startTick: 48);
        SaleTests.GiveAboard(session.Context, session.PlayerShip, session.Player, GoodType.Parts, 25m);

        LedgerViewModel aboard = LedgerView.Build(session);
        LedgerLine goods = Assert.Single(aboard.Possessions);
        Assert.Equal(new LedgerAction("Store at Meridian Station", GoodType.Parts, Store: true), goods.Action);
        Assert.Contains("hold", goods.Detail, System.StringComparison.Ordinal);
        Assert.Empty(aboard.Contracts);

        session.Store(GoodType.Parts);
        LedgerLine stored = Assert.Single(LedgerView.Build(session).Possessions);
        Assert.Equal(new LedgerAction("Load aboard", GoodType.Parts, Store: false), stored.Action);
        Assert.Contains("storage at Meridian Station", stored.Detail, System.StringComparison.Ordinal);

        HaulContract haul = PlayerActionsTests.FarmHaul(session.Context);
        session.Accept(haul.Id);
        LedgerViewModel busy = LedgerView.Build(session);
        Assert.Null(Assert.Single(busy.Possessions).Action); // the ship is away, so the goods stay put
        LedgerLine contract = Assert.Single(busy.Contracts);
        Assert.Equal((haul.Id, Tone.Gain), (contract.Contract, contract.Tone));
        Assert.StartsWith("carrying for Verdant Agricombine", contract.Detail, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Selecting_a_contract_draws_its_empty_leg_and_laden_legs()
    {
        var session = new GameSession(42L, startTick: 48);
        HaulContract away = PlayerActionsTests.FarmHaul(session.Context); // from Verdant; the player is at Meridian
        DealEstimate estimate = DealEstimate.ForHaul(session.Context, session.Player, session.PlayerShip, away)!;

        MapViewModel map = MapView.Build(session, away.Id);

        Assert.Equal(estimate.Repositioning!.Path.Count - 1, map.Overlay.Count(l => l.Empty));
        Assert.Equal(estimate.Laden!.Path.Count - 1, map.Overlay.Count(l => !l.Empty));
        Assert.Equal(map.Overlay.Count, map.Routes.Count(r => r.Dimmed) + DuplicateLegs(map.Overlay));
        Assert.NotNull(map.OverlayEta);
        Assert.Empty(MapView.Build(session, null).Overlay);
    }

    [Fact]
    public void A_ship_moves_along_its_path_by_share_of_the_hours()
    {
        SimulationContext context = WorldGenerator.Generate(42L, withPlayer: false).Context;
        Location meridian = context.Locations.Values.Single(l => l.Name == "Meridian Station");
        Location tycho = context.Locations.Values.Single(l => l.Name == "Tycho Foundry");
        Location rust = context.Locations.Values.Single(l => l.Name == "Rust Belt");
        var course = new ShipCourse(new[] { meridian.Id, tycho.Id, rust.Id }, DepartedTick: 100, ArrivalTick: 210); // 40 h + 70 h

        Assert.Equal((meridian.Map.X, meridian.Map.Y), Rounded(MapView.Along(context, course, 90)));
        Assert.Equal((tycho.Map.X, tycho.Map.Y), Rounded(MapView.Along(context, course, 140)));
        Assert.Equal((rust.Map.X, rust.Map.Y), Rounded(MapView.Along(context, course, 300)));
        (double x, double y) = MapView.Along(context, course, 175); // 35 h into the 70 h leg
        Assert.Equal((tycho.Map.X + rust.Map.X) / 2.0, x, precision: 6);
        Assert.Equal((tycho.Map.Y + rust.Map.Y) / 2.0, y, precision: 6);
    }

    private static int DuplicateLegs(IReadOnlyList<MapLeg> overlay) =>
        overlay.Count - overlay.Select(l => (System.Math.Min(l.X1, l.X2), System.Math.Max(l.X1, l.X2), System.Math.Min(l.Y1, l.Y2), System.Math.Max(l.Y1, l.Y2))).Distinct().Count();

    private static (int, int) Rounded((double X, double Y) p) => ((int)System.Math.Round(p.X), (int)System.Math.Round(p.Y));

    private static void CheckContract(GameSession session, ContractId id)
    {
        ContractViewModel c = ContractView.Build(session, id);
        AssertFilled(c.KindLine, c.Head, c.Sub, c.Status ?? string.Empty, c.TakeHome, c.TakeHomeNote, c.Party, c.PartyRole, c.Blocked ?? string.Empty);
        AssertFilled(c.Terms);
        foreach (MoneyLine line in c.Lines)
        {
            AssertFilled(line.Label, line.Note, line.Amount);
        }

        foreach (InfoLine line in c.Schedule.Concat(c.Uncertain))
        {
            AssertFilled(line.Label, line.Note, line.Value);
        }

        Assert.Equal(c.IsOpen, c.Status is null);
        MapViewModel map = MapView.Build(session, id);
        AssertFilled(map.OverlayTitle ?? string.Empty, map.OverlayEta ?? string.Empty);
        Assert.All(map.Nodes, n => Assert.InRange(n.X, 0, map.Width));
        Assert.All(map.Regions, r => Assert.InRange(r.X + r.Width, 0, map.Width));
    }

    private static LedgerViewModel CheckLedger(GameSession session)
    {
        LedgerViewModel ledger = LedgerView.Build(session);
        AssertFilled(ledger.Title, ledger.PossessionsTitle, ledger.PossessionsEmpty, ledger.ContractsTitle, ledger.ContractsEmpty);
        foreach (LedgerLine line in ledger.Money.Concat(ledger.Possessions).Concat(ledger.Contracts))
        {
            AssertFilled(line.Label, line.Detail, line.Value, line.Action?.Label ?? string.Empty);
        }

        Assert.Equal(session.PlayerActiveContracts.Count(), ledger.Contracts.Count);
        return ledger;
    }

    private static void CheckShips(GameSession session)
    {
        foreach (MapShip ship in MapView.Ships(session, session.Clock))
        {
            AssertFilled(ship.Tip);
            Assert.InRange(ship.X, 0, MapView.Width);
            Assert.InRange(ship.Y, 0, MapView.Height);
        }

        Assert.Single(MapView.Ships(session, session.Clock), s => s.Yours);
    }

    private static void AssertFilled(params string[] texts) => AssertFilled((IEnumerable<string>)texts);

    private static void AssertFilled(IEnumerable<string> texts)
    {
        foreach (string text in texts)
        {
            Assert.DoesNotContain("{", text, System.StringComparison.Ordinal);
            Assert.DoesNotContain("}", text, System.StringComparison.Ordinal);
        }
    }
}
