using Vista.Recipes;
using Vista.Rendering;
using Vista.Weather;
using Vista.World;

namespace Vista.Tests;

/// <summary>
/// The forest as a material (vista-plan §10 M1.12b): the tree line from the climate; where the rules put forest (below the line,
/// lower on poleward faces, on slopes trees hold to, away from standing water, favouring moist ground, deterministic); the
/// canopy's kernels against their published white-sky albedos; the crowns' detail within its measured envelope; and the forest's
/// detail weights, its bound only where it grows, and a crack-free mesh with crowns.
/// </summary>
[TestClass]
public sealed class ForestTests
{
    private const ulong Seed = 21;

    /// <summary>The rules with forest below 2300 m (and the grass line at 2900 m).</summary>
    private static readonly MaterialConstants K = MaterialRules.Default.Constants(2900f, 1f, 1234u, 2300f, true);

    /// <summary>A place on gentle, dry, middling ground below the tree line, with no breakup.</summary>
    private static MaterialInputs Place(float slope = 15f, float altitude = 1800f, float poleward = 0f, float area = 1000f, float water = 0f,
        float loose = 0f, float b1 = 0f, float b2 = 0f, float b3 = 0f) =>
        new(slope, poleward, altitude, loose, MathF.Log10(area), water, 0.5f, b1, b2, b3);

    private static MaterialWeights Weigh(MaterialInputs p) => MaterialRules.Evaluate(p, K);

    [TestMethod]
    public void TreeLine_FollowsTheWarmestMonth_SomeSixHundredMetresBelowTheGrassLine()
    {
        ClimateNormals zermatt = ClimateTable.For(Astronomy.Place.Zermatt);
        double line = TreeLine.AltitudeMetres(zermatt);
        Assert.AreEqual(2276.0, line, 1.0, "Zermatt: July's 14.15 °C at 1638 m, 10 °C 638 m higher");
        Assert.AreEqual(4.0 / Lapse.RateCelsiusPerMetre, GrassLine.AltitudeMetres(zermatt) - line, 1e-6, "4 °C of warmest month apart");
        Assert.IsTrue(TreeLine.AltitudeMetres(ClimateTable.For(Astronomy.Place.Lofoten)) < line - 1000.0, "a cool maritime summer keeps it low");
        Assert.AreEqual(line, Scene.GroundCover.For(zermatt, 46.0).TreeLineMetres);
        Assert.IsTrue(Scene.GroundCover.For(zermatt, 46.0).Forest, "forest grows by default");
    }

    [TestMethod]
    public void Forest_GrowsBelowItsLine_LowerOnPolewardFaces()
    {
        Assert.IsTrue(Weigh(Place()).Forest > 0.99f, "gentle ground well below the line is forest");
        MaterialWeights above = Weigh(Place(altitude: 2500f));
        Assert.IsTrue(above.Forest < 0.01f && above.Grass > 0.99f, $"above it, meadow: {above}");
        // 30° faces at the line: the equator-facing one keeps more forest than the poleward one.
        float south = Weigh(Place(slope: 30f, altitude: 2300f, poleward: -0.5f)).Forest;
        float north = Weigh(Place(slope: 30f, altitude: 2300f, poleward: 0.5f)).Forest;
        Assert.IsTrue(south > 0.8f && north < 0.2f, $"south {south}, north {north}");
        // The fine noise rags the edge: at the line the same slope is forest in one place and open in the next.
        float up = Weigh(Place(altitude: 2300f, b2: 1f)).Forest, down = Weigh(Place(altitude: 2300f, b2: -1f)).Forest;
        Assert.IsTrue(up > 0.7f && down < 0.3f, $"{up}, {down}");
    }

    [TestMethod]
    public void SteepGround_StandingWater_AndScree_HaveNoForest()
    {
        Assert.IsTrue(Weigh(Place(slope: 35f)).Forest > 0.99f, "trees hold to 35°, where grass thins out");
        Assert.IsTrue(Weigh(Place(slope: 46f)).Forest < 0.01f, "not to 46° (crag there, and too steep for trees)");
        Assert.IsTrue(Weigh(Place(slope: 55f)).Rock > 0.99f);
        MaterialWeights pond = Weigh(Place(water: 2f));
        Assert.IsTrue(pond.Forest < 0.01f && pond.Wet > 0.99f, $"a flat where water stood is wet meadow: {pond}");
        Assert.IsTrue(Weigh(Place(slope: 32f, loose: 4f)).Scree > 0.95f, "a talus below a face stays scree");
    }

