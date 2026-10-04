using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Siphoning;

namespace SpaceTraders.Application.Exploring;

/// <summary>
/// What each system the bot knows offers, for the systems dashboard (asked on 2026-10-04: "a systems grafana dashboard with a
/// more wide view of which systems have been explored and what kind of mining, trading and shipyard opportunities it
/// gives"): how far it is, its gate, what is there, what can be mined or siphoned, which raw goods its markets buy, and its
/// best trades. Its shipyards are in the shipyard metrics already.
/// </summary>
public static class SystemOpportunities
{
    /// <summary>How many trades each system lists: the best by margin, one per good.</summary>
    public const int TradesPerSystem = 5;

    private const string ImportType = "IMPORT";
    private const string ExportType = "EXPORT";
    private const string ExchangeType = "EXCHANGE";
    private const string UnchartedTrait = "UNCHARTED";

    /// <summary>Sums up every system the explore plan knows, and every system with cached waypoints or markets.</summary>
    /// <param name="plan">The explore plan's state; null when the plan never ran.</param>
    /// <param name="homeSystemSymbol">The headquarters' system.</param>
    /// <param name="waypoints">Every cached waypoint.</param>
    /// <param name="markets">Every cached market with its prices.</param>
    /// <param name="now">The time to judge the gates by.</param>
    /// <returns>One sample per system, by symbol.</returns>
    public static IReadOnlyList<SystemSample> Summarise(
        ExplorePlanState? plan,
        string homeSystemSymbol,
        IReadOnlyList<WaypointCacheModel> waypoints,
        IReadOnlyList<MarketSnapshot> markets,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(homeSystemSymbol);
        ArgumentNullException.ThrowIfNull(waypoints);
        ArgumentNullException.ThrowIfNull(markets);

        var known = (plan?.Systems ?? [])
            .GroupBy(system => system.SystemSymbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var jumps = plan is null ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) : ExploreAtlas.JumpsFromHome(plan, now);
        var bySystem = waypoints
            .GroupBy(waypoint => waypoint.SystemSymbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var marketsBySystem = markets
            .GroupBy(market => market.SystemSymbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var symbols = known.Keys
            .Concat(bySystem.Keys)
            .Concat(marketsBySystem.Keys)
            .Append(homeSystemSymbol)
            .Where(symbol => symbol.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal);

        var samples = new List<SystemSample>();
        foreach (var symbol in symbols)
        {
            var system = known.GetValueOrDefault(symbol);
            var cached = bySystem.GetValueOrDefault(symbol) ?? [];
            var gate = system?.GateWaypointSymbol is { Length: > 0 } gateSymbol
                ? gateSymbol
                : cached.FirstOrDefault(waypoint => waypoint.Type.Equals("JUMP_GATE", StringComparison.OrdinalIgnoreCase))?.Symbol ?? string.Empty;
            var systemMarkets = marketsBySystem.GetValueOrDefault(symbol) ?? [];
            samples.Add(new SystemSample
            {
                System = symbol,
                State = StateOf(symbol, homeSystemSymbol, system, now),
                Gate = gate,
                GateState = GateStateOf(system),
                Jumps = jumps.TryGetValue(symbol, out var away) ? away : null,
                ExploredAt = system?.ExploredAt,
                Connections = [.. (system?.Connections ?? []).Select(Ports.WaypointSymbols.SystemOf).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)],
                Markets = cached.Count(waypoint => waypoint.HasMarket),
                Shipyards = cached.Count(waypoint => waypoint.HasShipyard),
                Uncharted = cached.Count(waypoint => AsteroidDeposits.TraitSymbols(waypoint.TraitsJson).Contains(UnchartedTrait, StringComparer.OrdinalIgnoreCase)),
                WaypointTypes = cached
                    .GroupBy(waypoint => waypoint.Type, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
                GatheringSites = cached
                    .SelectMany(waypoint => AsteroidDeposits.OresAt(waypoint).Concat(GasGiants.GasesAt(waypoint)))
                    .GroupBy(good => good, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
                RawGoods = RawGoodsBought(systemMarkets),
                Trades = BestTrades(systemMarkets),
            });
        }

        return samples;
    }

    /// <summary>
    /// The raw goods the system's markets buy (ores and gases, imported or exchanged): the best price one pays, where, and
    /// the lowest supply among them: a market short of an ore is where a drone earns most.
    /// </summary>
    private static IReadOnlyList<RawGoodSample> RawGoodsBought(IReadOnlyList<MarketSnapshot> markets)
        => [.. markets
            .SelectMany(market => market.TradeGoods.Select(good => (Market: market.WaypointSymbol, Good: good)))
            .Where(entry => (AsteroidDeposits.Ores.Contains(entry.Good.Symbol) || GasGiants.Gases.Contains(entry.Good.Symbol))
                && entry.Good.SellPrice > 0
                && (entry.Good.Type.Equals(ImportType, StringComparison.OrdinalIgnoreCase) || entry.Good.Type.Equals(ExchangeType, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(entry => entry.Good.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var best = group.OrderByDescending(entry => entry.Good.SellPrice).ThenBy(entry => entry.Market, StringComparer.Ordinal).First();
                return new RawGoodSample
                {
                    Good = group.Key,
                    Price = best.Good.SellPrice,
                    Market = best.Market,
                    Supply = SupplyName(group.Min(entry => SupplyRank(entry.Good.Supply))),
                };
            })
            .OrderBy(good => good.Good, StringComparer.Ordinal)];

    /// <summary>
    /// The best trade of each good within the system, buying where it is cheapest (an export or exchange) and selling where
    /// it pays most (an import or exchange), by margin per unit before fuel; the <see cref="TradesPerSystem"/> best goods.
    /// </summary>
    private static IReadOnlyList<TradeSample> BestTrades(IReadOnlyList<MarketSnapshot> markets)
    {
        var listings = markets
            .SelectMany(market => market.TradeGoods.Select(good => (Market: market.WaypointSymbol, Good: good)))
            .ToList();
        var trades = new List<TradeSample>();
        foreach (var good in listings.GroupBy(entry => entry.Good.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            var buys = good
                .Where(entry => entry.Good.PurchasePrice > 0
                    && (entry.Good.Type.Equals(ExportType, StringComparison.OrdinalIgnoreCase) || entry.Good.Type.Equals(ExchangeType, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(entry => entry.Good.PurchasePrice)
                .ThenBy(entry => entry.Market, StringComparer.Ordinal)
                .ToList();
            if (buys.Count == 0)
            {
                continue;
            }

            var buy = buys[0];
            var sells = good
                .Where(entry => entry.Good.SellPrice > buy.Good.PurchasePrice
                    && !entry.Market.Equals(buy.Market, StringComparison.OrdinalIgnoreCase)
                    && (entry.Good.Type.Equals(ImportType, StringComparison.OrdinalIgnoreCase) || entry.Good.Type.Equals(ExchangeType, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(entry => entry.Good.SellPrice)
                .ThenBy(entry => entry.Market, StringComparer.Ordinal)
                .ToList();
            if (sells.Count == 0)
            {
                continue;
            }

            var sell = sells[0];
            trades.Add(new TradeSample
            {
                Good = good.Key,
                BuyAt = buy.Market,
                SellAt = sell.Market,
                Margin = sell.Good.SellPrice - buy.Good.PurchasePrice,
                Volume = Math.Min(buy.Good.TradeVolume, sell.Good.TradeVolume),
            });
        }

        return [.. trades
            .OrderByDescending(trade => trade.Margin)
            .ThenBy(trade => trade.Good, StringComparer.Ordinal)
            .Take(TradesPerSystem)];
    }

    private static string StateOf(string symbol, string home, KnownSystem? system, DateTimeOffset now)
    {
        if (symbol.Equals(home, StringComparison.OrdinalIgnoreCase))
        {
            return "home";
        }

        if (system is null)
        {
            return "cached";
        }

        if (system.ExploredAt is not null)
        {
            return "explored";
        }

        if (system.JumpRefusedAt is { } refused && now - refused < ExploreAtlas.RecheckAfter)
        {
            return "jump_refused";
        }

        return system.Gate switch
        {
            GateState.Active => "to_explore",
            GateState.UnderConstruction => "gate_under_construction",
            GateState.None => "no_gate",
            _ => "gate_unknown",
        };
    }

    private static string GateStateOf(KnownSystem? system) => system?.Gate switch
    {
        GateState.Active => "active",
        GateState.UnderConstruction => "under_construction",
        GateState.None => "none",
        _ => "unknown",
    };

    private static int SupplyRank(string supply) => supply.ToUpperInvariant() switch
    {
        "SCARCE" => 1,
        "LIMITED" => 2,
        "MODERATE" => 3,
        "HIGH" => 4,
        "ABUNDANT" => 5,
        _ => int.MaxValue,
    };

    private static string SupplyName(int rank) => rank switch
    {
        1 => "SCARCE",
        2 => "LIMITED",
        3 => "MODERATE",
        4 => "HIGH",
        5 => "ABUNDANT",
        _ => string.Empty,
    };
}

/// <summary>What one system offers, as the systems dashboard shows it.</summary>
public sealed record SystemSample
{
    /// <summary>The system.</summary>
    public required string System { get; init; }

    /// <summary>
    /// What the explore plan knows of it: <c>home</c>, <c>explored</c>, <c>to_explore</c>, <c>gate_under_construction</c>,
    /// <c>jump_refused</c>, <c>no_gate</c>, <c>gate_unknown</c>, or <c>cached</c> for a system the plan doesn't know.
    /// </summary>
    public required string State { get; init; }

    /// <summary>Its jump gate's waypoint; empty when not known.</summary>
    public required string Gate { get; init; }

    /// <summary>Its gate: <c>active</c>, <c>under_construction</c>, <c>none</c> or <c>unknown</c>.</summary>
    public required string GateState { get; init; }

    /// <summary>How many jumps it is from home; null when no known gate leads there.</summary>
    public int? Jumps { get; init; }

    /// <summary>When the command ship explored it; null while it hasn't.</summary>
    public DateTimeOffset? ExploredAt { get; init; }

    /// <summary>The systems its gate connects to, as far as asked.</summary>
    public IReadOnlyList<string> Connections { get; init; } = [];

    /// <summary>Its waypoints with a market, among those cached.</summary>
    public int Markets { get; init; }

    /// <summary>Its waypoints with a shipyard, among those cached.</summary>
    public int Shipyards { get; init; }

    /// <summary>Its waypoints nobody has charted, whose traits are hidden.</summary>
    public int Uncharted { get; init; }

    /// <summary>Its cached waypoints, counted by type.</summary>
    public IReadOnlyDictionary<string, int> WaypointTypes { get; init; } = new Dictionary<string, int>();

    /// <summary>For each good that can be mined or siphoned there, at how many waypoints.</summary>
    public IReadOnlyDictionary<string, int> GatheringSites { get; init; } = new Dictionary<string, int>();

    /// <summary>The raw goods its markets buy.</summary>
    public IReadOnlyList<RawGoodSample> RawGoods { get; init; } = [];

    /// <summary>Its best trades.</summary>
    public IReadOnlyList<TradeSample> Trades { get; init; } = [];
}

/// <summary>A raw good a system's markets buy: the best price one pays, where, and the lowest supply among them.</summary>
public sealed record RawGoodSample
{
    /// <summary>The ore or gas.</summary>
    public required string Good { get; init; }

    /// <summary>The best price a market pays for a unit.</summary>
    public required int Price { get; init; }

    /// <summary>The market that pays it.</summary>
    public required string Market { get; init; }

    /// <summary>The lowest supply among the markets that buy it; empty when unknown.</summary>
    public required string Supply { get; init; }
}

/// <summary>The best trade of a good within a system, before fuel.</summary>
public sealed record TradeSample
{
    /// <summary>The good.</summary>
    public required string Good { get; init; }

    /// <summary>The market where it is cheapest.</summary>
    public required string BuyAt { get; init; }

    /// <summary>The market that pays most for it.</summary>
    public required string SellAt { get; init; }

    /// <summary>What a unit earns, before fuel.</summary>
    public required int Margin { get; init; }

    /// <summary>The smaller of the two markets' trade volumes: what one trade moves at once.</summary>
    public required int Volume { get; init; }
}
