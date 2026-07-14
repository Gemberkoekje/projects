using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Channels;

namespace RayTracer;

public partial class JobSystem
{
    private WavelengthLookup WavelengthLookup { get; init; } = new();

    private readonly RenderBuffers _buffers;
    private readonly DiffuseIrradianceCache _irradianceCache;
    private const int DiffuseCacheMaxAgeFrames = 240;

    // Public buffer accessors for backward compatibility
    public Vector3[] AccumXYZ => _buffers.AccumXYZ;
    public uint[] SampleCount => _buffers.SampleCount;
    public long[] WavelengthCounter => _buffers.WavelengthCounter;
    public bool[] LastHit => _buffers.LastHit;

    // Internal buffer accessors for partial class files
    internal Vector3[] _hitPointWorld => _buffers.HitPointWorld;
    internal float[] _lumaM2 => _buffers.LumaM2;
    internal float[] _lumaVariance => _buffers.LumaVariance;
    internal float[] _lumaDirectM2 => _buffers.LumaDirectM2;
    internal float[] _lumaIndirectM2 => _buffers.LumaIndirectM2;
    internal float[] _lumaDirectVariance => _buffers.LumaDirectVariance;
    internal float[] _lumaIndirectVariance => _buffers.LumaIndirectVariance;
    internal float[] _historyWeight => _buffers.HistoryWeight;
    internal byte[] _historyRejected => _buffers.HistoryRejected;
    internal float[] _clampAmount => _buffers.ClampAmount;
    internal bool[] _clampHitFrame => _buffers.ClampHitFrame;
    internal float[] _depthDistance => _buffers.DepthDistance;
    internal float[] _albedoScalar => _buffers.AlbedoScalar;
    internal Vector3[] _normalWorld => _buffers.NormalWorld;
    internal Vector3[] _directLightingXYZ => _buffers.DirectLightingXYZ;
    internal Vector3[] _indirectLightingXYZ => _buffers.IndirectLightingXYZ;
    internal Vector3[] _emissiveLightingXYZ => _buffers.EmissiveLightingXYZ;
    internal Vector3[] _bounce0XYZ => _buffers.Bounce0XYZ;
    internal Vector3[] _bounce1XYZ => _buffers.Bounce1XYZ;
    internal Vector3[] _bounce2PlusXYZ => _buffers.Bounce2PlusXYZ;
    internal float[] _diffCurrentVsAccum => _buffers.DiffCurrentVsAccum;
    internal float[] _diffUnfilteredVsFiltered => _buffers.DiffUnfilteredVsFiltered;
    internal float[] _diffReprojectedVsCurrent => _buffers.DiffReprojectedVsCurrent;
    internal int[] _lastUpdatedFrame => _buffers.LastUpdatedFrame;
    internal Vector3[] _taaNextXYZ => _buffers.TaaNextXYZ;
    internal Vector3[] _taaNextHitPoint => _buffers.TaaNextHitPoint;
    internal bool[] _taaNextValid => _buffers.TaaNextValid;

    // Convenience aliases used by TraceCore and debug rendering within this partial class
    private Vector3[] HitPointWorld => _buffers.HitPointWorld;
    private float[] LumaM2 => _buffers.LumaM2;
    private float[] LumaVariance => _buffers.LumaVariance;
    private float[] LumaDirectM2 => _buffers.LumaDirectM2;
    private float[] LumaIndirectM2 => _buffers.LumaIndirectM2;
    private float[] LumaDirectVariance => _buffers.LumaDirectVariance;
    private float[] LumaIndirectVariance => _buffers.LumaIndirectVariance;
    private float[] HistoryWeight => _buffers.HistoryWeight;
    private byte[] HistoryRejected => _buffers.HistoryRejected;
    private float[] ClampAmount => _buffers.ClampAmount;
    private bool[] ClampHitFrame => _buffers.ClampHitFrame;
    private float[] DepthDistance => _buffers.DepthDistance;
    private float[] AlbedoScalar => _buffers.AlbedoScalar;
    private Vector3[] NormalWorld => _buffers.NormalWorld;
    private Vector3[] DirectLightingXYZ => _buffers.DirectLightingXYZ;
    private Vector3[] IndirectLightingXYZ => _buffers.IndirectLightingXYZ;
    private Vector3[] EmissiveLightingXYZ => _buffers.EmissiveLightingXYZ;
    private Vector3[] Bounce0XYZ => _buffers.Bounce0XYZ;
    private Vector3[] Bounce1XYZ => _buffers.Bounce1XYZ;
    private Vector3[] Bounce2PlusXYZ => _buffers.Bounce2PlusXYZ;
    private float[] DiffCurrentVsAccum => _buffers.DiffCurrentVsAccum;
    private float[] DiffUnfilteredVsFiltered => _buffers.DiffUnfilteredVsFiltered;
    private float[] DiffReprojectedVsCurrent => _buffers.DiffReprojectedVsCurrent;
    private int[] LastUpdatedFrame => _buffers.LastUpdatedFrame;

