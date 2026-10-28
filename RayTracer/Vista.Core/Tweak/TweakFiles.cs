using System.Globalization;
using System.Text;
using Vista.Cli;
using Vista.Recipes;
using Vista.Scene;
using Vista.Sliders;

namespace Vista.Tweak;

/// <summary>What a window run does (vista-plan §10 M1.16).</summary>
public enum TweakRun
{
    /// <summary>Not set.</summary>
    None,

    /// <summary>The preview, with the scene's camera (or, for a new scene of no mountains, the default camera).</summary>
    Preview,

    /// <summary>The preview with the auto camera's pick (<c>--auto-camera</c>), which becomes the scene's camera.</summary>
    AutoCamera,

    /// <summary>The estimate of the final (<c>--estimate</c>).</summary>
    Estimate,

    /// <summary>The final.</summary>
    Final,
}

/// <summary>The four files of a scene, named alike: <c>name.recipe.json</c>, <c>name.camera.json</c>, <c>name.sliders.json</c>, <c>name.options.txt</c>.</summary>
/// <param name="Recipe">The recipe file.</param>
/// <param name="Camera">The camera file.</param>
/// <param name="Sliders">The slider file.</param>
/// <param name="Options">The options file (the seed and the sizes, as a watch reads them).</param>
public sealed record TweakPaths(string Recipe, string Camera, string Sliders, string Options)
{
    /// <summary>The scene <paramref name="name"/>'s files in <paramref name="folder"/>.</summary>
    public static TweakPaths Of(string folder, string name)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(name);
        string Path_(string suffix) => Path.GetFullPath(Path.Combine(folder, name + suffix));
        return new TweakPaths(Path_(TweakFiles.RecipeSuffix), Path_(TweakFiles.CameraSuffix), Path_(TweakFiles.SlidersSuffix), Path_(TweakFiles.OptionsSuffix));
    }

    /// <summary>The files named like <paramref name="recipePath"/> (<c>name.recipe.json</c>, or any <c>name.json</c>) beside it.</summary>
    public static TweakPaths Beside(string recipePath)
    {
        ArgumentNullException.ThrowIfNull(recipePath);
        string file = Path.GetFileName(recipePath);
        string name = file.EndsWith(TweakFiles.RecipeSuffix, StringComparison.OrdinalIgnoreCase)
            ? file[..^TweakFiles.RecipeSuffix.Length]
            : Path.GetFileNameWithoutExtension(file);
        return Of(Path.GetDirectoryName(Path.GetFullPath(recipePath))!, name);
    }

    /// <summary>The scene's name: the recipe file's, without its suffix.</summary>
    public string Name => Path.GetFileName(Recipe)[..^TweakFiles.RecipeSuffix.Length];

    /// <summary>The folder the files are in.</summary>
    public string Folder => Path.GetDirectoryName(Recipe)!;
}

/// <summary>
/// The window's scene as the CLI's own files (vista-plan §10 M1.16: "recipe, camera and slider files save and load, so the window and
/// the CLI share them"), and its runs as the CLI's own arguments. Before each run the window writes the scene's files and parses the
/// arguments of that run with <see cref="PreviewCommandLine"/>, so a window run is the CLI run of its <see cref="CommandLine"/>: a
/// converged preview in the window is the CLI's preview of the files it saves, byte for byte.
/// </summary>
public static class TweakFiles
{
    /// <summary>The recipe file's suffix.</summary>
    public const string RecipeSuffix = ".recipe.json";

    /// <summary>The camera file's suffix.</summary>
    public const string CameraSuffix = ".camera.json";

    /// <summary>The slider file's suffix.</summary>
    public const string SlidersSuffix = ".sliders.json";

    /// <summary>The options file's suffix.</summary>
    public const string OptionsSuffix = ".options.txt";

    /// <summary>The device the window denoises on: the GPU's, 16–21× faster than the CPU's at the same error (vista-plan §10 M1.6).</summary>
    public const string DenoiseDevice = "cuda";

