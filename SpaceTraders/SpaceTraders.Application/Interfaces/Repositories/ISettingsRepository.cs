namespace SpaceTraders.Application.Interfaces.Repositories;

public interface ISettingsRepository
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task<string?> GetRawAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(string Key, string Value, string Type, string Description)>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns key-value pairs for all settings whose key starts with <paramref name="prefix"/>.</summary>
    Task<IReadOnlyList<(string Key, string Value)>> GetByKeyPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    Task ResetToDefaultsAsync(CancellationToken cancellationToken = default);
}
