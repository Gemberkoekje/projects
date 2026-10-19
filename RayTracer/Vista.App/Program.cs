using System.Globalization;
using Vista.App.Gpu;
using Vista.Cli;
using Vista.Output;
using Vista.Rendering;

namespace Vista.App;

/// <summary>
/// Vista entry point (vista-plan §7.4). Runs the headless preview CLI — on the RTX 3070 through offline
/// integrator v0, or without a GPU with <c>--dry-run</c> — and the dev-box checks with <c>--gpu-selftest</c>.
/// </summary>
internal static class Program
{
    private const string SelfTestFlag = "--gpu-selftest";

    private static int Main(string[] args)
    {
        if (args is [SelfTestFlag])
            return RunGpu(() => GpuSelfTest.Run(Console.Out));

        if (!PreviewCommandLine.TryParse(args, out PreviewRequest? request, out IReadOnlyList<string> errors))
        {
            foreach (string error in errors)
                Console.Error.WriteLine(error);
            Console.Error.WriteLine(PreviewCommandLine.Usage);
            Console.Error.WriteLine($"       Vista.App {SelfTestFlag}   (determinism checks on the GPU)");
            return 1;
        }

        if (request!.DryRun)
            return Report(PreviewPipeline.Run(request, renderer: null));

        return RunGpu(() =>
        {
            using var renderer = new D3D12VistaRenderer();
            return Report(PreviewPipeline.Run(request, renderer));
        });
    }

    private static int RunGpu(Func<int> run)
    {
        try
        {
            return run();
        }
        catch (PlatformNotSupportedException e)
        {
            Console.Error.WriteLine($"Rendering needs a DXR 1.1 GPU: {e.Message} Use --dry-run to generate the world without one.");
            return 2;
        }
    }

    private static int Report(Sidecar sidecar)
    {
        foreach ((string role, string file) in sidecar.Outputs)
            Console.WriteLine($"{role}: {file}");
        if (sidecar.Render is { } render)
        {
            RenderStats s = render.Stats;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"rendered on {sidecar.Engine.Device}: {s.RaysTraced:N0} rays, {render.RaysPerSample:0.00} per sample, " +
                $"{render.RaysPerSecond / 1e6:0.0} Mrays/s; {s.Dispatches} dispatches, {s.TraceSeconds:0.000} s GPU, " +
                $"longest dispatch {s.LongestDispatchSeconds * 1e3:0.0} ms"));
        }
        foreach (string warning in sidecar.Warnings)
            Console.Error.WriteLine($"warning: {warning}");
        return 0;
    }
}