    private readonly Vector3 _sampleClampVec;

    public int Width { get; init; }
    public int Height { get; init; }
    public int TileSize { get; }

    public int SppPerJob { get; }

    public uint MaxSampleCount { get; }

    public Channel<Tile> Jobs { get; init; } = Channel.CreateBounded<Tile>(1000);

    public Tracable[] Scene { get; init; }

    private readonly BVH _bvh;

    private volatile int _checkerPhase;

    private readonly Light[] _lights;

    /// <summary>Base ambient illumination applied to every hit point.</summary>
    private const float AmbientLevel = 0.01f;

    /// <summary>
    /// Intensity of each point light. Scaled so that the floor
    /// directly below a ceiling light receives roughly unit illumination.
    /// </summary>
    private const float LightIntensity = 1.5f;

    public Camera Camera { get; set; }

    private long _totalRays;
    private long _totalTileCompletions;

    public long TotalRays => _totalRays;

    /// <summary>
    /// Number of individual tile render completions since startup.
    /// Divide by <see cref="TotalTiles"/> to get full-screen passes.
    /// </summary>
    public long TotalTileCompletions => _totalTileCompletions;

    /// <summary>
    /// Total number of tiles that cover the full screen.
    /// </summary>
    public int TotalTiles { get; private set; }

    public int Stride { get; init; } = 0;

    public byte[] DisplayBuffer { get; init; }

    /// <summary>
    /// Set by the render loop when the camera is actively moving.
    /// Workers read this to decide whether to apply spatial denoising.
    /// </summary>
    public bool IsMoving { get; set; }

    /// <summary>
    /// Set by the render loop when the camera is actively turning in place.
    /// Used to temporarily disable checkerboard skipping during turns.
    /// </summary>
    public bool IsTurning { get; set; }

    /// <summary>
    /// During motion, sample counts are capped to this value so new
    /// samples quickly replace stale data. Kept high enough for
    /// reasonable spectral coverage; the 3×3 spatial filter in
    /// <see cref="Render"/> provides the remaining noise reduction.
    /// </summary>
    public uint MotionSampleCap { get; }

    /// <summary>
    /// When true, each sample is jittered to a random sub-pixel position
    /// instead of the pixel centre. Converts aliasing into noise that
    /// the accumulator averages away.
    /// </summary>
    public bool SubPixelJitter { get; }

    /// <summary>
    /// Radius of the spatial averaging filter applied during camera
    /// motion.  0 = disabled, 1 = 3×3, 2 = 5×5.
    /// </summary>
    public int FilterRadius { get; }

    /// <summary>
    /// When true the spatial filter uses luminance-based bilateral
    /// weighting: neighbours with similar brightness are averaged
    /// (denoised) while large luminance jumps (edges) are preserved.
    /// </summary>
    public bool EdgeAwareFilter { get; }

    /// <summary>
    /// When true, only half the pixels are traced per tile during motion,
    /// alternating in a checkerboard pattern each soft-reset. The spatial
    /// filter fills the gaps, effectively doubling throughput while moving.
    /// </summary>
    public bool CheckerboardMotion { get; }

    /// <summary>
    /// Blend factor for exponential moving average during camera motion.
    /// 0 = disabled (uses running mean). Values around 0.2 give stable,
    /// responsive results. Ignored when the camera is stationary.
    /// </summary>
    public float TemporalBlendAlpha { get; }
    public bool EnableTaa { get; }
    public bool EnableDiffuseCache { get; }
    public float DiffuseCacheCellSize { get; }
    public uint DiffuseCacheMinSamples { get; }

    /// <summary>
    /// Per-component XYZ clamp applied to each sample before accumulation.
    /// Suppresses firefly artifacts from extreme spectral contributions.
    /// 0 = disabled.
    /// </summary>
    public float SampleClamp { get; }

    public SmokeMode SmokeMode { get; }

