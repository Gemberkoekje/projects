namespace AdventureEngine.Application.Agents;

/// <summary>
/// Generates a complete WorldManifest from a player premise (one-shot call).
/// </summary>
public interface IDirectorAgent
{
    Task<(WorldManifest World, AgentUsage Usage)> GenerateWorldAsync(string playerPrompt, CancellationToken ct = default);
}
