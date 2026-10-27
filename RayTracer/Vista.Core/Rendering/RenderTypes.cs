using RayTracer;
using Vista.Astronomy;
using Vista.Atmosphere;
using Vista.Recipes;
using Vista.Scene;
using Vista.World;

namespace Vista.Rendering;

/// <summary>Quality and size of one render. A preview differs from a final only in its pixel count (vista-plan §7.4).</summary>
public sealed record RenderSettings
{
    /// <summary>Frame width in pixels: the camera's image, of which <see cref="Window"/> may render only a part.</summary>
    public required int Width { get; init; }

    /// <summary>Frame height in pixels.</summary>
    public required int Height { get; init; }

    /// <summary>
    /// The loupe (vista-plan §7.4): the part of the frame to render, in frame pixels. Each pixel keeps the
    /// frame's camera ray and random keys, so a window is exactly a crop of the full render. Empty (the
    /// default) renders the whole frame.
    /// </summary>
    public PixelWindow Window { get; init; }

    /// <summary>The pixels actually rendered: <see cref="Window"/>, or the whole frame when it is empty.</summary>
    public PixelWindow Rendered => Window.IsEmpty ? PixelWindow.Full(Width, Height) : Window;

    /// <summary>Samples per pixel (fixed in M0; a noise target comes with adaptive sampling).</summary>
    public int SamplesPerPixel { get; init; } = 64;

    /// <summary>Maximum path length in bounces.</summary>
    public int MaxBounces { get; init; } = 6;

    /// <summary>How the render is split into TDR-safe dispatches.</summary>
    public BatchPlannerOptions Batching { get; init; } = new();

    /// <summary>How the air is evaluated: the LUTs (the default) or the slow, unbiased reference (vista-plan §10 M1.3).</summary>
    public AtmosphereMode Atmosphere { get; init; } = AtmosphereMode.Luts;

    /// <summary>
    /// True for the small render the average meter reads the PNG's exposure from (<see cref="Output.AutoExposure"/>), not an
    /// image anyone sees. A renderer renders it like any other; it only tells the two apart in statistics and tests.
    /// </summary>
    public bool ExposureMeter { get; init; }

    /// <summary>Which lights a surface samples, and how the estimates are combined (vista-plan §10 M1.5).</summary>
    public LightSampling LightSampling { get; init; } = LightSampling.Mis;

    /// <summary>
    /// The bounce from which Russian roulette may end a path (0: from the first surface); paths shorter than this always
    /// continue to <see cref="MaxBounces"/>.
    /// </summary>
    public int RussianRouletteStart { get; init; } = DefaultRussianRouletteStart;

    /// <summary>
    /// The <see cref="RussianRouletteStart"/> every image uses, re-measured with M1.5's light sampling (vista-plan §10 M1.5): from
    /// bounce 2 instead of M1.4's 3, 1–13% more efficient for under 1% more variance at equal samples; from bounce 1 the
    /// second surface, which no longer samples the sky, cost up to twice the efficiency.
    /// </summary>
    public const int DefaultRussianRouletteStart = 2;

    /// <summary>The latest bounce <see cref="RussianRouletteStart"/> may name.</summary>
    public const int MaxRussianRouletteStart = 64;

    /// <summary>
    /// Which set of random numbers the render draws: 0 for every image anyone sees. Another value gives each pixel-sample
    /// an independent stream, so a second render of the same view is a statistically independent estimate — what a
    /// per-pixel comparison of two estimators needs (vista-plan §10 M1.5).
    /// </summary>
    public uint RandomStream { get; init; }

    /// <summary>
    /// How diffuse surfaces scatter (vista-plan §10 M1.12): rough, Oren–Nayar-style, each material at its own roughness, so a low sun
    /// behind the camera lights them back toward it; or Lambert's, the same in every direction (every material's roughness 0).
    /// </summary>
    public DiffuseModel Diffuse { get; init; } = DiffuseModel.OrenNayar;

