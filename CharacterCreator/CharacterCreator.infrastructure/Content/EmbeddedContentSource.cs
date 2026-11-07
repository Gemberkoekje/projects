using System.IO;
using System.Reflection;
using System.Text.Json;
using CharacterCreator.application.Ports;
using CharacterCreator.core.Content;

namespace CharacterCreator.infrastructure.Content;

/// <summary>
/// The content files in this assembly (<c>Content/{ruleset}/skills.json</c> and <c>presets.json</c>),
/// made by <c>scripts/import-thorfgin-content.py</c> and checked by <c>scripts/check-against-rulebook.py</c>.
/// </summary>
/// <remarks>
/// Embedded rather than read from disk, so the runtime-only image carries them without a volume.
/// Thread-safe: each ruleset is read once and kept.
/// </remarks>
public sealed class EmbeddedContentSource : IContentSource
{
    private const string Prefix = "Content/";

    private static readonly JsonSerializerOptions Json = new (JsonSerializerDefaults.Web);

    private readonly Assembly assembly = typeof(EmbeddedContentSource).Assembly;
    private readonly Dictionary<string, Result<ContentBook>> loaded = new (StringComparer.OrdinalIgnoreCase);
    private readonly Lock gate = new ();

    /// <inheritdoc />
    public IReadOnlyList<string> Rulesets
        => [.. ResourceNames()
            .Select(name => name[Prefix.Length..].Split('/')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];

    /// <inheritdoc />
    public Task<Result<ContentBook>> LoadAsync(string ruleset, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (!loaded.TryGetValue(ruleset ?? string.Empty, out var result))
            {
                result = Load(ruleset ?? string.Empty);
                loaded[ruleset ?? string.Empty] = result;
            }

            return Task.FromResult(result);
        }
    }

    private Result<ContentBook> Load(string ruleset)
    {
        var skills = Read<SkillsDocument>(ruleset, "skills.json");
        var presets = Read<PresetsDocument>(ruleset, "presets.json");
        if (skills is null || presets is null)
        {
            return Result.WithMessages<ContentBook>(ValidationMessage.Error($"There is no content for ruleset '{ruleset}'."));
        }

        return ContentBook.Create(skills.Ruleset, skills.Book, skills.Skills, skills.CommonRecipes, presets.Presets);
    }

    private T Read<T>(string ruleset, string file)
        where T : class
    {
        var wanted = $"{Prefix}{ruleset}/{file}";
        var resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => string.Equals(Normalize(name), wanted, StringComparison.OrdinalIgnoreCase));
        if (resource is null)
        {
            return null;
        }

        using var stream = assembly.GetManifestResourceStream(resource);
        return JsonSerializer.Deserialize<T>(stream, Json);
    }

    private IEnumerable<string> ResourceNames()
        => assembly.GetManifestResourceNames().Select(Normalize).Where(name => name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>The logical name carries the build machine's path separator; Windows writes a backslash.</summary>
    private static string Normalize(string name) => name.Replace('\\', '/');

    private sealed record SkillsDocument
    {
        public string Ruleset { get; init; } = string.Empty;

        public string Book { get; init; } = string.Empty;

        public List<SkillContent> Skills { get; init; } = [];

        public List<RecipeGroup> CommonRecipes { get; init; } = [];
    }

    private sealed record PresetsDocument
    {
        public List<PresetContent> Presets { get; init; } = [];
    }
}
