namespace SpaceTraders.Application.Interfaces.Repositories;

public interface IAgentCreditsSampleRepository
{
    Task AppendAsync(long credits, CancellationToken cancellationToken = default);

    /// <summary>Returns credits samples within the given UTC time range, ordered by <c>ObservedAt</c> ascending.</summary>
    Task<IReadOnlyList<CreditsSampleDto>> GetRangeAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public sealed record CreditsSampleDto(DateTimeOffset ObservedAt, long Credits);
