using System.Text;
using CharacterCreator.core.Content;
using CharacterCreator.core.Skills;

namespace CharacterCreator.WebApp.Client.Words;

/// <summary>Why a skill matched a search, in words for its row: empty when its own name matched.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record SearchHit
{
    /// <summary>No match.</summary>
    public static SearchHit None { get; } = new () { Matched = false, Via = string.Empty };

    /// <summary>A match on the skill's own name or category.</summary>
    public static SearchHit Direct { get; } = new () { Matched = true, Via = string.Empty };

    /// <summary>Whether the skill matched.</summary>
    required public bool Matched { get; init; }

    /// <summary>What matched when it wasn't the name, such as "via Heal Wounds"; empty otherwise.</summary>
    required public string Via { get; init; }
}

/// <summary>
/// The picker's search (UX-REVIEW.md P8): a skill's names in both languages and its category, and from
/// three letters on also its spells and techniques, its recipes, and Thorfgin's old names for it.
/// Case and accents don't matter: "magier" finds "Magiër".
/// </summary>
/// <remarks>Immutable and thread-safe.</remarks>
/// <param name="ui">The language, for the words of a hit.</param>
/// <param name="legacy">Thorfgin's skills and the entries they became.</param>
public sealed class SkillSearch(Ui ui, IEnumerable<LegacySkill> legacy)
{
    /// <summary>A search this short only looks at names, or every spell would match.</summary>
    public const int DeepSearchFrom = 3;

    private readonly ILookup<SkillKey, string> oldNames = (legacy ?? []).ToLookup(skill => skill.Key, skill => skill.Name);

    /// <summary>Whether, and why, a skill matches a search.</summary>
    /// <param name="skill">The skill.</param>
    /// <param name="query">What was typed.</param>
    /// <returns>The hit; <see cref="SearchHit.Direct"/> for an empty search.</returns>
    public SearchHit Match(SkillContent skill, string query)
    {
        ArgumentNullException.ThrowIfNull(skill);

        var wanted = Fold(query);
        if (wanted.Length == 0
            || Contains(skill.Name.Nl, wanted)
            || Contains(skill.Name.En, wanted)
            || Contains(skill.Category.Nl, wanted)
            || Contains(skill.Category.En, wanted))
        {
            return SearchHit.Direct;
        }

        if (wanted.Length < DeepSearchFrom)
        {
            return SearchHit.None;
        }

        var spell = skill.Spells.FirstOrDefault(candidate => Contains(candidate.Name.Nl, wanted) || Contains(candidate.Name.En, wanted));
        if (spell is not null)
        {
            return Via(ui.T(spell.Name));
        }

        var recipe = skill.Recipes.FirstOrDefault(candidate => Contains(candidate.Name.Nl, wanted) || Contains(candidate.Name.En, wanted));
        if (recipe is not null)
        {
            return Via(ui.T(recipe.Name));
        }

        var old = oldNames[skill.Key].FirstOrDefault(name => Contains(name, wanted));
        return old is null
            ? SearchHit.None
            : new SearchHit { Matched = true, Via = ui.T($"oude naam: {old}", $"old name: {old}") };
    }

    /// <summary>Text for comparing: lower case, accents off, spaces trimmed.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The folded text.</returns>
    public static string Fold(string text)
    {
        var folded = new StringBuilder((text ?? string.Empty).Length);
        foreach (var character in (text ?? string.Empty).Trim().ToLowerInvariant())
        {
            folded.Append(character switch
            {
                'à' or 'á' or 'â' or 'ä' or 'ã' or 'å' => 'a',
                'è' or 'é' or 'ê' or 'ë' => 'e',
                'ì' or 'í' or 'î' or 'ï' => 'i',
                'ò' or 'ó' or 'ô' or 'ö' or 'õ' => 'o',
                'ù' or 'ú' or 'û' or 'ü' => 'u',
                'ç' => 'c',
                'ñ' => 'n',
                'ý' or 'ÿ' => 'y',
                _ => character,
            });
        }

        return folded.ToString();
    }

    private static bool Contains(string text, string folded) => Fold(text).Contains(folded, StringComparison.Ordinal);

    private SearchHit Via(string name) => new () { Matched = true, Via = ui.T($"via {name}", $"via {name}") };
}