    public VolumetricOptions Volumetrics { get; }

    /// <summary>
    /// Forward caustic transport controls (shadows-and-caustics-plan §B). When
    /// <see cref="CausticOptions.Enable"/> is set and the scene holds specular casters, a spectral
    /// photon map is built from the lights toward those casters and its density estimate is added at
    /// each diffuse hit in <see cref="TraceCore"/>. Disabled by default, so ordinary renders are
    /// untouched.
    /// </summary>
    public CausticOptions Caustics { get; }

    /// <summary>
    /// The caustic photon map, built once after the BVH from the caster bounds (the CPU scene is
    /// static, so a single build is stable and folds cleanly into accumulation). <c>null</c> when
    /// caustics are off, there are no lights, or the scene has no specular casters — in which case the
    /// composite in <see cref="TraceCore"/> is skipped and output stays byte-identical.
    /// </summary>
    private readonly PhotonMap? _causticMap;

    /// <summary>
    /// Controls how surfaces are shaded: <see cref="LightingMode.None"/>
    /// for raw albedo, <see cref="LightingMode.Direct"/> for ambient +
    /// Lambertian without shadows, or <see cref="LightingMode.NEE"/> for
    /// full next event estimation with stochastic shadow rays.
    /// </summary>
    public LightingMode Lighting { get; }

    /// <summary>
    /// Controls how many worker threads are spawned and at what OS priority they run.
    /// See <see cref="CpuThrottle"/> for the available levels.
    /// </summary>
    public CpuThrottle ThrottleCpu { get; }

    public DebugOptions DebugOptions { get; }

    // Precomputed projection constants (depend only on Fov/Aspect/resolution, not position/rotation)
    private readonly float _tanHalfFov;
    private readonly float _aspectTanHalfFov;
    private readonly float _invWidth;
    private readonly float _invHeight;

    // TAA history and camera state (resolved once per frame on UI thread).
    private readonly Vector3[] _taaHistoryXYZ;
    private readonly Vector3[] _taaHistoryHitPoint;
    private readonly bool[] _taaHistoryValid;
    private Vector3 _taaPrevCamPos;
    private Quaternion _taaPrevCamRot;
    private bool _taaHasPrevCamera;
    private long _clampEventCount;

    private readonly TileScheduler _tileScheduler;
    private readonly PathTracer _pathTracer;
    private readonly AccumulationBuffer _accumulationBuffer;
    private readonly TaaResolver _taaResolver;
    private readonly DisplayResolver _displayResolver;
    private readonly DebugBufferRenderer _debugBufferRenderer;

    public PerPixelState PerPixel { get; }
    public HistoryState History { get; }
    public DebugState Debug { get; }

    public double RejectedHistoryPercent { get; private set; }
    public double ClampedPixelPercent { get; private set; }
    public double AverageVariance { get; private set; }
    public double AverageHistoryWeight { get; private set; }
    public double AverageEffectiveSpp { get; private set; }
    public uint MaxObservedSampleCount { get; private set; }
    public int FrameIndex { get; private set; }
    public int DiffuseCacheEntryCount { get; private set; }
    public double DiffuseCacheHitRate { get; private set; }

    /// <summary>
    /// Snapshot of per-frame diagnostics captured at the end of each
    /// <see cref="ResolveDisplayBufferWithTaa"/> call.
    /// </summary>
    public FrameDiagnostics LastFrameDiagnostics { get; private set; }

    public JobSystem(int width, int height, int tilesize, Tracable[] scene, Camera camera, int stride, Light[]? lights = null)
        : this(
            width,
            height,
            scene,
            camera,
            stride,
            lights,
            new RenderOptions(TileSize: tilesize),
            new SamplingOptions(),
            new DenoiseOptions(),
            new DebugOptions())
    {
    }

    public JobSystem(
        int width,
        int height,
        Tracable[] scene,
        Camera camera,
        int stride,
        Light[]? lights = null,
        RenderOptions? renderOptions = null,
        SamplingOptions? samplingOptions = null,
        DenoiseOptions? denoiseOptions = null,
        DebugOptions? debugOptions = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);
        ValidateCoreInputs(width, height, stride);

        RenderOptions effectiveRenderOptions = renderOptions ?? new RenderOptions();
        SamplingOptions effectiveSamplingOptions = samplingOptions ?? new SamplingOptions();
        DenoiseOptions effectiveDenoiseOptions = denoiseOptions ?? new DenoiseOptions();
        DebugOptions effectiveDebugOptions = debugOptions ?? new DebugOptions();

