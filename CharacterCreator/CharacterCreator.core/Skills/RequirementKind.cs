namespace CharacterCreator.core.Skills;

/// <summary>
/// What a prerequisite group counts, as VOID's <c>skill_requirements.kind</c> says (DESIGN.md §3.2).
/// </summary>
public enum RequirementKind
{
    /// <summary>No known kind. Like VOID, a group of an unknown kind always holds.</summary>
    None = 0,

    /// <summary>The character has one of the group's skills at least <c>amount</c> times.</summary>
    Any,

    /// <summary>The character spent at least <c>amount</c> XP on the group's skills.</summary>
    Points,

    /// <summary>The character has at least <c>amount</c> extra skills (skills with <c>base_max</c> 0).</summary>
    Extra,
}

/// <summary>Reads VOID's spelling of a <see cref="RequirementKind"/>.</summary>
public static class RequirementKinds
{
    /// <summary>The kind VOID names <paramref name="kind"/>: <c>any</c>, <c>points</c> or <c>extra</c>.</summary>
    /// <param name="kind">VOID's text; may be empty.</param>
    /// <returns>The kind, or <see cref="RequirementKind.None"/> for anything else.</returns>
    public static RequirementKind FromVoid(string kind) => kind?.Trim().ToUpperInvariant() switch
    {
        "ANY" => RequirementKind.Any,
        "POINTS" => RequirementKind.Points,
        "EXTRA" => RequirementKind.Extra,
        _ => RequirementKind.None,
    };
}
