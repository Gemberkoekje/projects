using System.Numerics;

namespace RayTracer;

/// <summary>
/// The scene flattened into GPU-ready buffers: a triangle mesh (four vertices
/// and two triangles per quad) plus a parallel <see cref="GpuPrimitive"/> array
/// carrying the per-quad basis, material rows, and pattern parameters.
/// </summary>
public sealed class PackedScene
{
    /// <summary>
    /// Vertex positions, three floats each. Four vertices per quad, laid out as
    /// <c>l1, l1+Edge1, l1+Edge2, l1+Edge1+Edge2</c>.
    /// </summary>
    public float[] Vertices { get; }

    /// <summary>Triangle indices, six per quad (two triangles covering the quad).</summary>
    public uint[] Indices { get; }

    /// <summary>One entry per quad. Quad <c>q</c> owns triangles <c>2q</c> and <c>2q+1</c>.</summary>
    public GpuPrimitive[] Primitives { get; }

    /// <summary>
    /// Distinct materials referenced by the scene, de-duplicated by
    /// <see cref="MaterialData.Id"/>. A primitive's
    /// <see cref="GpuPrimitive.MatPrimary"/>/<see cref="GpuPrimitive.MatSecondary"/>
    /// index into this list.
    /// </summary>
    public IReadOnlyList<MaterialData> Materials { get; }

    /// <summary>Number of quads (equals <c>Primitives.Length</c>).</summary>
    public int QuadCount => Primitives.Length;

    internal PackedScene(float[] vertices, uint[] indices, GpuPrimitive[] primitives, IReadOnlyList<MaterialData> materials)
    {
        Vertices = vertices;
        Indices = indices;
        Primitives = primitives;
        Materials = materials;
    }
}

/// <summary>
/// Converts a <c>Tracable[]</c> scene into <see cref="PackedScene"/> GPU buffers.
/// Every <see cref="IQuadPrimitive"/> becomes two triangles and one
/// <see cref="GpuPrimitive"/>; anything else is skipped (the maze scene is
/// entirely quads). This keeps the GPU geometry bit-for-bit derived from the
/// same scene the CPU renderer traces.
/// </summary>
public static class GpuScenePacker
{
    /// <summary>Packs <paramref name="scene"/> into GPU buffers.</summary>
    public static PackedScene Pack(IReadOnlyList<Tracable> scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var quads = new List<IQuadPrimitive>(scene.Count);
        foreach (Tracable t in scene)
            if (t is IQuadPrimitive q)
                quads.Add(q);

        var materials = new List<MaterialData>();
        var materialIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        int GetMaterialRow(MaterialData m)
        {
            if (!materialIndex.TryGetValue(m.Id, out int row))
            {
                row = materials.Count;
                materialIndex[m.Id] = row;
                materials.Add(m);
            }
            return row;
        }

        var vertices = new float[quads.Count * 4 * 3];
        var indices = new uint[quads.Count * 6];
        var primitives = new GpuPrimitive[quads.Count];

        for (int q = 0; q < quads.Count; q++)
        {
            IQuadPrimitive quad = quads[q];
            Vector3 l1 = quad.L1;
            Vector3 e1 = quad.Edge1;
            Vector3 e2 = quad.Edge2;
            Vector3 l2 = l1 + e1;
            Vector3 l3 = l1 + e2;
            Vector3 l4 = l1 + e1 + e2;

            int vb = q * 4 * 3;
            WriteVertex(vertices, vb + 0, l1);
            WriteVertex(vertices, vb + 3, l2);
            WriteVertex(vertices, vb + 6, l3);
            WriteVertex(vertices, vb + 9, l4);

            uint baseV = (uint)(q * 4);
            int ib = q * 6;
            // Triangle A: l1, l2, l4.  Triangle B: l1, l4, l3.  Together they
            // cover the whole quad; winding is irrelevant because the tracer
            // does not cull faces and UVs are reconstructed analytically.
            indices[ib + 0] = baseV + 0;
            indices[ib + 1] = baseV + 1;
            indices[ib + 2] = baseV + 3;
            indices[ib + 3] = baseV + 0;
            indices[ib + 4] = baseV + 3;
            indices[ib + 5] = baseV + 2;

            float e1LenSq = Vector3.Dot(e1, e1);
            float e2LenSq = Vector3.Dot(e2, e2);
            Vector3 n = quad.QuadNormal;

            int matPrimary = GetMaterialRow(quad.PrimaryMaterial);
            int matSecondary = quad.SecondaryMaterial is { } sec ? GetMaterialRow(sec) : matPrimary;

            primitives[q] = new GpuPrimitive
            {
                L1X = l1.X, L1Y = l1.Y, L1Z = l1.Z,
                E1X = e1.X, E1Y = e1.Y, E1Z = e1.Z,
                E2X = e2.X, E2Y = e2.Y, E2Z = e2.Z,
                NX = n.X, NY = n.Y, NZ = n.Z,
                InvEdge1LenSq = e1LenSq > 0f ? 1f / e1LenSq : 0f,
                InvEdge2LenSq = e2LenSq > 0f ? 1f / e2LenSq : 0f,
                Pattern = (uint)quad.Pattern,
                MatPrimary = (uint)matPrimary,
                MatSecondary = (uint)matSecondary,
                P0 = quad.PatternP0,
                P1 = quad.PatternP1,
                P2 = quad.PatternP2,
                P3 = quad.PatternP3,
                Surface = (uint)quad.PrimaryMaterial.Surface,
                Ior = quad.PrimaryMaterial.CauchyA,
            };
        }

        return new PackedScene(vertices, indices, primitives, materials);
    }

    private static void WriteVertex(float[] dest, int offset, Vector3 v)
    {
        dest[offset + 0] = v.X;
        dest[offset + 1] = v.Y;
        dest[offset + 2] = v.Z;
    }
}