    [TestMethod]
    public void MoistGround_IsFavoured_DryRidgesOpenIntoClearings()
    {
        int n = 2001;
        double hollow = 0.0, ridge = 0.0;
        for (int i = 0; i < n; i++)
        {
            float b3 = -1f + 2f * i / (n - 1);
            hollow += Weigh(Place(area: 1e5f, b3: b3)).Forest;
            ridge += Weigh(Place(area: 30f, b3: b3)).Forest;
        }
        Assert.IsTrue(hollow / n > ridge / n + 0.05, $"hollow {hollow / n:0.00}, ridge {ridge / n:0.00}");
        Assert.AreEqual(Weigh(Place(area: 1000f, b3: -0.42f)).Forest, Weigh(Place(area: 1000f, b3: -0.42f)).Forest);
        float typical = Weigh(Place(area: 1000f, b3: -0.43f)).Forest;
        Assert.IsTrue(typical is > 0.3f and < 0.7f, $"a typical hillside cell is neither dry nor moist: {typical}");
        Assert.IsTrue(Weigh(Place(b3: -0.9f)).Forest < 0.01f, "the forest's noise opens clearings");
    }

    [TestMethod]
    public void Weights_SumToOne_AndTheForestTakesOnlyTheOpenGround()
    {
        MaterialConstants none = MaterialRules.Default.Constants(2900f, 1f, 1234u, 2300f, false);
        var random = new Random(31);
        for (int i = 0; i < 20_000; i++)
        {
            var p = new MaterialInputs((float)(random.NextDouble() * 90.0), (float)(random.NextDouble() * 2.0 - 1.0), (float)(random.NextDouble() * 4000.0),
                (float)(random.NextDouble() * 5.0), (float)(1.0 + random.NextDouble() * 8.0), (float)(random.NextDouble() * 3.0), (float)random.NextDouble(),
                (float)(random.NextDouble() * 2.0 - 1.0), (float)(random.NextDouble() * 2.0 - 1.0), (float)(random.NextDouble() * 2.0 - 1.0));
            MaterialWeights w = MaterialRules.Evaluate(p, K);
            MaterialWeights bare = MaterialRules.Evaluate(p, none);
            Assert.IsTrue(w.Forest is >= 0f and <= 1f, $"{w} at {p}");
            Assert.AreEqual(1f, w.Rock + w.Scree + w.Soil + w.Grass + w.Forest, 1e-5f, $"{w} at {p}");
            Assert.AreEqual(bare.Rock, w.Rock, $"{p}");
            Assert.AreEqual(bare.Scree, w.Scree, $"{p}");
            Assert.AreEqual(bare.Wet, w.Wet, $"{p}");
            Assert.AreEqual(bare.Soil + bare.Grass, w.Soil + w.Grass + w.Forest, 1e-5f, $"{p}");
            Assert.AreEqual(w, MaterialRules.Evaluate(p, K), "a pure function");
        }
        Assert.AreNotEqual(MaterialNoise.ForestBreakup(9u, 100f, 200f, 250f), MaterialNoise.Breakup(9u, 100f, 200f, 250f, 250f).B1, "its own stream");
    }

    [TestMethod]
    public void TheKernels_IntegrateToTheirPublishedWhiteSkyAlbedos()
    {
        // White-sky albedo: (2/π)·∫∫∫ K cos θi sin θi cos θv sin θv dθi dθv dφ over the two hemispheres (Lucht, Schaaf and Strahler 2000).
        const int steps = 96;
        double h = 0.5 * Math.PI / steps, dPhi = 2.0 * Math.PI / (2 * steps);
        double vol = 0.0, geo = 0.0;
        for (int a = 0; a < steps; a++)
        {
            double ti = (a + 0.5) * h, ci = Math.Cos(ti), si = Math.Sin(ti);
            for (int b = 0; b < steps; b++)
            {
                double tv = (b + 0.5) * h, cv = Math.Cos(tv), sv = Math.Sin(tv);
                for (int c = 0; c < 2 * steps; c++)
                {
                    double phi = (c + 0.5) * dPhi;
                    double cosXi = ci * cv + si * sv * Math.Cos(phi);
                    (float kv, float kg) = CanopyBrdf.Unclamped((float)ci, (float)cv, (float)cosXi);
                    double w = ci * si * cv * sv * h * h * dPhi;
                    vol += kv * w;
                    geo += kg * w;
                }
            }
        }
        vol *= 2.0 / Math.PI;
        geo *= 2.0 / Math.PI;
        Assert.AreEqual(MaterialRules.VolumeKernelAlbedo, vol, 0.002, "RossThick");
        Assert.AreEqual(MaterialRules.GeometricKernelAlbedo, geo, 0.01, "LiSparse-Reciprocal");
        Assert.AreEqual(1.0 / (1.0 + 0.189184 * 0.5 - 1.377622 * 0.25), MaterialRules.Default.CanopyAlbedoScale, 1e-5);
    }

