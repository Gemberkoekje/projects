namespace AdventureEngine.Domain.Events;

public sealed record SessionCreated(
    Guid SessionId,
    string PlayerId,
    string PlayerPrompt,
    WorldManifest WorldManifest,
    DateTime OccurredAt);

public sealed record ChapterStarted(
    Guid SessionId,
    int ChapterIndex,
    string Summary,
    DateTime OccurredAt);

public sealed record SceneEntered(
    Guid SessionId,
    string SceneId,
    string NarratorContext,
    DateTime OccurredAt);

public sealed record PlayerActed(
    Guid SessionId,
    string Input,
    string NarratorResponse,
    DateTime OccurredAt);

public sealed record NpcSpoke(
    Guid SessionId,
    string NpcId,
    string Dialogue,
    DateTime OccurredAt);

public sealed record ChapterCompleted(
    Guid SessionId,
    int ChapterIndex,
    string OutcomeSummary,
    DateTime OccurredAt);

public sealed record GameEnded(
    Guid SessionId,
    bool Won,
    string Outcome,
    DateTime OccurredAt);

public sealed record UsageRecorded(
    Guid SessionId,
    string Agent,
    int InputTokens,
    int OutputTokens,
    DateTime OccurredAt,
    int CacheReadInputTokens = 0,
    int CacheCreationInputTokens = 0);
