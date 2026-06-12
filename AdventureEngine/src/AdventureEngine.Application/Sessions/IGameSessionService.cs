namespace AdventureEngine.Application.Sessions;

/// <summary>
/// Service for managing game sessions.
/// </summary>
public interface IGameSessionService
{
    Task<Guid> CreateSessionAsync(string playerId, string playerPrompt, CancellationToken ct = default);
    Task<GameSession?> GetSessionAsync(Guid sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<GameSession>> GetPlayerSessionsAsync(string playerId, CancellationToken ct = default);
    Task<string> SubmitActionAsync(Guid sessionId, string playerInput, CancellationToken ct = default);
}
