using System.Reflection;
using Vista.Astronomy;
using Vista.Atmosphere;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;
using Vista.World;

namespace Vista.Output;

/// <summary>
/// The JSON file written next to every image (vista-plan §5.7 / §7.4): everything needed to reproduce it
/// — recipe, seed, camera, data tier per material, engine version — plus what it cost (rays, samples,
/// timings), which is what the preview's first calibration of the estimate reads back (§7.2).
///
/// <para>Schema 2 (M1.1) added <see cref="ImageInfo.Window"/>, the part of the frame the image shows. Schema 3
/// (M1.2) added <see cref="Place"/> and the astronomy in <see cref="Sun"/>: date, local solar time, sunrise,
/// sunset, azimuth and elevation, each with where it came from. Schema 4 (M1.3) added <see cref="Atmosphere"/>
/// (the air model, the scene's base altitude, how the air was evaluated) and <see cref="Exposure"/> (how the PNG
/// was metered), the radiance units in <see cref="ImageInfo.Units"/>, and in <see cref="Sun"/> the sunlight's
/// transmittance to the camera in place of the M0 light's colour temperature. Schema 5 (M1.4) added, in
/// <see cref="Atmosphere"/>, where the base altitude came from and how aerial perspective was evaluated. Schema 6 (M1.5)
/// added, in <see cref="Image"/>, which lights surfaces sampled and where Russian roulette starts. Schema 7 (M1.6) added
/// <see cref="Denoise"/>: the settings asked for, and the denoiser's library, version, device and filter, or why it did not
/// run. Schema 8 (M1.7) added <see cref="WorldInfo.Erosion"/>: where the world's erosion came from (eroded now, the cache, or
/// none), the settings, the un-eroded field's hash, the device and driver that eroded it, the cache file and the cost; the
/// <c>heightFieldHash</c> is now the eroded surface's. Schema 9 (M1.8) added <see cref="Stages"/>, which stages of the preview
/// ran for this image and which it reused from an earlier render in the same process, and <see cref="Watch"/>, what changed
/// before a watch render. Schema 10 (M1.9) added <see cref="WorldInfo.Mesh"/>: the view-dependent mesh's settings, its
/// triangles (those below the world grid and beyond it apart), its reach and what it cost; <c>triangles</c> and
/// <c>meshBytes</c> are the view's mesh now, not the world grid's. Schema 11 (M1.10a) added <see cref="ExposureInfo.Meter"/>, which
/// meter exposed the PNG, with its reading in <see cref="ExposureInfo.Incident"/> or <see cref="ExposureInfo.Average"/> (the
/// average meter's fields moved there) and the meter's own stops in <see cref="ExposureInfo.MeteredEv"/>, and
/// <see cref="ImageInfo.Display"/>, the PNG's display curve. Schema 12 (M1.11) added <see cref="Sliders"/>: every linked slider's value,
/// whether it was pinned, and where an auto value came from, with the weather the chain worked out (the climate's day, the dew point,
/// the freezing level, the snowline and the haze). Schema 13 (M1.11a) added, in <see cref="WorldInfo.Erosion"/>, the world's drainage
/// measures (<see cref="ErosionInfo.Drainage"/>), the stream-power incision's settings and its time and that of the maps and the
/// drainage in the cost. Schema 14 (M1.11b) added <see cref="ImageInfo.Look"/>, AgX's look, and
/// <see cref="ExposureInfo.MeterCompensationEv"/>, the stops the meter added on its own. Schema 15 (M1.11c) added
/// <see cref="ImageInfo.Dither"/> and <see cref="ImageInfo.BitDepth"/>, how the PNG was quantised.</para>
/// </summary>
public sealed record Sidecar
{
    /// <summary>The sidecar format version this build writes.</summary>
    public const int CurrentSchema = 15;

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

    /// <summary>The place the sky was computed for: the location slider's place or custom latitude.</summary>
    public required PlaceInfo Place { get; init; }

