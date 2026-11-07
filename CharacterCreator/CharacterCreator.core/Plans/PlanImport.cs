using System.Globalization;
using System.Text;
using System.Text.Json;
using CharacterCreator.core.Content;
using CharacterCreator.core.Rulesets;
using CharacterCreator.core.Skills;

namespace CharacterCreator.core.Plans;

/// <summary>Where an imported plan came from.</summary>
public enum PlanSource
{
    /// <summary>Not known.</summary>
    None = 0,

    /// <summary>A Character Creator file.</summary>
    CharacterCreator,

    /// <summary>A save of Thorfgin's builder (builder.qequoia.nl).</summary>
    OldBuilder,
}

/// <summary>A plan read from a file, and what could not be brought along.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record ImportedPlan
{
    /// <summary>The plan.</summary>
    required public PlanFile File { get; init; }

    /// <summary>Where it came from.</summary>
    required public PlanSource Source { get; init; }

    /// <summary>The old builder's version, such as <c>2023-10b</c>; empty for a Character Creator file.</summary>
    required public string OldVersion { get; init; }

    /// <summary>Skills of an old save that have no entry now, by the old builder's name.</summary>
    required public IReadOnlyList<string> LeftOut { get; init; }
}

/// <summary>
/// Reads a plan file: the Character Creator's own JSON, or a save of Thorfgin's builder (DESIGN.md
/// §4.3). His files hold base64 of the URI-encoded JSON; his versions are 2023-10b (skills by id),
/// 2023-10a and the 2023-10 array (skills by name).
/// </summary>
/// <remarks>Stateless and thread-safe. Uses no reflection, so it runs in the trimmed build screen.</remarks>
public static class PlanImport
{
    /// <summary>Reads a file's text.</summary>
    /// <param name="text">The file's contents.</param>
    /// <param name="legacy">Thorfgin's skills and the entries they became.</param>
    /// <returns>The plan, or why the file is not one.</returns>
    public static Result<ImportedPlan> Read(string text, IReadOnlyList<LegacySkill> legacy)
    {
        ArgumentNullException.ThrowIfNull(legacy);

        var json = Unwrap(text ?? string.Empty);
        if (json.Length == 0)
        {
            return NotAPlan();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object && Text(root, "format") == PlanFile.FormatName)
            {
                var file = JsonSerializer.Deserialize(json, RulesetJsonContext.Default.PlanFile);
                return file is null ? NotAPlan() : Result.For(new ImportedPlan { File = file, Source = PlanSource.CharacterCreator, OldVersion = string.Empty, LeftOut = [] });
            }

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Skills", out var skills) && skills.ValueKind == JsonValueKind.Array)
            {
                var version = Text(root, "version");
                return Result.For(Old(Text(root, "name"), Number(root, "max_xp"), Flag(root, "is_checked"), skills, byId: version == "2023-10b", version, legacy));
            }

            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0
                && root[0].ValueKind == JsonValueKind.Object
                && Text(root[0], "ruleset_version") == "2023-10"
                && root[0].TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                return Result.For(Old(string.Empty, Number(root[0], "MAX_XP"), Flag(root[0], "isChecked"), data, byId: false, "2023-10", legacy));
            }

            return NotAPlan();
        }
        catch (JsonException)
        {
            return NotAPlan();
        }
    }

    private static ImportedPlan Old(string name, decimal maxXp, bool newCharacter, JsonElement skills, bool byId, string version, IReadOnlyList<LegacySkill> legacy)
    {
        var holdings = Holdings.None;
        var leftOut = new List<string>();

        using var items = skills.EnumerateArray();
        foreach (var skill in items)
        {
            if (skill.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var oldName = Text(skill, "skill");
            var times = Math.Max((int)Number(skill, "count"), 1);
            var found = byId ? ById(skill, legacy) : ByName(oldName, legacy);
            if (found is null)
            {
                leftOut.Add(oldName.Length > 0 ? oldName : Text(skill, "id"));
                continue;
            }

            holdings = holdings.Add(found.Key, times);
        }

        var plan = newCharacter ? Plan.NewCharacter(holdings) : Plan.Free(maxXp, holdings);
        return new ImportedPlan
        {
            File = PlanFile.From(name, plan, string.Empty, DateTimeOffset.UnixEpoch),
            Source = PlanSource.OldBuilder,
            OldVersion = version,
            LeftOut = leftOut,
        };
    }

    private static LegacySkill ById(JsonElement skill, IReadOnlyList<LegacySkill> legacy)
        => skill.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out var number)
            ? legacy.FirstOrDefault(old => old.Id == number)
            : ByName(Text(skill, "skill"), legacy);

    /// <summary>The old builder matched a name to the first skill whose name contains it; so does this, after an exact match.</summary>
    private static LegacySkill ByName(string name, IReadOnlyList<LegacySkill> legacy)
        => name.Length == 0
            ? null
            : legacy.FirstOrDefault(old => string.Equals(old.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? legacy.FirstOrDefault(old => old.Name.Contains(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Thorfgin's files hold <c>btoa(encodeURIComponent(json))</c>; JSON passes through as it is.</summary>
    private static string Unwrap(string text)
    {
        var trimmed = text.Trim().TrimStart('﻿');
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            return trimmed;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(trimmed));
            return Uri.UnescapeDataString(decoded).Trim();
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }

    private static string Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.GetRawText(),
                _ => string.Empty,
            }
            : string.Empty;

    /// <summary>A number, written as one or as text; 0 when absent or unreadable.</summary>
    private static decimal Number(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return 0m;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out number)
            ? number
            : 0m;
    }

    private static bool Flag(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static Result<ImportedPlan> NotAPlan()
        => Result.WithMessages<ImportedPlan>(ValidationMessage.Error("This is not a Character Creator file, nor a save of the old builder."));
}
