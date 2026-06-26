using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Events;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Cli;

/// <summary>
/// The captain's chair: the character/ASCII-forward UI (§5.7). The screens map one-to-one onto the
/// GDD's information architecture — the Board, the Map, the Ship Sheet, the Ledger, the Negotiation
/// View, and a time-control bar that hard-pauses on a player alert (§3.4).
/// </summary>
internal static class Game
{
    public static void Run(long seed)
    {
        WorldHandles handles = WorldGenerator.Generate(seed, withPlayer: true);
        SimulationContext ctx = handles.Context;
        ActorState player = ctx.Actor(handles.Player!.Value);

        Intro(ctx, player);

        bool running = true;
        while (running)
        {
            Console.Write(Prompt(ctx, player));
            string? line = Console.ReadLine();
            if (line is null)
            {
                break;
            }

            string[] parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string cmd = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;

            switch (cmd)
            {
                case "":
                case "r":
                case "run":
                    Advance(ctx, player, ParseTicks(parts, 50));
                    break;
                case "b":
                case "board":
                    ShowBoard(ctx, player);
                    break;
                case "take":
                case "negotiate":
                case "n":
                    TakeContract(ctx, player, parts);
                    break;
                case "m":
                case "map":
                    ShowMap(ctx, player);
                    break;
                case "s":
                case "ship":
                    ShowShips(ctx, player);
                    break;
                case "l":
                case "ledger":
                    ShowLedger(ctx, player);
                    break;
                case "g":
                case "log":
                    ShowLog(ctx);
                    break;
                case "h":
                case "help":
                case "?":
                    Help();
                    break;
                case "q":
                case "quit":
                case "retire":
                    running = !Retire(ctx, player);
                    break;
                default:
                    Console.WriteLine("Unknown command. Type 'help'.");
                    break;
            }
        }
    }

    // ---------------------------------------------------------------- intro / prompt

    private static void Intro(SimulationContext ctx, ActorState player)
    {
        Console.WriteLine();
        Console.WriteLine("====================  BY GALACTIC ACCORD  ====================");
        Console.WriteLine("  A contract is the universal language of this economy.");
        Console.WriteLine("  You are an Independent Pilot with one beat-up Light Freighter,");
        Console.WriteLine($"  docked at {ctx.Location(player.Home).Name}. The world runs whether or not you do.");
        Console.WriteLine("==============================================================");
        Console.WriteLine();
        Console.WriteLine("  Type 'board' to read the local contract board, 'take <#>' to negotiate one,");
        Console.WriteLine("  'run [ticks]' to let time flow, 'help' for everything else.");
        Console.WriteLine();
        Help();
    }

    private static string Prompt(SimulationContext ctx, ActorState player) =>
        $"\n[t{ctx.CurrentTick} · {player.Credits} · {FreeShips(ctx, player).Count} ship(s) idle] > ";

    // ---------------------------------------------------------------- time control (§3.4)

    private static void Advance(SimulationContext ctx, ActorState player, long ticks)
    {
        long target = ctx.CurrentTick + Math.Max(1, ticks);
        RunStop stop = ctx.RunTo(target);
        Console.WriteLine($"... advanced to tick {ctx.CurrentTick}.");

        if (stop == RunStop.PlayerAlert)
        {
            Console.WriteLine();
            Console.WriteLine(">>> HARD PAUSE — something needs your attention:");
            foreach (string alert in ctx.PlayerAlerts)
            {
                Console.WriteLine($"    ! {alert}");
            }

            ctx.ClearPlayerAlerts();
        }

        // Surface anything that just happened to the player's own contracts.
        ReportPlayerActivity(ctx, player);
    }

    private static void ReportPlayerActivity(SimulationContext ctx, ActorState player)
    {
        var mine = ctx.Journal.Tail(40)
            .Where(e => e.Text.Contains(player.Name, StringComparison.Ordinal)
                     && e.Category is "Fulfilled" or "Breach")
            .ToList();
        foreach (JournalEntry e in mine.TakeLast(5))
        {
            Console.WriteLine($"    · t{e.Tick} [{e.Category}] {e.Text}");
        }
    }

    // ---------------------------------------------------------------- the board (§5.7)

    private static List<Contract> VisibleHauls(SimulationContext ctx, ActorState player) =>
        Visibility.OpenContractsVisibleTo(ctx, player)
            .Where(c => c.Type == ContractType.Haul && c.Status == ContractStatus.Open)
            .OrderBy(c => ctx.FindPath(c.OriginLocationId, c.DestinationLocationId)?.MaxDanger ?? 0m)
            .ToList();