    /// <summary>The linked sliders (schema 12): each one's value, pin and origin, and the weather chain's working.</summary>
    public required SlidersInfo Sliders { get; init; }

    /// <summary>The sun the image was lit by.</summary>
    public required SunInfo Sun { get; init; }

    /// <summary>The air the light came through.</summary>
    public required AtmosphereInfo Atmosphere { get; init; }

    /// <summary>How the PNG was exposed; absent for a dry run.</summary>
    public ExposureInfo? Exposure { get; init; }

    /// <summary>How the denoised beauty was made, or why there is none; absent when no denoising was asked for.</summary>
    public DenoiseInfo? Denoise { get; init; }

    /// <summary>Data tier per material key (vista-plan §6.2).</summary>
    public required SortedDictionary<string, DataTier> DataTiers { get; init; }

    /// <summary>Renderer statistics; absent for a dry run.</summary>
    public RenderInfo? Render { get; init; }

    /// <summary>Which stages ran for this image and which it reused from an earlier render in the same process (schema 9).</summary>
    public required StageInfo Stages { get; init; }

    /// <summary>What changed before this render, when a watch session made it (schema 9); absent otherwise.</summary>
    public WatchInfo? Watch { get; init; }

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
public sealed record ImageInfo(int Width, int Height, int SamplesPerPixel, int MaxBounces, string DisplayTransform, PixelWindow Window)
{
    /// <summary>Recorded in <see cref="Units"/>.</summary>
    public const string RadianceUnits = "W·m⁻²·sr⁻¹·nm⁻¹: Y is the ȳ-weighted mean spectral radiance; × 72 985 gives cd/m²";

    /// <summary>The units of the EXR's values (the PNG is exposed, see <see cref="Sidecar.Exposure"/>).</summary>
    public string Units { get; init; } = RadianceUnits;

    /// <summary>The PNG's display curve (schema 11); <see cref="DisplayTransform"/> says the same in words.</summary>
    public DisplayCurve Display { get; init; } = Output.DisplayTransform.DefaultCurve;

    /// <summary>AgX's look (schema 14); <see cref="DisplayLook.None"/> under the ACES fit, which has none.</summary>
    public DisplayLook Look { get; init; } = Output.DisplayTransform.DefaultLook;

    /// <summary>The 8-bit PNG's dither (schema 15); <see cref="PngDither.None"/> for a 16-bit PNG, which needs none.</summary>
    public PngDither Dither { get; init; } = Output.DisplayTransform.DefaultDither;

    /// <summary>The beauty's and the denoised beauty's bits per channel (schema 15): 8, or 16.</summary>
    public int BitDepth { get; init; } = Output.DisplayTransform.DefaultBitDepth;

    /// <summary>
    /// Which lights surfaces sampled (vista-plan §10 M1.5): <see cref="LightSampling.Mis"/>, or M1.4's
    /// <see cref="LightSampling.SunOnly"/> — always for the reference atmosphere, which has no sky view to sample.
    /// </summary>
    public LightSampling LightSampling { get; init; } = LightSampling.Mis;

    /// <summary>The bounce from which Russian roulette could end a path.</summary>
    public int RussianRouletteStart { get; init; } = RenderSettings.DefaultRussianRouletteStart;

    /// <summary>The light sampling a render with <paramref name="settings"/> actually does.</summary>
    public static LightSampling LightSamplingOf(RenderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Atmosphere == AtmosphereMode.Luts ? settings.LightSampling : LightSampling.SunOnly;
    }
}

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
    long MeshBytes)
{
    /// <summary>The world's erosion (schema 8): from the cache, eroded now, or not at all, and why.</summary>
    public ErosionInfo? Erosion { get; init; }

    /// <summary>The view's terrain mesh (schema 10): how it was refined, how big it is, how far it reaches, what it cost.</summary>
    public MeshInfo? Mesh { get; init; }
}

