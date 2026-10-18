using RayTracer;
using Vista.Recipes;
using Vista.Scene;
using Vista.World;

namespace Vista.Rendering;

/// <summary>Quality and size of one render. A preview differs from a final only in its pixel count (vista-plan §7.4).</summary>
public sealed record RenderSettings
{
    /// <summary>Image width in pixels.</summary>
    public required int Width { get; init; }

    /// <summary>Image height in pixels.</summary>
    public required int Height { get; init; }

    /// <summary>Samples per pixel (fixed in M0; a noise target comes with adaptive sampling).</summary>
    public int SamplesPerPixel { get; init; } = 64;

    /// <summary>Maximum path length in bounces.</summary>
    public int MaxBounces { get; init; } = 6;

    /// <summary>How the render is split into TDR-safe dispatches.</summary>
    public BatchPlannerOptions Batching { get; init; } = new();
}

/// <summary>Per-pixel auxiliary outputs the renderer can produce for the debug images (vista-plan §7.4).</summary>
public enum Aov
{
    /// <summary>Linear-sRGB albedo of the first hit, 3 floats per pixel; 0 for sky.</summary>
    Albedo,
    /// <summary>World-space unit normal of the first hit, 3 floats per pixel; 0 for sky.</summary>
    Normal,
    /// <summary>Distance along the primary ray in metres, 1 float per pixel; +∞ for sky.</summary>
    Depth,
    /// <summary><see cref="VistaMaterial.Id"/> of the first hit as a float, 1 per pixel; −1 for sky.</summary>
    MaterialId,
    /// <summary>Variance of the pixel's luminance estimate, 1 float per pixel.</summary>
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
/// <param name="Sun">The M0 sun; the sky is the engine's Rayleigh <c>Sky</c> built from it.</param>
/// <param name="Materials">The material table; ids index it.</param>
public sealed record VistaScene(
    Recipe Recipe,
    ulong Seed,
    CameraSpec Camera,
    HeightField Terrain,
    TerrainMesh Mesh,
    SunState Sun,
    IReadOnlyList<VistaMaterial> Materials);

/// <summary>What a renderer returns.</summary>
/// <param name="Width">Image width.</param>
/// <param name="Height">Image height.</param>
/// <param name="Xyz">Mean CIE XYZ per pixel, 3 floats per pixel, row 0 at the top.</param>
/// <param name="Aovs">The requested auxiliary buffers (see <see cref="Aov"/> for each layout).</param>
/// <param name="Stats">Ray counts and timings.</param>
public sealed record RenderOutput(
    int Width,
    int Height,
    float[] Xyz,
    IReadOnlyDictionary<Aov, float[]> Aovs,
    RenderStats Stats);

/// <summary>
/// The seam between the pure pipeline and a GPU backend. The D3D12 implementation (offline integrator v0
/// on the RTX 3070) is open M0 work; tests drive the pipeline with a fake.
/// </summary>
public interface IVistaRenderer
{
    /// <summary>The adapter the renderer runs on, for the sidecar.</summary>
    string DeviceName { get; }

    /// <summary>
    /// Renders <paramref name="scene"/>, issuing dispatches from <paramref name="planner"/> and reporting
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

    /// <summary>Renderer stages (e.g. device init, acceleration build, readback) in seconds.</summary>
    public SortedDictionary<string, double> StageSeconds { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Mean rays per pixel-sample.</summary>
    public double RaysPerSample() => PixelSamples > 0 ? (double)RaysTraced / PixelSamples : 0.0;

    /// <summary>Rays per second of trace time.</summary>
    public double RaysPerSecond() => TraceSeconds > 0.0 ? RaysTraced / TraceSeconds : 0.0;
}
