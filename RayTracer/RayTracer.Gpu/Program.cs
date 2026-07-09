using System.Numerics;
using System.Windows.Forms;
using RayTracer;

namespace RayTracer.Gpu;

internal static class Program
{
    private const int Width = 1280;
    private const int Height = 720;

    [STAThread]
    private static int Main(string[] args)
    {
        bool selfTest = args.Contains("--selftest", StringComparer.OrdinalIgnoreCase);
        bool phase1SelfTest = args.Contains("--phase1-selftest", StringComparer.OrdinalIgnoreCase);
        bool phase1 = args.Contains("--phase1", StringComparer.OrdinalIgnoreCase);
        int maxFrames = ParseIntOption(args, "--frames", defaultValue: 0);
        bool headless = selfTest || phase1SelfTest;

        try
        {
            if (phase1SelfTest) return RunPhase1SelfTest();
            if (phase1) return RunPhase1Windowed(maxFrames);
            return selfTest ? RunSelfTest() : RunWindowed(maxFrames);
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

    private static int RunPhase1Windowed(int maxFrames)
    {
        ApplicationConfiguration.Initialize();
        Phase1Scene built = Phase1Scene.Build(Width, Height);

        using var form = new Form
        {
            Text = "RayTracer.Gpu — Phase 1 (fullbright spectral)",
            ClientSize = new Size(Width, Height),
            FormBorderStyle = FormBorderStyle.FixedSingle,
            MaximizeBox = false,
        };

        using var renderer = new Phase1Renderer(Width, Height, built.Packed, built.Spectral, built.Camera);
        bool running = true;
        form.FormClosed += (_, _) => running = false;

        form.Show();
        renderer.Initialize(form.Handle);
        form.Text = $"RayTracer.Gpu — Phase 1 — {renderer.AdapterName}";

        uint frame = 0;
        while (running && form.Created)
        {
            renderer.RenderFrame(reset: frame == 0);
            frame++;
            Application.DoEvents();

            if (maxFrames > 0 && frame >= (uint)maxFrames)
            {
                Console.WriteLine($"Rendered {frame} Phase 1 frame(s) to the swap chain without error.");
                break;
            }
        }

        return 0;
    }

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

    // maxFrames == 0 means run until the window is closed.
    private static int RunWindowed(int maxFrames)
    {
        ApplicationConfiguration.Initialize();

        using var form = new Form
        {
            Text = "RayTracer.Gpu — Phase 0 (inline RayQuery)",
            ClientSize = new Size(Width, Height),
            FormBorderStyle = FormBorderStyle.FixedSingle,
            MaximizeBox = false,
        };

        using var renderer = new GpuRayTracer(Width, Height);
        bool initialized = false;
        bool running = true;
        form.FormClosed += (_, _) => running = false;

        form.Show();
        renderer.Initialize(form.Handle);
        initialized = true;
        form.Text = $"RayTracer.Gpu — Phase 0 — {renderer.AdapterName}";

        uint frame = 0;
        while (running && form.Created)
        {
            if (initialized)
                renderer.RenderFrame(frame++);
            Application.DoEvents();

            if (maxFrames > 0 && frame >= (uint)maxFrames)
            {
                Console.WriteLine($"Rendered {frame} frame(s) to the swap chain without error.");
                break;
            }
        }

        return 0;
    }
}
