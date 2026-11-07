using CharacterCreator.core.Skills;

namespace CharacterCreator.core.Plans;

/// <summary>
/// A plan as it is saved: in the browser's storage, and as an exported file (DESIGN.md §4.3). It
/// records the ruleset it was made on and a key and number of purchases per skill.
/// </summary>
/// <remarks>
/// Immutable and thread-safe. Every member has a default and none is <c>required</c>: a file comes
/// from outside, and one written by an older version may lack what a newer one adds. The setters
/// guard the defaults: compile-time JSON metadata (<see cref="Rulesets.RulesetJsonContext"/>) sets
/// a missing init-only member to <c>null</c> or 0 instead of leaving its initializer.
/// </remarks>
public sealed record PlanFile
{
    /// <summary>What <see cref="Format"/> holds in a Character Creator file.</summary>
    public const string FormatName = "vortex-character-creator";

    private readonly string format = FormatName;
    private readonly int version = 1;
    private readonly string id = string.Empty;
    private readonly string name = string.Empty;
    private readonly PlanMode mode = PlanMode.NewCharacter;
    private readonly string ruleset = string.Empty;
    private readonly IReadOnlyList<PlanSkill> skills = [];

    /// <summary>The file's kind, always <see cref="FormatName"/>; tells it apart from other JSON.</summary>
    public string Format { get => format; init => format = value ?? FormatName; }

    /// <summary>The format's version.</summary>
    public int Version { get => version; init => version = value > 0 ? value : 1; }

    /// <summary>The plan's id in the browser's list of saved plans; empty until it is saved there.</summary>
    public string Id { get => id; init => id = value ?? string.Empty; }

    /// <summary>The character's name; may be empty.</summary>
    public string Name { get => name; init => name = value ?? string.Empty; }

    /// <summary>Which rules apply; a new character when the file does not say.</summary>
    public PlanMode Mode { get => mode; init => mode = value == PlanMode.None ? PlanMode.NewCharacter : value; }

    /// <summary>The XP total for free planning; ignored for a new character.</summary>
    public decimal Xp { get; init; }

    /// <summary>The published ruleset it was made on; empty for an old builder's save.</summary>
    public string Ruleset { get => ruleset; init => ruleset = value ?? string.Empty; }

    /// <summary>When it was saved.</summary>
    public DateTimeOffset SavedAt { get; init; }

    /// <summary>The planned purchases, in the order they were added.</summary>
    public IReadOnlyList<PlanSkill> Skills { get => skills; init => skills = value ?? []; }

    /// <summary>The purchases as holdings.</summary>
    /// <returns>The holdings.</returns>
    public Holdings ToHoldings() => Holdings.Of(Skills.Select(skill => new KeyValuePair<SkillKey, int>(skill.Key, skill.Times)));

    /// <summary>A file for a plan.</summary>
    /// <param name="name">The character's name.</param>
    /// <param name="plan">The plan.</param>
    /// <param name="ruleset">The published ruleset's id.</param>
    /// <param name="savedAt">The moment.</param>
    /// <returns>The file.</returns>
    public static PlanFile From(string name, Plan plan, string ruleset, DateTimeOffset savedAt)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new ()
        {
            Name = name ?? string.Empty,
            Mode = plan.Mode,
            Xp = plan.Xp,
            Ruleset = ruleset ?? string.Empty,
            SavedAt = savedAt,
            Skills = [.. plan.Planned.Select(pair => new PlanSkill { Key = pair.Key, Times = pair.Value })],
        };
    }

    /// <summary>The plan the file holds.</summary>
    /// <returns>
    /// The plan. An existing character's file is planned freely against its XP: the skills VOID holds
    /// for it are not in the file, and come from VOID when it is opened from <c>Mijn personages</c>.
    /// </returns>
    public Plan ToPlan() => Mode == PlanMode.NewCharacter ? Plan.NewCharacter(ToHoldings()) : Plan.Free(Math.Max(Xp, 0m), ToHoldings());
}

/// <summary>A skill in a saved plan, with how often it is bought.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record PlanSkill
{
    /// <summary>The skill.</summary>
    required public SkillKey Key { get; init; }

    /// <summary>How often it is bought.</summary>
    required public int Times { get; init; }
}
