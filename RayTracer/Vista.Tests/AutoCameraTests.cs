using Vista.Astronomy;
using Vista.Cli;
using Vista.Composition;
using Vista.Output;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;
using Vista.World;

namespace Vista.Tests;

/// <summary>
/// The auto camera (vista-plan §10 M1.14) on synthetic worlds: the CPU tracer's hits against geometry and the Earth's curve, sight
/// lines, the valley floors; the plant (the peak, the range's axis, the sun aside); the candidates (above the ground, seeing the peak,
/// the same every time); the command line; and the preview with it, dry and against the fake renderer.
/// </summary>
[TestClass]
public sealed class AutoCameraTests
{
    private const double Radius = 6_371_000.0;

    private static readonly SunPosition Evening = new(270.0, 3.0, 3.0, 0.0, 0.0);

    /// <summary>A world of <paramref name="size"/>² samples <paramref name="cell"/> m apart whose height <paramref name="height"/> gives.</summary>
    private static HeightField Field(int size, double cell, Func<double, double, double> height)
    {
        var heights = new float[size * size];
        double origin = -0.5 * (size - 1) * cell;
        for (int j = 0; j < size; j++)
        {
            for (int i = 0; i < size; i++)
                heights[j * size + i] = (float)height(origin + i * cell, origin + j * cell);
        }
        return new HeightField(size, cell, heights);
    }

    /// <summary>A peak 2 km above a 200 m plain at (1024, 512), its height falling off over 1.5 km, on a 16.4 km world.</summary>
    private static readonly Lazy<HeightField> PeakWorld = new(() => Field(257, 64.0,
        (x, z) => 200.0 + 2000.0 * Math.Exp(-Math.Sqrt((x - 1024) * (x - 1024) + (z - 512) * (z - 512)) / 1500.0)));

    [TestMethod]
    public void Trace_FlatGround_HitsWhereTheGeometrySays()
    {
        var sight = new TerrainSight(Field(65, 64.0, (_, _) => 0.0), null, Radius);
        var camera = new CameraSpec { Position = new WorldPoint(0, 100, 0), PitchDegrees = -30.0, FocalLengthMm = 50.0 };
        CompositionBuffer buffer = sight.Trace(camera, 33, 19);
        // The middle pixel's ray (straight ahead) meets the ground 100/sin 30° away; the curve lowers it by 2 mm there.
        Assert.AreEqual(200.0, buffer.Depth[9 * 33 + 16], 0.1);
        Assert.AreEqual(1f, buffer.Normal[(9 * 33 + 16) * 3 + 1], 1e-6f);
        Assert.AreEqual(GroundClass.Rock, buffer.Class[9 * 33 + 16], "rock without a classifier");
        Assert.IsTrue(float.IsNaN(buffer.Rise[9 * 33 + 16]), "no floors, no rise");
    }

    [TestMethod]
    public void Hit_AStepInFront_AtItsFoot()
    {
        // The ground rises 500 m over one 64 m cell from x = 960: a level ray 100 m up meets it a fifth of the way up.
        var sight = new TerrainSight(Field(65, 64.0, (x, _) => x > 1000 ? 500.0 : 0.0), null, Radius);
        double t = sight.Hit(new WorldPoint(0, 100, 0), new Vector3D(1, 0, 0));
        Assert.AreEqual(960.0 + 0.2 * 64.0, t, 1.0);
    }

    [TestMethod]
    public void Sees_ARidgeBetweenBlocks_TheEarthsCurveHides()
    {
        HeightField ridge = Field(65, 64.0, (x, _) => Math.Abs(x) < 100 ? 300.0 : 0.0);
        var eye = new WorldPoint(-1500, 50, 0);
        var target = new WorldPoint(1500, 50, 0);
        Assert.IsFalse(new TerrainSight(ridge, null, Radius).Sees(eye, target, 10.0));
        Assert.IsTrue(new TerrainSight(Field(65, 64.0, (_, _) => 0.0), null, Radius).Sees(eye, target, 10.0));

        // A ray 0.01° down from 2 m over level ground would meet it 11.5 km out on a flat Earth; the curve drops it away first.
        HeightField plain = Field(129, 512.0, (_, _) => 0.0);
        (double s, double c) = Math.SinCos(-0.01 * Math.PI / 180.0);
        var down = new Vector3D(c, s, 0);
        Assert.AreEqual(2.0 / Math.Tan(0.01 * Math.PI / 180.0), new TerrainSight(plain, null, 1e15).Hit(new WorldPoint(-30000, 2, 0), down), 20.0);
        Assert.IsTrue(double.IsPositiveInfinity(new TerrainSight(plain, null, Radius).Hit(new WorldPoint(-30000, 2, 0), down)));
    }