    private static void ShowBoard(SimulationContext ctx, ActorState player)
    {
        List<Contract> hauls = VisibleHauls(ctx, player);
        Console.WriteLine();
        Console.WriteLine("  THE BOARD — haul contracts visible to you (local + scouted, §4.6)");
        Console.WriteLine("  ----------------------------------------------------------------------------");
        if (hauls.Count == 0)
        {
            Console.WriteLine("  (nothing on the board here right now — 'run' a while, or deliver to scout new docks)");
            return;
        }

        Console.WriteLine("   #  good       qty   route                         danger  law      deadline  offer");
        int i = 1;
        foreach (Contract c in hauls)
        {
            RoutePlan? route = ctx.FindPath(c.OriginLocationId, c.DestinationLocationId);
            string band = route is null ? "  ?  " : Band(route.Band);
            string from = ctx.Location(c.OriginLocationId).Name;
            string to = ctx.Location(c.DestinationLocationId).Name;
            string routeText = Trunc($"{from}→{to}", 28);
            long left = c.DeadlineTick - ctx.CurrentTick;
            Console.WriteLine($"  {i,2}  {c.Good,-9} {c.Quantity,5:0}   {routeText,-28}  {band}  {Reach(c.Jurisdiction),-7}  t+{left,-6} {c.OfferedPayment}");
            i++;
        }

        Console.WriteLine();
        Console.WriteLine("  'take <#>' to open the Negotiation View. Offer shown is the poster's opening bid.");
    }

    // ---------------------------------------------------------------- negotiation (§4.1.6)

    private static void TakeContract(SimulationContext ctx, ActorState player, string[] parts)
    {
        List<Contract> hauls = VisibleHauls(ctx, player);
        if (parts.Length < 2 || !int.TryParse(parts[1], out int idx) || idx < 1 || idx > hauls.Count)
        {
            Console.WriteLine("  Usage: take <#>   (see 'board' for the numbers)");
            return;
        }

        Contract contract = hauls[idx - 1];
        PlayerActions.HaulFeasibility feas = PlayerActions.CheckHaul(ctx, player, contract);
        if (!feas.Ok)
        {
            Console.WriteLine($"  {feas.Reason}");
            return;
        }

        Ship ship = feas.Ship!;
        long repoTicks = feas.RepoTicks;

        ActorState consumer = ctx.Actor(contract.PostedBy);
        Console.WriteLine();
        Console.WriteLine($"  NEGOTIATION — {consumer.Name} wants {contract.Quantity:0} {contract.Good}");
        Console.WriteLine($"  {ctx.Location(contract.OriginLocationId).Name} → {ctx.Location(contract.DestinationLocationId).Name}"
                          + (repoTicks > 0 ? $"  (your ship repositions first: ~{repoTicks} ticks)" : string.Empty));
        Console.WriteLine($"  Goods cost (paid to producer): {contract.GoodsCost}.  Your performance bond if you take it: {contract.PerformanceBond}.");
        Console.WriteLine("  Enter an ask (a number), 'accept' to take their current offer, or 'walk'.");

        var session = new NegotiationSession(
            new NegotiationParty(contract.BuyerReservationFee, consumer.Personality.ConcessionRate, IsBuyer: true),
            contract.OfferedPayment,
            ctx.Config.NegotiationMaxRounds);

        while (!session.Concluded)
        {
            Console.WriteLine($"    their offer: {session.CurrentOffer}   (they {session.Tell})");
            Console.Write("    your move > ");
            string? input = Console.ReadLine();
            if (input is null)
            {
                return;
            }

            input = input.Trim().ToLowerInvariant();
            if (input is "walk" or "w")
            {
                Console.WriteLine("  You walk away from the table.");
                return;
            }

            if (input is "accept" or "a")
            {
                Commit(ctx, player, contract, ship, session.CurrentOffer, repoTicks);
                return;
            }

            if (!decimal.TryParse(input, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal ask))
            {
                Console.WriteLine("    Enter a number, 'accept', or 'walk'.");
                continue;
            }

            NegotiationStep step = session.Counter(Credits.Of(ask));
            switch (step.Kind)
            {
                case NegotiationStepKind.Accepted:
                    Console.WriteLine($"    They accept {step.Fee}.");
                    Commit(ctx, player, contract, ship, step.Fee, repoTicks);
                    return;
                case NegotiationStepKind.Walked:
                    Console.WriteLine("    They walk away. No deal.");
                    return;
                case NegotiationStepKind.Countered:
                default:
                    break;
            }
        }
    }

