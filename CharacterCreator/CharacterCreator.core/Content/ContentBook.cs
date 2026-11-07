using CharacterCreator.core.Skills;

namespace CharacterCreator.core.Content;

/// <summary>
/// The content of one ruleset: every offered skill's text, the common recipes and the presets
/// (DESIGN.md §4.1). A published ruleset pairs it with the rules imported from VOID.
/// </summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed class ContentBook
{
    private readonly Dictionary<SkillKey, SkillContent> byKey;
    private readonly Dictionary<string, SkillContent> byVoidName;

    private ContentBook(string ruleset, string book, IReadOnlyList<SkillContent> skills, IReadOnlyList<RecipeGroup> commonRecipes, IReadOnlyList<PresetContent> presets, IReadOnlyList<LegacySkill> legacy)
    {
        Ruleset = ruleset;
        Book = book;
        Skills = skills;
        CommonRecipes = commonRecipes;
        Presets = presets;
        Legacy = legacy;
        byKey = skills.ToDictionary(skill => skill.Key);
        byVoidName = skills.ToDictionary(skill => skill.Void, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The ruleset this content is for, such as <c>2026-v07</c>.</summary>
    public string Ruleset { get; }

    /// <summary>The book it follows, such as <c>Vaardigheden, Vortex Adventures 2026 V07</c>.</summary>
    public string Book { get; }

    /// <summary>Every offered skill, in the book's order.</summary>
    public IReadOnlyList<SkillContent> Skills { get; }

    /// <summary>Recipes open to a whole crafting category.</summary>
    public IReadOnlyList<RecipeGroup> CommonRecipes { get; }

    /// <summary>The archetypes to start from.</summary>
    public IReadOnlyList<PresetContent> Presets { get; }

    /// <summary>Thorfgin's skills and the entries they became, for importing his saves.</summary>
    public IReadOnlyList<LegacySkill> Legacy { get; }

    /// <summary>Builds the content, refusing what cannot be right.</summary>
    /// <param name="ruleset">The ruleset it is for.</param>
    /// <param name="book">The book it follows.</param>
    /// <param name="skills">The skills.</param>
    /// <param name="commonRecipes">The common recipes.</param>
    /// <param name="presets">The presets.</param>
    /// <param name="legacy">Thorfgin's skills and the entries they became; none when left out.</param>
    /// <returns>
    /// The content, or why not: a key or VOID name used twice, or a preset or one of Thorfgin's skills
    /// naming a skill that is not there.
    /// </returns>
    public static Result<ContentBook> Create(string ruleset, string book, IEnumerable<SkillContent> skills, IEnumerable<RecipeGroup> commonRecipes, IEnumerable<PresetContent> presets, IEnumerable<LegacySkill> legacy = null)
    {
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(commonRecipes);
        ArgumentNullException.ThrowIfNull(presets);

        var skillList = skills.ToList();
        var presetList = presets.ToList();
        var legacyList = (legacy ?? []).ToList();
        var messages = new List<IValidationMessage>();

        messages.AddRange(skillList.GroupBy(skill => skill.Key).Where(group => group.Count() > 1)
            .Select(group => ValidationMessage.Error($"The key '{group.Key}' is used by more than one skill.")));
        messages.AddRange(skillList.GroupBy(skill => skill.Void, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1)
            .Select(group => ValidationMessage.Error($"The VOID skill '{group.Key}' has more than one entry.")));

        var keys = skillList.Select(skill => skill.Key).ToHashSet();
        messages.AddRange(presetList.SelectMany(preset => preset.Skills.Where(skill => !keys.Contains(skill.Key))
            .Select(skill => ValidationMessage.Error($"The preset '{preset.Key}' names '{skill.Key}', which is not in the content."))));
        messages.AddRange(legacyList.Where(old => !keys.Contains(old.Key))
            .Select(old => ValidationMessage.Error($"Thorfgin's skill {old.Id} ({old.Name}) names '{old.Key}', which is not in the content.")));

        return messages.Count > 0
            ? Result.WithMessages<ContentBook>(messages)
            : Result.For(new ContentBook(ruleset ?? string.Empty, book ?? string.Empty, skillList, [.. commonRecipes], presetList, legacyList));
    }

    /// <summary>Looks up a skill's content by its key.</summary>
    /// <param name="key">The key.</param>
    /// <param name="skill">The content, when found.</param>
    /// <returns>Whether it was found.</returns>
    public bool TryGet(SkillKey key, out SkillContent skill) => byKey.TryGetValue(key, out skill);

    /// <summary>Looks up a skill's content by its VOID name, ignoring case, as VOID's own loader matches.</summary>
    /// <param name="voidName">VOID's name for the skill.</param>
    /// <param name="skill">The content, when found.</param>
    /// <returns>Whether it was found.</returns>
    public bool TryGetByVoidName(string voidName, out SkillContent skill) => byVoidName.TryGetValue(voidName ?? string.Empty, out skill);
}
