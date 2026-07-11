using System.Diagnostics;
using System.Numerics;
using System.Windows.Forms;
using RayTracer;

namespace RayTracer.Gpu;

internal static class Program
{
    private const int Width = 1280;
    private const int Height = 720;

    // WinForms one-time init. ApplicationConfiguration.Initialize() must run exactly
    // once and before the first window (SetCompatibleTextRenderingDefault throws
    // otherwise), so the config-then-run flow — which opens a dialog and then a
    // render window — routes every entry point through this guard.
    private static bool _appConfigured;

    internal static void EnsureAppConfigured()
    {
        if (_appConfigured) return;
        _appConfigured = true;
        ApplicationConfiguration.Initialize();
    }

    // Default per-sample firefly clamp for the Phase 3 windowed demo. The GPU
    // dropped the diffuse irradiance cache (plan §2.4), so the indirect estimator
    // (uniform light selection × nLights) is high-variance and sprays colored
    // fireflies the cache would otherwise pre-average; TAA holds each on screen
    // ~1/alpha frames. Clamping the per-sample total tames them, matching the CPU
    // renderer's cache-less presets. Override with `--clamp <value>` (0 disables).
    private const float Phase3FireflyClamp = 3.0f;

    [STAThread]
    private static int Main(string[] args)
    {
        bool selfTest = args.Contains("--selftest", StringComparer.OrdinalIgnoreCase);
        bool phase1SelfTest = args.Contains("--phase1-selftest", StringComparer.OrdinalIgnoreCase);
        bool phase2SelfTest = args.Contains("--phase2-selftest", StringComparer.OrdinalIgnoreCase);
        bool phase3SelfTest = args.Contains("--phase3-selftest", StringComparer.OrdinalIgnoreCase);
        bool phase4SelfTest = args.Contains("--phase4-selftest", StringComparer.OrdinalIgnoreCase);
        bool phase4 = args.Contains("--phase4", StringComparer.OrdinalIgnoreCase);
        bool phase5SelfTest = args.Contains("--phase5-selftest", StringComparer.OrdinalIgnoreCase);
        bool phase5 = args.Contains("--phase5", StringComparer.OrdinalIgnoreCase);
        bool phase6SelfTest = args.Contains("--phase6-selftest", StringComparer.OrdinalIgnoreCase);
        bool phase6 = args.Contains("--phase6", StringComparer.OrdinalIgnoreCase);
        bool mirrorDemo = args.Contains("--mirror-demo", StringComparer.OrdinalIgnoreCase);
        bool glassDemo = args.Contains("--glass-demo", StringComparer.OrdinalIgnoreCase);
        bool screensaverSelfTest = args.Contains("--screensaver-selftest", StringComparer.OrdinalIgnoreCase);
        bool screensaverPreviewSelfTest = args.Contains("--screensaver-preview-selftest", StringComparer.OrdinalIgnoreCase);
        bool phase6Regress = args.Contains("--phase6-regress", StringComparer.OrdinalIgnoreCase);
        bool regenSelfTest = args.Contains("--regen-selftest", StringComparer.OrdinalIgnoreCase);
        bool setupSelfTest = args.Contains("--setup-selftest", StringComparer.OrdinalIgnoreCase);
        int maxFrames = ParseIntOption(args, "--frames", defaultValue: 0);
        float sampleClamp = ParseFloatOption(args, "--clamp", defaultValue: Phase3FireflyClamp);
        string? savePath = ParseStringOption(args, "--save", defaultValue: null);
        int mazeSeed = ResolveMazeSeed(args);
        // Ceiling biome-category overlay is on by default in the demo; turn it off
        // with --no-indicator.
        bool biomeIndicator = !args.Contains("--no-indicator", StringComparer.OrdinalIgnoreCase);
        bool headless = selfTest || phase1SelfTest || phase2SelfTest || phase3SelfTest
            || phase4SelfTest || phase5SelfTest || phase6SelfTest || screensaverSelfTest
            || screensaverPreviewSelfTest || phase6Regress || setupSelfTest || regenSelfTest
            || mirrorDemo || glassDemo
            || ((phase4 || phase5 || phase6) && savePath is not null);

        try
        {
            // Windows screensaver switches (/s, /p <hwnd>, /c) — a .scr is this exe
            // renamed. Handle them before the --phaseN dispatch (Phase 6.2).
            if (Screensaver.TryParse(args, out Screensaver.Mode ssMode, out nint ssHwnd))
                return Screensaver.Run(ssMode, ssHwnd, args);
            if (screensaverSelfTest) return RunScreensaverSelfTest();
            if (screensaverPreviewSelfTest) return Screensaver.PreviewSelfTest(maxFrames);
            if (setupSelfTest) return RunSetupSelfTest();
            if (phase6Regress) return RegressionHarness.Run(
                update: args.Contains("--update", StringComparer.OrdinalIgnoreCase),
                dirOverride: ParseStringOption(args, "--dir", null));
            if (phase6SelfTest) return RunPhase6SelfTest();
            if (mirrorDemo)
                return RunMirrorDemo(maxFrames, savePath ?? "mirror-demo.png", sampleClamp, mazeSeed, ParseSmokeMode(args),
                    (uint)ParseIntOption(args, "--motion-cap", 12), args.Contains("--pan", StringComparer.OrdinalIgnoreCase));
            if (glassDemo)
                return RunGlassDemo(maxFrames, savePath ?? "glass-demo.png", sampleClamp, mazeSeed);
            if (regenSelfTest) return RunRegenSelfTest();
            if (phase6 && savePath is not null)
                return RunPhase6Capture(ParseSmokeMode(args), maxFrames, savePath, sampleClamp, mazeSeed,
                    Phase5Reference.ParseMode(ParseStringOption(args, "--debug", null)),
                    ParseFloatOption(args, "--walk", 0f),
                    args.Contains("--classic", StringComparer.OrdinalIgnoreCase) ? RenderStyle.Classic : RenderStyle.Enhanced,
                    props: args.Contains("--props", StringComparer.OrdinalIgnoreCase) ? new MazeProps.Options(Seed: mazeSeed) : null,
                    bumpyWalls: args.Contains("--bumpy", StringComparer.OrdinalIgnoreCase),
                    showOverheadMap: args.Contains("--map", StringComparer.OrdinalIgnoreCase),
                    showRat: args.Contains("--rat", StringComparer.OrdinalIgnoreCase),
                    reveal: ParseFloatOption(args, "--reveal", -1f),
                    depthCue: ParseFloatOption(args, "--depthcue", 0f),
                    pixelate: ParseIntOption(args, "--pixelate", 1));
            if (phase5SelfTest) return RunPhase5SelfTest();
            if (phase5 && savePath is not null)
                return RunPhase5Capture(ParseSmokeMode(args), maxFrames, savePath, sampleClamp, mazeSeed,
                    biomeIndicator, Phase5Reference.ParseMode(ParseStringOption(args, "--debug", null)),
                    ParseFloatOption(args, "--walk", 0f),
                    ParseFloatOption(args, "--fog-time", 0f));
            if (phase4SelfTest) return RunPhase4SelfTest();
            if (phase4 && savePath is not null)
                return RunPhase4Capture(ParseSmokeMode(args), maxFrames, savePath, sampleClamp, mazeSeed,
                    biomeIndicator,
                    ParseFloatOption(args, "--walk", 0f),
                    ParseFloatOption(args, "--inscatter", -1f),
                    ParseFloatOption(args, "--sigma", -1f),
                    ParseFloatOption(args, "--fog-time", 0f));
            if (phase3SelfTest) return RunPhase3SelfTest();
            if (phase2SelfTest) return RunPhase2SelfTest();
            if (phase1SelfTest) return RunPhase1SelfTest();
            if (selfTest) return RunSelfTest();
            // Default launch: the productized app — the unified GPU/CPU config
            // screen, then the chosen renderer.
            return RunConfigApp();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FATAL: " + ex.Message);
            Console.Error.WriteLine(ex);
            if (!headless)
                MessageBox.Show(ex.ToString(), "RayTracer.Gpu — startup failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    // ── Phase 1: fullbright spectral maze render ──────────────────────

    /// <summary>
    /// Headless Phase 1 validation: render one deterministic (jitter-off) frame,
    /// then cross-check a grid of surface pixels against the CPU reference
    /// (<see cref="Phase1Reference"/> over the same BVH hit). Requires a DXR 1.1 GPU.
    /// </summary>
    private static int RunPhase1SelfTest()
    {
        Console.WriteLine("RayTracer.Gpu — Phase 1 headless self-test");
        Phase1Scene built = Phase1Scene.Build(Width, Height);

        using var renderer = new Phase1Renderer(
            Width, Height, built.Packed, built.Spectral, built.Camera,
            maxSampleCount: 1, subPixelJitter: false);
        renderer.Initialize(windowHandle: 0);
        Console.WriteLine($"Adapter: {renderer.AdapterName}");

        byte[] image = renderer.RenderHeadless(frames: 1);

        long nonBlack = 0;
        for (int i = 0; i < Width * Height; i++)
        {
            int o = i * 4;
            if (image[o] > 4 || image[o + 1] > 4 || image[o + 2] > 4)
                nonBlack++;
        }
        double coverage = (double)nonBlack / (Width * Height);

        var bvh = new BVH(built.Tracables);
        var quads = built.Tracables.OfType<IQuadPrimitive>().ToList();
        float tanHalfFov = MathF.Tan(built.Camera.Fov * 0.5f);
        float aspectTanHalfFov = built.Camera.Aspect * tanHalfFov;
        var rot = new Vector4(
            built.Camera.Rotation.X, built.Camera.Rotation.Y,
            built.Camera.Rotation.Z, built.Camera.Rotation.W);
        int wl0 = built.Spectral.DeterWavelengths[0];

        int checkHits = 0, within = 0;
        const float tol = 6f / 255f;
        for (int y = 8; y < Height; y += 37)
        {
            for (int x = 8; x < Width; x += 37)
            {
                Vector3 dir = Phase1Reference.PrimaryRayDirection(
                    x, y, 0u, rot, built.Camera.ImgPlaneZ,
                    tanHalfFov, aspectTanHalfFov, 1f / Width, 1f / Height, subPixelJitter: false);

                var ray = new Ray { Origin = built.Camera.Position, Direction = dir, Wavelength = wl0, Intensity = 1f };
                var hit = bvh.FindClosest(ray);
                if (!hit.hit || hit.hitPrimitive is not IQuadPrimitive qp)
                    continue; // background — covered by the coverage check

                int idx = quads.IndexOf(qp);
                if (idx < 0)
                    continue;

                Vector3 corrected = Phase1Reference.ShadeHit(
                    built.Packed.Primitives[idx], built.Spectral, dir, hit.hitPoint, Phase1Reference.Hash2D(x, y), 0u);
                Vector3 expected = Vector3.Clamp(Phase1Reference.ResolveToSRGB(corrected), Vector3.Zero, Vector3.One);

                int o = (y * Width + x) * 4;
                var gpu = new Vector3(image[o] / 255f, image[o + 1] / 255f, image[o + 2] / 255f);
                checkHits++;
                if (MathF.Abs(gpu.X - expected.X) <= tol &&
                    MathF.Abs(gpu.Y - expected.Y) <= tol &&
                    MathF.Abs(gpu.Z - expected.Z) <= tol)
                    within++;
            }
        }

        double matchRate = checkHits > 0 ? (double)within / checkHits : 0.0;
        bool pass = coverage > 0.10 && coverage < 1.0 && checkHits > 0 && matchRate >= 0.90;

        Console.WriteLine($"  surface coverage    : {coverage:P1}");
        Console.WriteLine($"  cpu cross-check px  : {checkHits}, within {tol * 255f:F0}/255: {within} ({matchRate:P1})");
        Console.WriteLine($"  overall             : {(pass ? "PASS" : "FAIL")}");
        return pass ? 0 : 1;
    }

    // ── Phase 2: spectral maze render with lighting (NEE + indirect) ──

    /// <summary>
    /// Headless Phase 2 validation: render one deterministic (jitter-off) NEE
    /// frame, then cross-check a grid of surface pixels against the CPU reference
    /// (<see cref="Phase2Reference.ShadeSample"/> over the same <see cref="BVH"/>).
    /// Requires a DXR 1.1 GPU. Shadow/indirect edges legitimately diverge between
    /// analytic and hardware intersection, so the match threshold is looser than
    /// Phase 1's; a gross shader bug still collapses the match rate to near zero.
    /// </summary>
    private static int RunPhase2SelfTest()
    {
        Console.WriteLine("RayTracer.Gpu — Phase 2 headless self-test (NEE lighting)");
        Phase2Scene built = Phase2Scene.Build(Width, Height);
        Console.WriteLine($"Lights: {built.PackedLights.Count}");

        using var renderer = new Phase2Renderer(
            Width, Height, built.Packed, built.Spectral, built.PackedLights, built.Camera,
            lightingMode: LightingMode.NEE, sampleClamp: 0f,
            maxSampleCount: 1, subPixelJitter: false);
        renderer.Initialize(windowHandle: 0);
        Console.WriteLine($"Adapter: {renderer.AdapterName}");

        byte[] image = renderer.RenderHeadless(frames: 1);

        long nonBlack = 0;
        for (int i = 0; i < Width * Height; i++)
        {
            int o = i * 4;
            if (image[o] > 4 || image[o + 1] > 4 || image[o + 2] > 4)
                nonBlack++;
        }
        double coverage = (double)nonBlack / (Width * Height);

        var tracer = new BvhSceneTracer(built.Tracables, built.Spectral.DeterWavelengths[0]);
        var lightPositions = built.PackedLights.Positions;
        float tanHalfFov = MathF.Tan(built.Camera.Fov * 0.5f);
        float aspectTanHalfFov = built.Camera.Aspect * tanHalfFov;
        var rot = new Vector4(
            built.Camera.Rotation.X, built.Camera.Rotation.Y,
            built.Camera.Rotation.Z, built.Camera.Rotation.W);

        int checkHits = 0, within = 0;
        const float tol = 8f / 255f;
        for (int y = 8; y < Height; y += 37)
        {
            for (int x = 8; x < Width; x += 37)
            {
                Vector3 dir = Phase1Reference.PrimaryRayDirection(
                    x, y, 0u, rot, built.Camera.ImgPlaneZ,
                    tanHalfFov, aspectTanHalfFov, 1f / Width, 1f / Height, subPixelJitter: false);

                if (!tracer.ClosestHit(built.Camera.Position, dir, out _, out _))
                    continue; // background — covered by the coverage check

                Vector3 corrected = Phase2Reference.ShadeSample(
                    tracer, built.Packed.Primitives, built.Spectral, lightPositions,
                    LightingMode.NEE, built.Camera.Position, dir,
                    Phase1Reference.Hash2D(x, y), 0u, out _, out _);
                Vector3 expected = Vector3.Clamp(Phase1Reference.ResolveToSRGB(corrected), Vector3.Zero, Vector3.One);

                int o = (y * Width + x) * 4;
                var gpu = new Vector3(image[o] / 255f, image[o + 1] / 255f, image[o + 2] / 255f);
                checkHits++;
                if (MathF.Abs(gpu.X - expected.X) <= tol &&
                    MathF.Abs(gpu.Y - expected.Y) <= tol &&
                    MathF.Abs(gpu.Z - expected.Z) <= tol)
                    within++;
            }
        }

        double matchRate = checkHits > 0 ? (double)within / checkHits : 0.0;
        bool pass = coverage > 0.10 && coverage < 1.0 && checkHits > 0 && matchRate >= 0.70;

        Console.WriteLine($"  surface coverage    : {coverage:P1}");
        Console.WriteLine($"  cpu cross-check px  : {checkHits}, within {tol * 255f:F0}/255: {within} ({matchRate:P1})");
        Console.WriteLine($"  overall             : {(pass ? "PASS" : "FAIL")}");
        return pass ? 0 : 1;
    }

    // ── Phase 3: temporal pipeline (TAA + bilateral filter + reset lifecycle) ──

    /// <summary>
    /// Headless Phase 3 validation, in two parts. Part A renders frame 0 — where
    /// the resolve is a pass-through (no previous camera ⇒ TAA off, spatial radius
    /// 0) — and cross-checks it against the CPU reference exactly as Phase 2 does,
    /// validating the trace + G-buffer + resolve wiring. Part B then drives the
    /// temporal path on hardware (static frames that reproject + accept history,
    /// then rotation frames with disocclusion + soft reset) as a smoke test that
    /// the two-pass pipeline and ping-pong history run without TDR or garbage.
    /// Requires a DXR 1.1 GPU.
    /// </summary>
    private static int RunPhase3SelfTest()
    {
        Console.WriteLine("RayTracer.Gpu — Phase 3 headless self-test (temporal pipeline)");
        Phase3Scene built = Phase3Scene.Build(Width, Height);
        Console.WriteLine($"Lights: {built.PackedLights.Count}");

        using var renderer = new Phase3Renderer(
            Width, Height, built.Packed, built.Spectral, built.PackedLights, built.Camera,
            lightingMode: LightingMode.NEE, sampleClamp: 0f,
            maxSampleCount: 1, subPixelJitter: false);
        renderer.Initialize(windowHandle: 0);
        Console.WriteLine($"Adapter: {renderer.AdapterName}");

        // ── Part A: frame-0 pass-through parity vs the CPU reference ────
        renderer.RenderHeadlessFrame(reset: true, moving: false);
        byte[] image = renderer.ReadbackOutput();

        var tracer = new BvhSceneTracer(built.Tracables, built.Spectral.DeterWavelengths[0]);
        var lightPositions = built.PackedLights.Positions;
        float tanHalfFov = MathF.Tan(built.Camera.Fov * 0.5f);
        float aspectTanHalfFov = built.Camera.Aspect * tanHalfFov;
        var rot = new Vector4(
            built.Camera.Rotation.X, built.Camera.Rotation.Y,
            built.Camera.Rotation.Z, built.Camera.Rotation.W);

        (double coverage, double meanLuma0) = ImageStats(image);

        int checkHits = 0, within = 0;
        const float tol = 8f / 255f;
        for (int y = 8; y < Height; y += 37)
        {
            for (int x = 8; x < Width; x += 37)
            {
                Vector3 dir = Phase1Reference.PrimaryRayDirection(
                    x, y, 0u, rot, built.Camera.ImgPlaneZ,
                    tanHalfFov, aspectTanHalfFov, 1f / Width, 1f / Height, subPixelJitter: false);

                if (!tracer.ClosestHit(built.Camera.Position, dir, out _, out _))
                    continue;

                Vector3 corrected = Phase2Reference.ShadeSample(
                    tracer, built.Packed.Primitives, built.Spectral, lightPositions,
                    LightingMode.NEE, built.Camera.Position, dir,
                    Phase1Reference.Hash2D(x, y), 0u, out _, out _);
                Vector3 expected = Vector3.Clamp(Phase1Reference.ResolveToSRGB(corrected), Vector3.Zero, Vector3.One);

                int o = (y * Width + x) * 4;
                var gpu = new Vector3(image[o] / 255f, image[o + 1] / 255f, image[o + 2] / 255f);
                checkHits++;
                if (MathF.Abs(gpu.X - expected.X) <= tol &&
                    MathF.Abs(gpu.Y - expected.Y) <= tol &&
                    MathF.Abs(gpu.Z - expected.Z) <= tol)
                    within++;
            }
        }

        double matchRate = checkHits > 0 ? (double)within / checkHits : 0.0;
        bool partA = coverage > 0.10 && coverage < 1.0 && checkHits > 0 && matchRate >= 0.70;

        // ── Part B: exercise the temporal path (static accept + rotation) ──
        for (int f = 0; f < 4; f++)
            renderer.RenderHeadlessFrame(reset: false, moving: false);

        Camera cam = built.Camera;
        for (int f = 0; f < 8; f++)
        {
            Quaternion delta = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.02f);
            cam.Rotation = Quaternion.Normalize(delta * cam.Rotation);
            renderer.SetCamera(cam);
            renderer.RenderHeadlessFrame(reset: false, moving: true);
        }

        byte[] image2 = renderer.ReadbackOutput();
        (double coverage2, double meanLuma2) = ImageStats(image2);
        // A fully-enclosed maze view legitimately fills every pixel after motion,
        // so 100% coverage is expected here; the meanLuma band is the white-out
        // guard. This part is a smoke test that the temporal pipeline runs
        // without TDR/NaN, not a numeric parity check (that's Part A + unit tests).
        bool partB = coverage2 > 0.5 && double.IsFinite(meanLuma2)
            && meanLuma2 > 0.3 * meanLuma0 && meanLuma2 < 3.0 * meanLuma0;

        bool pass = partA && partB;
        Console.WriteLine($"  [A] frame-0 coverage : {coverage:P1}");
        Console.WriteLine($"  [A] cpu cross-check  : {checkHits} px, within {tol * 255f:F0}/255: {within} ({matchRate:P1})");
        Console.WriteLine($"  [B] temporal coverage: {coverage2:P1}, meanLuma {meanLuma0:F1} -> {meanLuma2:F1}");
        Console.WriteLine($"  overall              : {(pass ? "PASS" : "FAIL")}");
        return pass ? 0 : 1;
    }

    // ── Phase 4: temporal pipeline + volumetrics (smoke / fog) ─────────

    /// <summary>
    /// Headless Phase 4 validation, mirroring the Phase 3 self-test with
    /// volumetrics enabled. Part A renders frame 0 (the resolve is a pass-through)
    /// and cross-checks surface pixels against the CPU reference composited with
    /// the <see cref="Phase4Reference"/> volumetric march (Biome smoke, Medium
    /// quality — <c>ShadowStepInterval == 0</c>, so the fog term is pure-math
    /// parity with no analytic-vs-hardware shadow divergence). Part B drives the
    /// temporal path (static + rotation) as a TDR / white-out smoke test.
    /// Requires a DXR 1.1 GPU.
    /// </summary>
    private static int RunPhase4SelfTest()
    {
        Console.WriteLine("RayTracer.Gpu — Phase 4 headless self-test (volumetrics)");
        Phase3Scene built = Phase3Scene.Build(Width, Height);
        VolumetricOptions volumetrics = VolumetricOptions.FromQuality(VolumetricQuality.Medium, SmokeMode.Biome);
        Console.WriteLine($"Lights: {built.PackedLights.Count}, smoke: {volumetrics.SmokeMode}, march steps: {volumetrics.MarchSteps}");

        using var renderer = new Phase4Renderer(
            Width, Height, built.Packed, built.Spectral, built.PackedLights, built.Camera,
            volumetrics, lightingMode: LightingMode.NEE, sampleClamp: 0f,
            maxSampleCount: 1, subPixelJitter: false);
        renderer.Initialize(windowHandle: 0);
        Console.WriteLine($"Adapter: {renderer.AdapterName}");

        // ── Part A: frame-0 pass-through parity vs CPU reference + fog ──
        renderer.RenderHeadlessFrame(reset: true, moving: false);
        byte[] image = renderer.ReadbackOutput();

        var tracer = new BvhSceneTracer(built.Tracables, built.Spectral.DeterWavelengths[0]);
        var lightPositions = built.PackedLights.Positions;
        var lightColors = built.PackedLights.Colors;
        float tanHalfFov = MathF.Tan(built.Camera.Fov * 0.5f);
        float aspectTanHalfFov = built.Camera.Aspect * tanHalfFov;
        var rot = new Vector4(
            built.Camera.Rotation.X, built.Camera.Rotation.Y,
            built.Camera.Rotation.Z, built.Camera.Rotation.W);
        float correction = built.Spectral.DeterministicCorrection;

        (double coverage, double meanLuma0) = ImageStats(image);

        int checkHits = 0, within = 0, foggy = 0;
        double sumDiff = 0;
        // The fog march is a line-for-line port of Phase4Reference, so with the
        // hit point matched the composite tracks the reference closely; the mean
        // error stays tiny and a gross shader/density mismatch blows it up.
        const float tol = 8f / 255f;
        for (int y = 8; y < Height; y += 37)
        {
            for (int x = 8; x < Width; x += 37)
            {
                Vector3 dir = Phase1Reference.PrimaryRayDirection(
                    x, y, 0u, rot, built.Camera.ImgPlaneZ,
                    tanHalfFov, aspectTanHalfFov, 1f / Width, 1f / Height, subPixelJitter: false);

                if (!tracer.ClosestHit(built.Camera.Position, dir, out _, out Vector3 hitPoint))
                    continue; // background — covered by the coverage check

                Vector3 corrected = Phase2Reference.ShadeSample(
                    tracer, built.Packed.Primitives, built.Spectral, lightPositions,
                    LightingMode.NEE, built.Camera.Position, dir,
                    Phase1Reference.Hash2D(x, y), 0u, out _, out _);

                VolumetricSample vol = Phase4Reference.IntegrateSegment(
                    built.Camera.Position, hitPoint, dir, volumetrics, isMoving: false,
                    lightPositions, lightColors, occluder: null);
                Vector3 correctedWithFog = Phase4Reference.Apply(corrected, vol, correction);
                Vector3 expected = Vector3.Clamp(Phase1Reference.ResolveToSRGB(correctedWithFog), Vector3.Zero, Vector3.One);

                int o = (y * Width + x) * 4;
                var gpu = new Vector3(image[o] / 255f, image[o + 1] / 255f, image[o + 2] / 255f);
                checkHits++;
                if (vol.Inscatter != Vector3.Zero || vol.Transmittance < 0.999f)
                    foggy++;
                float dx = MathF.Abs(gpu.X - expected.X);
                float dy = MathF.Abs(gpu.Y - expected.Y);
                float dz = MathF.Abs(gpu.Z - expected.Z);
                sumDiff += (dx + dy + dz) / 3.0;
                if (dx <= tol && dy <= tol && dz <= tol)
                    within++;
            }
        }

        double matchRate = checkHits > 0 ? (double)within / checkHits : 0.0;
        double foggyRate = checkHits > 0 ? (double)foggy / checkHits : 0.0;
        double meanDiff255 = checkHits > 0 ? sumDiff / checkHits * 255.0 : 0.0;
        // The default camera sits in a *clear* biome, whose interior is correctly
        // fog-free now that smoke stays contained in its biome — so only rays that
        // reach a neighbouring fog biome down an open corridor are foggy (a small
        // fraction). The real check is the frame-0 parity: every pixel (foggy and
        // clear) must track the reference, which validates both the fog composite
        // and that the clear-biome pixels are correctly fog-free (containment).
        bool partA = coverage > 0.10 && coverage < 1.0 && checkHits > 0
            && foggyRate > 0.005 && meanDiff255 < 4.0 && matchRate >= 0.85;

        // ── Part B: exercise the temporal path (static accept + rotation) ──
        for (int f = 0; f < 4; f++)
            renderer.RenderHeadlessFrame(reset: false, moving: false);

        Camera cam = built.Camera;
        for (int f = 0; f < 8; f++)
        {
            Quaternion delta = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.02f);
            cam.Rotation = Quaternion.Normalize(delta * cam.Rotation);
            renderer.SetCamera(cam);
            renderer.RenderHeadlessFrame(reset: false, moving: true);
        }

        byte[] image2 = renderer.ReadbackOutput();
        (double coverage2, double meanLuma2) = ImageStats(image2);
        bool partB = coverage2 > 0.5 && double.IsFinite(meanLuma2)
            && meanLuma2 > 0.3 * meanLuma0 && meanLuma2 < 3.0 * meanLuma0;

        bool pass = partA && partB;
        Console.WriteLine($"  [A] frame-0 coverage : {coverage:P1}");
        Console.WriteLine($"  [A] fog present px   : {foggy}/{checkHits} ({foggyRate:P1})");
        Console.WriteLine($"  [A] cpu cross-check  : {checkHits} px, within {tol * 255f:F0}/255: {within} ({matchRate:P1}), mean |Δ|={meanDiff255:F1}/255");
        Console.WriteLine($"  [B] temporal coverage: {coverage2:P1}, meanLuma {meanLuma0:F1} -> {meanLuma2:F1}");
        Console.WriteLine($"  overall              : {(pass ? "PASS" : "FAIL")}");
        return pass ? 0 : 1;
    }

    /// <summary>
    /// Headless capture: converges a still (static accumulation, no motion) from
    /// the scene's start camera with volumetrics enabled, then writes the resolved
    /// frame to a PNG for visual inspection. Handy for eyeballing / tuning the
    /// smoke without an interactive window.
    /// </summary>
    private static int RunPhase4Capture(
        SmokeMode smokeMode, int frames, string savePath, float sampleClamp, int mazeSeed, bool biomeIndicator,
        float walkSeconds, float inscatterOverride, float sigmaOverride, float fogTime)
    {
        if (frames <= 0)
            frames = 200; // enough samples to converge a clean still

        Console.WriteLine($"RayTracer.Gpu — Phase 4 capture ({smokeMode} smoke, {frames} frames, " +
            $"walk {walkSeconds:0.#}s, seed {mazeSeed}) -> {savePath}");
        Phase3Scene built = Phase3Scene.Build(Width, Height, mazeSeed: mazeSeed, lightSeed: LightSeedFrom(mazeSeed));
        VolumetricOptions volumetrics = VolumetricOptions.FromQuality(VolumetricQuality.Medium, smokeMode);
        if (inscatterOverride >= 0f)
            volumetrics = volumetrics with { InscatterStrength = inscatterOverride };
        if (sigmaOverride >= 0f)
            volumetrics = volumetrics with { SigmaScaleFog = sigmaOverride, SigmaScaleGround = sigmaOverride };

        // Optionally advance the autonomous walker deterministically to reach a
        // corridor that looks into a smoky biome, then hold still to converge.
        Camera camera = built.Camera;
        if (walkSeconds > 0f)
        {
            CameraController controller = built.CreateCameraController();
            controller.StillTime = 0.4f;
            controller.MoveTime = 0.8f;
            controller.TurnTime = 0.6f;
            const float dt = 1f / 60f;
            for (int i = 0; i < (int)(walkSeconds / dt); i++)
                controller.Update(dt, camera);
        }

        using var renderer = new Phase4Renderer(
            Width, Height, built.Packed, built.Spectral, built.PackedLights, camera,
            volumetrics, lightingMode: LightingMode.NEE, sampleClamp: sampleClamp,
            biomeIndicator: biomeIndicator);
        renderer.Initialize(windowHandle: 0);
        renderer.SetCamera(camera);
        renderer.SetFogTime(fogTime); // fixed fog phase for a reproducible still
        Console.WriteLine($"Adapter: {renderer.AdapterName}");

        for (int f = 0; f < frames; f++)
            renderer.RenderHeadlessFrame(reset: f == 0, moving: false);

        byte[] rgba = renderer.ReadbackOutput();
        SavePng(rgba, Width, Height, savePath);
        Console.WriteLine($"Saved {Width}x{Height} PNG to {savePath}");
        return 0;
    }

    // ── Phase 5: temporal + volumetric pipeline with debug views ──────

    private static readonly Phase5DebugMode[] DebugModes =
        (Phase5DebugMode[])Enum.GetValues(typeof(Phase5DebugMode));


    /// <summary>
    /// Headless Phase 5 validation. Part A renders frame 0 in Beauty mode and
    /// cross-checks surface pixels against the CPU reference exactly as the Phase 4
    /// self-test does (Beauty is bit-for-bit the Phase 4 path, so this pins the
    /// trace + fog + resolve wiring). Part B then converges a few frames and renders
    /// every <see cref="Phase5DebugMode"/> once, asserting each view is non-degenerate
    /// (finite, some coverage) and that distinct views actually differ — a hardware
    /// smoke test that the resolve debug switch runs TDR-free and routes correctly.
    /// Requires a DXR 1.1 GPU.
    /// </summary>
    private static int RunPhase5SelfTest()
    {
        Console.WriteLine("RayTracer.Gpu — Phase 5 headless self-test (debug views)");
        Phase3Scene built = Phase3Scene.Build(Width, Height);
        VolumetricOptions volumetrics = VolumetricOptions.FromQuality(VolumetricQuality.Medium, SmokeMode.Biome);
        Console.WriteLine($"Lights: {built.PackedLights.Count}, smoke: {volumetrics.SmokeMode}");

        using var renderer = new Phase5Renderer(
            Width, Height, built.Packed, built.Spectral, built.PackedLights, built.Camera,
            volumetrics, lightingMode: LightingMode.NEE, sampleClamp: 0f,
            maxSampleCount: 1, subPixelJitter: false, debugMode: Phase5DebugMode.Beauty);
        renderer.Initialize(windowHandle: 0);
        Console.WriteLine($"Adapter: {renderer.AdapterName}");

        // ── Part A: frame-0 Beauty parity vs CPU reference + fog ──
        renderer.RenderHeadlessFrame(reset: true, moving: false);
        byte[] image = renderer.ReadbackOutput();

        var tracer = new BvhSceneTracer(built.Tracables, built.Spectral.DeterWavelengths[0]);
        var lightPositions = built.PackedLights.Positions;
        var lightColors = built.PackedLights.Colors;
        float tanHalfFov = MathF.Tan(built.Camera.Fov * 0.5f);
        float aspectTanHalfFov = built.Camera.Aspect * tanHalfFov;
        var rot = new Vector4(
            built.Camera.Rotation.X, built.Camera.Rotation.Y,
            built.Camera.Rotation.Z, built.Camera.Rotation.W);
        float correction = built.Spectral.DeterministicCorrection;

        (double coverage, _) = ImageStats(image);

        int checkHits = 0, within = 0, foggy = 0;
        const float tol = 8f / 255f;
        for (int y = 8; y < Height; y += 37)
        {
            for (int x = 8; x < Width; x += 37)
            {
                Vector3 dir = Phase1Reference.PrimaryRayDirection(
                    x, y, 0u, rot, built.Camera.ImgPlaneZ,
                    tanHalfFov, aspectTanHalfFov, 1f / Width, 1f / Height, subPixelJitter: false);

                if (!tracer.ClosestHit(built.Camera.Position, dir, out _, out Vector3 hitPoint))
                    continue;

                Vector3 corrected = Phase2Reference.ShadeSample(
                    tracer, built.Packed.Primitives, built.Spectral, lightPositions,
                    LightingMode.NEE, built.Camera.Position, dir,
                    Phase1Reference.Hash2D(x, y), 0u, out _, out _);

                VolumetricSample vol = Phase4Reference.IntegrateSegment(
                    built.Camera.Position, hitPoint, dir, volumetrics, isMoving: false,
                    lightPositions, lightColors, occluder: null);
                Vector3 correctedWithFog = Phase4Reference.Apply(corrected, vol, correction);
                Vector3 expected = Vector3.Clamp(Phase1Reference.ResolveToSRGB(correctedWithFog), Vector3.Zero, Vector3.One);

                int o = (y * Width + x) * 4;
                var gpu = new Vector3(image[o] / 255f, image[o + 1] / 255f, image[o + 2] / 255f);
                checkHits++;
                if (vol.Inscatter != Vector3.Zero || vol.Transmittance < 0.999f)
                    foggy++;
                if (MathF.Abs(gpu.X - expected.X) <= tol &&
                    MathF.Abs(gpu.Y - expected.Y) <= tol &&
                    MathF.Abs(gpu.Z - expected.Z) <= tol)
                    within++;
            }
        }

        double matchRate = checkHits > 0 ? (double)within / checkHits : 0.0;
        double foggyRate = checkHits > 0 ? (double)foggy / checkHits : 0.0;
        // The default camera sits in a clear biome (now correctly fog-free thanks
        // to biome containment), so only a small fraction of rays reach fog; the
        // per-pixel Beauty parity is the real check.
        bool partA = coverage > 0.10 && coverage < 1.0 && checkHits > 0
            && foggyRate > 0.005 && matchRate >= 0.85;

        // ── Part B: render every debug view (converged) ──
        for (int f = 0; f < 8; f++)
            renderer.RenderHeadlessFrame(reset: false, moving: false);

        var modeImages = new Dictionary<Phase5DebugMode, byte[]>();
        bool viewsOk = true;
        foreach (Phase5DebugMode mode in DebugModes)
        {
            renderer.DebugMode = mode;
            renderer.RenderHeadlessFrame(reset: false, moving: false);
            byte[] view = renderer.ReadbackOutput();
            modeImages[mode] = view;

            (double cov, double luma) = ImageStats(view);
            // Every view must be finite (no NaN/garbage from an unbound buffer).
            // Most must also carry content, but two are legitimately near-black in
            // this parity run: IndirectLighting (the maze's indirect bounce is tiny
            // at 1 spp) and ClampHeatmap (the parity run disables the clamp, so the
            // heatmap is empty). Those only have to be finite.
            bool needsContent = mode != Phase5DebugMode.IndirectLighting
                && mode != Phase5DebugMode.ClampHeatmap;
            bool ok = double.IsFinite(luma) && (!needsContent || (luma > 0.5 && cov > 0.10));
            if (!ok) viewsOk = false;
            Console.WriteLine($"  view {(int)mode} {mode,-16}: coverage {cov:P1}, meanLuma {luma:F1} {(ok ? "" : "  <-- degenerate")}");
        }

        // Distinct AOVs must actually differ (proves the switch routes each buffer,
        // not a constant fallback): Normal vs Depth vs SampleCount disagree on most
        // pixels, and the direct/indirect splits are separately bound.
        double dND = FractionDiffering(modeImages[Phase5DebugMode.Normal], modeImages[Phase5DebugMode.Depth]);
        double dNS = FractionDiffering(modeImages[Phase5DebugMode.Normal], modeImages[Phase5DebugMode.SampleCount]);
        double dDI = FractionDiffering(modeImages[Phase5DebugMode.DirectLighting], modeImages[Phase5DebugMode.IndirectLighting]);
        bool distinct = dND > 0.15 && dNS > 0.15 && dDI > 0.15;

        bool partB = viewsOk && distinct;

        // ── Part C: on-GPU statistics reduction ──
        // With maxSampleCount == 1 the reduction is deterministic: every pixel holds
        // exactly one sample (so spp = 1, max = 1, variance = 0), the clamp is off
        // (clamped ≈ 0%), and the fully-enclosed maze view fills nearly every pixel
        // (hit ≈ 100%). The averages/percentages must be finite and in range.
        Phase5Stats st = renderer.FrameStats;
        bool sppOk = st.AverageEffectiveSpp is > 0.99 and < 1.01 && st.MaxObservedSampleCount == 1u;
        bool varOk = double.IsFinite(st.AverageVariance) && st.AverageVariance < 1e-4;
        bool clampOk = st.ClampedPixelPercent is >= 0.0 and < 0.5;
        bool hitOk = st.HitPixelPercent > 90.0;
        bool histOk = st.AverageHistoryWeight is >= 0.0 and <= 1.0;
        bool rejOk = st.RejectedHistoryPercent is >= 0.0 and <= 100.0;
        bool partC = sppOk && varOk && clampOk && hitOk && histOk && rejOk;

        bool pass = partA && partB && partC;

        Console.WriteLine($"  [A] frame-0 coverage : {coverage:P1}");
        Console.WriteLine($"  [A] fog present px   : {foggy}/{checkHits} ({foggyRate:P1})");
        Console.WriteLine($"  [A] cpu cross-check  : {checkHits} px, within {tol * 255f:F0}/255: {within} ({matchRate:P1})");
        Console.WriteLine($"  [B] views non-degen  : {viewsOk}");
        Console.WriteLine($"  [B] view distinctness: Normal≠Depth {dND:P0}, Normal≠SampleCount {dNS:P0}, Direct≠Indirect {dDI:P0}");
        Console.WriteLine($"  [C] reduced stats    : spp {st.AverageEffectiveSpp:0.000}/{st.MaxObservedSampleCount}, " +
            $"var {st.AverageVariance:0.000000}, clamp {st.ClampedPixelPercent:0.00}%, " +
            $"hit {st.HitPixelPercent:0.0}%, histW {st.AverageHistoryWeight:0.000}, rej {st.RejectedHistoryPercent:0.0}% " +
            $"[{(partC ? "ok" : "OUT OF RANGE")}]");
        Console.WriteLine($"  overall              : {(pass ? "PASS" : "FAIL")}");
        return pass ? 0 : 1;
    }

    // Fraction of pixels where two RGBA8 images differ by more than 8/255 in any
    // channel — a cheap "these views are not the same picture" check.
    private static double FractionDiffering(byte[] a, byte[] b)
    {
        int pixels = Width * Height;
        long differ = 0;
        for (int i = 0; i < pixels; i++)
        {
            int o = i * 4;
            if (Math.Abs(a[o] - b[o]) > 8 || Math.Abs(a[o + 1] - b[o + 1]) > 8 || Math.Abs(a[o + 2] - b[o + 2]) > 8)
                differ++;
        }
        return (double)differ / pixels;
    }

    /// <summary>
    /// Headless Phase 5 capture: converges a still from the scene's start camera
    /// (optionally after walking into the maze) and writes the selected debug view
    /// to a PNG for visual inspection.
    /// </summary>
    private static int RunPhase5Capture(
        SmokeMode smokeMode, int frames, string savePath, float sampleClamp, int mazeSeed,
        bool biomeIndicator, Phase5DebugMode debugMode, float walkSeconds, float fogTime)
    {
        if (frames <= 0)
            frames = 200;

        Console.WriteLine($"RayTracer.Gpu — Phase 5 capture (view={debugMode}, {smokeMode} smoke, {frames} frames, " +
            $"walk {walkSeconds:0.#}s, seed {mazeSeed}) -> {savePath}");
        Phase3Scene built = Phase3Scene.Build(Width, Height, mazeSeed: mazeSeed, lightSeed: LightSeedFrom(mazeSeed));
        VolumetricOptions volumetrics = VolumetricOptions.FromQuality(VolumetricQuality.Medium, smokeMode);

        Camera camera = built.Camera;
        if (walkSeconds > 0f)
        {
            CameraController controller = built.CreateCameraController();
            controller.StillTime = 0.4f;
            controller.MoveTime = 0.8f;
            controller.TurnTime = 0.6f;
            const float dt = 1f / 60f;
            for (int i = 0; i < (int)(walkSeconds / dt); i++)
                controller.Update(dt, camera);
        }

        using var renderer = new Phase5Renderer(
            Width, Height, built.Packed, built.Spectral, built.PackedLights, camera,
            volumetrics, lightingMode: LightingMode.NEE, sampleClamp: sampleClamp,
            biomeIndicator: biomeIndicator, debugMode: debugMode);
        renderer.Initialize(windowHandle: 0);
        renderer.SetCamera(camera);
        renderer.SetFogTime(fogTime);
        Console.WriteLine($"Adapter: {renderer.AdapterName}");

        for (int f = 0; f < frames; f++)
            renderer.RenderHeadlessFrame(reset: f == 0, moving: false);

        byte[] rgba = renderer.ReadbackOutput();
        SavePng(rgba, Width, Height, savePath);
        Console.WriteLine($"Saved {Width}x{Height} PNG ({debugMode}) to {savePath}");
        return 0;
    }

    // ── Phase 6: productization (resize, device-removed recovery, fullscreen) ──

    /// <summary>
    /// Windowed Phase 6 renderer — the GPU backend of the productized default app
    /// (<see cref="RunConfigApp"/>) and the screensaver. Renders the same
    /// autonomous maze walk as Phase 5 (identical shaders / image) on the productized
    /// <see cref="Phase6Renderer"/> with a resizable window, a fullscreen-borderless
    /// toggle (F11), and device-removed recovery. Esc leaves fullscreen (or closes when
    /// windowed); number keys / arrows switch debug views.
    /// </summary>
    private static int RunPhase6Windowed(
        int width, int height, int maxFrames, float sampleClamp, SmokeMode smokeMode, int mazeSeed,
        bool biomeIndicator, float fogDrift, Phase5DebugMode debugMode, bool startFullscreen, int mazeSize = 16,
        RenderStyle style = RenderStyle.Enhanced, bool regenerate = false, MazeProps.Options? props = null,
        bool bumpyWalls = false, bool showOverheadMap = false, bool showRat = false, bool showBuildIn = false,
        bool showOutro = false, bool classicDepthCue = false, int pixelSize = 1)
    {
        EnsureAppConfigured();
        // Classic look: unlit fullbright spectral (LightingMode.None) with smoke off —
        // the spectral equivalent of the original screensaver's flat textures (plan §2).
        bool classic = style == RenderStyle.Classic;
        LightingMode lighting = classic ? LightingMode.None : LightingMode.NEE;
        Phase3Scene built = Phase3Scene.Build(width, height, mazeSeed: mazeSeed, mazeSize: mazeSize,
            lightSeed: LightSeedFrom(mazeSeed), props: props,
            mirrors: new MazeMirrors.Options(Chance: 0.12f, Seed: mazeSeed),
            windows: new MazeWindows.Options(Chance: 0.15f, Seed: mazeSeed),
            wallThickness: 0.12f);
        VolumetricOptions volumetrics = VolumetricOptions.FromQuality(
            VolumetricQuality.Medium, classic ? SmokeMode.None : smokeMode);

        using var form = new Form
        {
            Text = "RayTracer.Gpu — Spectral Maze",
            ClientSize = new Size(width, height),
            FormBorderStyle = FormBorderStyle.Sizable, // Phase 6 supports resize
            KeyPreview = true,
        };

        using var renderer = new Phase6Renderer(
            width, height, built.Packed, built.Spectral, built.PackedLights, built.Camera,
            volumetrics, lightingMode: lighting, sampleClamp: sampleClamp,
            biomeIndicator: biomeIndicator, debugMode: debugMode, bumpyWalls: bumpyWalls,
            showOverheadMap: showOverheadMap, showRat: showRat,
            classicDepthCue: classic && classicDepthCue ? ClassicMode.DepthCueStrength : 0f,
            pixelSize: classic ? pixelSize : 1);

        bool running = true;
        form.FormClosed += (_, _) => running = false;

        // Fullscreen borderless toggle (F11), remembering the windowed placement.
        // Screensaver mode (6.2) will reuse this to go fullscreen on launch.
        bool isFullscreen = false;
        FormBorderStyle savedBorder = form.FormBorderStyle;
        Rectangle savedBounds = form.Bounds;
        void SetFullscreen(bool on)
        {
            if (on == isFullscreen) return;
            if (on)
            {
                savedBorder = form.FormBorderStyle;
                savedBounds = form.Bounds;
                form.WindowState = FormWindowState.Normal;
                form.FormBorderStyle = FormBorderStyle.None;
                form.Bounds = Screen.FromControl(form).Bounds;
            }
            else
            {
                form.FormBorderStyle = savedBorder;
                form.Bounds = savedBounds;
            }
            isFullscreen = on;
        }

        void UpdateTitle()
        {
            if (isFullscreen) return; // no title bar visible
            Phase5Stats st = renderer.FrameStats;
            form.Text = $"RayTracer.Gpu — Spectral Maze — [{(int)renderer.DebugMode}] {renderer.DebugMode} — " +
                $"{renderer.Width}x{renderer.Height} — spp {st.AverageEffectiveSpp:0.0}/{st.MaxObservedSampleCount} — " +
                $"var {st.AverageVariance:0.0000} — seed {mazeSeed}  (F11 fullscreen · Esc exit · 0-9/←→ views)";
        }

        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { if (isFullscreen) SetFullscreen(false); else form.Close(); return; }
            if (e.KeyCode == Keys.F11) { SetFullscreen(!isFullscreen); return; }

            int digit = -1;
            if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) digit = e.KeyCode - Keys.D0;
            else if (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9) digit = e.KeyCode - Keys.NumPad0;

            if (digit >= 0 && digit < DebugModes.Length)
                renderer.DebugMode = DebugModes[digit];
            else if (e.KeyCode is Keys.Right or Keys.Space or Keys.Tab)
                renderer.DebugMode = DebugModes[((int)renderer.DebugMode + 1) % DebugModes.Length];
            else if (e.KeyCode == Keys.Left)
                renderer.DebugMode = DebugModes[((int)renderer.DebugMode + DebugModes.Length - 1) % DebugModes.Length];
            UpdateTitle();
        };

