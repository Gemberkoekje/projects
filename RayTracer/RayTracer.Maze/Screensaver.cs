using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using RayTracer;

namespace RayTracer.Gpu;

/// <summary>
/// Phase 6.2 — Windows screensaver mode. A <c>.scr</c> is just this renamed
/// executable; Windows launches it with one of the classic switches:
/// <list type="bullet">
/// <item><c>/s</c> — run full-screen (exit on any key / mouse input).</item>
/// <item><c>/p &lt;hwnd&gt;</c> — render into the little preview pane of the Screen
///   Saver settings dialog (the passed HWND is that pane; we parent a child window
///   to it and exit when it closes).</item>
/// <item><c>/c[:&lt;hwnd&gt;]</c> — show the configuration dialog.</item>
/// </list>
/// The HWND may arrive glued to the switch with a colon (<c>/c:12345</c>) or as the
/// next argument (<c>/p 12345</c>), and the switch letter is case-insensitive, so
/// <see cref="TryParse"/> normalizes all of those. All modes render the same Phase 5
/// pipeline via <see cref="Phase6Renderer"/> (fullscreen borderless + device-removed
/// recovery are exactly what a long-running screensaver needs), reading the smoke /
/// maze / motion options the user picked in the config dialog from
/// <see cref="AppSettings"/>.
/// </summary>
internal static class Screensaver
{
    internal enum Mode { Show, Preview, Config }

    private const int Width = 1280;
    private const int Height = 720;

    // ── Argument parsing ──────────────────────────────────────────────

    /// <summary>
    /// Recognizes the classic screensaver switches. Returns <c>false</c> (so the
    /// normal <c>--phaseN</c> dispatch runs) when the first argument is not one.
    /// </summary>
    public static bool TryParse(string[] args, out Mode mode, out nint hwnd)
    {
        mode = Mode.Show;
        hwnd = 0;
        if (args.Length == 0)
            return false;

        string first = args[0];
        if (first.Length < 2 || (first[0] != '/' && first[0] != '-'))
            return false;

        char letter = char.ToLowerInvariant(first[1]);
        switch (letter)
        {
            case 's': mode = Mode.Show; break;
            case 'p': mode = Mode.Preview; break;
            case 'c': mode = Mode.Config; break;
            default: return false;
        }

        // HWND may be attached with ':' (or '=') or supplied as the next token.
        int colon = first.IndexOfAny([':', '=']);
        if (colon >= 0 && colon + 1 < first.Length)
            hwnd = ParseHandle(first[(colon + 1)..]);
        else if (args.Length > 1)
            hwnd = ParseHandle(args[1]);

        return true;
    }

