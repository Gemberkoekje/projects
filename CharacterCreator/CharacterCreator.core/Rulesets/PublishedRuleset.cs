using CharacterCreator.core.Content;
using CharacterCreator.core.Rules;
using CharacterCreator.core.Skills;

namespace CharacterCreator.core.Rulesets;

/// <summary>
/// A published ruleset (DESIGN.md §4.2): the rules imported from VOID and the content they go with,
/// as they were at publishing. Never changed afterwards; plans record the one they were made on.
/// </summary>
/// <remarks>
/// Immutable and thread-safe. It is stored as one document, and served whole to the build screen.
/// It names only skills of the book: a prerequisite on a skill outside the book (SL-only) refers to
/// it as <c>hidden-{VOID id}</c>, never by name.
/// </remarks>
public sealed record PublishedRuleset
{
    private readonly IReadOnlyList<LegacySkill> legacy = [];

    /// <summary>The ruleset's id: the content ruleset and the moment of publishing, such as <c>2026-v07@20261107T091500Z</c>.</summary>
    required public string Id { get; init; }

    /// <summary>The content ruleset it was built from, such as <c>2026-v07</c>.</summary>
    required public string ContentRuleset { get; init; }

    /// <summary>The book it follows.</summary>
    required public string Book { get; init; }

    /// <summary>When it was published.</summary>
    required public DateTimeOffset PublishedAt { get; init; }

    /// <summary>Who published it: an SL's or EC's name, or <c>first start</c> for the seed.</summary>
    required public string PublishedBy { get; init; }

    /// <summary>Where the rules came from: the VOID address, or the offline snapshot.</summary>
    required public string Source { get; init; }

    /// <summary>
    /// Whether the rules came from the synthesized offline snapshot rather than from a VOID, so the
    /// build screen can say the numbers are provisional.
    /// </summary>
    required public bool Provisional { get; init; }

    /// <summary>The rules for a new character.</summary>
    required public CreationRules Creation { get; init; }

    /// <summary>The rules per offered skill.</summary>
    required public IReadOnlyList<RuleSkill> Rules { get; init; }

    /// <summary>The content per offered skill.</summary>
    required public IReadOnlyList<SkillContent> Skills { get; init; }

    /// <summary>Recipes open to a whole crafting category.</summary>
    required public IReadOnlyList<RecipeGroup> CommonRecipes { get; init; }

    /// <summary>The archetypes to start from.</summary>
    required public IReadOnlyList<PresetContent> Presets { get; init; }

    /// <summary>Thorfgin's skills and the entries they became, for importing his saves.</summary>
    /// <remarks>
    /// Not <c>required</c>, and so are fields added later: rulesets already stored lack them, and a
    /// missing required member would make the stored document unreadable. The setter guards the
    /// default, as compile-time JSON metadata sets a missing init-only member to <c>null</c>.
    /// </remarks>
    public IReadOnlyList<LegacySkill> Legacy { get => legacy; init => legacy = value ?? []; }

    /// <summary>The rules as a rule book for the engine.</summary>
    /// <returns>The rule book, or why its rules cannot be right.</returns>
    public Result<RuleBook> ToRuleBook() => RuleBook.Create(Rules, Creation);

    /// <summary>The content as a content book.</summary>
    /// <returns>The content book, or why it cannot be right.</returns>
    public Result<ContentBook> ToContentBook() => ContentBook.Create(ContentRuleset, Book, Skills, CommonRecipes, Presets, Legacy);

    /// <summary>
    /// The skills the picker offers: those with both rules and content, and not retired by VOID. A
    /// retired skill keeps its rules, for characters that already have it.
    /// </summary>
    /// <returns>Their keys, in the content's order.</returns>
    public IReadOnlyList<SkillKey> Offered()
    {
        var ruled = Rules.Where(rule => !rule.Deprecated).Select(rule => rule.Key).ToHashSet();
        return [.. Skills.Select(skill => skill.Key).Where(ruled.Contains)];
    }
}

/// <summary>A published ruleset in a list: who, when and from where, without the rules themselves.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record RulesetSummary
{
    /// <summary>The ruleset's id.</summary>
    required public string Id { get; init; }

    /// <summary>When it was published.</summary>
    required public DateTimeOffset PublishedAt { get; init; }

    /// <summary>Who published it.</summary>
    required public string PublishedBy { get; init; }

    /// <summary>Where the rules came from.</summary>
    required public string Source { get; init; }

    /// <summary>Whether the rules came from the synthesized offline snapshot.</summary>
    required public bool Provisional { get; init; }
}
