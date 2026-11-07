using CharacterCreator.core.Plans;
using CharacterCreator.core.Skills;
using Qowaiv;

namespace CharacterCreator.core.Rules;

/// <summary>
/// The skill rules that need nothing but data, mirroring VOID's <c>App\Checkout\SkillRules</c>: whole
/// XP, XP spent, and which prerequisite groups hold. The shared cases from VOID
/// (<c>tests/data/skill-rules-cases.json</c>) keep the two engines equal (DESIGN.md §3.5).
/// </summary>
/// <remarks>Stateless and thread-safe.</remarks>
public static class SkillRules
{
    /// <summary>VOID's tolerance for XP that is a whole number give or take rounding (<c>EPSILON</c>).</summary>
    private const decimal Epsilon = 0.001m;

    /// <summary>XP that can buy a skill: half and quarter points are kept, not spent.</summary>
    /// <param name="xp">The XP.</param>
    /// <returns>The whole XP.</returns>
    public static int Whole(decimal xp) => (int)(xp + Epsilon).Round(0, DecimalRounding.Floor);

    /// <summary>XP spent on skills: cost times purchases. A skill not in the book costs nothing, as in VOID.</summary>
    /// <param name="holdings">The skills.</param>
    /// <param name="book">The rule book.</param>
    /// <returns>The XP spent.</returns>
    public static decimal Spent(Holdings holdings, RuleBook book)
    {
        ArgumentNullException.ThrowIfNull(holdings);
        ArgumentNullException.ThrowIfNull(book);

        return holdings.Sum(pair => (decimal)book.CostOf(pair.Key) * pair.Value);
    }

    /// <summary>Whether a prerequisite group holds for these holdings.</summary>
    /// <param name="group">The group.</param>
    /// <param name="holdings">The character's skills.</param>
    /// <param name="book">The rule book, for costs and which skills are extra.</param>
    /// <returns>Whether it holds. A group of an unknown kind always holds, as in VOID.</returns>
    public static bool Holds(RequirementGroup group, Holdings holdings, RuleBook book)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(holdings);
        ArgumentNullException.ThrowIfNull(book);

        return group.Kind switch
        {
            RequirementKind.Any => group.Skills.Any(skill => holdings[skill] >= group.Amount),
            RequirementKind.Points => Spent(Only(holdings, group.Skills), book) >= group.Amount - Epsilon,
            RequirementKind.Extra => holdings.Skills.Count(skill => book.TryGet(skill, out var known) && known.IsExtra) >= group.Amount,
            _ => true,
        };
    }

    /// <summary>The groups of <paramref name="requirements"/> that do not hold.</summary>
    /// <param name="requirements">A skill's prerequisite groups.</param>
    /// <param name="holdings">The character's skills.</param>
    /// <param name="book">The rule book.</param>
    /// <returns>The missing groups, in their original order.</returns>
    public static IReadOnlyList<RequirementGroup> Missing(IEnumerable<RequirementGroup> requirements, Holdings holdings, RuleBook book)
    {
        ArgumentNullException.ThrowIfNull(requirements);

        return [.. requirements.Where(group => !Holds(group, holdings, book))];
    }

    private static Holdings Only(Holdings holdings, IReadOnlyList<SkillKey> skills)
        => Holdings.Of(holdings.Where(pair => skills.Contains(pair.Key)));
}
