namespace RayTracer;

using System.Numerics;

/// <summary>
/// Encapsulates all per-pixel render buffers and accumulation state arrays
/// for a single JobSystem instance. Isolated to reduce JobSystem file size
/// and clarify buffer lifetime management.
/// </summary>
internal class RenderBuffers
{
    // Per-pixel accumulation state
    public Vector3[] AccumXYZ { get; }
    public uint[] SampleCount { get; }
    public long[] WavelengthCounter { get; }
    public bool[] LastHit { get; }
    public Vector3[] HitPointWorld { get; }

    // Luminance variance tracking
    public float[] LumaM2 { get; }
    public float[] LumaVariance { get; }
    public float[] LumaDirectM2 { get; }
    public float[] LumaIndirectM2 { get; }
    public float[] LumaDirectVariance { get; }
    public float[] LumaIndirectVariance { get; }

    // TAA history and rejection state
    public float[] HistoryWeight { get; }
    public byte[] HistoryRejected { get; }

    // Debug/diagnostic accumulation
    public float[] ClampAmount { get; }
    public bool[] ClampHitFrame { get; }
    public float[] DepthDistance { get; }
    public float[] AlbedoScalar { get; }
    public Vector3[] NormalWorld { get; }
    public Vector3[] DirectLightingXYZ { get; }
    public Vector3[] IndirectLightingXYZ { get; }
    public Vector3[] EmissiveLightingXYZ { get; }
    public Vector3[] Bounce0XYZ { get; }
    public Vector3[] Bounce1XYZ { get; }
    public Vector3[] Bounce2PlusXYZ { get; }
    public float[] DiffCurrentVsAccum { get; }
    public float[] DiffUnfilteredVsFiltered { get; }
    public float[] DiffReprojectedVsCurrent { get; }
    public int[] LastUpdatedFrame { get; }
    public byte[] DiffuseCacheState { get; }

    // TAA next-frame buffers
    public Vector3[] TaaNextXYZ { get; }
    public Vector3[] TaaNextHitPoint { get; }
    public bool[] TaaNextValid { get; }

    private RenderBuffers(int pixelCount)
    {
        AccumXYZ = new Vector3[pixelCount];
        SampleCount = new uint[pixelCount];
        WavelengthCounter = new long[pixelCount];
        LastHit = new bool[pixelCount];
        HitPointWorld = new Vector3[pixelCount];

        LumaM2 = new float[pixelCount];
        LumaVariance = new float[pixelCount];
        LumaDirectM2 = new float[pixelCount];
        LumaIndirectM2 = new float[pixelCount];
        LumaDirectVariance = new float[pixelCount];
        LumaIndirectVariance = new float[pixelCount];

        HistoryWeight = new float[pixelCount];
        HistoryRejected = new byte[pixelCount];

        ClampAmount = new float[pixelCount];
        ClampHitFrame = new bool[pixelCount];
        DepthDistance = new float[pixelCount];
        AlbedoScalar = new float[pixelCount];
        NormalWorld = new Vector3[pixelCount];
        DirectLightingXYZ = new Vector3[pixelCount];
        IndirectLightingXYZ = new Vector3[pixelCount];
        EmissiveLightingXYZ = new Vector3[pixelCount];
        Bounce0XYZ = new Vector3[pixelCount];
        Bounce1XYZ = new Vector3[pixelCount];
        Bounce2PlusXYZ = new Vector3[pixelCount];
        DiffCurrentVsAccum = new float[pixelCount];
        DiffUnfilteredVsFiltered = new float[pixelCount];
        DiffReprojectedVsCurrent = new float[pixelCount];
        LastUpdatedFrame = new int[pixelCount];
        DiffuseCacheState = new byte[pixelCount];

        TaaNextXYZ = new Vector3[pixelCount];
        TaaNextHitPoint = new Vector3[pixelCount];
        TaaNextValid = new bool[pixelCount];
    }

    /// <summary>Factory method to allocate all buffers for a given resolution.</summary>
    public static RenderBuffers Create(int width, int height)
    {
        return new RenderBuffers(width * height);
    }
}
