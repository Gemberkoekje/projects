using CharacterCreator.core.Skills;

namespace CharacterCreator.core.Content;

/// <summary>
/// What the Character Creator adds to a VOID skill: names, text, page, spells, recipes and loresheet
/// (DESIGN.md §4.1). One entry per skill the picker offers, which is one per VOID skill.
/// </summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record SkillContent
{
    /// <summary>The skill's key: a slug of its VOID name, such as <c>healer-a-elemental</c>.</summary>
    required public SkillKey Key { get; init; }

    /// <summary>The VOID skill this entry stands for, by VOID's name, such as <c>healer A (elemental)</c>.</summary>
    required public string Void { get; init; }

    /// <summary>The book's names for it; more than one where VOID files two book skills under one.</summary>
    required public IReadOnlyList<string> Book { get; init; }

    /// <summary>The name to show.</summary>
    required public LocalizedText Name { get; init; }

    /// <summary>The picker's group, such as <c>Pantservaardigheden</c>.</summary>
    required public LocalizedText Category { get; init; }

    /// <summary>The page of the skills book its heading is on.</summary>
    required public int Page { get; init; }

    /// <summary>Which book <see cref="Page"/> is from, such as <c>2026-v07</c>.</summary>
    required public string PageFrom { get; init; }

    /// <summary>
    /// The cost the book prints, for the VOID import to compare with VOID's cost; -1 when not found.
    /// VOID's cost is the one the rules use (DESIGN.md §2, decision 3).
    /// </summary>
    required public int BookCost { get; init; }

    /// <summary>What the skill does.</summary>
    required public LocalizedText Description { get; init; }

    /// <summary>Properties VOID does not hold, such as hit points (DESIGN.md §3.1).</summary>
    required public IReadOnlyList<SkillProperty> Properties { get; init; }

    /// <summary>The spells or techniques the skill gives.</summary>
    required public IReadOnlyList<SpellContent> Spells { get; init; }

    /// <summary>The recipes the skill gives.</summary>
    required public IReadOnlyList<RecipeContent> Recipes { get; init; }

    /// <summary>The loresheet, if there is one.</summary>
    required public Loresheet Loresheet { get; init; }
}

/// <summary>A property of a skill that VOID does not hold, such as <c>hitpoints</c> or <c>inspiration</c>.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record SkillProperty
{
    /// <summary>The property, as Thorfgin named it: <c>hitpoints</c>, <c>armourpoints</c>, <c>elemental_mana</c>, ….</summary>
    required public string Name { get; init; }

    /// <summary>What one purchase gives.</summary>
    required public int Value { get; init; }
}

/// <summary>A loresheet: a PDF on the-vortex.nl, and sometimes a font that goes with it.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record Loresheet
{
    /// <summary>No loresheet.</summary>
    public static Loresheet None { get; } = new () { Url = string.Empty, Page = 0, Extra = string.Empty };

    /// <summary>The PDF's address; empty when there is no loresheet.</summary>
    required public string Url { get; init; }

    /// <summary>The page in the PDF.</summary>
    required public int Page { get; init; }

    /// <summary>A second file, such as the runes font; empty when there is none.</summary>
    required public string Extra { get; init; }

    /// <summary>Whether there is a loresheet.</summary>
    public bool Exists => !string.IsNullOrEmpty(Url);
}

/// <summary>A spell or fighter technique.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record SpellContent
{
    /// <summary>Thorfgin's id, unique within the content.</summary>
    required public int Id { get; init; }

    /// <summary>The spell's name.</summary>
    required public LocalizedText Name { get; init; }

    /// <summary>The energy it costs, such as Wilskracht or Mana.</summary>
    required public LocalizedText EnergyType { get; init; }

    /// <summary>How much energy, as the book writes it.</summary>
    required public string Cost { get; init; }

    /// <summary>What is called out.</summary>
    required public LocalizedText Incantation { get; init; }

    /// <summary>What it does.</summary>
    required public LocalizedText Description { get; init; }

    /// <summary>Its reach: direct, touch, self, ….</summary>
    required public LocalizedText Effect { get; init; }

    /// <summary>How long it lasts.</summary>
    required public LocalizedText Duration { get; init; }

    /// <summary>The page of the spells book it is on.</summary>
    required public int Page { get; init; }
}

/// <summary>A crafting or alchemy recipe.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record RecipeContent
{
    /// <summary>Thorfgin's id.</summary>
    required public int Id { get; init; }

    /// <summary>The recipe's name.</summary>
    required public LocalizedText Name { get; init; }

    /// <summary>What it makes or does.</summary>
    required public LocalizedText Effect { get; init; }

    /// <summary>The inspiration it costs, as the book writes it.</summary>
    required public string Inspiration { get; init; }

    /// <summary>The components it needs.</summary>
    required public LocalizedText Components { get; init; }
}

/// <summary>Recipes open to everyone in a crafting category, not tied to one skill.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record RecipeGroup
{
    /// <summary>The category, such as <c>Ambachtsvaardigheden</c>.</summary>
    required public LocalizedText Category { get; init; }

    /// <summary>The recipes.</summary>
    required public IReadOnlyList<RecipeContent> Recipes { get; init; }
}

/// <summary>An archetype to start from, such as a light fighter (DESIGN.md §5, Voorbeelden).</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record PresetContent
{
    /// <summary>The preset's key.</summary>
    required public string Key { get; init; }

    /// <summary>The preset's name.</summary>
    required public LocalizedText Name { get; init; }

    /// <summary>What kind of character it is, and why these skills.</summary>
    required public LocalizedText Information { get; init; }

    /// <summary>The XP it is built for.</summary>
    required public decimal Xp { get; init; }

    /// <summary>Whether it is a new character (15 XP, creation rules).</summary>
    required public bool NewCharacter { get; init; }

    /// <summary>Its skills.</summary>
    required public IReadOnlyList<PresetSkill> Skills { get; init; }
}

/// <summary>A skill in a preset.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record PresetSkill
{
    /// <summary>The skill.</summary>
    required public SkillKey Key { get; init; }

    /// <summary>How often it is bought.</summary>
    required public int Times { get; init; }
}
