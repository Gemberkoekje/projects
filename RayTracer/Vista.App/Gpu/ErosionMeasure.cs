using System.Diagnostics;
using System.Globalization;
using Vista.Output;
using Vista.Recipes;
using Vista.Scene;
using Vista.Sliders;
using Vista.World;

namespace Vista.App.Gpu;

/// <summary>
/// <c>Vista.App --erosion-measure</c> (vista-plan §10 M1.7): erodes the recipe's world for each seed on the GPU, on a grid
/// of a chosen size and cell, and reports what it cost — GPU and wall time, submissions, the state's memory and the process's
/// video memory — with top-down images of the ground before and after and of the erosion maps. Since M1.11a it also reports
/// the stream-power incision (its time, and the drainage measures of the coarse grid before and after it and of the world grid
/// it hands the passes) and the drainage measures of the base and the eroded world (<see cref="DrainageStats"/>), with their
/// drainage images; <c>--incise-only</c> stops before the GPU. <c>--set Name=value</c> overrides an <see cref="ErosionSettings"/>
/// field (for tuning; <c>--set IncisionSteps=0;PoolFillMetres=0</c> is M1.7's erosion); <c>--parity</c> also runs the HLSL-against-C# checks.
/// <code>
/// Vista.App --erosion-measure [--recipe r.json] [--seeds 42,43] [--grid 4097x8] [--set Capacity=2 ...] [--parity] [--incise-only]
///                             [--out dir] [--sight-lines c1.json,c2.json [--sight-grids 1025x16,1025x32]]
/// </code>
/// <c>--sight-lines</c> first measures what each camera sees on the base terrain of each grid's extent (<see cref="SightLines"/>):
/// how much is terrain, sky, or beyond the grid, and how far the terrain is, and stops there unless <c>--seeds</c> is given too.
/// </summary>
internal static class ErosionMeasure
{
    /// <summary>The flag that selects this mode.</summary>
    public const string Flag = "--erosion-measure";

    /// <summary>Usage text.</summary>
    public const string Usage =
        "       Vista.App --erosion-measure [--recipe r.json] [--seeds 42,43] [--grid 4097x8] [--set Name=value ...] [--parity] [--incise-only]\n" +
        "                 [--out dir] [--sight-lines c1.json,c2.json [--sight-grids 1025x16,1025x32]]\n" +
        "                 (erosion on the GPU: time, memory, drainage, top-down maps; what cameras see on each grid extent)";

    /// <summary>Runs the measurement; returns 0, or 1 for bad arguments or a failed parity check.</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter log)
    {
        string? recipePath = null, outDir = null;
        var seeds = new List<ulong> { 42 };
        bool seedsGiven = false;
        WorldGrid grid = WorldGrid.Default;
        var overrides = new List<string>();
        bool parity = false, inciseOnly = false;
        List<string>? sightLines = null;
        var sightGrids = new List<WorldGrid> { new(1025, 16.0), new(1025, 32.0) };
        for (int i = 1; i < args.Count; i++)
        {
            string arg = args[i];
            string Next() => i + 1 < args.Count ? args[++i] : throw new ArgumentException($"{arg} needs a value.");
            switch (arg)
            {
                case "--recipe": recipePath = Next(); break;
                case "--out": outDir = Next(); break;
                case "--parity": parity = true; break;
                case "--incise-only": inciseOnly = true; break;
                case "--sight-lines": sightLines = Next().Split(',').ToList(); break;
                case "--sight-grids":
                    sightGrids = Next().Split(',').Select(g => g.Split('x')).Select(p => new WorldGrid(int.Parse(p[0], CultureInfo.InvariantCulture), double.Parse(p[1], CultureInfo.InvariantCulture))).ToList();
                    break;
                case "--seeds":
                    seedsGiven = true;
                    seeds = Next().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => ulong.Parse(s, CultureInfo.InvariantCulture)).ToList();
                    break;
                case "--grid":
                    string[] parts = Next().Split('x');
                    grid = new WorldGrid(int.Parse(parts[0], CultureInfo.InvariantCulture), double.Parse(parts[1], CultureInfo.InvariantCulture));
                    break;
                case "--set":
                    overrides.Add(Next());
                    break;
                default:
                    log.WriteLine($"Unknown argument '{arg}'.");
                    log.WriteLine(Usage);
                    return 1;
            }
        }
        Recipe recipe = recipePath is null ? new Recipe { SetPieces = [SetPiece.AlpineRange] } : VistaJson.ParseRecipe(File.ReadAllText(recipePath));
        ErosionSettings settings = ErosionSettings.For(TerrainProfile.For(recipe.SetPieces));
        if (overrides.Count > 0)
            settings = settings.With(string.Join(';', overrides));
        if (outDir is not null)
            Directory.CreateDirectory(outDir);

