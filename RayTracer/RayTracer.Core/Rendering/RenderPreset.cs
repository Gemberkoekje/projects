namespace RayTracer;

/// <summary>
/// A set of rendering parameters that trade visual quality for
/// performance. The <see cref="Recommend"/> method picks the best
/// preset that matches the measured CPU throughput.
/// </summary>
/// <param name="SubPixelJitter">
/// When <c>true</c>, each sample is traced through a random position
/// inside the pixel instead of the centre. This converts hard
/// aliasing into noise that the accumulator averages away — classic
/// ray-tracer anti-aliasing at essentially zero extra cost.
/// </param>
/// <param name="ResolutionScale">
/// Multiplier applied to <see cref="RenderWidth"/>/<see cref="RenderHeight"/>.
/// Values &gt;1 render at higher-than-display resolution
/// (supersampling); the <c>PictureBox.Zoom</c> downscale provides
/// natural anti-aliasing.
/// </param>
/// <param name="FilterRadius">
/// Radius of the spatial averaging filter applied during camera
/// motion.  0 = disabled, 1 = 3×3, 2 = 5×5.
/// </param>
/// <param name="EdgeAwareFilter">
/// When <c>true</c>, the spatial filter uses luminance-based
/// bilateral weighting so it smooths noise without blurring edges.
/// </param>
/// <param name="Lighting">
/// Controls how surfaces are shaded: <see cref="LightingMode.None"/>
/// for raw albedo, <see cref="LightingMode.Direct"/> for ambient +
/// Lambertian without shadows, or <see cref="LightingMode.NEE"/> for
/// full next event estimation with stochastic shadow rays.
/// </param>
public record RenderPreset(
    string Name,
    int RenderWidth,
    int RenderHeight,
    int SppPerJob,
    uint MaxSampleCount,
    uint MotionSampleCap,
    int TileSize,
    bool SubPixelJitter,
    float ResolutionScale,
    int FilterRadius,
    bool EdgeAwareFilter,
    LightingMode Lighting = LightingMode.None,
    bool CheckerboardMotion = false,
    float TemporalBlendAlpha = 0f,
    float SampleClamp = 0f,
    CpuThrottle ThrottleCpu = CpuThrottle.Normal,
    SmokeMode SmokeMode = SmokeMode.Biome,
    VolumetricOptions Volumetrics = default,
    bool IsDebugSmokePreset = false)
{
    // ── Presets ─────────────────────────────────────────────────────
    public static RenderPreset Low => new(
        "Low", 320, 240, 2, 100, 8, 32,
        SubPixelJitter: false, ResolutionScale: 1.0f,
        FilterRadius: 1, EdgeAwareFilter: false,
        Lighting: LightingMode.None,
        SampleClamp: 10f,
        SmokeMode: SmokeMode.None,
        Volumetrics: VolumetricOptions.FromQuality(VolumetricQuality.Off, SmokeMode.None));

    public static RenderPreset Playable => new(
        "Playable", 320, 240, 1, 150, 8, 32,
        SubPixelJitter: true, ResolutionScale: 1.0f,
        FilterRadius: 1, EdgeAwareFilter: true,
        Lighting: LightingMode.Direct,
        CheckerboardMotion: true, TemporalBlendAlpha: 0.08f, SampleClamp: 8f,
        SmokeMode: SmokeMode.Biome,
        Volumetrics: VolumetricOptions.FromQuality(VolumetricQuality.Low, SmokeMode.Biome)
            with { MarchSteps = 3, MaxMarchDistance = 10f, InscatterStrength = 0.22f });

    public static RenderPreset Medium => new(
        "Medium", 480, 360, 4, 300, 16, 32,
        SubPixelJitter: true, ResolutionScale: 1.0f,
        FilterRadius: 1, EdgeAwareFilter: true,
        Lighting: LightingMode.Direct,
        CheckerboardMotion: true, TemporalBlendAlpha: 0.05f, SampleClamp: 0f,
        SmokeMode: SmokeMode.Biome,
        Volumetrics: VolumetricOptions.FromQuality(VolumetricQuality.Medium, SmokeMode.Biome));

    public static RenderPreset High => new(
        "High", 640, 480, 4, 500, 24, 32,
        SubPixelJitter: true, ResolutionScale: 1.0f,
        FilterRadius: 1, EdgeAwareFilter: true,
        Lighting: LightingMode.NEE,
        CheckerboardMotion: true, TemporalBlendAlpha: 0.05f, SampleClamp: 0f,
        SmokeMode: SmokeMode.Biome,
        Volumetrics: VolumetricOptions.FromQuality(VolumetricQuality.High, SmokeMode.Biome));

    public static RenderPreset Ultra => new(
        "Ultra", 800, 600, 8, 800, 32, 32,
        SubPixelJitter: true, ResolutionScale: 1.25f,
        FilterRadius: 2, EdgeAwareFilter: true,
        Lighting: LightingMode.NEE,
        CheckerboardMotion: true, TemporalBlendAlpha: 0.04f, SampleClamp: 0f,
        SmokeMode: SmokeMode.Biome,
        Volumetrics: VolumetricOptions.FromQuality(VolumetricQuality.Ultra, SmokeMode.Biome));

    public static RenderPreset FogDebug => new(
        "Fog Debug", 480, 360, 2, 250, 12, 32,
        SubPixelJitter: true, ResolutionScale: 1.0f,
        FilterRadius: 1, EdgeAwareFilter: true,
        Lighting: LightingMode.Direct,
        CheckerboardMotion: true, TemporalBlendAlpha: 0.05f, SampleClamp: 0f,
        SmokeMode: SmokeMode.AlwaysFog,
        Volumetrics: VolumetricOptions.FromQuality(VolumetricQuality.High, SmokeMode.AlwaysFog) with { SigmaScaleFog = 1.35f },
        IsDebugSmokePreset: true);

    public static RenderPreset GroundSmokeDebug => new(
        "Ground Smoke Debug", 480, 360, 2, 250, 12, 32,
        SubPixelJitter: true, ResolutionScale: 1.0f,
        FilterRadius: 1, EdgeAwareFilter: true,
        Lighting: LightingMode.Direct,
        CheckerboardMotion: true, TemporalBlendAlpha: 0.05f, SampleClamp: 0f,
        SmokeMode: SmokeMode.AlwaysGroundSmoke,
        Volumetrics: VolumetricOptions.FromQuality(VolumetricQuality.High, SmokeMode.AlwaysGroundSmoke) with { SigmaScaleGround = 1.45f, MaxMarchDistance = 12f },
        IsDebugSmokePreset: true);

    public static RenderPreset[] All => [Low, Playable, Medium, High, Ultra, FogDebug, GroundSmokeDebug];

    public static RenderPreset[] QualityPresets => [Low, Playable, Medium, High, Ultra];

    // ── Derived metrics ────────────────────────────────────────────
    /// <summary>Effective width after applying <see cref="ResolutionScale"/>.</summary>
    public int EffectiveWidth => Math.Max(1, (int)(RenderWidth * ResolutionScale));

    /// <summary>Effective height after applying <see cref="ResolutionScale"/>.</summary>
    public int EffectiveHeight => Math.Max(1, (int)(RenderHeight * ResolutionScale));

    /// <summary>
    /// Total rays needed for one complete screen pass at the effective
    /// resolution.
    /// </summary>
    public long RaysPerFrame => (long)EffectiveWidth * EffectiveHeight * SppPerJob;

    /// <summary>
    /// Predicts full-screen passes per second at the given throughput.
    /// </summary>
    public double PredictFps(double raysPerSecond)
        => RaysPerFrame > 0 ? raysPerSecond / RaysPerFrame : 0;

    /// <summary>Human-readable summary of the enabled quality features.</summary>
    public string FeatureSummary
    {
        get
        {
            var parts = new List<string>();
            if (Lighting != LightingMode.None)
                parts.Add(Lighting == LightingMode.NEE ? "NEE" : "direct light");
            if (SubPixelJitter) parts.Add("jitter");
            if (ResolutionScale > 1f) parts.Add($"{ResolutionScale:G3}× SS");
            if (FilterRadius > 0)
            {
                int size = 1 + FilterRadius * 2;
                parts.Add(EdgeAwareFilter ? $"{size}×{size} edge-aware" : $"{size}×{size} filter");
            }
            if (CheckerboardMotion) parts.Add("checker");
            if (TemporalBlendAlpha > 0f) parts.Add("EMA");
            if (SampleClamp > 0f) parts.Add("clamp");
            if (SmokeMode != SmokeMode.None)
            {
                parts.Add(SmokeMode switch
                {
                    SmokeMode.Biome => "biome smoke",
                    SmokeMode.AlwaysFog => "always fog",
                    SmokeMode.AlwaysGroundSmoke => "ground smoke",
                    _ => "smoke"
                });
            }
            return parts.Count > 0 ? string.Join(", ", parts) : "baseline";
        }
    }

    /// <summary>
    /// Returns the highest preset the CPU can sustain based on the
    /// measured <paramref name="raysPerSecond"/> at the lowest settings.
    /// The recommendation targets at least ~10 full-screen passes/sec
    /// so the image feels responsive during camera motion.
    /// </summary>
    public static RenderPreset Recommend(double raysPerSecond)
    {
        // Walk from highest to lowest quality preset; pick the first that hits ≥10 FPS.
        var presets = new[] { Ultra, High, Medium, Playable, Low };
        foreach (var p in presets)
            if (!p.IsDebugSmokePreset && p.PredictFps(raysPerSecond) >= 10)
                return p;
        return Low;
    }
}