/// <summary>The sun (vista-plan §3.5): when, where in the sky, and how much of its light reaches the camera.</summary>
/// <param name="Source">How the position was computed.</param>
/// <param name="Date">The date.</param>
/// <param name="DateFrom">"season &lt;name&gt;" when the season chose the date for the hemisphere, "pinned date" for a pin.</param>
/// <param name="Season">The season (the date's, when the date is pinned and the season is not).</param>
/// <param name="SolarTime">The local solar time rendered, <c>HH:mm:ss</c> (12:00:00 is solar noon).</param>
/// <param name="SolarTimeFrom">How the time was chosen, e.g. "dawn: sun at −4°, rising", "pinned solarTime" or "pinned sunElevation −2°, setting".</param>
/// <param name="Daylight">Whether the sun rises and sets on the date.</param>
/// <param name="Sunrise">Sunrise in local solar time; null in polar day or night.</param>
/// <param name="Sunset">Sunset in local solar time; null in polar day or night.</param>
/// <param name="AzimuthDegrees">Compass bearing of the sun (0 north, 90 east).</param>
/// <param name="ElevationDegrees">Apparent elevation (with refraction), as rendered.</param>
/// <param name="TrueElevationDegrees">Geometric elevation, without refraction.</param>
/// <param name="DeclinationDegrees">The sun's declination.</param>
/// <param name="Direction">Sunlight travel direction in engine axes (the sun is toward its negation).</param>
/// <param name="Spectrum">The light above the atmosphere.</param>
/// <param name="InEarthShadowAtCamera">True when the ground sphere hides the sun's centre from the camera.</param>
/// <param name="TransmittanceToCamera">The air's transmittance along the sun's direction to the camera at three wavelengths (0 in the Earth's shadow).</param>
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
    string Spectrum,
    bool InEarthShadowAtCamera,
    ThreeWavelengths TransmittanceToCamera)
{
    /// <summary>Recorded in <see cref="Source"/>.</summary>
    public const string NoaaSource = "NOAA solar calculator (Meeus), local solar time";

    /// <summary>Recorded in <see cref="Spectrum"/>.</summary>
    public const string SolarSpectrumName = "blackbody 5778 K, disk radius 0.2666°, physical units (vista-plan §10 M1.3)";

    /// <summary>The record of <paramref name="almanac"/>, its sunlight's transmittance to the camera taken through <paramref name="frame"/>.</summary>
    public static SunInfo From(Almanac almanac, AtmosphereFrame frame)
    {
        ArgumentNullException.ThrowIfNull(almanac);
        ArgumentNullException.ThrowIfNull(frame);
        Daylight daylight = almanac.Daylight;
        SunPosition sun = almanac.Sun;
        double altitude = frame.ClampAltitude(frame.CameraAltitudeMetres);
        double mu = frame.ToSun.Y;
        bool shadow = AtmosphereLuts.HitsSphere(frame.GroundRadius, altitude - frame.GroundAltitudeMetres, mu);
        DensityColumns columns = shadow ? default : AtmosphereLuts.ColumnsAlong(frame.Model, altitude, mu, 4096);
        double At(double nm) => shadow ? 0.0 : Math.Exp(-frame.Model.CoefficientsAt(nm).OpticalDepth(columns));
        return new SunInfo(
            NoaaSource,
            almanac.Date,
            almanac.DateFromSeason ? "season " + System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(almanac.Season.ToString()) : "pinned date",
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
            new WorldPoint(-frame.ToSun.X, -frame.ToSun.Y, -frame.ToSun.Z),
            SolarSpectrumName,
            shadow,
            new ThreeWavelengths(At(450.0), At(550.0), At(650.0)));
    }
}