        if (sightLines is not null)
        {
            // What the views see, on the base terrain of each extent (sight lines need no fine grid and no erosion).
            foreach (WorldGrid g in sightGrids)
            {
                HeightField field = TerrainGenerator.Generate(recipe, seeds.Count > 0 ? seeds[0] : 42, g);
                foreach (string file in sightLines)
                {
                    SightLineStats st = SightLines.Measure(field, VistaJson.ParseCamera(File.ReadAllText(file)), 320, 180);
                    log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"sight lines, {g.Size}² × {g.CellSize} m ({g.Size * g.CellSize / 1000 - g.CellSize / 1000:0.0} km), {Path.GetFileName(file)}: terrain {100.0 * st.Terrain / st.Pixels:0.0}%, " +
                        $"sky {100.0 * st.Sky / st.Pixels:0.0}%, beyond the grid {100.0 * st.Beyond / st.Pixels:0.0}%; hits at {st.Quantile(0.5) / 1000:0.0} / {st.Quantile(0.9) / 1000:0.0} / {st.Quantile(0.99) / 1000:0.0} km " +
                        $"(median / 90% / 99%), {100 * st.FartherThan(8000):0}% beyond 8 km, {100 * st.FartherThan(16000):0}% beyond 16 km"));
                }
            }
            if (!seedsGiven)
                return 0;
        }

        using var renderer = new D3D12VistaRenderer();
        log.WriteLine($"Device: {renderer.DeviceName}, driver {renderer.DriverVersion}");
        log.WriteLine($"Erosion: {VistaJson.Serialize(settings).ReplaceLineEndings(" ")}");
        bool pass = true;
        if (parity)
            pass &= ErosionParity.Run(renderer, settings, grid.CellSize, log);

        long idle = renderer.VideoMemoryUsed;
        foreach (ulong seed in seeds)
        {
            var wall = Stopwatch.StartNew();
            HeightField ground = TerrainGenerator.Generate(recipe, seed, grid);
            double baseSeconds = wall.Elapsed.TotalSeconds;
            HeightField incised = ground;
            StrataFrame frame = StrataFrame.Identity;
            HeightField? upliftField = null;
            if (settings.IncisionSteps > 0)
            {
                // StreamPower.Prepare in its two halves, so the coarse grid can be measured too.
                var incisionClock = Stopwatch.StartNew();
                (HeightField coarseBase, HeightField coarseIncised, frame, upliftField) = StreamPower.Coarse(recipe, seed, grid, settings);
                double coarseSeconds = incisionClock.Elapsed.TotalSeconds;
                incised = StreamPower.Refine(coarseIncised, coarseBase, ground, settings.ChannelAreaKm2);
                double incisionSeconds = incisionClock.Elapsed.TotalSeconds;
                DrainageMap coarseBefore = DrainageMap.Of(coarseBase), coarseAfter = DrainageMap.Of(coarseIncised);
                DrainageMap incisedDrainage = DrainageMap.Of(incised);
                log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"seed {seed}: incision {incisionSeconds:0.00} s on the CPU ({coarseSeconds:0.00} s of it {settings.IncisionSteps} steps on the {coarseBase.Size}² × {coarseBase.CellSize} m grid); " +
                    $"mapped back from {frame.Bottom:0} m by {frame.Scale:0.000} to the base terrain's range from {frame.Low:0} m"));
                log.WriteLine("  " + Describe("coarse base   ", DrainageStats.Measure(coarseBefore)));
                log.WriteLine("  " + Describe("coarse incised", DrainageStats.Measure(coarseAfter)));
                log.WriteLine("  " + Describe("incised       ", DrainageStats.Measure(incisedDrainage)));
                log.WriteLine("  " + Describe("coarse base   ", ReliefStats.Measure(coarseBase)));
                log.WriteLine("  " + Describe("coarse incised", ReliefStats.Measure(coarseIncised)));
                if (outDir is not null)
                {
                    (byte[] image, int imageSize) = DebugImages.DrainageImage(coarseIncised, coarseAfter, null, 1024);
                    PngEncoder.Write(Path.Combine(outDir, $"seed{seed}.coarse.drainage.png"), image, imageSize, imageSize, PngColor.Rgb);
                    (byte[] relief, _) = DebugImages.HeightMap(coarseIncised, null, 1024);
                    PngEncoder.Write(Path.Combine(outDir, $"seed{seed}.coarse.png"), relief, imageSize, imageSize, PngColor.Rgb);
                    (image, imageSize) = DebugImages.DrainageImage(incised, incisedDrainage, null, 1024);
                    PngEncoder.Write(Path.Combine(outDir, $"seed{seed}.incised.drainage.png"), image, imageSize, imageSize, PngColor.Rgb);
                    // The uplift map on the coarse grid, black to white from none to IncisionUplift, north up.
                    ReadOnlySpan<float> uplift = upliftField.Heights;
                    int us = coarseBase.Size;
                    var grey = new byte[us * us * 3];
                    for (int row = 0; row < us; row++)
                    {
                        for (int col = 0; col < us; col++)
                        {
                            byte v = (byte)Math.Round(255.0 * Math.Clamp(uplift[(us - 1 - row) * us + col] / Math.Max(settings.IncisionUplift, 1e-6f), 0f, 1f));
                            grey[(row * us + col) * 3] = grey[(row * us + col) * 3 + 1] = grey[(row * us + col) * 3 + 2] = v;
                        }
                    }
                    PngEncoder.Write(Path.Combine(outDir, $"seed{seed}.uplift.png"), grey, us, us, PngColor.Rgb);
                }
                if (inciseOnly)
                    continue;
            }
            TerrainProfile profile = TerrainProfile.For(recipe.SetPieces);
            StrataGrid strata = StrataGrid.For(RockHardness.For(seed, settings), grid.Size, grid.CellSize, frame,
                (x, z) => upliftField is null ? 0.0 : upliftField.HeightAt(x, z));
            ErosionRun run = renderer.Erode(incised, settings, strata);
            // Today's glaciers on the eroded ground, as the world builder lays them (M1.13), so the cache holds the world a render reads.
            var laying = Stopwatch.StartNew();
            ErosionResult r = Glaciers.Lay(run.Result, settings);
            double laidSeconds = laying.Elapsed.TotalSeconds;
            ErosionStats s = run.Stats;
            if (grid == WorldGrid.Default)
            {
                // Kept in the cache, so a render of the same world after the measure reads it instead of eroding it again.
                ErosionCache.Default.Store(profile, seed, grid, settings, r, new WorldOrigin(renderer.DeviceName, renderer.DriverVersion,
                    DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), ground.ContentHash()));
            }
            double moved = 0.0, deepest = 0.0, highest = 0.0;
            for (int c = 0; c < ground.Heights.Length; c++)
            {
                double d = r.Surface.Heights[c] - ground.Heights[c];
                moved += Math.Abs(d);
                deepest = Math.Min(deepest, d);
                highest = Math.Max(highest, d);
            }
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"seed {seed}, {grid.Size}² × {grid.CellSize} m: base {baseSeconds:0.00} s; erosion {s.Seconds:0.00} s GPU ({s.Seconds / Math.Max(settings.Iterations, 1) * 1e3:0.00} ms a step), " +
                $"{s.WallSeconds:0.00} s wall ({s.MapsSeconds:0.00} s of it the maps and the drainage), {s.Submissions} submissions, longest {s.LongestSubmissionSeconds * 1e3:0} ms; " +
                $"state {s.StateBytes / 1048576.0:0} MiB, process VRAM {s.ProcessVideoMemory / 1048576.0:0} MiB ({idle / 1048576.0:0} before)"));
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  ground moved {moved / ground.Heights.Length:0.00} m on average, cut {-deepest:0} m, filled {highest:0} m; loose mean {r.Loose.Average():0.00} m, " +
                $"max {r.Loose.Max():0} m, over 1 m on {r.Loose.Count(v => v > 1f) * 100.0 / r.Loose.Length:0}% of the cells; discharge max {r.Discharge.Max():0} m³/s; standing water max {r.StandingWater.Max():0.0} m; hash {r.Surface.ContentHash():x16}"));
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  spikes (cells over 5 m above all eight neighbours): {Spikes(ground, 5f)} before, {Spikes(r.Surface, 5f)} after; over 20 m: {Spikes(ground, 20f)} before, {Spikes(r.Surface, 20f)} after"));
            DrainageMap baseDrainage = DrainageMap.Of(ground);
            foreach (double threshold in new[] { 1.0, 0.1 })
            {
                log.WriteLine("  " + Describe("base  ", DrainageStats.Measure(baseDrainage, threshold)));
                log.WriteLine("  " + Describe("eroded", DrainageStats.Measure(r.Drainage, threshold)));
            }
            log.WriteLine("  " + Describe("base  ", ReliefStats.Measure(ground)));
            // The eroded slopes against each cell's own rock talus angle (the GPU erosion's, from the rock's hardness there).
            double tanSoft = Math.Tan(settings.RockTalusDegrees * Math.PI / 180.0), tanHard = Math.Tan(settings.RockTalusHardDegrees * Math.PI / 180.0);
            log.WriteLine("  " + Describe("eroded", ReliefStats.Measure(r.Surface, c => Math.Atan(tanSoft + (tanHard - tanSoft) * r.Hardness[c]) * 180.0 / Math.PI)));
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  hardness at the surface: mean {r.Hardness.Average():0.000}, soft (< 0.3) {r.Hardness.Count(v => v < 0.3f) * 100.0 / r.Hardness.Length:0}%, hard (> 0.7) {r.Hardness.Count(v => v > 0.7f) * 100.0 / r.Hardness.Length:0}%, " +
                $"massive (> 0.5) {strata.Massive.Count(v => v > 0.5f) * 100.0 / strata.Massive.Length:0}%"));
            // M1.12c: where the world stands, and how much of its rock's lines run along the contours (the materials' rock, by the
            // rules): the beds' outcrops (the measure) and the lines of high curvature.
            WorldFooting footing = WorldFooting.Of(r.Surface);
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  footing: the {footing.Percentile:0}th percentile at {footing.HeightMetres:0.0} m, lowest {footing.LowestMetres:0.0} m, highest {r.Surface.MaxHeight:0.0} m"));
            MaterialField field = MaterialField.For(r.Surface, r, strata, RockHardness.For(seed, settings).Tints, MaterialRules.Default, MaterialNoise.SeedOf(seed));
            MaterialWeights[] weights = field.SampleWeights(r.Surface, field.Apply(MaterialRules.Default.Constants(3000f, 1f, field.NoiseSeed, 0f, false)), 0f);
            log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  rock (the rules' weight ≥ 0.5): {weights.Count(w => w.Rock >= 0.5f) * 100.0 / weights.Length:0.0}% of the grid"));
            foreach (int stride in new[] { 1, 2 })
            {
                log.WriteLine("  " + Describe(LedgeStats.Bands(r.Surface, r.Hardness, c => weights[c].Rock >= 0.5f, stride)));
                log.WriteLine("  " + Describe(LedgeStats.Ledges(r.Surface, c => weights[c].Rock >= 0.5f, stride)));
            }
            // The ground's materials at the altitude the sliders anchor this world at (the recipe's place, the world's lowest valleys
            // at its valley floor), as a dry run's shares: rock, scree, soil, meadow and forest.
            SliderState sliders = LinkedSliders.Resolve(recipe, SliderPins.FromRecipe(recipe), footing: footing);
            // Since M1.13 the season's snow of the recipe's date lies too, and the world's glaciers at this altitude.
            GroundCover cover = GroundCover.For(sliders.Climate, sliders.Almanac.Place.LatitudeDegrees) with
            {
                Snow = true,
                SnowlineMetres = sliders.SnowlineMetres,
                FreezingLevelMetres = sliders.FreezingLevelMetres,
                GlacierLineMetres = settings.GlacierElaMetres + sliders.BaseAltitudeMetres,
            };
            MaterialWeights[] anchored = field.SampleWeights(r.Surface, field.Apply(MaterialRules.Default.Constants((float)cover.GrassLineMetres, cover.Poleward,
                field.NoiseSeed, (float)cover.TreeLineMetres, cover.Forest)).WithSnow(cover.SnowPlacement), (float)sliders.BaseAltitudeMetres);
            double Share(Func<MaterialWeights, float> of) => anchored.Sum(w => (double)of(w)) / anchored.Length;
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  at {sliders.Almanac.Place.Name}, base {sliders.BaseAltitudeMetres:0} m (valley floor {sliders.ValleyFloorMetres:0} m, tree line {cover.TreeLineMetres:0} m, grass line {cover.GrassLineMetres:0} m, " +
                $"snowline {cover.SnowlineMetres:0} m, glaciers' line {cover.GlacierLineMetres:0} m): " +
                $"rock {Share(w => w.Rock):0.0%}, scree {Share(w => w.Scree):0.0%}, soil {Share(w => w.Soil):0.0%}, meadow {Share(w => w.Grass):0.0%}, forest {Share(w => w.Forest):0.0%}, " +
                $"snow {Share(w => w.Snow):0.0%}, firn {Share(w => w.Firn):0.0%}, ice {Share(w => w.Ice):0.0%}"));
            if (Output.GlacierInfo.Of(r, settings) is { } glaciers)
            {
                log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  glaciers: line {glaciers.ElaMetres:0} m above height 0, {glaciers.Share:0.0%} of the grid under ice, {glaciers.VolumeKm3:0.00} km³, thickest {glaciers.MaxThicknessMetres:0} m; laid in {laidSeconds:0.0} s"));
            }
            if (outDir is not null)
            {
                string stem = Path.Combine(outDir, $"seed{seed}");
                (byte[] before, int size) = DebugImages.HeightMap(ground, null, 1024);
                PngEncoder.Write(stem + ".base.png", before, size, size, PngColor.Rgb);
                (byte[] after, _) = DebugImages.HeightMap(r.Surface, null, 1024);
                PngEncoder.Write(stem + ".eroded.png", after, size, size, PngColor.Rgb);
                (byte[] maps, _) = DebugImages.ErosionMap(r, null, 1024);
                PngEncoder.Write(stem + ".maps.png", maps, size, size, PngColor.Rgb);
                (byte[] cover2, int coverSize) = DebugImages.MaterialMap(r.Surface, field, anchored, null);
                PngEncoder.Write(stem + ".materials.png", cover2, coverSize, coverSize, PngColor.Rgb);
                (byte[] drainage, _) = DebugImages.DrainageImage(ground, baseDrainage, null, 1024);
                PngEncoder.Write(stem + ".base.drainage.png", drainage, size, size, PngColor.Rgb);
                (drainage, _) = DebugImages.DrainageImage(r.Surface, r.Drainage, null, 1024);
                PngEncoder.Write(stem + ".drainage.png", drainage, size, size, PngColor.Rgb);
                (byte[] hardness, _) = DebugImages.HardnessImage(r, null, 1024);
                PngEncoder.Write(stem + ".hardness.png", hardness, size, size, PngColor.Rgb);
            }
        }
        return pass ? 0 : 1;
    }

    /// <summary>One line of drainage measures.</summary>
    internal static string Describe(string label, DrainageStats s) => string.Create(CultureInfo.InvariantCulture,
        $"{label} channels ≥ {s.ChannelAreaKm2:0.##} km²: {s.ChannelShare:0.0%} of the grid, longest {s.LongestChannelKm:0.0} km without a lake, " +
        $"{s.LongestOpenChannelKm:0.0} km without a closed basin ({s.LongestRouteKm:0.0} km through both); {s.DrainsToChannels:0.0%} drains into one; " +
        $"lakes {s.LakeShare:0.0%}, closed basins {s.BasinShare:0.0%}; largest catchment {s.LargestBasinShare:0.0%}; " +
        $"Strahler {s.MaxOrder} ({string.Join('/', s.Streams)}), bifurcation {s.BifurcationRatio:0.00}");

    /// <summary>One line of relief measures (vista-plan §10 M1.11d).</summary>
    internal static string Describe(string label, ReliefStats s)
    {
        static string Slopes(ReliefStats.SlopeStats t) => string.Create(CultureInfo.InvariantCulture,
            $"at {t.SpacingMetres:0} m mean {t.MeanDegrees:0.0}°, over 40° {t.Over40:0.0%}, facet {t.FacetShare:0.0%} at {t.FacetDegrees:0.0}°")
            + (double.IsNaN(t.NearTalus) ? "" : string.Create(CultureInfo.InvariantCulture, $", near its talus {t.NearTalus:0.0%}"));
        return string.Create(CultureInfo.InvariantCulture,
            $"{label} relief 1 km {s.Relief1Km.P10:0}/{s.Relief1Km.P50:0}/{s.Relief1Km.P90:0} m (spread {s.Relief1Km.Spread:0.00}), " +
            $"5 km {s.Relief5Km.P10:0}/{s.Relief5Km.P50:0}/{s.Relief5Km.P90:0} m (spread {s.Relief5Km.Spread:0.00}); summits {s.Summits1Km.P10:0}/{s.Summits1Km.P50:0}/{s.Summits1Km.P90:0} m; " +
            $"slopes {Slopes(s.Slopes)}; {Slopes(s.CoarseSlopes)}");
    }

    /// <summary>One line of the contour-line measure (vista-plan §10 M1.12c).</summary>
    internal static string Describe(LedgeStats s) => string.Create(CultureInfo.InvariantCulture,
        $"{s.Kind} at {s.SpacingMetres:0} m: {s.AlongContour:0.0%} along the contour (within {LedgeStats.AlongContourDegrees:0}°) of {s.LineCells} line cells " +
        $"({s.RockCells} steep rock); by 10° from the contour {string.Join('/', s.AngleShares.Select(v => (100.0 * v).ToString("0.0", CultureInfo.InvariantCulture)))}%");

    /// <summary>Cells that stand more than <paramref name="metres"/> above all eight neighbours.</summary>
    internal static int Spikes(HeightField field, float metres)
    {
        int n = field.Size, count = 0;
        for (int j = 1; j < n - 1; j++)
        {
            for (int i = 1; i < n - 1; i++)
            {
                float h = field[i, j], highest = float.MinValue;
                for (int dj = -1; dj <= 1; dj++)
                {
                    for (int di = -1; di <= 1; di++)
                    {
                        if (di != 0 || dj != 0)
                            highest = Math.Max(highest, field[i + di, j + dj]);
                    }
                }
                if (h - highest > metres)
                    count++;
            }
        }
        return count;
    }
}
