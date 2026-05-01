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
}
