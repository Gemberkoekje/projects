namespace RayTracer;

public sealed record RenderOptions(
    int TileSize = 32,
    int SppPerJob = 4,
    uint MaxSampleCount = 500,
    LightingMode Lighting = LightingMode.None,
    CpuThrottle ThrottleCpu = CpuThrottle.Normal);

public sealed record SamplingOptions(
    uint MotionSampleCap = 20,
    bool SubPixelJitter = false,
    LensOptions Lens = default);

public sealed record DenoiseOptions(
    int FilterRadius = 1,
    bool EdgeAwareFilter = false,
    bool CheckerboardMotion = false,
    float TemporalBlendAlpha = 0f,
    bool EnableTaa = false,
    float SampleClamp = 0f,
    bool EnableDiffuseCache = true,
    float DiffuseCacheCellSize = 0.25f,
    uint DiffuseCacheMinSamples = 4,
    SmokeMode SmokeMode = SmokeMode.Biome,
    VolumetricOptions Volumetrics = default,
    CausticOptions Caustics = default,
    RealismOptions Realism = default);

public sealed record DebugOptions(
    bool EnableDiagnostics = true);