    /// <summary>
    /// How the forest's canopy scatters (vista-plan §10 M1.12b): by the kernels of a canopy's volume and its crowns' shadows, or as
    /// rough diffuse ground at the forest's own roughness. Lambert's diffuse (<see cref="Diffuse"/>) shades it as Lambert's too.
    /// </summary>
    public CanopyModel Canopy { get; init; } = CanopyModel.Kernels;
}

/// <summary>How the integrator shades the forest's canopy (vista-plan §10 M1.12b).</summary>
public enum CanopyModel
{
    /// <summary>Not chosen.</summary>
    None,
    /// <summary>
    /// The kernel-driven BRDF of MODIS (RossThick–LiSparse-Reciprocal; Wanner, Li and Strahler 1995): the isotropic part, the
    /// volume scattering of the needles, and the crowns' shadows on the gaps between them, dark looking toward the sun and bright in
    /// the hot spot with the sun behind the viewer.
    /// </summary>
    Kernels,
    /// <summary>M1.12's rough diffuse surface (<see cref="DiffuseModel.OrenNayar"/>) at the forest's own roughness.</summary>
    Rough,
}

/// <summary>How the integrator's diffuse surfaces scatter (vista-plan §10 M1.12).</summary>
public enum DiffuseModel
{
    /// <summary>Not chosen.</summary>
    None,
    /// <summary>Lambert's: the same radiance in every direction.</summary>
    Lambert,
    /// <summary>
    /// Oren and Nayar's rough diffuse surface in Fujii's form (<c>f = ρ·A·(1 + σ·s/t)</c>, Fujii 2012), each material at its
    /// <see cref="Scene.VistaMaterial.Roughness"/>: brighter toward the light behind the viewer, flatter across a lit face.
    /// </summary>
    OrenNayar,
}

/// <summary>Which lights the integrator samples at a surface (vista-plan §5.1, §10 M1.5).</summary>
public enum LightSampling
{
    /// <summary>Not chosen.</summary>
    None,
    /// <summary>
    /// Every surface samples the sun's disk and a path's first surface also samples the sky (from <see cref="SkyDistribution"/>),
    /// and the bounce's own direction counts too: multiple importance sampling (the power heuristic) weighs them.
    /// </summary>
    Mis,
    /// <summary>
    /// M1.4's estimator, kept for comparison: only the sun is sampled as a light; the sky is reached by the cosine-weighted
    /// bounce alone, and a bounce that hits the sun's disk counts nothing (next-event estimation already took it). The
    /// reference atmosphere always works this way.
    /// </summary>
    SunOnly,
}

/// <summary>How the integrator evaluates the atmosphere (vista-plan §10 M1.3).</summary>
public enum AtmosphereMode
{
    /// <summary>Not chosen.</summary>
    None,
    /// <summary>
    /// Precomputed LUTs (Hillaire 2020): the sky-view LUT for rays that leave the terrain, the columns LUT for the sun's
    /// transmittance, and a short march through both and the scattering orders along every segment that ends on the
    /// terrain (aerial perspective, M1.4).
    /// </summary>
    Luts,
    /// <summary>
    /// The air path-traced as a medium (delta tracking, next-event estimation to the sun): slow and unbiased, the check on
    /// the LUTs. Like them it ignores the terrain inside the air (vista-plan §10 M1.4).
    /// </summary>
    Reference,
    /// <summary>
    /// <see cref="Reference"/> with the terrain's shadow on the sunlight the air scatters once: the light the LUTs cannot
    /// know about (air in a mountain's shadow is still lit for them), measured (vista-plan §10 M1.4). Later scattering keeps
    /// ignoring the terrain, as in the reference.
    /// </summary>
    ReferenceShadowed,
}