        ValidateOptions(effectiveRenderOptions, effectiveSamplingOptions, effectiveDenoiseOptions);

        TileSize = effectiveRenderOptions.TileSize;
        SppPerJob = effectiveRenderOptions.SppPerJob;
        MaxSampleCount = effectiveRenderOptions.MaxSampleCount;
        Lighting = effectiveRenderOptions.Lighting;
        ThrottleCpu = effectiveRenderOptions.ThrottleCpu;

        MotionSampleCap = effectiveSamplingOptions.MotionSampleCap;
        SubPixelJitter = effectiveSamplingOptions.SubPixelJitter;

        FilterRadius = effectiveDenoiseOptions.FilterRadius;
        EdgeAwareFilter = effectiveDenoiseOptions.EdgeAwareFilter;
        CheckerboardMotion = effectiveDenoiseOptions.CheckerboardMotion;
        TemporalBlendAlpha = effectiveDenoiseOptions.TemporalBlendAlpha;
        EnableTaa = effectiveDenoiseOptions.EnableTaa;
        SampleClamp = effectiveDenoiseOptions.SampleClamp;
        EnableDiffuseCache = effectiveDenoiseOptions.EnableDiffuseCache;
        DiffuseCacheCellSize = effectiveDenoiseOptions.DiffuseCacheCellSize;
        DiffuseCacheMinSamples = effectiveDenoiseOptions.DiffuseCacheMinSamples;
        SmokeMode = effectiveDenoiseOptions.SmokeMode;
        VolumetricOptions effectiveVolumetrics = effectiveDenoiseOptions.Volumetrics == default
            ? VolumetricOptions.FromQuality(VolumetricQuality.Medium, SmokeMode)
            : effectiveDenoiseOptions.Volumetrics;
        Volumetrics = effectiveVolumetrics.ForSmokeMode(SmokeMode);
        Caustics = effectiveDenoiseOptions.Caustics;

        _sampleClampVec = new Vector3(SampleClamp);
        _irradianceCache = new DiffuseIrradianceCache(DiffuseCacheCellSize, DiffuseCacheMinSamples);

        DebugOptions = effectiveDebugOptions;

        // Allocate all per-pixel render buffers as a single unit
        _buffers = RenderBuffers.Create(width, height);

        Width = width;
        Height = height;
        Scene = scene;
        _bvh = new BVH(scene);
        _lights = lights ?? [];
        _causticMap = BuildCausticMap();
        Camera = camera;
        int byteCount = stride * height;
        DisplayBuffer = new byte[byteCount];
        Stride = stride;

        _tanHalfFov = MathF.Tan(camera.Fov * 0.5f);
        _aspectTanHalfFov = camera.Aspect * _tanHalfFov;
        _invWidth = 1f / width;
        _invHeight = 1f / height;

        _taaHistoryXYZ = new Vector3[width * height];
        _taaHistoryHitPoint = new Vector3[width * height];
        _taaHistoryValid = new bool[width * height];

        PerPixel = new PerPixelState(AccumXYZ, SampleCount, WavelengthCounter, LastHit, _buffers.HitPointWorld);
        History = new HistoryState(_taaHistoryXYZ, _taaHistoryHitPoint, _taaHistoryValid, _buffers.TaaNextXYZ, _buffers.TaaNextHitPoint, _buffers.TaaNextValid);
        Debug = new DebugState(
            _buffers.LumaVariance,
            _buffers.HistoryWeight,
            _buffers.HistoryRejected,
            _buffers.ClampAmount,
            _buffers.ClampHitFrame,
            _buffers.DepthDistance,
            _buffers.AlbedoScalar,
            _buffers.NormalWorld,
            _buffers.DirectLightingXYZ,
            _buffers.IndirectLightingXYZ,
            _buffers.EmissiveLightingXYZ,
            _buffers.Bounce0XYZ,
            _buffers.Bounce1XYZ,
            _buffers.Bounce2PlusXYZ,
            _buffers.DiffCurrentVsAccum,
            _buffers.DiffUnfilteredVsFiltered,
            _buffers.DiffReprojectedVsCurrent,
            _buffers.LastUpdatedFrame);

        _pathTracer = new PathTracer(this);
        _displayResolver = new DisplayResolver(this);
        _accumulationBuffer = new AccumulationBuffer(this);
        _taaResolver = new TaaResolver(this);
        _debugBufferRenderer = new DebugBufferRenderer(this);
        _tileScheduler = new TileScheduler(this, _pathTracer, _displayResolver);