    [TestMethod]
    public void Lit_ARidgeShadesTheGroundBehindIt_FromALowSun()
    {
        // A ridge 300 m high along x = 0; the evening sun 3° up in the west.
        var sight = new TerrainSight(Field(65, 64.0, (x, _) => Math.Abs(x) < 100 ? 300.0 : 0.0), null, Radius);
        Vector3D toSun = CompositionScorer.SunDirection(Evening);
        var west = new Vector3D(-0.5, Math.Sqrt(0.75), 0); // a slope facing the sun
        Assert.IsFalse(sight.Lit(1000, 0, west, toSun), "1 km east of the ridge, in its shadow");
        Assert.IsTrue(sight.Lit(-1000, 0, west, toSun), "west of it, in the sun");
        Assert.IsFalse(sight.Lit(-1000, 0, new Vector3D(0, 1, 0), toSun), "level ground under a 3° sun: grazing, not lit");
        Assert.IsFalse(sight.Lit(-1000, 0, new Vector3D(0.999, -0.045, 0).Normalized(), toSun), "a face turned away from the sun");
    }

    [TestMethod]
    public void TheFarField_TakesOverPastTheWorld()
    {
        HeightField world = Field(65, 64.0, (_, _) => 0.0);
        HeightField far = Field(65, 512.0, (_, _) => 800.0);
        var sight = new TerrainSight(world, far, Radius);
        Assert.AreEqual(0.0, sight.GroundAt(0, 0));
        Assert.AreEqual(800.0, sight.GroundAt(5000, 0));
        Assert.IsTrue(double.IsNaN(sight.GroundAt(20000, 0)));
        Assert.AreEqual(64.0, sight.DetailMetres(0, 0), "the world grid's cell, for the detail budget");
        Assert.IsTrue(double.IsNaN(sight.DetailMetres(5000, 0)), "the budget does not look at the far field");
        double t = sight.Hit(new WorldPoint(0, 100, 0), new Vector3D(1, 0, 0));
        Assert.IsTrue(t is > 2048 and < 2600, $"the far field's ground meets a level ray just past the world's edge: {t}");
    }

    [TestMethod]
    public void FloorAt_ReadsTheFinestLevelThatHoldsThePoint()
    {
        var fine = new PoolLevel(3, 100.0, -100.0, -100.0, [0, 10, 20, 0, 10, 20, 0, 10, 20], new float[9], new float[9]);
        var coarse = new PoolLevel(3, 1000.0, -1000.0, -1000.0, [5, 5, 5, 5, 5, 5, 5, 5, 5], new float[9], new float[9]);
        var map = new PoolMap([fine, coarse]);
        Assert.AreEqual(15.0, CompositionBuffer.FloorAt(map, 50.0, 0.0), 1e-9);
        Assert.AreEqual(5.0, CompositionBuffer.FloorAt(map, 500.0, 0.0), 1e-9);
        Assert.IsTrue(double.IsNaN(CompositionBuffer.FloorAt(map, 5000.0, 0.0)));
        var sight = new TerrainSight(Field(65, 64.0, (_, _) => 40.0), null, Radius, floors: map);
        CompositionBuffer buffer = sight.Trace(new CameraSpec { Position = new WorldPoint(0, 100, 0), PitchDegrees = -60.0 }, 8, 6);
        Assert.IsTrue(buffer.Rise.Where(float.IsFinite).All(r => r is >= 20f and <= 40f), "40 m ground over floors of 0 to 20 m");
    }

