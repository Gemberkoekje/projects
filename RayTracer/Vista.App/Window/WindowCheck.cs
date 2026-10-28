using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using Vista.Output;
using Vista.Recipes;
using Vista.Scene;
using Vista.Sliders;
using Vista.Tweak;

namespace Vista.App.Window;

/// <summary>
/// <c>Vista.App --window-check</c>: the M1.16 check (vista-plan §10) on the RTX 3070. It opens the real window on M1's hero scene and drives
/// it step by step through the same methods its controls call, the way a person would walk through M1: the new scene's auto camera, the
/// progressive preview at a larger size, the time of day from dawn to dusk, the camera's nudges, turns, exposure and collision, pinned and
/// reset sliders with the °F toggle and an implausible pair, the debug views and the denoiser, the snow-capped mountains' bare steep faces
/// and their dawn, then the estimate and the final on the kept stages. After each step it checks the stages each run built and reused,
/// and saves a picture of the window. Twice it saves the scene and renders its files with a cold <c>--preview</c> process: the window's
/// converged preview must be the CLI's, byte for byte.
/// </summary>
internal static class WindowCheck
{
    /// <summary>The flag that selects this check.</summary>
    public const string Flag = "--window-check";

    /// <summary>Usage text.</summary>
    public const string Usage =
        "       Vista.App --window-check [--seed 42] [--out folder] [--final-width 3840] [--final-spp 64] [--no-final] [--visible]\n" +
        "                 (the window driven step by step: stages, frames, collision, hints, saved files bit-identical to the CLI's,\n" +
        "                 the estimate against the final; pictures of the window in <out>/shots; off the screen unless --visible)";

    private static readonly TimeSpan StepTimeout = TimeSpan.FromMinutes(15);

