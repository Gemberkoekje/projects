using CharacterCreator.core.Content;
using CharacterCreator.core.Plans;
using CharacterCreator.core.Rules;
using CharacterCreator.core.Skills;

namespace CharacterCreator.WebApp.Client.Words;

/// <summary>What kind of character a preset makes, for the presets page's filter (UX-REVIEW.md P9).</summary>
public enum PresetKind
{
    /// <summary>Not a kind.</summary>
    None = 0,

    /// <summary>Weapons, armour, fighter and paladin skills.</summary>
    Fighting,

    /// <summary>Mage and priest spells, demonology, druidism, necromancy.</summary>
    Magic,

    /// <summary>Healing spells and the healer's craft.</summary>
    Healing,

    /// <summary>Crafts and alchemy.</summary>
    Craft,

    /// <summary>Ritualism.</summary>
    Ritual,
}

/// <summary>
/// What the presets page shows of a preset at a glance (UX-REVIEW.md P9): its kinds, derived from
/// where it spends its XP, and its defining skills, the dearest ones. Derived, so no content changes
/// and no republish are needed.
/// </summary>
/// <remarks>Stateless and thread-safe.</remarks>
public static class PresetFacts
{
    /// <summary>The XP a preset spends in a kind's skills before it counts as that kind.</summary>
    public const decimal KindThreshold = 4m;

    /// <summary>The kinds, in the order the filter shows them.</summary>
    public static IReadOnlyList<PresetKind> AllKinds { get; } = [PresetKind.Fighting, PresetKind.Magic, PresetKind.Healing, PresetKind.Craft, PresetKind.Ritual];

    /// <summary>The book's categories, by their Dutch names, and the kind each belongs to. General skills belong to none.</summary>
    private static readonly Dictionary<string, PresetKind> Families = new (StringComparer.Ordinal)
    {
        ["Wapenvaardigheden"] = PresetKind.Fighting,
        ["Pantservaardigheden"] = PresetKind.Fighting,
        ["Vechtersvaardigheden"] = PresetKind.Fighting,
        ["Paladijnvaardigheden"] = PresetKind.Fighting,
        ["Magiërvaardigheden"] = PresetKind.Magic,
        ["Priestervaardigheden"] = PresetKind.Magic,
        ["Demonologie"] = PresetKind.Magic,
        ["Druidisme"] = PresetKind.Magic,
        ["Necromantie"] = PresetKind.Magic,
        ["Heelmeestervaardigheden"] = PresetKind.Healing,
        ["Genezingsvaardigheden (EL)"] = PresetKind.Healing,
        ["Genezingsvaardigheden (SP)"] = PresetKind.Healing,
        ["Ambachtsvaardigheden"] = PresetKind.Craft,
        ["Alchemistenvaardigheden"] = PresetKind.Craft,
        ["Ritualisme"] = PresetKind.Ritual,
        ["Ritualistenvaardigheden"] = PresetKind.Ritual,
    };

    /// <summary>The preset's skills as holdings.</summary>
    /// <param name="preset">The preset.</param>
    /// <returns>Its skills.</returns>
    public static Holdings Holdings(PresetContent preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return core.Plans.Holdings.Of(preset.Skills.Select(skill => new KeyValuePair<SkillKey, int>(skill.Key, skill.Times)));
    }

    /// <summary>The kinds the preset spends at least <see cref="KindThreshold"/> XP in, in filter order.</summary>
    /// <param name="preset">The preset.</param>
    /// <param name="content">The content, for the skills' categories.</param>
    /// <param name="rules">The rules, for the skills' costs.</param>
    /// <returns>Its kinds; empty when it spends that much in none.</returns>
    public static IReadOnlyList<PresetKind> Kinds(PresetContent preset, ContentBook content, RuleBook rules)
    {
        var spent = SpentPerKind(preset, content, rules);
        return [.. AllKinds.Where(kind => spent.GetValueOrDefault(kind) >= KindThreshold)];
    }

