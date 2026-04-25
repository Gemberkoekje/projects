using SpaceTraders.Application.DTOs;

namespace SpaceTraders.Application.Interfaces.Repositories;

public interface IActivityLogRepository
{
    Task AppendAsync(string shipSymbol, string eventType, string message, string? jsonDetails = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ActivityLogDto>> GetPagedAsync(int page = 1, int pageSize = 50, string? shipFilter = null, CancellationToken cancellationToken = default);
}
