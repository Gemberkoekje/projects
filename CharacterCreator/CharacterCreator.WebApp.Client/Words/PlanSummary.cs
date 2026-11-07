using System.Globalization;
using CharacterCreator.core.Content;
using CharacterCreator.core.Plans;
using CharacterCreator.core.Rules;
using CharacterCreator.core.Skills;

namespace CharacterCreator.WebApp.Client.Words;

/// <summary>A labelled number for the summary: a property, an imbue cap.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record SummaryValue
{
    /// <summary>The label, in the screen's language.</summary>
    required public string Label { get; init; }

    /// <summary>The value, written out.</summary>
    required public string Value { get; init; }
}

/// <summary>A group of tiles in the summary: one skill's spells or recipes, or a category's common recipes.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
/// <typeparam name="TItem">A spell or a recipe.</typeparam>
public sealed record TileGroup<TItem>
{
    /// <summary>What the group is: the skill's name, or the category with "(algemeen)".</summary>
    required public LocalizedText Title { get; init; }

    /// <summary>Its tiles, each once in the whole summary.</summary>
    required public IReadOnlyList<TItem> Items { get; init; }
}

/// <summary>
/// What a plan adds up to beyond its XP: properties, spells and techniques, recipes. The build
/// screen's summary and the printed sheet show the same, so both ask here.
/// </summary>
/// <remarks>Stateless and thread-safe.</remarks>
public static class PlanSummary
{
    /// <summary>
    /// Hit points (1 to start with, as in the old builder), armour points and craft caps from the
    /// content's properties; imbue caps from VOID's rules. Energy is left out: it comes from VOID, as
    /// <see cref="PlanEvaluation.Energy"/>.
    /// </summary>
    /// <param name="ui">The language.</param>
    /// <param name="words">The words, for energy types.</param>
    /// <param name="content">The content.</param>
    /// <param name="plan">The plan.</param>
    /// <param name="evaluation">What the engine made of it.</param>
    /// <returns>The properties, in a fixed order.</returns>
    public static IReadOnlyList<SummaryValue> Properties(Ui ui, FindingWords words, ContentBook content, Plan plan, PlanEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(evaluation);

        int Sum(string property) => plan.All.Sum(pair => content.TryGet(pair.Key, out var skill)
            ? skill.Properties.Where(item => item.Name == property).Sum(item => item.Value) * pair.Value
            : 0);

        var properties = new List<SummaryValue>
        {
            Value(ui.T("Levenspunten", "Hit points"), 1 + Sum("hitpoints")),
            Value(ui.T("Pantserpunten", "Armour points"), Sum("armourpoints")),
        };

        properties.AddRange(evaluation.Energy.Where(total => total.Kind == EnergyKind.ImbueCap)
            .Select(total => Value(ui.T($"Imbue-limiet {words.EnergyType(total.Type).ToLowerInvariant()}", $"{words.EnergyType(total.Type)} imbue cap"), total.Amount)));

        foreach (var (property, nl, en) in new[] { ("glyph_craft_cap", "Craft-limiet glyphs", "Glyph craft cap"), ("rune_craft_cap", "Craft-limiet runen", "Rune craft cap") })
        {
            var value = Sum(property);
            if (value > 0)
            {
                properties.Add(Value(ui.T(nl, en), value));
            }
        }

        return properties;
    }

    /// <summary>The spells and techniques the plan's skills bring, each once, in plan order.</summary>
    /// <param name="content">The content.</param>
    /// <param name="plan">The plan.</param>
    /// <returns>The spells and techniques.</returns>
    public static IReadOnlyList<SpellContent> Spells(ContentBook content, Plan plan)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(plan);

