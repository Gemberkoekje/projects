namespace SpaceTraders.Application.Interfaces.Repositories;

public interface ISettingsRepository
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);
    Task<string?> GetRawAsync(string key, CancellationToken cancellationToken = default);
    Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(string Key, string Value, string Type, string Description)>> GetAllAsync(CancellationToken cancellationToken = default);
    Task ResetToDefaultsAsync(CancellationToken cancellationToken = default);
}
