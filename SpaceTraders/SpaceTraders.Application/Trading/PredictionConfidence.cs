using SpaceTraders.Application.Exploring;
using SpaceTraders.Application.Health;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Trading;

/// <summary>
/// How far a market's predicted prices are trusted (PLAN.md slice 6.47, D133): the weight a route on them ranks at
/// (<see cref="TradeRoutePlanner.RankingRate"/>). Asked on 2026-11-08: "The more other players are trading there, the lower the
/// prediction accuracy should be. This should count for the entire system, not just the market itself. In fact, I'd like it to
/// ripple out. So the market itself should get the biggest accuracy hit, the system should get a smaller accuracy hit, and the
/// systems 1 jump out should get an even smaller accuracy hit." Chosen: "1 : ½ : ¼".
/// </summary>
/// <remarks>
/// The other agents' transactions the markets listed in the last <see cref="Window"/> count (<see cref="IMarketTradeRepository"/>):
/// the market's own fully, the rest of its system's at <c>Prediction.SystemSharePercent</c>, those of the systems one jump away
/// at <c>Prediction.NeighbourSharePercent</c>. With none the weight is <c>Prediction.MaxWeightPercent</c>; every
/// <c>Prediction.HalvingTrades</c> of them halve it, down to <c>Prediction.MinWeightPercent</c>. A market seen lately with prices
/// isn't predicted, and counts fully (weight 1).
/// </remarks>
public interface IPredictionConfidence
{
    /// <summary>The weight of each predicted market's prices.</summary>
    /// <param name="markets">The predicted markets, each with its system.</param>
    /// <param name="network">The systems and gates the explore plan knows, for the systems one jump away; null for none.</param>
    /// <param name="now">The time.</param>
    /// <param name="cancellationToken">Stops the reads.</param>
    /// <returns>A weight, above 0 and below 1, per market.</returns>
    Task<IReadOnlyDictionary<string, double>> WeightsAsync(
        IReadOnlyDictionary<string, string> markets,
        ExplorePlanState? network,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class PredictionConfidence(IMarketTradeRepository trades, ISettingsRepository settings, MarketTradeCountCache cache) : IPredictionConfidence
{
    /// <summary>The weight of predicted prices where nobody else trades, in percent.</summary>
    public const string MaxWeightSetting = "Prediction.MaxWeightPercent";

    /// <summary>The lowest weight of predicted prices, in percent.</summary>
    public const string MinWeightSetting = "Prediction.MinWeightPercent";

    /// <summary>The other agents' transactions a day, weighed by where they were, that halve the weight.</summary>
    public const string HalvingTradesSetting = "Prediction.HalvingTrades";

    /// <summary>What the other markets of the system count for, in percent of the market's own.</summary>
    public const string SystemShareSetting = "Prediction.SystemSharePercent";

    /// <summary>What the markets of the systems one jump away count for, in percent of the market's own.</summary>
    public const string NeighbourShareSetting = "Prediction.NeighbourSharePercent";

    /// <summary>How far back the other agents' transactions count.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, double>> WeightsAsync(
        IReadOnlyDictionary<string, string> markets,
        ExplorePlanState? network,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (markets.Count == 0)
        {
            return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        var shape = new Ripple(
            await settings.ThresholdAsync(MaxWeightSetting, 90, cancellationToken) / 100.0,
            await settings.ThresholdAsync(MinWeightSetting, 10, cancellationToken) / 100.0,
            await settings.ThresholdAsync(HalvingTradesSetting, 10, cancellationToken),
            await settings.ThresholdAsync(SystemShareSetting, 50, cancellationToken) / 100.0,
            await settings.ThresholdAsync(NeighbourShareSetting, 25, cancellationToken) / 100.0);
        var counts = await cache.GetOrReadAsync(now, token => trades.CountSinceAsync(now - Window, token), cancellationToken);
        var byMarket = counts
            .GroupBy(count => count.WaypointSymbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(count => count.Trades), StringComparer.OrdinalIgnoreCase);
        var bySystem = counts
            .GroupBy(count => count.SystemSymbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(count => count.Trades), StringComparer.OrdinalIgnoreCase);
        var neighbours = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int NeighbourTrades(string system)
        {
            if (!neighbours.TryGetValue(system, out var sum))
            {
                sum = network is null
                    ? 0
                    : ExploreAtlas.Reachable(network, system, now)
                        .Where(entry => entry.Value == 1)
                        .Sum(entry => bySystem.GetValueOrDefault(entry.Key));
                neighbours[system] = sum;
            }

            return sum;
        }

        return markets.ToDictionary(
            market => market.Key,
            market =>
            {
                var own = byMarket.GetValueOrDefault(market.Key);
                return Weight(own, bySystem.GetValueOrDefault(market.Value) - own, NeighbourTrades(market.Value), shape);
            },
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A predicted market's weight: <see cref="Ripple.Max"/> halved for every <see cref="Ripple.HalvingTrades"/> of the other
    /// agents' transactions, the market's own counted fully, the rest of its system's and the systems one jump away at their
    /// shares; never below <see cref="Ripple.Min"/>.
    /// </summary>
    /// <param name="market">The other agents' transactions at the market.</param>
    /// <param name="restOfSystem">Those at the other markets of its system.</param>
    /// <param name="neighbours">Those in the systems one jump away.</param>
    /// <param name="shape">The settings.</param>
    /// <returns>The weight.</returns>
    public static double Weight(int market, int restOfSystem, int neighbours, Ripple shape)
    {
        ArgumentNullException.ThrowIfNull(shape);

        var weighted = market + (shape.SystemShare * restOfSystem) + (shape.NeighbourShare * neighbours);
        var weight = shape.Max * Math.Pow(0.5, weighted / Math.Max(1, shape.HalvingTrades));
        return Math.Clamp(weight, Math.Min(shape.Min, shape.Max), shape.Max);
    }
}

/// <summary>The settings of <see cref="PredictionConfidence.Weight"/> (D133).</summary>
public sealed record Ripple
{
    /// <summary>Creates the settings.</summary>
    /// <param name="Max">The weight with no other agent trading.</param>
    /// <param name="Min">The lowest weight.</param>
    /// <param name="HalvingTrades">The weighed transactions that halve the weight.</param>
    /// <param name="SystemShare">What the rest of the system counts for.</param>
    /// <param name="NeighbourShare">What the systems one jump away count for.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public Ripple(double Max, double Min, int HalvingTrades, double SystemShare, double NeighbourShare)
    {
        this.Max = Max;
        this.Min = Min;
        this.HalvingTrades = HalvingTrades;
        this.SystemShare = SystemShare;
        this.NeighbourShare = NeighbourShare;
    }

    /// <summary>The defaults: 90%, 10%, 10 transactions, 1 : ½ : ¼.</summary>
    public static Ripple Default { get; } = new(0.9, 0.1, 10, 0.5, 0.25);

    /// <summary>The weight with no other agent trading.</summary>
    public required double Max { get; init; }

    /// <summary>The lowest weight.</summary>
    public required double Min { get; init; }

    /// <summary>The weighed transactions that halve the weight.</summary>
    public required int HalvingTrades { get; init; }

    /// <summary>What the rest of the system counts for.</summary>
    public required double SystemShare { get; init; }

    /// <summary>What the systems one jump away count for.</summary>
    public required double NeighbourShare { get; init; }
}

/// <summary>
/// The other agents' transaction counts (D133), read at most once a <see cref="Lifetime"/>: every prediction asks for them,
/// several times a tick, and they change by a transaction or two a minute.
/// </summary>
/// <remarks>Thread-safe; registered once for the process.</remarks>
public sealed class MarketTradeCountCache
{
    /// <summary>How long counts read once serve.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    private readonly Lock _gate = new();
    private (DateTimeOffset ReadAt, IReadOnlyList<MarketTradeCount> Counts)? _last;

    /// <summary>The counts, read with <paramref name="read"/> when the last read is older than <see cref="Lifetime"/>.</summary>
    /// <param name="now">The time.</param>
    /// <param name="read">Reads the counts.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The counts.</returns>
    public async Task<IReadOnlyList<MarketTradeCount>> GetOrReadAsync(
        DateTimeOffset now,
        Func<CancellationToken, Task<IReadOnlyList<MarketTradeCount>>> read,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_last is { } last && now - last.ReadAt < Lifetime && now >= last.ReadAt)
            {
                return last.Counts;
            }
        }

        var counts = await read(cancellationToken);
        lock (_gate)
        {
            _last = (now, counts);
        }

        return counts;
    }
}
