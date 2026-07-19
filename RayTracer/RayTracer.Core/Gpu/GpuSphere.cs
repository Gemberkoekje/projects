using System.Runtime.InteropServices;

namespace RayTracer;

/// <summary>
/// Flat, GPU-uploadable description of one analytic sphere (spectral-effects-plan.md §0.4),
/// carried as procedural-AABB geometry. Scalar-only layout (all fields four bytes) so it packs
/// identically in a C# <c>struct</c> and an HLSL <c>StructuredBuffer</c> element; the size is a
/// multiple of 16 bytes. At a hit the shader synthesises a <c>PrimitiveInfo</c> from these
/// fields (material row, surface kind, optical params) plus the analytic normal, so the whole
/// quad-shading pipeline works on a sphere unchanged.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct GpuSphere
{
    /// <summary>Centre X/Y/Z.</summary>
    public float CX, CY, CZ;
    /// <summary>Radius (world units).</summary>
    public float Radius;

    /// <summary>Row index of the material in the reflectance table.</summary>
    public uint MatRow;
    /// <summary>The material's <see cref="SurfaceKind"/> (as a <c>uint</c>).</summary>
    public uint Surface;
    /// <summary>Base index of refraction (Cauchy A) for a dielectric sphere; 1 otherwise.</summary>
    public float Ior;
    /// <summary>Cauchy B (dielectric dispersion) / thin-film thickness — same union as <see cref="GpuPrimitive.CauchyB"/>.</summary>
    public float CauchyB;

    /// <summary>Optical param 0 (thin-film amp / dielectric absorption σR) — mirrors <see cref="GpuPrimitive.P0"/>.</summary>
    public float P0;
    /// <summary>Optical param 1 (thin-film contrast / dielectric absorption σG) — mirrors <see cref="GpuPrimitive.P1"/>.</summary>
    public float P1;
    /// <summary>Optical param 2 (dielectric absorption σB) — mirrors <see cref="GpuPrimitive.P2"/>.</summary>
    public float P2;
    /// <summary>1 = a moving part (the chrome ball) → cheap mover shading tier (pinball-plan §4.1); 0 =
    /// static. Occupies the former padding slot, keeping the 48-byte (16-aligned) stride.</summary>
    public uint Dynamic;
}
