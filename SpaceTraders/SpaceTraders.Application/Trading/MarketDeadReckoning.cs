using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Trading;

/// <summary>
/// Dead reckoning for a market nobody has looked at lately (PLAN.md slice 6.46, D131): what its goods trade at now, from what
/// it showed at its last look and at its earlier looks this run. Fitted on 2026-11-08 on the price samples of the runs of
/// 2026-10-11 to 2026-11-01, gaps where we didn't trade the good (a market nobody looks at sees no trade of ours either):
/// <list type="bullet">
///   <item>a price drifts back towards the good's resting price, on a log scale, e^(−t/τ) of the gap left after the time t
///   since the last look. At a market that exports the good the resting price is the lowest it showed this run and τ is
///   16 hours (the best fit was 12 to 24 hours by run); at one that imports it, the highest it showed in the 24 hours up to
///   the last look, and τ is 4 hours (3 to 6). With fewer than <see cref="FewLooks"/> looks the supply level at the last look
///   says how far off the resting price was (<see cref="ExportLadder"/>, <see cref="ImportLadder"/>);</item>
///   <item>the sell price moves with the purchase price: the spread held within 1 to 2%;</item>
///   <item>after <see cref="SupplyAfter"/> the supply is the level whose ladder step is nearest the predicted distance from the
///   resting price, never back past the last look's side: an export's supply only grows while nobody buys, an import's only
///   shrinks while nobody sells;</item>
///   <item>the trade volume stays as seen (unchanged in 86 to 100% of the gaps, even 32 to 100 hours long), and a good the
///   market exchanges stays as seen altogether: nothing predicted it better.</item>
/// </list>
/// On real gaps of 16 to 32 hours the purchase price was off by a mean 11.8% for exports (as seen: 21.9%) and 2.5% for imports
/// (7.6%).
/// </summary>
/// <remarks>
/// The export rule assumes nobody buys while the market is dark, so it errs on the cheap side: other players can keep a price
/// up (in the run of 2026-10-18 exports at SCARCE stayed high for 9 to 30 hours). A ship re-reads the real prices when it
/// arrives and drops a trip that no longer pays.
/// </remarks>
public static class MarketDeadReckoning
{
    /// <summary>How far before its last look a market's samples are read: the whole run, as the raw samples are kept 7 days.</summary>
    public static readonly TimeSpan RunHistory = TimeSpan.FromDays(8);

    /// <summary>How far before its last look an import's looks count towards its resting price.</summary>
    public static readonly TimeSpan ImportRestingWindow = TimeSpan.FromHours(24);

    /// <summary>How fast a price drifts back to its resting price at a market that exports the good.</summary>
    public static readonly TimeSpan ExportTimeConstant = TimeSpan.FromHours(16);

    /// <summary>How fast a price drifts back to its resting price at a market that imports the good.</summary>
    public static readonly TimeSpan ImportTimeConstant = TimeSpan.FromHours(4);

    /// <summary>How long the supply stays as last seen.</summary>
    public static readonly TimeSpan SupplyAfter = TimeSpan.FromHours(4);

    /// <summary>Below this many looks at the good, the supply ladder bounds its resting price.</summary>
    public const int FewLooks = 5;

    private const string ExportType = "EXPORT";
    private const string ImportType = "IMPORT";

    private static readonly string[] SupplyLevels = ["SCARCE", "LIMITED", "MODERATE", "HIGH", "ABUNDANT"];

    /// <summary>
    /// How far above its resting price an export stands, ln(price / resting price), by its supply at the look: SCARCE first. It
    /// held on all four runs (0.63 to 0.69, 0.51 to 0.57, 0.31 to 0.37, 0.07 to 0.14, 0 to 0.04).
    /// </summary>
    public static IReadOnlyList<double> ExportLadder { get; } = [0.63, 0.56, 0.31, 0.07, 0.0];

    /// <summary>How far below its resting price an import stands, ln(resting price / price), by its supply at the look: SCARCE first.</summary>
    public static IReadOnlyList<double> ImportLadder { get; } = [0.01, 0.07, 0.12, 0.37, 0.62];