/// <summary>A quantity at 450, 550 and 650 nm (blue, green, red).</summary>
/// <param name="At450Nm">At 450 nm.</param>
/// <param name="At550Nm">At 550 nm.</param>
/// <param name="At650Nm">At 650 nm.</param>
public sealed record ThreeWavelengths(double At450Nm, double At550Nm, double At650Nm);

/// <summary>The air of one render (vista-plan §5.2, §10 M1.3, M1.4).</summary>
/// <param name="Mode">How the integrator evaluated it: the LUTs or the reference.</param>
/// <param name="Model">The atmosphere model.</param>
/// <param name="GroundAltitudeMetres">The scene's base altitude above sea level: world y = 0 and the ground sphere.</param>
/// <param name="GroundAltitudeFrom">Where the base altitude came from: the recipe's altitude or the place's valley floor.</param>
/// <param name="CameraAltitudeMetres">The camera's altitude above sea level.</param>
/// <param name="ZenithFromSeaLevel">Vertical optical depths from sea level.</param>
/// <param name="ZenithFromGround">Vertical optical depths from the scene's base altitude.</param>
/// <param name="AerialPerspective">How the air along segments that end on the terrain was evaluated.</param>
/// <param name="Sources">Where the constants come from.</param>
public sealed record AtmosphereInfo(
    AtmosphereMode Mode,
    AtmosphereModel Model,
    double GroundAltitudeMetres,
    string GroundAltitudeFrom,
    double CameraAltitudeMetres,
    OpticalDepths ZenithFromSeaLevel,
    OpticalDepths ZenithFromGround,
    string AerialPerspective,
    string Sources)
{
    /// <summary>Recorded in <see cref="AerialPerspective"/> for the LUTs.</summary>
    public static readonly string LutAerialPerspective = string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"march: 2 steps + 1 per {Vista.Atmosphere.AerialPerspective.StepLengthMetres:0} m, at most {Vista.Atmosphere.AerialPerspective.MaxSteps}; scattering orders read at both ends (once when the segment spans under {Vista.Atmosphere.AerialPerspective.SingleLookupSpanMetres:0} m of height); the world bent onto the Earth around the camera; no mountain shadows in the air");

    /// <summary>Recorded in <see cref="AerialPerspective"/> for the reference modes.</summary>
    public const string ReferenceAerialPerspective = "delta tracking along each segment; the rest of a scattered path in the air alone";

    /// <summary>Recorded in <see cref="Sources"/>.</summary>
    public const string SourceNotes =
        "Rayleigh: Peck & Reeder 1972 refractive index, Bates 1984 King factor, US Standard Atmosphere column; " +
        "ozone: Serdyuchenko et al. 2014 (233 K) in 10 nm bins via Bruneton 2017; haze: Ångström law, Henyey–Greenstein; " +
        "LUTs after Hillaire 2020, multiple scattering after Bruneton 2017; built-in tier 5";

    /// <summary>The record of <paramref name="frame"/> evaluated by <paramref name="mode"/>, its base altitude from <paramref name="groundAltitudeFrom"/>.</summary>
    public static AtmosphereInfo From(AtmosphereFrame frame, AtmosphereMode mode, string groundAltitudeFrom)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return new AtmosphereInfo(
            mode,
            frame.Model,
            frame.GroundAltitudeMetres,
            groundAltitudeFrom,
            frame.CameraAltitudeMetres,
            OpticalDepths.Of(frame.Model, 0.0),
            OpticalDepths.Of(frame.Model, frame.GroundAltitudeMetres),
            mode switch
            {
                AtmosphereMode.Reference => ReferenceAerialPerspective,
                AtmosphereMode.ReferenceShadowed => ReferenceAerialPerspective + "; the terrain's shadow on the sunlight it scatters once",
                _ => LutAerialPerspective,
            },
            SourceNotes);
    }
}

