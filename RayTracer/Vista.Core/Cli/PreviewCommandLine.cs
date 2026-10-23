using System.Globalization;
using System.Text.Json;
using Vista.Astronomy;
using Vista.Output;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Sliders;
using Vista.World;

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

    /// <summary>
    /// The slider file (vista-plan §10 M1.11): the sliders pinned and their values; null pins none beyond what the CLI options
    /// (<see cref="SliderOverrides"/>) and an older recipe's own fields pin.
    /// </summary>
    public string? SlidersPath { get; init; }

    /// <summary>
    /// Slider-file fields to override before the strict parse, as dotted camelCase path → JSON value: the place, time and weather
    /// options pin their sliders through these, and a contact sheet varies a slider through them.
    /// </summary>
    public IReadOnlyDictionary<string, string> SliderOverrides { get; init; } = new Dictionary<string, string>();

    /// <summary>Camera fields to override, applied to the supplied or the default camera.</summary>
    public IReadOnlyDictionary<string, string> CameraOverrides { get; init; } = new Dictionary<string, string>();

    /// <summary>How the air is evaluated (vista-plan §10 M1.3): the LUTs, or the reference.</summary>
    public AtmosphereMode Atmosphere { get; init; } = AtmosphereMode.Luts;

    /// <summary>Which lights a surface samples (vista-plan §10 M1.5): sun and sky with MIS, or M1.4's sun alone.</summary>
    public LightSampling LightSampling { get; init; } = LightSampling.Mis;

    /// <summary>The bounce from which Russian roulette may end a path (<see cref="RenderSettings.RussianRouletteStart"/>).</summary>
    public int RussianRouletteStart { get; init; } = RenderSettings.DefaultRussianRouletteStart;

    /// <summary>
    /// Also write a denoised beauty (vista-plan §10 M1.6), with these settings; null writes the raw beauty only. The raw
    /// beauty is always written.
    /// </summary>
    public DenoiseSettings? Denoise { get; init; }

    /// <summary>
    /// The world grid (vista-plan §4.1). Every world is made on <see cref="WorldGrid.Default"/>; another grid is another world,
    /// for measurements (and the tests' small worlds).
    /// </summary>
    public WorldGrid Grid { get; init; } = WorldGrid.Default;

    /// <summary>Erode the world (vista-plan §10 M1.7); false renders the un-eroded terrain.</summary>
    public bool Erosion { get; init; } = true;

    /// <summary>How the terrain is meshed (vista-plan §10 M1.9): the view-dependent mesh by default.</summary>
    public MeshSettings Mesh { get; init; } = new();

    /// <summary>How the PNG's exposure is metered (vista-plan §10 M1.10a): the incident meter by default. The EXR never changes.</summary>
    public Metering Meter { get; init; } = Metering.Incident;

    /// <summary>The PNG's display curve (vista-plan §10 M1.10a): AgX by default. The EXR never changes.</summary>
    public DisplayCurve Display { get; init; } = DisplayTransform.DefaultCurve;

    /// <summary>AgX's look (vista-plan §10 M1.11b); the ACES fit has none and ignores it. The EXR never changes.</summary>
    public DisplayLook Look { get; init; } = DisplayTransform.DefaultLook;

    /// <summary>
    /// The stops the meter adds on its own, beside the camera's EV (vista-plan §10 M1.11b); null for the meter's default
    /// (<see cref="IncidentMeter.DefaultCompensationEv"/>, or 0 for the average meter). The EXR never changes.
    /// </summary>
    public double? MeterCompensationEv { get; init; }

    /// <summary>
    /// The 8-bit PNG's dither (vista-plan §10 M1.11c): the triangular dither by default, <see cref="PngDither.Off"/> for M1.11b's
    /// bytes. A 16-bit PNG has none. The EXR never changes.
    /// </summary>
    public PngDither Dither { get; init; } = DisplayTransform.DefaultDither;

    /// <summary>The beauty's and the denoised beauty's bits per channel (vista-plan §10 M1.11c): 8, or 16 with <c>--png16</c>.</summary>
    public int BitDepth { get; init; } = DisplayTransform.DefaultBitDepth;

    /// <summary>How diffuse surfaces scatter (vista-plan §10 M1.12): <c>--diffuse orenNayar</c> (the default) or <c>lambert</c>.</summary>
    public DiffuseModel Diffuse { get; init; } = DiffuseModel.OrenNayar;

    /// <summary>Whether forest grows below the place's tree line (vista-plan §10 M1.12b): <c>--forest on</c> (the default) or <c>off</c>.</summary>
    public bool Forest { get; init; } = true;

    /// <summary>How the forest's canopy scatters (vista-plan §10 M1.12b): <c>--canopy kernels</c> (the default) or <c>rough</c>.</summary>
    public CanopyModel Canopy { get; init; } = CanopyModel.Kernels;

    /// <summary>
    /// Which hero wavelengths the render traces and how they weigh into colour (vista-plan §10 M1.12a): <c>--quadrature bins</c> (the
    /// default) or <c>engine</c>.
    /// </summary>
    public Quadrature Quadrature { get; init; } = WavelengthTable.DefaultQuadrature;
}

