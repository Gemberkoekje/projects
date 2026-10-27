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
/// <see cref="ImageInfo.Dither"/> and <see cref="ImageInfo.BitDepth"/>, how the PNG was quantised. Schema 16 (M1.11d) added, in
/// <see cref="WorldInfo.Erosion"/>, the uplift map's and the rock's settings and the rock's frame (<see cref="ErosionInfo.Strata"/>). Schema 17
/// (M1.12) added <see cref="Materials"/>: whether the ground has its materials by rule, the rules, the grass line and where it came
/// from, each material's roughness and albedo, the detail kinds below the grid and, for a dry run, the shares of the world grid each
/// material covers; <see cref="ImageInfo.Diffuse"/>, how diffuse surfaces scattered; and in <see cref="WorldInfo.Mesh"/>,
/// whether the detail followed the materials. Schema 18 (M1.12a) added <see cref="ImageInfo.Quadrature"/>, which hero wavelengths
/// the render traced and how they weighed into colour, and <see cref="ImageInfo.Wavelengths"/>, the table's span; each material's
/// albedo is now its colour through that quadrature. Schema 19 (M1.12b) added, in <see cref="Materials"/>, whether forest grows, the
/// tree line and where it came from, the canopy's detail and, for a dry run, the forest's share; and <see cref="ImageInfo.Canopy"/>,
/// how the canopy scattered. Schema 20 (M1.12c) added <see cref="WorldInfo.Footing"/>, the height of the world's lowest valleys that
/// the auto altitude puts at the place's valley floor, and, in <see cref="SlidersInfo.Weather"/>, the valley floor's altitude the
/// weather was read at and that footing; the erosion's settings gained the strata's regional dip, folds, formations and massive share.
/// Schema 21 (M1.13) added <see cref="WorldInfo.Glaciers"/>, the world's glaciers; in <see cref="Materials"/>, whether the season's snow
/// lay, the snowline, the freezing level and the glaciers' line it was placed by, for a dry run the shares of snow, firn and ice and,
/// for a render with the material and normal outputs, the share of its ground that is white by slope;
/// the erosion's settings gained the glacial carving's and today's glaciers' own. Schema 22 (M1.13a) added the valley haze: its slider in
/// <see cref="SlidersInfo.Values"/>, its layer in <see cref="SlidersInfo.Weather"/>, and in the atmosphere's model its lid, softness,
/// extinction and the haze's unit. Schema 23 (M1.13b) added the mist's slider in <see cref="SlidersInfo.Values"/> and, in
/// <see cref="SlidersInfo.Weather"/>, the valleys' pools (<see cref="PoolsInfo"/>): how they were rendered, what the weather told them,
/// what they added up to and the flat layer the tables carried in their place; the valley haze's layer is now the scene's own
/// valley's pool. Schema 24 (M1.14) added <see cref="AutoCamera"/>: how the auto camera chose the camera, when it did. Schema 25 (M1.14's
/// follow-up) added the detail budget: its limits and each scored framing's share in <see cref="AutoCamera"/> (the limits' frame size is
/// now the final frame's, <c>finalFrameWidth</c>, not the triangles' alone), and <see cref="Detail"/>, the budget over a preview with
/// <c>--debug detail</c>.</para>
/// </summary>
public sealed record Sidecar
{
    /// <summary>The sidecar format version this build writes.</summary>
    public const int CurrentSchema = 25;

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

    /// <summary>How the auto camera chose <see cref="Camera"/> (schema 24, vista-plan §10 M1.14); absent when it did not.</summary>
    public Composition.AutoCameraInfo? AutoCamera { get; init; }

    /// <summary>The auto camera's detail budget over this image (schema 25, <c>--debug detail</c>); absent without it.</summary>
    public Composition.DetailInfo? Detail { get; init; }

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

    /// <summary>The ground's materials (schema 17, vista-plan §10 M1.12).</summary>
    public MaterialsInfo? Materials { get; init; }

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

    /// <summary>How diffuse surfaces scattered (schema 17, vista-plan §10 M1.12).</summary>
    public DiffuseModel Diffuse { get; init; } = DiffuseModel.OrenNayar;

