namespace Vista.Recipes;

/// <summary>
/// A Vista recipe (vista-plan §3.2): the building blocks a wallpaper is generated from. Together with a
/// seed it fixes the whole world (pillar 4, §4.1); the camera only decides how much of that world is
/// evaluated. M0 holds the building blocks only — the linked sliders (§3.4) and advanced controls (§3.3)
/// arrive with M1 under a new <see cref="Schema"/> version.
///
/// <para><b>Modelling choice to confirm:</b> terrain and water are <i>sets</i> ("snow-capped mountains +
/// forest", "lake + waterfall"); time of day and season are single values.</para>
/// </summary>
public sealed record Recipe
{
    /// <summary>The recipe file format version this build reads and writes.</summary>
    public const int CurrentSchema = 1;

    /// <summary>File format version; must equal <see cref="CurrentSchema"/>.</summary>
    public int Schema { get; init; } = CurrentSchema;

    /// <summary>Optional human-readable name, e.g. "autumn lake, dawn, fog". Not part of the world.</summary>
    public string? Name { get; init; }

    /// <summary>Terrain building blocks; at least one.</summary>
    public required IReadOnlyList<TerrainBlock> Terrain { get; init; }

    /// <summary>Water building blocks; empty means no water.</summary>
    public IReadOnlyList<WaterBlock> Water { get; init; } = [];

    /// <summary>Time of day.</summary>
    public TimeOfDay Time { get; init; } = TimeOfDay.Day;

    /// <summary>Season.</summary>
    public Season Season { get; init; } = Season.Summer;

    /// <summary>
    /// Problems that make this recipe unusable (empty when it is valid). Contradictions between blocks are
    /// <i>not</i> errors (vista-plan §3.4: "contradictions are allowed").
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Schema != CurrentSchema)
            errors.Add($"Unsupported recipe schema {Schema}; this build reads schema {CurrentSchema}.");
        if (Terrain is null || Terrain.Count == 0)
            errors.Add("A recipe needs at least one terrain building block.");
        else if (Terrain.Distinct().Count() != Terrain.Count)
            errors.Add("Terrain building blocks must not repeat.");
        if (Water is null)
            errors.Add("Water must be a list (empty for none).");
        else if (Water.Distinct().Count() != Water.Count)
            errors.Add("Water building blocks must not repeat.");
        return errors;
    }

    /// <summary>Record equality compares the lists by reference; this compares recipes by content.</summary>
    public bool ContentEquals(Recipe? other) =>
        other is not null
        && Schema == other.Schema
        && Name == other.Name
        && Terrain.SequenceEqual(other.Terrain)
        && Water.SequenceEqual(other.Water)
        && Time == other.Time
        && Season == other.Season;
}