    private static void Commit(SimulationContext ctx, ActorState player, Contract contract, Ship ship, Credits fee, long repoTicks)
    {
        (bool ok, string message) = PlayerActions.AcceptHaul(ctx, player, contract, ship, fee, repoTicks);
        Console.WriteLine("  " + message + (ok ? " 'run' to fly it." : string.Empty));
    }

    // ---------------------------------------------------------------- the map (§5.7)

    private static void ShowMap(SimulationContext ctx, ActorState player)
    {
        Console.WriteLine();
        Console.WriteLine("  THE MAP — the sector graph. Danger is the static geographic baseline (§6.3.1).");
        Console.WriteLine("  ----------------------------------------------------------------------------");
        foreach (Location loc in ctx.Locations.Values.OrderBy(l => l.Id.Value))
        {
            bool known = loc.Id == player.Home || ctx.RouteBetween(player.Home, loc.Id) is not null || ctx.HasFreshScout(player.Id, loc.Id);
            string here = loc.Id == player.Home ? " (you are here)" : string.Empty;
            Console.WriteLine($"   {loc.Name,-20} {loc.Type,-16} {Reach(loc.Reach),-7} [{loc.Region}]{here}");
            if (known)
            {
                PrintLocalPrices(ctx, loc);
            }
        }

        Console.WriteLine();
        Console.WriteLine("  Routes:");
        foreach (Route r in ctx.Routes)
        {
            Console.WriteLine($"   {Band(r.Band)} {ctx.Location(r.A).Name,-18} -- {ctx.Location(r.B).Name,-18}  {r.DistanceTicks,3} ticks  danger {r.DangerRating:0.00}");
        }
    }

    private static void PrintLocalPrices(SimulationContext ctx, Location loc)
    {
        foreach (ActorId id in loc.Residents)
        {
            ActorState a = ctx.Actor(id);
            if (a.Producer is { } p)
            {
                decimal stock = a.Stock(p.Good);
                string idle = p.IsIdle(stock) ? "  <idle: warehouse full, price soft>" : string.Empty;
                Console.WriteLine($"        sells {p.Good,-9} ask {PriceModel.ProducerAsk(p, stock)}/u  stock {stock:0}/{p.StockCap:0}{idle}");
            }
        }
    }

    // ---------------------------------------------------------------- ship sheet (§5.7)

    private static void ShowShips(SimulationContext ctx, ActorState player)
    {
        Console.WriteLine();
        Console.WriteLine("  YOUR SHIPS — stats and the life-story behind them");
        Console.WriteLine("  ----------------------------------------------------------------------------");
        foreach (ShipId id in player.Ships)
        {
            Ship s = ctx.Ship(id);
            ShipClassInfo c = s.Class;
            Console.WriteLine($"   {c.Name}  [{s.Id}]   status: {s.Status} @ {ctx.Location(s.Location).Name}");
            Console.WriteLine($"     cargo {s.CargoCapacity:0}   speed x{c.SpeedMultiplier}   running ₡{c.RunningCostPerTick.Amount:0.00}/tick   incident sev x{c.IncidentSeverityModifier}");
            Console.WriteLine($"     niche: {c.Niche}");
            Console.WriteLine("     history:");
            foreach (ShipLogEntry log in s.History.TakeLast(6))
            {
                Console.WriteLine($"        t{log.Tick,-6} {log.Text}");
            }
        }
    }

    // ---------------------------------------------------------------- ledger (§5.7)

    private static void ShowLedger(SimulationContext ctx, ActorState player)
    {
        Console.WriteLine();
        Console.WriteLine("  THE LEDGER — your business, by exception");
        Console.WriteLine("  ----------------------------------------------------------------------------");
        Console.WriteLine($"   Credits: {player.Credits}");
        var active = ctx.Contracts.Values.Where(c => c.CounterpartyId == player.Id && c.Status == ContractStatus.Active).ToList();
        Console.WriteLine($"   Active contracts: {active.Count}");
        foreach (Contract c in active)
        {
            Console.WriteLine($"     {c.Id}  {c.Quantity:0} {c.Good}  → {ctx.Location(c.DestinationLocationId).Name}  fee {c.Fee}  due t{c.DeadlineTick}");
        }

        long fulfilled = ctx.Contracts.Values.Count(c => c.CounterpartyId == player.Id && c.Status == ContractStatus.Fulfilled);
        long breached = ctx.Contracts.Values.Count(c => c.CounterpartyId == player.Id && c.Status == ContractStatus.Breached);
        Console.WriteLine($"   Lifetime: {fulfilled} fulfilled, {breached} breached.");

        Console.WriteLine("   Reputation (where you can operate, §4.3):");
        var reps = ctx.Reputation.Snapshot
            .Where(kv => kv.Key.Actor == player.Id && Math.Abs(kv.Value) > 0.001m)
            .OrderByDescending(kv => kv.Value)
            .ToList();
        if (reps.Count == 0)
        {
            Console.WriteLine("     (a blank slate — nobody knows you yet)");
        }

        foreach (var kv in reps.Take(10))
        {
            Console.WriteLine($"     {kv.Key,-26} {Bar(kv.Value)} {kv.Value:+0.00;-0.00}");
        }
    }

