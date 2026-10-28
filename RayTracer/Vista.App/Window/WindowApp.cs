using System.Globalization;
using Vista.App.Denoise;
using Vista.App.Gpu;
using Vista.Cli;
using Vista.Recipes;
using Vista.Sliders;
using Vista.Tweak;
using Vista.World;

namespace Vista.App.Window;

/// <summary>
/// <c>Vista.App --window</c>: the preview and tweak window (vista-plan §3.1, §10 M1.16) on its own STA thread, its runner's session on the
/// RTX 3070 with the final's stages kept. It opens a recipe and the files beside it, or M1's hero scene: an alpine range at golden hour.
/// </summary>
internal static class WindowApp
{
    /// <summary>The flag that selects the window.</summary>
    public const string Flag = "--window";

    /// <summary>Usage text.</summary>
    public const string Usage =
        "       Vista.App --window [--recipe <name.recipe.json>] [--seed <n>] [--work <folder>]\n" +
        "                 (the preview and tweak window: set pieces, seed, sliders, camera, debug views, estimate and final; it opens\n" +
        "                 the recipe with the camera, slider and options files beside it, or an alpine range at golden hour)";

    public static int Run(string[] args)
    {
        if (!TryParse(args, out Dictionary<string, string> values, out string problem))
        {
            Console.Error.WriteLine(problem);
            Console.Error.WriteLine(Usage);
            return 1;
        }
        TweakScene scene;
        TweakPaths? saved = null;
        if (values.TryGetValue("--recipe", out string? recipe))
        {
            scene = TweakFiles.Read(recipe, out IReadOnlyList<string> ignored);
            saved = TweakPaths.Beside(recipe);
            foreach (string option in ignored)
                Console.Error.WriteLine($"warning: the window keeps only --seed, --width and --spp of the options file; {option} is not kept.");
        }
        else
        {
            scene = HeroScene();
        }
        if (values.TryGetValue("--seed", out string? seedText))
        {
            if (!ulong.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed))
            {
                Console.Error.WriteLine($"--seed must be a non-negative integer, got '{seedText}'.");
                return 1;
            }
            scene = scene.WithSeed(seed);
        }
        bool temporary = !values.TryGetValue("--work", out string? work);
        work ??= Path.Combine(Path.GetTempPath(), "vista-window-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

        RunOnStaThread(() =>
        {
            using TweakRunner runner = NewRunner();
            Application.Run(new TweakWindow(scene, runner, work, saved));
        });
        if (temporary)
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
                // A file still open: the temporary folder stays, harmless.
            }
        }
        return 0;
    }

    /// <summary>M1's hero scene (vista-plan §10 M1): an alpine range at golden hour, seed 42, its camera the auto camera's.</summary>
    public static TweakScene HeroScene() => new()
    {
        Recipe = new Recipe { Name = "alpine range, golden hour", SetPieces = [SetPiece.AlpineRange] },
        Seed = TweakFiles.DefaultSeed,
        Pins = new SliderPins { Time = TimeOfDay.GoldenHour },
    };

    /// <summary>A runner on the RTX 3070: the D3D12 renderer, eroder and OIDN denoiser, in a session that keeps the final's stages.</summary>
    public static TweakRunner NewRunner() => new(progress =>
    {
        var renderer = new D3D12VistaRenderer();
        var denoiser = new OidnDenoiser();
        var session = new PreviewSession(renderer, denoiser, eroder: renderer, erosionCache: ErosionCache.Default)
        {
            KeepsFinalStages = true,
            Progress = progress,
        };
        return new TweakEngine(session, [denoiser, renderer]);
    })
    {
        Adjust = request => request with { LowAir = LowAirOverrides.Apply(request.LowAir) },
    };

    /// <summary>Runs <paramref name="run"/> on a new STA thread with WinForms set up (file dialogs and the clipboard need STA), and waits for it.</summary>
    public static void RunOnStaThread(Action run)
    {
        var thread = new Thread(() =>
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            run();
        })
        { Name = "Vista window" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    private static bool TryParse(string[] args, out Dictionary<string, string> values, out string problem)
    {
        values = new Dictionary<string, string>(StringComparer.Ordinal);
        problem = string.Empty;
        for (int k = 1; k < args.Length; k++)
        {
            if (args[k] is not ("--recipe" or "--seed" or "--work") || k + 1 >= args.Length || !values.TryAdd(args[k], args[k + 1]))
            {
                problem = $"Unknown, repeated or valueless argument '{args[k]}'.";
                return false;
            }
            k++;
        }
        return true;
    }
}
