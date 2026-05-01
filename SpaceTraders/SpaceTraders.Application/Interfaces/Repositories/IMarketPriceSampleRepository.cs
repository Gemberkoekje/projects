namespace SpaceTraders.Application.Interfaces.Repositories;

public interface IMarketPriceSampleRepository
{
    Task AppendSamplesAsync(string waypointSymbol, string tradeGoodsJson, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prunes old market price samples.
    /// Rows between <paramref name="aggregateRetentionCutoff"/> and <paramref name="rawRetentionCutoff"/>
    /// are downsampled to one row per (waypoint, good, hour) (the earliest in each bucket is kept).
    /// Rows older than <paramref name="aggregateRetentionCutoff"/> are deleted entirely.
    /// </summary>
    /// <returns>Total number of rows deleted.</returns>
    Task<int> PruneAsync(DateTimeOffset rawRetentionCutoff, DateTimeOffset aggregateRetentionCutoff, CancellationToken cancellationToken = default);
}