/// <summary>Vertical optical depths: molecules and haze at 550 nm, ozone at 602 nm (the Chappuis band's peak on the engine's grid).</summary>
/// <param name="Rayleigh550">Molecular scattering at 550 nm.</param>
/// <param name="Aerosol550">Haze extinction at 550 nm.</param>
/// <param name="Ozone602">Ozone absorption at 602 nm.</param>
public sealed record OpticalDepths(double Rayleigh550, double Aerosol550, double Ozone602)
{
    /// <summary>The depths of <paramref name="model"/> from <paramref name="altitude"/> above sea level to the top.</summary>
    public static OpticalDepths Of(AtmosphereModel model, double altitude)
    {
        ArgumentNullException.ThrowIfNull(model);
        (double rayleigh, double aerosol, _) = model.ZenithOpticalDepths(550.0, altitude);
        (_, _, double ozone) = model.ZenithOpticalDepths(602.0, altitude);
        return new OpticalDepths(rayleigh, aerosol, ozone);
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

/// <summary>
/// A stage of a preview, in pipeline order (vista-plan §7.4, §10 M1.8). What each depends on decides when a session rebuilds it:
/// the world follows the set pieces and the seed; the mesh the world (and the camera from M1.9); the acceleration structure the
/// mesh; the air's tables its model, the scattering orders also the base altitude, the sky view and the sky distribution also the
/// sun, the camera's altitude and the terrain's span; the render everything the renderer reads; the exposure meter, as the incident
/// meter, the air's frame (the sun, the camera's altitude), or as the average meter the same as the render at its own size; the
/// denoised image the render, the denoise settings and the meter. Writing the files — exposing and display-encoding the PNG
/// included — always runs.
/// </summary>
public enum PreviewStage
{
    /// <summary>Not a stage.</summary>
    None,

    /// <summary>The GPU device and its compiled shaders.</summary>
    Device,

    /// <summary>The world: the base terrain and its erosion (eroded, or read from the erosion cache on disk).</summary>
    World,

    /// <summary>The terrain mesh, on the CPU.</summary>
    Mesh,

    /// <summary>The mesh on the GPU and its acceleration structures.</summary>
    AccelerationStructure,

    /// <summary>The air model's wavelength tables and the columns LUT.</summary>
    AtmosphereTables,

    /// <summary>The air's scattering orders and the sky's irradiance on the ground.</summary>
    ScatteringOrders,

    /// <summary>The sky-view LUT.</summary>
    SkyView,

    /// <summary>The sky distribution (the sky sampled as a light).</summary>
    SkyDistribution,

    /// <summary>The image's render (trace and readback), with its debug outputs.</summary>
    Render,

    /// <summary>The exposure meter: the incident meter's measurement (M1.10a), or the average meter's render.</summary>
    ExposureMeter,

    /// <summary>The denoised image.</summary>
    Denoise,
}

/// <summary>Which stages ran for one image and which it reused (sidecar schema 9, vista-plan §10 M1.8).</summary>
/// <param name="Built">The stages that ran for this image, in pipeline order.</param>
/// <param name="Reused">
/// The stages whose result an earlier render in the same process made and this image used as it was, in pipeline order. A
/// stage that does not apply (the GPU stages of a dry run, the denoiser when none was asked for) is in neither list.
/// </param>
public sealed record StageInfo(IReadOnlyList<PreviewStage> Built, IReadOnlyList<PreviewStage> Reused);

/// <summary>One render of a watch session (sidecar schema 9, vista-plan §10 M1.8).</summary>
/// <param name="Render">Its number in the session, from 1.</param>
/// <param name="Changes">What changed since the last render, by input and field (e.g. <c>camera: exposureEv</c>); empty for the first.</param>
/// <param name="WaitSeconds">Wall time from the change being seen to this render starting: the wait for the file to settle.</param>
public sealed record WatchInfo(int Render, IReadOnlyList<string> Changes, double WaitSeconds)
{
    /// <summary>Wall time of the render itself, from its start to the sidecar (every stage, the files included).</summary>
    public double RunSeconds { get; init; }
}