/// <summary>
/// Parses the headless preview CLI (vista-plan §7.4):
/// <code>
/// Vista.App --preview --recipe r.json --seed N [--camera c.json] --out p.png
///           [--width 320] [--aspect 16:9] [--spp 64] [--bounces 6] [--dispatch-ms 250]
///           [--debug all | albedo,normal,depth,material,tier,variance] [--loupe x,y,w,h] [--dry-run]
///           [--sliders s.json] [--location zermatt | --latitude 46.0] [--season autumn] [--date 2026-10-15]
///           [--time dawn | --time 17:30] [--sun-elevation -2] [--altitude 3000] [--temperature 5 | --temperature 41F]
///           [--humidity 60] [--snowline 2400] [--visibility 80] [--atmosphere luts | reference | referenceShadowed]
///           [--light-sampling mis | sunOnly] [--rr-start 2] [--denoise [--denoise-device cpu | cuda]]
///           [--erosion on | off] [--grid 4097x8] [--mesh view | uniform] [--mesh-error 1] [--detail on | off] [--far-field on | off]
///           [--materials on | off] [--diffuse orenNayar | lambert] [--quadrature bins | engine]
///           [--forest on | off] [--canopy kernels | rough]
///           [--meter incident | average] [--display agx | aces] [--look punchy | base] [--compensation EV]
///           [--dither on | off] [--png16]
/// </code>
/// The preview keeps the final's quality settings and changes only the pixel count, at the target's aspect
/// ratio (320×180 for 16:9). <c>--loupe</c> renders one window of that frame at full detail. <c>--sliders</c> names the slider
/// file (vista-plan §10 M1.11): the sliders pinned. The slider options pin one slider each on top of it (§3.4–§3.6):
/// <c>--location</c> or <c>--latitude</c> the place (clearing the other), <c>--season</c> and <c>--date</c> the season and the
/// date, <c>--time</c> a time-of-day preset (clearing a pinned solar time) or a local solar time, and <c>--sun-elevation</c> the
/// time at which the sun stands that high (rising when the time is dawn); <c>--altitude</c> the scene's base altitude,
/// <c>--temperature</c> (°C, or °F with an <c>F</c>), <c>--humidity</c> (%), <c>--snowline</c> (m) and <c>--visibility</c> (km)
/// the weather.
/// <c>--atmosphere reference</c> renders the slow, unbiased reference atmosphere instead of the LUTs (vista-plan §10
/// M1.3), and <c>referenceShadowed</c> adds the terrain's shadow to the air's sunlight (M1.4). <c>--light-sampling
/// sunOnly</c> renders with M1.4's estimator (the sky left to the bounce) instead of sampling the sky as a light too (M1.5);
/// with <c>--rr-start 3</c>, M1.4's Russian roulette as well, it renders M1.4's images bit for bit. <c>--denoise</c> also writes
/// the beauty denoised by Intel Open Image Denoise (M1.6) beside the raw one, on the CPU or, with <c>--denoise-device cuda</c>,
/// the GPU. <c>--erosion off</c> renders the world un-eroded (M1.7), and <c>--grid</c> makes the world on another grid (samples per
/// side × metres between them): another world, for measurements. <c>--mesh-error</c> sets the view-dependent mesh's error target in
/// pixels (M1.9), <c>--detail off</c> leaves out the detail below the world grid, <c>--far-field off</c> the terrain beyond it, and
/// <c>--mesh uniform</c> meshes every sample of the world grid and nothing else (M1.7's mesh, bent onto the Earth), for comparisons.
/// <c>--materials off</c> renders M0's single material over M1.9's smooth detail instead of the ground's materials by rule and
/// their detail (M1.12), and <c>--diffuse lambert</c> scatters light as Lambert's surfaces do instead of rough ones.
/// <c>--quadrature engine</c> traces the engine's hero wavelengths with the engine's weights instead of Vista's own bins (M1.12a):
/// <c>main</c>'s colours before M1.12a, cast and all.
/// <c>--forest off</c> grows no forest below the tree line (M1.12's ground: meadow and soil there), and <c>--canopy rough</c> shades
/// the forest as rough diffuse ground instead of by its canopy's kernels (M1.12b).
/// <c>--meter average</c> exposes the PNG by the frame's log-average luminance (M1.3's meter) instead of a grey card facing the sun
/// (M1.10a), and <c>--display aces</c> display-encodes it through the ACES fit instead of AgX; neither changes the EXR.
/// <c>--dither off</c> rounds the 8-bit PNG's values without M1.11c's dither, and <c>--png16</c> writes the beauty and the denoised
/// beauty at 16 bits a channel instead, which need none.
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
        "--erosion off renders the world un-eroded; --grid SIZExCELL makes it on another grid (another world, for measurements).\n" +
        "--mesh-error <px>: the view-dependent mesh's error target (default 1); --detail off leaves out the detail below the grid,\n" +
        "  --far-field off the terrain beyond it; --mesh uniform meshes every grid sample and nothing else (for comparisons).\n" +
        "--materials on (default) | off: the ground's rock, scree, soil and grass by rule and their detail, or M0's single grey material\n" +
        "  over smooth detail. --diffuse orenNayar (default) | lambert: rough diffuse surfaces, or Lambert's.\n" +
        "--quadrature bins (default) | engine: the hero wavelengths and their weights, Vista's own 50 bins of equal colour mass,\n" +
        "  or the engine's table (before M1.12a; every colour 1.7-4 dE off).\n" +
        "--forest on (default) | off: forest below the place's tree line, or M1.12's meadow and soil there.\n" +
        "  --canopy kernels (default) | rough: the canopy's own scattering (its crowns' shadows and hot spot), or rough diffuse ground.\n" +
        "--dispatch-ms sets the GPU time per dispatch (at most 1000); it changes how the render is split, never the image.\n" +
        "--loupe renders only the window at column x, row y, w wide and h high of the --width frame: exactly a crop of it.\n" +
        "--sliders <sliders.json>: the slider file, the sliders pinned (any slider it does not name is auto). Each option below\n" +
        "  pins one slider on top of it:\n" +
        "  [--location <place> | --latitude <degrees>] [--season <season>] [--date yyyy-mm-dd] [--time <preset> | --time hh:mm]\n" +
        "  places: yellowstone, banff, yosemite, lofoten, iceland, scottishHighlands, zermatt (default), dolomites,\n" +
        "          torresDelPaine, fiordland, hokkaido\n" +
        "  seasons: spring, summer (default), autumn, winter; a date pins the date, and the season follows it unless pinned too.\n" +
        "  presets: dawn (the rising sun at −4°), day (solar noon), goldenHour (the setting sun at +2°), dusk (setting, −4°),\n" +
        "  night (solar midnight); hh:mm is local solar time (12:00 is solar noon).\n" +
        "  --sun-elevation <degrees>: the time the sun stands that high (geometric; rising with --time dawn, else setting).\n" +
        "  --altitude <metres>: the scene's base altitude above sea level (default: the place's valley floor), 0 to 8000.\n" +
        "  --temperature <°C> (or <°F>F, e.g. 41F), --humidity <%>: the air at the valley floor (default: the climate's).\n" +
        "  --snowline <metres>: where seasonal snow begins, above sea level; --visibility <km>: the haze (default: the humidity's).\n" +
        "--atmosphere luts (default) | reference | referenceShadowed: the precomputed air, or the air path-traced as a medium\n" +
        "  (slow, unbiased), optionally with the terrain's shadow in it.\n" +
        "--light-sampling mis (default) | sunOnly: sample the sun and the sky as lights, weighed against the bounce, or (M1.4)\n" +
        "  the sun alone, the sky left to the bounce; the same image on average, with different noise.\n" +
        "--rr-start <bounce>: the bounce from which Russian roulette may end a path (default 2; M1.4 used 3).\n" +
        "--denoise: also write <preview>.denoised.png and <preview>.denoised.exr (Intel Open Image Denoise, fetched by\n" +
        "  tools/fetch-oidn.ps1; without it a warning, and the raw image only). --denoise-device cpu (default) | cuda.\n" +
        "--meter incident (default) | average: expose the PNG for a grey card facing the sun where the camera stands, or for the\n" +
        "  frame's log-average luminance. --display agx (default) | aces: the PNG's display curve. --look punchy (default) | base: AgX's look\n" +
        "  (the ACES fit has none). --compensation <EV>: the stops the meter adds on its own, beside the camera's EV (default: the\n" +
        "  incident meter's +1 EV, the average meter's 0). None of them changes the EXR.\n" +
        "--dither on (default) | off: a triangular dither of ±1 step before the 8-bit PNG is rounded, against banding in smooth skies.\n" +
        "--png16: write <preview>.png (and <preview>.denoised.png) at 16 bits a channel instead, with no dither.";

    /// <summary>The slider-file fields each slider option decides, so a contact sheet cannot vary them as well.</summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> SliderOptionFields { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["--location"] = ["location", "latitude"],
            ["--latitude"] = ["location", "latitude"],
            ["--season"] = ["season", "date"],
            ["--date"] = ["date", "season"],
            ["--time"] = ["time", "solarTime"],
            ["--sun-elevation"] = ["sunElevation", "solarTime"],
            ["--altitude"] = ["altitude"],
            ["--temperature"] = ["temperature", "temperatureUnit"],
            ["--humidity"] = ["humidity"],
            ["--snowline"] = ["snowline"],
            ["--visibility"] = ["visibility"],
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

        (Dictionary<string, string> values, IReadOnlySet<string> flags) = SplitOptions(args, 1, ValueOptions, problems);
        PreviewRequest? parsed = Build(values, flags, recipeRequired: true, seedRequired: true, problems);
        if (problems.Count > 0)
            return false;
        request = parsed;
        return true;
    }

    /// <summary>The options that take a value.</summary>
    internal static IReadOnlySet<string> ValueOptions { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "--recipe", "--seed", "--camera", "--out", "--width", "--aspect", "--spp", "--bounces", "--debug", "--dispatch-ms", "--loupe",
        "--sliders", "--location", "--latitude", "--season", "--date", "--time", "--sun-elevation", "--altitude", "--temperature", "--humidity",
        "--snowline", "--visibility", "--atmosphere", "--light-sampling",
        "--rr-start", "--denoise-device", "--erosion", "--grid", "--mesh", "--mesh-error", "--detail", "--far-field", "--meter", "--display", "--look", "--compensation",
        "--dither", "--materials", "--diffuse", "--quadrature", "--forest", "--canopy",
    };

    /// <summary>The options that take no value.</summary>
    internal static IReadOnlySet<string> FlagOptions { get; } = new HashSet<string>(StringComparer.Ordinal) { "--dry-run", "--denoise", "--png16" };

    /// <summary>
    /// Splits <paramref name="args"/> from <paramref name="start"/> into <c>--option value</c> pairs and the flags
    /// (<see cref="FlagOptions"/>) given, reporting unknown, valueless and repeated options.
    /// </summary>
    internal static (Dictionary<string, string> Values, IReadOnlySet<string> Flags) SplitOptions(
        IReadOnlyList<string> args, int start, IReadOnlySet<string> valueOptions, List<string> problems)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        int k = start;
        while (k < args.Count)
        {
            string arg = args[k++];
            if (FlagOptions.Contains(arg))
            {
                if (!flags.Add(arg))
                    problems.Add($"{arg} is given more than once.");
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
        return (values, flags);
    }

    /// <summary>
    /// Builds a request from split options and flags, adding every problem to <paramref name="problems"/>. A contact
    /// sheet whose axis supplies the recipe or the seed passes false for that requirement; the field is then
    /// empty or 0 until the axis fills it in.
    /// </summary>
    internal static PreviewRequest? Build(
        Dictionary<string, string> values, IReadOnlySet<string> flags, bool recipeRequired, bool seedRequired, List<string> problems)
    {
        int before = problems.Count;
        bool dryRun = flags.Contains("--dry-run");
        DenoiseSettings? denoise = Denoise(flags.Contains("--denoise"), values.GetValueOrDefault("--denoise-device"), dryRun, problems);
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
        Dictionary<string, string> sliderOverrides = SliderPinOverrides(values, problems);
        AtmosphereMode atmosphere = Atmosphere(values.GetValueOrDefault("--atmosphere"), problems);
        LightSampling lightSampling = Lights(values.GetValueOrDefault("--light-sampling"), problems);
        int rrStart = Int(values, "--rr-start", RenderSettings.DefaultRussianRouletteStart, 0, RenderSettings.MaxRussianRouletteStart, problems);
        bool erosion = Erosion(values.GetValueOrDefault("--erosion"), problems);
        WorldGrid grid = Grid(values.GetValueOrDefault("--grid"), problems);
        MeshSettings mesh = Mesh(values, problems);
        Metering meter = Meter(values.GetValueOrDefault("--meter"), problems);
        DisplayCurve display = Display(values.GetValueOrDefault("--display"), problems);
        DisplayLook look = Look(values.GetValueOrDefault("--look"), problems);
        double? compensation = Compensation(values.GetValueOrDefault("--compensation"), problems);
        PngDither dither = Dither(values.GetValueOrDefault("--dither"), problems);
        DiffuseModel diffuse = Diffuse(values.GetValueOrDefault("--diffuse"), problems);
        Quadrature quadrature = QuadratureOf(values.GetValueOrDefault("--quadrature"), problems);
        bool forest = OnOff(values.GetValueOrDefault("--forest"), "--forest", problems);
        CanopyModel canopy = CanopyOf(values.GetValueOrDefault("--canopy"), problems);

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
            SlidersPath = values.GetValueOrDefault("--sliders"),
            SliderOverrides = sliderOverrides,
            Atmosphere = atmosphere,
            LightSampling = lightSampling,
            RussianRouletteStart = rrStart,
            Denoise = denoise,
            Erosion = erosion,
            Grid = grid,
            Mesh = mesh,
            Meter = meter,
            Display = display,
            Look = look,
            MeterCompensationEv = compensation,
            Dither = dither,
            BitDepth = flags.Contains("--png16") ? 16 : DisplayTransform.DefaultBitDepth,
            Diffuse = diffuse,
            Quadrature = quadrature,
            Forest = forest,
            Canopy = canopy,
        };
    }

    /// <summary>Parses an on/off option's value; on when it is not given.</summary>
    private static bool OnOff(string? text, string option, List<string> problems)
    {
        switch (text)
        {
            case null or "on":
                return true;
            case "off":
                return false;
            default:
                problems.Add($"{option} must be on or off, got '{text}'.");
                return true;
        }
    }

    /// <summary>Parses a <c>--canopy</c> value (<c>kernels</c> or <c>rough</c>).</summary>
    internal static bool TryParseCanopy(string text, out CanopyModel canopy) =>
        Enum.TryParse(text, ignoreCase: true, out canopy) && canopy != CanopyModel.None && Enum.IsDefined(canopy) && !int.TryParse(text, out _);

    private static CanopyModel CanopyOf(string? text, List<string> problems)
    {
        if (text is null)
            return CanopyModel.Kernels;
        if (TryParseCanopy(text, out CanopyModel canopy))
            return canopy;
        problems.Add($"--canopy must be kernels or rough, got '{text}'.");
        return CanopyModel.Kernels;
    }

    /// <summary>Parses a <c>--diffuse</c> value (<c>orenNayar</c> or <c>lambert</c>).</summary>
    internal static bool TryParseDiffuse(string text, out DiffuseModel diffuse) =>
        Enum.TryParse(text, ignoreCase: true, out diffuse) && diffuse != DiffuseModel.None && Enum.IsDefined(diffuse) && !int.TryParse(text, out _);

    private static DiffuseModel Diffuse(string? text, List<string> problems)
    {
        if (text is null)
            return DiffuseModel.OrenNayar;
        if (TryParseDiffuse(text, out DiffuseModel diffuse))
            return diffuse;
        problems.Add($"--diffuse must be orenNayar or lambert, got '{text}'.");
        return DiffuseModel.OrenNayar;
    }

    /// <summary>Parses a <c>--quadrature</c> value (<c>bins</c> or <c>engine</c>).</summary>
    internal static bool TryParseQuadrature(string text, out Quadrature quadrature) =>
        Enum.TryParse(text, ignoreCase: true, out quadrature) && quadrature != Quadrature.None && Enum.IsDefined(quadrature) && !int.TryParse(text, out _);

    private static Quadrature QuadratureOf(string? text, List<string> problems)
    {
        if (text is null)
            return WavelengthTable.DefaultQuadrature;
        if (TryParseQuadrature(text, out Quadrature quadrature))
            return quadrature;
        problems.Add($"--quadrature must be bins or engine, got '{text}'.");
        return WavelengthTable.DefaultQuadrature;
    }

    /// <summary>Parses a <c>--dither</c> value: <c>on</c> (the triangular dither) or <c>off</c>.</summary>
    internal static bool TryParseDither(string text, out PngDither dither)
    {
        dither = text switch
        {
            "on" => PngDither.Triangular,
            "off" => PngDither.Off,
            _ => PngDither.None,
        };
        return dither != PngDither.None;
    }

    private static PngDither Dither(string? text, List<string> problems)
    {
        if (text is null)
            return DisplayTransform.DefaultDither;
        if (TryParseDither(text, out PngDither dither))
            return dither;
        problems.Add($"--dither must be on or off, got '{text}'.");
        return DisplayTransform.DefaultDither;
    }

    /// <summary>Parses a <c>--meter</c> value (<c>incident</c> or <c>average</c>).</summary>
    internal static bool TryParseMeter(string text, out Metering meter) =>
        Enum.TryParse(text, ignoreCase: true, out meter) && meter != Metering.None && Enum.IsDefined(meter) && !int.TryParse(text, out _);

    private static Metering Meter(string? text, List<string> problems)
    {
        if (text is null)
            return Metering.Incident;
        if (TryParseMeter(text, out Metering meter))
            return meter;
        problems.Add($"--meter must be incident or average, got '{text}'.");
        return Metering.Incident;
    }

    /// <summary>Parses a <c>--display</c> value (<c>agx</c> or <c>aces</c>).</summary>
    internal static bool TryParseDisplay(string text, out DisplayCurve curve) =>
        Enum.TryParse(text, ignoreCase: true, out curve) && curve != DisplayCurve.None && Enum.IsDefined(curve) && !int.TryParse(text, out _);

    private static DisplayCurve Display(string? text, List<string> problems)
    {
        if (text is null)
            return DisplayTransform.DefaultCurve;
        if (TryParseDisplay(text, out DisplayCurve curve))
            return curve;
        problems.Add($"--display must be agx or aces, got '{text}'.");
        return DisplayTransform.DefaultCurve;
    }

    /// <summary>Parses a <c>--look</c> value (<c>base</c> or <c>punchy</c>).</summary>
    internal static bool TryParseLook(string text, out DisplayLook look) =>
        Enum.TryParse(text, ignoreCase: true, out look) && look != DisplayLook.None && Enum.IsDefined(look) && !int.TryParse(text, out _);

    private static DisplayLook Look(string? text, List<string> problems)
    {
        if (text is null)
            return DisplayTransform.DefaultLook;
        if (TryParseLook(text, out DisplayLook look))
            return look;
        problems.Add($"--look must be base or punchy, got '{text}'.");
        return DisplayTransform.DefaultLook;
    }

    /// <summary>Most stops <c>--compensation</c> may add or take away.</summary>
    internal const double MaxCompensationEv = 10.0;

    /// <summary>Parses a <c>--compensation</c> value: stops, within ±<see cref="MaxCompensationEv"/>.</summary>
    internal static bool TryParseCompensation(string text, out double ev) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out ev) && double.IsFinite(ev) && Math.Abs(ev) <= MaxCompensationEv;

    private static double? Compensation(string? text, List<string> problems)
    {
        if (text is null)
            return null;
        if (TryParseCompensation(text, out double ev))
            return ev;
        problems.Add($"--compensation must be a number of stops within ±{MaxCompensationEv}, got '{text}'.");
        return null;
    }

    private static bool Erosion(string? text, List<string> problems)
    {
        switch (text)
        {
            case null or "on":
                return true;
            case "off":
                return false;
            default:
                problems.Add($"--erosion must be on or off, got '{text}'.");
                return true;
        }
    }

    /// <summary>Parses a <c>--grid</c> value, <c>SIZExCELL</c> (e.g. <c>4097x8</c>: samples per side, metres between them).</summary>
    internal static WorldGrid Grid(string? text, List<string> problems)
    {
        if (text is null)
            return WorldGrid.Default;
        string[] parts = text.Split('x');
        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int size) && size is >= 3 and <= MaxGridSize
            && double.TryParse(parts[1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double cell) && cell > 0.0 && double.IsFinite(cell))
            return new WorldGrid(size, cell);
        problems.Add($"--grid must be SIZExCELL with 3 to {MaxGridSize} samples and a positive cell in metres (e.g. 4097x8), got '{text}'.");
        return WorldGrid.Default;
    }

    /// <summary>The largest grid <c>--grid</c> accepts: its mesh and erosion state must fit in memory.</summary>
    public const int MaxGridSize = 8193;

    /// <summary>The denoise settings of <c>--denoise</c> and <c>--denoise-device</c>; null without <c>--denoise</c>.</summary>
    private static DenoiseSettings? Denoise(bool denoise, string? deviceText, bool dryRun, List<string> problems)
    {
        if (!denoise)
        {
            if (deviceText is not null)
                problems.Add("--denoise-device needs --denoise.");
            return null;
        }
        if (dryRun)
            problems.Add("--denoise needs a render, and --dry-run renders nothing.");
        var settings = new DenoiseSettings();
        if (deviceText is null)
            return settings;
        if (Enum.TryParse(deviceText, ignoreCase: true, out DenoiseDevice device) && device != DenoiseDevice.None && Enum.IsDefined(device)
            && !int.TryParse(deviceText, out _))
            return settings with { Device = device };
        problems.Add($"--denoise-device must be cpu or cuda, got '{deviceText}'.");
        return settings;
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

    /// <summary>Parses a <c>--mesh</c> value (<c>view</c> or <c>uniform</c>).</summary>
    internal static bool TryParseMeshKind(string text, out MeshKind kind) =>
        Enum.TryParse(text, ignoreCase: true, out kind) && kind != MeshKind.None && Enum.IsDefined(kind) && !int.TryParse(text, out _);

    /// <summary>Parses a <c>--mesh-error</c> value: pixels between 0.01 and 1000.</summary>
    internal static bool TryParseMeshError(string text, out double pixels) =>
        double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out pixels) && pixels is >= 0.01 and <= 1000.0;

    /// <summary>
    /// The mesh settings of <c>--mesh</c>, <c>--mesh-error</c>, <c>--detail</c> and <c>--far-field</c> (vista-plan §10 M1.9), and
    /// <c>--materials</c> (M1.12).
    /// </summary>
    private static MeshSettings Mesh(Dictionary<string, string> values, List<string> problems)
    {
        var settings = new MeshSettings();
        if (values.TryGetValue("--mesh", out string? kindText))
        {
            if (TryParseMeshKind(kindText, out MeshKind kind))
                settings = settings with { Kind = kind };
            else
                problems.Add($"--mesh must be view or uniform, got '{kindText}'.");
        }
        if (values.TryGetValue("--mesh-error", out string? errorText))
        {
            if (TryParseMeshError(errorText, out double pixels))
                settings = settings with { ErrorPixels = pixels };
            else
                problems.Add($"--mesh-error must be a number of pixels between 0.01 and 1000, got '{errorText}'.");
        }
        foreach ((string option, Func<MeshSettings, bool, MeshSettings> set) in new (string, Func<MeshSettings, bool, MeshSettings>)[]
        {
            ("--detail", (s, on) => s with { Detail = on }),
            ("--far-field", (s, on) => s with { FarField = on }),
            ("--materials", (s, on) => s with { Materials = on }),
        })
        {
            switch (values.GetValueOrDefault(option))
            {
                case null:
                    break;
                case "on":
                    settings = set(settings, true);
                    break;
                case "off":
                    settings = set(settings, false);
                    break;
                case string other:
                    problems.Add($"{option} must be on or off, got '{other}'.");
                    break;
            }
        }
        return settings;
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

    /// <summary>The slider-file overrides of the slider options (JSON values by field), checked here so a typo is one CLI error.</summary>
    private static Dictionary<string, string> SliderPinOverrides(Dictionary<string, string> values, List<string> problems)
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
        if (values.TryGetValue("--season", out string? seasonText))
        {
            if (Enum.TryParse(seasonText, ignoreCase: true, out Season season) && Enum.IsDefined(season) && !int.TryParse(seasonText, out _))
                overrides["season"] = JsonSerializer.Serialize(JsonName(season.ToString()));
            else
                problems.Add($"--season must be spring, summer, autumn or winter, got '{seasonText}'.");
        }
        Number("--altitude", "altitude", 0.0, SliderPins.MaxAltitudeMetres, "metres above sea level");
        if (values.TryGetValue("--temperature", out string? temperatureText))
        {
            if (TryParseTemperature(temperatureText, out double celsius, out TemperatureUnit unit))
            {
                overrides["temperature"] = celsius.ToString("R", CultureInfo.InvariantCulture);
                overrides["temperatureUnit"] = JsonSerializer.Serialize(JsonName(unit.ToString()));
            }
            else
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture,
                    $"--temperature must be degrees Celsius in [{SliderPins.MinTemperatureCelsius}, {SliderPins.MaxTemperatureCelsius}] (or Fahrenheit with an F, like 41F), got '{temperatureText}'."));
            }
        }
        Number("--humidity", "humidity", 0.0, 100.0, "% relative humidity");
        Number("--snowline", "snowline", 0.0, SliderPins.MaxSnowlineMetres, "metres above sea level");
        Number("--visibility", "visibility", SliderPins.MinVisibilityKm, SliderPins.MaxVisibilityKm, "km");
        return overrides;

        void Number(string option, string field, double min, double max, string unit)
        {
            if (!values.TryGetValue(option, out string? text))
                return;
            if (double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double value)
                && value >= min && value <= max)
                overrides[field] = value.ToString("R", CultureInfo.InvariantCulture);
            else
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"{option} must be a number of {unit} in [{min}, {max}], got '{text}'."));
        }
    }

    /// <summary>
    /// Parses a <c>--temperature</c> value: degrees Celsius (<c>5</c>, <c>-3.5</c>, <c>5C</c>), or Fahrenheit with an <c>F</c>
    /// (<c>41F</c>), which also asks for temperatures to be shown in °F; the value returned is °C, inside the slider's range.
    /// </summary>
    internal static bool TryParseTemperature(string text, out double celsius, out TemperatureUnit unit)
    {
        ArgumentNullException.ThrowIfNull(text);
        string number = text.Trim();
        unit = TemperatureUnit.Celsius;
        if (number.EndsWith('F') || number.EndsWith('f'))
        {
            unit = TemperatureUnit.Fahrenheit;
            number = number[..^1];
        }
        else if (number.EndsWith('C') || number.EndsWith('c'))
        {
            number = number[..^1];
        }
        celsius = 0.0;
        if (!double.TryParse(number, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double value))
            return false;
        celsius = unit == TemperatureUnit.Fahrenheit ? (value - 32.0) * 5.0 / 9.0 : value;
        return celsius is >= SliderPins.MinTemperatureCelsius and <= SliderPins.MaxTemperatureCelsius;
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
