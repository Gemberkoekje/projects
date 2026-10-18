namespace Vista.Recipes;

/// <summary>
/// A Vista recipe (vista-plan §3.2): the set pieces currently chosen, plus time of day and season. Together
/// with a seed it fixes the whole world (pillar 4, §4.1); the camera only decides how much of that world is
/// evaluated. M0 holds no sliders yet — the linked sliders (§3.4) and advanced controls (§3.3) arrive with
/// M1 under a new <see cref="Schema"/> version.
///
/// <para><see cref="SetPieces"/> is a <b>set</b>: order never matters (the same pieces in any order make
/// the same world). Set pieces are chosen before anything renders, and changing the set starts a new scene
/// — world, planted viewpoint and auto camera are regenerated (§3.4). What never moves the world is time
/// of day, season, the sliders and the camera.</para>
/// </summary>
public sealed record Recipe
{
    /// <summary>The recipe file format version this build reads and writes.</summary>
    public const int CurrentSchema = 1;

    /// <summary>File format version; must equal <see cref="CurrentSchema"/>.</summary>
    public int Schema { get; init; } = CurrentSchema;

    /// <summary>Optional human-readable name, e.g. "autumn lake, dawn, fog". Not part of the world.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// The chosen set pieces, each at most once. Empty is allowed: it renders plain base ground, so removing
    /// the last piece is not an error state.
    /// </summary>
    public IReadOnlyList<SetPiece> SetPieces { get; init; } = [];

    /// <summary>Time of day.</summary>
    public TimeOfDay Time { get; init; } = TimeOfDay.Day;

    /// <summary>Season.</summary>
    public Season Season { get; init; } = Season.Summer;

    /// <summary>
    /// This recipe with <paramref name="piece"/> added (unchanged if it is already there). Any piece it
    /// conflicts with (<see cref="SetPieceConflicts"/>) is swapped out: adding a frozen lake replaces the lake.
    /// </summary>
    public Recipe With(SetPiece piece) =>
        SetPieces.Contains(piece)
            ? this
            : this with { SetPieces = [.. SetPieces.Where(p => !SetPieceConflicts.Between(p, piece)).Append(piece).Order()] };

    /// <summary>This recipe with <paramref name="piece"/> removed (unchanged if it is not there).</summary>
    public Recipe Without(SetPiece piece) =>
        SetPieces.Contains(piece) ? this with { SetPieces = [.. SetPieces.Where(p => p != piece)] } : this;

    /// <summary>
    /// Problems that make this recipe unusable (empty when it is valid). Contradictions between pieces are
    /// <i>not</i> errors (vista-plan §3.4: "contradictions are allowed").
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Schema != CurrentSchema)
            errors.Add($"Unsupported recipe schema {Schema}; this build reads schema {CurrentSchema}.");
        if (SetPieces is null)
            errors.Add("Set pieces must be a list (empty for plain base ground).");
        else
        {
            if (SetPieces.Distinct().Count() != SetPieces.Count)
                errors.Add("A set piece must not be listed twice.");
            SetPiece[] distinct = [.. SetPieces.Distinct().Order()];
            for (int i = 0; i < distinct.Length; i++)
            {
                for (int j = i + 1; j < distinct.Length; j++)
                {
                    if (SetPieceConflicts.Between(distinct[i], distinct[j]))
                        errors.Add($"Set pieces {distinct[i]} and {distinct[j]} conflict; keep one.");
                }
            }
        }
        return errors;
    }

    /// <summary>Compares recipes by content, with <see cref="SetPieces"/> as a set (record equality compares the list by reference).</summary>
    public bool ContentEquals(Recipe? other) =>
        other is not null
        && Schema == other.Schema
        && Name == other.Name
        && SetPieces.ToHashSet().SetEquals(other.SetPieces)
        && Time == other.Time
        && Season == other.Season;
}