    /// <summary>How the forest's canopy scattered (schema 19, vista-plan §10 M1.12b).</summary>
    public CanopyModel Canopy { get; init; } = CanopyModel.Kernels;

    /// <summary>Which hero wavelengths the render traced and how they weighed into colour (schema 18, vista-plan §10 M1.12a).</summary>
    public Quadrature Quadrature { get; init; } = WavelengthTable.DefaultQuadrature;

    /// <summary>The span of the quadrature's wavelength table (schema 18).</summary>
    public WavelengthsInfo? Wavelengths { get; init; }

    /// <summary>The light sampling a render with <paramref name="settings"/> actually does.</summary>
    public static LightSampling LightSamplingOf(RenderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Atmosphere == AtmosphereMode.Luts ? settings.LightSampling : LightSampling.SunOnly;
    }
}

/// <summary>The span of a render's wavelength table (vista-plan §10 M1.12a).</summary>
/// <param name="Count">Hero wavelengths in the cycle.</param>
/// <param name="FirstNm">The shortest, nm.</param>
/// <param name="LastNm">The longest, nm.</param>
public sealed record WavelengthsInfo(int Count, int FirstNm, int LastNm)
{
    /// <summary>The span of <paramref name="table"/>.</summary>
    public static WavelengthsInfo Of(WavelengthTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return new WavelengthsInfo(WavelengthTable.Count, table.FirstNm, table.LastNm);
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

    /// <summary>
    /// Where the world stands (schema 20, vista-plan §10 M1.12c): the percentile of its ground taken as its lowest valleys, their
    /// height above its height 0 (which the auto altitude puts at the place's valley floor) and its lowest point.
    /// </summary>
    public WorldFooting? Footing { get; init; }

    /// <summary>Today's glaciers (schema 21, vista-plan §10 M1.13): how much of the world they cover and how thick; null without them.</summary>
    public GlacierInfo? Glaciers { get; init; }
}

/// <summary>A world's glaciers (vista-plan §10 M1.13, <see cref="World.Glaciers"/>).</summary>
/// <param name="ElaMetres">Their equilibrium line, m above the world's height 0 (the recipe's own).</param>
/// <param name="Share">The share of the world grid under glacier ice (at least 1 m of it).</param>
/// <param name="VolumeKm3">The ice's volume, km³.</param>
/// <param name="MaxThicknessMetres">The thickest ice, m.</param>
public sealed record GlacierInfo(double ElaMetres, double Share, double VolumeKm3, double MaxThicknessMetres)
{
    /// <summary>The glaciers of <paramref name="erosion"/>, eroded with <paramref name="settings"/>; null when it has none.</summary>
    public static GlacierInfo? Of(World.ErosionResult? erosion, World.ErosionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (erosion?.Ice is not { } ice)
            return null;
        double area = erosion.Surface.CellSize * erosion.Surface.CellSize;
        int covered = 0;
        double volume = 0.0, thickest = 0.0;
        foreach (float metres in ice)
        {
            covered += metres >= 1f ? 1 : 0;
            volume += metres * area;
            thickest = Math.Max(thickest, metres);
        }
        return new GlacierInfo(settings.GlacierElaMetres, covered / (double)ice.Length, volume * 1e-9, thickest);
    }
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

/// <summary>The ground's materials (schema 17, vista-plan §10 M1.12).</summary>
public sealed record MaterialsInfo
{
    /// <summary>True when the rules put rock, scree, soil and grass on the ground; false for M0's single material (<c>--materials off</c>).</summary>
    public required bool ByRule { get; init; }

    /// <summary>Each material the render shaded: its key, tier, roughness and linear-sRGB albedo.</summary>
    public required IReadOnlyList<MaterialEntry> Table { get; init; }

    /// <summary>The rules' thresholds; null without them.</summary>
    public MaterialRules? Rules { get; init; }

    /// <summary>The grass line above sea level, m; null without the rules.</summary>
    public double? GrassLineMetres { get; init; }

    /// <summary>Where the grass line came from.</summary>
    public string? GrassLineFrom { get; init; }

    /// <summary>1 when the pole lies toward +z (north), −1 when toward −z.</summary>
    public int Poleward { get; init; }

    /// <summary>Whether forest grew below the tree line (schema 19, vista-plan §10 M1.12b; <c>--forest off</c>: none).</summary>
    public bool Forest { get; init; }

