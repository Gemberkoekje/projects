using SpaceTraders.Domain.Enums;

namespace SpaceTraders.Application.Interfaces.Repositories;

public interface ILedgerRepository
{
    Task AppendAsync(
        string shipSymbol,
        LedgerCategory category,
        long amount,
        string? goodSymbol = null,
        int? unitPrice = null,
        int? units = null,
        string? waypointSymbol = null,
        string? sourceEventId = null,
        Guid? runId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns ledger entries filtered by the given parameters, ordered by <c>OccurredAt</c> descending.
    /// All filter parameters are optional.
    /// </summary>
    Task<IReadOnlyList<LedgerEntryDto>> GetRangeAsync(
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? shipSymbol = null,
        LedgerCategory? category = null,
        Guid? runId = null,
        int limit = 500,
        CancellationToken cancellationToken = default);

    /// <summary>Returns total income and expense grouped by <see cref="LedgerCategory"/> for the given run (or all entries if <paramref name="runId"/> is null).</summary>
    Task<IReadOnlyList<LedgerSummaryDto>> GetSummaryAsync(Guid? runId = null, CancellationToken cancellationToken = default);

    /// <summary>Deletes all ledger entries with <c>OccurredAt</c> older than <paramref name="olderThan"/>.</summary>
    /// <returns>Number of rows deleted.</returns>
    Task<int> PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);
}

public sealed record LedgerEntryDto(
    long Id,
    DateTimeOffset OccurredAt,
    string ShipSymbol,
    Guid? RunId,
    string Category,
    long Amount,
    string? GoodSymbol,
    int? UnitPrice,
    int? Units,
    string? WaypointSymbol);

public sealed record LedgerSummaryDto(string Category, long TotalAmount, int EntryCount);
