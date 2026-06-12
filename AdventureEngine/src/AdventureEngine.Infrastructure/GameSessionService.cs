namespace AdventureEngine.Infrastructure;

internal sealed class GameSessionService : IGameSessionService
{
    private const int MaxRecentHistory = 6;

    private readonly IDocumentSession _session;
    private readonly IDirectorAgent _director;
    private readonly INarratorAgent _narrator;
    private readonly INpcAgent _npcAgent;
    private readonly ILogger<GameSessionService> _logger;

    public GameSessionService(
        IDocumentSession session,
        IDirectorAgent director,
        INarratorAgent narrator,
        INpcAgent npcAgent,
        ILogger<GameSessionService> logger)
    {
        _session = session;
        _director = director;
        _narrator = narrator;
        _npcAgent = npcAgent;
        _logger = logger;
    }

    public async Task<Guid> CreateSessionAsync(string playerId, string playerPrompt, CancellationToken ct = default)
    {
        var world = await _director.GenerateWorldAsync(playerPrompt, ct);
        var sessionId = Guid.NewGuid();

        var created = new SessionCreated(sessionId, playerId, playerPrompt, world, DateTime.UtcNow);
        _session.Events.StartStream<GameSession>(sessionId, created);
        await _session.SaveChangesAsync(ct);

        _logger.LogInformation("Created session {SessionId} for player {PlayerId}", sessionId, playerId);
        return sessionId;
    }

    public async Task<GameSession?> GetSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        return await _session.Events.AggregateStreamAsync<GameSession>(sessionId, token: ct);
    }

    public async Task<IReadOnlyList<GameSession>> GetPlayerSessionsAsync(string playerId, CancellationToken ct = default)
    {
        return await _session.Query<GameSession>()
            .Where(s => s.PlayerId == playerId)
            .ToListAsync(ct);
    }

    public async Task<string> SubmitActionAsync(Guid sessionId, string playerInput, CancellationToken ct = default)
    {
        var gameSession = await _session.Events.AggregateStreamAsync<GameSession>(sessionId, token: ct)
            ?? throw new InvalidOperationException($"Session {sessionId} not found.");

        if (gameSession.WorldManifest is null)
            throw new InvalidOperationException($"Session {sessionId} has no world manifest.");

        var ctx = new NarratorContext
        {
            WorldManifest = gameSession.WorldManifest,
            CurrentChapterIndex = gameSession.CurrentChapterIndex,
            CurrentSceneId = gameSession.CurrentSceneId,
            RecentHistory = gameSession.RecentHistory,
            ChapterSummaries = gameSession.ChapterSummaries,
            PlayerInput = playerInput,
        };

        var responseBuilder = new System.Text.StringBuilder();
        await foreach (var token in _narrator.StreamResponseAsync(ctx, ct))
        {
            responseBuilder.Append(token);
        }

        var narratorResponse = responseBuilder.ToString();

        var events = new List<object>
        {
            new PlayerActed(sessionId, playerInput, narratorResponse, DateTime.UtcNow),
        };

        // Trigger NPC dialogue for active NPCs in the current scene
        var chapter = gameSession.WorldManifest.Chapters
            .FirstOrDefault(c => c.Index == gameSession.CurrentChapterIndex);
        var scene = chapter?.Scenes.FirstOrDefault(s => s.Id == gameSession.CurrentSceneId);

        if (scene is not null && scene.ActiveNpcIds.Count > 0)
        {
            var npcTasks = gameSession.WorldManifest.Npcs
                .Where(n => scene.ActiveNpcIds.Contains(n.Id))
                .Select(async npc =>
                {
                    var npcCtx = new NpcContext
                    {
                        Npc = npc,
                        SceneDescription = scene.Description,
                        RecentNarratorText = narratorResponse,
                        PlayerInput = playerInput,
                    };
                    var dialogue = await _npcAgent.RespondAsync(npcCtx, ct);
                    return new NpcSpoke(sessionId, npc.Id, dialogue, DateTime.UtcNow);
                });

            var npcEvents = await Task.WhenAll(npcTasks);
            events.AddRange(npcEvents);
        }

        _session.Events.Append(sessionId, events.ToArray());
        await _session.SaveChangesAsync(ct);

        return narratorResponse;
    }
}