    private static nint ParseHandle(string s)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(s[2..], System.Globalization.NumberStyles.HexNumber, null, out long hex))
            return (nint)hex;
        return long.TryParse(s, out long dec) ? (nint)dec : 0;
    }

    // ── Dispatch ──────────────────────────────────────────────────────

    public static int Run(Mode mode, nint hwnd, string[] args)
    {
        Program.EnsureAppConfigured();
        AppSettings settings = AppSettings.Load();
        return mode switch
        {
            Mode.Config => RunConfig(settings),
            Mode.Preview => RunPreview(hwnd, settings),
            _ => RunShow(settings, ParseFramesForTest(args)),
        };
    }

    // Optional `--frames N` cap so `/s --frames 60` can verify the show path
    // non-interactively; 0 (the default) means run until the user provides input.
    private static int ParseFramesForTest(string[] args)
    {
        int i = Array.FindIndex(args, a => a.Equals("--frames", StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int n) ? n : 0;
    }

    // ── /s — full-screen show, exit on input ──────────────────────────

    private static int RunShow(AppSettings settings, int maxFrames)
    {
        Phase3Scene built = BuildScene(settings);
        Rectangle screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, Width, Height);

        using var form = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            Bounds = screen,
            TopMost = maxFrames <= 0, // don't steal focus during a bounded test run
            ShowInTaskbar = false,
            Text = "RayTracer.Gpu — Screensaver",
        };

        bool running = true;
        // Ignore the first spurious mouse-move (cursor position at startup); exit
        // once real input arrives — matching classic screensaver behavior.
        Point? firstMouse = null;
        void Quit() => running = false;
        form.FormClosed += (_, _) => running = false;
        form.KeyDown += (_, _) => Quit();
        form.MouseDown += (_, _) => Quit();
        form.MouseMove += (_, e) =>
        {
            if (firstMouse is null) { firstMouse = e.Location; return; }
            if (Math.Abs(e.X - firstMouse.Value.X) > 10 || Math.Abs(e.Y - firstMouse.Value.Y) > 10)
                Quit();
        };

        if (maxFrames <= 0)
            Cursor.Hide();
        try
        {
            using var renderer = BuildRenderer(built, settings, screen.Width, screen.Height);
            form.Show();
            renderer.Initialize(form.Handle);
            RenderWalk(renderer, built, settings,
                keepGoing: () => running && form.Created, maxFrames: maxFrames);
        }
        finally
        {
            if (maxFrames <= 0)
                Cursor.Show();
        }

        if (maxFrames > 0)
            Console.WriteLine($"Screensaver /s rendered {maxFrames} frame(s) at {screen.Width}x{screen.Height} without error.");
        return 0;
    }

    // ── /p <hwnd> — preview inside the settings dialog's pane ──────────

    private static int RunPreview(nint parent, AppSettings settings, int maxFrames = 0)
    {
        if (parent == 0 || !IsWindow(parent))
            return 0; // no valid preview pane — nothing to do

        Phase3Scene built = BuildScene(settings);

        GetClientRect(parent, out RECT rc);
        int w = Math.Max(16, rc.Right - rc.Left);
        int h = Math.Max(16, rc.Bottom - rc.Top);

        using var form = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            ShowInTaskbar = false,
            Bounds = new Rectangle(0, 0, w, h),
        };

        // Reparent the form as a WS_CHILD of the preview pane (x64: the *Ptr APIs).
        nint handle = form.Handle; // force creation
        nint style = GetWindowLongPtr(handle, GWL_STYLE);
        SetWindowLongPtr(handle, GWL_STYLE, (nint)(((long)style & ~WS_POPUP) | WS_CHILD));
        SetParent(handle, parent);

        using var renderer = BuildRenderer(built, settings, w, h);
        form.Show();
        renderer.Initialize(handle);

        // Run until the host closes the settings dialog (the parent HWND disappears),
        // or until a bounded frame count for the smoke test.
        RenderWalk(renderer, built, settings,
            keepGoing: () => form.Created && IsWindow(parent), maxFrames: maxFrames);
        return 0;
    }

    /// <summary>
    /// Dev-box smoke test for the <c>/p</c> preview path: stands up a host window to
    /// stand in for the Screen Saver settings dialog's pane, reparents a child render
    /// window into it (the Win32 <c>SetParent</c> / <c>WS_CHILD</c> dance the
    /// <c>/p</c> switch performs), renders a bounded number of frames into that child
    /// swap chain, then tears down. Requires a DXR 1.1 GPU + a desktop session.
    /// </summary>
    public static int PreviewSelfTest(int frames)
    {
        if (frames <= 0) frames = 30;
        Program.EnsureAppConfigured();
        Console.WriteLine($"RayTracer.Gpu — screensaver /p preview smoke test ({frames} frames)");

        using var host = new Form
        {
            Text = "preview host (stand-in for the settings dialog)",
            ClientSize = new Size(320, 240),
            StartPosition = FormStartPosition.CenterScreen,
            ShowInTaskbar = false,
        };
        host.Show();
        Application.DoEvents();

        RunPreview(host.Handle, AppSettings.Load(), maxFrames: frames);

        Console.WriteLine($"  Reparented child rendered {frames} preview frame(s) without error.");
        Console.WriteLine("  overall              : PASS");
        return 0;
    }

    // ── /c — configuration dialog ─────────────────────────────────────

    private static int RunConfig(AppSettings settings)
    {
        using var dialog = new ConfigForm(settings, gpuOnly: true);
        if (dialog.ShowDialog() == DialogResult.OK)
            dialog.Result.Save();
        return 0;
    }

    // ── Shared rendering ──────────────────────────────────────────────

    private static Phase3Scene BuildScene(AppSettings s)
    {
        int mazeSeed = Random.Shared.Next();
        MazeProps.Options? props = (s.ShowOpenGlLogo || s.ShowWallSigns)
            ? new MazeProps.Options(Logo: s.ShowOpenGlLogo, Signs: s.ShowWallSigns, Seed: mazeSeed)
            : null;
        return Phase3Scene.Build(Width, Height, mazeSeed: mazeSeed, mazeSize: s.MazeSize,
            lightSeed: new Random(mazeSeed).Next(), props: props);
    }

    private static Phase6Renderer BuildRenderer(Phase3Scene built, AppSettings s, int width, int height)
    {
        // Classic look: unlit fullbright spectral, smoke off (plan §2).
        bool classic = s.Style == RenderStyle.Classic;
        VolumetricOptions volumetrics = VolumetricOptions.FromQuality(
            s.VolumetricQuality, classic ? SmokeMode.None : s.SmokeMode);
        // Match RunPhase6Windowed: force fog occlusion on at any non-Off tier (Low/Medium omit it by preset).
        if (volumetrics.EnableVolumetrics && volumetrics.ShadowStepInterval == 0)
            volumetrics = volumetrics with { ShadowStepInterval = 4 };
        var renderer = new Phase6Renderer(
            width, height, built.Packed, built.Spectral, built.PackedLights, built.Camera,
            volumetrics, lightingMode: classic ? LightingMode.None : LightingMode.NEE,
            sampleClamp: s.SampleClamp,
            maxSampleCount: s.MaxSampleCount, subPixelJitter: s.SubPixelJitter,
            motionSampleCap: s.MotionSampleCap, temporalBlendAlpha: s.TemporalBlendAlpha,
            filterRadius: (uint)Math.Max(0, s.FilterRadius),
            biomeIndicator: false, debugMode: Phase5DebugMode.Beauty,
            bumpyWalls: s.BumpyWalls, showOverheadMap: s.ShowOverheadMap, showRat: s.ShowRat,
            classicDepthCue: classic && s.ClassicDepthCue ? ClassicMode.DepthCueStrength : 0f,
            pixelSize: classic && s.RetroPixelation ? ClassicMode.RetroBlockFor(height) : 1, decalAtlas: MazeDecals.Atlas);
        renderer.SetLens(s.ResolveLens()); // §4: /c's lens choice reaches /s and /p
        return renderer;
    }

    // The autonomous maze-walk loop shared by /s and /p (mirrors RunPhase6Windowed's
    // core), with a caller-supplied stop predicate. Recovers from a lost device so a
    // screensaver left running for hours survives a driver reset / TDR.
    private static void RenderWalk(
        Phase6Renderer renderer, Phase3Scene built, AppSettings settings,
        Func<bool> keepGoing, int maxFrames)
    {
        CameraController controller = built.CreateCameraController();
        controller.StillTime = settings.StillTime;
        controller.MoveTime = 0.9f;
        controller.TurnTime = 0.7f;
        Camera camera = settings.Style == RenderStyle.Classic
            ? ClassicMode.WithClassicFov(built.Camera) : built.Camera;
        if (settings.ShowOverheadMap)
        {
            var (g, gw, gh) = MazeMinimap.Build(built.Maze);
            renderer.SetMinimap(g, gw, gh);
        }
        CameraController? ratCtrl = null;
        Camera ratCam = default!;
        if (settings.ShowRat) ratCtrl = built.CreateRatController(out ratCam);
        var clock = Stopwatch.StartNew();
        double lastSeconds = 0;
        const double BuildInDuration = 1.5; // §9.3 wall-rise intro length
        const double OutroDuration = 0.6;   // §9.4 fade-to-black length
        double mazeStartTime = 0;
        bool outroActive = false;
        double outroStart = 0;

        uint frame = 0;

        // Generates a fresh maze in place (shared by the outro and the instant cut).
        void Regen()
        {
            built = BuildScene(settings);
            camera = settings.Style == RenderStyle.Classic
                ? ClassicMode.WithClassicFov(built.Camera) : built.Camera;
            controller = built.CreateCameraController();
            controller.StillTime = settings.StillTime;
            controller.MoveTime = 0.9f;
            controller.TurnTime = 0.7f;
            renderer.RebuildScene(built.Packed, built.Spectral, built.PackedLights, camera);
            if (settings.ShowOverheadMap)
            {
                var (g, gw, gh) = MazeMinimap.Build(built.Maze);
                renderer.SetMinimap(g, gw, gh);
            }
            if (settings.ShowRat) ratCtrl = built.CreateRatController(out ratCam);
            mazeStartTime = clock.Elapsed.TotalSeconds;
            frame = 0;
        }

        while (keepGoing())
        {
            double now = clock.Elapsed.TotalSeconds;
            float dt = (float)Math.Min(now - lastSeconds, 0.1);
            lastSeconds = now;

            double buildInT = now - mazeStartTime;
            bool building = settings.MazeBuildInAnim && buildInT < BuildInDuration;
            renderer.SetRevealHeight(building
                ? (float)(MazeGeometryBuilder.WallHeight * (buildInT / BuildInDuration)) : 1e9f);

            bool holdWalker = building || outroActive;
            controller.Dirty = false;
            if (!holdWalker) controller.Update(dt, camera);
            bool moving = controller.Dirty;

            // Maze finished (plan §9.1). With an outro (§9.4) fade to black first, then
            // regenerate; otherwise cut straight. A long-running screensaver cycles mazes.
            bool atGoal = settings.MazeRegenerate && !building && !moving && frame > 0 &&
                controller.CurrentCell == (built.Maze.Width - 1, built.Maze.Height - 1);
            if (atGoal && settings.MazeOutroAnim && !outroActive) { outroActive = true; outroStart = now; }
            if (outroActive)
            {
                double ot = now - outroStart;
                renderer.SetFadeLevel((float)Math.Min(1.0, ot / OutroDuration));
                if (ot >= OutroDuration)
                {
                    outroActive = false;
                    renderer.SetFadeLevel(0f);
                    Regen();
                    continue;
                }
            }
            else if (atGoal)
            {
                Regen();
                continue;
            }

            if (ratCtrl is not null && !holdWalker) { ratCtrl.Update(dt, ratCam); renderer.SetRatPosition(ratCam.Position); }

            renderer.SetCamera(camera);
            renderer.SetFogTime((float)(now * settings.FogDrift));
            if (!renderer.RenderFrame(reset: frame == 0 || building, moving: moving))
            {
                if (!renderer.TryRecoverDevice())
                    break;
                frame = 0;
                continue;
            }
            frame++;
            Application.DoEvents();

            if (maxFrames > 0 && frame >= (uint)maxFrames)
                break;
        }
    }

    // ── Win32 interop (preview child-window parenting) ────────────────

    private const int GWL_STYLE = -16;
    private const long WS_CHILD = 0x40000000L;
    private const long WS_POPUP = unchecked((long)0x80000000L);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(nint hWndChild, nint hWndNewParent);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);
}