    /// <summary>The tree line above sea level, m (schema 19); null without the rules.</summary>
    public double? TreeLineMetres { get; init; }

    /// <summary>Where the tree line came from (schema 19).</summary>
    public string? TreeLineFrom { get; init; }

    /// <summary>The detail kinds below the grid and how fast their weights change; null without the rules or without detail.</summary>
    public MaterialDetailInfo? Detail { get; init; }

    /// <summary>Whether the season's snow lay (schema 21, vista-plan §10 M1.13; <c>--snow off</c>: none).</summary>
    public bool Snow { get; init; }

    /// <summary>The snowline the snow was placed by, m above sea level (schema 21, the snow slider's); null without the rules.</summary>
    public double? SnowlineMetres { get; init; }

    /// <summary>The freezing level below which the snow was wet, m above sea level (schema 21); null without the rules.</summary>
    public double? FreezingLevelMetres { get; init; }

    /// <summary>The glaciers' equilibrium line above sea level at the scene's altitude, m (schema 21); null without glaciers.</summary>
    public double? GlacierLineMetres { get; init; }

    /// <summary>
    /// For a render with the material and normal outputs, the share of its ground that is snow, firn or ice by slope (schema 21,
    /// <see cref="SnowBySlope"/>); null otherwise.
    /// </summary>
    public IReadOnlyList<SlopeBand>? SnowBySlope { get; init; }

    /// <summary>For a dry run, the shares of the world grid's samples each material covers; null otherwise.</summary>
    public MaterialShares? Shares { get; init; }
}

/// <summary>One material as a render shaded it.</summary>
/// <param name="Key">Its key.</param>
/// <param name="Tier">Where its spectrum came from.</param>
/// <param name="Roughness">Its roughness for the rough diffuse surface.</param>
/// <param name="Albedo">Its linear-sRGB albedo, R, G and B.</param>
public sealed record MaterialEntry(string Key, DataTier Tier, float Roughness, float[] Albedo);

/// <summary>The detail kinds below the world grid (schema 17): each one's amplitude and how fast its weight changes.</summary>
/// <param name="RockAmplitudeMetres">The most the rock detail moves a point, m.</param>
/// <param name="ScreeAmplitudeMetres">The most the scree detail does, m.</param>
/// <param name="SmoothAmplitudeMetres">The most the smooth detail does, m.</param>
/// <param name="RockGradientPerMetre">The steepest the rock detail's weight changes, per metre.</param>
/// <param name="ScreeGradientPerMetre">The scree's.</param>
/// <param name="SmoothGradientPerMetre">The smooth detail's.</param>
public sealed record MaterialDetailInfo(double RockAmplitudeMetres, double ScreeAmplitudeMetres, double SmoothAmplitudeMetres,
    double RockGradientPerMetre, double ScreeGradientPerMetre, double SmoothGradientPerMetre)
{
    /// <summary>The most the canopy's crowns move a point, m (schema 19); 0 without forest.</summary>
    public double CanopyAmplitudeMetres { get; init; }

    /// <summary>The steepest the canopy detail's weight changes, per metre (schema 19).</summary>
    public double CanopyGradientPerMetre { get; init; }
}

/// <summary>The shares of the world grid's samples each material covers (their weights summed), and the mean wetness.</summary>
/// <param name="RockHard">Hard rock (rock on beds of hardness 0.5 and up).</param>
/// <param name="RockSoft">Soft rock.</param>
/// <param name="Scree">Scree.</param>
/// <param name="Soil">Soil.</param>
/// <param name="Grass">Grass.</param>
/// <param name="Wet">The mean wetness.</param>
public sealed record MaterialShares(double RockHard, double RockSoft, double Scree, double Soil, double Grass, double Wet)
{
    /// <summary>Forest (schema 19, vista-plan §10 M1.12b).</summary>
    public double Forest { get; init; }

    /// <summary>The season's snow (schema 21, vista-plan §10 M1.13).</summary>
    public double Snow { get; init; }

    /// <summary>Glacier firn (schema 21).</summary>
    public double Firn { get; init; }

    /// <summary>Bare glacier ice (schema 21).</summary>
    public double Ice { get; init; }
}
