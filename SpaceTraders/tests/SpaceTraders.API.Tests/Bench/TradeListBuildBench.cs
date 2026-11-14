// A memory bench measures from a collected heap and reads its size: what S1215 warns against in other code.
#pragma warning disable S1215

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SpaceTraders.Application.Construction;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using SpaceTraders.Infrastructure.Persistence.Scoping;
using Xunit;
using Xunit.Abstractions;

namespace SpaceTraders.API.Tests.Bench;

/// <summary>
/// Slice 6.67 (B91): on a copy of the day's world, what one build of the trade list costs, as the background worker builds it
/// and as slices 6.63 to 6.66 built it: its time, the bytes it allocates, the most the managed heap held while it ran, and a
/// fingerprint of the list, which must be the same both ways.
/// </summary>
/// <remarks>Runs only with <c>SPACETRADERS_BENCH_DB</c> set, as <see cref="TickBench"/>; it writes nothing.</remarks>
/// <param name="output">Where the measures go.</param>
[Trait("Category", "Bench")]
public sealed class TradeListBuildBench(ITestOutputHelper output)
{
    [SkippableFact]
    public async Task OneBuildOfTheTradeList_OnACopyOfTheDaysWorld()
    {
        var connectionString = Environment.GetEnvironmentVariable("SPACETRADERS_BENCH_DB");
        Skip.If(string.IsNullOrWhiteSpace(connectionString), "SPACETRADERS_BENCH_DB isn't set: see tools/bench/README.md.");

        var before = Environment.GetEnvironmentVariable(TickBench.ConnectionStringVariable);
        Environment.SetEnvironmentVariable(TickBench.ConnectionStringVariable, connectionString);
        try
        {
            await using var factory = new BenchApiFactory(connectionString, TickBench.ReadChains());
            await TickBench.ShouldUseOnlyAsync(factory.Services, connectionString);
            factory.Services.GetRequiredService<IAgentDataScope>().Set(await TickBench.AgentOfAsync(connectionString));

            await using var scope = factory.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var hold = (await services.GetRequiredService<IShipRepository>().GetAllAsync(CancellationToken.None))
                .Where(ship => ship.IsTradingCapable)
                .Select(ship => ship.CargoCapacity)
                .DefaultIfEmpty(0)
                .Max();
            var home = await services.GetRequiredService<IConstructionSites>().HomeSystemAsync(CancellationToken.None);
            var universe = await services.GetRequiredService<ITradeContextReader>().ReadReachAsync(home, CancellationToken.None);
            var systems = universe.Map.Waypoints.Select(waypoint => waypoint.SystemSymbol).ToList();

            // Three builds on the one map, so the same markets count as fresh in each: the first works out the gates' ways and
            // keeps them on the map, as each build of the worker does on the map it reads; the second and third find them there.
            var cold = Measure("Streamed, map read afresh", universe, hold, systems, Streamed);
            var before67 = Measure("Listed, as before 6.67", universe, hold, systems, ListedThenSorted);
            var streamed = Measure("Streamed", universe, hold, systems, Streamed);
            Fingerprint(streamed).Should().Be(Fingerprint(before67), "streaming keeps the same trades in the same order");
            Fingerprint(cold).Should().Be(Fingerprint(streamed));
        }
        finally
        {
            Environment.SetEnvironmentVariable(TickBench.ConnectionStringVariable, before);
        }
    }

    /// <summary>One build, measured: its time, the bytes it allocated and the most the managed heap held above its start.</summary>
    private TradeList Measure(string name, TradeContext universe, int hold, IReadOnlyList<string> systems, Func<TradeContext, int, IReadOnlyList<string>, TradeList> build)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var baseline = GC.GetTotalMemory(forceFullCollection: true);
        var peak = baseline;
        TradeList list;
        double seconds;
        long allocated;
        using (new Timer(_ => InterlockedMax(ref peak, GC.GetTotalMemory(forceFullCollection: false)), null, 0, 10))
        {
            allocated = GC.GetAllocatedBytesForCurrentThread();
            var clock = Stopwatch.StartNew();
            list = build(universe, hold, systems);
            seconds = clock.Elapsed.TotalSeconds;
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            InterlockedMax(ref peak, GC.GetTotalMemory(forceFullCollection: false));
        }

        list.Entries.Should().NotBeEmpty("the copy's home gate is built and its markets trade");
        output.WriteLine(Invariant(
            $"{name}: {universe.Map.MarketWaypoints.Count:N0} markets, hold {hold}: {list.Entries.Count:N0} trades kept in {seconds:F1} s; allocated {allocated / 1048576.0:N0} MiB, managed heap at most {(Interlocked.Read(ref peak) - baseline) / 1048576.0:N0} MiB above its start; list {Fingerprint(list)}"));
        return list;
    }

    /// <summary>As <see cref="TradeListService"/> builds it, with the seeded sizes: each trade read once, the best kept.</summary>
    private static TradeList Streamed(TradeContext universe, int hold, IReadOnlyList<string> systems)
        => TradeListBuilder.Select(
            TradeListBuilder.Estimate(universe.Map, hold, TripTime.DefaultEngineSpeed, universe.MinProfitPerUnit),
            universe.Map.Gates.Now,
            TradeListService.DefaultPerSystem,
            TradeListService.DefaultSize,
            systems);

    /// <summary>
    /// As slices 6.63 to 6.66 built it: every trade listed, then all of them sorted and grouped by system; the selection is
    /// copied here from before slice 6.67 as the reference the streamed one must match.
    /// </summary>
    private static TradeList ListedThenSorted(TradeContext universe, int hold, IReadOnlyList<string> systems)
    {
        var estimates = TradeListBuilder.Estimate(universe.Map, hold, TripTime.DefaultEngineSpeed, universe.MinProfitPerUnit).ToList();
        var ordered = estimates
            .Where(entry => entry.Profit > 0)
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => TradeRoutePlanner.RouteKey(entry.TradeSymbol, entry.BuyWaypointSymbol, entry.SellWaypointSymbol), StringComparer.Ordinal)
            .ToList();
        var kept = new HashSet<TradeListEntry>(ordered
            .GroupBy(entry => WaypointSymbols.SystemOf(entry.BuyWaypointSymbol), StringComparer.OrdinalIgnoreCase)
            .SelectMany(bySystem => bySystem.Take(TradeListService.DefaultPerSystem)));
        kept.UnionWith(ordered.Take(TradeListService.DefaultSize));
        return new TradeList(universe.Map.Gates.Now, [.. ordered.Where(kept.Contains)], systems);
    }

    /// <summary>The list's trades and values, in order, as a short SHA-256: the same list gives the same one.</summary>
    private static string Fingerprint(TradeList list)
    {
        var text = new StringBuilder();
        foreach (var entry in list.Entries)
        {
            text.Append(CultureInfo.InvariantCulture, $"{entry.TradeSymbol}|{entry.BuyWaypointSymbol}|{entry.SellWaypointSymbol}|{entry.Units}|{entry.Profit}|{entry.Seconds:R}|{entry.Jumps}|{entry.PredictionWeight:R}|{entry.FeedsProduction}\n");
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }

    private static void InterlockedMax(ref long target, long value)
    {
        var current = Interlocked.Read(ref target);
        while (value > current)
        {
            var seen = Interlocked.CompareExchange(ref target, value, current);
            if (seen == current)
            {
                return;
            }

            current = seen;
        }
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
