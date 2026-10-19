using System.Reflection;
using Vista.Astronomy;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;

namespace Vista.Output;

/// <summary>
/// The JSON file written next to every image (vista-plan §5.7 / §7.4): everything needed to reproduce it
/// — recipe, seed, camera, data tier per material, engine version — plus what it cost (rays, samples,
/// timings), which is what the preview's first calibration of the estimate reads back (§7.2).
/// The linked sliders join under a new <see cref="Schema"/> later in M1.
///
/// <para>Schema 2 (M1.1) added <see cref="ImageInfo.Window"/>, the part of the frame the image shows. Schema 3
/// (M1.2) added <see cref="Place"/> and the astronomy in <see cref="Sun"/>: date, local solar time, sunrise,
/// sunset, azimuth and elevation, each with where it came from.</para>
/// </summary>
public sealed record Sidecar
{
    /// <summary>The sidecar format version this build writes.</summary>
    public const int CurrentSchema = 3;

    /// <summary>Format version.</summary>
    public int Schema { get; init; } = CurrentSchema;

    /// <summary>"preview" for a rendered preview, "dryRun" when no GPU renderer ran.</summary>
    public required string Kind { get; init; }

    /// <summary>When the file was written (UTC, ISO 8601).</summary>
    public required string CreatedUtc { get; init; }

    /// <summary>Which build and device made the image.</summary>
    public required EngineInfo Engine { get; init; }

    /// <summary>The recipe.</summary>
    public required Recipe Recipe { get; init; }

    /// <summary>The world seed.</summary>
    public required ulong Seed { get; init; }

    /// <summary>The camera actually used (the default camera when none was given).</summary>
    public required CameraSpec Camera { get; init; }

    /// <summary>True when <see cref="Camera"/> is the default camera rather than one the caller supplied.</summary>
    public bool DefaultCamera { get; init; }

    /// <summary>Image size and quality settings.</summary>
    public required ImageInfo Image { get; init; }

    /// <summary>World generation facts.</summary>
    public required WorldInfo World { get; init; }

    /// <summary>The place the sky was computed for: the recipe's location or custom latitude, or the default place.</summary>
    public required PlaceInfo Place { get; init; }

    /// <summary>The sun the image was lit by.</summary>
    public required SunInfo Sun { get; init; }

    /// <summary>Data tier per material key (vista-plan §6.2).</summary>
    public required SortedDictionary<string, DataTier> DataTiers { get; init; }

    /// <summary>Renderer statistics; absent for a dry run.</summary>
    public RenderInfo? Render { get; init; }

    /// <summary>Pipeline stage timings in seconds (world generation, mesh, render, write).</summary>
    public required SortedDictionary<string, double> TimingsSeconds { get; init; }

    /// <summary>Output role → file name (relative to the sidecar).</summary>
    public required SortedDictionary<string, string> Outputs { get; init; }