    /// <summary>
    /// Writes <paramref name="scene"/>'s files to <paramref name="paths"/>: the recipe, the slider file, the options file and, once the
    /// scene has one, the camera (a new scene's camera file is removed: the run chooses one). A file whose content is the same is not
    /// written again, so a watch on the files sees only real changes. Returns whether any file changed.
    /// </summary>
    public static bool Write(TweakScene scene, TweakPaths paths)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(paths);
        Directory.CreateDirectory(paths.Folder);
        bool changed = WriteIfChanged(paths.Recipe, VistaJson.Serialize(scene.Recipe));
        changed |= WriteIfChanged(paths.Sliders, VistaJson.Serialize(scene.Pins));
        changed |= WriteIfChanged(paths.Options, OptionsText(scene));
        if (scene.Camera is { } camera)
        {
            changed |= WriteIfChanged(paths.Camera, VistaJson.Serialize(camera));
        }
        else if (File.Exists(paths.Camera))
        {
            File.Delete(paths.Camera);
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// The scene of <paramref name="recipePath"/> and the files beside it (<see cref="TweakPaths.Beside"/>): the camera, the slider file
    /// and the options file when they are there (a new scene, no pins, seed 42 and the default sizes when not). An options file's other
    /// options are not the window's to keep: each is listed in <paramref name="ignored"/>.
    /// </summary>
    public static TweakScene Read(string recipePath, out IReadOnlyList<string> ignored)
    {
        ArgumentNullException.ThrowIfNull(recipePath);
        TweakPaths paths = TweakPaths.Beside(recipePath);
        Recipe recipe = Parse(recipePath, VistaJson.ParseRecipe);
        CameraSpec? camera = File.Exists(paths.Camera) ? Parse(paths.Camera, VistaJson.ParseCamera) : null;
        SliderPins? pins = File.Exists(paths.Sliders) ? Parse(paths.Sliders, VistaJson.ParseSliders) : null;
        TweakScene scene = TweakScene.From(recipe, DefaultSeed, camera, pins);
        ignored = [];
        return File.Exists(paths.Options) ? ReadOptions(scene, File.ReadAllText(paths.Options), paths.Options, out ignored) : scene;
    }

    /// <summary>The seed of a scene whose files give none.</summary>
    public const ulong DefaultSeed = 42;

    /// <summary>
    /// The options file of <paramref name="scene"/>: its seed and the preview's size and samples, as <c>--watch --options</c> reads them,
    /// with the final's as a comment.
    /// </summary>
    public static string OptionsText(TweakScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return string.Create(CultureInfo.InvariantCulture,
            $"# Vista scene options (vista-plan §10 M1.16): the seed and the preview, as Vista.App --watch --options reads them.\n" +
            $"--seed {scene.Seed} --width {scene.Preview.Width} --spp {scene.Preview.SamplesPerPixel}\n" +
            $"# The window's final: --width {scene.Final.Width} --spp {scene.Final.SamplesPerPixel}{(scene.Final.Denoise ? " --denoise" : "")}\n");
    }

    /// <summary>
    /// <paramref name="scene"/> with the seed and the preview's size and samples of the options file <paramref name="text"/> (read from
    /// <paramref name="path"/>); its other options are listed in <paramref name="ignored"/>.
    /// </summary>
    public static TweakScene ReadOptions(TweakScene scene, string text, string path, out IReadOnlyList<string> ignored)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(text);
        var problems = new List<string>();
        IReadOnlyList<string> tokens = WatchCommandLine.Tokenize(text, problems);
        (Dictionary<string, string> values, IReadOnlySet<string> flags) = PreviewCommandLine.SplitOptions(tokens, 0, PreviewCommandLine.ValueOptions, problems);
        if (problems.Count > 0)
            throw new InvalidDataException($"{path}: {string.Join(" ", problems)}");
        ulong seed = scene.Seed;
        int width = scene.Preview.Width, spp = scene.Preview.SamplesPerPixel;
        if (values.Remove("--seed", out string? s) && !ulong.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out seed))
            throw new InvalidDataException($"{path}: --seed must be a non-negative integer, got '{s}'.");
        if (values.Remove("--width", out string? w) && !(int.TryParse(w, NumberStyles.None, CultureInfo.InvariantCulture, out width) && width is >= 16 and <= PreviewCommandLine.MaxDimension))
            throw new InvalidDataException($"{path}: --width must be a whole number of pixels, got '{w}'.");
        if (values.Remove("--spp", out string? n) && !(int.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out spp) && spp >= 1))
            throw new InvalidDataException($"{path}: --spp must be a positive whole number, got '{n}'.");
        ignored = [.. values.Keys.Concat(flags).Order(StringComparer.Ordinal)];
        return scene with { Seed = seed, Preview = scene.Preview with { Width = width, SamplesPerPixel = spp } };
    }

    /// <summary>
    /// The CLI arguments of a <paramref name="run"/> of <paramref name="scene"/> from its files at <paramref name="paths"/>, writing
    /// <paramref name="outPath"/> (a <c>.png</c>): <c>--preview</c> with the recipe, the seed, the slider file and the camera (none for a
    /// new scene or the auto camera, which choose one); the preview's or the final's size and samples; the view's debug image or the
    /// denoiser; <c>--auto-camera</c>, or <c>--estimate</c> with the noise target.
    /// </summary>
    public static IReadOnlyList<string> Arguments(TweakScene scene, TweakPaths paths, TweakRun run, string outPath)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(outPath);
        if (run is not (TweakRun.Preview or TweakRun.AutoCamera or TweakRun.Estimate or TweakRun.Final))
            throw new ArgumentOutOfRangeException(nameof(run), run, "Unknown run.");
        string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
        var args = new List<string> { "--preview", "--recipe", paths.Recipe, "--seed", Number((long)scene.Seed), "--sliders", paths.Sliders };
        if (scene.Camera is not null && run != TweakRun.AutoCamera)
            args.AddRange(["--camera", paths.Camera]);
        args.AddRange(["--out", outPath]);
        if (run is TweakRun.Preview or TweakRun.AutoCamera)
        {
            args.AddRange(["--width", Number(scene.Preview.Width), "--spp", Number(scene.Preview.SamplesPerPixel)]);
            if (scene.Preview.View == TweakView.Denoised)
                args.AddRange(["--denoise", "--denoise-device", DenoiseDevice]);
            else if (DebugName(scene.Preview.View) is { } debug)
                args.AddRange(["--debug", debug]);
            if (run == TweakRun.AutoCamera)
                args.Add("--auto-camera");
        }
        else
        {
            args.AddRange(["--width", Number(scene.Final.Width), "--spp", Number(scene.Final.SamplesPerPixel)]);
            if (scene.Final.Denoise)
                args.AddRange(["--denoise", "--denoise-device", DenoiseDevice]);
            if (run == TweakRun.Estimate)
            {
                args.Add("--estimate");
                if (scene.Final.NoiseTarget is double noise)
                    args.AddRange(["--noise", noise.ToString("R", CultureInfo.InvariantCulture)]);
            }
        }
        return args;
    }

    /// <summary>The request <paramref name="args"/> (<see cref="Arguments"/>) make, parsed as the CLI parses them.</summary>
    public static PreviewRequest Request(IReadOnlyList<string> args)
    {
        if (!PreviewCommandLine.TryParse(args, out PreviewRequest? request, out IReadOnlyList<string> errors))
            throw new ArgumentException($"The window made arguments the CLI refuses: {string.Join(" ", errors)}", nameof(args));
        return request!;
    }

    /// <summary><paramref name="args"/> as one command line to paste into a shell: <c>Vista.App</c> and each argument, quoted when it holds a space.</summary>
    public static string CommandLine(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var line = new StringBuilder("Vista.App");
        foreach (string arg in args)
            line.Append(' ').Append(arg.Contains(' ', StringComparison.Ordinal) || arg.Length == 0 ? $"\"{arg}\"" : arg);
        return line.ToString();
    }

    /// <summary>The <c>--debug</c> name of <paramref name="view"/>; null for the beauty and the denoised beauty.</summary>
    public static string? DebugName(TweakView view) => view switch
    {
        TweakView.Albedo => "albedo",
        TweakView.Normal => "normal",
        TweakView.Depth => "depth",
        TweakView.Material => "material",
        TweakView.Tier => "tier",
        TweakView.Variance => "variance",
        TweakView.Detail => "detail",
        _ => null,
    };

    /// <summary>The sidecar's output role of the image <paramref name="view"/> shows (<see cref="Output.Sidecar.Outputs"/>).</summary>
    public static string OutputRole(TweakView view) => view switch
    {
        TweakView.Denoised => "denoised",
        _ => DebugName(view) ?? "beauty",
    };

    private static bool WriteIfChanged(string path, string text)
    {
        if (File.Exists(path) && File.ReadAllText(path) == text)
            return false;
        File.WriteAllText(path, text);
        return true;
    }

    private static T Parse<T>(string path, Func<string, T> parse)
    {
        try
        {
            return parse(File.ReadAllText(path));
        }
        catch (System.Text.Json.JsonException e)
        {
            throw new InvalidDataException($"{path}: {e.Message}", e);
        }
        catch (InvalidDataException e)
        {
            throw new InvalidDataException($"{path}: {e.Message}", e);
        }
    }
}