        form.Show();
        renderer.Initialize(form.Handle);
        if (showOverheadMap) { var (g, gw, gh) = MazeMinimap.Build(built.Maze); renderer.SetMinimap(g, gw, gh); }
        if (startFullscreen) SetFullscreen(true);
        UpdateTitle();

        CameraController controller = built.CreateCameraController();
        controller.StillTime = 1.5f;
        controller.MoveTime = 0.9f;
        controller.TurnTime = 0.7f;
        // Classic mode widens the FOV toward the original screensaver's feel; the
        // resize path below preserves camera.Fov, so this carries through.
        Camera camera = classic ? ClassicMode.WithClassicFov(built.Camera) : built.Camera;

        // Animated rat (plan §8): its own autonomous walker; the renderer projects its
        // position to screen in the resolve pass.
        CameraController? ratCtrl = null;
        Camera ratCam = default!;
        if (showRat) ratCtrl = built.CreateRatController(out ratCam);
        var clock = Stopwatch.StartNew();
        double lastSeconds = 0;
        const double BuildInDuration = 1.5; // §9.3 wall-rise intro length (seconds)
        const double OutroDuration = 0.6;   // §9.4 fade-to-black length
        double mazeStartTime = 0;           // reset on each fresh/regenerated maze
        bool outroActive = false;
        double outroStart = 0;

