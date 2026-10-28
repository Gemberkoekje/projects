using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Cli;

/// <summary>
/// The headless soak harness (§5.6): run the economy with no player for many ticks and assert the
/// invariants hold throughout. This is also the primary balance instrument — read the telemetry,
/// move the knobs in <see cref="SimulationConfig"/>, repeat. Returns a process exit code.
/// </summary>
internal static class Soak
{
    public static int Run(long seed, long ticks)
    {
        Console.WriteLine($"== By Galactic Accord — soak run ==  seed={seed}  ticks={ticks:N0} (to {GameTime.FormatDate(ticks)})");
        WorldHandles handles = WorldGenerator.Generate(seed, withPlayer: false);
        SimulationContext ctx = handles.Context;
        Console.WriteLine($"World: {ctx.Actors.Count} actors, {ctx.Locations.Count} locations, {ctx.Routes.Count} routes. Baseline supply {ctx.InitialCredits}.");
        Console.WriteLine();

        const long chunk = 5000;
        long t = 0;
        while (t < ticks)
        {
            t = Math.Min(ticks, t + chunk);
            ctx.RunTo(t);

            IReadOnlyList<InvariantViolation> violations = Invariants.Check(ctx);
            if (violations.Count > 0)
            {
                Console.WriteLine($"INVARIANT VIOLATION at tick {ctx.CurrentTick} ({GameTime.FormatDate(ctx.CurrentTick)}):");
                foreach (InvariantViolation v in violations)
                {
                    Console.WriteLine($"  [{v.Name}] {v.Detail}");
                }

                return 1;
            }
        }

        PrintTelemetry(ctx);
        Console.WriteLine();
        Console.WriteLine("All invariants held for the whole run. ✓");
        return 0;
    }

