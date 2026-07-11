using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// Phase 1 GPU-port parity tests. These pin the pure-C# scene packer, spectral
/// baker, and fullbright shading replica (<see cref="Phase1Reference"/>) to the
/// CPU renderer's own geometry and spectral classes. The HLSL path tracer is a
/// line-for-line port of <see cref="Phase1Reference"/>, so passing these gives
/// confidence in the GPU spectral math where the GPU itself cannot run in CI.
/// </summary>
[TestClass]
public class GpuPhase1Tests
{
    private const int DeterministicCount = 50;

    // Build a small, deterministic maze scene mirroring the CPU app's setup (CpuLauncher).
    private static (Tracable[] scene, WavelengthLookup wl, PackedScene packed, SpectralResources res) BuildScene()
    {
        var materials = new MaterialsLookup();
        var maze = new Maze(6, 6, seed: 12345);

        materials.TryGetMaterial("01138", out var goal);
        materials.TryGetMaterial("01085", out var mortar);
        materials.TryGetMaterial("01138", out var grid);

        var scene = MazeGeometryBuilder.Build(
            maze,
            wallMaterial: materials["00115"],
            floorMaterial: materials["01138"],
            ceilingMaterial: materials["01085"],
            mortarMaterial: mortar,
            ceilingGridMaterial: grid,
            goalMaterial: goal);

        var wl = new WavelengthLookup();
        var packed = GpuScenePacker.Pack(scene);
        var res = SpectralResourceBaker.Bake(wl, packed.Materials);
        return (scene, wl, packed, res);
    }

    private static void AssertClose(float expected, float actual, float tol, string what)
        => Assert.IsTrue(MathF.Abs(expected - actual) <= tol,
            $"{what}: expected {expected}, got {actual} (|Δ|={MathF.Abs(expected - actual)} > {tol})");

    private static void AssertClose(Vector3 expected, Vector3 actual, float tol, string what)
    {
        AssertClose(expected.X, actual.X, tol, what + ".X");
        AssertClose(expected.Y, actual.Y, tol, what + ".Y");
        AssertClose(expected.Z, actual.Z, tol, what + ".Z");
    }

    // ── Packer ────────────────────────────────────────────────────────

    [TestMethod]
    public void Packer_ProducesOnePrimitivePerQuad_WithMatchingBuffers()
    {
        var (scene, _, packed, _) = BuildScene();
        var quads = scene.OfType<IQuadPrimitive>().ToList();

        Assert.AreEqual(quads.Count, packed.QuadCount, "primitive count");
        Assert.AreEqual(quads.Count * 4 * 3, packed.Vertices.Length, "vertex float count");
        Assert.AreEqual(quads.Count * 6, packed.Indices.Length, "index count");
        Assert.IsTrue(quads.Count > 0, "scene should contain quads");
    }

    [TestMethod]
    public void Packer_VerticesAndBasisMatchGeometry()
    {
        var (scene, _, packed, _) = BuildScene();
        var quads = scene.OfType<IQuadPrimitive>().ToList();

        for (int q = 0; q < quads.Count; q++)
        {
            IQuadPrimitive quad = quads[q];
            GpuPrimitive p = packed.Primitives[q];

            Vector3 l1 = new(p.L1X, p.L1Y, p.L1Z);
            Vector3 e1 = new(p.E1X, p.E1Y, p.E1Z);
            Vector3 e2 = new(p.E2X, p.E2Y, p.E2Z);
            Vector3 n = new(p.NX, p.NY, p.NZ);

            AssertClose(quad.L1, l1, 1e-5f, $"prim[{q}].L1");
            AssertClose(quad.Edge1, e1, 1e-5f, $"prim[{q}].Edge1");
            AssertClose(quad.Edge2, e2, 1e-5f, $"prim[{q}].Edge2");
            AssertClose(quad.QuadNormal, n, 1e-5f, $"prim[{q}].Normal");
            Assert.AreEqual((uint)quad.Pattern, p.Pattern, $"prim[{q}].Pattern");

            // Vertices: l1, l1+e1, l1+e2, l1+e1+e2.
            int vb = q * 12;
            Vector3 v0 = new(packed.Vertices[vb + 0], packed.Vertices[vb + 1], packed.Vertices[vb + 2]);
            Vector3 v1 = new(packed.Vertices[vb + 3], packed.Vertices[vb + 4], packed.Vertices[vb + 5]);
            Vector3 v2 = new(packed.Vertices[vb + 6], packed.Vertices[vb + 7], packed.Vertices[vb + 8]);
            Vector3 v3 = new(packed.Vertices[vb + 9], packed.Vertices[vb + 10], packed.Vertices[vb + 11]);
            AssertClose(quad.L1, v0, 1e-5f, $"vert[{q}].0");
            AssertClose(quad.L1 + quad.Edge1, v1, 1e-5f, $"vert[{q}].1");
            AssertClose(quad.L1 + quad.Edge2, v2, 1e-5f, $"vert[{q}].2");
            AssertClose(quad.L1 + quad.Edge1 + quad.Edge2, v3, 1e-5f, $"vert[{q}].3");

            // InvEdge*LenSq == 1 / |edge|^2.
            AssertClose(1f / Vector3.Dot(e1, e1), p.InvEdge1LenSq, 1e-4f, $"prim[{q}].InvEdge1LenSq");
            AssertClose(1f / Vector3.Dot(e2, e2), p.InvEdge2LenSq, 1e-4f, $"prim[{q}].InvEdge2LenSq");

            // Indices reference this quad's four vertices only.
            uint baseV = (uint)(q * 4);
            for (int i = 0; i < 6; i++)
            {
                uint idx = packed.Indices[q * 6 + i];
                Assert.IsTrue(idx >= baseV && idx < baseV + 4, $"index {q * 6 + i} out of quad range");
            }
        }
    }

