using System.Numerics;

namespace RayTracer;

public struct IrradianceCacheEntry
{
    public Vector3 Position;
    public Vector3 Normal;

    /// <summary>
    /// Scalar hero-wavelength bounce radiance stored for this cell (realism finding §1). It is a
    /// spectral <b>throughput</b> scalar — the CIE tristimulus weight is applied once, at the primary
    /// surface, by the consumer — so the cache must not carry a CIE-tinted XYZ (which would apply the
    /// colour-matching curve twice). Accumulated as a running mean across the samples that land here.
    /// </summary>
    public float Irradiance;
    public uint SampleCount;
    public int LastUsedFrame;
}