        uint frame = 0;

        // Generates a fresh maze and rebuilds the scene in place (shared by the outro and
        // the no-outro instant cut). Resets the build-in clock so the new maze rises.
        void Regenerate()
        {
            mazeSeed = Random.Shared.Next();
            built = Phase3Scene.Build(renderer.Width, renderer.Height, mazeSeed: mazeSeed,
                mazeSize: mazeSize, lightSeed: LightSeedFrom(mazeSeed),
                props: props is null ? null : props with { Seed = mazeSeed });
            camera = classic ? ClassicMode.WithClassicFov(built.Camera) : built.Camera;
            controller = built.CreateCameraController();
            controller.StillTime = 1.5f;
            controller.MoveTime = 0.9f;
            controller.TurnTime = 0.7f;
            renderer.RebuildScene(built.Packed, built.Spectral, built.PackedLights, camera);
            if (showOverheadMap) { var (g, gw, gh) = MazeMinimap.Build(built.Maze); renderer.SetMinimap(g, gw, gh); }
            if (showRat) ratCtrl = built.CreateRatController(out ratCam);
            mazeStartTime = clock.Elapsed.TotalSeconds;
            frame = 0;
        }

        while (running && form.Created)
        {
            // Apply any pending resize (window drag or fullscreen toggle) before
            // recording, so the swap chain / resources match the client size.
            int cw = form.ClientSize.Width, ch = form.ClientSize.Height;
            if (cw <= 0 || ch <= 0) { Application.DoEvents(); continue; } // minimized
            if (cw != renderer.Width || ch != renderer.Height)
            {
                renderer.Resize(cw, ch);
                // Camera.Aspect is init-only; rebuild with the new ratio, keeping
                // the walker's current position/rotation.
                camera = new Camera
                {
                    Position = camera.Position,
                    Rotation = camera.Rotation,
                    Fov = camera.Fov,
                    Aspect = (float)cw / ch,
                    ImgPlaneZ = camera.ImgPlaneZ,
                };
                frame = 0;
            }

            double now = clock.Elapsed.TotalSeconds;
            float dt = (float)Math.Min(now - lastSeconds, 0.1); // clamp large hitches
            lastSeconds = now;

            // Build-in intro (§9.3): while the maze rises, hold the walker still, drive the
            // reveal height, and reset each frame (the geometry is changing).
            double buildInT = now - mazeStartTime;
            bool building = showBuildIn && buildInT < BuildInDuration;
            renderer.SetRevealHeight(building
                ? (float)(MazeGeometryBuilder.WallHeight * (buildInT / BuildInDuration)) : 1e9f);

            bool holdWalker = building || outroActive;
            controller.Dirty = false;
            if (!holdWalker) controller.Update(dt, camera);
            bool moving = controller.Dirty;

            // Maze finished — the walker reached the goal cell (plan §9.1). With an outro
            // (§9.4) it fades to black first, then regenerates; otherwise it cuts straight.
            bool atGoal = regenerate && !building && !moving && frame > 0 &&
                controller.CurrentCell == (built.Maze.Width - 1, built.Maze.Height - 1);
            if (atGoal && showOutro && !outroActive) { outroActive = true; outroStart = now; }
            if (outroActive)
            {
                double ot = now - outroStart;
                renderer.SetFadeLevel((float)Math.Min(1.0, ot / OutroDuration));
                if (ot >= OutroDuration)
                {
                    outroActive = false;
                    renderer.SetFadeLevel(0f);
                    Regenerate();
                    UpdateTitle();
                    continue;
                }
            }
            else if (atGoal)
            {
                Regenerate();
                UpdateTitle();
                continue;
            }

            if (ratCtrl is not null && !holdWalker) { ratCtrl.Update(dt, ratCam); renderer.SetRatPosition(ratCam.Position); }

            renderer.SetCamera(camera);
            renderer.SetFogTime((float)(now * fogDrift));
            if (!renderer.RenderFrame(reset: frame == 0 || building, moving: moving))
            {
                // Device lost (TDR / driver reset) — rebuild and continue (P10/P12).
                Console.Error.WriteLine("Device removed — attempting recovery…");
                if (!renderer.TryRecoverDevice())
                {
                    MessageBox.Show("The graphics device was lost and could not be recovered.",
                        "RayTracer.Gpu — device removed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    break;
                }
                frame = 0;
                continue;
            }
            frame++;
            if (frame % 10 == 0) UpdateTitle();
            Application.DoEvents();

            if (maxFrames > 0 && frame >= (uint)maxFrames)
            {
                Console.WriteLine($"Rendered {frame} Phase 6 frame(s) without error at {renderer.Width}x{renderer.Height} " +
                    $"(style={style}, {(classic ? "unlit/no-smoke" : smokeMode + " smoke")}, view={renderer.DebugMode}, fullscreen={isFullscreen}).");
                break;
            }
        }

        return 0;
    }

