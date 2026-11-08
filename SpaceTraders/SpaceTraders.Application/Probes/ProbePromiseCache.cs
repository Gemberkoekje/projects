using System.Collections.Concurrent;

namespace SpaceTraders.Application.Probes;

/// <summary>
/// What each system's predicted markets promise a probe (PLAN.md slice 6.47, D134), reckoned at most once a <see cref="Lifetime"/>
/// per system: weighing every route through every market costs more than a probe pass should, and the predictions drift slowly.
/// </summary>
/// <remarks>Thread-safe; registered once for the process.</remarks>
public sealed class ProbePromiseCache
{
    /// <summary>How long promises reckoned once serve.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, (DateTimeOffset At, IReadOnlyDictionary<string, double> Promises)> _systems
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The system's promises, reckoned with <paramref name="reckon"/> when the last are older than <see cref="Lifetime"/>.</summary>
    /// <param name="systemSymbol">The system.</param>
    /// <param name="now">The time.</param>
    /// <param name="reckon">Reckons them.</param>
    /// <param name="cancellationToken">Stops the reckoning.</param>
    /// <returns>Each predicted market's promise.</returns>
    public async Task<IReadOnlyDictionary<string, double>> GetOrReadAsync(
        string systemSymbol,
        DateTimeOffset now,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, double>>> reckon,
        CancellationToken cancellationToken)
    {
        if (_systems.TryGetValue(systemSymbol, out var last) && now - last.At < Lifetime && now >= last.At)
        {
            return last.Promises;
        }

        var promises = await reckon(cancellationToken);
        _systems[systemSymbol] = (now, promises);
        return promises;
    }
}

/// <summary>
/// What a probe at a market would make sure of, in credits an hour (PLAN.md slices 6.47 and 6.48, D134, D135): the best route
/// through it at its predicted prices times how far they aren't trusted, plus a share of what the other agents trade there an
/// hour. Asked on 2026-11-08: "Probes should also prioritize markets with a lot of players. As the accuracy of predictions is
/// low, but the actual profits could be high." Chosen: "Add their trade flow to the promise".
/// </summary>
public static class ProbePromise
{
    /// <summary>The promise of one market.</summary>
    /// <param name="bestRate">The most a route through it earns an hour at the prices the map holds (<see cref="Trading.TradeRoutePlanner.BestRateThrough"/>); counts only while its prices are predicted.</param>
    /// <param name="weight">How far its prices are trusted (<see cref="Trading.TradeMarketMap.PredictionWeightOf"/>): 1 when seen lately.</param>
    /// <param name="othersCreditsPerHour">What the other agents traded there an hour over the last day, units times price.</param>
    /// <param name="flowShare">The share of that which counts (<c>Prediction.ProbeFlowSharePercent</c>).</param>
    /// <returns>The promise; never below 0.</returns>
    public static double Of(double bestRate, double weight, double othersCreditsPerHour, double flowShare)
        => (Math.Max(0, bestRate) * Math.Clamp(1 - weight, 0, 1)) + (Math.Max(0, othersCreditsPerHour) * Math.Max(0, flowShare));
}
