using Vista.Astronomy;

namespace Vista.Recipes;

/// <summary>
/// A Vista recipe (vista-plan §3.2): the set pieces currently chosen, plus time of day and season, and the
/// place controls (§3.3, §3.5): a location or a custom latitude, and date and local solar time overrides.
/// Together with a seed it fixes the whole world (pillar 4, §4.1); the camera only decides how much of that
/// world is evaluated. The linked sliders (§3.4) arrive in M1.11.
///
/// <para>Schema 2 (M1.2) added <see cref="Location"/>, <see cref="Latitude"/>, <see cref="Date"/> and
/// <see cref="SolarTime"/>, and made <see cref="Time"/> relative to the real sunrise and sunset. Schema 1 files
/// still read; they render at the default place.</para>
///
/// <para><see cref="SetPieces"/> is a <b>set</b>: order never matters (the same pieces in any order make
/// the same world). Set pieces are chosen before anything renders, and changing the set starts a new scene
/// — world, planted viewpoint and auto camera are regenerated (§3.4). What never moves the world is time
/// of day, season, the sliders and the camera.</para>
/// </summary>
public sealed record Recipe
{
    /// <summary>The recipe file format version this build writes; it reads every version from 1 up to this.</summary>
    public const int CurrentSchema = 2;

    /// <summary>File format version, from 1 to <see cref="CurrentSchema"/>.</summary>
    public int Schema { get; init; } = CurrentSchema;

    /// <summary>Optional human-readable name, e.g. "autumn lake, dawn, fog". Not part of the world.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// The chosen set pieces, each at most once. Empty is allowed: it renders plain base ground, so removing
    /// the last piece is not an error state.
    /// </summary>
    public IReadOnlyList<SetPiece> SetPieces { get; init; } = [];

    /// <summary>
    /// Time of day, relative to the date's sunrise and sunset (<see cref="Almanac"/>). <see cref="SolarTime"/>
    /// overrides it.
    /// </summary>
    public TimeOfDay Time { get; init; } = TimeOfDay.Day;

    /// <summary>Season; it picks the date for the place's hemisphere. <see cref="Date"/> overrides it.</summary>
    public Season Season { get; init; } = Season.Summer;

    /// <summary>
    /// The place from the location dropdown. <see cref="Place.None"/> means none named: the custom
    /// <see cref="Latitude"/> when there is one, else <see cref="Places.Default"/>.
    /// </summary>
    public Place Location { get; init; } = Place.None;

    /// <summary>A custom latitude in degrees (north positive), instead of a <see cref="Location"/>; null for none.</summary>
    public double? Latitude { get; init; }

    /// <summary>A date overriding <see cref="Season"/>; null takes the season's date (<see cref="SeasonCalendar"/>).</summary>
    public DateOnly? Date { get; init; }

    /// <summary>A local solar time overriding <see cref="Time"/> (12:00 is solar noon); null for none.</summary>
    public TimeOnly? SolarTime { get; init; }

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
        if (Schema < 1 || Schema > CurrentSchema)
            errors.Add($"Unsupported recipe schema {Schema}; this build reads schemas 1 to {CurrentSchema}.");
        if (!Enum.IsDefined(Location))
            errors.Add($"Unknown location {(int)Location}.");
        if (Latitude is double latitude && !(latitude is >= -90.0 and <= 90.0))
            errors.Add($"Latitude must be in [-90, 90] degrees, got {latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
        if (Latitude is not null && Location != Place.None)
            errors.Add("Give a location or a custom latitude, not both.");
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
        && Season == other.Season
        && Location == other.Location
        && Nullable.Equals(Latitude, other.Latitude)
        && Date == other.Date
        && SolarTime == other.SolarTime;
}
