using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Trading;

/// <summary>
/// Builds the <see cref="TradeList"/> (PLAN.md slice 6.63, D148): every trade of the known universe estimated from the buy market
/// on, for no ship in particular, and the high-value ones kept. Measured on 2026-11-13: across systems fuel is 0.9% of a route's
/// margin but each jump's antimatter 49%, so the haul's fuel is an average cost per unit of distance and its antimatter is
/// priced at each gate.
/// </summary>
public static class TradeListBuilder
{
    /// <summary>A sliver of floating-point error, as the planner's: an exact sum must not round a credit the wrong way.</summary>
    private const double Sliver = 1e-6;

    /// <summary>
    /// Estimates every trade on the map with a price gap: a hold of <paramref name="hold"/> units, as far as each unit earns
    /// <paramref name="minProfitPerUnit"/> at the batch prices (D79), for nothing a feeder of the gate's chain (D90); its haul
    /// from the buy market straight to the sell market, through the fewest gates within reach (D96), at the map's average
    /// fuel price per unit of distance and each gate's antimatter (D63). The rules a route keeps stay: only markets recent or
    /// predicted (<see cref="TradeMarketMap.IsFresh"/>), and no gate material bought in the gate's system (D128).
    /// </summary>
    /// <remarks>
    /// One at a time, as they are worked out, and none kept here (slice 6.67, B91): on 2026-11-14 the 2.3 million trades of
    /// 3,258 markets, held in a list before <see cref="Select(IEnumerable{TradeListEntry}, DateTimeOffset, int, int, IEnumerable{string})"/>
    /// kept about 7,000, made each build's heap swing by half a gigabyte. Each enumeration works them out again; a caller that
    /// wants them all lists them.
    /// </remarks>
    /// <param name="map">The markets of the known universe.</param>
    /// <param name="hold">The units a trade is set up for: the fleet's largest hold.</param>
    /// <param name="speed">The engine speed its time is worked out at.</param>
    /// <param name="minProfitPerUnit">What each unit must earn (D14).</param>
    /// <returns>The trades, by buy market, in no particular order.</returns>
    public static IEnumerable<TradeListEntry> Estimate(TradeMarketMap map, int hold, int speed, int minProfitPerUnit)
    {
        ArgumentNullException.ThrowIfNull(map);

        return hold <= 0 ? [] : Estimated(map, hold, speed, minProfitPerUnit);
    }

    /// <summary>
    /// The high-value trades of <paramref name="estimates"/>, by <see cref="TradeListEntry.Value"/>: the best
    /// <paramref name="perSystem"/> bought in each system, so a ship anywhere finds a close one (D148: "A ship can then take a
    /// close high value trade to do"), and the best <paramref name="total"/> of all.
    /// </summary>
    /// <param name="estimates">The trades, as <see cref="Estimate"/> gives them.</param>
    /// <param name="builtAt">When their markets were read.</param>
    /// <param name="perSystem">How many to keep of each system's buy markets.</param>
    /// <param name="total">How many to keep of all.</param>
    /// <param name="systems">The systems whose markets were estimated (<see cref="TradeList.Covers"/>).</param>
    /// <returns>The list, best first.</returns>
    public static TradeList Select(IEnumerable<TradeListEntry> estimates, DateTimeOffset builtAt, int perSystem, int total, IEnumerable<string> systems)
        => Select(estimates, builtAt, perSystem, total, systems, out _);

    /// <summary>
    /// The high-value trades of <paramref name="estimates"/>, as the overload without a count, and how many it looked at.
    /// </summary>
    /// <remarks>
    /// It reads the trades once and keeps only the best of each system and of all as it goes (slice 6.67, B91), not every
    /// trade sorted: the same list, best first by value and then by route key, as before.
    /// </remarks>
    /// <param name="estimates">The trades, as <see cref="Estimate"/> gives them.</param>
    /// <param name="builtAt">When their markets were read.</param>
    /// <param name="perSystem">How many to keep of each system's buy markets.</param>
    /// <param name="total">How many to keep of all.</param>
    /// <param name="systems">The systems whose markets were estimated (<see cref="TradeList.Covers"/>).</param>
    /// <param name="estimated">How many trades it looked at.</param>
    /// <returns>The list, best first.</returns>
    public static TradeList Select(
        IEnumerable<TradeListEntry> estimates,
        DateTimeOffset builtAt,
        int perSystem,
        int total,
        IEnumerable<string> systems,
        out int estimated)
    {
        ArgumentNullException.ThrowIfNull(estimates);
        ArgumentNullException.ThrowIfNull(systems);

        var ofAll = new Best(Math.Max(0, total));
        var bySystem = new Dictionary<string, Best>(StringComparer.OrdinalIgnoreCase);
        var ofSystem = new Best(0);
        var lastBuy = string.Empty;
        var anyBuy = false;
        estimated = 0;
        foreach (var entry in estimates)
        {
            estimated++;
            if (entry.Profit <= 0)
            {
                continue;
            }

            // The trades come by buy market, so its system is looked up once a market.
            if (!anyBuy || !entry.BuyWaypointSymbol.Equals(lastBuy, StringComparison.Ordinal))
            {
                anyBuy = true;
                lastBuy = entry.BuyWaypointSymbol;
                var system = WaypointSymbols.SystemOf(lastBuy);
                if (!bySystem.TryGetValue(system, out var best))
                {
                    best = new Best(Math.Max(0, perSystem));
                    bySystem[system] = best;
                }

                ofSystem = best;
            }

            ofSystem.Offer(entry);
            ofAll.Offer(entry);
        }

        var kept = new HashSet<TradeListEntry>(ReferenceEqualityComparer.Instance);
        foreach (var best in bySystem.Values)
        {
            kept.UnionWith(best.Kept);
        }

        kept.UnionWith(ofAll.Kept);
        var ordered = kept.ToList();
        ordered.Sort(Ranking.Instance);
        return new TradeList(builtAt, ordered, systems);
    }

