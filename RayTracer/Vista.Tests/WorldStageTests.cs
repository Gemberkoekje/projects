using System.Globalization;
using System.Text.Json;
using Vista.Cli;
using Vista.Output;
using Vista.Recipes;
using Vista.Scene;
using Vista.World;

namespace Vista.Tests;

/// <summary>
/// The world stage of M1.7 (vista-plan §10): the erosion cache on disk, the builder that reads it or erodes, the preview
/// pipeline and CLI on top of them, and the sight-line measurement that chose the grid's extent. The eroder here is the C#
/// reference on small grids; the GPU's is checked against it by <c>--gpu-selftest</c>.
/// </summary>
[TestClass]
public sealed class WorldStageTests
{
    private static readonly WorldGrid Small = new(33, 64.0);
    private static readonly Recipe Alpine = new() { SetPieces = [SetPiece.AlpineRange], Time = TimeOfDay.GoldenHour };

    private string _dir = null!;

    [TestInitialize]
    public void CreateDirectory()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vista-world-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_dir, recursive: true);

    private ErosionCache Cache() => new(Path.Combine(_dir, "worlds"));

    private static ErosionSettings Quick => ErosionSettings.Mountains with { Iterations = 60, AverageFrom = 30 };

    // ── The cache ──────────────────────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Cache_RoundTripsAWorld_BitForBit()
    {
        ErosionCache cache = Cache();
        HeightField ground = TerrainGenerator.Generate(Alpine, 5, Small);
        ErosionResult result = ErosionReference.Erode(ground, Quick);
        var origin = new WorldOrigin("C# reference", "", "2026-10-20T12:00:00.0000000Z", ground.ContentHash());
        Assert.IsNull(cache.TryLoad(TerrainProfile.Mountains, 5, Small, Quick, out string? missing));
        Assert.IsNull(missing, "an absent world is no problem");

        string path = cache.Store(TerrainProfile.Mountains, 5, Small, Quick, result, origin);
        Assert.IsTrue(File.Exists(path));
        Assert.IsTrue(path.EndsWith(ErosionCache.Extension, StringComparison.Ordinal));
        (ErosionResult Result, WorldOrigin Origin)? back = cache.TryLoad(TerrainProfile.Mountains, 5, Small, Quick, out string? problem);
        Assert.IsNull(problem);
        Assert.IsNotNull(back);
        Assert.AreEqual(origin, back.Value.Origin);
        Assert.AreEqual(result.ContentHash(), back.Value.Result.ContentHash());
        CollectionAssert.AreEqual(result.Surface.Heights.ToArray(), back.Value.Result.Surface.Heights.ToArray());
        CollectionAssert.AreEqual(result.Discharge, back.Value.Result.Discharge);
        CollectionAssert.AreEqual(result.Drainage.Direction, back.Value.Result.Drainage.Direction);
        CollectionAssert.AreEqual(result.Drainage.FillDepth, back.Value.Result.Drainage.FillDepth);
        CollectionAssert.AreEqual(result.Drainage.Upstream, back.Value.Result.Drainage.Upstream, "the upstream areas follow from the directions");
        Assert.AreEqual(result.Steps, back.Value.Result.Steps);
        Assert.IsFalse(Directory.GetFiles(cache.Directory, "*.tmp").Any(), "no temporary file is left behind");
    }

    [TestMethod]
    public void Cache_KeysEverythingTheErosionReads()
    {
        ulong key = ErosionCache.Key(TerrainProfile.Mountains, 5, Small, Quick);
        Assert.AreNotEqual(key, ErosionCache.Key(TerrainProfile.Hills, 5, Small, Quick));
        Assert.AreNotEqual(key, ErosionCache.Key(TerrainProfile.Mountains, 6, Small, Quick));
        Assert.AreNotEqual(key, ErosionCache.Key(TerrainProfile.Mountains, 5, new WorldGrid(33, 32.0), Quick));
        Assert.AreNotEqual(key, ErosionCache.Key(TerrainProfile.Mountains, 5, Small, Quick with { Capacity = 2.5f }));

        // The key is text hashed: it must not depend on the machine's culture (this one writes decimal commas).
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("nl-NL");
            Assert.AreEqual(key, ErosionCache.Key(TerrainProfile.Mountains, 5, Small, Quick));
            StringAssert.Contains(Cache().PathFor(TerrainProfile.Mountains, 5, Small, Quick), "mountains-5-33x64-");
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [TestMethod]
    public void Cache_TreatsADamagedFileAsMissing_AndSaysSo()
    {
        ErosionCache cache = Cache();
        HeightField ground = TerrainGenerator.Generate(Alpine, 5, Small);
        string path = cache.Store(TerrainProfile.Mountains, 5, Small, Quick, ErosionReference.Erode(ground, Quick), new WorldOrigin("d", "", "t", 1));
        byte[] bytes = File.ReadAllBytes(path);
        bytes[^3] ^= 0x40;
        File.WriteAllBytes(path, bytes);
        Assert.IsNull(cache.TryLoad(TerrainProfile.Mountains, 5, Small, Quick, out string? problem));
        StringAssert.Contains(problem, "do not match its hash");

        File.WriteAllBytes(path, bytes[..100]);
        Assert.IsNull(cache.TryLoad(TerrainProfile.Mountains, 5, Small, Quick, out problem));
        Assert.IsNotNull(problem);
    }

    [TestMethod]
    public void Cache_KeepsTheNewestWorlds_WithinItsSize()
    {
        HeightField ground = TerrainGenerator.Generate(Alpine, 5, Small);
        ErosionResult result = ErosionReference.Erode(ground, Quick);
        long one = new FileInfo(Cache().Store(TerrainProfile.Mountains, 1, Small, Quick, result, new WorldOrigin("d", "", "t", 1))).Length;
        var cache = new ErosionCache(Path.Combine(_dir, "worlds"), maxBytes: one * 2 + one / 2);
        string second = cache.Store(TerrainProfile.Mountains, 2, Small, Quick, result, new WorldOrigin("d", "", "t", 1));
        File.SetLastWriteTimeUtc(second, DateTime.UtcNow.AddMinutes(1));
        string third = cache.Store(TerrainProfile.Mountains, 3, Small, Quick, result, new WorldOrigin("d", "", "t", 1));
        string[] kept = Directory.GetFiles(cache.Directory, "*" + ErosionCache.Extension);
        Assert.AreEqual(2, kept.Length);
        CollectionAssert.Contains(kept, third, "the world just stored is kept");
        CollectionAssert.Contains(kept, second);
    }

    // ── The builder ────────────────────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Builder_ErodesAndStores_ThenReadsTheCache()
    {
        ErosionCache cache = Cache();
        var eroder = new CountingEroder();
        TerrainWorld first = WorldBuilder.Build(Alpine, 5, Small, eroder, cache);
        Assert.AreEqual(ErosionSource.Eroded, first.Info.Source);
        Assert.AreEqual(1, eroder.Runs);
        Assert.AreEqual("C# reference", first.Info.Device);
        Assert.IsNotNull(first.Info.Stats);
        Assert.IsNotNull(first.Info.CacheFile);
        Assert.AreEqual(TerrainGenerator.Generate(Alpine, 5, Small).ContentHash().ToString("x16"), first.Info.BaseHeightFieldHash);
        Assert.AreEqual(0, first.Warnings.Count);

        TerrainWorld second = WorldBuilder.Build(Alpine with { Time = TimeOfDay.Dawn }, 5, Small, eroder, cache);
        Assert.AreEqual(ErosionSource.Cache, second.Info.Source);
        Assert.AreEqual(1, eroder.Runs, "the cache answered");
        Assert.AreEqual(first.Surface.ContentHash(), second.Surface.ContentHash());
        Assert.AreEqual(first.Info.ErodedUtc, second.Info.ErodedUtc);
        Assert.AreEqual(first.Info.BaseHeightFieldHash, second.Info.BaseHeightFieldHash);
        Assert.IsNull(second.Info.Stats, "nothing ran");
    }

    [TestMethod]
    public void Builder_WithoutEroderOrCachedWorld_UsesTheBaseTerrain_AndWarns()
    {
        TerrainWorld world = WorldBuilder.Build(Alpine, 5, Small, eroder: null, Cache());
        Assert.AreEqual(ErosionSource.Uneroded, world.Info.Source);
        Assert.IsNull(world.Erosion);
        Assert.AreEqual(TerrainGenerator.Generate(Alpine, 5, Small).ContentHash(), world.Surface.ContentHash());
        StringAssert.Contains(world.Warnings.Single(), "Not eroded");

        TerrainWorld off = WorldBuilder.Build(Alpine, 5, Small, new CountingEroder(), Cache(), erode: false);
        Assert.AreEqual(ErosionSource.Off, off.Info.Source);
        Assert.AreEqual(0, off.Warnings.Count, "asked for, so no warning");
    }

    [TestMethod]
    public void Builder_ErodesAgain_WhenTheCachedFileIsDamaged()
    {
        ErosionCache cache = Cache();
        var eroder = new CountingEroder();
        TerrainWorld first = WorldBuilder.Build(Alpine, 5, Small, eroder, cache);
        File.WriteAllBytes(first.Info.CacheFile!, [1, 2, 3]);
        TerrainWorld again = WorldBuilder.Build(Alpine, 5, Small, eroder, cache);
        Assert.AreEqual(ErosionSource.Eroded, again.Info.Source);
        Assert.AreEqual(2, eroder.Runs);
        StringAssert.Contains(again.Warnings.Single(), "could not be read");
        Assert.AreEqual(ErosionSource.Cache, WorldBuilder.Build(Alpine, 5, Small, eroder, cache).Info.Source, "and stored it afresh");
    }

    // ── The pipeline and the CLI ───────────────────────────────────────────────────────────────────────────────────

    private PreviewRequest Request(bool dryRun) => new()
    {
        RecipePath = Path.Combine(_dir, "r.json"),
        Seed = 5,
        OutPath = Path.Combine(_dir, dryRun ? "dry.png" : "p.png"),
        Width = 32,
        Height = 18,
        SamplesPerPixel = 2,
        DryRun = dryRun,
        Grid = Small,
    };

    [TestMethod]
    public void Pipeline_RendersTheErodedWorld_AndADryRunReadsItFromTheCache()
    {
        File.WriteAllText(Path.Combine(_dir, "r.json"), """{ "setPieces": ["alpineRange"], "time": "goldenHour" }""");
        ErosionCache cache = Cache();
        var renderer = new FakeRenderer();
        Sidecar rendered = PreviewPipeline.Run(Request(dryRun: false), renderer, eroder: new CountingEroder(), erosionCache: cache);
        ErosionInfo erosion = rendered.World.Erosion!;
        Assert.AreEqual(ErosionSource.Eroded, erosion.Source);
        Assert.AreEqual(20, rendered.Schema);
        Assert.AreEqual(renderer.Scene!.Terrain.ContentHash().ToString("x16"), rendered.World.HeightFieldHash, "the sidecar hashes the eroded surface");
        Assert.AreNotEqual(erosion.BaseHeightFieldHash, rendered.World.HeightFieldHash);
        Assert.AreEqual(0, rendered.Warnings.Count);

        Sidecar dry = PreviewPipeline.Run(Request(dryRun: true), renderer: null, erosionCache: cache);
        Assert.AreEqual(ErosionSource.Cache, dry.World.Erosion!.Source);
        Assert.AreEqual(rendered.World.HeightFieldHash, dry.World.HeightFieldHash, "the dry run shows the world the render drew");
        Assert.IsTrue(File.Exists(Path.Combine(_dir, dry.Outputs["erosionMap"])));
        Assert.IsTrue(File.Exists(Path.Combine(_dir, dry.Outputs["hardnessMap"])), "the rock's hardness map (M1.11d)");
        Assert.AreEqual(0, dry.Warnings.Count);

        // The sidecar's erosion block reads back.
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, "dry.json")));
        JsonElement block = doc.RootElement.GetProperty("world").GetProperty("erosion");
        Assert.AreEqual("cache", block.GetProperty("source").GetString());
        Assert.AreEqual(ErosionSettings.Mountains.ContentHash().ToString("x16"), block.GetProperty("settingsHash").GetString());
        Assert.AreEqual(1500, block.GetProperty("settings").GetProperty("iterations").GetInt32());
        Assert.AreEqual(ErosionSettings.Mountains.IncisionSteps, block.GetProperty("strata").GetProperty("upliftSteps").GetInt32(),
            "the rock's frame (schema 16)");
        Assert.AreEqual(dry.World.Erosion.Strata, rendered.World.Erosion.Strata, "the cached world keeps its frame");
    }

    [TestMethod]
    public void Pipeline_DryRunWithoutACachedWorld_WarnsAndShowsTheBaseTerrain()
    {
        File.WriteAllText(Path.Combine(_dir, "r.json"), """{ "setPieces": ["alpineRange"] }""");
        Sidecar dry = PreviewPipeline.Run(Request(dryRun: true), renderer: null, erosionCache: Cache());
        Assert.AreEqual(ErosionSource.Uneroded, dry.World.Erosion!.Source);
        Assert.AreEqual(dry.World.Erosion.BaseHeightFieldHash, dry.World.HeightFieldHash);
        Assert.IsFalse(dry.Outputs.ContainsKey("erosionMap"));
        StringAssert.Contains(dry.Warnings.Single(), "Not eroded");
    }

    [TestMethod]
    public void CommandLine_ErosionAndGrid()
    {
        static PreviewRequest Parse(params string[] extra)
        {
            string[] args = ["--preview", "--recipe", "r.json", "--seed", "1", "--out", "p.png", .. extra];
            Assert.IsTrue(PreviewCommandLine.TryParse(args, out PreviewRequest? request, out IReadOnlyList<string> errors), string.Join(" | ", errors));
            return request!;
        }
        static string Error(params string[] extra)
        {
            string[] args = ["--preview", "--recipe", "r.json", "--seed", "1", "--out", "p.png", .. extra];
            Assert.IsFalse(PreviewCommandLine.TryParse(args, out _, out IReadOnlyList<string> errors));
            return string.Join(" | ", errors);
        }

        PreviewRequest plain = Parse();
        Assert.IsTrue(plain.Erosion);
        Assert.AreEqual(WorldGrid.Default, plain.Grid);
        Assert.AreEqual(new WorldGrid(4097, 8.0), WorldGrid.Default, "the M1.7 grid: 32.8 km at 8 m");
        Assert.IsFalse(Parse("--erosion", "off").Erosion);
        Assert.IsTrue(Parse("--erosion", "on").Erosion);
        Assert.AreEqual(new WorldGrid(1025, 16.5), Parse("--grid", "1025x16.5").Grid);
        StringAssert.Contains(Error("--erosion", "maybe"), "--erosion must be on or off");
        StringAssert.Contains(Error("--grid", "4097"), "--grid must be");
        StringAssert.Contains(Error("--grid", "2x8"), "--grid must be");
        StringAssert.Contains(Error("--grid", "99999x8"), "--grid must be");
        StringAssert.Contains(Error("--grid", "65x-1"), "--grid must be");
    }

    [TestMethod]
    public void SheetAxis_VariesErosion()
    {
        Assert.IsTrue(SheetAxis.TryParse("erosion=off,on", out SheetAxis? axis, out string? error), error);
        var request = new PreviewRequest { RecipePath = "r.json", Seed = 1, OutPath = "p.png" };
        Assert.IsFalse(axis!.Apply(request, 0).Erosion);
        Assert.IsTrue(axis.Apply(request with { Erosion = false }, 1).Erosion);
        Assert.IsFalse(SheetAxis.TryParse("erosion=off,maybe", out _, out error));
        StringAssert.Contains(error, "not on or off");
    }

    // ── Sight lines and the maps image ─────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void SightLines_TellTerrainSkyAndBeyondApart()
    {
        var heights = new float[65 * 65];
        var flat = new HeightField(65, 100.0, heights); // 6.4 km of flat ground at 0 m
        var down = new CameraSpec { Position = new WorldPoint(0, 100, 0), PitchDegrees = -60, FocalLengthMm = 50 };
        SightLineStats ground = SightLines.Measure(flat, down, 32, 18);
        Assert.AreEqual(ground.Pixels, ground.Terrain);
        Assert.IsTrue(ground.Quantile(0.5) is > 100 and < 200, $"median {ground.Quantile(0.5):0} m, about 115 m");

        var up = down with { PitchDegrees = 60 };
        Assert.AreEqual(32 * 18, SightLines.Measure(flat, up, 32, 18).Sky);

        // Looking level from 100 m up, the bottom half of the frame meets the ground — the farther part of it past the edge.
        var level = down with { PitchDegrees = 0 };
        SightLineStats far = SightLines.Measure(flat, level, 32, 18);
        Assert.AreEqual(far.Pixels, far.Terrain + far.Sky + far.Beyond);
        Assert.IsTrue(far.Beyond > 0 && far.Terrain > 0 && far.Sky > 0, $"{far.Terrain} terrain, {far.Sky} sky, {far.Beyond} beyond");
    }

    [TestMethod]
    public void ErosionMap_IsTopDown_AtMostTheGridSize()
    {
        ErosionResult result = ErosionReference.Erode(TerrainGenerator.Generate(Alpine, 5, Small), Quick);
        (byte[] rgb, int size) = DebugImages.ErosionMap(result, camera: null);
        Assert.AreEqual(Small.Size, size);
        Assert.AreEqual(size * size * 3, rgb.Length);
        (_, int capped) = DebugImages.ErosionMap(result, camera: null, maxSize: 16);
        Assert.AreEqual(16, capped);
    }

    /// <summary>The reference, counting its runs.</summary>
    private sealed class CountingEroder : ITerrainEroder
    {
        private readonly ReferenceEroder _reference = new();

        public int Runs { get; private set; }

        public string DeviceName => _reference.DeviceName;

        public string DriverVersion => _reference.DriverVersion;

        public ErosionRun Erode(HeightField ground, ErosionSettings settings, StrataGrid? strata = null)
        {
            Runs++;
            return _reference.Erode(ground, settings with { Iterations = 60, AverageFrom = 30 }, strata);
        }
    }
}
