namespace AdventureEngine.Application.Agents;

/// <summary>
/// Returns in-character dialogue for a single NPC in a scene.
/// </summary>
public interface INpcAgent
{
    Task<(string Dialogue, AgentUsage Usage)> RespondAsync(NpcContext ctx, CancellationToken ct = default);
}
