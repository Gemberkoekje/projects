namespace CharacterCreator.core.Skills;

/// <summary>
/// One group of a skill's prerequisites, as VOID stores it in <c>skill_requirements</c>. Every group
/// of a skill must hold (DESIGN.md §3.2).
/// </summary>
/// <remarks>Immutable and thread-safe. Two groups are equal when kind, amount and skills (in order) are.</remarks>
public sealed record RequirementGroup
{
    /// <summary>Initializes a new instance of the <see cref="RequirementGroup"/> class.</summary>
    /// <param name="kind">What the group counts.</param>
    /// <param name="amount">Times (<see cref="RequirementKind.Any"/>), XP (<see cref="RequirementKind.Points"/>) or extra skills (<see cref="RequirementKind.Extra"/>).</param>
    /// <param name="skills">The skills it counts; none for <see cref="RequirementKind.Extra"/>.</param>
    public RequirementGroup(RequirementKind kind, int amount, IEnumerable<SkillKey> skills)
    {
        Kind = kind;
        Amount = amount;
        Skills = [.. skills ?? []];
    }

    /// <summary>What the group counts.</summary>
    public RequirementKind Kind { get; }

    /// <summary>Times, XP or extra skills, depending on <see cref="Kind"/>.</summary>
    public int Amount { get; }

    /// <summary>The skills the group counts; empty for <see cref="RequirementKind.Extra"/>.</summary>
    public IReadOnlyList<SkillKey> Skills { get; }

    /// <summary>One of <paramref name="skills"/>, at least <paramref name="amount"/> times.</summary>
    /// <param name="amount">How many times one of them must be held.</param>
    /// <param name="skills">The skills.</param>
    /// <returns>The group.</returns>
    public static RequirementGroup Any(int amount, params SkillKey[] skills) => new (RequirementKind.Any, amount, skills);

    /// <summary>At least <paramref name="amount"/> XP spent on <paramref name="skills"/>.</summary>
    /// <param name="amount">The XP.</param>
    /// <param name="skills">The skills.</param>
    /// <returns>The group.</returns>
    public static RequirementGroup Points(int amount, params SkillKey[] skills) => new (RequirementKind.Points, amount, skills);

    /// <summary>At least <paramref name="amount"/> extra skills.</summary>
    /// <param name="amount">The number of extra skills.</param>
    /// <returns>The group.</returns>
    public static RequirementGroup Extra(int amount) => new (RequirementKind.Extra, amount, []);

    /// <inheritdoc />
    public bool Equals(RequirementGroup other)
        => other is not null
            && Kind == other.Kind
            && Amount == other.Amount
            && Skills.SequenceEqual(other.Skills);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, Amount, Skills.Count);
}