/// <summary>
/// Per-pixel auxiliary outputs the renderer can produce for the debug images (vista-plan §7.4). Albedo and
/// normal average over every sample of the pixel (what a denoiser wants); depth and material id cannot be
/// averaged, so they come from the pixel's sample 0.
/// </summary>
public enum Aov
{
    /// <summary>Linear-sRGB albedo of the first hit averaged over the pixel's samples, 3 floats per pixel; sky samples count as 0.</summary>
    Albedo,
    /// <summary>World-space first-hit normal averaged over the pixel's samples and renormalised, 3 floats per pixel; 0 where every sample saw sky.</summary>
    Normal,
    /// <summary>Distance along sample 0's primary ray in metres, 1 float per pixel; +∞ for sky.</summary>
    Depth,
    /// <summary><see cref="VistaMaterial.Id"/> of sample 0's first hit as a float, 1 per pixel; −1 for sky.</summary>
    MaterialId,
    /// <summary>Variance of the pixel's mean luminance (sample variance ÷ samples), 1 float per pixel.</summary>
    Variance,
}

/// <summary>Layout facts about <see cref="Aov"/> buffers.</summary>
public static class AovLayout
{
    /// <summary>Floats per pixel for <paramref name="aov"/>.</summary>
    public static int Channels(Aov aov) => aov is Aov.Albedo or Aov.Normal ? 3 : 1;
}

/// <summary>Everything the integrator needs to render one view of one world.</summary>
/// <param name="Recipe">The recipe the world came from.</param>
/// <param name="Seed">The world seed.</param>
/// <param name="Camera">The view.</param>
/// <param name="Terrain">The terrain height field (for collision checks and future height-field tracing).</param>
/// <param name="Mesh">
/// The terrain mesh to build the acceleration structure from: since M1.9 the view's own (<see cref="ViewMesher"/>), in the frame
/// bent onto the Earth around the camera, which stands at <c>(0, height, 0)</c> in it.
/// </param>
/// <param name="Sun">Where the sun is (<see cref="Astronomy.Almanac"/>).</param>
/// <param name="Materials">The material table; ids index it.</param>
/// <param name="Air">The atmosphere above the scene.</param>
/// <param name="GroundAltitudeMetres">
/// Altitude above sea level of world <c>y = 0</c>: the scene's base altitude, where the ground sphere of the
/// atmosphere sits (<see cref="AtmosphereFrame"/>): the recipe's altitude, else the place's valley floor
/// (<see cref="Places.BaseAltitude"/>).
/// </param>
public sealed record VistaScene(
    Recipe Recipe,
    ulong Seed,
    CameraSpec Camera,
    HeightField Terrain,
    TerrainMesh Mesh,
    SunPosition Sun,
    IReadOnlyList<VistaMaterial> Materials,
    AtmosphereModel Air,
    double GroundAltitudeMetres)
{
    /// <summary>
    /// What the ground's materials read of the place (vista-plan §10 M1.12): its grass line and which way its pole lies. The world's
    /// material field travels with the mesh (<see cref="TerrainMesh.Materials"/>).
    /// </summary>
    public GroundCover Ground { get; init; } = GroundCover.Default;

    /// <summary>
    /// Which hero wavelengths the render traces and how they weigh into colour (vista-plan §10 M1.12a): Vista's own bins, or the
    /// engine's table for <c>main</c>'s images before M1.12a. Every spectral table of the render is laid out on it.
    /// </summary>
    public Quadrature Quadrature { get; init; } = WavelengthTable.DefaultQuadrature;

    /// <summary>The wavelength table of <see cref="Quadrature"/>.</summary>
    public WavelengthTable Wavelengths() => WavelengthTable.For(Quadrature);

    /// <summary>
    /// The valleys' pools in the low air (vista-plan §10 M1.13b): each valley's haze and mist in three dimensions, which the march takes
    /// instead of the air's flat valley haze layer (the tables keep that, as the pools' altitude average); null for none.
    /// </summary>
    public LowAir? LowAir { get; init; }

    /// <summary>The atmosphere frame of this view: the camera's altitude and the terrain's span set the sky-view slices.</summary>
    public AtmosphereFrame AirFrame() => FrameFor(Air, GroundAltitudeMetres, Sun, Camera, Mesh);

    /// <summary>
    /// The atmosphere frame of a view of <paramref name="mesh"/> from <paramref name="camera"/>: the camera's altitude and the
    /// mesh's heights above the ground sphere set the sky-view slices, and its farthest vertex from the camera its reach (since
    /// M1.9 the far field's, out past the horizon).
    /// </summary>
    public static AtmosphereFrame FrameFor(AtmosphereModel air, double groundAltitude, SunPosition sun, CameraSpec camera, TerrainMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(air);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(mesh);
        return AtmosphereFrame.For(air, groundAltitude, sun.ToSun, camera.Position.Y, mesh.MinHeight, mesh.MaxHeight, mesh.ReachMetres);
    }
}

