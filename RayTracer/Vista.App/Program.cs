using System.Globalization;
using System.Text;
using System.Text.Json;
using Vista.App.Gpu;
using Vista.Cli;
using Vista.Output;
using Vista.Rendering;

namespace Vista.App;

/// <summary>
/// Vista entry point (vista-plan §7.4). Runs the headless preview CLI — on the RTX 3070 through the offline
/// integrator, or without a GPU with <c>--dry-run</c> — the contact sheets (<c>--sheet</c> renders variants,
/// <c>--sheet-from</c> tiles existing images), and the dev-box checks: <c>--gpu-selftest</c> and
/// <c>--atmosphere-check</c> (the LUT sky against the reference atmosphere).
/// </summary>
internal static class Program
{
    private const string SelfTestFlag = "--gpu-selftest";

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8; // sizes print as 640×360
        if (args is [SelfTestFlag])
            return RunGpu(() => GpuSelfTest.Run(Console.Out));
        if (args.FirstOrDefault() == AtmosphereCheck.Flag)
            return RunGpu(() => AtmosphereCheck.Run(args, Console.Out));

        try
        {
            return args.FirstOrDefault() switch
            {
                "--sheet" => Sheet(args),
                "--sheet-from" => SheetFrom(args),
                _ => Preview(args),
            };
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or IOException or UnauthorizedAccessException)
        {
            // A bad or missing input file: the message says which and why; a stack trace would only bury it.
            Console.Error.WriteLine($"error: {e.Message}");
            return 1;
        }
    }

    private static int Preview(string[] args)
    {
        if (!PreviewCommandLine.TryParse(args, out PreviewRequest? request, out IReadOnlyList<string> errors))
            return Usage(errors);

        if (request!.DryRun)
            return Report(PreviewPipeline.Run(request, renderer: null));

        return RunGpu(() =>
        {
            using var renderer = new D3D12VistaRenderer();
            return Report(PreviewPipeline.Run(request, renderer));
        });
    }

    private static int Sheet(string[] args)
    {
        if (!SheetCommandLine.TryParse(args, out SheetRequest? request, out IReadOnlyList<string> errors))
            return Usage(errors);

        if (request!.Base.DryRun)
            return Report(SheetPipeline.Render(request, renderer: null));

        return RunGpu(() =>
        {
            using var renderer = new D3D12VistaRenderer();
            return Report(SheetPipeline.Render(request, renderer));
        });
    }

    private static int SheetFrom(string[] args)
    {
        if (!SheetCommandLine.TryParseFrom(args, out SheetFromRequest? request, out IReadOnlyList<string> errors))
            return Usage(errors);
        return Report(SheetPipeline.FromFiles(request!));
    }

    private static int Usage(IReadOnlyList<string> errors)
    {
        foreach (string error in errors)
            Console.Error.WriteLine(error);
        Console.Error.WriteLine(PreviewCommandLine.Usage);
        Console.Error.WriteLine(SheetCommandLine.Usage);
        Console.Error.WriteLine($"       Vista.App {SelfTestFlag}   (determinism and LUT checks on the GPU)");
        Console.Error.WriteLine(AtmosphereCheck.Usage);
        return 1;
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
        SunInfo sun = sidecar.Sun;
        string daylight = sun.Sunrise is null ? sun.Daylight.ToString() : $"sunrise {sun.Sunrise}, sunset {sun.Sunset}";
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"sun: {sidecar.Place.Name} ({sidecar.Place.LatitudeDegrees:0.00}°), {sun.Date:yyyy-MM-dd} ({sun.DateFrom}), " +
            $"{sun.SolarTime} solar ({sun.SolarTimeFrom}); azimuth {sun.AzimuthDegrees:0.0}°, elevation {sun.ElevationDegrees:0.00}°; {daylight}"));
        if (sidecar.Exposure is { } exposure)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"exposure: metered {exposure.LogAverageLuminanceCdM2:G3} cd/m² (EV100 {exposure.Ev100:0.0}), scaled by {exposure.AppliedEv:+0.0;-0.0} EV; atmosphere {sidecar.Atmosphere.Mode.ToString().ToLowerInvariant()}"));
        }
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

    private static int Report(SheetSidecar sheet)
    {
        foreach ((string role, string file) in sheet.Outputs)
            Console.WriteLine($"{role}: {file}");
        Console.WriteLine($"{sheet.Cells.Count} cells, {sheet.Columns}×{sheet.Rows}, {sheet.Width}×{sheet.Height} px");
        foreach (SheetCellInfo cell in sheet.Cells)
            Console.WriteLine($"  {cell.Index}: {cell.Label} ({cell.Source})");
        foreach (string warning in sheet.Warnings)
            Console.Error.WriteLine($"warning: {warning}");
        return 0;
    }
}
