namespace AdventureEngine.Application.Agents;

public sealed record NarratorUsage(
    int InputTokens,
    int OutputTokens,
    int CacheReadInputTokens,
    int CacheCreationInputTokens);