    [TestMethod]
    public void Packer_MaterialRowsResolveToCorrectMaterials()
    {
        var (scene, _, packed, _) = BuildScene();
        var quads = scene.OfType<IQuadPrimitive>().ToList();

        for (int q = 0; q < quads.Count; q++)
        {
            IQuadPrimitive quad = quads[q];
            GpuPrimitive p = packed.Primitives[q];

            Assert.IsTrue(p.MatPrimary < packed.Materials.Count, "primary row in range");
            Assert.IsTrue(p.MatSecondary < packed.Materials.Count, "secondary row in range");
            Assert.AreEqual(quad.PrimaryMaterial.Id, packed.Materials[(int)p.MatPrimary].Id, $"prim[{q}] primary material id");

            string expectedSecondary = (quad.SecondaryMaterial ?? quad.PrimaryMaterial).Id;
            Assert.AreEqual(expectedSecondary, packed.Materials[(int)p.MatSecondary].Id, $"prim[{q}] secondary material id");
        }
    }

    // ── Baker ─────────────────────────────────────────────────────────

    [TestMethod]
    public void Baker_TablesMatchWavelengthLookup()
    {
        var (_, wl, _, res) = BuildScene();

        Assert.AreEqual(DeterministicCount, res.DeterministicCount);
        Assert.AreEqual(wl.DeterministicCorrection, res.DeterministicCorrection, 0f, "correction");

        for (int i = 0; i < res.DeterministicCount; i++)
        {
            int expectedWl = wl.GetDeterministicWavelength(i);
            Assert.AreEqual(expectedWl, res.DeterWavelengths[i], $"deter wavelength [{i}]");

            Assert.IsTrue(wl.TryGet(expectedWl, out Vector3 xyz), $"CIE lookup [{i}]");
            var baked = new Vector3(res.DeterXYZ[i * 4 + 0], res.DeterXYZ[i * 4 + 1], res.DeterXYZ[i * 4 + 2]);
            AssertClose(xyz, baked, 0f, $"deterXYZ[{i}]");
        }
    }

    [TestMethod]
    public void Baker_MaterialReflectanceMatchesMaterialData()
    {
        var (_, _, packed, res) = BuildScene();

        Assert.AreEqual(packed.Materials.Count, res.MaterialCount);
        for (int m = 0; m < res.MaterialCount; m++)
        {
            MaterialData mat = packed.Materials[m];
            for (int i = 0; i < res.DeterministicCount; i++)
            {
                float expected = mat.GetSpectralReflectance(res.DeterWavelengths[i]);
                float baked = res.MaterialReflectance[m * res.DeterministicCount + i];
                Assert.AreEqual(expected, baked, 0f, $"reflectance[m={m},i={i}]");
            }
        }
    }

    // ── End-to-end fullbright parity ───────────────────────────────────

