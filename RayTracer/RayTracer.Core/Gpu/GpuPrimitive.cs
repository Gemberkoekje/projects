using System.Runtime.InteropServices;

namespace RayTracer;

/// <summary>
/// Flat, GPU-uploadable description of one quad, indexed on the GPU by
/// <c>PrimitiveIndex &gt;&gt; 1</c> (each quad is two triangles). The layout is
/// scalar-only (all fields four bytes) so it packs identically in a C#
/// <c>struct</c> and an HLSL <c>StructuredBuffer</c> element, sidestepping the
/// notorious <c>float3</c> alignment ambiguity in structured buffers. The size
/// is a multiple of 16 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct GpuPrimitive
{
    /// <summary>Quad origin corner (pattern origin), X/Y/Z.</summary>
    public float L1X;
    /// <summary>Quad origin corner, Y.</summary>
    public float L1Y;
    /// <summary>Quad origin corner, Z.</summary>
    public float L1Z;

    /// <summary>U-axis edge vector, X.</summary>
    public float E1X;
    /// <summary>U-axis edge vector, Y.</summary>
    public float E1Y;
    /// <summary>U-axis edge vector, Z.</summary>
    public float E1Z;

    /// <summary>W-axis edge vector, X.</summary>
    public float E2X;
    /// <summary>W-axis edge vector, Y.</summary>
    public float E2Y;
    /// <summary>W-axis edge vector, Z.</summary>
    public float E2Z;

    /// <summary>Normalized geometric normal, X.</summary>
    public float NX;
    /// <summary>Normalized geometric normal, Y.</summary>
    public float NY;
    /// <summary>Normalized geometric normal, Z.</summary>
    public float NZ;

    /// <summary><c>1 / dot(Edge1, Edge1)</c>, so the shader recovers <c>u</c> with a multiply.</summary>
    public float InvEdge1LenSq;
    /// <summary><c>1 / dot(Edge2, Edge2)</c>, so the shader recovers <c>w</c> with a multiply.</summary>
    public float InvEdge2LenSq;

    /// <summary>The <see cref="QuadPattern"/> value.</summary>
    public uint Pattern;
    /// <summary>Row index of the primary material in the reflectance table.</summary>
    public uint MatPrimary;
    /// <summary>Row index of the secondary (mortar) material; equals
    /// <see cref="MatPrimary"/> when unused.</summary>
    public uint MatSecondary;

    /// <summary>Pattern parameter 0.</summary>
    public float P0;
    /// <summary>Pattern parameter 1.</summary>
    public float P1;
    /// <summary>Pattern parameter 2.</summary>
    public float P2;
    /// <summary>Pattern parameter 3.</summary>
    public float P3;

    /// <summary>Padding to a 96-byte (16-aligned) stride.</summary>
    public float Pad0;
    /// <summary>Padding.</summary>
    public float Pad1;
    /// <summary>Padding.</summary>
    public float Pad2;
}