    /// <summary>A market as predicted for now: each good as <see cref="Predict(TradeGoodSnapshot, IReadOnlyList{MarketPriceSampleDto}, DateTimeOffset, TimeSpan)"/> has it.</summary>
    /// <param name="market">The market as last seen.</param>
    /// <param name="history">Its price samples this run up to its last look, every good's, in any order.</param>
    /// <param name="lastLook">When it was last seen.</param>
    /// <param name="sinceLastLook">The time since then.</param>
    /// <returns>The market as predicted.</returns>
    public static MarketSnapshot Predict(MarketSnapshot market, IReadOnlyList<MarketPriceSampleDto> history, DateTimeOffset lastLook, TimeSpan sinceLastLook)
    {
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(history);

        var byGood = history
            .GroupBy(sample => sample.GoodSymbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<MarketPriceSampleDto>)[.. group], StringComparer.OrdinalIgnoreCase);
        return market with
        {
            TradeGoods = [.. market.TradeGoods.Select(good => Predict(good, byGood.GetValueOrDefault(good.Symbol, []), lastLook, sinceLastLook))],
        };
    }

    /// <summary>A good as predicted for now, from its last look and its earlier looks at the market this run.</summary>
    /// <param name="lastSeen">The good as last seen.</param>
    /// <param name="history">Its samples at the market this run up to its last look, the last look included or not.</param>
    /// <param name="lastLook">When it was last seen.</param>
    /// <param name="sinceLastLook">The time since then.</param>
    /// <returns>The good as predicted.</returns>
    public static TradeGoodSnapshot Predict(TradeGoodSnapshot lastSeen, IReadOnlyList<MarketPriceSampleDto> history, DateTimeOffset lastLook, TimeSpan sinceLastLook)
    {
        ArgumentNullException.ThrowIfNull(lastSeen);
        ArgumentNullException.ThrowIfNull(history);

        var export = lastSeen.Type.Equals(ExportType, StringComparison.OrdinalIgnoreCase);
        var import = lastSeen.Type.Equals(ImportType, StringComparison.OrdinalIgnoreCase);
        var level = Array.FindIndex(SupplyLevels, supply => supply.Equals(lastSeen.Supply, StringComparison.OrdinalIgnoreCase));
        if ((!export && !import) || sinceLastLook <= TimeSpan.Zero || lastSeen.PurchasePrice <= 0 || level < 0)
        {
            return lastSeen;
        }

        var looks = history.Where(sample => sample.PurchasePrice > 0 && sample.ObservedAt <= lastLook).ToList();
        var lookCount = looks.Count(sample => sample.ObservedAt < lastLook) + 1;
        var lastPrice = Math.Log(lastSeen.PurchasePrice);
        double resting;
        if (export)
        {
            resting = Math.Min(lastPrice, looks.Select(sample => Math.Log(sample.PurchasePrice)).DefaultIfEmpty(lastPrice).Min());
            if (lookCount < FewLooks)
            {
                resting = Math.Min(resting, lastPrice - ExportLadder[level]);
            }
        }
        else
        {
            resting = Math.Max(
                lastPrice,
                looks.Where(sample => sample.ObservedAt >= lastLook - ImportRestingWindow).Select(sample => Math.Log(sample.PurchasePrice)).DefaultIfEmpty(lastPrice).Max());
            if (lookCount < FewLooks)
            {
                resting = Math.Max(resting, lastPrice + ImportLadder[level]);
            }
        }

        var tau = export ? ExportTimeConstant : ImportTimeConstant;
        var predicted = resting + ((lastPrice - resting) * Math.Exp(-sinceLastLook.TotalSeconds / tau.TotalSeconds));
        var purchasePrice = (int)Math.Round(Math.Exp(predicted), MidpointRounding.AwayFromZero);
        var sellPrice = (int)Math.Round(lastSeen.SellPrice * (double)purchasePrice / lastSeen.PurchasePrice, MidpointRounding.AwayFromZero);
        return lastSeen with
        {
            PurchasePrice = purchasePrice,
            SellPrice = sellPrice,
            Supply = sinceLastLook < SupplyAfter ? lastSeen.Supply : SupplyLevels[PredictedLevel(export, level, Math.Abs(predicted - resting))],
        };
    }

    /// <summary>
    /// The supply level whose ladder step is nearest the distance from the resting price, the lowest on a tie; never below the
    /// last look's for an export, never above it for an import.
    /// </summary>
    private static int PredictedLevel(bool export, int lastLevel, double distance)
    {
        var ladder = export ? ExportLadder : ImportLadder;
        var nearest = 0;
        for (var candidate = 1; candidate < ladder.Count; candidate++)
        {
            if (Math.Abs(ladder[candidate] - distance) < Math.Abs(ladder[nearest] - distance))
            {
                nearest = candidate;
            }
        }

        return export ? Math.Max(nearest, lastLevel) : Math.Min(nearest, lastLevel);
    }
}
