using System.Diagnostics;
using System.Globalization;
using Vista.Output;
using Vista.Recipes;
using Vista.World;

namespace Vista.App.Gpu;

/// <summary>
/// <c>Vista.App --valley-measure</c> (vista-plan §10 M1.13 verify): for each seed, the recipe's world incised on the coarse grid with
/// its glacial carving and without it (<see cref="StreamPower.Coarse"/>, the same uplift and rock), glaciers at the carving's mean line
/// (<see cref="ErosionSettings.CarvingClimate"/> half way up) solved on the uncarved ground, and the sections of the mountain valleys
/// they run in (<see cref="ValleyShape"/>), the same for every carving: each trough's width at half its bottom's depth over its width
/// at the whole of it, carved against uncarved. Runs on the CPU (the incision's grid, before the GPU erosion fills the valley floors).
/// <code>
/// Vista.App --valley-measure [--recipe r.json] [--seeds 42,7] [--sections 24] [--set Name=value ...] [--out dir]
/// </code>
/// With <c>--out</c> it draws each seed's sections, the carved trough over the uncarved one, to <c>valleys-&lt;seed&gt;.png</c>.
/// <c>--set</c> overrides an <see cref="ErosionSettings"/> field of the carving, as <c>--erosion-measure</c>'s does (for tuning).
/// </summary>
internal static class ValleyMeasure
{
    /// <summary>The flag that selects this mode.</summary>
    public const string Flag = "--valley-measure";

    /// <summary>Usage text.</summary>
    public const string Usage =
        "       Vista.App --valley-measure [--recipe r.json] [--seeds 42,7] [--sections 24] [--set Name=value ...] [--out dir]\n" +
        "                 (glacial carving's troughs against the uncarved valleys: width at half depth over width, CPU only)";