    [TestMethod]
    public void TheKernels_AreReciprocal_ZeroAtNadir_BrightInTheHotSpot_AndHeldAt70Degrees()
    {
        (float v0, float g0) = CanopyBrdf.Unclamped(1f, 1f, 1f);
        Assert.AreEqual(0f, v0, 1e-5f, "both kernels are 0 with the sun and the view at the zenith");
        Assert.AreEqual(0f, g0, 1e-5f);
        var random = new Random(17);
        for (int i = 0; i < 5000; i++)
        {
            float ci = (float)(0.05 + 0.95 * random.NextDouble()), cv = (float)(0.05 + 0.95 * random.NextDouble());
            float si = MathF.Sqrt(1f - ci * ci), sv = MathF.Sqrt(1f - cv * cv);
            float cosXi = ci * cv + si * sv * MathF.Cos((float)(random.NextDouble() * Math.PI));
            float k = CanopyBrdf.Kernels(ci, cv, cosXi, 0.5f, 0.25f);
            Assert.AreEqual(k, CanopyBrdf.Kernels(cv, ci, cosXi, 0.5f, 0.25f), 1e-4f, "reciprocal");
            Assert.IsTrue(float.IsFinite(k) && k >= -1f && k < 4f, $"{k} at {ci}, {cv}, {cosXi}");
        }
        // The sun 50° from the normal: seen from behind it (the hot spot), the canopy is brighter than seen across from it.
        float c50 = MathF.Cos(50f * MathF.PI / 180f), s50 = MathF.Sin(50f * MathF.PI / 180f);
        float back = CanopyBrdf.Kernels(c50, c50, 1f, 0.5f, 0.25f), forward = CanopyBrdf.Kernels(c50, c50, c50 * c50 - s50 * s50, 0.5f, 0.25f);
        Assert.IsTrue(back > 0.3f && forward < -0.3f, $"hot spot {back}, forward {forward}");
        Assert.AreEqual(CanopyBrdf.Kernels(CanopyBrdf.MinCosine, 0.8f, 0.3f, 0.5f, 0.25f), CanopyBrdf.Kernels(0.01f, 0.8f, 0.3f, 0.5f, 0.25f), "held at 70°");
    }

    [TestMethod]
    public void TheCrowns_ArePure_WithinTheirAmplitude_AndTheirEnvelopeHoldsOnFreshSegments()
    {
        var detail = new CanopyDetail(42);
        Assert.AreEqual(detail.Height(123.25, -456.5), detail.Height(123.25, -456.5));
        Assert.AreNotEqual(detail.Height(1.0, 2.0), new CanopyDetail(43).Height(1.0, 2.0));
        var random = new Random(6);
        double worst = 0.0, sum = 0.0;
        for (int i = 0; i < 20_000; i++)
        {
            double h = detail.Height(random.NextDouble() * 4000.0 - 2000.0, random.NextDouble() * 4000.0 - 2000.0);
            worst = Math.Max(worst, Math.Abs(h));
            sum += h;
        }
        Assert.IsTrue(worst <= detail.MaxAmplitude && worst > 0.5 * detail.MaxAmplitude, $"{worst} against {detail.MaxAmplitude}");
        Assert.AreEqual(0.0, sum / 20_000, 0.05 * detail.MaxAmplitude, "centred: the forest does not stand proud of the ground");
        Assert.IsTrue(detail.MaxAmplitude is > 1.0 and < 2.0, $"crowns {detail.MaxAmplitude:0.00} m: a bumpy silhouette");

        random = new Random(13);
        double previous = 0.0;
        for (double t = 0.002; t < 6.0; t *= 1.25)
        {
            double bound = CanopyDetail.Envelope.At(t);
            Assert.IsTrue(bound >= previous, $"t = {t}: the bound fell");
            previous = bound;
            for (int i = 0; i < 3_000; i++)
            {
                ulong seed = (ulong)random.NextInt64();
                double x = random.NextDouble() * 1000.0 - 500.0, y = random.NextDouble() * 1000.0 - 500.0, a = random.NextDouble() * 2.0 * Math.PI;
                double bx = x + t * Math.Cos(a), by = y + t * Math.Sin(a);
                double off = CanopyDetail.Unit(seed, 0.5 * (x + bx), 0.5 * (y + by)) - 0.5 * (CanopyDetail.Unit(seed, x, y) + CanopyDetail.Unit(seed, bx, by));
                Assert.IsTrue(Math.Abs(off) <= bound, $"t = {t}: {off} beyond {bound}");
            }
        }
    }