/// <summary>What a renderer returns: the <see cref="RenderSettings.Rendered"/> window of the frame.</summary>
/// <param name="Width">Width of the rendered window.</param>
/// <param name="Height">Height of the rendered window.</param>
/// <param name="Xyz">Mean CIE XYZ per pixel, 3 floats per pixel, the window's top row first.</param>
/// <param name="Aovs">The requested auxiliary buffers (see <see cref="Aov"/> for each layout).</param>
/// <param name="Stats">Ray counts and timings.</param>
public sealed record RenderOutput(
    int Width,
    int Height,
    float[] Xyz,
    IReadOnlyDictionary<Aov, float[]> Aovs,
    RenderStats Stats);

/// <summary>
/// The seam between the pure pipeline and a GPU backend: <c>D3D12VistaRenderer</c> in Vista.App (the offline
/// integrator on the RTX 3070); tests drive the pipeline with a fake.
///
/// <para>A renderer keeps what it built between renders and rebuilds only what a scene changed (vista-plan §10 M1.8). Each
/// render reports what it did build in <see cref="RenderStats.StageSeconds"/>, under the <see cref="RenderStageKeys"/>, so a
/// session can say which stages a watch render reused.</para>
/// </summary>
public interface IVistaRenderer
{
    /// <summary>The adapter the renderer runs on, for the sidecar.</summary>
    string DeviceName { get; }

    /// <summary>
    /// Renders the <see cref="RenderSettings.Rendered"/> window of <paramref name="scene"/>, issuing dispatches
    /// from <paramref name="planner"/> (made for that window's size; its rows are window rows) and reporting
    /// each one's GPU time back to it, and returns the requested <paramref name="aovs"/>.
    /// </summary>
    RenderOutput Render(VistaScene scene, RenderSettings settings, IReadOnlySet<Aov> aovs, BatchPlanner planner);

    /// <summary>
    /// Measures the light on the incident meter's card for <paramref name="scene"/> (vista-plan §10 M1.10a,
    /// <see cref="Output.IncidentMeter"/>) from the scene's atmosphere LUTs, building whichever of them the scene changed.
    /// </summary>
    IncidentLight MeasureIncidentLight(VistaScene scene);
}

