using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using RayTracer;
using RayTracer.Gpu;
using Pinball.Content;

namespace Pinball.App;

/// <summary>
/// The P0 static-table golden regression (pinball-plan §5.3 / §8 P0) — the pinball analogue of the maze's
/// <c>--phase6-regress</c>. Converges <see cref="PinballTableScene"/> through the shipping Phase 6 path and
/// compares (or with <c>--update</c>, rewrites) the committed <c>Regression/golden/table.png</c>. Longer
/// converge than the maze goldens (the mirror ball / dispersive gem / thin-film are hero-λ and noisier);
/// same-GPU determinism keeps it bit-exact so the tight tolerance holds. Runs on the local RTX 3070.
/// </summary>
internal static class TableRegression
{
    private const int Width = 1280, Height = 720;
    private const int TableConvergeFrames = 256;
    private const double MinWithinTolFraction = 0.995; // ≥99.5% of channels within…
    private const int WithinTol = 2;                    // …2/255, and
    private const double MaxMeanAbs = 1.0;              // mean |Δ| < 1.0/255.

    /// <summary>Runs the table regression; returns 0 on pass/update, non-zero on failure.</summary>
    public static int Run(bool update)
    {
        Console.WriteLine($"Space Cadet RT — table golden {(update ? "update" : "regress")}");
        byte[] img = RenderTableBeauty();
        string path = System.IO.Path.Combine(DefaultGoldenDir(), "table.png");

        if (update)
        {
            Program.SavePng(img, Width, Height, path);
            Console.WriteLine($"  wrote table -> {path}");
            return 0;
        }
        if (!System.IO.File.Exists(path))
        {
            Console.WriteLine($"  table: MISSING golden ({path}) — run --table-regress --update");
            return 1;
        }

        byte[] golden = LoadPngRgba(path, out int gw, out int gh);
        if (gw != Width || gh != Height)
        {
            Console.WriteLine($"  table: size mismatch (golden {gw}x{gh}) — FAIL");
            return 1;
        }

        (double within, double meanAbs, int maxDiff) = Compare(img, golden);
        bool pass = within >= MinWithinTolFraction && meanAbs < MaxMeanAbs;
        Console.WriteLine($"  table: within {WithinTol}/255 {within:P2}, mean|Δ| {meanAbs:0.00}, max {maxDiff} [{(pass ? "ok" : "REGRESSED")}]");
        return pass ? 0 : 1;
    }

    private static byte[] RenderTableBeauty()
    {
        PinballTableScene table = PinballTableScene.Build(Width, Height);
        VolumetricOptions volumetrics = VolumetricOptions.FromQuality(VolumetricQuality.Medium, SmokeMode.None);
        using var renderer = new Phase6Renderer(
            Width, Height, table.Packed, table.Spectral, table.PackedLights, table.Camera,
            volumetrics, lightingMode: LightingMode.NEE, sampleClamp: 3f,
            maxSampleCount: 4096, subPixelJitter: true, debugMode: Phase5DebugMode.Beauty,
            decalAtlas: Phase6Renderer.EmptyDecalAtlas);
        renderer.Initialize(windowHandle: 0);
        renderer.SetCamera(table.Camera);
        for (int f = 0; f < TableConvergeFrames; f++)
            renderer.RenderHeadlessFrame(reset: f == 0, moving: false);
        return renderer.ReadbackOutput();
    }

    // Walks up from the executable to the Pinball.App project dir so the golden lives in the source tree.
    private static string DefaultGoldenDir()
    {
        System.IO.DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "Pinball.App.csproj")))
            dir = dir.Parent;
        string root = dir?.FullName ?? AppContext.BaseDirectory;
        return System.IO.Path.Combine(root, "Regression", "golden");
    }

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

    private static byte[] LoadPngRgba(string path, out int width, out int height)
    {
        using var bmp = new System.Drawing.Bitmap(path);
        width = bmp.Width;
        height = bmp.Height;
        var rect = new System.Drawing.Rectangle(0, 0, width, height);
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
                    rgba[d + 0] = row[s + 2];
                    rgba[d + 1] = row[s + 1];
                    rgba[d + 2] = row[s + 0];
                    rgba[d + 3] = 255;
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