    /// <summary>
    /// Headless Phase 6 capture: converges a still (optionally after walking a few
    /// seconds into the maze) and writes a PNG. Honours the classic/enhanced look so
    /// the two can be A/B'd (plan §2.6) and to seed the golden captures (§10).
    /// </summary>
    private static int RunPhase6Capture(
        SmokeMode smokeMode, int frames, string savePath, float sampleClamp, int mazeSeed,
        Phase5DebugMode debugMode, float walkSeconds, RenderStyle style, MazeProps.Options? props = null,
        bool bumpyWalls = false, bool showOverheadMap = false, bool showRat = false, float reveal = -1f,
        float depthCue = 0f, int pixelate = 1)
    {
        if (frames <= 0) frames = 200; // enough samples to converge a clean still
        bool classic = style == RenderStyle.Classic;
        LightingMode lighting = classic ? LightingMode.None : LightingMode.NEE;

        Console.WriteLine($"RayTracer.Gpu — Phase 6 capture (style={style}, props={props is not null}, {frames} frames, " +
            $"walk {walkSeconds:0.#}s, seed {mazeSeed}) -> {savePath}");
        Phase3Scene built = Phase3Scene.Build(Width, Height, mazeSeed: mazeSeed, lightSeed: LightSeedFrom(mazeSeed), props: props);
        VolumetricOptions volumetrics = VolumetricOptions.FromQuality(
            VolumetricQuality.Medium, classic ? SmokeMode.None : smokeMode);

        Camera camera = classic ? ClassicMode.WithClassicFov(built.Camera) : built.Camera;
        if (walkSeconds > 0f)
        {
            CameraController controller = built.CreateCameraController();
            controller.StillTime = 0.4f;
            controller.MoveTime = 0.8f;
            controller.TurnTime = 0.6f;
            const float dt = 1f / 60f;
            for (int i = 0; i < (int)(walkSeconds / dt); i++)
                controller.Update(dt, camera);
        }

        using var renderer = new Phase6Renderer(
            Width, Height, built.Packed, built.Spectral, built.PackedLights, camera,
            volumetrics, lightingMode: lighting, sampleClamp: sampleClamp,
            biomeIndicator: false, debugMode: debugMode, bumpyWalls: bumpyWalls,
            showOverheadMap: showOverheadMap, showRat: showRat,
            classicDepthCue: classic ? depthCue : 0f, pixelSize: pixelate);
        renderer.Initialize(windowHandle: 0);
        if (showOverheadMap) { var (g, gw, gh) = MazeMinimap.Build(built.Maze); renderer.SetMinimap(g, gw, gh); }
        if (reveal >= 0f) renderer.SetRevealHeight(reveal); // §9.3 frozen build-in still
        // Static still: place the rat a few units ahead of the camera on the floor so it's
        // in view (the walker only moves in the live windowed/screensaver paths).
        if (showRat)
        {
            Vector3 fwd = Vector3.Transform(Vector3.UnitZ, camera.Rotation);
            renderer.SetRatPosition(camera.Position + fwd * 2.6f - new Vector3(0, camera.Position.Y - 0.25f, 0));
        }
        renderer.SetCamera(camera);
        Console.WriteLine($"Adapter: {renderer.AdapterName}");

        for (int f = 0; f < frames; f++)
            renderer.RenderHeadlessFrame(reset: f == 0, moving: false);

        byte[] rgba = renderer.ReadbackOutput();
        SavePng(rgba, Width, Height, savePath);
        Console.WriteLine($"Saved {Width}x{Height} PNG to {savePath}");
        return 0;
    }

