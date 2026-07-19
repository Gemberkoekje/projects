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

    /// <summary>
    /// Number of <b>static</b> quads, which are packed first (indices <c>[0, StaticQuadCount)</c>); any
    /// <see cref="IQuadPrimitive.Dynamic"/> quads (flippers / plunger — pinball-plan §4.2) are appended
    /// after them (<c>[StaticQuadCount, QuadCount)</c>). The renderer builds the static triangle BLAS from
    /// the first range and a separate <i>dynamic</i> triangle BLAS (a movable TLAS instance) from the
    /// second, so rigid movers can be posed via <c>SetDynamicPose</c>. Equals <see cref="QuadCount"/> for
    /// a scene with no dynamic quads, so those scenes pack and render exactly as before.
    /// </summary>
    public int StaticQuadCount { get; }

    /// <summary>
    /// One <c>(Start, Count)</c> range per independently-posable mover (pinball-plan §4.2 / P6), over the
    /// appended dynamic quads <c>[StaticQuadCount, QuadCount)</c> — grouped by <see cref="IQuadPrimitive.DynamicGroup"/>.
    /// The renderer builds one movable triangle BLAS / TLAS instance per range, so the left flipper, right
    /// flipper and plunger get distinct <c>SetDynamicPose</c> handles. Empty when the scene has no dynamic quads.
    /// </summary>
    public IReadOnlyList<(int Start, int Count)> DynamicParts { get; }

    /// <summary>Analytic spheres (spectral-effects-plan §0.4), carried on the GPU as
    /// procedural-AABB geometry. Empty for the (common) quad-only scenes.</summary>
    public IReadOnlyList<GpuSphere> Spheres { get; }

    internal PackedScene(float[] vertices, uint[] indices, GpuPrimitive[] primitives,
        IReadOnlyList<MaterialData> materials, IReadOnlyList<GpuSphere> spheres, int staticQuadCount,
        IReadOnlyList<(int Start, int Count)> dynamicParts)
    {
        Vertices = vertices;
        Indices = indices;
        Primitives = primitives;
        Materials = materials;
        Spheres = spheres;
        StaticQuadCount = staticQuadCount;
        DynamicParts = dynamicParts;
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
    /// <summary>
    /// The scene's quads in <b>packed order</b> — static quads first (stable), then every
    /// <see cref="IQuadPrimitive.Dynamic"/> quad appended (stable), matching the row order of
    /// <see cref="PackedScene.Primitives"/>. <paramref name="staticQuadCount"/> receives the boundary
    /// (= <see cref="PackedScene.StaticQuadCount"/>). <b>Any</b> consumer that maps a hit quad back to a
    /// <see cref="PackedScene.Primitives"/> row (the CPU reference oracle <c>BvhSceneTracer</c>, the headless
    /// self-test cross-check) MUST index via this order — indexing by raw scene order desyncs the moment a
    /// <c>Dynamic</c> quad is not already last in the scene, because §4.2 moves it to the tail. Non-quad
    /// tracables (spheres, etc.) are skipped, exactly as <see cref="Pack"/> does.
    /// </summary>
    public static List<IQuadPrimitive> OrderQuads(IReadOnlyList<Tracable> scene, out int staticQuadCount)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var statics = new List<IQuadPrimitive>(scene.Count);
        var dynamics = new List<IQuadPrimitive>();
        foreach (Tracable t in scene)
            if (t is IQuadPrimitive q)
                (q.Dynamic ? dynamics : statics).Add(q);
        staticQuadCount = statics.Count;
        // Dynamic quads grouped by DynamicGroup (OrderBy is a stable sort, so authoring order is kept within
        // a group) → each mover part is a contiguous Primitives range (§4.2 / P6).
        statics.AddRange(dynamics.OrderBy(q => q.DynamicGroup));
        return statics;
    }

    /// <summary>Packs <paramref name="scene"/> into GPU buffers.</summary>
    public static PackedScene Pack(IReadOnlyList<Tracable> scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        // Static quads first, then Dynamic quads (flippers/plunger — §4.2), each stable within its group,
        // so the primitives split cleanly into a static range [0, staticQuadCount) + a dynamic range. The
        // renderer builds a separate movable BLAS from the dynamic range. No dynamic quads → order unchanged.
        List<IQuadPrimitive> quads = OrderQuads(scene, out int staticQuadCount);

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

            var (p0, p1, p2, cauchyB) = OpticalParams(quad.PrimaryMaterial, quad.PatternP0, quad.PatternP1, quad.PatternP2);

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
                P0 = p0,
                P1 = p1,
                P2 = p2,
                P3 = quad.PatternP3,
                Surface = (uint)quad.PrimaryMaterial.Surface,
                Ior = quad.PrimaryMaterial.CauchyA,
                CauchyB = cauchyB,
                Dynamic = quad.Dynamic ? 1u : 0u,
            };
        }

        // Analytic spheres (§0.4) — carried as procedural-AABB geometry on the GPU.
        var spheres = new List<GpuSphere>();
        foreach (Tracable t in scene)
        {
            if (t is not Sphere s) continue;
            var (sp0, sp1, sp2, sCauchyB) = OpticalParams(s.Material, 0f, 0f, 0f);
            spheres.Add(new GpuSphere
            {
                CX = s.Center.X, CY = s.Center.Y, CZ = s.Center.Z, Radius = s.Radius,
                MatRow = (uint)GetMaterialRow(s.Material),
                Surface = (uint)s.Material.Surface,
                Ior = s.Material.CauchyA,
                CauchyB = sCauchyB,
                P0 = sp0, P1 = sp1, P2 = sp2,
                Dynamic = s.Dynamic ? 1u : 0u,
            });
        }

        // Split the appended dynamic range into one contiguous part per DynamicGroup (§4.2 / P6).
        var dynamicParts = new List<(int Start, int Count)>();
        for (int i = staticQuadCount; i < quads.Count;)
        {
            int group = quads[i].DynamicGroup;
            int start = i;
            while (i < quads.Count && quads[i].DynamicGroup == group)
                i++;
            dynamicParts.Add((start, i - start));
        }

        return new PackedScene(vertices, indices, primitives, materials, spheres, staticQuadCount, dynamicParts);
    }

    private static void WriteVertex(float[] dest, int offset, Vector3 v)
    {
        dest[offset + 0] = v.X;
        dest[offset + 1] = v.Y;
        dest[offset + 2] = v.Z;
    }

    // The P0..P2 slots are pattern parameters for a Plain quad, so an optical surface (never
    // patterned) reuses them: thin-film carries film amp/contrast (§2.2), a dielectric carries
    // the Beer–Lambert absorption anchors σ(R/G/B)+base (§2.3). CauchyB doubles as the thin-film
    // thickness (§2.2). Shared by quads and spheres so both pack identically.
    private static (float p0, float p1, float p2, float cauchyB) OpticalParams(
        MaterialData mat, float patternP0, float patternP1, float patternP2)
    {
        (float p0, float p1, float p2) = mat.Surface switch
        {
            SurfaceKind.ThinFilm => (mat.FilmSpatialAmpNm, mat.FilmContrast, patternP2),
            SurfaceKind.Dielectric => (
                mat.AbsorptionRgb.X + mat.ExtinctionSigma,
                mat.AbsorptionRgb.Y + mat.ExtinctionSigma,
                mat.AbsorptionRgb.Z + mat.ExtinctionSigma),
            _ => (patternP0, patternP1, patternP2),
        };
        // Thin-film and bubble surfaces (§2.2, §2.6) carry the film thickness (nm) in the CauchyB
        // slot; a dielectric uses it for real dispersion.
        float cauchyB = mat.Surface is SurfaceKind.ThinFilm or SurfaceKind.Bubble
            ? mat.FilmThicknessNm
            : mat.CauchyB;
        return (p0, p1, p2, cauchyB);
    }
}
