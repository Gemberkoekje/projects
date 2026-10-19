using System.Globalization;
using Vista.Rendering;

namespace Vista.Cli;

/// <summary>Debug images the preview CLI can write next to the beauty (vista-plan §7.4).</summary>
public enum DebugOutput
{
    /// <summary>First-hit albedo.</summary>
    Albedo,
    /// <summary>First-hit world normal.</summary>
    Normal,
    /// <summary>Log-scaled depth.</summary>
    Depth,
    /// <summary>Material ids as colours.</summary>
    Material,
    /// <summary>Data tier per pixel.</summary>
    Tier,
    /// <summary>Per-pixel variance heat map.</summary>
    Variance,
}

/// <summary>A parsed <c>--preview</c> invocation.</summary>
public sealed record PreviewRequest
{
    /// <summary>Recipe JSON path.</summary>
    public required string RecipePath { get; init; }

    /// <summary>World seed.</summary>
    public required ulong Seed { get; init; }

    /// <summary>Camera JSON path; null uses the default camera.</summary>
    public string? CameraPath { get; init; }

    /// <summary>Beauty PNG path; siblings (<c>.exr</c>, <c>.json</c>, <c>.&lt;debug&gt;.png</c>) share its base name.</summary>
    public required string OutPath { get; init; }

    /// <summary>Image width in pixels.</summary>
    public int Width { get; init; } = PreviewCommandLine.DefaultWidth;

    /// <summary>Image height in pixels (from the width and the aspect ratio).</summary>
    public int Height { get; init; } = PreviewCommandLine.DefaultHeight;

    /// <summary>Samples per pixel.</summary>
    public int SamplesPerPixel { get; init; } = 64;

    /// <summary>Maximum bounces.</summary>
    public int MaxBounces { get; init; } = 6;

    /// <summary>Debug images to write.</summary>
    public IReadOnlySet<DebugOutput> Debug { get; init; } = new HashSet<DebugOutput>();

    /// <summary>Target GPU time per dispatch in milliseconds (<see cref="BatchPlannerOptions.DispatchBudgetMs"/>); never changes the image.</summary>
    public double DispatchBudgetMs { get; init; } = new BatchPlannerOptions().DispatchBudgetMs;

    /// <summary>Run everything up to the GPU (world, mesh, sidecar, height map) and render nothing.</summary>
    public bool DryRun { get; init; }
}

/// <summary>
/// Parses the headless preview CLI (vista-plan §7.4):
/// <code>
/// Vista.App --preview --recipe r.json --seed N [--camera c.json] --out p.png
///           [--width 320] [--aspect 16:9] [--spp 64] [--bounces 6] [--dispatch-ms 250]
///           [--debug all | albedo,normal,depth,material,tier,variance] [--dry-run]
/// </code>
/// The preview keeps the final's quality settings and changes only the pixel count, at the target's aspect
/// ratio (320×180 for 16:9).
/// </summary>
public static class PreviewCommandLine
{
    /// <summary>Default preview width.</summary>
    public const int DefaultWidth = 320;

    /// <summary>Default preview height (16:9 at the default width).</summary>
    public const int DefaultHeight = 180;

    /// <summary>Largest accepted width or height (16K).</summary>
    public const int MaxDimension = 16384;

    /// <summary>Usage text.</summary>
    public const string Usage =
        "Usage: Vista.App --preview --recipe <recipe.json> --seed <n> [--camera <camera.json>] --out <preview.png>\n" +
        "                 [--width 320] [--aspect 16:9] [--spp 64] [--bounces 6] [--dispatch-ms 250]\n" +
        "                 [--debug all|albedo,normal,depth,material,tier,variance] [--dry-run]\n" +
        "Writes <preview>.png, <preview>.exr, <preview>.json (sidecar) and <preview>.<debug>.png.\n" +
        "--dry-run generates the world and writes the sidecar and <preview>.heightmap.png without a GPU.\n" +
        "--dispatch-ms sets the GPU time per dispatch (at most 1000); it changes how the render is split, never the image.";

