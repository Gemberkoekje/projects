namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class AgentSetting
{
    public string AgentId { get; init; } = string.Empty;

    required public string Key { get; init; }

    required public string Value { get; init; }

    required public string Type { get; init; }

    required public string Description { get; init; }

    /// <summary>
    /// True while the setting holds the default it was seeded with and nobody has set it since: every start gives it
    /// the default as the running version has it, so a changed default reaches the agent that runs (D69). Seeded from
    /// a value chosen for the next runs, or set through the settings repository (by you or by the bot), it keeps its
    /// value.
    /// </summary>
    public bool FollowsDefault { get; init; }
}
