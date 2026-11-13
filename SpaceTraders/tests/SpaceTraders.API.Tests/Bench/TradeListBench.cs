using System.Diagnostics;
using System.Globalization;
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
/// Slice 6.63 (D148): on a copy of the day's world, what a trade list of each size costs the traders against working out
/// every route, and what it saves. Each trader's best route is worked out exactly, once over every route and once over a
/// list's trades only; the list is built as the background worker builds it.
/// </summary>
/// <remarks>Runs only with <c>SPACETRADERS_BENCH_DB</c> set, as <see cref="TickBench"/>; it writes nothing.</remarks>
/// <param name="output">Where the measures go.</param>
[Trait("Category", "Bench")]
public sealed class TradeListBench(ITestOutputHelper output)
{
    private static readonly IReadOnlySet<string> NoneHeld = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The lists tried: the best of each system's buy markets, and the best of all.</summary>
    private static readonly (int PerSystem, int Total)[] Sizes =
    [
        (10, 0), (25, 0), (50, 0), (100, 0), (0, 2_000), (0, 5_000), (25, 2_000), (50, 5_000),
    ];

    [SkippableFact]
    public async Task ATradeList_AgainstEveryRoute_OnACopyOfTheDaysWorld()
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
            var traders = (await services.GetRequiredService<IShipRepository>().GetAllAsync(CancellationToken.None))
                .Where(ship => ship.IsTradingCapable)
                .OrderBy(ship => ship.Symbol, StringComparer.Ordinal)
                .ToList();
            traders.Should().NotBeEmpty("the copy holds a fleet that trades");
            var reader = services.GetRequiredService<ITradeContextReader>();
            var home = await services.GetRequiredService<IConstructionSites>().HomeSystemAsync(CancellationToken.None);

            // The list, as the worker builds it: from the trading plan's reach of home, for the fleet's largest hold.
            var clock = Stopwatch.StartNew();
            var universe = await reader.ReadReachAsync(home, CancellationToken.None);
            var read = clock.Elapsed.TotalSeconds;
            clock.Restart();
            var hold = traders.Max(ship => ship.CargoCapacity);
            var estimates = TradeListBuilder.Estimate(universe.Map, hold, TripTime.DefaultEngineSpeed, universe.MinProfitPerUnit);
            var estimated = clock.Elapsed.TotalSeconds;
            output.WriteLine(Invariant($"Universe from {home}: {universe.Map.MarketWaypoints.Count:N0} markets, read in {read:F1} s; {estimates.Count:N0} trades estimated for a hold of {hold} in {estimated:F1} s."));

            // Every route, as now: each trader's best.
            var contexts = await ReadAsync(reader, traders);
            var best = new Dictionary<string, TradeRoute>(StringComparer.Ordinal);
            clock.Restart();
            foreach (var ship in traders)
            {
                var context = contexts[ship.SystemSymbol ?? string.Empty];
                if (TradeRoutePlanner.Rank(context.Map, ship, context.CreditsForCargo, context.MinProfitPerUnit, NoneHeld, HeldBuys.None) is [var first, ..])
                {
                    best[ship.Symbol] = first;
                }
            }

            output.WriteLine(Invariant($"Every route: {traders.Count} traders ranked in {clock.Elapsed.TotalSeconds:F1} s; {best.Count} have a route."));