    // ---------------------------------------------------------------- captain's log (§3.5)

    private static void ShowLog(SimulationContext ctx)
    {
        Console.WriteLine();
        Console.WriteLine("  CAPTAIN'S LOG — recent events in the sector");
        Console.WriteLine("  ----------------------------------------------------------------------------");
        foreach (JournalEntry e in ctx.Journal.Tail(18))
        {
            Console.WriteLine($"   t{e.Tick,-6} [{e.Category,-9}] {e.Text}");
        }
    }

    // ---------------------------------------------------------------- retire / legacy (§3.5)

    private static bool Retire(SimulationContext ctx, ActorState player)
    {
        Console.Write("  Retire and read your legacy? (y/N) ");
        string? confirm = Console.ReadLine();
        if (confirm is null || !confirm.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("  Back to work.");
            return false;
        }

        long fulfilled = ctx.Contracts.Values.Count(c => c.CounterpartyId == player.Id && c.Status == ContractStatus.Fulfilled);
        long breached = ctx.Contracts.Values.Count(c => c.CounterpartyId == player.Id && c.Status == ContractStatus.Breached);
        Console.WriteLine();
        Console.WriteLine("  ============================  LEGACY  ============================");
        Console.WriteLine($"   You flew for {ctx.CurrentTick} ticks out of {ctx.Location(player.Home).Name}.");
        Console.WriteLine($"   Contracts honoured: {fulfilled}.  Breached: {breached}.");
        Console.WriteLine($"   Final credits: {player.Credits}.");
        foreach (ShipId id in player.Ships)
        {
            Ship s = ctx.Ship(id);
            Console.WriteLine($"   You flew the {s.Class.Name} [{s.Id}] — {s.History.Count} entries in its log.");
        }

        Console.WriteLine("   The world goes on without you. By Galactic Accord.");
        Console.WriteLine("  =================================================================");
        return true;
    }

    // ---------------------------------------------------------------- helpers

    private static void Help()
    {
        Console.WriteLine("  Commands:");
        Console.WriteLine("    board            read the contract board visible to you");
        Console.WriteLine("    take <#>         open the Negotiation View for board entry #");
        Console.WriteLine("    run [ticks]      let time flow (default 50); auto-pauses on an alert");
        Console.WriteLine("    map              locations, routes, danger, and prices you know");
        Console.WriteLine("    ship             your ship sheet(s) and their history");
        Console.WriteLine("    ledger           credits, active contracts, reputation");
        Console.WriteLine("    log              recent sector events (captain's log)");
        Console.WriteLine("    retire           end the game and read your legacy");
        Console.WriteLine("    help             this list");
    }

    private static List<Ship> FreeShips(SimulationContext ctx, ActorState player) =>
        player.Ships.Select(ctx.Ship).Where(s => s.Status == ShipStatus.Docked).ToList();

    private static long ParseTicks(string[] parts, long fallback) =>
        parts.Length > 1 && long.TryParse(parts[1], out long t) && t > 0 ? t : fallback;

    private static string Band(DangerBand band) => band switch
    {
        DangerBand.Green => "[grn]",
        DangerBand.Amber => "[amb]",
        _ => "[RED]",
    };

    private static string Reach(AccordReach reach) => reach switch
    {
        AccordReach.DeepAccord => "core",
        AccordReach.AccordEdge => "mid",
        AccordReach.OutsideAccord => "frontr",
        _ => "black",
    };

    private static string Bar(decimal value)
    {
        int n = (int)Math.Round(Math.Abs(value) * 10m);
        n = Math.Clamp(n, 0, 10);
        char fill = value >= 0 ? '+' : '-';
        return "[" + new string(fill, n) + new string('·', 10 - n) + "]";
    }

    private static string Trunc(string s, int width) => s.Length <= width ? s : s[..(width - 1)] + "…";
}
