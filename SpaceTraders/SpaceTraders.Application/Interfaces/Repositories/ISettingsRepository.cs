namespace SpaceTraders.Application.Interfaces.Repositories;

public interface ISettingsRepository
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task<string?> GetRawAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(string Key, string Value, string Type, string Description)>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns key-value pairs for all settings whose key starts with <paramref name="prefix"/>.</summary>
    Task<IReadOnlyList<(string Key, string Value)>> GetByKeyPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives every setting its default back, to follow it from then on, and forgets the values chosen for the next
    /// runs (D69).
    /// </summary>
    Task ResetToDefaultsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Every setting a run starts with, by key, with the value the next run starts with: the one chosen for it, else
    /// the default (<c>IsDefault</c>). The next run is the agent the next server reset registers (D69).
    /// </summary>
    Task<IReadOnlyList<(string Key, string Value, string Type, string Description, bool IsDefault)>> GetNextRunSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Chooses the value the next runs start with, and leaves the setting of the run that runs now alone (D69).
    /// </summary>
    /// <returns>False, with nothing stored, for a key that isn't a setting a run starts with: one the seed doesn't
    /// hold, or a <c>Runtime.*</c> status flag.</returns>
    Task<bool> SetNextRunSettingAsync(string key, string value, CancellationToken cancellationToken = default);

    /// <summary>Forgets the value chosen for the next runs: they start with the default again (D69).</summary>
    /// <returns>False for a key that isn't a setting a run starts with.</returns>
    Task<bool> RemoveNextRunSettingAsync(string key, CancellationToken cancellationToken = default);
}