    [TestMethod]
    public void ShadeHit_MatchesCpuFullbright_ForEachPatternType()
    {
        var (scene, wl, packed, res) = BuildScene();
        var quads = scene.OfType<IQuadPrimitive>().ToList();

        // Pick one primitive of each pattern type present in the scene.
        foreach (QuadPattern pattern in new[] { QuadPattern.Plain, QuadPattern.Brick, QuadPattern.CeilingTile })
        {
            int q = quads.FindIndex(x => x.Pattern == pattern);
            Assert.IsTrue(q >= 0, $"scene should contain a {pattern} quad");

            var tracable = (Tracable)quads[q];
            GpuPrimitive prim = packed.Primitives[q];
            Vector3 l1 = quads[q].L1, e1 = quads[q].Edge1, e2 = quads[q].Edge2, n = quads[q].QuadNormal;

            // A spread of (u,w) targets and per-target ray offsets (straight and oblique).
            (float u, float w, float nOff, float tanU, float tanW)[] cases =
            {
                (0.5f, 0.5f, 2.0f, 0f, 0f),
                (0.20f, 0.65f, 1.5f, 0f, 0f),
                (0.5f, 0.5f, 1.5f, 0.6f, -0.3f),   // oblique → cosTheta < 1
                (0.35f, 0.42f, 1.2f, -0.4f, 0.5f), // oblique
            };

            (uint pixelHash, uint sampleIdx)[] samples =
            {
                (0u, 0u),
                (12345u, 7u),
                (4_000_000_000u, 3u),
                (987u, 49u),
            };

            foreach (var (u, w, nOff, tanU, tanW) in cases)
            {
                Vector3 target = l1 + u * e1 + w * e2;
                Vector3 e1n = Vector3.Normalize(e1);
                Vector3 e2n = Vector3.Normalize(e2);
                Vector3 origin = target + n * nOff + e1n * tanU + e2n * tanW;
                Vector3 dir = Vector3.Normalize(target - origin);

                foreach (var (pixelHash, sampleIdx) in samples)
                {
                    uint heroIdx = Phase1Reference.HeroIndex(pixelHash, sampleIdx, DeterministicCount);
                    uint stride = (uint)(DeterministicCount / Phase1Reference.CompanionCount);

                    // Hero hit gives us the exact hit point the shader would reconstruct UV from.
                    var heroRay = new Ray
                    {
                        Origin = origin,
                        Direction = dir,
                        Wavelength = res.DeterWavelengths[heroIdx],
                        Intensity = 1f,
                    };
                    var heroHit = tracable.Intersect(heroRay);
                    Assert.IsTrue(heroHit.HasValue, $"{pattern} hero ray should hit");
                    Vector3 hitPoint = heroHit.Value.Location;

                    // CPU "truth": sum CIE(wl) * primitive.Intersect(wl).reflectance over the 4 wavelengths.
                    Vector3 baseXyzTruth = Vector3.Zero;
                    for (uint k = 0; k < Phase1Reference.CompanionCount; k++)
                    {
                        uint idx = (heroIdx + k * stride) % (uint)DeterministicCount;
                        int wlK = res.DeterWavelengths[idx];
                        var ray = new Ray { Origin = origin, Direction = dir, Wavelength = wlK, Intensity = 1f };
                        var hit = tracable.Intersect(ray);
                        Assert.IsTrue(hit.HasValue, $"{pattern} companion ray should hit");
                        Assert.IsTrue(wl.TryGet(wlK, out Vector3 cie), "CIE lookup");
                        baseXyzTruth += cie * hit.Value.Reflectance;
                    }
                    baseXyzTruth /= Phase1Reference.CompanionCount;
                    Vector3 correctedTruth = baseXyzTruth * wl.DeterministicCorrection;

                    Vector3 correctedRef = Phase1Reference.ShadeHit(prim, res, dir, hitPoint, pixelHash, sampleIdx);

                    AssertClose(correctedTruth, correctedRef, 1e-4f,
                        $"{pattern} u={u} w={w} pixel={pixelHash} sample={sampleIdx}");
                }
            }
        }
    }

    [TestMethod]
    public void RotateByQuaternion_MatchesSystemNumerics()
    {
        Vector4[] quats =
        {
            new(0, 0, 0, 1),
            new(0f, MathF.Sin(MathF.PI * 0.25f), 0f, MathF.Cos(MathF.PI * 0.25f)), // 90° about Y
            Vector4.Normalize(new Vector4(0.3f, -0.5f, 0.2f, 0.8f)),
        };
        Vector3[] vecs = { new(1, 0, 0), new(0.2f, -0.7f, 0.5f), new(0, 0, 1) };

        foreach (Vector4 qv in quats)
        {
            var quat = new Quaternion(qv.X, qv.Y, qv.Z, qv.W);
            foreach (Vector3 v in vecs)
            {
                Vector3 expected = Vector3.Transform(v, quat);
                Vector3 actual = Phase1Reference.RotateByQuaternion(qv, v);
                AssertClose(expected, actual, 1e-5f, "quaternion rotate");
            }
        }
    }
}