        int tilesX = (width + TileSize - 1) / TileSize;
        int tilesY = (height + TileSize - 1) / TileSize;
        TotalTiles = tilesX * tilesY;
    }

    /// <summary>
    /// Builds the caustic photon map from the lights toward every specular caster in the scene
    /// (shadows-and-caustics-plan §B). Returns <c>null</c> — so the composite is skipped entirely —
    /// when caustics are disabled, there are no lights, or the scene has no dielectric/bubble casters.
    /// The total <see cref="CausticOptions.PhotonsPerFrame"/> budget is split across the casters so the
    /// cost is bounded regardless of how many casters the scene holds. The CPU scene is static (the BVH
    /// is built once), so a single build is correct; the GPU port rebuilds per frame as bubbles drift.
    /// </summary>
    private PhotonMap? BuildCausticMap()
    {
        if (!Caustics.Enable || Caustics.PhotonsPerFrame <= 0 || _lights.Length == 0)
            return null;

        var casterBounds = new List<AABB>();
        foreach (Tracable prim in Scene)
        {
            // Refractive casters throw the focused/dispersed caustics the plan targets (glass windows,
            // jewels, bubbles). Mirrors are left out for now: a maze wall mirror's bounds would swamp
            // the photon-aiming target, and the plan scopes caustics to the refractive casters.
            if (prim.Surface is SurfaceKind.Dielectric or SurfaceKind.Bubble)
                casterBounds.Add(prim.Bounds);
        }

        if (casterBounds.Count == 0)
            return null;

        var map = new PhotonMap(Caustics.GatherRadius);
        int photonsPerCaster = Math.Max(1, Caustics.PhotonsPerFrame / (casterBounds.Count * _lights.Length));
        uint seed = 1u;
        foreach (AABB bounds in casterBounds)
        {
            PhotonTracer.EmitInto(
                map, _lights, _bvh, bounds, WavelengthLookup,
                photonsPerCaster, Caustics.MaxBounces, seed);
            seed += 0x9E3779B9u; // decorrelate each caster's photon stream
        }

        return map;
    }

    public void SetupJobs(CancellationToken cancellationToken)
    {
        _tileScheduler.SetupJobs(cancellationToken);
    }

    private void Render(int stride, byte[] buffer, int y, int x)
    {
        _displayResolver.Render(stride, buffer, y, x);
    }

    public void ResolveDisplayBufferWithTaa()
    {
        _taaResolver.ResolveDisplayBufferWithTaa();
    }

    /// <summary>
    /// Returns a snapshot of debug values for the specified pixel coordinates.
    /// Coordinates are in render-target space (0..Width-1, 0..Height-1).
    /// </summary>
    public PixelDebugInfo GetPixelDebugInfo(int x, int y)
    {
        return _debugBufferRenderer.GetPixelDebugInfo(x, y);
    }

    public async Task AddJobs()
    {
        await _tileScheduler.AddJobs();
    }

    private void Trace(Camera camera, int y, int x)
    {
        _pathTracer.Trace(camera, y, x);
    }

    public string GetDebugLegend(DebugViewMode mode)
    {
        return _debugBufferRenderer.GetDebugLegend(mode);
    }

    public void RenderDebugModeToBuffer(DebugViewMode mode, byte[] targetBuffer, int targetStride)
    {
        _debugBufferRenderer.RenderDebugModeToBuffer(mode, targetBuffer, targetStride);
    }
}

/// <summary>
/// Lightweight container for per-pixel debug information exposed to the UI.
/// </summary>
public readonly record struct PixelDebugInfo(
    Vector3 AccumulatedXYZ,
    Vector3 FilteredXYZ,
    float CurrentVsAccumDiff,
    float ClampAmount,
    bool ClampHit,
    uint SampleCount,
    float HistoryWeight,
    byte HistoryRejected,
    float Depth,
    float Albedo,
    int LastUpdatedFrame,
    Vector3 Normal,
    Vector3 DirectLighting,
    Vector3 IndirectLighting,
    float UnfilteredVsFilteredDiff,
    float ReprojectedVsCurrentDiff,
    Vector3 Bounce0,
    Vector3 Bounce1,
    Vector3 Bounce2Plus
);

public readonly record struct Tile(int X, int Y, int Width, int Height);