/// <summary>
/// The light on the incident meter's grey card (vista-plan §10 M1.10a, <see cref="Output.IncidentMeter"/>) as a renderer measures
/// it: the spectral irradiance the sun and the sky put on the card at each hero wavelength (cycle order), in W·m⁻²·nm⁻¹.
/// </summary>
/// <param name="Sun">The sun's irradiance on the card, one value per hero wavelength.</param>
/// <param name="Sky">The sky's over the card's hemisphere (below the horizon, the ground sphere the sky view holds), per hero wavelength.</param>
public sealed record IncidentLight(double[] Sun, double[] Sky)
{
    /// <summary>The renderer stages the measurement built, in seconds (<see cref="RenderStageKeys"/>); a stage it reused is absent.</summary>
    public SortedDictionary<string, double> StageSeconds { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>
/// The <see cref="RenderStats.StageSeconds"/> keys a renderer writes when a render built that stage, and only then (vista-plan
/// §10 M1.8): a render that reused the device, the acceleration structure or a table leaves its key out.
/// </summary>
public static class RenderStageKeys
{
    /// <summary>Creating the device (the first render of a renderer).</summary>
    public const string DeviceInit = "deviceInit";

    /// <summary>Compiling the shaders the renderer always needs (the first render of a renderer).</summary>
    public const string ShaderCompile = "shaderCompile";

    /// <summary>Compiling a shader variant on its first use (the reference atmosphere).</summary>
    public const string ReferenceShaderCompile = "referenceShaderCompile";

    /// <summary>Compiling the integrator's variant with the valleys' pools on its first use (M1.13b).</summary>
    public const string LowAirShaderCompile = "lowAirShaderCompile";

    /// <summary>Uploading a new mesh.</summary>
    public const string MeshUpload = "meshUpload";

    /// <summary>Building the acceleration structures of a new mesh.</summary>
    public const string AccelerationBuild = "accelBuild";

    /// <summary>GPU seconds building the air model's wavelength tables and the columns LUT (a new model).</summary>
    public const string AtmosphereTables = "atmosphereTablesGpu";

    /// <summary>GPU seconds building the scattering orders and the sky's ground irradiance (a new model or base altitude).</summary>
    public const string ScatteringOrders = "scatteringOrdersGpu";

    /// <summary>GPU seconds building the sky-view LUT (a new atmosphere frame: the sun, the camera's altitude, the terrain).</summary>
    public const string SkyView = "skyViewGpu";

    /// <summary>GPU seconds measuring the sky distribution (a new frame, when the sky is sampled as a light).</summary>
    public const string SkyDistribution = "skyDistributionGpu";

    /// <summary>GPU seconds measuring the light on the incident meter's card (every measurement).</summary>
    public const string IncidentMeter = "incidentMeterGpu";

    /// <summary>GPU seconds building the valleys' pools' sunlight volumes (vista-plan §10 M1.13b: new pools, terrain or sun).</summary>
    public const string SunlightVolume = "sunlightVolumeGpu";
}

/// <summary>
/// Ray counts and timings of one render (vista-plan §7.2: <c>time ≈ pixels × spp × rays per sample ÷ rays
/// per second</c>, both measured). <see cref="RaysTraced"/> counts every ray the integrator traces —
/// camera, bounce and shadow rays — via the shader's ray counter.
/// </summary>
public sealed record RenderStats
{
    /// <summary>All rays traced.</summary>
    public ulong RaysTraced { get; init; }

    /// <summary>Pixel-samples traced.</summary>
    public long PixelSamples { get; init; }

    /// <summary>GPU dispatches issued.</summary>
    public int Dispatches { get; init; }

    /// <summary>GPU time spent tracing, in seconds.</summary>
    public double TraceSeconds { get; init; }

    /// <summary>GPU time of the longest single dispatch, in seconds — the number to hold against the TDR.</summary>
    public double LongestDispatchSeconds { get; init; }

    /// <summary>The highest cost any dispatch measured, in nanoseconds per pixel-sample.</summary>
    public double MaxCostNsPerPixelSample { get; init; }

    /// <summary>
    /// Renderer stages (e.g. device init, acceleration build, readback) in seconds. A stage the render reused is absent
    /// (<see cref="RenderStageKeys"/>).
    /// </summary>
    public SortedDictionary<string, double> StageSeconds { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Bytes the terrain's bottom-level acceleration structure takes on the device (0 when not measured).</summary>
    public long AccelerationStructureBytes { get; init; }

    /// <summary>
    /// Video memory the whole process used once the scene was on the device, in bytes (DXGI's local segment; 0 when not
    /// measured): the number the 8 GB budget (vista-plan §7.1) is about.
    /// </summary>
    public long VideoMemoryBytes { get; init; }

    /// <summary>Mean rays per pixel-sample.</summary>
    public double RaysPerSample() => PixelSamples > 0 ? (double)RaysTraced / PixelSamples : 0.0;

    /// <summary>Rays per second of trace time.</summary>
    public double RaysPerSecond() => TraceSeconds > 0.0 ? RaysTraced / TraceSeconds : 0.0;
}
