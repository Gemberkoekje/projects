using System.Collections.Concurrent;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Trading;

/// <summary>
/// Predicts what the markets of the home system nobody has looked at lately trade at now (PLAN.md slice 6.46, D131), so a trade
/// route can still buy or sell there and a ship eventually goes to look. Asked on 2026-11-08: "Can we make a dead reckoning
/// prediction rule for any markets without active ships or probes so that our command ship goes there to trade eventually?
/// Even if it isn't fully accurate (e.g. due to other players) at least it will solve the problem. But only do this for the home
/// system." In the run of 2026-11-01, X1-UK3-K88 and -E49 went 18 hours unseen, their routes off the board (D96), until a ship
/// happened by and found trips worth 50,000 to 100,000 credits.
/// </summary>
public interface IMarketPredictions
{
    /// <summary>
    /// Each market as it is predicted to trade now (<see cref="MarketDeadReckoning"/>), from what it showed at its last look and
    /// what it showed before (the price samples of the run).
    /// </summary>
    /// <param name="markets">The markets, each with its last look.</param>
    /// <param name="now">The time to predict for.</param>
    /// <param name="cancellationToken">Stops the reads.</param>
    /// <returns>The predicted markets, in the order given.</returns>
    Task<IReadOnlyList<MarketSnapshot>> PredictAsync(
        IReadOnlyList<(MarketSnapshot Market, DateTimeOffset LastObservedAt)> markets,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

/// <inheritdoc />
/// <remarks>
/// A market's samples before its last look don't change until it is looked at again, so they are read once per look
/// (<see cref="MarketHistoryCache"/>), not on every pass.
/// </remarks>
public sealed class MarketPredictions(IMarketPriceSampleRepository samples, MarketHistoryCache cache) : IMarketPredictions
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<MarketSnapshot>> PredictAsync(
        IReadOnlyList<(MarketSnapshot Market, DateTimeOffset LastObservedAt)> markets,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var predicted = new List<MarketSnapshot>(markets.Count);
        foreach (var (market, lastObservedAt) in markets)
        {
            var history = await cache.GetOrReadAsync(
                market.WaypointSymbol,
                lastObservedAt,
                token => samples.GetWaypointPricesAsync(market.WaypointSymbol, lastObservedAt - MarketDeadReckoning.HistoryWindow, lastObservedAt, token),
                cancellationToken);
            predicted.Add(MarketDeadReckoning.Predict(market, history, now - lastObservedAt));
        }

        return predicted;
    }
}

/// <summary>
/// The price samples of each market up to its last look (D131), kept until it is looked at again: a market that is predicted
/// stays unseen, so the same samples serve every pass meanwhile. One entry per market.
/// </summary>
/// <remarks>Thread-safe; registered once for the process.</remarks>
public sealed class MarketHistoryCache
{
    private readonly ConcurrentDictionary<string, (DateTimeOffset LastObservedAt, IReadOnlyList<MarketPriceSampleDto> Samples)> _entries
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The market's samples up to its last look, read with <paramref name="read"/> once per look.</summary>
    /// <param name="waypointSymbol">The market.</param>
    /// <param name="lastObservedAt">Its last look.</param>
    /// <param name="read">Reads the samples.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The samples, the oldest first.</returns>
    public async Task<IReadOnlyList<MarketPriceSampleDto>> GetOrReadAsync(
        string waypointSymbol,
        DateTimeOffset lastObservedAt,
        Func<CancellationToken, Task<IReadOnlyList<MarketPriceSampleDto>>> read,
        CancellationToken cancellationToken)
    {
        if (_entries.TryGetValue(waypointSymbol, out var entry) && entry.LastObservedAt == lastObservedAt)
        {
            return entry.Samples;
        }

        var samples = await read(cancellationToken);
        _entries[waypointSymbol] = (lastObservedAt, samples);
        return samples;
    }
}