    public static int Run(string[] args, TextWriter log)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool final = true, visible = false;
        for (int k = 1; k < args.Length; k++)
        {
            if (args[k] == "--no-final")
                final = false;
            else if (args[k] == "--visible")
                visible = true;
            else if (args[k] is "--seed" or "--out" or "--final-width" or "--final-spp" && k + 1 < args.Length)
                values[args[k]] = args[++k];
            else
            {
                Console.Error.WriteLine($"Unknown or valueless argument '{args[k]}'.");
                Console.Error.WriteLine(Usage);
                return 1;
            }
        }
        ulong seed = ulong.Parse(values.GetValueOrDefault("--seed", "42"), CultureInfo.InvariantCulture);
        int finalWidth = int.Parse(values.GetValueOrDefault("--final-width", "3840"), CultureInfo.InvariantCulture);
        int finalSpp = int.Parse(values.GetValueOrDefault("--final-spp", "64"), CultureInfo.InvariantCulture);
        string folder = Path.GetFullPath(values.GetValueOrDefault("--out",
            Path.Combine(Path.GetTempPath(), "vista-window-check-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture))));
        Directory.CreateDirectory(folder);
        log.WriteLine($"Window check in {folder}");

        TweakScene hero = WindowApp.HeroScene().WithSeed(seed);
        hero = hero with { Final = hero.Final with { Width = finalWidth, SamplesPerPixel = finalSpp } };
        using var ready = new ManualResetEventSlim();
        TweakWindow? window = null;
        var ui = new Thread(() =>
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using TweakRunner runner = WindowApp.NewRunner();
            window = new TweakWindow(hero, runner, Path.Combine(folder, "work")) { Quiet = !visible };
            window.Shown += (_, _) => ready.Set();
            Application.Run(window);
        })
        { Name = "Vista window" };
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        if (!ready.Wait(TimeSpan.FromMinutes(1)))
        {
            log.WriteLine("FAIL: the window did not open.");
            return 1;
        }

        var check = new Checker(window!, log, folder);
        try
        {
            Walk(check, final);
            check.WriteSheets();
        }
        catch (TimeoutException e)
        {
            check.Fail(e.Message);
        }
        finally
        {
            check.Ui(() => window!.Close());
            ui.Join(TimeSpan.FromMinutes(1));
        }
        log.WriteLine();
        log.WriteLine(check.Failures == 0 ? "PASS: every step as expected." : $"FAIL: {check.Failures} problem(s), listed above.");
        return check.Failures == 0 ? 0 : 1;
    }

    /// <summary>The walk through M1 in the window.</summary>
    private static void Walk(Checker check, bool final)
    {
        TweakWindow w = check.Window;
        // A new scene: the auto camera chooses its camera, and its preview is shown.
        IReadOnlyList<TweakResult> first = check.Settle("hero: alpine range at golden hour, a new scene");
        check.Expect(first.Any(r => r.Job.Kind == TweakRun.AutoCamera && r.Succeeded), "the auto camera ran for the new scene");
        check.Expect(check.Ui(() => w.Scene.Camera) is not null, "the scene took the auto camera's pick");
        check.Shot("hero");
        check.SameAsCli("hero");

        // The progressive preview, at a size that takes a few seconds: frames before the image, the image the CLI's.
        TweakScene scene = check.Ui(() => w.Scene);
        check.Do("preview 960×540 at 256 spp", () => w.Apply(scene with { Preview = scene.Preview with { Width = 960, SamplesPerPixel = 256 } }), async_: true);
        if (check.WaitFor(() => w.FramesShown >= 2, TimeSpan.FromMinutes(2)))
            check.Shot("progressive-partial");
        IReadOnlyList<TweakResult> big = check.Settle("preview 960×540 at 256 spp");
        check.Expect(check.Ui(() => w.FramesShown) >= 2, $"the preview refined on screen ({check.Ui(() => w.FramesShown)} frames before its image)");
        check.Expect(Built(big) is { } b && !b.Contains(PreviewStage.World) && b.Contains(PreviewStage.Mesh), "a larger preview meshes again, the world kept");
        check.Shot("progressive-converged");
        check.SameAsCli("progressive");
        IReadOnlyList<TweakResult> back = Edit(check, "preview back to 640×360 at 64 spp",
            s => s with { Preview = s.Preview with { Width = TweakPreview.DefaultWidth, SamplesPerPixel = 64 } });
        check.Expect(Built(back) is { } k && !k.Contains(PreviewStage.Mesh) && k.Contains(PreviewStage.AccelerationStructure),
            "the preview's own mesh came back (two are kept), its acceleration structure built again");

        // The time of day: only the sky and the image change.
        foreach ((TimeOfDay time, string name) in new[] { (TimeOfDay.Dawn, "dawn"), (TimeOfDay.Dusk, "dusk"), (TimeOfDay.GoldenHour, "goldenHour") })
        {
            IReadOnlyList<TweakResult> runs = Edit(check, $"time of day: {name}", s => s with { Pins = s.Pins with { Time = time } });
            check.Expect(Built(runs) is { } built && built.Contains(PreviewStage.SkyView) && !built.Contains(PreviewStage.World) && !built.Contains(PreviewStage.Mesh),
                $"{name} rebuilt the sky, not the world or the mesh");
            if (time != TimeOfDay.GoldenHour)
                check.Shot(name);
        }

        // The camera: nudges, turns, the exposure, and the collision.
        IReadOnlyList<TweakResult> nudge = check.Do("camera: 1.5 m (5 ft) left", () => w.NudgeCamera(0, -1.5, 0));
        check.Expect(Built(nudge) is { } n && n.Contains(PreviewStage.Mesh) && !n.Contains(PreviewStage.World), "a nudge meshed again, the world kept");
        IReadOnlyList<TweakResult> pan = check.Do("camera: pan 5° right", () => w.TurnCamera(5, 0, 0));
        check.Expect(Built(pan) is { } p && p.Contains(PreviewStage.Render) && !p.Contains(PreviewStage.World) && !p.Contains(PreviewStage.AtmosphereTables),
            "a pan rendered again (the mesh follows the frustum since M1.9), the world and the air kept");
        check.Do("camera: tilt 1° up, roll 2° left", () => w.TurnCamera(0, 1, 2));
        check.Do("camera: 1000 km down", () => w.NudgeCamera(0, 0, -1_000_000));
        check.Expect(check.Ui(() => w.CameraNote) is { } note && note.Contains("lifted", StringComparison.Ordinal), $"the collision lifted the camera ({check.Ui(() => w.CameraNote)})");
        if (check.Ui(() => w.Scene.Camera) is { } camera)
        {
            double ground = check.GroundUnder(camera);
            check.Log($"  the camera stands {camera.Position.Y - ground:0.##} m above the grid's ground");
            check.Expect(camera.Position.Y - ground >= CameraGround.EyeMetres, "the camera stands at least eye height above the ground");
        }
        check.Shot("camera-collision");
        check.ShotControls("camera-controls", "camera");
        check.Do("camera: 300 m up again", () => w.NudgeCamera(0, 0, 300));
        IReadOnlyList<TweakResult> exposure = check.Do("camera: exposure +1 EV", () => w.Apply(w.Scene with { Camera = w.Scene.Camera! with { ExposureEv = w.Scene.Camera.ExposureEv + 1 } }));
        check.Expect(Reused(exposure) is { } e && e.Contains(PreviewStage.Render), "the exposure re-exposed the kept render");
        Edit(check, "camera: exposure back", s => s with { Camera = s.Camera! with { ExposureEv = s.Camera.ExposureEv - 1 } });

        // The sliders: a pin, the °F toggle, a reset, and a contradiction.
        IReadOnlyList<TweakResult> cold = Edit(check, "temperature pinned at −10 °C", s => s with { Pins = s.Pins with { Temperature = -10 } });
        check.Expect(check.Ui(() => w.Sliders!.Readings[Slider.Temperature].Pinned), "the temperature shows pinned");
        check.Expect(Built(cold) is { } c && c.Contains(PreviewStage.AtmosphereTables), "a pinned temperature moved the air's tables (the haze follows the humidity)");
        IReadOnlyList<TweakResult> fahrenheit = Edit(check, "temperature shown in °F", s => s with { Pins = s.Pins with { TemperatureUnit = TemperatureUnit.Fahrenheit } });
        check.Expect(check.Ui(() => w.Sliders!.Readings[Slider.Temperature].Value).Contains("°F", StringComparison.Ordinal), "the temperature reads in °F");
        check.Expect(Reused(fahrenheit) is { } f && f.Contains(PreviewStage.Render), "°F changes nothing in the image");
        check.Ui(() => w.AdvancedShown = true);
        check.Shot("temperature-pinned");
        check.ShotControls("sliders-advanced", "sliders");
        Edit(check, "temperature reset", s => s.Reset(Slider.Temperature));
        check.Expect(!check.Ui(() => w.Sliders!.Readings[Slider.Temperature].Pinned), "the temperature is auto again");
        Edit(check, "date 2026-01-15 with summer pinned", s => s with { Pins = s.Pins with { Season = Season.Summer, Date = new DateOnly(2026, 1, 15) } });
        check.Expect(check.Ui(() => w.Sliders!.ImplausibleSliders) is { } flagged && flagged.Contains(Slider.Date) && flagged.Contains(Slider.Season),
            "the date and the season are marked implausible");
        check.Shot("implausible");
        check.Ui(() => w.AdvancedShown = false);
        Edit(check, "date and season reset", s => s.Reset(Slider.Date).Reset(Slider.Season));

        // The debug views and the denoiser.
        foreach (TweakView view in new[] { TweakView.Material, TweakView.Depth, TweakView.Denoised, TweakView.Beauty })
        {
            Edit(check, $"view: {view}", s => s with { Preview = s.Preview with { View = view } });
            string role = TweakFiles.OutputRole(view);
            check.Expect(check.Ui(() => w.LastResult?.ImageRole) == role, $"the window shows the {role} image");
            if (view is TweakView.Material or TweakView.Denoised)
                check.Shot("view-" + role);
        }

        // M1's checks in the window: snow-capped mountains show bare steep faces, and their dawn its alpenglow.
        IReadOnlyList<TweakResult> snowcaps = Edit(check, "set pieces: snow-capped mountains (a new scene)",
            s => s.Without(SetPiece.AlpineRange).With(SetPiece.SnowCappedMountains));
        check.Expect(snowcaps.Any(r => r.Job.Kind == TweakRun.AutoCamera && r.Succeeded), "the new scene's camera was chosen again");
        check.Shot("snowcaps");
        Edit(check, "view: material", s => s with { Preview = s.Preview with { View = TweakView.Material } });
        check.Shot("snowcaps-material");
        Edit(check, "view: beauty, at dawn", s => s with { Preview = s.Preview with { View = TweakView.Beauty }, Pins = s.Pins with { Time = TimeOfDay.Dawn } });
        check.Shot("snowcaps-dawn");
        // M1.13's sweep put the alpenglow near 0°: the summits catch the sun while the valleys lie in the twilight. This camera looks
        // north-east, into the rising sun, so it is the setting sun, behind it, that lights the faces it sees.
        Edit(check, "the setting sun at 0°", s => s with { Pins = s.Pins with { Time = TimeOfDay.Dusk, SunElevation = 0 } });
        check.Shot("snowcaps-sunset");
        Edit(check, "golden hour again", s => s with { Pins = s.Pins with { Time = TimeOfDay.GoldenHour, SunElevation = null } });
        check.SameAsCli("snowcaps");

        // The files round trip: saved, opened again, the same scene.
        TweakScene before = check.Ui(() => w.Scene);
        string saved = Path.Combine(check.Folder, "saved", "roundtrip" + TweakFiles.RecipeSuffix);
        check.Ui(() => w.SaveScene(saved));
        IReadOnlyList<TweakResult> reopened = check.Do("open the saved scene", () => w.LoadScene(saved));
        TweakScene after = check.Ui(() => w.Scene);
        check.Expect(after.Recipe.ContentEquals(before.Recipe) && after.Seed == before.Seed && after.Camera == before.Camera && after.Pins == before.Pins,
            "the saved scene opens as it was");
        check.Expect(reopened.All(r => r.Sidecar is null || r.Sidecar.Stages.Reused.Contains(PreviewStage.Render)), "opening it renders nothing new");

        if (!final)
            return;
        // The estimate and the final, on the session's kept stages.
        string finalPath = Path.Combine(check.Folder, "final", "hero-final.png");
        IReadOnlyList<TweakResult> estimated = check.Do("estimate the final", () => w.Estimate(finalPath));
        TweakResult? estimate = estimated.LastOrDefault(r => r.Job.Kind == TweakRun.Estimate && r.Succeeded);
        check.Expect(estimate?.Sidecar?.Estimate?.Total is not null, "the estimate gave a range");
        check.Shot("estimate");
        IReadOnlyList<TweakResult> rendered = check.Do("render the final", () => w.RenderFinal(finalPath));
        TweakResult? finalRun = rendered.LastOrDefault(r => r.Job.Kind == TweakRun.Final && r.Succeeded);
        check.Expect(finalRun is not null, "the final rendered");
        check.Shot("final");
        check.ShotControls("final-controls", "final");
        if (finalRun?.Sidecar is { } finalSidecar && estimate?.Sidecar?.Estimate is { Total: { } total } info)
        {
            IReadOnlyList<PreviewStage> kept = finalSidecar.Stages.Reused;
            check.Log(string.Create(CultureInfo.InvariantCulture,
                $"  estimate {total}: setup {info.Setup}, trace {info.Trace}, files {info.Write}; the final ran {finalRun.Seconds:0.00} s " +
                $"(trace {finalSidecar.TimingsSeconds.GetValueOrDefault("render"):0.00} s)"));
            check.Expect(kept.Contains(PreviewStage.Mesh) && kept.Contains(PreviewStage.AccelerationStructure) && kept.Contains(PreviewStage.AtmosphereTables),
                $"the final kept the estimate's mesh, acceleration structure and tables (reused: {string.Join(", ", kept)})");
            check.Expect(finalRun.Seconds >= total.Low && finalRun.Seconds <= total.High, $"the final's {finalRun.Seconds:0.00} s lies inside the estimate's range");
            check.Expect(File.Exists(finalPath) && File.Exists(Path.ChangeExtension(finalPath, ".exr")) && File.Exists(Path.ChangeExtension(finalPath, ".json")),
                "the final wrote its PNG, EXR and sidecar");
            check.Expect(File.Exists(Path.Combine(Path.GetDirectoryName(finalPath)!, "hero-final" + TweakFiles.RecipeSuffix)), "and its scene's files beside them");
        }
    }

    /// <summary>Applies <paramref name="change"/> to the window's scene and waits for its runs.</summary>
    private static IReadOnlyList<TweakResult> Edit(Checker check, string name, Func<TweakScene, TweakScene> change) =>
        check.Do(name, () => check.Window.Apply(change(check.Window.Scene)));

    private static IReadOnlyList<PreviewStage>? Built(IReadOnlyList<TweakResult> runs) => runs.LastOrDefault(r => r.Sidecar is not null)?.Sidecar!.Stages.Built;

    private static IReadOnlyList<PreviewStage>? Reused(IReadOnlyList<TweakResult> runs) => runs.LastOrDefault(r => r.Sidecar is not null)?.Sidecar!.Stages.Reused;

    /// <summary>Drives the window from the check's thread: every call runs on the window's own.</summary>
    private sealed class Checker
    {
        private readonly TextWriter _log;
        private readonly ConcurrentQueue<TweakResult> _results = new();
        private int _shots;

        public Checker(TweakWindow window, TextWriter log, string folder)
        {
            Window = window;
            _log = log;
            Folder = folder;
            Ui(() => window.Handled += _results.Enqueue);
        }

        public TweakWindow Window { get; }

        public string Folder { get; }

        public int Failures { get; private set; }

        public T Ui<T>(Func<T> read) => (T)Window.Invoke(read);

        public void Ui(Action act) => Window.Invoke(act);

        public void Log(string line) => _log.WriteLine(line);

        public void Fail(string what)
        {
            Failures++;
            _log.WriteLine($"  FAIL: {what}");
        }

        public void Expect(bool ok, string what)
        {
            if (ok)
                _log.WriteLine($"  ok: {what}");
            else
                Fail(what);
        }

        /// <summary>Runs <paramref name="act"/> on the window and waits for every run it starts; <paramref name="async_"/> returns at once instead.</summary>
        public IReadOnlyList<TweakResult> Do(string name, Action act, bool async_ = false)
        {
            _results.Clear();
            Ui(act);
            return async_ ? [] : Settle(name);
        }

        /// <summary>Waits until the window has nothing running, waiting or about to be asked for; logs and returns the runs it handled.</summary>
        public IReadOnlyList<TweakResult> Settle(string name)
        {
            var clock = Stopwatch.StartNew();
            while (true)
            {
                if (clock.Elapsed > StepTimeout)
                    throw new TimeoutException($"'{name}' did not settle in {StepTimeout.TotalMinutes} minutes.");
                Window.Runner.WaitIdle(TimeSpan.FromSeconds(1));
                if (Ui(() => Window.Settled) && !Window.Runner.Busy && Ui(() => Window.Settled))
                    break;
                Thread.Sleep(25); // a change the window holds back until its controls are still (TweakWindow.Apply)
            }
            var runs = new List<TweakResult>();
            while (_results.TryDequeue(out TweakResult? r))
                runs.Add(r);
            _log.WriteLine($"{name}: {clock.Elapsed.TotalSeconds:0.00} s");
            foreach (TweakResult r in runs)
            {
                string what = r.Cancelled ? "stopped" : r.Problem is { } p ? "failed: " + p
                    : string.Create(CultureInfo.InvariantCulture,
                        $"{r.Seconds:0.00} s, built {string.Join(", ", r.Sidecar!.Stages.Built)}; reused {string.Join(", ", r.Sidecar.Stages.Reused)}");
                _log.WriteLine($"  #{r.Job.Id} {r.Job.Kind}: {what}");
                if (r.Sidecar is { } s)
                    _log.WriteLine("    " + Slowest(s));
                if (r.Problem is not null)
                    Fail($"#{r.Job.Id} {r.Job.Kind} failed");
            }
            return runs;
        }

        /// <summary>Where a run's wall time went: the session's stages and the renderer's, those of a quarter second or more.</summary>
        private static string Slowest(Sidecar sidecar)
        {
            IEnumerable<KeyValuePair<string, double>> parts = sidecar.TimingsSeconds
                .Concat(sidecar.Render?.Stats.StageSeconds.Select(s => KeyValuePair.Create("render." + s.Key, s.Value)) ?? []);
            return string.Join(", ", parts.Where(p => p.Value >= 0.25).OrderByDescending(p => p.Value)
                .Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Key} {p.Value:0.00} s")));
        }

        /// <summary>Waits until <paramref name="condition"/> holds on the window's thread, or <paramref name="timeout"/> passes.</summary>
        public bool WaitFor(Func<bool> condition, TimeSpan timeout)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < timeout)
            {
                if (Ui(condition))
                    return true;
                Thread.Sleep(50);
            }
            return false;
        }

