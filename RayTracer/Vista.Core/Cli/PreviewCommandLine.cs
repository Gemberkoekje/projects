using System.Globalization;
using System.Text.Json;
using Vista.Astronomy;
using Vista.Recipes;
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

    /// <summary>
    /// The loupe (vista-plan §7.4): render only this window of the <see cref="Width"/>×<see cref="Height"/>
    /// frame, with the frame's camera rays and random keys, so the image is exactly a crop of the full render.
    /// Empty (the default) renders the whole frame.
    /// </summary>
    public PixelWindow Loupe { get; init; }

    /// <summary>
    /// Recipe fields to override before the strict parse, as dotted camelCase path → JSON value (see
    /// <see cref="Recipes.JsonOverrides"/>). A contact sheet varies a recipe field through these.
    /// </summary>
    public IReadOnlyDictionary<string, string> RecipeOverrides { get; init; } = new Dictionary<string, string>();

    /// <summary>Camera fields to override, applied to the supplied or the default camera.</summary>
    public IReadOnlyDictionary<string, string> CameraOverrides { get; init; } = new Dictionary<string, string>();

    /// <summary>How the air is evaluated (vista-plan §10 M1.3): the LUTs, or the reference.</summary>
    public AtmosphereMode Atmosphere { get; init; } = AtmosphereMode.Luts;

    /// <summary>Which lights a surface samples (vista-plan §10 M1.5): sun and sky with MIS, or M1.4's sun alone.</summary>
    public LightSampling LightSampling { get; init; } = LightSampling.Mis;

    /// <summary>The bounce from which Russian roulette may end a path (<see cref="RenderSettings.RussianRouletteStart"/>).</summary>
    public int RussianRouletteStart { get; init; } = RenderSettings.DefaultRussianRouletteStart;
}

