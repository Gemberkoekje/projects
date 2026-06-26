using System;
using System.Collections.Generic;
using System.Globalization;
using ByGalacticAccord.Engine.Domain;
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
        Console.WriteLine($"== By Galactic Accord — soak run ==  seed={seed}  ticks={ticks:N0}");
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
                Console.WriteLine($"INVARIANT VIOLATION at tick {ctx.CurrentTick}:");
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
        Line("Tick", ctx.CurrentTick.ToString("N0", CultureInfo.InvariantCulture));
        Line("Total credit supply", $"{ctx.TotalCredits()}  (drift {drift:P1} from baseline)");
        Line("  in actor hands", ctx.TotalActorCredits().ToString());
        Line("  held in escrow", ctx.TotalEscrowHeld().ToString());
        Line("Faucet (income injected)", ctx.TotalFaucet.ToString());
        Line("Sink (costs/tax destroyed)", ctx.TotalSink.ToString());
        Line("Faucet multiplier (monitor)", ctx.FaucetMultiplier.ToString("0.00", CultureInfo.InvariantCulture));
        Line("Government revenue (taxed)", ctx.GovernmentRevenue.ToString());
        Console.WriteLine();
        Line("Contracts fulfilled", ctx.ContractsFulfilled.ToString("N0", CultureInfo.InvariantCulture));
        Line("Contracts breached", ctx.ContractsBreached.ToString("N0", CultureInfo.InvariantCulture));
        Line("Transit incidents", ctx.IncidentsOccurred.ToString("N0", CultureInfo.InvariantCulture));
        Line("Cargo: picked up / delivered / lost", $"{ctx.CargoPickedUp:N0} / {ctx.CargoDelivered:N0} / {ctx.CargoLost:N0}");
        Line("RNG draws consumed", ctx.Rng.DrawCount.ToString("N0", CultureInfo.InvariantCulture));
        Line("Journal entries recorded", ctx.Journal.TotalRecorded.ToString("N0", CultureInfo.InvariantCulture));

        Console.WriteLine();
        var byStatus = new Dictionary<ContractStatus, int>();
        foreach (Contract c in ctx.Contracts.Values)
        {
            byStatus[c.Status] = byStatus.GetValueOrDefault(c.Status) + 1;
        }

        Console.Write("Contracts by status: ");
        foreach (var kv in byStatus)
        {
            Console.Write($"{kv.Key}={kv.Value}  ");
        }

        Console.WriteLine($"(total {ctx.Contracts.Count})");

        Console.WriteLine();
        Console.WriteLine("Hauler solvency (idle fleets bleed running cost, §3.6):");
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