    /// <summary>Runs the measurement; returns 0, or 1 for bad arguments.</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter log)
    {
        string? recipePath = null, outDir = null;
        var seeds = new List<ulong> { 42 };
        int count = 24;
        var overrides = new List<string>();
        for (int i = 1; i < args.Count; i++)
        {
            string arg = args[i];
            string Next() => i + 1 < args.Count ? args[++i] : throw new ArgumentException($"{arg} needs a value.");
            switch (arg)
            {
                case "--recipe": recipePath = Next(); break;
                case "--out": outDir = Next(); break;
                case "--seeds": seeds = Next().Split(',').Select(s => ulong.Parse(s, CultureInfo.InvariantCulture)).ToList(); break;
                case "--sections": count = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--set": overrides.Add(Next()); break;
                default:
                    log.WriteLine($"Unknown argument '{arg}'.");
                    log.WriteLine(Usage);
                    return 1;
            }
        }
        Recipe recipe = recipePath is null ? new Recipe { SetPieces = [SetPiece.SnowCappedMountains] } : VistaJson.ParseRecipe(File.ReadAllText(recipePath));
        ErosionSettings settings = ErosionSettings.For(TerrainProfile.For(recipe.SetPieces));
        if (overrides.Count > 0)
            settings = settings.With(string.Join(';', overrides));
        if (settings.GlacialSteps == 0)
        {
            log.WriteLine("The recipe's erosion carves no glaciers.");
            return 1;
        }
        ErosionSettings plain = settings with { GlacialSteps = 0, GlacierElaMetres = 0f };
        var grid = WorldGrid.Default;
        foreach (ulong seed in seeds)
        {
            var stopwatch = Stopwatch.StartNew();
            HeightField carved = StreamPower.Coarse(recipe, seed, grid, settings).Incised;
            HeightField incised = StreamPower.Coarse(recipe, seed, grid, plain).Incised;
            // Glaciers at the cycle's mean line on the uncarved ground: the valleys the carving's glaciers enter, the same sections
            // whatever the carving.
            GlacierClimate middle = settings.CarvingClimate(settings.GlacialCycleSteps / 4);
            IceField ice = GlacierFlow.Solve(incised, middle, GlacierFlowSettings.Default, null, Glaciers.Iterations);
            IReadOnlyList<ValleySection> sections = ValleyShape.Measure(carved, incised, ice, count);
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"seed {seed}: {sections.Count} sections in mountain valleys where glaciers at the carving's mean line ({middle.ElaMetres:0} m) run on the uncarved ground; {stopwatch.Elapsed.TotalSeconds:0} s"));
            log.WriteLine("  section          ice   width carved/uncarved   half-depth ratio carved/uncarved   power carved/uncarved");
            foreach (ValleySection s in sections)
            {
                log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  ({s.I,3}, {s.J,3})  {s.IceMetres,5:0} m   {s.Carved.WidthMetres,6:0} / {s.Incised.WidthMetres,6:0} m   {s.Carved.HalfDepthRatio,13:0.00} / {s.Incised.HalfDepthRatio:0.00}   {s.Carved.Power,16:0.00} / {s.Incised.Power:0.00}"));
            }
            if (sections.Count > 0)
            {
                double Median(Func<ValleySection, double> f) => sections.Select(f).Order().ElementAt(sections.Count / 2);
                int rounder = sections.Count(s => s.Carved.HalfDepthRatio > s.Incised.HalfDepthRatio);
                log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  median half-depth ratio: carved {Median(s => s.Carved.HalfDepthRatio):0.00}, uncarved {Median(s => s.Incised.HalfDepthRatio):0.00}; power {Median(s => s.Carved.Power):0.00} against {Median(s => s.Incised.Power):0.00}; {rounder} of {sections.Count} sections rounder"));
            }
            if (outDir is not null)
            {
                Directory.CreateDirectory(outDir);
                string path = Path.Combine(outDir, string.Create(CultureInfo.InvariantCulture, $"valleys-{seed}.png"));
                (byte[] rgb, int w, int h) = Draw(sections);
                PngEncoder.Write(path, rgb, w, h, PngColor.Rgb);
                log.WriteLine($"  {path}");
            }
        }
        return 0;
    }

    /// <summary>
    /// The sections, four to a row, each 160 × 110 px on one scale (true proportions, a section's 5 km across a cell): the uncarved
    /// valley grey, the carved one blue; 640 px wide, a sheet's cell.
    /// </summary>
    private static (byte[] Rgb, int Width, int Height) Draw(IReadOnlyList<ValleySection> sections)
    {
        const int cellW = 160, cellH = 110, columns = 4;
        int rows = Math.Max((sections.Count + columns - 1) / columns, 1);
        int w = cellW * columns, h = cellH * rows;
        var rgb = new byte[w * h * 3];
        Array.Fill(rgb, (byte)255);
        double metresPerPixel = 2.0 * ValleyShape.ReachMetres / (cellW - 8);
        for (int k = 0; k < sections.Count; k++)
        {
            ValleySection s = sections[k];
            int ox = (k % columns) * cellW + 4, oy = (k / columns) * cellH;
            double low = Math.Min(s.Carved.Heights.Min(), s.Incised.Heights.Min());
            void Line(double[] heights, (byte R, byte G, byte B) colour)
            {
                for (int i = 0; i < heights.Length - 1; i++)
                {
                    int x0 = ox + (int)(i * ValleyShape.StepMetres / metresPerPixel), x1 = ox + (int)((i + 1) * ValleyShape.StepMetres / metresPerPixel);
                    int y0 = oy + cellH - 6 - (int)((heights[i] - low) / metresPerPixel), y1 = oy + cellH - 6 - (int)((heights[i + 1] - low) / metresPerPixel);
                    int steps = Math.Max(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)), 1);
                    for (int t = 0; t <= steps; t++)
                    {
                        int x = x0 + (x1 - x0) * t / steps, y = y0 + (y1 - y0) * t / steps;
                        if (x < 0 || x >= w || y < oy || y >= oy + cellH)
                            continue;
                        int p = (y * w + x) * 3;
                        (rgb[p], rgb[p + 1], rgb[p + 2]) = colour;
                    }
                }
            }
            Line(s.Incised.Heights, (150, 150, 150));
            Line(s.Carved.Heights, (30, 60, 220));
        }
        return (rgb, w, h);
    }
}
