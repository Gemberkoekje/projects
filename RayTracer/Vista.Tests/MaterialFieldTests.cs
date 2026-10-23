using Vista.Recipes;
using Vista.World;

namespace Vista.Tests;

/// <summary>
/// The world's material field (vista-plan §10 M1.12): the erosion's maps and the rock's beds packed as the GPU reads them, decoded
/// alike; the detail kinds' weights per sample and how fast they change; and the detail by material the mesh adds below the grid,
/// within its bound.
/// </summary>
[TestClass]
public sealed class MaterialFieldTests
{
    private const ulong Seed = 21;

    /// <summary>A small eroded-looking world: the base terrain, scree under its steep ground, a wet band, and its drainage.</summary>
    private static readonly Lazy<(HeightField Surface, ErosionResult Erosion, StrataGrid Strata, RockHardness Rock, MaterialField Field)> World = new(() =>
    {
        HeightField surface = TerrainGenerator.Generate(new Recipe { SetPieces = [SetPiece.AlpineRange] }, Seed, TestWorlds.Small);
        int n = surface.Size, cells = n * n;
        var loose = new float[cells];
        var water = new float[cells];
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                loose[j * n + i] = i < n / 3 ? 6f : i < 2 * n / 3 ? 0.02f : 0.4f;
                water[j * n + i] = j is > 20 and < 26 ? 1.2f : 0.01f;
            }
        }
        var erosion = new ErosionResult(surface, loose, new float[cells], water, new float[cells], 1, DrainageMap.Of(surface), new float[cells], StrataFrame.Identity);
        ErosionSettings settings = ErosionSettings.For(TerrainProfile.Mountains);
        RockHardness rock = RockHardness.For(Seed, settings);
        StrataGrid strata = StrataGrid.For(rock, n, surface.CellSize, StrataFrame.Identity, (_, _) => 0.0);
        return (surface, erosion, strata, rock, MaterialField.For(surface, erosion, strata, rock.Tints, MaterialRules.Default, MaterialNoise.SeedOf(Seed)));
    });

    [TestMethod]
    public void Codes_RoundTrip_WithinAStep()
    {
        Assert.AreEqual(0f, MaterialField.DepthOf(MaterialField.DepthCode(0f)), 1e-6f);
        foreach (float d in new[] { 0.05f, 0.3f, 1f, 7f, 40f, 99f })
            Assert.AreEqual(d, MaterialField.DepthOf(MaterialField.DepthCode(d)), 0.04f * d, $"{d} m");
        foreach (float a in new[] { 1.8f, 3.3f, 5f, 8.4f })
            Assert.AreEqual(a, MaterialField.AreaOf(MaterialField.AreaCode(a)), 0.5f * MaterialField.AreaLogSpan / 255f + 1e-4f);
        foreach (float m in new[] { -0.2f, -0.08f, 0f, 0.05f, 0.24f })
            Assert.AreEqual(m, MaterialField.MottleOf(MaterialField.MottleCode(m)), 0.5f * MaterialField.MottleRange / 127f + 1e-6f);
        Assert.AreEqual(255u, MaterialField.DepthCode(1000f), "clamped");
    }

    [TestMethod]
    public void AtASample_TheMapsDecodeToWhatWasStored_AndTheBedToTheStrata()
    {
        (HeightField surface, ErosionResult erosion, StrataGrid strata, _, MaterialField field) = World.Value;
        MaterialConstants k = field.Apply(MaterialRules.Default.Constants(2900f, 1f, field.NoiseSeed, 0f, false));
        int n = surface.Size;
        foreach ((int i, int j) in new[] { (10, 32), (32, 23), (50, 40) })
        {
            int c = j * n + i;
            float x = (float)surface.XOf(i), z = (float)surface.ZOf(j);
            MaterialInputs p = field.Inputs(x, z, surface[i, j], 0f, 0f, 1000f, k);
            Assert.AreEqual(erosion.Loose[c], p.LooseMetres, 0.04f * erosion.Loose[c] + 1e-3f, $"loose at ({i}, {j})");
            Assert.AreEqual(erosion.WaterDepth[c], p.WaterDepthMetres, 0.04f * erosion.WaterDepth[c] + 1e-3f, $"water at ({i}, {j})");
            Assert.AreEqual((float)Math.Log10(erosion.Drainage.Area(c)), p.Log10Area, MaterialField.AreaLogSpan / 255f, $"area at ({i}, {j})");
            Assert.AreEqual(strata.At(c, surface[i, j]), p.Hardness, 0.003f, $"hardness at ({i}, {j})");
            Assert.AreEqual(1000f + surface[i, j], p.AltitudeMetres);
            Assert.AreEqual(0f, p.SlopeDegrees, "a flat normal");
        }
    }

    [TestMethod]
    public void TheMapsFadeAtTheGridsEdge_AndAwayFromItTheGroundReadsTheDefaults()
    {
        (HeightField surface, _, _, _, MaterialField field) = World.Value;
        MaterialConstants k = field.Apply(MaterialRules.Default.Constants(2900f, 1f, field.NoiseSeed, 0f, false));
        MaterialInputs edge = field.Inputs((float)surface.XOf(0), (float)surface.ZOf(32), 500f, 0f, 0f, 0f, k);
        Assert.AreEqual(0f, edge.LooseMetres, 1e-3f, "scree stored at the edge fades with the surface");
        MaterialInputs away = field.Inputs(1e5f, 0f, 500f, 0f, 0f, 0f, k);
        Assert.AreEqual((0f, MaterialField.AreaLogFloor, 0f, MaterialField.DefaultHardness), (away.LooseMetres, away.Log10Area, away.WaterDepthMetres, away.Hardness));
    }

    [TestMethod]
    public void TheField_IsAPureFunctionOfTheWorld_AndAnUnerodedWorldStillHasBeds()
    {
        (HeightField surface, ErosionResult erosion, StrataGrid strata, RockHardness rock, MaterialField field) = World.Value;
        MaterialField again = MaterialField.For(surface, erosion, strata, rock.Tints, MaterialRules.Default, MaterialNoise.SeedOf(Seed));
        CollectionAssert.AreEqual(field.Packed.ToArray(), again.Packed.ToArray());
        CollectionAssert.AreEqual(field.Beds.ToArray(), again.Beds.ToArray());
        Assert.AreEqual(field.RockGradient, again.RockGradient);
        Assert.IsTrue(field.Eroded);

        MaterialField bare = MaterialField.For(surface, null, strata, rock.Tints, MaterialRules.Default, MaterialNoise.SeedOf(Seed));
        Assert.IsFalse(bare.Eroded);
        var hardness = new HashSet<float>();
        for (float y = 0f; y < 2000f; y += 7f)
            hardness.Add(bare.BedAt(0f, 0f, y).Hardness);
        Assert.IsTrue(hardness.Count > 15, $"beds of many hardnesses up the column: {hardness.Count}");
        Assert.ThrowsExactly<ArgumentException>(() => MaterialField.For(surface, erosion, strata, [0f, 1f], MaterialRules.Default, 1u));
    }

    [TestMethod]
    public void TheDetailWeights_StayInRange_AndChangeNoFasterThanTheirGradients()
    {
        (HeightField surface, _, _, _, MaterialField field) = World.Value;
        Assert.IsTrue(field.RockGradient > 0.0 && field.ScreeGradient > 0.0, "the small world has rock and scree");
        var random = new Random(4);
        double extent = surface.Extent;
        for (int t = 0; t < 20_000; t++)
        {
            double x = surface.OriginX + random.NextDouble() * extent, z = surface.OriginZ + random.NextDouble() * extent;
            double length = random.NextDouble() * 300.0, a = random.NextDouble() * 2.0 * Math.PI;
            (double rock, double scree, _) = field.DetailWeightsAt(x, z);
            Assert.IsTrue(rock is >= 0.0 and <= 1.0 && scree is >= 0.0 and <= 1.0 && rock + scree <= 1.0 + 1e-12);
            (double rock2, double scree2, _) = field.DetailWeightsAt(x + length * Math.Cos(a), z + length * Math.Sin(a));
            Assert.IsTrue(Math.Abs(rock2 - rock) <= field.RockGradient * length + 1e-9, "rock");
            Assert.IsTrue(Math.Abs(scree2 - scree) <= field.ScreeGradient * length + 1e-9, "scree");
            Assert.IsTrue(Math.Abs(rock2 + scree2 - rock - scree) <= field.SmoothGradient * length + 1e-9, "smooth");
        }
    }

    [TestMethod]
    public void TheSamplesWeights_CoverEverySample()
    {
        (HeightField surface, _, _, _, MaterialField field) = World.Value;
        MaterialWeights[] weights = field.SampleWeights(surface, field.Apply(MaterialRules.Default.Constants(2900f, 1f, field.NoiseSeed, 0f, false)), 1000f);
        Assert.AreEqual(surface.Size * surface.Size, weights.Length);
        Assert.IsTrue(weights.All(w => Math.Abs(w.Rock + w.Scree + w.Soil + w.Grass - 1f) < 1e-5f));
        Assert.IsTrue(weights.Any(w => w.Scree > 0.5f) && weights.Any(w => w.Rock > 0.5f) && weights.Any(w => w.Grass > 0.5f));
    }

    [TestMethod]
    public void TheDetailKinds_ArePureFunctions_WithinTheirAmplitudes()
    {
        foreach (ISurfaceDetail detail in new ISurfaceDetail[] { new RockDetail(42, 8.0), new ScreeDetail(42, 8.0) })
        {
            string name = detail.GetType().Name;
            Assert.AreEqual(detail.Height(123.25, -456.5), detail.Height(123.25, -456.5), name);
            var random = new Random(6);
            double worst = 0.0;
            for (int i = 0; i < 20_000; i++)
                worst = Math.Max(worst, Math.Abs(detail.Height(random.NextDouble() * 4000.0 - 2000.0, random.NextDouble() * 4000.0 - 2000.0)));
            Assert.IsTrue(worst <= detail.MaxAmplitude && worst > 0.1 * detail.MaxAmplitude, $"{name}: {worst} against {detail.MaxAmplitude}");
        }
        Assert.AreNotEqual(new RockDetail(42, 8.0).Height(1.0, 2.0), new RockDetail(43, 8.0).Height(1.0, 2.0));
        Assert.AreEqual(8.0, new RockDetail(42, 8.0).CoarsestWavelength, "the rock's crests start at the grid's cell");
    }

    [TestMethod]
    public void TheMeasuredEnvelopes_HoldOnFreshSegments_AndNeverFall()
    {
        foreach ((string name, Func<ulong, double, double, double> unit, DetailEnvelope envelope) in new (string, Func<ulong, double, double, double>, DetailEnvelope)[]
        {
            ("rock", RockDetail.Unit, RockDetail.Envelope),
            ("scree", ScreeDetail.Unit, ScreeDetail.Envelope),
        })
        {
            var random = new Random(13);
            double previous = 0.0;
            for (double t = 0.002; t < 6.0; t *= 1.25)
            {
                double bound = envelope.At(t);
                Assert.IsTrue(bound >= previous, $"{name}, t = {t}: the bound fell");
                previous = bound;
                for (int i = 0; i < 3_000; i++)
                {
                    ulong seed = (ulong)random.NextInt64();
                    double x = random.NextDouble() * 1000.0 - 500.0, y = random.NextDouble() * 1000.0 - 500.0, a = random.NextDouble() * 2.0 * Math.PI;
                    double bx = x + t * Math.Cos(a), by = y + t * Math.Sin(a);
                    double off = unit(seed, 0.5 * (x + bx), 0.5 * (y + by)) - 0.5 * (unit(seed, x, y) + unit(seed, bx, by));
                    Assert.IsTrue(Math.Abs(off) <= bound, $"{name}, t = {t}: {off} beyond {bound}");
                }
            }
        }
    }

    [TestMethod]
    public void TheDetailByMaterial_BlendsItsKinds_WithinItsBound_AtEveryLevelOfTheMesh()
    {
        (HeightField surface, _, _, _, MaterialField field) = World.Value;
        var detail = new MaterialDetail(Seed, field);
        var random = new Random(8);
        for (int i = 0; i < 2000; i++)
        {
            double x = surface.OriginX + random.NextDouble() * surface.Extent, z = surface.OriginZ + random.NextDouble() * surface.Extent;
            (double rock, double scree, _) = field.DetailWeightsAt(x, z);
            double expected = (1.0 - rock - scree) * detail.Smooth.Height(x, z) + rock * detail.Rock.Height(x, z) + scree * detail.Scree.Height(x, z);
            Assert.AreEqual(expected, detail.Height(x, z), 1e-9);
        }
        double previous = double.PositiveInfinity;
        // The mesh's edges, from the grid's diagonal down to its finest below the grid (the grid's cell is 64 m here).
        for (double length = 64.0 * Math.Sqrt(2.0); length > 64.0 / 128.0; length /= Math.Sqrt(2.0))
        {
            double bound = detail.MidpointBound(length);
            Assert.IsTrue(bound <= previous, $"{length} m: the bound grew as the edge shrank");
            previous = bound;
            for (int i = 0; i < 3000; i++)
            {
                double x = surface.OriginX + random.NextDouble() * surface.Extent, z = surface.OriginZ + random.NextDouble() * surface.Extent, a = random.NextDouble() * 2.0 * Math.PI;
                double bx = x + length * Math.Cos(a), bz = z + length * Math.Sin(a);
                double off = detail.Height(0.5 * (x + bx), 0.5 * (z + bz)) - 0.5 * (detail.Height(x, z) + detail.Height(bx, bz));
                Assert.IsTrue(Math.Abs(off) <= bound, $"{length:0.###} m: {off} beyond {bound}");
            }
        }
    }

    [TestMethod]
    public void TheBoundByRegion_HoldsInItsBox_NeverPassesTheBoundEverywhere_AndNests()
    {
        (HeightField surface, _, _, _, MaterialField field) = World.Value;
        var detail = new MaterialDetail(Seed, field);
        var random = new Random(9);
        int single = 0;
        for (double length = 64.0 * Math.Sqrt(2.0); length > 0.5; length /= Math.Sqrt(2.0))
        {
            double[] kinds = detail.KindBounds(length);
            double everywhere = detail.MidpointBound(length);
            for (int i = 0; i < 3000; i++)
            {
                double x = surface.OriginX + random.NextDouble() * surface.Extent, z = surface.OriginZ + random.NextDouble() * surface.Extent;
                double a = random.NextDouble() * 2.0 * Math.PI, bx = x + length * Math.Cos(a), bz = z + length * Math.Sin(a);
                // The segment's own box, and one around it twice as wide.
                double x0 = Math.Min(x, bx), x1 = Math.Max(x, bx), z0 = Math.Min(z, bz), z1 = Math.Max(z, bz);
                double inner = detail.MidpointBoundIn(length, kinds, x0, z0, x1, z1);
                double outer = detail.MidpointBoundIn(length, kinds, x0 - length, z0 - length, x1 + length, z1 + length);
                Assert.IsTrue(inner <= outer && outer <= everywhere + 1e-12, $"{length:0.###} m: {inner}, {outer}, {everywhere}");
                double off = detail.Height(0.5 * (x + bx), 0.5 * (z + bz)) - 0.5 * (detail.Height(x, z) + detail.Height(bx, bz));
                Assert.IsTrue(Math.Abs(off) <= inner, $"{length:0.###} m: {off} beyond {inner} in its box");
                DetailRegion region = field.RegionIn(x0, z0, x1, z1);
                if ((region.Kinds & (region.Kinds - 1)) == 0)
                    single++;
            }
        }
        Assert.IsTrue(single > 0, "some segments lie where one kind alone reaches");
    }

    [TestMethod]
    public void TheMesh_WithTheDetailByMaterial_HasNoCrack()
    {
        (HeightField surface, _, _, _, MaterialField field) = World.Value;
        TerrainHierarchy hierarchy = TerrainHierarchy.Build(surface, TerrainProfile.Mountains, Seed, new MaterialDetail(Seed, field), field);
        foreach ((double x, double z, double yaw) in new[] { (100.0, -200.0, 40.0), (-1500.0, 900.0, 200.0), (1200.0, 1200.0, 300.0) })
        {
            var spot = new Scene.CameraSpec { Position = new Scene.WorldPoint(x, surface.HeightAt(x, z) + 4.0, z), YawDegrees = yaw, PitchDegrees = -5.0 };
            Assert.AreEqual(0, ViewMesher.TJunctions(hierarchy, MeshView.For(spot, 1280, 720, 6_361_000.0), new MeshSettings { ErrorPixels = 0.5 }), $"at ({x}, {z})");
        }
        var camera = new Scene.CameraSpec { Position = new Scene.WorldPoint(100.0, surface.HeightAt(100.0, -200.0) + 4.0, -200.0), YawDegrees = 40.0, PitchDegrees = -5.0 };
        MeshView view = MeshView.For(camera, 640, 360, 6_361_000.0);
        Assert.AreEqual(0, ViewMesher.TJunctions(hierarchy, view, new MeshSettings()));
        TerrainMesh mesh = ViewMesher.Build(hierarchy, view, new MeshSettings());
        Assert.AreSame(field, mesh.Materials, "the mesh carries the world's field to the shader");
        Assert.IsTrue(mesh.Info.Materials);
    }
}
