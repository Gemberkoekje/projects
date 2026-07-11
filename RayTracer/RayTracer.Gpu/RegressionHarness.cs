using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using RayTracer;

namespace RayTracer.Gpu;

/// <summary>
/// Phase 6.3 — golden-image regression harness (plan Testing §6, layer 3). Renders a
/// deterministic scene (fixed maze / light seeds, the start camera, no motion, fog
/// phase 0) converged to a fixed sample count, then for <b>every</b> debug view
/// captures the resolved image and compares it against a committed golden PNG with a
/// perceptual tolerance — regressions hide in the AOV buffers, not just Beauty, so all
/// views are checked. Because the trace RNG is deterministic and the accumulation
/// order is fixed, the same GPU reproduces each image run-to-run, so the tolerance can
/// be tight (it only absorbs driver-level FP jitter, pitfall P4).
///
/// <para><c>--phase6-regress</c> compares (fails on any regression or missing golden);
/// <c>--phase6-regress --update</c> (re-)writes the goldens. Goldens live under
/// <c>RayTracer.Gpu/Regression/golden</c> by default (override with <c>--dir</c>).
/// It needs a DXR 1.1 GPU, so like the phase self-tests it is a dev-box tool, not CI.</para>
/// </summary>
internal static class RegressionHarness
{
    private const int Width = 1280;
    private const int Height = 720;
    private const int ConvergeFrames = 32;

    // Same-GPU determinism means near-identical images; these bounds only absorb
    // FP contraction / wave-order jitter (P4). A real regression blows past them.
    private const double MinWithinTolFraction = 0.995; // ≥99.5% of channels within…
    private const int WithinTol = 2;                    // …2/255, and
    private const double MaxMeanAbs = 1.0;              // mean |Δ| < 1.0/255.

    private static readonly Phase5DebugMode[] DebugModes =
        (Phase5DebugMode[])Enum.GetValues(typeof(Phase5DebugMode));

    public static int Run(bool update, string? dirOverride)
    {
        string dir = dirOverride ?? DefaultGoldenDir();
        Console.WriteLine($"RayTracer.Gpu — Phase 6 regression harness ({(update ? "UPDATE goldens" : "compare")})");
        Console.WriteLine($"  golden dir: {dir}");

        Phase3Scene built = Phase3Scene.Build(Width, Height); // fixed default seeds
        VolumetricOptions volumetrics = VolumetricOptions.FromQuality(VolumetricQuality.Medium, SmokeMode.Biome);

        using var renderer = new Phase6Renderer(
            Width, Height, built.Packed, built.Spectral, built.PackedLights, built.Camera,
            volumetrics, lightingMode: LightingMode.NEE, sampleClamp: 3f,
            maxSampleCount: 4096, subPixelJitter: true, debugMode: Phase5DebugMode.Beauty);
        renderer.Initialize(windowHandle: 0);
        renderer.SetFogTime(0f); // fixed fog phase → reproducible
        Console.WriteLine($"  adapter   : {renderer.AdapterName}");

        // Converge a still (mode does not affect the accumulation, only the resolve).
        for (int f = 0; f < ConvergeFrames; f++)
            renderer.RenderHeadlessFrame(reset: f == 0, moving: false);

        if (update)
            Directory.CreateDirectory(dir);

        int failures = 0, compared = 0;
        foreach (Phase5DebugMode mode in DebugModes)
        {
            renderer.DebugMode = mode;
            renderer.RenderHeadlessFrame(reset: false, moving: false);
            byte[] img = renderer.ReadbackOutput();
            string path = Path.Combine(dir, $"{mode}.png");

            if (update)
            {
                Program.SavePng(img, Width, Height, path);
                Console.WriteLine($"  wrote {mode,-16} -> {Path.GetFileName(path)}");
                continue;
            }

            if (!File.Exists(path))
            {
                Console.WriteLine($"  {mode,-16}: MISSING golden ({Path.GetFileName(path)}) — run --update");
                failures++;
                continue;
            }

            byte[] golden = LoadPngRgba(path, out int gw, out int gh);
            compared++;
            if (gw != Width || gh != Height)
            {
                Console.WriteLine($"  {mode,-16}: size mismatch (golden {gw}x{gh}) — FAIL");
                failures++;
                continue;
            }

            (double within, double meanAbs, int maxDiff) = Compare(img, golden);
            bool pass = within >= MinWithinTolFraction && meanAbs < MaxMeanAbs;
            Console.WriteLine($"  {mode,-16}: within {WithinTol}/255 {within:P2}, mean|Δ| {meanAbs:0.00}, max {maxDiff} " +
                $"[{(pass ? "ok" : "REGRESSED")}]");
            if (!pass) failures++;
        }

        if (update)
        {
            Console.WriteLine($"  wrote {DebugModes.Length} golden image(s). Commit them as the regression baseline.");
            return 0;
        }

        bool overall = failures == 0 && compared > 0;
        Console.WriteLine($"  overall              : {(overall ? "PASS" : "FAIL")} ({compared} compared, {failures} failing)");
        return overall ? 0 : 1;
    }

    // Fraction of RGB channels within WithinTol, mean absolute channel diff (0-255),
    // and the worst channel diff. Alpha is ignored.
    private static (double withinFraction, double meanAbs, int maxDiff) Compare(byte[] a, byte[] b)
    {
        int pixels = Math.Min(a.Length, b.Length) / 4;
        long within = 0, sum = 0;
        int maxDiff = 0;
        for (int i = 0; i < pixels; i++)
        {
            int o = i * 4;
            for (int c = 0; c < 3; c++)
            {
                int d = Math.Abs(a[o + c] - b[o + c]);
                if (d <= WithinTol) within++;
                sum += d;
                if (d > maxDiff) maxDiff = d;
            }
        }
        long channels = (long)pixels * 3;
        return (channels > 0 ? (double)within / channels : 0.0,
                channels > 0 ? (double)sum / channels : 0.0,
                maxDiff);
    }

    // Walks up from the executable to the project directory so the goldens live in the
    // source tree (version-controllable) rather than the wiped bin output.
    private static string DefaultGoldenDir()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RayTracer.Gpu.csproj")))
            dir = dir.Parent;
        string root = dir?.FullName ?? AppContext.BaseDirectory;
        return Path.Combine(root, "Regression", "golden");
    }

    // Reads a PNG into tightly-packed RGBA8 (GDI+ 32bppArgb is BGRA in memory).
    private static byte[] LoadPngRgba(string path, out int width, out int height)
    {
        using var bmp = new Bitmap(path);
        width = bmp.Width;
        height = bmp.Height;
        var rect = new Rectangle(0, 0, width, height);
        BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rgba = new byte[width * height * 4];
            var row = new byte[width * 4];
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, width * 4);
                for (int x = 0; x < width; x++)
                {
                    int s = x * 4, d = (y * width + x) * 4;
                    rgba[d + 0] = row[s + 2]; // R
                    rgba[d + 1] = row[s + 1]; // G
                    rgba[d + 2] = row[s + 0]; // B
                    rgba[d + 3] = 255;        // A
                }
            }
            return rgba;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