    [TestMethod]
    public void Plant_FindsThePeak_AndSeesItWithTheSunAside()
    {
        var sight = new TerrainSight(PeakWorld.Value, null, Radius);
        PlantedShot shot = ShotPlanter.Plant(sight, Evening, CompositionIntent.Peak, CompositionWeights.Default, CompositionLimits.Default, 16.0 / 9.0);
        Assert.AreEqual(1024.0, shot.MainPeak.X, 1e-9);
        Assert.AreEqual(512.0, shot.MainPeak.Z, 1e-9);
        Assert.AreEqual(2200.0, shot.MainPeak.Y, 1.0);
        Assert.AreEqual(2000.0, shot.ReliefMetres, 50.0);
        CollectionAssert.Contains(new[] { 40.0, 60.0, 90.0, 135.0 }, Math.Round(shot.SunRelativeDegrees));
        Assert.IsTrue(shot.DistanceMetres is >= 3000.0 and <= 12000.0);
        HeightField w = sight.World;
        Assert.IsTrue(Math.Abs(shot.Viewpoint.X) <= 0.5 * w.Extent - ShotPlanter.EdgeMarginMetres + 1e-6);
        Assert.IsTrue(Math.Abs(shot.Viewpoint.Z) <= 0.5 * w.Extent - ShotPlanter.EdgeMarginMetres + 1e-6);
        var eye = shot.Viewpoint with { Y = shot.Viewpoint.Y + ShotPlanter.PlantHeightMetres };
        Assert.IsTrue(sight.Sees(eye, shot.MainPeak, 200.0));
        Assert.AreEqual(shot, ShotPlanter.Plant(sight, Evening, CompositionIntent.Peak, CompositionWeights.Default, CompositionLimits.Default, 16.0 / 9.0));
    }

    [TestMethod]
    public void Plant_ARidge_FindsItsAxis()
    {
        // A ridge 2 km high running north-east, long (σ 4 km) along its axis and narrow (σ 1 km) across.
        HeightField ridge = Field(257, 64.0, (x, z) =>
        {
            double along = (x + z) / Math.Sqrt(2.0), across = (x - z) / Math.Sqrt(2.0);
            return 200.0 + 2000.0 * Math.Exp(-along * along / (2 * 4000.0 * 4000.0) - across * across / (2 * 1000.0 * 1000.0));
        });
        PlantedShot shot = ShotPlanter.Plant(new TerrainSight(ridge, null, Radius), Evening, CompositionIntent.Range, CompositionWeights.Default,
            CompositionLimits.Default, 16.0 / 9.0);
        Assert.AreEqual(45.0, shot.RangeAxisDegrees, 10.0);
        Assert.IsTrue(shot.RangeHalfLengthMetres > 2000.0);
        // Broadside or within 40° of it.
        double off = Math.Abs(((shot.HeadingDegrees - shot.RangeAxisDegrees) % 180.0 + 180.0) % 180.0 - 90.0);
        Assert.IsTrue(off <= 40.0 + 1e-9, $"{off}° off broadside");
    }

    [TestMethod]
    public void Collide_LiftsACameraOutOfTheGround()
    {
        var sight = new TerrainSight(PeakWorld.Value, null, Radius);
        double ground = sight.GroundAt(300, 200);
        var buried = new CameraSpec { Position = new WorldPoint(300, ground - 50, 200) };
        CameraSpec lifted = AutoCamera.Collide(buried, sight);
        Assert.IsTrue(lifted.Position.Y >= ground + DefaultCamera.EyeHeightMetres);
        var high = new CameraSpec { Position = new WorldPoint(300, ground + 500, 200) };
        Assert.AreEqual(high, AutoCamera.Collide(high, sight), "a camera clear of the ground stays put");
    }