    /// <summary>Non-fatal problems, e.g. a camera below the terrain.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>Build and device identification.</summary>
/// <param name="VistaVersion">Informational version of Vista.Core (includes the commit when the SDK knows it).</param>
/// <param name="Runtime">.NET runtime description.</param>
/// <param name="Device">GPU adapter name; null for a dry run.</param>
public sealed record EngineInfo(string VistaVersion, string Runtime, string? Device)
{
    /// <summary>The running build, on <paramref name="device"/>.</summary>
    public static EngineInfo Current(string? device) => new(
        typeof(Sidecar).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
        System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        device);
}

/// <summary>Image size and quality settings.</summary>
/// <param name="Width">Frame width in pixels (the camera's image).</param>
/// <param name="Height">Frame height in pixels.</param>
/// <param name="SamplesPerPixel">Samples per pixel.</param>
/// <param name="MaxBounces">Maximum bounces.</param>
/// <param name="DisplayTransform">How the PNG was display-encoded.</param>
/// <param name="Window">
/// The part of the frame the image files show, in frame pixels: the whole frame, or a loupe window (vista-plan
/// §7.4) whose pixels are exactly those of the full render.
/// </param>
public sealed record ImageInfo(int Width, int Height, int SamplesPerPixel, int MaxBounces, string DisplayTransform, PixelWindow Window);

/// <summary>World generation facts.</summary>
/// <param name="TerrainProfile">Terrain profile key.</param>
/// <param name="GridSize">Height-field samples per side.</param>
/// <param name="CellSizeMetres">Sample spacing.</param>
/// <param name="HeightFieldHash">Content hash of the height field (hex) — equal hashes mean the same world.</param>
/// <param name="MinHeightMetres">Lowest terrain point.</param>
/// <param name="MaxHeightMetres">Highest terrain point.</param>
/// <param name="Triangles">Terrain mesh triangles.</param>
/// <param name="MeshBytes">Terrain mesh upload size.</param>
public sealed record WorldInfo(
    string TerrainProfile,
    int GridSize,
    double CellSizeMetres,
    string HeightFieldHash,
    double MinHeightMetres,
    double MaxHeightMetres,
    long Triangles,
    long MeshBytes);

/// <summary>The sun (vista-plan §3.5): when, where in the sky, and the light the M0 dome made of it.</summary>
/// <param name="Source">How the position was computed.</param>
/// <param name="Date">The date.</param>
/// <param name="DateFrom">"season &lt;name&gt;" when the season chose the date for the hemisphere, "recipe date" for an override.</param>
/// <param name="Season">The season (the date's, when the recipe gives a date).</param>
/// <param name="SolarTime">The local solar time rendered, <c>HH:mm:ss</c> (12:00:00 is solar noon).</param>
/// <param name="SolarTimeFrom">How the time was chosen, e.g. "dawn: sunrise − 20 min" or "recipe solarTime".</param>
/// <param name="Daylight">Whether the sun rises and sets on the date.</param>
/// <param name="Sunrise">Sunrise in local solar time; null in polar day or night.</param>
/// <param name="Sunset">Sunset in local solar time; null in polar day or night.</param>
/// <param name="AzimuthDegrees">Compass bearing of the sun (0 north, 90 east).</param>
/// <param name="ElevationDegrees">Apparent elevation (with refraction), as rendered.</param>
/// <param name="TrueElevationDegrees">Geometric elevation, without refraction.</param>
/// <param name="DeclinationDegrees">The sun's declination.</param>
/// <param name="Direction">Sunlight travel direction in engine axes (the sun is toward its negation).</param>
/// <param name="TemperatureK">Blackbody colour temperature of the M0 light.</param>
public sealed record SunInfo(
    string Source,
    DateOnly Date,
    string DateFrom,
    Season Season,
    string SolarTime,
    string SolarTimeFrom,
    DaylightKind Daylight,
    string? Sunrise,
    string? Sunset,
    double AzimuthDegrees,
    double ElevationDegrees,
    double TrueElevationDegrees,
    double DeclinationDegrees,
    WorldPoint Direction,
    double TemperatureK)
{
    /// <summary>Recorded in <see cref="Source"/>.</summary>
    public const string NoaaSource = "NOAA solar calculator (Meeus), local solar time";

    /// <summary>The record of <paramref name="almanac"/> lit by <paramref name="light"/>.</summary>
    public static SunInfo From(Almanac almanac, SkyLight light)
    {
        ArgumentNullException.ThrowIfNull(almanac);
        ArgumentNullException.ThrowIfNull(light);
        Daylight daylight = almanac.Daylight;
        SunPosition sun = almanac.Sun;
        return new SunInfo(
            NoaaSource,
            almanac.Date,
            almanac.DateFromSeason ? "season " + System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(almanac.Season.ToString()) : "recipe date",
            almanac.Season,
            Almanac.FormatHours(almanac.SolarHours),
            almanac.SolarTimeFrom,
            daylight.Kind,
            daylight.HasSunriseAndSunset ? Almanac.FormatHours(daylight.SunriseHours) : null,
            daylight.HasSunriseAndSunset ? Almanac.FormatHours(daylight.SunsetHours) : null,
            sun.AzimuthDegrees,
            sun.ElevationDegrees,
            sun.TrueElevationDegrees,
            sun.DeclinationDegrees,
            new WorldPoint(-light.ToSun.X, -light.ToSun.Y, -light.ToSun.Z),
            light.SunTempK);
    }
}

/// <summary>Renderer statistics as written to the sidecar.</summary>
/// <param name="Stats">Raw counts and timings.</param>
/// <param name="RaysPerSample">Mean rays per pixel-sample.</param>
/// <param name="RaysPerSecond">Rays per second of trace time.</param>
/// <param name="DispatchBudgetMs">The GPU time per dispatch the batch planner aimed for (never changes the image).</param>
public sealed record RenderInfo(RenderStats Stats, double RaysPerSample, double RaysPerSecond, double DispatchBudgetMs)
{
    /// <summary>Wraps <paramref name="stats"/> with its derived rates and the planner's <paramref name="dispatchBudgetMs"/>.</summary>
    public static RenderInfo From(RenderStats stats, double dispatchBudgetMs)
    {
        ArgumentNullException.ThrowIfNull(stats);
        return new RenderInfo(stats, stats.RaysPerSample(), stats.RaysPerSecond(), dispatchBudgetMs);
    }
}
