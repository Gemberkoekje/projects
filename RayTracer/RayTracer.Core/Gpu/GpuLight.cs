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
/// The scene's lights flattened for the GPU: a float4-per-light position buffer
/// for upload plus the parallel <see cref="Positions"/> array the CPU
/// <see cref="Phase2Reference"/> mirror consumes. Keeping both from one packer
/// guarantees the GPU light buffer and the reference see identical data.
///
/// The path tracer's surface lighting (Phase 2/3) only ever reads a light's
/// position; colour is packed additionally (<see cref="Colors"/> /
/// <see cref="ColorData"/>) because the Phase 4 volumetric inscatter tints the
/// medium by <c>light.Color</c> (mirroring <c>JobSystem.EstimateInscatterLight</c>).
/// </summary>
public sealed class PackedLights
{
    /// <summary>Light positions, one per light (packer order).</summary>
    public Vector3[] Positions { get; }

    /// <summary>Light colours, one per light (packer order); consumed by the
    /// Phase 4 volumetric inscatter and its CPU mirror.</summary>
    public Vector3[] Colors { get; }

    /// <summary>Light radii (world units), one per light — 0 for a point light (§C1 soft shadows).
    /// Also packed into <see cref="Data"/>'s per-light <c>.w</c> for the GPU (<c>Lights[i].w</c>).</summary>
    public float[] Radii { get; }

    /// <summary>Flat float buffer, four floats per light (xyz + pad), for GPU upload.</summary>
    public float[] Data { get; }

    /// <summary>Flat float buffer, four floats per light (rgb + pad), for GPU
    /// upload as a parallel <c>StructuredBuffer&lt;float4&gt;</c> of light colours.</summary>
    public float[] ColorData { get; }

    /// <summary>Flat float buffer, four floats per light — the directional (sun) travel direction in
    /// xyz and a <c>1</c>/<c>0</c> directional flag in w (§C2) — uploaded as a parallel
    /// <c>StructuredBuffer&lt;float4&gt;</c>. A point light is all-zero (flag 0).</summary>
    public float[] DirData { get; }

    /// <summary>Number of lights.</summary>
    public int Count => Positions.Length;

    internal PackedLights(Vector3[] positions, Vector3[] colors, float[] radii, float[] data, float[] colorData, float[] dirData)
    {
        Positions = positions;
        Colors = colors;
        Radii = radii;
        Data = data;
        ColorData = colorData;
        DirData = dirData;
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
        var colors = new Vector3[lights.Count];
        var radii = new float[lights.Count];
        var data = new float[lights.Count * 4];
        var colorData = new float[lights.Count * 4];
        var dirData = new float[lights.Count * 4];
        for (int i = 0; i < lights.Count; i++)
        {
            Vector3 p = lights[i].Position;
            Vector3 c = lights[i].Color;
            Vector3 d = lights[i].Direction;
            positions[i] = p;
            colors[i] = c;
            radii[i] = lights[i].Radius;
            data[i * 4 + 0] = p.X;
            data[i * 4 + 1] = p.Y;
            data[i * 4 + 2] = p.Z;
            data[i * 4 + 3] = lights[i].Radius; // §C1: radius in .w for the shader's SampleLightPoint
            colorData[i * 4 + 0] = c.X;
            colorData[i * 4 + 1] = c.Y;
            colorData[i * 4 + 2] = c.Z;
            colorData[i * 4 + 3] = lights[i].EmissionTempK; // §O9: blackbody temp in .w (0 = flat white)
            dirData[i * 4 + 0] = d.X;                                  // §C2: sun travel direction
            dirData[i * 4 + 1] = d.Y;
            dirData[i * 4 + 2] = d.Z;
            dirData[i * 4 + 3] = lights[i].Directional ? 1f : 0f;      // directional flag in .w
        }

        return new PackedLights(positions, colors, radii, data, colorData, dirData);
    }
}
