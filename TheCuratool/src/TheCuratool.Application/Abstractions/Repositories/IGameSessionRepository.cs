namespace TheCuratool.Application.Abstractions.Repositories;

/// <summary>Persistence contract for <see cref="GameSession"/> entities.</summary>
public interface IGameSessionRepository
{
    /// <summary>Persists a new <see cref="GameSession"/> and associates it with the given script.</summary>
    Task<GameSession> AddAsync(GameSession session, Guid scriptId, CancellationToken cancellationToken = default);

    /// <summary>Returns the <see cref="GameSession"/> with the specified <paramref name="id"/>.</summary>
    Task<GameSession> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Persists changes to an existing <see cref="GameSession"/>.</summary>
    Task<GameSession> UpdateAsync(GameSession session, CancellationToken cancellationToken = default);

    /// <summary>Flushes pending changes to the underlying store.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
