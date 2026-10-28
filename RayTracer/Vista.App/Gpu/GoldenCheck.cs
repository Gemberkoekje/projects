using System.Diagnostics;
using System.Globalization;
using Vista.Cli;
using Vista.App.Denoise;
using Vista.Output;
using Vista.World;

namespace Vista.App.Gpu;

/// <summary>
/// <c>Vista.App --golden-check</c>: the Vista golden suite (vista-plan §10 M1.17) on the RTX 3070. It renders every case of
/// <c>vista-views/goldens/suite.json</c> through one session, as <c>--preview</c> would, and compares each PNG and EXR with the golden
/// beside the suite: identical, within the suite's tolerance, or different (<see cref="GoldenComparison"/>). With <c>--update</c> it
/// writes the renders over the goldens instead, re-baselining them deliberately, and says what each one moved by.
/// </summary>
internal static class GoldenCheck
{
    /// <summary>The flag that selects this check.</summary>
    public const string Flag = "--golden-check";

    /// <summary>Usage text.</summary>
    public const string Usage =
        "       Vista.App --golden-check [--suite vista-views/goldens/suite.json] [--only name,name] [--update] [--out folder]\n" +
        "                 (the golden suite rendered and compared with the committed goldens; --update re-baselines them)";

    public static int Run(string[] args, TextWriter log)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool update = false;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--update")
            {
                update = true;
            }
            else if (args[i] is "--suite" or "--only" or "--out" && i + 1 < args.Length)
            {
                values[args[i]] = args[++i];
            }
            else
            {
                Console.Error.WriteLine($"Unknown or incomplete option '{args[i]}'.");
                Console.Error.WriteLine(Usage);
                return 1;
            }
        }
        string suitePath = Path.GetFullPath(values.GetValueOrDefault("--suite", Path.Combine("vista-views", "goldens", "suite.json")));
        string suiteFolder = Path.GetDirectoryName(suitePath)!;
        string outFolder = Path.GetFullPath(values.GetValueOrDefault("--out", Path.Combine(Path.GetTempPath(), "vista-goldens")));
        GoldenSuite suite = GoldenSuite.Parse(File.ReadAllText(suitePath));
        IReadOnlyList<GoldenCase> cases = suite.Cases;
        if (values.TryGetValue("--only", out string? only))
        {
            HashSet<string> names = [.. only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            string[] unknown = [.. names.Where(n => !cases.Any(c => c.Name == n))];
            if (unknown.Length > 0)
            {
                Console.Error.WriteLine($"No golden named {string.Join(", ", unknown)} in {suitePath}.");
                return 1;
            }
            cases = [.. cases.Where(c => names.Contains(c.Name))];
        }
        Directory.CreateDirectory(outFolder);

        // Every case's command line is checked before anything renders.
        var requests = new List<(GoldenCase Case, PreviewRequest Request, string Png)>();
        foreach (GoldenCase golden in cases)
        {
            string png = Path.Combine(outFolder, golden.Name + ".png");
            if (!PreviewCommandLine.TryParse([.. GoldenSuite.CommandLine(golden, suiteFolder, png)], out PreviewRequest? request, out IReadOnlyList<string> errors))
            {
                Console.Error.WriteLine($"{golden.Name}: {string.Join(" ", errors)}");
                return 1;
            }
            requests.Add((golden, request!, png));
        }

        using var renderer = new D3D12VistaRenderer();
        using var denoiser = new OidnDenoiser();
        var session = new PreviewSession(renderer, denoiser, eroder: renderer, erosionCache: ErosionCache.Default)
        {
            Notice = notice => log.WriteLine($"warning: {notice}"),
        };
        log.WriteLine($"golden suite {suitePath}: {requests.Count} cases on {renderer.DeviceName}; renders in {outFolder}");
        int failed = 0;
        foreach ((GoldenCase golden, PreviewRequest request, string png) in requests)
        {
            var clock = Stopwatch.StartNew();
            Sidecar sidecar = session.Run(request);
            string exr = Path.Combine(outFolder, sidecar.Outputs["beautyExr"]);
            string goldenPng = Path.Combine(suiteFolder, golden.Name + ".png"), goldenExr = Path.Combine(suiteFolder, golden.Name + ".exr");
            GoldenResult result = GoldenComparison.Compare(png, exr, goldenPng, goldenExr, suite.Tolerance);
            string measures = result.Verdict is GoldenVerdict.Missing or GoldenVerdict.Identical
                ? string.Empty
                : string.Create(CultureInfo.InvariantCulture,
                    $" (mean {GoldenComparison.Signed(result.MeanRelative * 100.0)}%, blocks {result.BlockRelative * 100.0:0.00}% rms, PNG channel mean {GoldenComparison.Signed(result.PngMeanLevels)} levels, {result.PngChangedShare * 100.0:0.0}% of its values changed)");
            string note = result.Note is null ? string.Empty : $": {result.Note}";
            if (update)
            {
                File.Copy(png, goldenPng, overwrite: true);
                File.Copy(exr, goldenExr, overwrite: true);
                log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{golden.Name}: re-baselined, was {Describe(result.Verdict)}{measures} [{clock.Elapsed.TotalSeconds:0.0} s]"));
                continue;
            }
            if (!result.Passed)
                failed++;
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{golden.Name}: {Describe(result.Verdict)}{measures}{note} [{clock.Elapsed.TotalSeconds:0.0} s] ({golden.Checks})"));
        }
        if (update)
        {
            log.WriteLine($"golden suite re-baselined: {requests.Count} goldens written to {suiteFolder}");
            return 0;
        }
        log.WriteLine(failed == 0 ? $"golden suite passed: {requests.Count} of {requests.Count}" : $"golden suite FAILED: {failed} of {requests.Count} different or missing");
        return failed == 0 ? 0 : 1;
    }

    private static string Describe(GoldenVerdict verdict) => verdict switch
    {
        GoldenVerdict.Identical => "identical",
        GoldenVerdict.WithinTolerance => "within tolerance",
        GoldenVerdict.Different => "DIFFERENT",
        GoldenVerdict.Missing => "missing",
        _ => verdict.ToString(),
    };
}
