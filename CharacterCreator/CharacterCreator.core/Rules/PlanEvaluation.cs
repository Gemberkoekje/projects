using CharacterCreator.core.Plans;
using CharacterCreator.core.Skills;

namespace CharacterCreator.core.Rules;

/// <summary>Energy per day, or an imbue cap, of one type, summed over a character's skills.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record EnergyTotal
{
    /// <summary>Energy or imbue cap.</summary>
    required public EnergyKind Kind { get; init; }

    /// <summary>The type as VOID names it.</summary>
    required public string Type { get; init; }

    /// <summary>The total.</summary>
    required public int Amount { get; init; }
}

/// <summary>What the engine makes of a plan: the XP, the findings and the energy (DESIGN.md §6).</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record PlanEvaluation
{
    /// <summary>The plan evaluated.</summary>
    required public Plan Plan { get; init; }

    /// <summary>The XP total the plan is held to: the rule book's starting XP for a new character.</summary>
    required public decimal Xp { get; init; }

    /// <summary>XP spent on every skill the character would have, held and planned.</summary>
    required public decimal Spent { get; init; }

    /// <summary>
    /// XP left to spend after the planned purchases; negative when they need more than there is. For
    /// an existing character this is whole XP, after any teaching took its share first.
    /// </summary>
    required public decimal Remaining { get; init; }

    /// <summary>Everything found: problems first, then warnings, then the rest, each in the order found.</summary>
    required public IReadOnlyList<Finding> Findings { get; init; }

    /// <summary>Energy per day and imbue caps, summed over every skill the character would have.</summary>
    required public IReadOnlyList<EnergyTotal> Energy { get; init; }

    /// <summary>Whether anything breaks a rule VOID enforces. A push into VOID needs this to be false.</summary>
    public bool HasProblems => Findings.Any(finding => finding.Severity == FindingSeverity.Problem);

    /// <summary>The findings about one skill.</summary>
    /// <param name="skill">The skill.</param>
    /// <returns>Its findings.</returns>
    public IEnumerable<Finding> For(SkillKey skill) => Findings.Where(finding => finding.Skill == skill);
}

/// <summary>
/// What the picker shows for one skill: whether one more purchase would break a rule, and why
/// (DESIGN.md §5, "states in words").
/// </summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record SkillOption
{
    /// <summary>The skill.</summary>
    required public SkillKey Skill { get; init; }

    /// <summary>Whether the picker lists it at all: not for a skill the book does not know, nor a deprecated one.</summary>
    required public bool Offered { get; init; }

    /// <summary>Everything one more purchase would bring about; empty for a skill that can simply be bought.</summary>
    required public IReadOnlyList<Finding> Reasons { get; init; }

    /// <summary>Whether one more purchase breaks no rule VOID enforces. It may still need a teacher.</summary>
    public bool CanBuy => Offered && !Reasons.Any(reason => reason.Severity == FindingSeverity.Problem);
}

/// <summary>
/// "Add with prerequisites": the purchases that meet a skill's single-skill prerequisites, deepest
/// first, ending with the skill itself, and the groups that need a choice (DESIGN.md §3.2).
/// </summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record PrerequisiteChain
{
    /// <summary>What to add, in order.</summary>
    required public Holdings Purchases { get; init; }

    /// <summary>Groups the chain cannot decide: a choice of several skills, XP in a set, extra skills.</summary>
    required public IReadOnlyList<RequirementGroup> Unresolved { get; init; }

    /// <summary>Whether adding <see cref="Purchases"/> meets every prerequisite.</summary>
    public bool IsComplete => Unresolved.Count == 0;
}
