using System.Numerics;

namespace RayTracer;

/// <summary>
/// The procedural surface pattern a quad carries. Mirrors the concrete
/// <see cref="Tracable"/> rectangle types so the GPU scene packer can flatten
/// them to data without knowing about virtual dispatch.
/// </summary>
public enum QuadPattern
{
    /// <summary>Flat rectangle, single material, no per-hit attenuation
    /// (<see cref="TracableRectangle"/>).</summary>
    Plain = 0,

    /// <summary>Running-bond brick with mortar (<see cref="BrickRectangle"/>).</summary>
    Brick = 1,

    /// <summary>Regular ceiling tiles with a bevelled border
    /// (<see cref="CeilingTileRectangle"/>).</summary>
    CeilingTile = 2,

    /// <summary>Image decal (plan §3): a wall-mounted plaque / sign / logo whose colour
    /// comes from a layer of the decal atlas, sampled through the RGB→reflectance basis
    /// (<see cref="DecalRectangle"/>). The atlas layer index rides in
    /// <see cref="IQuadPrimitive.PatternP0"/>.</summary>
    Decal = 3,
}

/// <summary>
/// A quad that can be flattened into GPU vertex/index/primitive buffers.
/// The three concrete rectangle types implement this so
/// <see cref="GpuScenePacker"/> can consume the same <c>Tracable[]</c> the CPU
/// renderer uses, guaranteeing the GPU scene is identical to the CPU scene.
///
/// The quad is defined by a corner <see cref="L1"/> and two edge vectors
/// <see cref="Edge1"/> and <see cref="Edge2"/> (the fourth corner is
/// <c>L1 + Edge1 + Edge2</c>). This is the exact basis the CPU intersection
/// uses to recover the <c>(u, w)</c> texture coordinates, so patterns stay
/// seamless when the quad becomes two triangles on the GPU.
/// </summary>
public interface IQuadPrimitive
{
    /// <summary>First corner of the quad (the pattern origin).</summary>
    Vector3 L1 { get; }

    /// <summary>Edge from <see cref="L1"/> to the second corner (the U axis).</summary>
    Vector3 Edge1 { get; }

    /// <summary>Edge from <see cref="L1"/> to the third corner (the W axis).</summary>
    Vector3 Edge2 { get; }

    /// <summary>Normalized geometric normal, <c>normalize(cross(Edge1, Edge2))</c>.</summary>
    Vector3 QuadNormal { get; }

    /// <summary>Which procedural pattern to evaluate at a hit.</summary>
    QuadPattern Pattern { get; }

    /// <summary>Primary material (plain surface, brick face, or tile).</summary>
    MaterialData PrimaryMaterial { get; }

    /// <summary>Secondary material (mortar). <c>null</c> when the pattern uses
    /// only one material.</summary>
    MaterialData? SecondaryMaterial { get; }

    /// <summary>Pattern parameter 0 — bricks/tiles across for patterned quads.</summary>
    float PatternP0 { get; }

    /// <summary>Pattern parameter 1 — bricks/tiles down for patterned quads.</summary>
    float PatternP1 { get; }

    /// <summary>Pattern parameter 2 — mortar fraction U (brick) / bevel fraction (tile).</summary>
    float PatternP2 { get; }

    /// <summary>Pattern parameter 3 — mortar fraction V (brick), unused otherwise.</summary>
    float PatternP3 { get; }
}