    /// <summary>The small world of <see cref="MaterialFieldTests"/>'s kind (un-eroded) with forest below its median height, 1000 m up.</summary>
    private static readonly Lazy<(HeightField Surface, MaterialField World, MaterialField Forested, ForestPlacement Forest)> Small = new(() =>
    {
        HeightField surface = TerrainGenerator.Generate(new Recipe { SetPieces = [SetPiece.AlpineRange] }, Seed, TestWorlds.Small);
        ErosionSettings settings = ErosionSettings.For(TerrainProfile.Mountains);
        RockHardness rock = RockHardness.For(Seed, settings);
        StrataGrid strata = StrataGrid.For(rock, surface.Size, surface.CellSize, StrataFrame.Identity, (_, _) => 0.0);
        MaterialField world = MaterialField.For(surface, null, strata, rock.Tints, MaterialRules.Default, MaterialNoise.SeedOf(Seed));
        float[] heights = surface.Heights.ToArray();
        Array.Sort(heights);
        var forest = new ForestPlacement(true, 1000f + heights[heights.Length / 2], 1, 1000f);
        return (surface, world, world.WithForest(surface, forest, MaterialRules.Default), forest);
    });

    [TestMethod]
    public void TheForestsDetailWeights_FollowTheRules_AndAWorldsOwnFieldHasNone()
    {
        (HeightField surface, MaterialField world, MaterialField forested, ForestPlacement forest) = Small.Value;
        Assert.AreSame(world, world.WithForest(surface, ForestPlacement.None, MaterialRules.Default), "no forest: the world's own field");
        Assert.AreEqual(forest, forested.Forest);
        Assert.AreEqual(0.0, world.CanopyGradient);
        Assert.IsTrue(forested.CanopyGradient > 0.0, "the small world has forest");
        MaterialField again = world.WithForest(surface, forest, MaterialRules.Default);
        int n = surface.Size, forestedSamples = 0;
        for (int j = 0; j < n; j += 3)
        {
            for (int i = 0; i < n; i += 3)
            {
                double x = surface.XOf(i), z = surface.ZOf(j);
                (double rock, double scree, double f) = forested.DetailWeightsAt(x, z);
                Assert.AreEqual((rock, scree, f), again.DetailWeightsAt(x, z), "built twice, the same");
                Assert.AreEqual((rock, scree, 0.0), world.DetailWeightsAt(x, z), "the forest leaves rock and scree");
                Assert.IsTrue(rock + scree + f <= 1.0 + 1e-12);
                forestedSamples += f > 0.5 ? 1 : 0;
                if (surface[i, j] + 1000f > forest.TreeLineMetres + 400f)
                    Assert.AreEqual(0.0, f, 1e-9, "no forest far above its line");
            }
        }
        Assert.IsTrue(forestedSamples > 20, $"{forestedSamples} forested samples");
        double extent = surface.Extent;
        Assert.IsTrue(forested.CanopyIn(surface.OriginX, surface.OriginZ, surface.OriginX + extent, surface.OriginZ + extent));
        Assert.IsFalse(world.CanopyIn(surface.OriginX, surface.OriginZ, surface.OriginX + extent, surface.OriginZ + extent));
    }

