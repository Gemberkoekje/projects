using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Trading;

/// <summary>
/// Dead reckoning for a market nobody has looked at lately (PLAN.md slice 6.46, D131): what its goods trade at now, from what
/// it showed at its last look and before. A trade moves a price (a purchase of a trade volume raises the next quote a few
/// percent, a sale lowers it), and the market drifts back: the price returns towards where the good usually stands at that
/// market, its resting price, the gap shrinking by e^(−t/τ) with the time t since the last look. The supply goes back to the
/// level the good usually has there once the price has mostly come back; the trade volume stays as last seen.
/// </summary>
/// <remarks>
/// The resting price is the median of the good's samples at the market within <see cref="HistoryWindow"/> of its last look,
/// that look included; with only the last look, it is that look, and the prediction stays as seen. Other players trade too, so
/// the prediction can be wrong: a ship re-reads the real prices when it arrives and drops a trip that no longer pays.
/// </remarks>
public static class MarketDeadReckoning
{
    /// <summary>How far before its last look a market's samples count towards a good's resting level.</summary>
    public static readonly TimeSpan HistoryWindow = TimeSpan.FromHours(24);

    /// <summary>How fast a price drifts back to its resting price at a market that exports the good.</summary>
    public static readonly TimeSpan ExportTimeConstant = TimeSpan.FromHours(2);

    /// <summary>How fast a price drifts back to its resting price at a market that imports the good.</summary>
    public static readonly TimeSpan ImportTimeConstant = TimeSpan.FromHours(2);

    /// <summary>How fast a price drifts back to its resting price at a market that exchanges the good.</summary>
    public static readonly TimeSpan ExchangeTimeConstant = TimeSpan.FromHours(2);

    /// <summary>After how many time constants the supply counts as back at its usual level.</summary>
    public const double SupplyBackAfterTimeConstants = 1.0;

    private const string ExportType = "EXPORT";
    private const string ImportType = "IMPORT";

    /// <summary>A market as predicted for now: each good as <see cref="Predict(TradeGoodSnapshot, IReadOnlyList{MarketPriceSampleDto}, TimeSpan)"/> has it.</summary>
    /// <param name="market">The market as last seen.</param>
    /// <param name="history">Its price samples up to its last look, every good's, in any order.</param>
    /// <param name="sinceLastLook">The time since its last look.</param>
    /// <returns>The market as predicted.</returns>
    public static MarketSnapshot Predict(MarketSnapshot market, IReadOnlyList<MarketPriceSampleDto> history, TimeSpan sinceLastLook)
    {
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(history);

        var byGood = history
            .GroupBy(sample => sample.GoodSymbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<MarketPriceSampleDto>)[.. group], StringComparer.OrdinalIgnoreCase);
        return market with
        {
            TradeGoods = [.. market.TradeGoods.Select(good => Predict(good, byGood.GetValueOrDefault(good.Symbol, []), sinceLastLook))],
        };
    }

    /// <summary>A good as predicted for now, from its last look and its samples at the market before.</summary>
    /// <param name="lastSeen">The good as last seen.</param>
    /// <param name="history">Its samples at the market up to its last look.</param>
    /// <param name="sinceLastLook">The time since the last look.</param>
    /// <returns>The good as predicted.</returns>
    public static TradeGoodSnapshot Predict(TradeGoodSnapshot lastSeen, IReadOnlyList<MarketPriceSampleDto> history, TimeSpan sinceLastLook)
    {
        ArgumentNullException.ThrowIfNull(lastSeen);
        ArgumentNullException.ThrowIfNull(history);

        if (history.Count == 0 || sinceLastLook <= TimeSpan.Zero)
        {
            return lastSeen;
        }

        var tau = TimeConstantOf(lastSeen.Type);
        var left = Math.Exp(-sinceLastLook.TotalSeconds / tau.TotalSeconds);
        var supply = sinceLastLook.TotalSeconds >= SupplyBackAfterTimeConstants * tau.TotalSeconds
            ? UsualSupply(history, lastSeen.Supply)
            : lastSeen.Supply;
        return lastSeen with
        {
            PurchasePrice = Relax(lastSeen.PurchasePrice, Median(history.Select(sample => sample.PurchasePrice)), left),
            SellPrice = Relax(lastSeen.SellPrice, Median(history.Select(sample => sample.SellPrice)), left),
            Supply = supply,
        };
    }

    /// <summary>The time constant of a good's price at a market that trades it so (<see cref="ExportTimeConstant"/> and the others).</summary>
    /// <param name="type">EXPORT, IMPORT or EXCHANGE.</param>
    /// <returns>The time constant.</returns>
    public static TimeSpan TimeConstantOf(string type)
        => type.ToUpperInvariant() switch
        {
            ExportType => ExportTimeConstant,
            ImportType => ImportTimeConstant,
            _ => ExchangeTimeConstant,
        };

    /// <summary>A price drifted back towards its resting price, on a log scale: <paramref name="left"/> of the gap remains.</summary>
    private static int Relax(int lastSeen, double resting, double left)
    {
        if (lastSeen <= 0 || resting <= 0)
        {
            return lastSeen;
        }

        var log = Math.Log(resting) + ((Math.Log(lastSeen) - Math.Log(resting)) * left);
        return (int)Math.Round(Math.Exp(log), MidpointRounding.AwayFromZero);
    }

    /// <summary>The median of the positive values; 0 for none.</summary>
    private static double Median(IEnumerable<int> values)
    {
        var sorted = values.Where(value => value > 0).Order().ToList();
        if (sorted.Count == 0)
        {
            return 0;
        }

        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }

    /// <summary>The supply level the good showed most often in its samples; the last seen on a tie, or without any.</summary>
    private static string UsualSupply(IReadOnlyList<MarketPriceSampleDto> history, string lastSeen)
    {
        var counts = history
            .Where(sample => !string.IsNullOrWhiteSpace(sample.Supply))
            .GroupBy(sample => sample.Supply!, StringComparer.OrdinalIgnoreCase)
            .Select(group => (Supply: group.Key, Count: group.Count()))
            .ToList();
        if (counts.Count == 0)
        {
            return lastSeen;
        }

        var most = counts.Max(entry => entry.Count);
        var usual = counts.Where(entry => entry.Count == most).Select(entry => entry.Supply).ToList();
        return usual.Contains(lastSeen, StringComparer.OrdinalIgnoreCase) ? lastSeen : usual.Order(StringComparer.Ordinal).First();
    }
}