    private static void PrintTelemetry(SimulationContext ctx)
    {
        decimal baseline = ctx.InitialCredits.Amount;
        decimal total = ctx.TotalCredits().Amount;
        decimal drift = baseline <= 0 ? 0 : (total - baseline) / baseline;

        Console.WriteLine("---- telemetry ----");
        Line("Tick", $"{ctx.CurrentTick.ToString("N0", CultureInfo.InvariantCulture)} ({GameTime.FormatDate(ctx.CurrentTick)})");
        Line("Total credit supply", $"{ctx.TotalCredits()}  (drift {drift:P1} from baseline)");
        Line("  in actor hands", ctx.TotalActorCredits().ToString());
        Line("  held in escrow", ctx.TotalEscrowHeld().ToString());
        Line("Faucet (income injected)", ctx.TotalFaucet.ToString());
        Line("Sink (costs/tax destroyed)", ctx.TotalSink.ToString());
        Line("Faucet multiplier (monitor)", ctx.FaucetMultiplier.ToString("0.00", CultureInfo.InvariantCulture));
        Line("Government revenue (taxed)", ctx.GovernmentRevenue.ToString());
        Line("Goods sold under Sales", ctx.GoodsSold.ToString("N0", CultureInfo.InvariantCulture));
        Console.WriteLine();
        Line("Contracts fulfilled", ctx.ContractsFulfilled.ToString("N0", CultureInfo.InvariantCulture));
        Line("Contracts breached (performer)", ctx.ContractsBreached.ToString("N0", CultureInfo.InvariantCulture));
        Line("Contracts defaulted (payer)", ctx.ContractsDefaulted.ToString("N0", CultureInfo.InvariantCulture));
        Line("Contracts cancelled", ctx.ContractsCancelled.ToString("N0", CultureInfo.InvariantCulture));
        Line("Transit incidents", ctx.IncidentsOccurred.ToString("N0", CultureInfo.InvariantCulture));
        Line("RNG draws consumed", ctx.Rng.DrawCount.ToString("N0", CultureInfo.InvariantCulture));
        Line("Journal entries recorded", ctx.Journal.TotalRecorded.ToString("N0", CultureInfo.InvariantCulture));

        Console.WriteLine();
        Console.WriteLine("Failure paths (design §16.3; a contract can count under several):");
        foreach (FailureKind kind in Enum.GetValues<FailureKind>())
        {
            Line("  " + kind, ctx.Failures(kind).ToString("N0", CultureInfo.InvariantCulture));
        }

        Console.WriteLine();
        Console.WriteLine("Signed Sales and Hauls by term package:");
        foreach (TermPackage package in Enum.GetValues<TermPackage>())
        {
            int signed = 0;
            int failed = 0;
            foreach (Contract c in ctx.Contracts.Values)
            {
                if (c is StorageContract || c.Terms.Package != package || c.SignedTick is null)
                {
                    continue;
                }

                signed++;
                if (c.Status is ContractStatus.Breached or ContractStatus.Cancelled or ContractStatus.Defaulted)
                {
                    failed++;
                }
            }

            Line("  " + package, $"{signed.ToString("N0", CultureInfo.InvariantCulture)} signed, {(signed == 0 ? 0m : (decimal)failed / signed):P1} failed or cancelled");
        }

        Console.WriteLine();
        Console.WriteLine("Signed by kind, and how many AI actors haggled in closed form (design §3.6) to a price other than posted:");
        foreach ((string label, Func<Contract, bool> kind) in new (string, Func<Contract, bool>)[]
                 {
                     ("Sale, wanted", c => c is SaleContract { Side: SaleSide.Wanted }),
                     ("Sale, for sale", c => c is SaleContract { Side: SaleSide.ForSale }),
                     ("Haul", c => c is HaulContract),
                 })
        {
            int signed = 0;
            int haggled = 0;
            decimal ratio = 0m;
            foreach (Contract c in ctx.Contracts.Values)
            {
                if (c.SignedTick is null || !kind(c))
                {
                    continue;
                }

                signed++;
                if (c.Price != c.OfferedPrice && c.OfferedPrice.Amount > 0m && c is not SaleContract { AtTheDoor: true })
                {
                    haggled++;
                    ratio += c.Price.Amount / c.OfferedPrice.Amount;
                }
            }

            string average = haggled == 0 ? "—" : (ratio / haggled).ToString("0.00", CultureInfo.InvariantCulture) + "× the posted price";
            Line("  " + label, $"{signed.ToString("N0", CultureInfo.InvariantCulture)} signed, {haggled.ToString("N0", CultureInfo.InvariantCulture)} haggled ({(signed == 0 ? 0m : (decimal)haggled / signed):P1}), on average {average}");
        }

        Console.WriteLine();
        Console.WriteLine("Information (design §7): what actors knew, and what the news told them:");
        Line("  Orders at a producer's door", $"{ctx.DoorOrders.ToString("N0", CultureInfo.InvariantCulture)}, turned down {ctx.DoorRefusedForPrice.ToString("N0", CultureInfo.InvariantCulture)} for the price, {ctx.DoorRefusedForStock.ToString("N0", CultureInfo.InvariantCulture)} for stock");
        NewsDesk news = ctx.News;
        long issues = Math.Max(1, news.IssuesPublished);
        Line("  The Daily Accord", $"{news.IssuesPublished.ToString("N0", CultureInfo.InvariantCulture)} issues, {(decimal)news.ItemsPublished / issues:0.0} items of {(decimal)news.CandidatesSeen / issues:0.0} contracts a day; {(news.ItemsPublished == 0 ? 0m : (decimal)news.FrontierItems / news.ItemsPublished):P0} about the frontier, {(news.ItemsPublished == 0 ? 0m : (decimal)news.PostedItems / news.ItemsPublished):P0} open posts");
        Line("  Paid to show posts further", $"{ctx.AdFeesPaid}: {string.Join(", ", Enumerable.Range(1, 4).Select(j => $"{ctx.AdsShown[j].ToString("N0", CultureInfo.InvariantCulture)} at {(j == 4 ? "4+" : j.ToString(CultureInfo.InvariantCulture))} jumps"))}; signed only through an ad {ctx.SignedViaAd.ToString("N0", CultureInfo.InvariantCulture)}");
        Line("  Signed from the news", $"{ctx.SignedFromNews.ToString("N0", CultureInfo.InvariantCulture)}; tries that found the post closed {ctx.FoundClosedFromNews.ToString("N0", CultureInfo.InvariantCulture)}");

        Console.WriteLine();
        Console.WriteLine("Goods (produced = consumed + held + lost):");
        Console.WriteLine($"   {"",-10} {"produced",12} {"consumed",12} {"held",10} {"lost",10}");
        foreach (GoodType good in Goods.All)
        {
            Console.WriteLine($"   {good,-10} {ctx.GoodsProduced(good),12:N0} {ctx.GoodsConsumed(good),12:N0} {ctx.GoodsHeld(good),10:N0} {ctx.GoodsLost(good),10:N0}");
        }

        Console.WriteLine();
        Console.WriteLine("Demand met (what each consumer used, of what it wanted to; plan risk R1 watches the frontier):");
        foreach (var actor in ctx.ActorsOrdered)
        {
            if (actor.Demands.Count == 0)
            {
                continue;
            }

            decimal beats = Math.Max(1m, ctx.CurrentTick / (decimal)ctx.Config.HeartbeatInterval);
            var shares = new List<string>();
            foreach (var demand in actor.Demands)
            {
                decimal share = actor.Consumed(demand.Good) / (demand.ConsumptionPerHeartbeat * beats);
                shares.Add($"{demand.Good} {share:P0}");
            }

            Console.WriteLine($"   {actor.Name,-24} {string.Join(", ", shares)}");
        }

        Console.WriteLine();
        Console.WriteLine("Contracts by kind and status:");
        foreach (ContractType kind in new[] { ContractType.Sale, ContractType.Haul, ContractType.Storage })
        {
            var byStatus = new SortedDictionary<ContractStatus, int>();
            foreach (Contract c in ctx.Contracts.Values)
            {
                if (c.Type == kind)
                {
                    byStatus[c.Status] = byStatus.GetValueOrDefault(c.Status) + 1;
                }
            }

            Console.Write($"   {kind,-8}");
            foreach (var kv in byStatus)
            {
                Console.Write($" {kv.Key}={kv.Value.ToString("N0", CultureInfo.InvariantCulture)}");
            }

            Console.WriteLine();
        }

        Console.WriteLine($"   (total {ctx.Contracts.Count.ToString("N0", CultureInfo.InvariantCulture)})");

        Console.WriteLine();
        Console.WriteLine("Trader solvency (idle fleets bleed running cost, design §6.5):");
        foreach (var actor in ctx.ActorsOrdered)
        {
            if (actor.MovesCargo)
            {
                Console.WriteLine($"   {Credits.Of(actor.Credits.Amount),-12} {actor.Role,-16} {actor.Name} ({actor.Ships.Count} ship)");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Wealth by actor (top of the heap — has anyone monopolised or gone bust?):");
        var ranked = new List<ActorSnapshot>();
        foreach (var actor in ctx.ActorsOrdered)
        {
            ranked.Add(new ActorSnapshot(actor.Name, actor.Role.ToString(), actor.Credits.Amount));
        }

        ranked.Sort((a, b) => b.Credits.CompareTo(a.Credits));
        int shown = 0;
        foreach (ActorSnapshot a in ranked)
        {
            Console.WriteLine($"   {Credits.Of(a.Credits),-12}  {a.Role,-16}  {a.Name}");
            if (++shown >= 8)
            {
                break;
            }
        }
    }

    private readonly record struct ActorSnapshot(string Name, string Role, decimal Credits);

    private static void Line(string label, string value) =>
        Console.WriteLine($"   {label,-34} {value}");
}
