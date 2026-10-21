using Vista.Astronomy;
using Vista.Sliders;

namespace Vista.Recipes;

/// <summary>
/// A Vista recipe (vista-plan §3.2): the set pieces currently chosen. Together with a seed it fixes the whole world (pillar 4,
/// §4.1); the camera only decides how much of that world is evaluated, and the linked sliders (§3.4) the conditions it is seen
/// in — place, date, time of day and weather.
///
/// <para><b>Slider pins</b> belong in a slider file (<see cref="SliderPins"/>, M1.11). A recipe may still pin the sliders it held
/// before the slider file existed — <see cref="Time"/> and <see cref="Season"/>, and since schema 2 (M1.2) <see cref="Location"/>,
/// <see cref="Latitude"/>, <see cref="Date"/> and <see cref="SolarTime"/>, schema 3 (M1.3) <see cref="SunElevation"/>, schema 4
/// (M1.4) <see cref="Altitude"/> — each field it gives pinning its slider as a slider file would
/// (<see cref="SliderPins.FromRecipe"/>); a field it leaves out is auto. A preview takes its pins from one or the other, never
/// both. The weather sliders (temperature, humidity, snow, haze) are pinned in a slider file or by options only.</para>
///
/// <para><see cref="SetPieces"/> is a <b>set</b>: order never matters (the same pieces in any order make
/// the same world). Set pieces are chosen before anything renders, and changing the set starts a new scene
/// — world, planted viewpoint and auto camera are regenerated (§3.4). What never moves the world is the sliders and the
/// camera.</para>
/// </summary>
public sealed record Recipe
{
    /// <summary>The recipe file format version this build writes; it reads every version from 1 up to this.</summary>
    public const int CurrentSchema = 4;

    /// <summary>File format version, from 1 to <see cref="CurrentSchema"/>.</summary>
    public int Schema { get; init; } = CurrentSchema;

    /// <summary>Optional human-readable name, e.g. "autumn lake, dawn, fog". Not part of the world.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// The chosen set pieces, each at most once. Empty is allowed: it renders plain base ground, so removing
    /// the last piece is not an error state.
    /// </summary>
    public IReadOnlyList<SetPiece> SetPieces { get; init; } = [];

    /// <summary>The time-of-day preset, which pins the <c>time</c> slider; null for auto (day, unless a set piece pushes another).</summary>
    public TimeOfDay? Time { get; init; }

    /// <summary>The season, which pins the <c>season</c> slider; null for auto (the date's, a set piece's, or summer).</summary>
    public Season? Season { get; init; }

    /// <summary>The place from the location dropdown, which pins the <c>location</c> slider; <see cref="Place.None"/> for none.</summary>
    public Place Location { get; init; } = Place.None;

    /// <summary>A custom latitude in degrees (north positive), which pins the <c>location</c> slider; null for none.</summary>
    public double? Latitude { get; init; }

    /// <summary>A date, which pins the <c>date</c> slider (and decides the season, M1.2); null for none.</summary>
    public DateOnly? Date { get; init; }

    /// <summary>A local solar time, which pins the <c>solarTime</c> slider; null for none.</summary>
    public TimeOnly? SolarTime { get; init; }

    /// <summary>A geometric sun elevation in degrees, which pins the <c>solarTime</c> slider by height; null for none.</summary>
    public double? SunElevation { get; init; }

    /// <summary>The scene's base altitude above sea level in metres, which pins the <c>altitude</c> slider; null for none.</summary>
    public double? Altitude { get; init; }

    /// <summary>True when the recipe pins any slider itself (it gives a time, season, place or altitude field).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasPins =>
        Time is not null || Season is not null || Location != Place.None || Latitude is not null || Date is not null
        || SolarTime is not null || SunElevation is not null || Altitude is not null;

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
        errors.AddRange(SliderPins.FromRecipe(this).Validate());
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
        && SolarTime == other.SolarTime
        && Nullable.Equals(SunElevation, other.SunElevation)
        && Nullable.Equals(Altitude, other.Altitude);
}