        /// <summary>Saves a picture of the window as <c>shots/NN-name.png</c>.</summary>
        public void Shot(string name)
        {
            string folder = Path.Combine(Folder, "shots");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"{++_shots:00}-{name}.png");
            Ui(() =>
            {
                using Bitmap bitmap = Window.Snapshot();
                bitmap.Save(path, ImageFormat.Png);
            });
            _log.WriteLine($"  picture: {path}");
        }

        /// <summary>Saves a picture of the column of controls, scrolled to <paramref name="group"/>, as <c>shots/NN-name.png</c>.</summary>
        public void ShotControls(string name, string group)
        {
            string path = Path.Combine(Folder, "shots", $"{++_shots:00}-{name}.png");
            Ui(() =>
            {
                using Bitmap bitmap = Window.SnapshotControls(group);
                bitmap.Save(path, ImageFormat.Png);
            });
            _log.WriteLine($"  picture: {path}");
        }

        /// <summary>
        /// The slice's sheets from the pictures (vista-plan §10, the size rule): <c>m1-16-window.png</c>, four pictures of the window at
        /// 640 px a cell; <c>m1-16-controls.png</c>, the column of controls at its own size (under 640 px wide).
        /// </summary>
        public void WriteSheets()
        {
            string shots = Path.Combine(Folder, "shots");
            string Find(string name) => Directory.GetFiles(shots, $"*-{name}.png").Single();
            Sheet(Path.Combine(Folder, "m1-16-window.png"), "Vista M1.16: the preview and tweak window (seed 42, Zermatt, 15 July)",
            [
                ("a new scene: the auto camera's pick, golden hour", Find("hero")),
                ("the preview refining, pass by pass (960×540, 256 spp)", Find("progressive-partial")),
                ("pinned sliders and an implausible pair", Find("implausible")),
                ("snow-capped mountains, the setting sun at 0°", Find("snowcaps-sunset")),
            ], 2, 640);
            Sheet(Path.Combine(Folder, "m1-16-controls.png"), "Vista M1.16: the window's controls",
            [
                ("sliders: auto, pinned, reset; the advanced ones", Find("sliders-advanced")),
                ("camera: nudges, turns, lens; the collision's note", Find("camera-controls")),
                ("final: the estimate, then the final on the kept stages", Find("final-controls")),
            ], 3, 640);
            _log.WriteLine($"sheets: {Path.Combine(Folder, "m1-16-window.png")}, {Path.Combine(Folder, "m1-16-controls.png")}");
        }

        /// <summary>Tiles <paramref name="cells"/> under a title, each reduced to at most <paramref name="cellWidth"/> px wide, labelled above.</summary>
        private static void Sheet(string path, string title, IReadOnlyList<(string Label, string File)> cells, int columns, int cellWidth)
        {
            const int Gap = 8, LabelHeight = 22, TitleHeight = 30;
            var images = cells.Select(c => new Bitmap(c.File)).ToList();
            try
            {
                Size Fit(Bitmap b) => b.Width <= cellWidth ? b.Size : new Size(cellWidth, (int)Math.Round(b.Height * (double)cellWidth / b.Width));
                int w = images.Max(b => Fit(b).Width), h = images.Max(b => Fit(b).Height);
                int rows = (cells.Count + columns - 1) / columns;
                using var sheet = new Bitmap(Gap + columns * (w + Gap), TitleHeight + rows * (LabelHeight + h + Gap));
                using Graphics g = Graphics.FromImage(sheet);
                g.Clear(Color.FromArgb(28, 28, 28));
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                using var titleFont = new Font("Segoe UI", 12f, FontStyle.Bold, GraphicsUnit.Pixel);
                using var font = new Font("Segoe UI", 12f, GraphicsUnit.Pixel);
                g.DrawString(title, titleFont, Brushes.Gainsboro, Gap, 8);
                for (int k = 0; k < images.Count; k++)
                {
                    int x = Gap + k % columns * (w + Gap), y = TitleHeight + k / columns * (LabelHeight + h + Gap);
                    g.DrawString(cells[k].Label, font, Brushes.Gainsboro, x, y + 3);
                    Size size = Fit(images[k]);
                    g.DrawImage(images[k], new Rectangle(x, y + LabelHeight, size.Width, size.Height));
                }
                sheet.Save(path, ImageFormat.Png);
            }
            finally
            {
                foreach (Bitmap b in images)
                    b.Dispose();
            }
        }

        /// <summary>The height of the world's grid under <paramref name="camera"/>, from the last run's world.</summary>
        public double GroundUnder(CameraSpec camera) =>
            Ui(() => Window.LastResult?.World?.Surface.HeightAt(camera.Position.X, camera.Position.Z) ?? double.NaN);

        /// <summary>
        /// Saves the window's scene as <c>saved/name.*</c>, renders those files with a cold <c>--preview</c> process at the window's preview
        /// size, and compares the window's converged preview with it, byte for byte (PNG, EXR, and the image the window shows).
        /// </summary>
        public void SameAsCli(string name)
        {
            TweakScene scene = Ui(() => Window.Scene);
            TweakPaths paths = Ui(() => Window.SaveScene(Path.Combine(Folder, "saved", name + TweakFiles.RecipeSuffix)));
            string cliOut = Path.Combine(Folder, "cli", name + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(cliOut)!);
            IReadOnlyList<string> args = TweakFiles.Arguments(scene, paths, TweakRun.Preview, cliOut);
            _log.WriteLine($"  cold CLI: {TweakFiles.CommandLine(args)}");
            var clock = Stopwatch.StartNew();
            int exit = RunCli(args, out string output);
            if (exit != 0)
            {
                Fail($"the CLI preview of the saved files failed ({exit}): {output}");
                return;
            }
            _log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  cold CLI took {clock.Elapsed.TotalSeconds:0.0} s"));
            string work = Path.Combine(Ui(() => Window.WorkFolder), "preview");
            string role = TweakFiles.OutputRole(scene.Preview.View);
            foreach (string suffix in new[] { ".png", ".exr" }.Concat(role == "beauty" ? [] : [$".{role}.png"]))
            {
                byte[] a = File.ReadAllBytes(work + suffix), b = File.ReadAllBytes(Path.ChangeExtension(cliOut, null) + suffix);
                Expect(a.AsSpan().SequenceEqual(b), $"the window's preview{suffix} is the CLI's, byte for byte ({a.Length:N0} bytes)");
            }
        }

        private static int RunCli(IReadOnlyList<string> args, out string output)
        {
            string host = Environment.ProcessPath ?? throw new InvalidOperationException("The running executable is unknown.");
            var start = new ProcessStartInfo(host) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            if (string.Equals(Path.GetFileNameWithoutExtension(host), "dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(typeof(WindowCheck).Assembly.Location);
            foreach (string arg in args)
                start.ArgumentList.Add(arg);
            using Process process = Process.Start(start)!;
            Task<string> error = process.StandardError.ReadToEndAsync();
            string stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            output = stdout + error.Result;
            return process.ExitCode;
        }
    }
}