    [TestMethod]
    public void TheCanopysBound_HoldsWhereItGrows_AndTheOpenBoundElsewhere()
    {
        (HeightField surface, MaterialField world, MaterialField forested, _) = Small.Value;
        var detail = new MaterialDetail(Seed, forested);
        var bare = new MaterialDetail(Seed, world);
        Assert.IsTrue(detail.Regional && !bare.Regional);
        Assert.AreEqual(Math.Max(bare.MaxAmplitude, detail.Canopy.MaxAmplitude), detail.MaxAmplitude, "the crowns count only where forest grows");
        var random = new Random(23);
        int open = 0, inForest = 0;
        for (double length = 64.0 * Math.Sqrt(2.0); length > 0.5; length /= Math.Sqrt(2.0))
        {
            double[] kinds = detail.KindBounds(length);
            double everywhere = detail.MidpointBound(length), openBound = detail.OpenBound(length);
            Assert.AreEqual(bare.MidpointBound(length), openBound, 1e-12, $"{length:0.###} m: without forest the bound is M1.12's");
            for (int i = 0; i < 3000; i++)
            {
                double x = surface.OriginX + random.NextDouble() * surface.Extent, z = surface.OriginZ + random.NextDouble() * surface.Extent;
                double a = random.NextDouble() * 2.0 * Math.PI, bx = x + length * Math.Cos(a), bz = z + length * Math.Sin(a);
                double x0 = Math.Min(x, bx), x1 = Math.Max(x, bx), z0 = Math.Min(z, bz), z1 = Math.Max(z, bz);
                double off = Math.Abs(detail.Height(0.5 * (x + bx), 0.5 * (z + bz)) - 0.5 * (detail.Height(x, z) + detail.Height(bx, bz)));
                Assert.IsTrue(off <= detail.MidpointBoundIn(length, kinds, x0, z0, x1, z1) && off <= everywhere, $"{length:0.###} m: {off}");
                if (detail.CanopyIn(x0, z0, x1, z1))
                {
                    inForest++;
                }
                else
                {
                    Assert.IsTrue(off <= openBound, $"{length:0.###} m, no forest in its box: {off} beyond {openBound}");
                    open++;
                }
            }
        }
        Assert.IsTrue(open > 1000 && inForest > 1000, $"{open} open, {inForest} in forest");
    }

    [TestMethod]
    public void TheMesh_WithCrowns_HasNoCrack_AndWithoutForestIsM112s()
    {
        (HeightField surface, MaterialField world, MaterialField forested, _) = Small.Value;
        TerrainHierarchy hierarchy = TerrainHierarchy.Build(surface, TerrainProfile.Mountains, Seed, new MaterialDetail(Seed, forested), forested);
        foreach ((double x, double z, double yaw) in new[] { (100.0, -200.0, 40.0), (-1500.0, 900.0, 200.0), (1200.0, 1200.0, 300.0) })
        {
            var spot = new Scene.CameraSpec { Position = new Scene.WorldPoint(x, surface.HeightAt(x, z) + 4.0, z), YawDegrees = yaw, PitchDegrees = -5.0 };
            Assert.AreEqual(0, ViewMesher.TJunctions(hierarchy, MeshView.For(spot, 1280, 720, 6_361_000.0), new MeshSettings { ErrorPixels = 0.5 }), $"at ({x}, {z})");
        }
        var camera = new Scene.CameraSpec { Position = new Scene.WorldPoint(100.0, surface.HeightAt(100.0, -200.0) + 30.0, -200.0), YawDegrees = 40.0, PitchDegrees = -5.0 };
        MeshView view = MeshView.For(camera, 640, 360, 6_361_000.0);
        TerrainMesh withCrowns = ViewMesher.Build(hierarchy, view, new MeshSettings());
        Assert.AreSame(forested, withCrowns.Materials);
        TerrainHierarchy plain = TerrainHierarchy.Build(surface, TerrainProfile.Mountains, Seed, new MaterialDetail(Seed, world), world);
        TerrainHierarchy plainAgain = TerrainHierarchy.Build(surface, TerrainProfile.Mountains, Seed, new MaterialDetail(Seed, world.WithForest(surface, ForestPlacement.None, MaterialRules.Default)), world);
        TerrainMesh a = ViewMesher.Build(plain, view, new MeshSettings()), b = ViewMesher.Build(plainAgain, view, new MeshSettings());
        CollectionAssert.AreEqual(a.Positions, b.Positions, "no forest: the same mesh");
        Assert.AreNotEqual(a.TriangleCount, withCrowns.TriangleCount, "the crowns refine the forest");
    }
}
