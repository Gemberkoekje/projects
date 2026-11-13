namespace SpaceTraders.Application.Trading;

/// <summary>
/// The high-value trades of the known universe once the home system's jump gate is built (PLAN.md slice 6.63, D148, asked on
/// 2026-11-13: "Have a pre-set (asynchronously generated) list of high value trades. [...] A ship can then take a close high
/// value trade to do."). Built in the background from the latest markets (<see cref="TradeListBuilder"/>), its figures
/// estimates; a ship's routes are then worked out exactly, as before, but only for the trades on the list
/// (<see cref="TradeMarketMap.TradeList"/>), so a ship looks at a few thousand trades instead of every good between every
/// pair of markets.
/// </summary>
/// <remarks>Immutable once built: plans on both loops read the same list at once.</remarks>
public sealed class TradeList
{
    private static readonly IReadOnlyList<string> NoMarkets = [];

    private readonly Dictionary<string, Dictionary<string, IReadOnlyList<string>>> _sellsByBuyAndGood;
    private readonly HashSet<string> _systems;

    /// <summary>Makes a list.</summary>
    /// <param name="builtAt">When its markets were read.</param>
    /// <param name="entries">Its trades, best first.</param>
    /// <param name="systems">The systems whose markets it was built from (<see cref="Covers"/>).</param>
    public TradeList(DateTimeOffset builtAt, IReadOnlyList<TradeListEntry> entries, IEnumerable<string> systems)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(systems);

        BuiltAt = builtAt;
        Entries = entries;
        _systems = new HashSet<string>(systems, StringComparer.OrdinalIgnoreCase);
        _sellsByBuyAndGood = new Dictionary<string, Dictionary<string, IReadOnlyList<string>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var byBuy in entries.GroupBy(entry => entry.BuyWaypointSymbol, StringComparer.OrdinalIgnoreCase))
        {
            _sellsByBuyAndGood[byBuy.Key] = byBuy
                .GroupBy(entry => entry.TradeSymbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    byGood => byGood.Key,
                    byGood => (IReadOnlyList<string>)[.. byGood.Select(entry => entry.SellWaypointSymbol)],
                    StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>No list: every route is worked out, as before the gate (D148).</summary>
    public static TradeList None { get; } = new(DateTimeOffset.MinValue, [], []);

    /// <summary>When the markets it was built from were read.</summary>
    public DateTimeOffset BuiltAt { get; }

    /// <summary>Its trades, best first.</summary>
    public IReadOnlyList<TradeListEntry> Entries { get; }

    /// <summary>Whether this is <see cref="None"/>.</summary>
    public bool IsNone => ReferenceEquals(this, None);

    /// <summary>The buy markets of its trades.</summary>
    public IReadOnlyCollection<string> BuyMarkets => _sellsByBuyAndGood.Keys;

    /// <summary>
    /// Whether the list was built from the system's markets: within the trading plan's reach of home. A ship in a system the
    /// gates don't join to home has its routes worked out over every trade there, as before the list (measured on 2026-11-13:
    /// one trader, in a system with two markets, would have had none).
    /// </summary>
    /// <param name="systemSymbol">The ship's system.</param>
    /// <returns>True when it covers the system.</returns>
    public bool Covers(string systemSymbol) => _systems.Contains(systemSymbol);

    /// <summary>Whether the list holds a trade of the good at the buy market.</summary>
    /// <param name="buyWaypointSymbol">The buy market.</param>
    /// <param name="tradeSymbol">The good.</param>
    /// <returns>True when it does.</returns>
    public bool HasGoodAt(string buyWaypointSymbol, string tradeSymbol)
        => _sellsByBuyAndGood.TryGetValue(buyWaypointSymbol, out var goods) && goods.ContainsKey(tradeSymbol);

    /// <summary>The sell markets the list holds for the good bought at the buy market.</summary>
    /// <param name="buyWaypointSymbol">The buy market.</param>
    /// <param name="tradeSymbol">The good.</param>
    /// <returns>The sell markets; none when the list holds no such trade.</returns>
    public IReadOnlyList<string> SellsFor(string buyWaypointSymbol, string tradeSymbol)
        => _sellsByBuyAndGood.TryGetValue(buyWaypointSymbol, out var goods) && goods.TryGetValue(tradeSymbol, out var sells)
            ? sells
            : NoMarkets;
}

/// <summary>
/// A trade on the <see cref="TradeList"/>, as estimated when the list was built (D148: "It's fine if they don't give exactly the
/// amount they're set up for, they'll still be high value trades").
/// </summary>
public sealed record TradeListEntry
{
    /// <summary>Creates a trade.</summary>
    /// <param name="TradeSymbol">The good.</param>
    /// <param name="BuyWaypointSymbol">Where it is bought.</param>
    /// <param name="SellWaypointSymbol">Where it is sold.</param>
    /// <param name="Units">The units it is set up for: a hold of the fleet's largest, as far as each earns the minimum.</param>
    /// <param name="Profit">What those units should earn after the haul's fuel and antimatter, from the buy market on.</param>
    /// <param name="Seconds">The haul's time from the buy market on, at the fleet's usual engine speed.</param>
    /// <param name="Jumps">The haul's jumps.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public TradeListEntry(string TradeSymbol, string BuyWaypointSymbol, string SellWaypointSymbol, int Units, long Profit, double Seconds, int Jumps)
    {
        this.TradeSymbol = TradeSymbol;
        this.BuyWaypointSymbol = BuyWaypointSymbol;
        this.SellWaypointSymbol = SellWaypointSymbol;
        this.Units = Units;
        this.Profit = Profit;
        this.Seconds = Seconds;
        this.Jumps = Jumps;
    }

    /// <summary>The good.</summary>
    public required string TradeSymbol { get; init; }

    /// <summary>Where it is bought.</summary>
    public required string BuyWaypointSymbol { get; init; }

    /// <summary>Where it is sold.</summary>
    public required string SellWaypointSymbol { get; init; }

    /// <summary>The units it is set up for: a hold of the fleet's largest, as far as each earns the minimum.</summary>
    public required int Units { get; init; }

    /// <summary>What those units should earn after the haul's fuel and antimatter, from the buy market on.</summary>
    public required long Profit { get; init; }

    /// <summary>The haul's time from the buy market on, at the fleet's usual engine speed.</summary>
    public required double Seconds { get; init; }

    /// <summary>The haul's jumps.</summary>
    public required int Jumps { get; init; }

    /// <summary>What the haul should earn an hour.</summary>
    public double CreditsPerHour => TripTime.PerHour(Profit, Seconds);

    /// <summary>
    /// How much the prices of its two markets count (D131–D133): below 1 for a predicted market, the less the more other
    /// players trade there ("Trades should still devaluate faster if there's a higher chance another trader might've picked it
    /// up in front of us", D148).
    /// </summary>
    public double PredictionWeight { get; init; } = 1.0;

    /// <summary>Whether its sale feeds production: not an end product, nor sold to an exchange (D91).</summary>
    public bool FeedsProduction { get; init; } = true;

    /// <summary>
    /// What the list ranks it by, as a ship's routes are ranked (<see cref="TradeRoutePlanner.RankingRate"/>): what the haul
    /// earns an hour, halved for a sale that feeds no production, times its prediction weight.
    /// </summary>
    public double Value => CreditsPerHour * (FeedsProduction ? 1 : 0.5) * PredictionWeight;
}