            foreach (var (perSystem, total) in Sizes)
            {
                clock.Restart();
                var list = TradeListBuilder.Select(estimates, universe.Map.Gates.Now, perSystem, total, universe.Map.Waypoints.Select(waypoint => waypoint.SystemSymbol));
                var selected = clock.Elapsed.TotalSeconds;

                // Quality: the same maps, so the same prices; a ship's pick from the list against its best of every route.
                var losses = new List<double>();
                var same = 0;
                var lost = 0;
                foreach (var ship in traders)
                {
                    var context = contexts[ship.SystemSymbol ?? string.Empty];
                    var routes = TradeRoutePlanner.Rank(Listed(context.Map, list, ship), ship, context.CreditsForCargo, context.MinProfitPerUnit, NoneHeld, HeldBuys.None);
                    if (!best.TryGetValue(ship.Symbol, out var everyRoute))
                    {
                        continue;
                    }

                    if (routes.Count == 0)
                    {
                        lost++;
                        losses.Add(1);
                        output.WriteLine(Invariant(
                            $"  no route from the list: {ship.Symbol} at {ship.WaypointSymbol} ({context.Map.MarketWaypoints.Count} markets on its map, covered {list.Covers(ship.SystemSymbol ?? string.Empty)}), tank {ship.FuelCapacity}, fuel {ship.FuelCurrent}, hold {ship.CargoCapacity - ship.CargoCurrent} free of {ship.CargoCapacity}; best of every route {everyRoute.Key}, {everyRoute.Jumps} jumps, {TradeRoutePlanner.RankingRate(everyRoute):N0}/h, on the list {list.HasGoodAt(everyRoute.BuyWaypointSymbol, everyRoute.TradeSymbol) && list.SellsFor(everyRoute.BuyWaypointSymbol, everyRoute.TradeSymbol).Contains(everyRoute.SellWaypointSymbol, StringComparer.OrdinalIgnoreCase)}"));
                        continue;
                    }

                    same += routes[0].Key == everyRoute.Key ? 1 : 0;
                    losses.Add(Math.Max(0, 1 - (TradeRoutePlanner.RankingRate(routes[0]) / TradeRoutePlanner.RankingRate(everyRoute))));
                }

                // Time: maps read again, so nothing is planned yet, as at a pass's start.
                var cold = await ReadAsync(reader, traders);
                clock.Restart();
                foreach (var ship in traders)
                {
                    var context = cold[ship.SystemSymbol ?? string.Empty];
                    _ = TradeRoutePlanner.Rank(Listed(context.Map, list, ship), ship, context.CreditsForCargo, context.MinProfitPerUnit, NoneHeld, HeldBuys.None);
                }

                var ranked = clock.Elapsed.TotalSeconds;
                clock.Restart();
                foreach (var ship in traders)
                {
                    var context = cold[ship.SystemSymbol ?? string.Empty];
                    _ = TradeRoutePlanner.Judge(Listed(context.Map, list, ship), ship, context.CreditsForCargo, context.MinProfitPerUnit, NoneHeld, HeldBuys.None);
                }

                var judged = clock.Elapsed.TotalSeconds;
                losses.Sort();
                output.WriteLine(Invariant(
                    $"List {perSystem}/system + best {total}: {list.Entries.Count:N0} trades (selected in {selected:F2} s); Rank {ranked:F1} s, Judge {judged:F1} s, cold; same best route {same}/{losses.Count}, none {lost}; rate lost median {At(losses, 0.5):P1}, 90th {At(losses, 0.9):P1}, most {At(losses, 1):P1}"));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(TickBench.ConnectionStringVariable, before);
        }
    }

    /// <summary>Each trader's reach map, one a system, as the trading plan reads them.</summary>
    private static async Task<Dictionary<string, TradeContext>> ReadAsync(ITradeContextReader reader, IReadOnlyList<ShipModel> traders)
    {
        var contexts = new Dictionary<string, TradeContext>(StringComparer.OrdinalIgnoreCase);
        foreach (var system in traders.Select(ship => ship.SystemSymbol ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            contexts[system] = await reader.ReadReachAsync(system, CancellationToken.None);
        }

        return contexts;
    }

    /// <summary>The map with the list, where the list covers the ship's system, as the reader hands it.</summary>
    private static TradeMarketMap Listed(TradeMarketMap map, TradeList list, ShipModel ship)
        => list.Covers(ship.SystemSymbol ?? string.Empty) ? map.WithTradeList(list) : map;

    private static double At(List<double> sorted, double at)
        => sorted.Count == 0 ? double.NaN : sorted[(int)Math.Min(sorted.Count - 1, sorted.Count * at)];

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