    /// <summary>The trades, one at a time, with each buy market's hauls worked out once and dropped after it.</summary>
    private static IEnumerable<TradeListEntry> Estimated(TradeMarketMap map, int hold, int speed, int minProfitPerUnit)
    {
        // A haul depends only on its two markets; the buy market is the outer loop, so its hauls are needed only while it is.
        var hauls = new Dictionary<string, Haul>(StringComparer.Ordinal);
        foreach (var buy in map.MarketWaypoints.Where(map.IsFresh))
        {
            hauls.Clear();
            foreach (var good in map.GoodsAt(buy).Where(good => good.PurchasePrice > 0 && good.TradeVolume > 0))
            {
                if (map.IsConstructionMaterialAt(buy, good.Symbol))
                {
                    continue;
                }

                foreach (var sell in map.MarketsTrading(good.Symbol))
                {
                    if (sell.Equals(buy, StringComparison.OrdinalIgnoreCase)
                        || !map.IsFresh(sell)
                        || !map.TryGetGood(sell, good.Symbol, out var atSell)
                        || atSell.SellPrice <= 0
                        || atSell.TradeVolume <= 0
                        || atSell.SellPrice < good.PurchasePrice)
                    {
                        continue;
                    }

                    var feedsGate = map.ConstructionFeedOf(sell, good.Symbol).Feeds;
                    if (atSell.SellPrice == good.PurchasePrice && !feedsGate)
                    {
                        continue;
                    }

                    if (!hauls.TryGetValue(sell, out var haul))
                    {
                        haul = HaulOf(map, buy, sell, speed);
                        hauls[sell] = haul;
                    }

                    if (!haul.Found || !TryFill(good, atSell, hold, minProfitPerUnit, feedsGate, out var units, out var margin))
                    {
                        continue;
                    }

                    yield return new TradeListEntry(
                        good.Symbol,
                        buy,
                        sell,
                        units,
                        (long)Math.Floor(margin + Sliver) - haul.FuelCost - haul.AntimatterCost,
                        haul.Seconds,
                        haul.Jumps)
                    {
                        PredictionWeight = map.PredictionWeightOf(buy) * map.PredictionWeightOf(sell),
                        FeedsProduction = !map.IsEndProduct(good.Symbol) && !map.Exchanges(sell, good.Symbol),
                    };
                }
            }
        }
    }

    /// <summary>
    /// The units of a hold that each earn the minimum at the batch prices, bought a step dearer and sold a step cheaper each
    /// batch (D79), and what they earn together.
    /// </summary>
    private static bool TryFill(
        TradeGoodSnapshot atBuy,
        TradeGoodSnapshot atSell,
        int hold,
        int minProfitPerUnit,
        bool feedsGate,
        out int units,
        out double margin)
    {
        units = 0;
        margin = 0;
        while (units < hold)
        {
            // A batch's units share their prices: the batch is taken whole, up to the hold, or not at all.
            var buy = PriceSteps.PurchasePriceOf(atBuy.PurchasePrice, units, atBuy.TradeVolume);
            var sell = PriceSteps.SalePriceOf(atSell.SellPrice, units, atSell.TradeVolume);
            if (!TradeRoutePlanner.Earns(sell - buy, minProfitPerUnit, feedsGate))
            {
                break;
            }

            var batch = Math.Min(hold - units, Math.Min(
                atBuy.TradeVolume - (units % atBuy.TradeVolume),
                atSell.TradeVolume - (units % atSell.TradeVolume)));
            margin += (sell - buy) * batch;
            units += batch;
        }

        return units > 0;
    }

