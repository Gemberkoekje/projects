using CharacterCreator.core.Rules;

namespace CharacterCreator.application.Void;

/// <summary>VOID's skills at one moment, as <c>GET /skills</c> gives them (DESIGN.md §3.1).</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record VoidSnapshot
{
    /// <summary>Where it came from: the VOID address, or the offline snapshot's description.</summary>
    required public string Source { get; init; }

    /// <summary>When it was taken.</summary>
    required public DateTimeOffset TakenAt { get; init; }

    /// <summary>Whether it is the synthesized offline snapshot rather than a VOID's answer.</summary>
    required public bool Synthesized { get; init; }

    /// <summary>Every skill VOID has, including the SL-only and deprecated ones.</summary>
    required public IReadOnlyList<VoidSkill> Skills { get; init; }

    /// <summary>
    /// The rules for a new character: the XP it starts with and how much may be left over. VOID
    /// gives them beside its skills (void <c>feature/skills-requirements-api</c>); a VOID without
    /// them leaves the Character Creator's own (<see cref="CreationRules.Vortex"/>).
    /// </summary>
    public CreationRules Creation { get; init; } = CreationRules.Vortex;

    /// <summary>Whether <see cref="Creation"/> came from VOID rather than the Character Creator's own numbers.</summary>
    public bool CreationFromVoid { get; init; }
}

/// <summary>One skill as VOID knows it.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record VoidSkill
{
    /// <summary>VOID's id; differs between VOID instances.</summary>
    required public int Id { get; init; }

    /// <summary>VOID's name, such as <c>healer A (elemental)</c>.</summary>
    required public string Name { get; init; }

    /// <summary>XP per purchase.</summary>
    required public int Cost { get; init; }

    /// <summary>Purchases allowed at creation; 0 for an extra skill.</summary>
    required public int BaseMax { get; init; }

    /// <summary>Purchases allowed in total.</summary>
    required public int TimesMax { get; init; }

    /// <summary>Whether VOID has retired it.</summary>
    required public bool Deprecated { get; init; }

    /// <summary>The energy type it gives per day; empty for none.</summary>
    required public string Manatype { get; init; }

    /// <summary>How much energy per purchase.</summary>
    required public int ManaAmount { get; init; }

    /// <summary>The imbue type its cap is for; empty for none.</summary>
    required public string ImbueType { get; init; }

    /// <summary>The imbue cap per purchase.</summary>
    required public int ImbueCap { get; init; }

    /// <summary>Whether it has a loresheet.</summary>
    required public bool Loresheet { get; init; }

    /// <summary>Its prerequisite groups (DESIGN.md §3.2).</summary>
    required public IReadOnlyList<VoidRequirement> Requirements { get; init; }
}

/// <summary>One prerequisite group as VOID stores it: kind, amount, and skills by VOID id.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record VoidRequirement
{
    /// <summary><c>any</c>, <c>points</c> or <c>extra</c>.</summary>
    required public string Kind { get; init; }

    /// <summary>Times, XP or extra skills.</summary>
    required public int Amount { get; init; }

    /// <summary>The skills, by VOID id; none for <c>extra</c>.</summary>
    required public IReadOnlyList<int> Skills { get; init; }
}
