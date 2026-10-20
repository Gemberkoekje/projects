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
    /// True for the small render the PNG's exposure is metered from (<see cref="Output.AutoExposure"/>), not an image
    /// anyone sees. A renderer renders it like any other; it only tells the two apart in statistics and tests.
    /// </summary>
    public bool ExposureMeter { get; init; }
}

/// <summary>How the integrator evaluates the atmosphere (vista-plan §10 M1.3).</summary>
public enum AtmosphereMode
{
    /// <summary>Not chosen.</summary>
    None,
    /// <summary>Precomputed LUTs (Hillaire 2020): the sky-view LUT for rays that leave the terrain, the columns LUT for the sun's transmittance.</summary>
    Luts,
    /// <summary>The air path-traced as a medium (delta tracking, next-event estimation to the sun): slow and unbiased, the check on the LUTs.</summary>
    Reference,
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
/// <param name="Mesh">The terrain mesh to build the acceleration structure from.</param>
/// <param name="Sun">Where the sun is (<see cref="Astronomy.Almanac"/>).</param>
/// <param name="Materials">The material table; ids index it.</param>
/// <param name="Air">The atmosphere above the scene.</param>
/// <param name="GroundAltitudeMetres">
/// Altitude above sea level of world <c>y = 0</c>: the scene's base altitude, where the ground sphere of the
/// atmosphere sits (<see cref="AtmosphereFrame"/>). The place's valley floor until M1.4's altitude control.
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
    /// <summary>The atmosphere frame of this view: the camera's altitude and the terrain's span set the sky-view slices.</summary>
    public AtmosphereFrame AirFrame() =>
        AtmosphereFrame.For(Air, GroundAltitudeMetres, Sun.ToSun, Camera.Position.Y, Terrain.MinHeight, Terrain.MaxHeight);
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

    /// <summary>Renderer stages (e.g. device init, acceleration build, readback) in seconds.</summary>
    public SortedDictionary<string, double> StageSeconds { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Mean rays per pixel-sample.</summary>
    public double RaysPerSample() => PixelSamples > 0 ? (double)RaysTraced / PixelSamples : 0.0;

    /// <summary>Rays per second of trace time.</summary>
    public double RaysPerSecond() => TraceSeconds > 0.0 ? RaysTraced / TraceSeconds : 0.0;
}