    [TestMethod]
    public void Candidates_StandClear_SeeThePeak_AndAreTheSameEveryTime()
    {
        var sight = new TerrainSight(PeakWorld.Value, null, Radius);
        PlantedShot shot = ShotPlanter.Plant(sight, Evening, CompositionIntent.Peak, CompositionWeights.Default, CompositionLimits.Default, 16.0 / 9.0);
        IReadOnlyList<CameraCandidate> candidates = AutoCamera.Candidates(sight, shot, 16.0 / 9.0, AutoCameraSettings.Default, []);
        Assert.IsTrue(candidates.Count >= 100, $"{candidates.Count} candidates");
        foreach (CameraCandidate c in candidates)
        {
            WorldPoint p = c.Camera.Position;
            Assert.IsTrue(p.Y >= sight.GroundAt(p.X, p.Z) + DefaultCamera.EyeHeightMetres - 1e-9, $"#{c.Index} in the ground");
            Assert.IsTrue(sight.Sees(p, shot.MainPeak, 100.0 + 0.02 * Math.Sqrt((p.X - shot.MainPeak.X) * (p.X - shot.MainPeak.X) + (p.Z - shot.MainPeak.Z) * (p.Z - shot.MainPeak.Z))));
            CollectionAssert.Contains(AutoCameraSettings.Default.PeakFocalLengths.ToArray(), c.Camera.FocalLengthMm);
            Assert.AreEqual(0.0, c.Camera.RollDegrees);
        }
        CollectionAssert.AreEqual(candidates.ToArray(), AutoCamera.Candidates(sight, shot, 16.0 / 9.0, AutoCameraSettings.Default, []).ToArray());

        ScoredCandidate[] ranked = AutoCamera.Rank(AutoCamera.Score(sight, candidates, Evening, shot, 16.0 / 9.0, AutoCameraSettings.Default,
            CompositionWeights.Default, CompositionLimits.Default));
        ScoredCandidate[] again = AutoCamera.Rank(AutoCamera.Score(sight, candidates, Evening, shot, 16.0 / 9.0, AutoCameraSettings.Default,
            CompositionWeights.Default, CompositionLimits.Default));
        CollectionAssert.AreEqual(ranked.Select(s => (s.Candidate.Index, s.Total)).ToArray(), again.Select(s => (s.Candidate.Index, s.Total)).ToArray());
        // Those that keep the limits first, then by total.
        static (int, double) Key(ScoredCandidate s) => (s.Feasible ? 0 : 1, -s.Total);
        for (int k = 1; k < ranked.Length; k++)
            Assert.IsTrue(Key(ranked[k - 1]).CompareTo(Key(ranked[k])) <= 0, $"ranked {k - 1} before {k}");
        ScoredCandidate[] finalists = AutoCamera.Finalists(ranked, AutoCameraSettings.Default);
        Assert.IsTrue(finalists.Length is > 0 and <= 10);
        Assert.IsTrue(finalists.All(f => f.Feasible));
        Assert.IsTrue(finalists.GroupBy(f => f.Candidate.Place).All(g => g.Count() <= AutoCameraSettings.Default.FinalistsPerPlace));
    }

    [TestMethod]
    public void Frame_PutsTheSubjectOnAThird_AndTheSummitAtItsRow()
    {
        var sight = new TerrainSight(PeakWorld.Value, null, Radius);
        var eye = new WorldPoint(1000, 600, -6000);
        var summit = new WorldPoint(1000, 2200, 500);
        foreach (double across in (double[])[-1.0 / 3.0, 1.0 / 3.0])
        {
            CameraSpec camera = AutoCamera.Frame(sight, eye, summit, summit, 50.0, across, 0.25, 16.0 / 9.0);
            (Vector3D d, _) = sight.Toward(eye, summit);
            (double x, double y) = new CameraRays(camera, 160, 90).Project(d)!.Value;
            Assert.AreEqual(160 * (0.5 + 0.5 * across), x, 1.5, "on the third");
            Assert.AreEqual(90 * 0.25, y, 1.5, "a quarter down");
        }
    }

    [TestMethod]
    public void IntentOf_TheMountainSetPieces()
    {
        Assert.AreEqual(CompositionIntent.Peak, AutoCamera.IntentOf(new Recipe { SetPieces = [SetPiece.SnowCappedMountains] }));
        Assert.AreEqual(CompositionIntent.Peak, AutoCamera.IntentOf(new Recipe { SetPieces = [SetPiece.AlpineRange, SetPiece.SnowCappedMountains] }));
        Assert.AreEqual(CompositionIntent.Range, AutoCamera.IntentOf(new Recipe { SetPieces = [SetPiece.AlpineRange] }));
        Assert.AreEqual(CompositionIntent.None, AutoCamera.IntentOf(new Recipe { SetPieces = [SetPiece.RollingHills] }));
    }