/// <summary>
/// Parses the headless preview CLI (vista-plan §7.4):
/// <code>
/// Vista.App --preview --recipe r.json --seed N [--camera c.json] --out p.png
///           [--width 320] [--aspect 16:9] [--spp 64] [--bounces 6] [--dispatch-ms 250]
///           [--debug all | albedo,normal,depth,material,tier,variance] [--loupe x,y,w,h] [--dry-run]
///           [--location zermatt | --latitude 46.0] [--date 2026-10-15] [--time dawn | --time 17:30]
///           [--sun-elevation -2] [--altitude 3000] [--atmosphere luts | reference | referenceShadowed]
///           [--light-sampling mis | sunOnly] [--rr-start 2]
/// </code>
/// The preview keeps the final's quality settings and changes only the pixel count, at the target's aspect
/// ratio (320×180 for 16:9). <c>--loupe</c> renders one window of that frame at full detail. The place
/// options override the recipe's fields (vista-plan §3.5): <c>--location</c> or <c>--latitude</c> picks the
/// place (and clears the other), <c>--date</c> overrides the season, and <c>--time</c> takes a time-of-day
/// preset (clearing the recipe's solar time) or a local solar time, and <c>--sun-elevation</c> the time at which the
/// sun stands that high (rising when the time is dawn); <c>--altitude</c> sets the scene's base altitude (§3.6).
/// <c>--atmosphere reference</c> renders the slow, unbiased reference atmosphere instead of the LUTs (vista-plan §10
/// M1.3), and <c>referenceShadowed</c> adds the terrain's shadow to the air's sunlight (M1.4). <c>--light-sampling
/// sunOnly</c> renders with M1.4's estimator (the sky left to the bounce) instead of sampling the sky as a light too (M1.5);
/// with <c>--rr-start 3</c>, M1.4's Russian roulette as well, it renders M1.4's images bit for bit.
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
        "                 [--debug all|albedo,normal,depth,material,tier,variance] [--loupe x,y,w,h] [--dry-run]\n" +
        "Writes <preview>.png, <preview>.exr, <preview>.json (sidecar) and <preview>.<debug>.png.\n" +
        "--dry-run generates the world and writes the sidecar and <preview>.heightmap.png without a GPU.\n" +
        "--dispatch-ms sets the GPU time per dispatch (at most 1000); it changes how the render is split, never the image.\n" +
        "--loupe renders only the window at column x, row y, w wide and h high of the --width frame: exactly a crop of it.\n" +
        "Place overrides: [--location <place> | --latitude <degrees>] [--date yyyy-mm-dd] [--time <preset> | --time hh:mm]\n" +
        "  places: yellowstone, banff, yosemite, lofoten, iceland, scottishHighlands, zermatt (default), dolomites,\n" +
        "          torresDelPaine, fiordland, hokkaido\n" +
        "  presets: dawn (sunrise − 20 min), day (solar noon), goldenHour (sunset − 60 min), dusk (sunset + 20 min), night;\n" +
        "  hh:mm is local solar time (12:00 is solar noon). A date overrides the recipe's season.\n" +
        "  --sun-elevation <degrees>: the time the sun stands that high (geometric; rising with --time dawn, else setting).\n" +
        "  --altitude <metres>: the scene's base altitude above sea level (default: the place's valley floor), 0 to 8000.\n" +
        "--atmosphere luts (default) | reference | referenceShadowed: the precomputed air, or the air path-traced as a medium\n" +
        "  (slow, unbiased), optionally with the terrain's shadow in it.\n" +
        "--light-sampling mis (default) | sunOnly: sample the sun and the sky as lights, weighed against the bounce, or (M1.4)\n" +
        "  the sun alone, the sky left to the bounce; the same image on average, with different noise.\n" +
        "--rr-start <bounce>: the bounce from which Russian roulette may end a path (default 2; M1.4 used 3).";

    /// <summary>The recipe fields each place option decides, so a contact sheet cannot vary them as well.</summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> PlaceOptionFields { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["--location"] = ["location", "latitude"],
            ["--latitude"] = ["location", "latitude"],
            ["--date"] = ["date", "season"],
            ["--time"] = ["time", "solarTime"],
            ["--sun-elevation"] = ["sunElevation", "solarTime"],
            ["--altitude"] = ["altitude"],
        };

    private static string PlaceList => string.Join(", ", Places.All.Select(p => JsonName(p.Place.ToString())));

    /// <summary>Parses <paramref name="args"/>. Returns false with every problem listed in <paramref name="errors"/>.</summary>
    public static bool TryParse(IReadOnlyList<string> args, out PreviewRequest? request, out IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(args);
        var problems = new List<string>();
        request = null;
        errors = problems;

        if (args.Count == 0 || args[0] != "--preview")
        {
            problems.Add("The first argument must be --preview.");
            return false;
        }

        (Dictionary<string, string> values, bool dryRun) = SplitOptions(args, 1, ValueOptions, problems);
        PreviewRequest? parsed = Build(values, dryRun, recipeRequired: true, seedRequired: true, problems);
        if (problems.Count > 0)
            return false;
        request = parsed;
        return true;
    }

    /// <summary>The options that take a value.</summary>
    internal static IReadOnlySet<string> ValueOptions { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "--recipe", "--seed", "--camera", "--out", "--width", "--aspect", "--spp", "--bounces", "--debug", "--dispatch-ms", "--loupe",
        "--location", "--latitude", "--date", "--time", "--sun-elevation", "--altitude", "--atmosphere", "--light-sampling",
        "--rr-start",
    };

    /// <summary>
    /// Splits <paramref name="args"/> from <paramref name="start"/> into <c>--option value</c> pairs and the
    /// <c>--dry-run</c> flag, reporting unknown, valueless and repeated options.
    /// </summary>
    internal static (Dictionary<string, string> Values, bool DryRun) SplitOptions(
        IReadOnlyList<string> args, int start, IReadOnlySet<string> valueOptions, List<string> problems)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool dryRun = false;
        int k = start;
        while (k < args.Count)
        {
            string arg = args[k++];
            if (arg == "--dry-run")
            {
                dryRun = true;
            }
            else if (!valueOptions.Contains(arg))
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
        return (values, dryRun);
    }

    /// <summary>
    /// Builds a request from split options, adding every problem to <paramref name="problems"/>. A contact
    /// sheet whose axis supplies the recipe or the seed passes false for that requirement; the field is then
    /// empty or 0 until the axis fills it in.
    /// </summary>
    internal static PreviewRequest? Build(
        Dictionary<string, string> values, bool dryRun, bool recipeRequired, bool seedRequired, List<string> problems)
    {
        int before = problems.Count;
        string? recipe = recipeRequired ? Required(values, "--recipe", problems) : values.GetValueOrDefault("--recipe", string.Empty);
        string? seedText = seedRequired ? Required(values, "--seed", problems) : values.GetValueOrDefault("--seed");
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
        PixelWindow loupe = Loupe(values.GetValueOrDefault("--loupe"), width, height, problems);
        Dictionary<string, string> recipeOverrides = PlaceOverrides(values, problems);
        AtmosphereMode atmosphere = Atmosphere(values.GetValueOrDefault("--atmosphere"), problems);
        LightSampling lightSampling = Lights(values.GetValueOrDefault("--light-sampling"), problems);
        int rrStart = Int(values, "--rr-start", RenderSettings.DefaultRussianRouletteStart, 0, RenderSettings.MaxRussianRouletteStart, problems);

        if (problems.Count > before)
            return null;
        return new PreviewRequest
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
            Loupe = loupe,
            RecipeOverrides = recipeOverrides,
            Atmosphere = atmosphere,
            LightSampling = lightSampling,
            RussianRouletteStart = rrStart,
        };
    }

    /// <summary>Parses an <c>--atmosphere</c> value (<c>luts</c>, <c>reference</c> or <c>referenceShadowed</c>).</summary>
    internal static bool TryParseAtmosphere(string text, out AtmosphereMode mode) =>
        Enum.TryParse(text, ignoreCase: true, out mode) && mode != AtmosphereMode.None && Enum.IsDefined(mode) && !int.TryParse(text, out _);

    private static AtmosphereMode Atmosphere(string? text, List<string> problems)
    {
        if (text is null)
            return AtmosphereMode.Luts;
        if (TryParseAtmosphere(text, out AtmosphereMode mode))
            return mode;
        problems.Add($"--atmosphere must be luts, reference or referenceShadowed, got '{text}'.");
        return AtmosphereMode.Luts;
    }

    /// <summary>Parses a <c>--light-sampling</c> value (<c>mis</c> or <c>sunOnly</c>).</summary>
    internal static bool TryParseLightSampling(string text, out LightSampling sampling) =>
        Enum.TryParse(text, ignoreCase: true, out sampling) && sampling != LightSampling.None && Enum.IsDefined(sampling) && !int.TryParse(text, out _);

    private static LightSampling Lights(string? text, List<string> problems)
    {
        if (text is null)
            return LightSampling.Mis;
        if (TryParseLightSampling(text, out LightSampling sampling))
            return sampling;
        problems.Add($"--light-sampling must be mis or sunOnly, got '{text}'.");
        return LightSampling.Mis;
    }

    /// <summary>The recipe overrides of the place options (JSON values by field), checked here so a typo is one CLI error.</summary>
    private static Dictionary<string, string> PlaceOverrides(Dictionary<string, string> values, List<string> problems)
    {
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        bool hasLocation = values.TryGetValue("--location", out string? location);
        bool hasLatitude = values.TryGetValue("--latitude", out string? latitudeText);
        if (hasLocation && hasLatitude)
            problems.Add("--location and --latitude both choose the place; give one.");
        if (hasLocation)
        {
            if (Enum.TryParse(location, ignoreCase: true, out Place place) && Enum.IsDefined(place) && place != Place.None && !int.TryParse(location, out _))
            {
                overrides["location"] = JsonSerializer.Serialize(JsonName(place.ToString()));
                overrides["latitude"] = "null";
            }
            else
            {
                problems.Add($"Unknown --location '{location}'; one of: {PlaceList}.");
            }
        }
        if (hasLatitude)
        {
            if (double.TryParse(latitudeText, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double latitude)
                && latitude is >= -90.0 and <= 90.0)
            {
                overrides["latitude"] = latitude.ToString("R", CultureInfo.InvariantCulture);
                overrides["location"] = "\"none\"";
            }
            else
            {
                problems.Add($"--latitude must be a number of degrees in [-90, 90] (north positive), got '{latitudeText}'.");
            }
        }
        if (values.TryGetValue("--date", out string? dateText))
        {
            if (DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
                overrides["date"] = JsonSerializer.Serialize(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            else
                problems.Add($"--date must look like 2026-10-15, got '{dateText}'.");
        }
        if (values.TryGetValue("--time", out string? timeText))
        {
            if (Enum.TryParse(timeText, ignoreCase: true, out TimeOfDay preset) && Enum.IsDefined(preset) && !int.TryParse(timeText, out _))
            {
                overrides["time"] = JsonSerializer.Serialize(JsonName(preset.ToString()));
                overrides["solarTime"] = "null";
            }
            else if (TimeOnly.TryParseExact(timeText, ["H:mm", "H:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time))
            {
                overrides["solarTime"] = JsonSerializer.Serialize(time.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
                overrides["sunElevation"] = "null";
            }
            else
            {
                problems.Add($"--time must be dawn, day, goldenHour, dusk, night or a local solar time like 17:30, got '{timeText}'.");
            }
        }
        if (values.TryGetValue("--sun-elevation", out string? elevationText))
        {
            if (double.TryParse(elevationText, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double elevation)
                && elevation is >= -90.0 and <= 90.0)
            {
                if (overrides.ContainsKey("solarTime") && values.ContainsKey("--time") && overrides["solarTime"] != "null")
                    problems.Add("--time hh:mm and --sun-elevation both set the time; give one.");
                overrides["sunElevation"] = elevation.ToString("R", CultureInfo.InvariantCulture);
                overrides["solarTime"] = "null";
            }
            else
            {
                problems.Add($"--sun-elevation must be a number of degrees in [-90, 90], got '{elevationText}'.");
            }
        }
        if (values.TryGetValue("--altitude", out string? altitudeText))
        {
            if (double.TryParse(altitudeText, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double altitude)
                && altitude is >= 0.0 and <= Recipe.MaxAltitudeMetres)
                overrides["altitude"] = altitude.ToString("R", CultureInfo.InvariantCulture);
            else
                problems.Add($"--altitude must be a number of metres above sea level in [0, 8000], got '{altitudeText}'.");
        }
        return overrides;
    }

    private static string JsonName(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);

    private static PixelWindow Loupe(string? text, int width, int height, List<string> problems)
    {
        if (text is null)
            return default;
        if (!PixelWindow.TryParse(text, out PixelWindow window))
        {
            problems.Add($"--loupe must look like x,y,width,height (pixels, positive size), got '{text}'.");
            return default;
        }
        if (!window.FitsIn(width, height))
            problems.Add($"--loupe {window} does not fit the {width}×{height} frame.");
        return window;
    }

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
