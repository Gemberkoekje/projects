using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace RayTracer;

/// <summary>
/// GPU-uploadable description of one point light. The path tracer's lighting
/// (<c>SelectLight</c>, NEE direct term, indirect bounces) only ever reads a
/// light's <b>position</b> — brightness comes from the global
/// <see cref="Phase2Reference.LightIntensity"/> and the colour/ambient fields on
/// <see cref="Light"/> are unused by the tracer — so only the position is packed.
/// The layout is scalar float4 (xyz + pad) so it maps onto an HLSL
/// <c>StructuredBuffer&lt;float4&gt;</c> with no alignment ambiguity.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct GpuLight
{
    /// <summary>Light world position, X.</summary>
    public float PosX;
    /// <summary>Light world position, Y.</summary>
    public float PosY;
    /// <summary>Light world position, Z.</summary>
    public float PosZ;
    /// <summary>Padding to a 16-byte stride.</summary>
    public float Pad;
}

/// <summary>
/// The scene's lights flattened for the GPU: a float4-per-light buffer for
/// upload plus the parallel <see cref="Positions"/> array the CPU
/// <see cref="Phase2Reference"/> mirror consumes. Keeping both from one packer
/// guarantees the GPU light buffer and the reference see identical data.
/// </summary>
public sealed class PackedLights
{
    /// <summary>Light positions, one per light (packer order).</summary>
    public Vector3[] Positions { get; }

    /// <summary>Flat float buffer, four floats per light (xyz + pad), for GPU upload.</summary>
    public float[] Data { get; }

    /// <summary>Number of lights.</summary>
    public int Count => Positions.Length;

    internal PackedLights(Vector3[] positions, float[] data)
    {
        Positions = positions;
        Data = data;
    }
}

/// <summary>Packs a <see cref="Light"/> array into <see cref="PackedLights"/>.</summary>
public static class LightPacker
{
    /// <summary>Flattens <paramref name="lights"/> into GPU-ready buffers.</summary>
    public static PackedLights Pack(IReadOnlyList<Light> lights)
    {
        System.ArgumentNullException.ThrowIfNull(lights);

        var positions = new Vector3[lights.Count];
        var data = new float[lights.Count * 4];
        for (int i = 0; i < lights.Count; i++)
        {
            Vector3 p = lights[i].Position;
            positions[i] = p;
            data[i * 4 + 0] = p.X;
            data[i * 4 + 1] = p.Y;
            data[i * 4 + 2] = p.Z;
            data[i * 4 + 3] = 0f;
        }

        return new PackedLights(positions, data);
    }
}
