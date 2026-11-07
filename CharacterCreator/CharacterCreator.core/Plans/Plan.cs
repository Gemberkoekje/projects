namespace CharacterCreator.core.Plans;

/// <summary>Which rules a plan is held to (DESIGN.md §3.3).</summary>
public enum PlanMode
{
    /// <summary>No mode chosen.</summary>
    None = 0,

    /// <summary>
    /// A new character: the rule book's starting XP, each skill at most <c>base_max</c> times (so no
    /// extra skills), and a warning when too much XP is left over.
    /// </summary>
    NewCharacter,

    /// <summary>
    /// An existing character: its skills from VOID are fixed, new purchases come out of whole XP,
    /// and a first purchase of an extra skill goes through a teacher, one per event.
    /// </summary>
    Existing,

    /// <summary>Free planning against any total XP, held only to <c>times_max</c> and the prerequisites.</summary>
    Free,
}

/// <summary>A character plan: a mode, an XP total, the skills already held and the purchases planned.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record Plan
{
    /// <summary>Which rules apply.</summary>
    required public PlanMode Mode { get; init; }

    /// <summary>
    /// The XP total: the character's XP for <see cref="PlanMode.Existing"/>, the chosen total for
    /// <see cref="PlanMode.Free"/>. A new character always starts with the rule book's XP, so this
    /// is ignored for <see cref="PlanMode.NewCharacter"/>.
    /// </summary>
    public decimal Xp { get; init; }

    /// <summary>The skills the character already has (only for <see cref="PlanMode.Existing"/>). They are fixed.</summary>
    public Holdings Held { get; init; } = Holdings.None;

    /// <summary>The purchases planned on top of <see cref="Held"/>.</summary>
    public Holdings Planned { get; init; } = Holdings.None;

    /// <summary>Everything the character would have: <see cref="Held"/> and <see cref="Planned"/> together.</summary>
    public Holdings All => Held.Plus(Planned);

    /// <summary>A new character with these purchases.</summary>
    /// <param name="planned">The purchases.</param>
    /// <returns>The plan.</returns>
    public static Plan NewCharacter(Holdings planned) => new () { Mode = PlanMode.NewCharacter, Planned = planned ?? Holdings.None };

    /// <summary>An existing character with its XP and skills, and purchases on top.</summary>
    /// <param name="xp">The character's XP total.</param>
    /// <param name="held">The character's skills.</param>
    /// <param name="planned">The purchases.</param>
    /// <returns>The plan.</returns>
    public static Plan Existing(decimal xp, Holdings held, Holdings planned)
        => new () { Mode = PlanMode.Existing, Xp = xp, Held = held ?? Holdings.None, Planned = planned ?? Holdings.None };

    /// <summary>Free planning against a total.</summary>
    /// <param name="xp">The total.</param>
    /// <param name="planned">The purchases.</param>
    /// <returns>The plan.</returns>
    public static Plan Free(decimal xp, Holdings planned) => new () { Mode = PlanMode.Free, Xp = xp, Planned = planned ?? Holdings.None };

    /// <summary>This plan with <paramref name="n"/> more purchases of <paramref name="skill"/>.</summary>
    /// <param name="skill">The skill.</param>
    /// <param name="n">Purchases to add, or to remove when negative.</param>
    /// <returns>The new plan.</returns>
    public Plan WithPurchase(Skills.SkillKey skill, int n = 1) => this with { Planned = Planned.Add(skill, n) };
}
