using System.Numerics;

namespace RayTracer;

public sealed class PerPixelState(
    Vector3[] accumXyz,
    uint[] sampleCount,
    long[] wavelengthCounter,
    bool[] lastHit,
    Vector3[] hitPointWorld)
{
    public Vector3[] AccumXYZ { get; } = accumXyz;
    public uint[] SampleCount { get; } = sampleCount;
    public long[] WavelengthCounter { get; } = wavelengthCounter;
    public bool[] LastHit { get; } = lastHit;
    public Vector3[] HitPointWorld { get; } = hitPointWorld;
}

public sealed class HistoryState(
    Vector3[] historyXyz,
    Vector3[] historyHitPoint,
    bool[] historyValid,
    Vector3[] nextXyz,
    Vector3[] nextHitPoint,
    bool[] nextValid)
{
    public Vector3[] HistoryXYZ { get; } = historyXyz;
    public Vector3[] HistoryHitPoint { get; } = historyHitPoint;
    public bool[] HistoryValid { get; } = historyValid;
    public Vector3[] NextXYZ { get; } = nextXyz;
    public Vector3[] NextHitPoint { get; } = nextHitPoint;
    public bool[] NextValid { get; } = nextValid;
}

public sealed class DebugState(
    float[] lumaVariance,
    float[] historyWeight,
    byte[] historyRejected,
    float[] clampAmount,
    bool[] clampHitFrame,
    float[] depthDistance,
    float[] albedoScalar,
    Vector3[] normalWorld,
    Vector3[] directLightingXyz,
    Vector3[] indirectLightingXyz,
    Vector3[] emissiveLightingXyz,
    Vector3[] bounce0Xyz,
    Vector3[] bounce1Xyz,
    Vector3[] bounce2PlusXyz,
    float[] diffCurrentVsAccum,
    float[] diffUnfilteredVsFiltered,
    float[] diffReprojectedVsCurrent,
    int[] lastUpdatedFrame)
{
    public float[] LumaVariance { get; } = lumaVariance;
    public float[] HistoryWeight { get; } = historyWeight;
    public byte[] HistoryRejected { get; } = historyRejected;
    public float[] ClampAmount { get; } = clampAmount;
    public bool[] ClampHitFrame { get; } = clampHitFrame;
    public float[] DepthDistance { get; } = depthDistance;
    public float[] AlbedoScalar { get; } = albedoScalar;
    public Vector3[] NormalWorld { get; } = normalWorld;
    public Vector3[] DirectLightingXYZ { get; } = directLightingXyz;
    public Vector3[] IndirectLightingXYZ { get; } = indirectLightingXyz;
    public Vector3[] EmissiveLightingXYZ { get; } = emissiveLightingXyz;
    public Vector3[] Bounce0XYZ { get; } = bounce0Xyz;
    public Vector3[] Bounce1XYZ { get; } = bounce1Xyz;
    public Vector3[] Bounce2PlusXYZ { get; } = bounce2PlusXyz;
    public float[] DiffCurrentVsAccum { get; } = diffCurrentVsAccum;
    public float[] DiffUnfilteredVsFiltered { get; } = diffUnfilteredVsFiltered;
    public float[] DiffReprojectedVsCurrent { get; } = diffReprojectedVsCurrent;
    public int[] LastUpdatedFrame { get; } = lastUpdatedFrame;
}
