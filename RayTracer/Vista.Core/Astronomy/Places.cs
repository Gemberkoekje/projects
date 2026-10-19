using System.Text.Json.Serialization;

namespace Vista.Astronomy;

/// <summary>
/// The location dropdown (vista-plan §3.5): large places and iconic landscapes, so nobody has to look up a
/// latitude. A place sets only the sky and, from M1.11, the climate defaults; the recipe decides what the
/// terrain looks like. JSON stores the camelCase names.
/// </summary>
public enum Place
{
    /// <summary>
    /// No place named: a recipe with a custom <c>latitude</c> uses that, any other uses
    /// <see cref="Places.Default"/>.
    /// </summary>
    None,
    /// <summary>Yellowstone, Wyoming.</summary>
    Yellowstone,
    /// <summary>Banff, Alberta.</summary>
    Banff,
    /// <summary>Yosemite Valley, California.</summary>
    Yosemite,
    /// <summary>Lofoten, Norway.</summary>
    Lofoten,
    /// <summary>Iceland.</summary>
    Iceland,
    /// <summary>The Scottish Highlands.</summary>
    ScottishHighlands,
    /// <summary>Zermatt, Switzerland.</summary>
    Zermatt,
    /// <summary>The Dolomites, Italy.</summary>
    Dolomites,
    /// <summary>Torres del Paine, Chile.</summary>
    TorresDelPaine,
    /// <summary>Fiordland, New Zealand.</summary>
    Fiordland,
    /// <summary>Hokkaido, Japan.</summary>
    Hokkaido,
}

/// <summary>A place's sky-relevant facts.</summary>
/// <param name="Place">The dropdown entry; <see cref="Place.None"/> for a custom latitude.</param>
/// <param name="Name">Display name.</param>
/// <param name="Spot">The point the numbers describe, e.g. "Zermatt village".</param>
/// <param name="LatitudeDegrees">Latitude, north positive.</param>
/// <param name="ValleyAltitudeMetres">
/// A typical valley-floor altitude: where the climate normals (M1.11) apply and, from M1.3–M1.4, the
/// default base of the air column.
/// </param>
public sealed record PlaceInfo(Place Place, string Name, string Spot, double LatitudeDegrees, double ValleyAltitudeMetres)
{
    /// <summary>True for a custom latitude rather than a dropdown place.</summary>
    [JsonIgnore]
    public bool IsCustom => Place == Place.None;

    /// <summary>True south of the equator, where the seasons are six months out of step (§3.5).</summary>
    [JsonIgnore]
    public bool Southern => LatitudeDegrees < 0.0;
}

/// <summary>
/// The dropdown's places. Latitudes are of the named spot (rounded to 0.01°); altitudes are typical valley
/// floors, rounded, because they only seed defaults. Sources: the spots' published coordinates and heights
/// (Wikipedia, park services); Lago Pehoé's latitude and 40 m from the Dirección General de Aguas
/// lake-shore station (climatologia.meteochile.gob.cl, station 510009).
/// </summary>
public static class Places
{
    /// <summary>
    /// The place of a recipe that names none: Zermatt, the setting of M1's "alpine range at golden hour". The
    /// linked sliders (M1.11) may let set pieces push a different default.
    /// </summary>
    public const Place Default = Place.Zermatt;

    /// <summary>Altitude of a custom latitude until the altitude control (M1.4) sets one: sea level.</summary>
    public const double CustomAltitudeMetres = 0.0;

    private static readonly PlaceInfo[] Table =
    [
        new(Place.Yellowstone, "Yellowstone", "Old Faithful, Upper Geyser Basin", 44.46, 2240.0),
        new(Place.Banff, "Banff", "Banff townsite", 51.18, 1380.0),
        new(Place.Yosemite, "Yosemite", "Yosemite Valley floor", 37.74, 1210.0),
        new(Place.Lofoten, "Lofoten", "Reine", 67.93, 10.0),
        new(Place.Iceland, "Iceland", "Þingvellir", 64.26, 100.0),
        new(Place.ScottishHighlands, "Scottish Highlands", "Glen Coe", 56.67, 150.0),
        new(Place.Zermatt, "Zermatt", "Zermatt village", 46.02, 1608.0),
        new(Place.Dolomites, "Dolomites", "Cortina d'Ampezzo", 46.54, 1224.0),
        new(Place.TorresDelPaine, "Torres del Paine", "Lago Pehoé", -51.08, 40.0),
        new(Place.Fiordland, "Fiordland", "Milford Sound", -44.67, 10.0),
        new(Place.Hokkaido, "Hokkaido", "Furano", 43.34, 175.0),
    ];

    /// <summary>Every dropdown place, in catalogue order.</summary>
    public static IReadOnlyList<PlaceInfo> All => Table;

    /// <summary>The facts for <paramref name="place"/>; <see cref="Place.None"/> gives <see cref="Default"/>'s.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="place"/> is not a catalogue value.</exception>
    public static PlaceInfo Get(Place place)
    {
        Place key = place == Place.None ? Default : place;
        return Array.Find(Table, p => p.Place == key)
            ?? throw new ArgumentOutOfRangeException(nameof(place), place, "Unknown place.");
    }

    /// <summary>A custom latitude (the dropdown's last entry) at sea level.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The latitude is outside [−90, 90].</exception>
    public static PlaceInfo Custom(double latitudeDegrees)
    {
        if (!(latitudeDegrees is >= -90.0 and <= 90.0))
            throw new ArgumentOutOfRangeException(nameof(latitudeDegrees), latitudeDegrees, "Latitude must be in [−90, 90] degrees.");
        return new PlaceInfo(Place.None, "custom latitude", "custom latitude", latitudeDegrees, CustomAltitudeMetres);
    }
}
