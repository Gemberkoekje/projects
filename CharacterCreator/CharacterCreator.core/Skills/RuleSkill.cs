using System.Text.Json.Serialization;

namespace CharacterCreator.core.Skills;

/// <summary>
/// A skill as the rules know it: what VOID says it costs, how often it may be bought, what it
/// requires and what it grants (DESIGN.md §3.1). Names and text live in the content, not here.
/// </summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record RuleSkill
{
    /// <summary>The content's key for the skill.</summary>
    required public SkillKey Key { get; init; }

    /// <summary>XP per purchase.</summary>
    required public int Cost { get; init; }

    /// <summary>How often the skill may be bought at character creation; 0 makes it an extra skill.</summary>
    required public int BaseMax { get; init; }

    /// <summary>How often the skill may be bought in total.</summary>
    required public int TimesMax { get; init; }

    /// <summary>Whether VOID has retired the skill. A deprecated skill is never offered.</summary>
    public bool Deprecated { get; init; }

    /// <summary>The prerequisite groups; every one must hold.</summary>
    public IReadOnlyList<RequirementGroup> Requirements { get; init; } = [];

    /// <summary>What each purchase adds to energy per day and imbue caps.</summary>
    public IReadOnlyList<EnergyGrant> Energy { get; init; } = [];

    /// <summary>
    /// Whether this is an extra skill: not to be bought at creation, and bought the first time
    /// through a teacher. VOID says so with <c>base_max</c> 0.
    /// </summary>
    [JsonIgnore]
    public bool IsExtra => BaseMax == 0;
}