    /// <summary>The kind the preset spends the most XP in; the earlier kind in filter order on a tie.</summary>
    /// <param name="preset">The preset.</param>
    /// <param name="content">The content, for the skills' categories.</param>
    /// <param name="rules">The rules, for the skills' costs.</param>
    /// <returns>Its main kind; <see cref="PresetKind.None"/> when it spends nothing in any kind.</returns>
    public static PresetKind Primary(PresetContent preset, ContentBook content, RuleBook rules)
    {
        var spent = SpentPerKind(preset, content, rules);
        return AllKinds
            .Where(kind => spent.GetValueOrDefault(kind) > 0)
            .OrderByDescending(kind => spent[kind])
            .FirstOrDefault();
    }

    /// <summary>
    /// The presets a first visit offers (UX-REVIEW.md P5): per kind, in filter order, the first preset
    /// that is mostly that kind. A kind no preset is mostly of is left out.
    /// </summary>
    /// <param name="content">The content, with its presets.</param>
    /// <param name="rules">The rules, for the costs.</param>
    /// <returns>One preset per kind at most.</returns>
    public static IReadOnlyList<PresetContent> Starters(ContentBook content, RuleBook rules)
    {
        ArgumentNullException.ThrowIfNull(content);

        var primaries = content.Presets.Select(preset => (Preset: preset, Kind: Primary(preset, content, rules))).ToList();
        return [.. AllKinds
            .Select(kind => primaries.FirstOrDefault(candidate => candidate.Kind == kind).Preset)
            .OfType<PresetContent>()];
    }

    /// <summary>The skills that define the preset: the dearest ones, in the preset's order when they cost the same.</summary>
    /// <param name="preset">The preset.</param>
    /// <param name="rules">The rules, for the costs.</param>
    /// <param name="count">How many.</param>
    /// <returns>Up to <paramref name="count"/> skills.</returns>
    public static IReadOnlyList<SkillKey> Defining(PresetContent preset, RuleBook rules, int count)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(rules);

        return [.. preset.Skills
            .Select((skill, index) => (skill.Key, Index: index, Cost: rules.TryGet(skill.Key, out var rule) ? rule.Cost * skill.Times : 0m))
            .OrderByDescending(skill => skill.Cost)
            .ThenBy(skill => skill.Index)
            .Take(count)
            .Select(skill => skill.Key)];
    }

    /// <summary>A kind's name in the screen's language.</summary>
    /// <param name="ui">The language.</param>
    /// <param name="kind">The kind.</param>
    /// <returns>Its name, such as "Vechten".</returns>
    public static string Name(Ui ui, PresetKind kind) => kind switch
    {
        PresetKind.Fighting => ui.T("Vechten", "Fighting"),
        PresetKind.Magic => ui.T("Magie", "Magic"),
        PresetKind.Healing => ui.T("Genezing", "Healing"),
        PresetKind.Craft => ui.T("Ambacht", "Craft"),
        PresetKind.Ritual => ui.T("Ritueel", "Ritual"),
        _ => string.Empty,
    };

    /// <summary>A kind in the presets page's address (<c>?soort=</c>), the same in both languages.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>Its slug, such as <c>vechten</c>.</returns>
    public static string Slug(PresetKind kind) => kind switch
    {
        PresetKind.Fighting => "vechten",
        PresetKind.Magic => "magie",
        PresetKind.Healing => "genezing",
        PresetKind.Craft => "ambacht",
        PresetKind.Ritual => "ritueel",
        _ => string.Empty,
    };

    private static Dictionary<PresetKind, decimal> SpentPerKind(PresetContent preset, ContentBook content, RuleBook rules)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(rules);

        var spent = new Dictionary<PresetKind, decimal>();
        foreach (var skill in preset.Skills)
        {
            if (content.TryGet(skill.Key, out var known) && Families.TryGetValue(known.Category.Nl, out var kind) && rules.TryGet(skill.Key, out var rule))
            {
                spent[kind] = spent.GetValueOrDefault(kind) + (rule.Cost * skill.Times);
            }
        }

        return spent;
    }

    /// <summary>The kind a slug stands for.</summary>
    /// <param name="slug">The slug, in any case.</param>
    /// <returns>The kind; <see cref="PresetKind.None"/> for an unknown or empty slug.</returns>
    public static PresetKind FromSlug(string slug)
        => AllKinds.FirstOrDefault(kind => string.Equals(Slug(kind), slug, StringComparison.OrdinalIgnoreCase));
}
