namespace RayTracer;

public sealed record RenderOptions(
    int TileSize = 32,
    int SppPerJob = 4,
    uint MaxSampleCount = 500,
    LightingMode Lighting = LightingMode.None,
    CpuThrottle ThrottleCpu = CpuThrottle.Normal);

public sealed record SamplingOptions(
    uint MotionSampleCap = 20,
    bool SubPixelJitter = false);

public sealed record DenoiseOptions(
    int FilterRadius = 1,
    bool EdgeAwareFilter = false,
    bool CheckerboardMotion = false,
    float TemporalBlendAlpha = 0f,
    bool EnableTaa = false,
    float SampleClamp = 0f);

public sealed record DebugOptions(
    bool EnableDiagnostics = true);