    /// <summary>Parses <paramref name="args"/>. Returns false with every problem listed in <paramref name="errors"/>.</summary>
    public static bool TryParse(IReadOnlyList<string> args, out PreviewRequest? request, out IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(args);
        var problems = new List<string>();
        request = null;
        errors = problems;

        if (args.Count == 0 || args[0] != "--preview")
        {
            problems.Add("The first argument must be --preview (the only mode in M0).");
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool dryRun = false;
        int k = 1;
        while (k < args.Count)
        {
            string arg = args[k++];
            if (arg == "--dry-run")
            {
                dryRun = true;
            }
            else if (!ValueOptions.Contains(arg))
            {
                problems.Add($"Unknown argument '{arg}'.");
            }
            else if (k >= args.Count || args[k].StartsWith("--", StringComparison.Ordinal))
            {
                problems.Add($"{arg} needs a value.");
            }
            else if (!values.TryAdd(arg, args[k++]))
            {
                problems.Add($"{arg} is given more than once.");
            }
        }

        string? recipe = Required(values, "--recipe", problems);
        string? seedText = Required(values, "--seed", problems);
        string? outPath = Required(values, "--out", problems);
        ulong seed = 0;
        if (seedText is not null && !ulong.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out seed))
            problems.Add($"--seed must be a non-negative integer below 2^64, got '{seedText}'.");
        if (outPath is not null && !outPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            problems.Add("--out must name a .png file.");

        int width = Int(values, "--width", DefaultWidth, 1, MaxDimension, problems);
        int spp = Int(values, "--spp", 64, 1, 1 << 20, problems);
        int bounces = Int(values, "--bounces", 6, 0, 64, problems);
        double dispatchMs = DispatchBudget(values.GetValueOrDefault("--dispatch-ms"), problems);
        int height = DefaultHeight;
        (int aw, int ah) = Aspect(values.GetValueOrDefault("--aspect", "16:9"), problems);
        if (aw > 0)
        {
            height = (int)Math.Round((double)width * ah / aw, MidpointRounding.AwayFromZero);
            if (height < 1 || height > MaxDimension)
                problems.Add($"Width {width} at aspect {aw}:{ah} gives an unusable height of {height}.");
        }
        HashSet<DebugOutput> debug = DebugSet(values.GetValueOrDefault("--debug"), problems);

        if (problems.Count > 0)
            return false;
        request = new PreviewRequest
        {
            RecipePath = recipe!,
            Seed = seed,
            CameraPath = values.GetValueOrDefault("--camera"),
            OutPath = outPath!,
            Width = width,
            Height = height,
            SamplesPerPixel = spp,
            MaxBounces = bounces,
            Debug = debug,
            DispatchBudgetMs = dispatchMs,
            DryRun = dryRun,
        };
        return true;
    }

    private static readonly HashSet<string> ValueOptions = new(StringComparer.Ordinal)
    {
        "--recipe", "--seed", "--camera", "--out", "--width", "--aspect", "--spp", "--bounces", "--debug", "--dispatch-ms",
    };

    private static string? Required(Dictionary<string, string> values, string name, List<string> problems)
    {
        if (values.TryGetValue(name, out string? value))
            return value;
        problems.Add($"{name} is required.");
        return null;
    }

    private static int Int(Dictionary<string, string> values, string name, int fallback, int min, int max, List<string> problems)
    {
        if (!values.TryGetValue(name, out string? text))
            return fallback;
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value >= min && value <= max)
            return value;
        problems.Add($"{name} must be an integer in [{min}, {max}], got '{text}'.");
        return fallback;
    }

    private static double DispatchBudget(string? text, List<string> problems)
    {
        double fallback = new BatchPlannerOptions().DispatchBudgetMs;
        if (text is null)
            return fallback;
        if (double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double ms)
            && ms > 0.0 && ms <= BatchPlannerOptions.MaxDispatchBudgetMs)
            return ms;
        problems.Add($"--dispatch-ms must be a number in (0, {BatchPlannerOptions.MaxDispatchBudgetMs}], got '{text}'.");
        return fallback;
    }

    private static (int W, int H) Aspect(string text, List<string> problems)
    {
        string[] parts = text.Split(':');
        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int w)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int h)
            && w > 0 && h > 0)
            return (w, h);
        problems.Add($"--aspect must look like 16:9, got '{text}'.");
        return (0, 0);
    }

    private static HashSet<DebugOutput> DebugSet(string? text, List<string> problems)
    {
        var set = new HashSet<DebugOutput>();
        if (text is null)
            return set;
        if (text == "all")
        {
            set.UnionWith(Enum.GetValues<DebugOutput>());
            return set;
        }
        foreach (string name in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (Enum.TryParse(name, ignoreCase: true, out DebugOutput output) && Enum.IsDefined(output) && !int.TryParse(name, out _))
                set.Add(output);
            else
                problems.Add($"Unknown debug output '{name}'.");
        }
        return set;
    }
}
