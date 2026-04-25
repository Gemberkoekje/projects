namespace SpaceTraders.Application.Interfaces.Repositories;

public interface IActivityLogRepository
{
    Task AppendAsync(string shipSymbol, string eventType, string message, string? jsonDetails = null, CancellationToken cancellationToken = default);
}