    [TestMethod]
    public void CommandLine_AutoCamera()
    {
        string[] Args(params string[] more) => ["--preview", "--recipe", "r.json", "--seed", "1", "--out", "p.png", .. more];
        Assert.IsTrue(PreviewCommandLine.TryParse(Args(), out PreviewRequest? plain, out _));
        Assert.IsNull(plain!.AutoCamera);
        Assert.IsTrue(PreviewCommandLine.TryParse(Args("--auto-camera"), out PreviewRequest? auto, out _));
        Assert.AreEqual(AutoCameraSettings.Default, auto!.AutoCamera);
        Assert.IsTrue(PreviewCommandLine.TryParse(Args("--auto-camera", "--finalists", "3"), out PreviewRequest? three, out _));
        Assert.AreEqual(3, three!.AutoCamera!.Finalists);
        Assert.IsFalse(PreviewCommandLine.TryParse(Args("--auto-camera", "--camera", "c.json"), out _, out IReadOnlyList<string> both));
        StringAssert.Contains(both[0], "--camera");
        Assert.IsFalse(PreviewCommandLine.TryParse(Args("--finalists", "3"), out _, out IReadOnlyList<string> alone));
        StringAssert.Contains(alone[0], "--auto-camera");
        Assert.IsFalse(PreviewCommandLine.TryParse(Args("--auto-camera", "--finalists", "99"), out _, out _));
        Assert.IsTrue(auto.AutoCamera!.DetailBudget, "the detail budget is on by default");
        Assert.IsTrue(PreviewCommandLine.TryParse(Args("--auto-camera", "--detail-budget", "off"), out PreviewRequest? off, out _));
        Assert.IsFalse(off!.AutoCamera!.DetailBudget);
        Assert.IsFalse(PreviewCommandLine.TryParse(Args("--detail-budget", "off"), out _, out _), "without --auto-camera");
    }

    [TestMethod]
    public void Preview_WithTheAutoCamera_DryAndRendered()
    {
        string dir = Path.Combine(Path.GetTempPath(), "vista-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "r.json"), """{ "setPieces": ["snowCappedMountains"], "time": "goldenHour" }""");
            PreviewRequest request = new()
            {
                RecipePath = Path.Combine(dir, "r.json"),
                Seed = 7,
                OutPath = Path.Combine(dir, "p.png"),
                Width = 32,
                Height = 18,
                SamplesPerPixel = 2,
                Grid = TestWorlds.Grid,
                Erosion = false,
                DryRun = true,
                AutoCamera = AutoCameraSettings.Default with { Finalists = 2 },
            };
            Sidecar dry = PreviewPipeline.Run(request, renderer: null);
            Assert.IsFalse(dry.DefaultCamera);
            AutoCameraInfo info = dry.AutoCamera!;
            Assert.IsTrue(info.Candidates > 0 && info.Feasible <= info.Candidates);
            Assert.AreEqual(0, info.Finalists.Count, "a dry run renders none");
            StringAssert.Contains(info.Note, "dry run");
            Assert.AreEqual(info.Pick.Camera, dry.Camera);
            Assert.AreEqual(dry.Camera, VistaJson.ParseCamera(File.ReadAllText(Path.Combine(dir, "p.camera.json"))));
            Assert.AreEqual("p.camera.json", dry.Outputs["autoCamera"]);
            Sidecar back = VistaJson.Deserialize<Sidecar>(File.ReadAllText(Path.Combine(dir, "p.json")));
            Assert.AreEqual(info.Pick, back.AutoCamera!.Pick with { Scores = info.Pick.Scores });
            Assert.AreEqual(info.Candidates, back.AutoCamera.Candidates);

            var renderer = new FakeRenderer();
            Sidecar rendered = PreviewPipeline.Run(request with { DryRun = false, Debug = new HashSet<DebugOutput> { DebugOutput.Detail } }, renderer);
            Assert.AreEqual("p.detail.png", rendered.Outputs["detail"]);
            Assert.IsTrue(File.Exists(Path.Combine(dir, "p.detail.png")));
            DetailInfo detail = rendered.Detail!;
            CollectionAssert.AreEqual(DetailInfo.Thresholds.Select(t => t.ToString("00.#", System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
                detail.ShareByCellPixels.Keys.ToArray());
            Assert.AreEqual(CompositionLimits.Default.DetailCellPixels, detail.CellPixels);
            Assert.IsTrue(renderer.Requested!.IsSupersetOf([Aov.Depth, Aov.Normal, Aov.MaterialId]), "the budget reads the depth, normal and material ids");
            Assert.AreEqual(2, rendered.AutoCamera!.Finalists.Count, "the finalists rendered");
            Assert.AreEqual(3, renderer.Scenes.Count, "two finalists, then the pick");
            Assert.IsTrue(rendered.AutoCamera.Finalists.All(f => f.Scores.Squint is not null && f.Scores.Values is not null));
            Assert.AreEqual(rendered.Camera, renderer.Scenes[^1].Camera, "the preview renders the pick");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