    // ── Mirror demo (spectral-effects-plan §1.1) ──────────────────────

    /// <summary>
    /// Headless capture of the maze with generator-placed framed mirrors
    /// (<see cref="MazeMirrors"/>): builds the normal maze with mirrors on a subset of
    /// walls, points the camera head-on at the mirror nearest the start (with a light in
    /// that cell so the reflection is lit), converges a still through the shipping Phase 6
    /// pipeline, and writes a PNG. The mirror reflects the lit maze in front of it, so a
    /// recognisable reflected scene proves the whole CPU→GPU mirror path renders on
    /// hardware. Fixed-seed self-test / golden scenes pass <c>mirrors: null</c>, so they
    /// are unaffected.
    /// </summary>
    private static int RunMirrorDemo(int frames, string savePath, float sampleClamp, int mazeSeed, SmokeMode smokeMode,
        uint motionCap = 12, bool pan = false)
    {
        if (frames <= 0) frames = pan ? 90 : 300; // pan: show the moving image; else converge a still
        Console.WriteLine($"RayTracer.Gpu — mirror demo ({frames} frames, seed {mazeSeed}, smoke {smokeMode}, motionCap {motionCap}, pan {pan}) -> {savePath}");

        var mirrors = new MazeMirrors.Options(Chance: 0.5f, Seed: mazeSeed);
        // Wall signs + floor logo, so the demo also shows decals reflected in mirrors.
        var props = new MazeProps.Options(Logo: true, Signs: true, Seed: mazeSeed);

        // First pass: place mirrors and find the one nearest the start cell.
        Phase3Scene probe = Phase3Scene.Build(Width, Height, mazeSeed: mazeSeed,
            lightSeed: LightSeedFrom(mazeSeed), props: props, mirrors: mirrors, wallThickness: 0.12f);
        float eye = probe.EyeHeight;
        Vector3 startCenter = new(1f, eye, 1f);

        Camera camera = probe.Camera;
        var extraLights = new List<Light>();
        Vector3 ratPos = new(1f, 0.35f, 1f);
        if (TryFindNearestMirror(probe.Tracables, startCenter, out Vector3 mCenter, out Vector3 mNormal))
        {
            // Stand in the facing cell, look head-on at the mirror; light that cell so
            // both the mirror wall and its reflection are lit.
            Vector3 pos = new Vector3(mCenter.X, eye, mCenter.Z) + mNormal * 1.5f;
            camera = new Camera
            {
                Position = pos,
                Rotation = CameraController.HeadingToQuaternion(HeadingFromForward(-mNormal)),
                Fov = MathF.PI / 3f,
                Aspect = (float)Width / Height,
                ImgPlaneZ = 1f,
            };
            extraLights.Add(new Light { Position = pos + mNormal * -0.3f + new Vector3(0, 0.85f, 0), Color = Vector3.One });
            // Stand the rat just behind the camera: the screen-space ApplyRat can't draw
            // anything behind the camera, so seeing it in the mirror proves it's the traced
            // reflection (the mirror reflects the whole near-side hemisphere).
            ratPos = pos + mNormal * 0.35f;
            ratPos.Y = 0.35f;
            extraLights.Add(new Light { Position = ratPos + new Vector3(0, 1.4f, 0), Color = Vector3.One });
        }
        else
        {
            Console.WriteLine("  (no mirror found — using default camera)");
        }

        // Rebuild with the light in the chosen cell (same seed → identical mirrors).
        Phase3Scene built = Phase3Scene.Build(Width, Height, mazeSeed: mazeSeed,
            lightSeed: LightSeedFrom(mazeSeed), props: props, mirrors: mirrors, extraLights: extraLights, wallThickness: 0.12f);

        VolumetricOptions volumetrics = VolumetricOptions.FromQuality(VolumetricQuality.Medium, smokeMode);
        using var renderer = new Phase6Renderer(
            Width, Height, built.Packed, built.Spectral, built.PackedLights, camera,
            volumetrics, lightingMode: LightingMode.NEE, sampleClamp: sampleClamp,
            biomeIndicator: false, debugMode: Phase5DebugMode.Beauty, showRat: true, motionSampleCap: motionCap);
        renderer.Initialize(windowHandle: 0);
        renderer.SetCamera(camera);
        renderer.SetRatPosition(ratPos);
        Console.WriteLine($"Adapter: {renderer.AdapterName}");

        Quaternion baseRot = camera.Rotation;
        for (int f = 0; f < frames; f++)
        {
            if (pan)
            {
                // Yaw the camera up to the final base orientation, so the last frame shows
                // the ghost trail from the recent motion (moving = true, so it accumulates).
                float angle = -0.22f * (1f - f / (float)(frames - 1)); // ~-12.6° → 0
                camera.Rotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle) * baseRot);
                renderer.SetCamera(camera);
            }
            renderer.RenderHeadlessFrame(reset: f == 0, moving: pan);
        }

        byte[] rgba = renderer.ReadbackOutput();
        SavePng(rgba, Width, Height, savePath);
        Console.WriteLine($"Saved {Width}x{Height} PNG to {savePath}");
        return 0;
    }

    /// <summary>Finds the mirror quad (<see cref="SurfaceKind.Mirror"/>) whose centre is
    /// nearest <paramref name="near"/> (compared in the XZ plane), returning its centre
    /// and facing normal.</summary>
    private static bool TryFindNearestMirror(
        IReadOnlyList<Tracable> scene, Vector3 near, out Vector3 center, out Vector3 normal)
    {
        center = default;
        normal = default;
        float best = float.MaxValue;
        foreach (Tracable t in scene)
        {
            if (t is not IQuadPrimitive q || q.PrimaryMaterial.Surface != SurfaceKind.Mirror)
                continue;
            Vector3 c = q.L1 + q.Edge1 * 0.5f + q.Edge2 * 0.5f;
            float d = Vector3.DistanceSquared(new Vector3(c.X, near.Y, c.Z), near);
            if (d < best) { best = d; center = c; normal = q.QuadNormal; }
        }
        return best < float.MaxValue;
    }

    /// <summary>Maps an axis-aligned forward vector to the nearest maze heading.</summary>
    private static Direction HeadingFromForward(Vector3 f)
    {
        if (MathF.Abs(f.Z) >= MathF.Abs(f.X))
            return f.Z >= 0f ? Direction.South : Direction.North;
        return f.X >= 0f ? Direction.East : Direction.West;
    }

    /// <summary>Builds the camera rotation that points local forward (+Z) from
    /// <paramref name="pos"/> toward <paramref name="target"/> (matching the camera's
    /// <c>Vector3.Transform(localDir, Rotation)</c> convention).</summary>
    private static Quaternion LookRotation(Vector3 pos, Vector3 target, Vector3 up)
    {
        Vector3 f = Vector3.Normalize(target - pos);
        Vector3 r = Vector3.Normalize(Vector3.Cross(up, f));
        Vector3 u = Vector3.Cross(f, r);
        var m = new Matrix4x4(
            r.X, r.Y, r.Z, 0f,
            u.X, u.Y, u.Z, 0f,
            f.X, f.Y, f.Z, 0f,
            0f, 0f, 0f, 1f);
        return Quaternion.CreateFromRotationMatrix(m);
    }

    /// <summary>
    /// Headless capture of the maze with generator-placed glass windows
    /// (<see cref="MazeWindows"/>): builds the maze with windows in passage openings,
    /// stands the camera in a cell looking through the window nearest the start into the
    /// far cell (lighting both cells), converges a still through the shipping Phase 6
    /// pipeline, and writes a PNG. Seeing the far cell refracted through the pane — with
    /// a faint Fresnel reflection of the near cell — proves the CPU→GPU dielectric path
    /// renders on hardware.
    /// </summary>
    private static int RunGlassDemo(int frames, string savePath, float sampleClamp, int mazeSeed)
    {
        if (frames <= 0) frames = 400; // glass roulette is noisier; give it more samples
        Console.WriteLine($"RayTracer.Gpu — glass demo ({frames} frames, seed {mazeSeed}) -> {savePath}");

        var windows = new MazeWindows.Options(Chance: 0.6f, Seed: mazeSeed);
        var mirrors = new MazeMirrors.Options(Chance: 0.2f, Seed: mazeSeed);
        var props = new MazeProps.Options(Logo: true, Signs: true, Seed: mazeSeed);

        Phase3Scene probe = Phase3Scene.Build(Width, Height, mazeSeed: mazeSeed,
            lightSeed: LightSeedFrom(mazeSeed), props: props, mirrors: mirrors, windows: windows, wallThickness: 0.12f);
        float eye = probe.EyeHeight;
        Vector3 startCenter = new(1f, eye, 1f);

        Camera camera = probe.Camera;
        var extraLights = new List<Light>();
        if (TryFindNearestGlass(probe.Tracables, startCenter, out Vector3 wCenter, out Vector3 wNormal))
        {
            // View the pane obliquely from the near cell: at an angle the Fresnel
            // reflection of the near cell is strong and the refracted far cell is
            // laterally shifted, so the glass reads as glass (head-on it is ~invisible).
            Vector3 worldUp = new(0, 1, 0);
            Vector3 flat = new(wCenter.X, eye, wCenter.Z);
            Vector3 side = Vector3.Normalize(Vector3.Cross(wNormal, worldUp)); // along the passage
            Vector3 pos = flat + wNormal * 1.7f + side * 0.55f;
            camera = new Camera
            {
                Position = pos,
                Rotation = LookRotation(pos, wCenter, worldUp), // aim at the glass centre (raised by the sill)
                Fov = MathF.PI / 3f,
                Aspect = (float)Width / Height,
                ImgPlaneZ = 1f,
            };
            Vector3 up = new(0, 0.85f, 0);
            extraLights.Add(new Light { Position = flat - wNormal * 1.1f + up, Color = Vector3.One });        // far cell (transmission)
            extraLights.Add(new Light { Position = flat + wNormal * 1.1f - side * 0.6f + up, Color = Vector3.One }); // near cell (reflection)
        }
        else
        {
            Console.WriteLine("  (no window found — using default camera)");
        }

        Phase3Scene built = Phase3Scene.Build(Width, Height, mazeSeed: mazeSeed,
            lightSeed: LightSeedFrom(mazeSeed), props: props, mirrors: mirrors, windows: windows,
            extraLights: extraLights, wallThickness: 0.12f);

        VolumetricOptions volumetrics = VolumetricOptions.FromQuality(VolumetricQuality.Medium, SmokeMode.None);
        using var renderer = new Phase6Renderer(
            Width, Height, built.Packed, built.Spectral, built.PackedLights, camera,
            volumetrics, lightingMode: LightingMode.NEE, sampleClamp: sampleClamp,
            biomeIndicator: false, debugMode: Phase5DebugMode.Beauty);
        renderer.Initialize(windowHandle: 0);
        renderer.SetCamera(camera);
        Console.WriteLine($"Adapter: {renderer.AdapterName}");

        for (int f = 0; f < frames; f++)
            renderer.RenderHeadlessFrame(reset: f == 0, moving: false);

        byte[] rgba = renderer.ReadbackOutput();
        SavePng(rgba, Width, Height, savePath);
        Console.WriteLine($"Saved {Width}x{Height} PNG to {savePath}");
        return 0;
    }

    /// <summary>Finds the dielectric (glass) quad whose centre is nearest
    /// <paramref name="near"/> (in the XZ plane), returning its centre and facing normal.</summary>
    private static bool TryFindNearestGlass(
        IReadOnlyList<Tracable> scene, Vector3 near, out Vector3 center, out Vector3 normal)
    {
        center = default;
        normal = default;
        float best = float.MaxValue;
        foreach (Tracable t in scene)
        {
            if (t is not IQuadPrimitive q || q.PrimaryMaterial.Surface != SurfaceKind.Dielectric)
                continue;
            Vector3 c = q.L1 + q.Edge1 * 0.5f + q.Edge2 * 0.5f;
            float d = Vector3.DistanceSquared(new Vector3(c.X, near.Y, c.Z), near);
            if (d < best) { best = d; center = c; normal = q.QuadNormal; }
        }
        return best < float.MaxValue;
    }

    // ── The productized app: config setup, then run ───────────────────

    /// <summary>
    /// The default launch — the finished product. Shows the unified <see cref="ConfigForm"/>
    /// (pick the GPU or CPU renderer plus every option for it, persisted across runs), then
    /// runs the chosen backend: <b>GPU</b> → the productized Phase 6 pipeline (spectral
    /// shading, NEE + indirect lighting, the temporal denoiser, volumetrics, debug views,
    /// resize / fullscreen / device-recovery); <b>CPU</b> → the software <see cref="RayForm"/>
    /// via <see cref="CpuLauncher"/>. Exit from the dialog quits without running.
    /// </summary>
    private static int RunConfigApp()
    {
        EnsureAppConfigured();

        AppSettings settings = AppSettings.Load();
        RenderPreset cpuPreset;
        using (var config = new ConfigForm(settings))
        {
            if (config.ShowDialog() != DialogResult.OK)
                return 0; // user chose Exit
            settings = config.Result;
            cpuPreset = config.SelectedCpuPreset;
        }
        settings.Save();

        // CPU backend: build the maze scene and run the software renderer.
        if (settings.Backend == RenderBackend.Cpu)
        {
            CpuLauncher.Run(cpuPreset);
            return 0;
        }

        // GPU backend: fresh maze each run (like the original, which seeds from the clock).
        int mazeSeed = Random.Shared.Next();
        return RunPhase6Windowed(
            settings.Width, settings.Height, maxFrames: 0, sampleClamp: settings.SampleClamp,
            smokeMode: settings.SmokeMode, mazeSeed: mazeSeed, biomeIndicator: false,
            fogDrift: settings.FogDrift, debugMode: settings.StartView,
            startFullscreen: settings.Fullscreen, mazeSize: settings.MazeSize, style: settings.Style,
            regenerate: settings.MazeRegenerate,
            props: (settings.ShowOpenGlLogo || settings.ShowWallSigns)
                ? new MazeProps.Options(Logo: settings.ShowOpenGlLogo, Signs: settings.ShowWallSigns, Seed: mazeSeed)
                : null,
            bumpyWalls: settings.BumpyWalls, showOverheadMap: settings.ShowOverheadMap,
            showRat: settings.ShowRat, showBuildIn: settings.MazeBuildInAnim,
            showOutro: settings.MazeOutroAnim, classicDepthCue: settings.ClassicDepthCue,
            pixelSize: settings.RetroPixelation ? ClassicMode.RetroBlockFor(settings.Height) : 1);
    }

    /// <summary>
    /// Constructs the config screen and reads back its defaults without showing it — a
    /// non-interactive check that the config UI builds (control layout, enum binding)
    /// and that <see cref="ConfigForm.Result"/> round-trips the incoming settings. Needs
    /// a desktop session (WinForms), so it is a dev-box check, not CI.
    /// </summary>
    private static int RunSetupSelfTest()
    {
        EnsureAppConfigured();
        Console.WriteLine("RayTracer.Gpu — config screen self-test");

        var input = new AppSettings
        {
            Backend = RenderBackend.Cpu,
            Width = 1920, Height = 1080, Fullscreen = true, Style = RenderStyle.Classic,
            SmokeMode = SmokeMode.AlwaysFog, StartView = Phase5DebugMode.Normal, MazeSize = 20,
            CpuPreset = "High", CpuThrottle = CpuThrottle.Half,
            ShowRat = false, ShowOpenGlLogo = false, ShowWallSigns = false, ShowOverheadMap = true,
            BumpyWalls = false, ClassicDepthCue = true, RetroPixelation = true,
            MazeBuildInAnim = false, MazeRegenerate = false, MazeOutroAnim = false,
        };
        using var dlg = new ConfigForm(input);
        bool builds = dlg.Controls.Count > 0;
        // Before OK is clicked, Result mirrors the incoming settings (a clone).
        bool roundTrips = dlg.Result.Backend == input.Backend
            && dlg.Result.Width == input.Width && dlg.Result.Height == input.Height
            && dlg.Result.Fullscreen == input.Fullscreen && dlg.Result.Style == input.Style
            && dlg.Result.SmokeMode == input.SmokeMode
            && dlg.Result.StartView == input.StartView && dlg.Result.MazeSize == input.MazeSize
            && dlg.Result.CpuPreset == input.CpuPreset && dlg.Result.CpuThrottle == input.CpuThrottle
            && dlg.Result.ShowRat == input.ShowRat && dlg.Result.ShowOpenGlLogo == input.ShowOpenGlLogo
            && dlg.Result.ShowWallSigns == input.ShowWallSigns && dlg.Result.ShowOverheadMap == input.ShowOverheadMap
            && dlg.Result.BumpyWalls == input.BumpyWalls && dlg.Result.ClassicDepthCue == input.ClassicDepthCue
            && dlg.Result.RetroPixelation == input.RetroPixelation
            && dlg.Result.MazeBuildInAnim == input.MazeBuildInAnim
            && dlg.Result.MazeRegenerate == input.MazeRegenerate && dlg.Result.MazeOutroAnim == input.MazeOutroAnim;

        Console.WriteLine($"  [{(builds ? "ok" : "FAIL")}] dialog builds ({dlg.Controls.Count} controls)");
        Console.WriteLine($"  [{(roundTrips ? "ok" : "FAIL")}] Result mirrors incoming settings");
        bool pass = builds && roundTrips;
        Console.WriteLine($"  overall              : {(pass ? "PASS" : "FAIL")}");
        return pass ? 0 : 1;
    }

    /// <summary>
    /// Headless Phase 6 validation. Part A renders frame 0 in Beauty and cross-checks
    /// surface pixels against the CPU reference exactly as the Phase 5 self-test does
    /// (Phase 6 reuses the Phase 5 shaders, so Beauty is bit-for-bit the Phase 5 image
    /// — this pins that the productized host still renders correctly). Part B exercises
    /// the resize path (down to 960×540 and back to 1280×720), asserting each resized
    /// image is non-degenerate. Part C exercises device-removed recovery by invoking
    /// <see cref="Phase6Renderer.TryRecoverDevice"/> (the same full teardown + re-init
    /// a real TDR triggers) and asserting the rebuilt device renders a valid frame.
    /// Requires a DXR 1.1 GPU.
    /// </summary>
    private static int RunPhase6SelfTest()
    {
        Console.WriteLine("RayTracer.Gpu — Phase 6 headless self-test (productization)");
        Phase3Scene built = Phase3Scene.Build(Width, Height);
        VolumetricOptions volumetrics = VolumetricOptions.FromQuality(VolumetricQuality.Medium, SmokeMode.Biome);
        Console.WriteLine($"Lights: {built.PackedLights.Count}, smoke: {volumetrics.SmokeMode}");

        using var renderer = new Phase6Renderer(
            Width, Height, built.Packed, built.Spectral, built.PackedLights, built.Camera,
            volumetrics, lightingMode: LightingMode.NEE, sampleClamp: 0f,
            maxSampleCount: 1, subPixelJitter: false, debugMode: Phase5DebugMode.Beauty);
        renderer.Initialize(windowHandle: 0);
        Console.WriteLine($"Adapter: {renderer.AdapterName}");

        // ── Part A: frame-0 Beauty parity vs CPU reference + fog ──
        renderer.RenderHeadlessFrame(reset: true, moving: false);
        byte[] image = renderer.ReadbackOutput();

        var tracer = new BvhSceneTracer(built.Tracables, built.Spectral.DeterWavelengths[0]);
        var lightPositions = built.PackedLights.Positions;
        var lightColors = built.PackedLights.Colors;
        float tanHalfFov = MathF.Tan(built.Camera.Fov * 0.5f);
        float aspectTanHalfFov = built.Camera.Aspect * tanHalfFov;
        var rot = new Vector4(
            built.Camera.Rotation.X, built.Camera.Rotation.Y,
            built.Camera.Rotation.Z, built.Camera.Rotation.W);
        float correction = built.Spectral.DeterministicCorrection;

        (double coverage, _) = ImageStats(image);

        int checkHits = 0, within = 0, foggy = 0;
        const float tol = 8f / 255f;
        for (int y = 8; y < Height; y += 37)
        {
            for (int x = 8; x < Width; x += 37)
            {
                Vector3 dir = Phase1Reference.PrimaryRayDirection(
                    x, y, 0u, rot, built.Camera.ImgPlaneZ,
                    tanHalfFov, aspectTanHalfFov, 1f / Width, 1f / Height, subPixelJitter: false);

                if (!tracer.ClosestHit(built.Camera.Position, dir, out _, out Vector3 hitPoint))
                    continue;

                Vector3 corrected = Phase2Reference.ShadeSample(
                    tracer, built.Packed.Primitives, built.Spectral, lightPositions,
                    LightingMode.NEE, built.Camera.Position, dir,
                    Phase1Reference.Hash2D(x, y), 0u, out _, out _);

                VolumetricSample vol = Phase4Reference.IntegrateSegment(
                    built.Camera.Position, hitPoint, dir, volumetrics, isMoving: false,
                    lightPositions, lightColors, occluder: null);
                Vector3 correctedWithFog = Phase4Reference.Apply(corrected, vol, correction);
                Vector3 expected = Vector3.Clamp(Phase1Reference.ResolveToSRGB(correctedWithFog), Vector3.Zero, Vector3.One);

                int o = (y * Width + x) * 4;
                var gpu = new Vector3(image[o] / 255f, image[o + 1] / 255f, image[o + 2] / 255f);
                checkHits++;
                if (vol.Inscatter != Vector3.Zero || vol.Transmittance < 0.999f)
                    foggy++;
                if (MathF.Abs(gpu.X - expected.X) <= tol &&
                    MathF.Abs(gpu.Y - expected.Y) <= tol &&
                    MathF.Abs(gpu.Z - expected.Z) <= tol)
                    within++;
            }
        }

        double matchRate = checkHits > 0 ? (double)within / checkHits : 0.0;
        double foggyRate = checkHits > 0 ? (double)foggy / checkHits : 0.0;
        bool partA = coverage > 0.10 && coverage < 1.0 && checkHits > 0
            && foggyRate > 0.005 && matchRate >= 0.85;

        // ── Part B: resize down to 960×540 and back to 1280×720 ──
        renderer.Resize(960, 540);
        for (int f = 0; f < 2; f++)
            renderer.RenderHeadlessFrame(reset: f == 0, moving: false);
        byte[] small = renderer.ReadbackOutput();
        (double covSmall, double lumaSmall) = ImageStatsAny(small);
        bool smallOk = small.Length == 960 * 540 * 4
            && covSmall > 0.10 && double.IsFinite(lumaSmall) && lumaSmall > 0.5;

        renderer.Resize(Width, Height);
        for (int f = 0; f < 2; f++)
            renderer.RenderHeadlessFrame(reset: f == 0, moving: false);
        byte[] restored = renderer.ReadbackOutput();
        (double covRestore, double lumaRestore) = ImageStatsAny(restored);
        bool restoreOk = restored.Length == Width * Height * 4
            && covRestore > 0.10 && double.IsFinite(lumaRestore) && lumaRestore > 0.5;
        bool partB = smallOk && restoreOk;

        // ── Part C: device-removed recovery (full teardown + re-init) ──
        bool recovered = renderer.TryRecoverDevice();
        for (int f = 0; f < 2; f++)
            renderer.RenderHeadlessFrame(reset: f == 0, moving: false);
        byte[] afterRecover = renderer.ReadbackOutput();
        (double covRec, double lumaRec) = ImageStatsAny(afterRecover);
        bool partC = recovered && afterRecover.Length == Width * Height * 4
            && covRec > 0.10 && double.IsFinite(lumaRec) && lumaRec > 0.5;

        bool pass = partA && partB && partC;
        Console.WriteLine($"  [A] frame-0 coverage : {coverage:P1}");
        Console.WriteLine($"  [A] fog present px   : {foggy}/{checkHits} ({foggyRate:P1})");
        Console.WriteLine($"  [A] cpu cross-check  : {checkHits} px, within {tol * 255f:F0}/255: {within} ({matchRate:P1})");
        Console.WriteLine($"  [B] resize 960x540   : coverage {covSmall:P1}, luma {lumaSmall:F1} [{(smallOk ? "ok" : "FAIL")}]");
        Console.WriteLine($"  [B] restore {Width}x{Height} : coverage {covRestore:P1}, luma {lumaRestore:F1} [{(restoreOk ? "ok" : "FAIL")}]");
        Console.WriteLine($"  [C] device recovery  : recovered={recovered}, coverage {covRec:P1}, luma {lumaRec:F1} [{(partC ? "ok" : "FAIL")}]");
        Console.WriteLine($"  overall              : {(pass ? "PASS" : "FAIL")}");
        return pass ? 0 : 1;
    }

    /// <summary>
    /// Headless validation of the maze-regeneration path (plan §9.5): builds a scene,
    /// then repeatedly renders a few frames and calls <see cref="Phase6Renderer.RebuildScene"/>
    /// with a fresh maze, asserting every maze renders a valid (non-degenerate) frame. This
    /// exercises the scene-rebuild loop the walk uses when the walker reaches the goal, and
    /// proves it is TDR-free and leak-free across many mazes. Requires a DXR 1.1 GPU.
    /// </summary>
    private static int RunRegenSelfTest()
    {
        Console.WriteLine("RayTracer.Gpu — maze regeneration self-test (scene rebuild loop)");
        int seed = 1;
        Phase3Scene built = Phase3Scene.Build(Width, Height, mazeSeed: seed, lightSeed: LightSeedFrom(seed));

        using var renderer = new Phase6Renderer(
            Width, Height, built.Packed, built.Spectral, built.PackedLights, built.Camera,
            VolumetricOptions.FromQuality(VolumetricQuality.Medium, SmokeMode.Biome),
            lightingMode: LightingMode.NEE, sampleClamp: 0f,
            maxSampleCount: 1, subPixelJitter: false, debugMode: Phase5DebugMode.Beauty);
        renderer.Initialize(windowHandle: 0);
        Console.WriteLine($"Adapter: {renderer.AdapterName}");

        const int cycles = 6;
        bool pass = true;
        for (int c = 0; c < cycles; c++)
        {
            for (int f = 0; f < 3; f++)
                renderer.RenderHeadlessFrame(reset: f == 0, moving: false);
            (double cov, double luma) = ImageStatsAny(renderer.ReadbackOutput());
            bool ok = cov > 0.10 && double.IsFinite(luma) && luma > 0.5;
            Console.WriteLine($"  maze {c} (seed {seed,11}): coverage {cov:P1}, luma {luma:F1} [{(ok ? "ok" : "FAIL")}]");
            pass &= ok;

            seed = new Random(seed).Next() | 1;
            built = Phase3Scene.Build(Width, Height, mazeSeed: seed, lightSeed: LightSeedFrom(seed));
            if (!renderer.RebuildScene(built.Packed, built.Spectral, built.PackedLights, built.Camera))
            {
                Console.WriteLine("  RebuildScene returned false");
                pass = false;
                break;
            }
        }

        for (int f = 0; f < 3; f++)
            renderer.RenderHeadlessFrame(reset: f == 0, moving: false);
        (double covF, double lumaF) = ImageStatsAny(renderer.ReadbackOutput());
        bool finalOk = covF > 0.10 && double.IsFinite(lumaF) && lumaF > 0.5;
        Console.WriteLine($"  final (after rebuild): coverage {covF:P1}, luma {lumaF:F1} [{(finalOk ? "ok" : "FAIL")}]");
        pass &= finalOk;

        Console.WriteLine($"  overall              : {(pass ? "PASS" : "FAIL")}");
        return pass ? 0 : 1;
    }

    /// <summary>
    /// CPU-only validation of the screensaver wiring (Phase 6.2): the classic switch
    /// forms parse to the right mode + HWND, non-screensaver args are ignored, and the
    /// persisted settings survive a JSON round-trip. No GPU required, so it runs in CI
    /// too — the actual full-screen / preview render paths are exercised by
    /// <c>/s --frames N</c> on the dev box.
    /// </summary>
    private static int RunScreensaverSelfTest()
    {
        Console.WriteLine("RayTracer.Gpu — screensaver mode self-test (arg parsing + settings)");
        int failures = 0;
        void Check(bool cond, string label)
        {
            Console.WriteLine($"  [{(cond ? "ok" : "FAIL")}] {label}");
            if (!cond) failures++;
        }

        Check(Screensaver.TryParse(["/s"], out var m1, out _) && m1 == Screensaver.Mode.Show, "/s -> Show");
        Check(Screensaver.TryParse(["/c"], out var m2, out _) && m2 == Screensaver.Mode.Config, "/c -> Config");
        Check(Screensaver.TryParse(["/C:12345"], out var m3, out nint h3) && m3 == Screensaver.Mode.Config && h3 == 12345, "/C:12345 -> Config + hwnd");
        Check(Screensaver.TryParse(["/p", "67890"], out var m4, out nint h4) && m4 == Screensaver.Mode.Preview && h4 == 67890, "/p 67890 -> Preview + hwnd");
        Check(Screensaver.TryParse(["/P:0x1F4"], out var m5, out nint h5) && m5 == Screensaver.Mode.Preview && h5 == 500, "/P:0x1F4 -> Preview + hex hwnd");
        Check(!Screensaver.TryParse(["--phase6"], out _, out _), "--phase6 is not a screensaver switch");
        Check(!Screensaver.TryParse([], out _, out _), "no args is not a screensaver switch");

        var s = new AppSettings
        {
            Style = RenderStyle.Classic,
            SmokeMode = SmokeMode.AlwaysFog,
            FogDrift = 2.5f,
            SampleClamp = 4f,
            MazeSize = 24,
            StillTime = 1.0f,
            ShowRat = false,
            ShowOverheadMap = true,
            BumpyWalls = false,
            MazeRegenerate = false,
        };
        string json = System.Text.Json.JsonSerializer.Serialize(s);
        var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;
        Check(back.Style == s.Style && back.SmokeMode == s.SmokeMode && back.FogDrift == s.FogDrift
            && back.SampleClamp == s.SampleClamp && back.MazeSize == s.MazeSize
            && back.ShowRat == s.ShowRat && back.ShowOverheadMap == s.ShowOverheadMap
            && back.BumpyWalls == s.BumpyWalls && back.MazeRegenerate == s.MazeRegenerate,
            "settings JSON round-trip");

        Console.WriteLine($"  overall              : {(failures == 0 ? "PASS" : "FAIL")}");
        return failures == 0 ? 0 : 1;
    }

    // Coverage + mean luma over an RGBA8 image of any size (derives the pixel count
    // from the buffer length, so it works for a resized render target).
    private static (double coverage, double meanLuma) ImageStatsAny(byte[] image)
    {
        long nonBlack = 0;
        double lumaSum = 0;
        int pixels = image.Length / 4;
        for (int i = 0; i < pixels; i++)
        {
            int o = i * 4;
            if (image[o] > 4 || image[o + 1] > 4 || image[o + 2] > 4)
                nonBlack++;
            lumaSum += 0.299 * image[o] + 0.587 * image[o + 1] + 0.114 * image[o + 2];
        }
        return ((double)nonBlack / pixels, lumaSum / pixels);
    }

    // Writes a tightly-packed RGBA8 buffer to a PNG (RGBA -> BGRA for GDI+).
    internal static void SavePng(byte[] rgba, int width, int height, string path)
    {
        using var bmp = new System.Drawing.Bitmap(
            width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var rect = new System.Drawing.Rectangle(0, 0, width, height);
        System.Drawing.Imaging.BitmapData data =
            bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
        try
        {
            var row = new byte[width * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int s = (y * width + x) * 4;
                    int d = x * 4;
                    row[d + 0] = rgba[s + 2]; // B
                    row[d + 1] = rgba[s + 1]; // G
                    row[d + 2] = rgba[s + 0]; // R
                    row[d + 3] = 255;         // A
                }
                System.Runtime.InteropServices.Marshal.Copy(
                    row, 0, System.IntPtr.Add(data.Scan0, y * data.Stride), width * 4);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    // Fraction of non-black pixels and mean sRGB luma (0-255) of an RGBA8 image.
    private static (double coverage, double meanLuma) ImageStats(byte[] image)
    {
        long nonBlack = 0;
        double lumaSum = 0;
        int pixels = Width * Height;
        for (int i = 0; i < pixels; i++)
        {
            int o = i * 4;
            if (image[o] > 4 || image[o + 1] > 4 || image[o + 2] > 4)
                nonBlack++;
            lumaSum += 0.299 * image[o] + 0.587 * image[o + 1] + 0.114 * image[o + 2];
        }
        return ((double)nonBlack / pixels, lumaSum / pixels);
    }

    private static int RunSelfTest()
    {
        Console.WriteLine("RayTracer.Gpu — Phase 0 headless self-test");
        using var renderer = new GpuRayTracer(Width, Height);
        renderer.Initialize(windowHandle: 0);
        Console.WriteLine($"Adapter: {renderer.AdapterName}");
        Console.WriteLine("DXR 1.1 (inline ray tracing) supported: yes");

        (bool passed, string report) = renderer.RunSelfTest();
        Console.WriteLine("RayQuery result:");
        Console.WriteLine(report);
        return passed ? 0 : 1;
    }

    private static int ParseIntOption(string[] args, string name, int defaultValue)
    {
        int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int value))
            return value;
        return defaultValue;
    }

    private static float ParseFloatOption(string[] args, string name, float defaultValue)
    {
        int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (i >= 0 && i + 1 < args.Length &&
            float.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out float value))
            return value;
        return defaultValue;
    }

    private static string? ParseStringOption(string[] args, string name, string? defaultValue)
    {
        int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (i >= 0 && i + 1 < args.Length)
            return args[i + 1];
        return defaultValue;
    }

    // The windowed demos vary the maze each run (like the original app, which
    // seeds from Environment.TickCount) so the start of the maze isn't always the
    // same. Pass `--seed <n>` to reproduce a specific maze. The self-tests keep
    // their own fixed seeds so their CPU cross-check stays deterministic.
    private static int ResolveMazeSeed(string[] args)
    {
        int seed = ParseIntOption(args, "--seed", defaultValue: int.MinValue);
        return seed == int.MinValue ? Random.Shared.Next() : seed;
    }

    // Derive the light seed from the maze seed so a single `--seed` fully
    // determines the scene (maze + light placement) and is reproducible.
    private static int LightSeedFrom(int mazeSeed) => new Random(mazeSeed).Next();

    // Phase 4 smoke-mode selection for the windowed demo. Defaults to biome-banded
    // smoke (the mode Phase 4.2 verifies); `--fog` / `--ground` force a single mode.
    private static SmokeMode ParseSmokeMode(string[] args)
    {
        if (args.Contains("--fog", StringComparer.OrdinalIgnoreCase))
            return SmokeMode.AlwaysFog;
        if (args.Contains("--ground", StringComparer.OrdinalIgnoreCase))
            return SmokeMode.AlwaysGroundSmoke;
        return SmokeMode.Biome;
    }

}
