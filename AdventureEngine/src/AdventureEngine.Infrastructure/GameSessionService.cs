namespace AdventureEngine.Infrastructure;

using System.Net.Http;
using System.Text.RegularExpressions;
using AdventureEngine.Infrastructure.Agents;

internal sealed partial class GameSessionService : IGameSessionService
{
    private const int MaxRecentHistory = 6;

    private readonly IDocumentSession _session;
    private readonly IDirectorAgent _director;
    private readonly INarratorAgent _narrator;
    private readonly INpcAgent _npcAgent;
    private readonly ModerationAgent _moderator;
    private readonly ILogger<GameSessionService> _logger;

    [GeneratedRegex(@"<!--\s*SCENE:([a-zA-Z0-9_]+)\s*-->")]
    private static partial Regex SceneMarkerRegex();

    public GameSessionService(
        IDocumentSession session,
        IDirectorAgent director,
        INarratorAgent narrator,
        INpcAgent npcAgent,
        ModerationAgent moderator,
        ILogger<GameSessionService> logger)
    {
        _session = session;
        _director = director;
        _narrator = narrator;
        _npcAgent = npcAgent;
        _moderator = moderator;
        _logger = logger;
    }

    public async Task<Guid> CreateSessionAsync(string playerId, string playerPrompt, CancellationToken ct = default)
    {
        if (!await _moderator.IsSafeAsync(playerPrompt, ct))
            throw new InvalidOperationException(
                "Your premise was flagged by content moderation. Premises containing graphic sexual content, " +
                "instructions for real-world violence, hate speech, or harmful content involving minors are not permitted. " +
                "Please try a different premise.");

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

        if (gameSession.Status == SessionStatus.Completed || gameSession.Status == SessionStatus.Abandoned)
            throw new InvalidOperationException("This adventure has already ended.");

        if (gameSession.WorldManifest is null)
            throw new InvalidOperationException($"Session {sessionId} has no world manifest.");

        var world = gameSession.WorldManifest;

        var ctx = new NarratorContext
        {
            WorldManifest = world,
            CurrentChapterIndex = gameSession.CurrentChapterIndex,
            CurrentSceneId = gameSession.CurrentSceneId,
            RecentHistory = gameSession.RecentHistory,
            ChapterSummaries = gameSession.ChapterSummaries,
            PlayerInput = playerInput,
        };

        NarratorUsage? narratorUsage = null;
        var rawResponse = await CallWithRetryAsync(
            ct2 => CollectStreamAsync(
                _narrator.StreamResponseAsync(ctx, usage => narratorUsage = usage, ct2),
                ct2),
            ct);

        var (cleanResponse, nextSceneId, outcomeWon) = ParseStateMarkers(rawResponse);

        var events = new List<object>
        {
            new PlayerActed(sessionId, playerInput, cleanResponse, DateTime.UtcNow),
        };
        if (narratorUsage is not null)
        {
            events.Add(new UsageRecorded(
                sessionId,
                "narrator",
                narratorUsage.InputTokens,
                narratorUsage.OutputTokens,
                DateTime.UtcNow,
                narratorUsage.CacheReadInputTokens,
                narratorUsage.CacheCreationInputTokens));
        }

        // NPC dialogue for active NPCs in the current scene
        var chapter = world.Chapters.FirstOrDefault(c => c.Index == gameSession.CurrentChapterIndex);
        var scene = chapter?.Scenes.FirstOrDefault(s => s.Id == gameSession.CurrentSceneId);

        if (scene is not null && scene.ActiveNpcIds.Count > 0)
        {
            var npcTasks = world.Npcs
                .Where(n => scene.ActiveNpcIds.Contains(n.Id))
                .Select(async npc =>
                {
                    var npcCtx = new NpcContext
                    {
                        Npc = npc,
                        SceneDescription = scene.Description,
                        RecentNarratorText = cleanResponse,
                        PlayerInput = playerInput,
                    };
                    var dialogue = await CallWithRetryAsync(ct2 => _npcAgent.RespondAsync(npcCtx, ct2), ct);
                    return new NpcSpoke(sessionId, npc.Id, dialogue, DateTime.UtcNow);
                });

            var npcEvents = await Task.WhenAll(npcTasks);
            events.AddRange(npcEvents);
        }

        // Chapter boundary detection and scene transitions
        if (nextSceneId is not null)
        {
            var targetChapter = world.Chapters
                .FirstOrDefault(c => c.Scenes.Any(s => s.Id == nextSceneId));

            if (targetChapter is not null && targetChapter.Index != gameSession.CurrentChapterIndex)
            {
                // Chapter transition — generate a summary of the completed chapter
                var chapterHistory = gameSession.CurrentChapterHistory
                    .Append((playerInput, cleanResponse))
                    .ToList();

                var summary = await CallWithRetryAsync(
                    ct2 => _narrator.GenerateChapterSummaryAsync(world, gameSession.CurrentChapterIndex, chapterHistory, ct2),
                    ct);

                _logger.LogInformation(
                    "Chapter {Chapter} completed for session {SessionId}", gameSession.CurrentChapterIndex, sessionId);

                events.Add(new ChapterCompleted(sessionId, gameSession.CurrentChapterIndex, summary, DateTime.UtcNow));
            }

            events.Add(new SceneEntered(sessionId, nextSceneId, string.Empty, DateTime.UtcNow));
        }

        // Win/lose condition
        if (outcomeWon.HasValue)
        {
            events.Add(new GameEnded(sessionId, outcomeWon.Value,
                outcomeWon.Value ? "You have won the adventure!" : "Your adventure has come to an end.",
                DateTime.UtcNow));

            _logger.LogInformation(
                "Game ended (won={Won}) for session {SessionId}", outcomeWon.Value, sessionId);
        }

        _session.Events.Append(sessionId, events.ToArray());
        await _session.SaveChangesAsync(ct);

        return cleanResponse;
    }

    private static (string response, string? nextSceneId, bool? outcomeWon) ParseStateMarkers(string raw)
    {
        string? nextSceneId = null;
        bool? outcomeWon = null;

        var cleaned = raw;

        var sceneMatch = SceneMarkerRegex().Match(cleaned);
        if (sceneMatch.Success)
        {
            nextSceneId = sceneMatch.Groups[1].Value;
            cleaned = cleaned.Replace(sceneMatch.Value, string.Empty);
        }

        if (cleaned.Contains("<!-- OUTCOME:WON -->", StringComparison.Ordinal))
        {
            outcomeWon = true;
            cleaned = cleaned.Replace("<!-- OUTCOME:WON -->", string.Empty);
        }
        else if (cleaned.Contains("<!-- OUTCOME:LOST -->", StringComparison.Ordinal))
        {
            outcomeWon = false;
            cleaned = cleaned.Replace("<!-- OUTCOME:LOST -->", string.Empty);
        }

        return (cleaned.Trim(), nextSceneId, outcomeWon);
    }

    private static async Task<string> CollectStreamAsync(IAsyncEnumerable<string> stream, CancellationToken ct)
    {
        var sb = new StringBuilder();
        await foreach (var token in stream.WithCancellation(ct))
            sb.Append(token);
        return sb.ToString();
    }

    private static async Task<T> CallWithRetryAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct, int maxAttempts = 3)
    {
        const int BackoffBaseSeconds = 2;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await action(ct);
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransientError(ex))
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt * BackoffBaseSeconds), ct);
            }
        }
        // Final attempt — let it throw
        return await action(ct);
    }

    private static bool IsTransientError(Exception ex) =>
        ex is HttpRequestException or TimeoutException or TaskCanceledException { InnerException: TimeoutException };
}
