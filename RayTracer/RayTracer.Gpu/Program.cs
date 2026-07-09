using System.Windows.Forms;

namespace RayTracer.Gpu;

internal static class Program
{
    private const int Width = 1280;
    private const int Height = 720;

    [STAThread]
    private static int Main(string[] args)
    {
        bool selfTest = args.Contains("--selftest", StringComparer.OrdinalIgnoreCase);
        int maxFrames = ParseIntOption(args, "--frames", defaultValue: 0);

        try
        {
            return selfTest ? RunSelfTest() : RunWindowed(maxFrames);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FATAL: " + ex.Message);
            Console.Error.WriteLine(ex);
            if (!selfTest)
                MessageBox.Show(ex.ToString(), "RayTracer.Gpu — startup failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
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
