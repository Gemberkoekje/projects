namespace AdventureEngine.Application.Agents;

public sealed record AgentUsage(
    int InputTokens,
    int OutputTokens,
    int CacheReadInputTokens,
    int CacheCreationInputTokens);