        return [.. plan.All.Skills
            .SelectMany(key => content.TryGet(key, out var skill) ? skill.Spells : [])
            .DistinctBy(spell => spell.Id)];
    }

    /// <summary>The plan's skills' recipes, and a category's common recipes once the plan has a skill in it.</summary>
    /// <param name="content">The content.</param>
    /// <param name="plan">The plan.</param>
    /// <returns>The recipes, each once.</returns>
    public static IReadOnlyList<RecipeContent> Recipes(ContentBook content, Plan plan)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(plan);

        var skills = Skills(content, plan);
        var categories = skills.Select(skill => skill.Category.Nl).ToHashSet(StringComparer.Ordinal);
        return [.. skills.SelectMany(skill => skill.Recipes)
            .Concat(content.CommonRecipes.Where(group => categories.Contains(group.Category.Nl)).SelectMany(group => group.Recipes))
            .DistinctBy(recipe => recipe.Id)];
    }

    /// <summary>
    /// The spells and techniques grouped under the skill that brings them (UX-REVIEW.md P10), in plan
    /// order; a spell two skills bring sits under the first.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="plan">The plan.</param>
    /// <returns>One group per skill with spells.</returns>
    public static IReadOnlyList<TileGroup<SpellContent>> SpellGroups(ContentBook content, Plan plan)
    {
        var seen = new HashSet<int>();
        return [.. Skills(content, plan)
            .Select(skill => new TileGroup<SpellContent> { Title = skill.Name, Items = [.. skill.Spells.Where(spell => seen.Add(spell.Id))] })
            .Where(group => group.Items.Count > 0)];
    }

    /// <summary>
    /// The recipes grouped by where they come from (UX-REVIEW.md P10): each skill's own, then each
    /// category's common recipes once the plan has a skill in it; each recipe once.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="plan">The plan.</param>
    /// <returns>The groups with recipes.</returns>
    public static IReadOnlyList<TileGroup<RecipeContent>> RecipeGroups(ContentBook content, Plan plan)
    {
        var skills = Skills(content, plan);
        var categories = skills.Select(skill => skill.Category.Nl).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<int>();
        var own = skills.Select(skill => new TileGroup<RecipeContent> { Title = skill.Name, Items = [.. skill.Recipes.Where(recipe => seen.Add(recipe.Id))] });
        var common = content.CommonRecipes
            .Where(group => categories.Contains(group.Category.Nl))
            .Select(group => new TileGroup<RecipeContent>
            {
                Title = new LocalizedText { Nl = $"{group.Category.Nl} (algemeen)", En = $"{group.Category.En} (general)" },
                Items = [.. group.Recipes.Where(recipe => seen.Add(recipe.Id))],
            });
        return [.. own.Concat(common).Where(group => group.Items.Count > 0)];
    }

    /// <summary>The content of the plan's skills that the content knows, in plan order.</summary>
    /// <param name="content">The content.</param>
    /// <param name="plan">The plan.</param>
    /// <returns>Their content.</returns>
    public static IReadOnlyList<SkillContent> Skills(ContentBook content, Plan plan)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(plan);

        return [.. plan.All.Skills.Select(key => Find(content, key)).OfType<SkillContent>()];
    }

    /// <summary>The chip colour for an energy type: Guild Hall's energy tokens, ritual ones for ritual energy.</summary>
    /// <param name="type">VOID's name for the type, such as <c>Spiritual ritual</c>.</param>
    /// <returns>The chip's class, such as <c>cc-en-spiritual-r</c>; empty for an unknown type.</returns>
    public static string EnergyClass(string type)
    {
        var name = (type ?? string.Empty).ToUpperInvariant();
        var ritual = name.Contains("RITUAL", StringComparison.Ordinal);
        return name switch
        {
            _ when name.StartsWith("ELEMENTAL", StringComparison.Ordinal) => ritual ? "cc-en-elemental-r" : "cc-en-elemental",
            _ when name.StartsWith("SPIRITUAL", StringComparison.Ordinal) => ritual ? "cc-en-spiritual-r" : "cc-en-spiritual",
            "WILLPOWER" => "cc-en-willpower",
            "INSPIRATION" => "cc-en-inspiration",
            _ => string.Empty,
        };
    }

    private static SkillContent? Find(ContentBook content, SkillKey key) => content.TryGet(key, out var skill) ? skill : null;

    private static SummaryValue Value(string label, int value) => new () { Label = label, Value = value.ToString(CultureInfo.InvariantCulture) };
}
