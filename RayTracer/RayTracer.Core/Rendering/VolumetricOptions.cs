namespace RayTracer;

/// <summary>
/// Quality tier used to map user-facing volumetric controls to concrete marcher settings.
/// </summary>
public enum VolumetricQuality
{
    None = 0,
    Off = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Ultra = 4,
}

/// <summary>
/// Controls camera-ray participating media integration.
/// </summary>
public readonly record struct VolumetricOptions(
    bool EnableVolumetrics = true,
    SmokeMode SmokeMode = SmokeMode.Biome,
    int MarchSteps = 8,
    float MaxMarchDistance = 16f,
    float SigmaScaleFog = 1f,
    float SigmaScaleGround = 1f,
    float AnisotropyG = 0f,
    float InscatterStrength = 0.35f,
    float EarlyOutTransmittance = 0.01f,
    VolumetricQuality Quality = VolumetricQuality.Medium,
    /// <summary>
    /// How often a BVH shadow ray is fired during volumetric marching.
    /// 0 = never (ambient-only inscatter — fastest, used for Low/Medium).
    /// 1 = every step (full shadow tracing — used for Ultra).
    /// N = every Nth step (e.g. 2 fires on steps 0, 2, 4, … — used for High).
    /// </summary>
    int ShadowStepInterval = 0)
{
    public static VolumetricOptions FromQuality(VolumetricQuality quality, SmokeMode smokeMode)
    {
        return quality switch
        {
            VolumetricQuality.Off => new VolumetricOptions(
                EnableVolumetrics: false,
                SmokeMode: SmokeMode.None,
                MarchSteps: 0,
                SigmaScaleFog: 0f,
                SigmaScaleGround: 0f,
                InscatterStrength: 0f,
                ShadowStepInterval: 0,
                Quality: quality),
            VolumetricQuality.Low => new VolumetricOptions(
                EnableVolumetrics: smokeMode != SmokeMode.None,
                SmokeMode: smokeMode,
                MarchSteps: 4,
                MaxMarchDistance: 12f,
                SigmaScaleFog: 0.85f,
                SigmaScaleGround: 0.85f,
                InscatterStrength: 0.25f,
                ShadowStepInterval: 0,
                Quality: quality),
            VolumetricQuality.High => new VolumetricOptions(
                EnableVolumetrics: smokeMode != SmokeMode.None,
                SmokeMode: smokeMode,
                MarchSteps: 16,
                MaxMarchDistance: 20f,
                SigmaScaleFog: 1f,
                SigmaScaleGround: 1f,
                InscatterStrength: 0.38f,
                ShadowStepInterval: 4,
                Quality: quality),
            VolumetricQuality.Ultra => new VolumetricOptions(
                EnableVolumetrics: smokeMode != SmokeMode.None,
                SmokeMode: smokeMode,
                MarchSteps: 24,
                MaxMarchDistance: 24f,
                SigmaScaleFog: 1f,
                SigmaScaleGround: 1f,
                AnisotropyG: 0.35f,
                InscatterStrength: 0.42f,
                ShadowStepInterval: 1,
                Quality: quality),
            _ => new VolumetricOptions(
                EnableVolumetrics: smokeMode != SmokeMode.None,
                SmokeMode: smokeMode,
                MarchSteps: 8,
                MaxMarchDistance: 16f,
                SigmaScaleFog: 1f,
                SigmaScaleGround: 1f,
                InscatterStrength: 0.35f,
                ShadowStepInterval: 0,
                Quality: VolumetricQuality.Medium),
        };
    }

    public VolumetricOptions ForSmokeMode(SmokeMode smokeMode)
    {
        return this with
        {
            EnableVolumetrics = EnableVolumetrics && smokeMode != SmokeMode.None,
            SmokeMode = smokeMode,
        };
    }
}
