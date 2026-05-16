namespace SpaceTraders.Application.Interfaces.Repositories;

public interface IAgentCreditsSampleRepository
{
    Task AppendAsync(long credits, CancellationToken cancellationToken = default);

    /// <summary>Returns credits samples within the given UTC time range, ordered by <c>ObservedAt</c> ascending.</summary>
    Task<IReadOnlyList<CreditsSampleDto>> GetRangeAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Prunes old credits samples.
    /// Rows between <paramref name="aggregateRetentionCutoff"/> and <paramref name="rawRetentionCutoff"/>
    /// are downsampled to one row per hour (the earliest in each hour is kept).
    /// Rows older than <paramref name="aggregateRetentionCutoff"/> are deleted entirely.
    /// </summary>
    /// <returns>Total number of rows deleted.</returns>
    Task<int> PruneAsync(DateTimeOffset rawRetentionCutoff, DateTimeOffset aggregateRetentionCutoff, CancellationToken cancellationToken = default);
}

public sealed record CreditsSampleDto(DateTimeOffset ObservedAt, long Credits);
