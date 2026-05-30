namespace TheCuratool.Application.Abstractions.Repositories;

/// <summary>Persistence contract for uploaded scripts.</summary>
public interface IScriptRepository
{
    /// <summary>Persists a new script and returns the stored record with its assigned ID.</summary>
    Task<StoredScript> AddAsync(string name, string author, string rawJson, CancellationToken cancellationToken = default);

    /// <summary>Returns the <see cref="StoredScript"/> with the specified <paramref name="id"/>.</summary>
    Task<StoredScript> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns all scripts, optionally including custom persisted scripts.</summary>
    Task<IReadOnlyList<StoredScript>> GetAllAsync(bool includeCustomScripts = true, CancellationToken cancellationToken = default);

    /// <summary>Flushes pending changes to the underlying store.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
