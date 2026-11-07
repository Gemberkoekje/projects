using CharacterCreator.core.Skills;

namespace CharacterCreator.core.Rules;

/// <summary>
/// The rules a plan is checked against: every skill as VOID knows it, the creation rules and the XP
/// table. A published ruleset wraps one, next to the content (DESIGN.md §4).
/// </summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed class RuleBook
{
    private readonly Dictionary<SkillKey, RuleSkill> byKey;

    private RuleBook(IReadOnlyList<RuleSkill> skills, CreationRules creation, XpTable xp)
    {
        Skills = skills;
        byKey = skills.ToDictionary(skill => skill.Key);
        Creation = creation;
        Xp = xp;
    }

    /// <summary>Every skill, in the order given.</summary>
    public IReadOnlyList<RuleSkill> Skills { get; }

    /// <summary>The rules for a new character.</summary>
    public CreationRules Creation { get; }

    /// <summary>The XP each event awards.</summary>
    public XpTable Xp { get; }

    /// <summary>Builds a rule book, refusing data that cannot be right.</summary>
    /// <param name="skills">The skills.</param>
    /// <param name="creation">The creation rules; Vortex's when not given.</param>
    /// <param name="xp">The XP table; Vortex's when not given.</param>
    /// <returns>The rule book, or the reasons it was refused: an empty or duplicate key, or a negative cost or maximum.</returns>
    /// <remarks>
    /// A prerequisite may name a skill that is not in the book: VOID ignores a deleted skill the same
    /// way, and such a group can then only be met by its other skills.
    /// </remarks>
    public static Result<RuleBook> Create(IEnumerable<RuleSkill> skills, CreationRules creation = null, XpTable xp = null)
    {
        ArgumentNullException.ThrowIfNull(skills);

        var list = skills.ToList();
        var messages = new List<IValidationMessage>();

        foreach (var skill in list.Where(skill => skill.Key.IsEmpty))
        {
            messages.Add(ValidationMessage.Error($"A skill costing {skill.Cost} XP has no key."));
        }

        foreach (var key in list.Where(skill => !skill.Key.IsEmpty).GroupBy(skill => skill.Key).Where(group => group.Count() > 1).Select(group => group.Key))
        {
            messages.Add(ValidationMessage.Error($"The key '{key}' is used by more than one skill."));
        }

        foreach (var skill in list.Where(skill => skill.Cost < 0 || skill.BaseMax < 0 || skill.TimesMax < 0))
        {
            messages.Add(ValidationMessage.Error($"'{skill.Key}' has a negative cost or maximum."));
        }

        return messages.Count > 0
            ? Result.WithMessages<RuleBook>(messages)
            : Result.For(new RuleBook(list, creation ?? CreationRules.Vortex, xp ?? XpTable.Vortex));
    }

    /// <summary>Looks up a skill.</summary>
    /// <param name="key">The skill's key.</param>
    /// <param name="skill">The skill, when found.</param>
    /// <returns>Whether the book has it.</returns>
    public bool TryGet(SkillKey key, out RuleSkill skill) => byKey.TryGetValue(key, out skill);

    /// <summary>What one purchase of a skill costs; 0 for a skill not in the book, as in VOID.</summary>
    /// <param name="key">The skill's key.</param>
    /// <returns>The cost.</returns>
    public int CostOf(SkillKey key) => TryGet(key, out var skill) ? skill.Cost : 0;
}