    /// <summary>
    /// The haul from the buy market to the sell market: within a system straight there; to another, to the system's gate,
    /// through the fewest jumps within reach, and from the last gate on. No refuelling stops: its fuel is the map's average
    /// price for each unit of distance.
    /// </summary>
    private static Haul HaulOf(TradeMarketMap map, string buy, string sell, int speed)
    {
        List<string> stops;
        IReadOnlyList<Exploring.GateJump> jumps = [];
        int fuel;
        if (map.SameSystem(buy, sell))
        {
            if (!map.TryGetDistance(buy, sell, out var distance))
            {
                return Haul.None;
            }

            fuel = TradeRoutePlanner.CruiseFuel(buy, sell, distance);
            stops = [sell];
        }
        else
        {
            if (!map.Gates.TryFindWay(map.SystemOf(buy), map.SystemOf(sell), out jumps) || jumps.Count == 0)
            {
                return Haul.None;
            }

            var gate = jumps[0].GateWaypointSymbol;
            var lastGate = jumps[^1].DestinationGateWaypointSymbol;
            if (!TryLeg(map, buy, gate, out var toGate) || !TryLeg(map, lastGate, sell, out var fromGate))
            {
                return Haul.None;
            }

            fuel = toGate + fromGate;
            stops = [.. gate.Equals(buy, StringComparison.OrdinalIgnoreCase) ? Array.Empty<string>() : [gate]];
            stops.AddRange(jumps.Select(jump => jump.DestinationGateWaypointSymbol));
            if (!lastGate.Equals(sell, StringComparison.OrdinalIgnoreCase))
            {
                stops.Add(sell);
            }
        }

        var fuelCost = (long)Math.Ceiling(fuel * (double)map.AverageFuelPrice / TradeRoutePlanner.FuelPerMarketUnit);
        var antimatter = jumps.Sum(jump => map.Gates.AntimatterAt(jump.GateWaypointSymbol));
        var seconds = TripTime.TradeSeconds(map, buy, [], buy, stops, speed);
        return new Haul(true, fuelCost, antimatter, seconds, jumps.Count);
    }

    /// <summary>A leg within one system, as the fuel it burns in CRUISE.</summary>
    private static bool TryLeg(TradeMarketMap map, string from, string to, out int fuel)
    {
        fuel = 0;
        if (from.Equals(to, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!map.TryGetDistance(from, to, out var distance))
        {
            return false;
        }

        fuel = TradeRoutePlanner.CruiseFuel(from, to, distance);
        return true;
    }

    /// <summary>A haul's estimate, or none.</summary>
    private sealed record Haul(bool Found, long FuelCost, long AntimatterCost, double Seconds, int Jumps)
    {
        public static Haul None { get; } = new(false, 0, 0, 0, 0);
    }

    /// <summary>
    /// The order of the list (D148): by <see cref="TradeListEntry.Value"/>, highest first, then by route key, as a ship's routes
    /// are ranked. The route key is built only when two values are the same.
    /// </summary>
    private sealed class Ranking : IComparer<TradeListEntry>
    {
        public static Ranking Instance { get; } = new();

        public int Compare(TradeListEntry? x, TradeListEntry? y)
        {
            ArgumentNullException.ThrowIfNull(x);
            ArgumentNullException.ThrowIfNull(y);

            var byValue = y.Value.CompareTo(x.Value);
            return byValue != 0
                ? byValue
                : string.CompareOrdinal(
                    TradeRoutePlanner.RouteKey(x.TradeSymbol, x.BuyWaypointSymbol, x.SellWaypointSymbol),
                    TradeRoutePlanner.RouteKey(y.TradeSymbol, y.BuyWaypointSymbol, y.SellWaypointSymbol));
        }
    }

    /// <summary>The best few trades seen so far: a heap with the worst kept on top, so a trade below it costs one comparison.</summary>
    /// <param name="size">How many it keeps.</param>
    private sealed class Best(int size)
    {
        private readonly PriorityQueue<TradeListEntry, TradeListEntry> _heap = new(Comparer<TradeListEntry>.Create(static (kept, other) => Ranking.Instance.Compare(other, kept)));

        /// <summary>The trades kept, in no particular order.</summary>
        public IEnumerable<TradeListEntry> Kept => _heap.UnorderedItems.Select(item => item.Element);

        /// <summary>Keeps the trade while there is room, or in place of the worst kept when it ranks above it.</summary>
        /// <param name="entry">The trade.</param>
        public void Offer(TradeListEntry entry)
        {
            if (size == 0)
            {
                return;
            }

            if (_heap.Count < size)
            {
                _heap.Enqueue(entry, entry);
            }
            else
            {
                _heap.EnqueueDequeue(entry, entry);
            }
        }
    }
}
