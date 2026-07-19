using System.Collections.Generic;
using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// CPU-only tests for the static/dynamic quad partition (pinball-plan §4.2). The packer places static
/// quads first and appends every <see cref="IQuadPrimitive.Dynamic"/> quad, and
/// <see cref="GpuScenePacker.OrderQuads"/> is the single source of truth every <c>Primitives</c>-row
/// consumer (the packer, the <c>BvhSceneTracer</c> reference oracle, the headless self-test cross-check)
/// must share. A regression here silently desyncs the mover parity oracle — a hit quad would map to the
/// wrong material/normal — so it is pinned here device-free, where the GPU-only <c>--flipper-demo</c> can't.
/// </summary>
[TestClass]
public class GpuScenePackerTests
{
    private static MaterialData Mat(string id)
    {
        int[] wl = [360, 830];
        float[] v = [0.5f, 0.5f];
        return new MaterialData(id, id, null, null, 0f, 0f, 0f, 0f, 0.5f, 0.5f, 0.5f, 0f, 0f,
            new SpectralData(wl, v, (float[])v.Clone()), SurfaceKind.Diffuse);
    }

    // A quad in the YZ plane at world-x, so its GpuPrimitive.L1X == x uniquely identifies it after packing.
    private static TracableRectangle Quad(float x, bool dyn)
        => new((new Vector3(x, 0f, 0f), new Vector3(x, 1f, 0f), new Vector3(x, 0f, 1f)), Mat($"q{x}")) { Dynamic = dyn };

    private static TracableRectangle Quad(float x, int group)
        => new((new Vector3(x, 0f, 0f), new Vector3(x, 1f, 0f), new Vector3(x, 0f, 1f)), Mat($"q{x}")) { Dynamic = true, DynamicGroup = group };

    [TestMethod]
    public void Pack_NoDynamicQuads_StaticCountEqualsQuadCount_AndSceneOrderPreserved()
    {
        var scene = new Tracable[] { Quad(0f, false), Quad(1f, false), Quad(2f, false) };
        PackedScene packed = GpuScenePacker.Pack(scene);

        Assert.AreEqual(3, packed.QuadCount);
        Assert.AreEqual(packed.QuadCount, packed.StaticQuadCount, "no dynamic quads ⇒ boundary == count");
        for (int i = 0; i < 3; i++)
            Assert.AreEqual((float)i, packed.Primitives[i].L1X, "static-only scene keeps authoring order");
    }

    [TestMethod]
    public void Pack_MidSceneDynamicQuad_AppendedLast_StaticQuadsCompactFirst()
    {
        // A(static, x=0)  B(dynamic, x=1)  C(static, x=2): the packer must emit static [A, C] then dynamic [B],
        // NOT authoring order [A, B, C]. This is the exact case the reference-oracle desync would corrupt.
        var scene = new Tracable[] { Quad(0f, false), Quad(1f, true), Quad(2f, false) };
        PackedScene packed = GpuScenePacker.Pack(scene);

        Assert.AreEqual(3, packed.QuadCount);
        Assert.AreEqual(2, packed.StaticQuadCount);
        Assert.AreEqual(0f, packed.Primitives[0].L1X, "static A first");
        Assert.AreEqual(2f, packed.Primitives[1].L1X, "static C second (B must not sit here)");
        Assert.AreEqual(1f, packed.Primitives[2].L1X, "dynamic B appended last");
        Assert.AreEqual(0u, packed.Primitives[0].Dynamic);
        Assert.AreEqual(0u, packed.Primitives[1].Dynamic);
        Assert.AreEqual(1u, packed.Primitives[2].Dynamic);
    }

    [TestMethod]
    public void OrderQuads_IndexMatchesPackedPrimitivesRow()
    {
        // Interleaved dynamic/static: the row a hit quad maps to (its OrderQuads index) must address its own
        // Primitives row — this is the invariant BvhSceneTracer and the self-test rely on.
        var scene = new Tracable[] { Quad(0f, true), Quad(1f, false), Quad(2f, true), Quad(3f, false) };
        PackedScene packed = GpuScenePacker.Pack(scene);
        List<IQuadPrimitive> ordered = GpuScenePacker.OrderQuads(scene, out int staticCount);

        Assert.AreEqual(packed.StaticQuadCount, staticCount);
        Assert.AreEqual(packed.QuadCount, ordered.Count);
        Assert.AreEqual(2, staticCount, "two static quads (x=1, x=3)");
        for (int i = 0; i < ordered.Count; i++)
            Assert.AreEqual(ordered[i].L1.X, packed.Primitives[i].L1X,
                "OrderQuads[i] must correspond to Primitives[i]");
    }

    [TestMethod]
    public void Pack_DynamicGroups_BecomeContiguousDynamicParts()
    {
        // static(x=0), dynamic group 1 (x=1), static (x=2), dynamic group 0 (x=3), dynamic group 1 (x=4).
        // Packed: statics [0,2] first, then dynamics grouped by group → group 0 (x=3), then group 1 (x=1, x=4).
        var scene = new Tracable[] { Quad(0f, false), Quad(1f, 1), Quad(2f, false), Quad(3f, 0), Quad(4f, 1) };
        PackedScene packed = GpuScenePacker.Pack(scene);

        Assert.AreEqual(2, packed.StaticQuadCount);
        Assert.AreEqual(0f, packed.Primitives[0].L1X);
        Assert.AreEqual(2f, packed.Primitives[1].L1X);
        Assert.AreEqual(3f, packed.Primitives[2].L1X, "group 0 first");
        Assert.AreEqual(1f, packed.Primitives[3].L1X, "group 1, authoring order kept (x=1 before x=4)");
        Assert.AreEqual(4f, packed.Primitives[4].L1X);

        Assert.AreEqual(2, packed.DynamicParts.Count, "two mover parts");
        Assert.AreEqual((2, 1), packed.DynamicParts[0], "group 0: one quad starting at row 2");
        Assert.AreEqual((3, 2), packed.DynamicParts[1], "group 1: two quads starting at row 3");
    }

    [TestMethod]
    public void OrderQuads_IgnoresNonQuadTracables()
    {
        // Spheres are packed separately; OrderQuads must skip them so quad indices stay dense and aligned.
        var scene = new Tracable[]
        {
            Quad(0f, false),
            new Sphere(new Vector3(9f, 9f, 9f), 1f, Mat("s")),
            Quad(1f, true),
        };
        List<IQuadPrimitive> ordered = GpuScenePacker.OrderQuads(scene, out int staticCount);

        Assert.AreEqual(2, ordered.Count);
        Assert.AreEqual(1, staticCount);
        Assert.AreEqual(0f, ordered[0].L1.X);
        Assert.AreEqual(1f, ordered[1].L1.X);
    }
}
