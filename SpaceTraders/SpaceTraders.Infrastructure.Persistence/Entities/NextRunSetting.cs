namespace SpaceTraders.Infrastructure.Persistence.Entities;

/// <summary>
/// A value chosen for a setting for the runs to come: the agent the next server reset registers starts with it instead
/// of the setting's default (D69). It belongs to no agent, so the agent cleanup leaves it.
/// </summary>
public sealed class NextRunSetting
{
    required public string Key { get; init; }

    required public string Value { get; init; }
}
