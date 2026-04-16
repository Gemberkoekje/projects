using System.Numerics;

namespace RayTracer;

public struct IrradianceCacheEntry
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector3 IrradianceXYZ;
    public uint SampleCount;
    public int LastUsedFrame;
}
