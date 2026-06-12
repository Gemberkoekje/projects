namespace AdventureEngine.Application.Agents;

/// <summary>
/// Context passed to an NPC agent for a character interaction.
/// </summary>
public sealed class NpcContext
{
    public required NpcDefinition Npc { get; init; }
    public required string SceneDescription { get; init; }
    public required string RecentNarratorText { get; init; }
    public required string PlayerInput { get; init; }
}
