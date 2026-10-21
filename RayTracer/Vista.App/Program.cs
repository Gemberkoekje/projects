using System.Globalization;
using System.Text;
using System.Text.Json;
using Vista.App.Denoise;
using Vista.App.Gpu;
using Vista.Cli;
using Vista.Output;
using Vista.Rendering;
using Vista.World;

namespace Vista.App;

/// <summary>
/// Vista entry point (vista-plan §7.4). Runs the headless preview CLI — on the RTX 3070 through the offline
/// integrator, or without a GPU with <c>--dry-run</c> — the contact sheets (<c>--sheet</c> renders variants,
/// <c>--sheet-from</c> tiles existing images), watch mode (<c>--watch</c> renders again whenever its files change, M1.8), and
/// the dev-box checks: <c>--watch-check</c> (a watch changed step by step against cold renders), <c>--gpu-selftest</c> and
/// <c>--atmosphere-check</c> (the LUT air against the reference atmosphere, by direction and segment) and
/// <c>--atmosphere-compare</c> (one view rendered with both, by distance), <c>--sampling-compare</c> (one view
/// rendered with M1.4's estimator and with MIS: agreement, noise and cost), and <c>--denoise-compare</c> (one view at low
/// samples denoised every way, against a reference: error, cost and determinism), and <c>--mesh-measure</c> (the terrain mesh of
/// fixed views: triangles, memory, build time and cost, M1.9).
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
        if (args.FirstOrDefault() == AtmosphereCompare.Flag)
            return RunGpu(() => AtmosphereCompare.Run(args, Console.Out));
        if (args.FirstOrDefault() == SamplingCompare.Flag)
            return RunGpu(() => SamplingCompare.Run(args, Console.Out));
        if (args.FirstOrDefault() == DenoiseCompare.Flag)
            return RunGpu(() => DenoiseCompare.Run(args, Console.Out));
        if (args.FirstOrDefault() == ErosionMeasure.Flag)
            return RunGpu(() => ErosionMeasure.Run(args, Console.Out));
        if (args.FirstOrDefault() == MeshMeasure.Flag)
            return RunGpu(() => MeshMeasure.Run(args, Console.Out));

        if (args.FirstOrDefault() == WatchCheck.Flag)
            return WatchCheck.Run(args, Console.Out);

        try
        {
            return args.FirstOrDefault() switch
            {
                "--sheet" => Sheet(args),
                "--sheet-from" => SheetFrom(args),
                "--watch" => Watch(args),
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
            return Report(PreviewPipeline.Run(request, renderer: null, erosionCache: ErosionCache.Default));

        return RunGpu(() =>
        {
            using var renderer = new D3D12VistaRenderer();
            using var denoiser = new OidnDenoiser();
            return Report(PreviewPipeline.Run(request, renderer, denoiser: denoiser, eroder: renderer, erosionCache: ErosionCache.Default));
        });
    }

    private static int Sheet(string[] args)
    {
        if (!SheetCommandLine.TryParse(args, out SheetRequest? request, out IReadOnlyList<string> errors))
            return Usage(errors);

        if (request!.Base.DryRun)
            return Report(SheetPipeline.Render(request, renderer: null, erosionCache: ErosionCache.Default));

        return RunGpu(() =>
        {
            using var renderer = new D3D12VistaRenderer();
            using var denoiser = new OidnDenoiser();
            return Report(SheetPipeline.Render(request, renderer, denoiser: denoiser, eroder: renderer, erosionCache: ErosionCache.Default));
        });
    }

    /// <summary>
    /// Watch mode (vista-plan §10 M1.8): one renderer, denoiser and session for the life of the process, rendering again whenever
    /// the inputs change. Typing <c>quit</c> or pressing Ctrl+C stops it after the render in progress (Ctrl+C twice: at once).
    /// </summary>
    private static int Watch(string[] args)
    {
        if (!WatchCommandLine.TryParse(args, out WatchRequest? request, out IReadOnlyList<string> errors))
            return Usage(errors);

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            if (stop.IsCancellationRequested)
                return;
            e.Cancel = true;
            stop.Cancel();
            Console.Error.WriteLine("Stopping after this render (Ctrl+C again stops at once).");
        };
        var input = new Thread(() => ReadCommands(stop)) { IsBackground = true, Name = "watch commands" };
        input.Start();

        if (request!.DryRun)
            return Watch(new PreviewWatch(request, new PreviewSession(renderer: null, erosionCache: ErosionCache.Default)), stop.Token);
        return RunGpu(() =>
        {
            using var renderer = new D3D12VistaRenderer();
            using var denoiser = new OidnDenoiser();
            var session = new PreviewSession(renderer, denoiser, eroder: renderer, erosionCache: ErosionCache.Default);
            return Watch(new PreviewWatch(request, session), stop.Token);
        });
    }

    private static int Watch(PreviewWatch watch, CancellationToken stop)
    {
        string watched = string.Empty;
        watch.Run(step =>
        {
            switch (step.Kind)
            {
                case WatchEventKind.Rendered:
                    Report(step.Sidecar!);
                    Console.WriteLine(WatchLine(step.Sidecar!));
                    break;
                case WatchEventKind.Failed:
                    Console.Error.WriteLine($"error: {step.Message}");
                    Console.Error.WriteLine("Still watching: fix the file and save it.");
                    break;
                default:
                    Console.Error.WriteLine($"waiting: {step.Message}");
                    break;
            }
            string now = string.Join(", ", watch.Watched.Select(path => Path.GetRelativePath(Environment.CurrentDirectory, path)));
            if (now != watched)
            {
                watched = now;
                Console.WriteLine($"Watching {now}. Type quit or press Ctrl+C to stop.");
            }
        }, stop);
        Console.WriteLine($"Watch ended after {watch.Renders} render{(watch.Renders == 1 ? "" : "s")}.");
        return 0;
    }

    /// <summary>The line that ends each watch render's report: its number, what changed, its wall time and the stages it built and reused.</summary>
    internal static string WatchLine(Sidecar sidecar)
    {
        WatchInfo watch = sidecar.Watch!;
        static string Names(IReadOnlyList<PreviewStage> stages) => stages.Count == 0
            ? "nothing"
            : string.Join(", ", stages.Select(s => JsonNamingPolicy.CamelCase.ConvertName(s.ToString())));
        string changes = watch.Changes.Count == 0 ? (watch.Render == 1 ? "first render" : "no field changed") : string.Join("; ", watch.Changes);
        return string.Create(CultureInfo.InvariantCulture,
            $"watch #{watch.Render}: {changes} | {watch.RunSeconds:0.000} s (+{watch.WaitSeconds:0.000} s settling) | built {Names(sidecar.Stages.Built)} | reused {Names(sidecar.Stages.Reused)}");
    }

    /// <summary>Reads the watch's commands from the console: <c>quit</c> stops it. A closed input (no console) just ends the reading.</summary>
    private static void ReadCommands(CancellationTokenSource stop)
    {
        try
        {
            while (Console.In.ReadLine() is { } line)
            {
                string command = line.Trim();
                if (command is "quit" or "q" or "exit")
                {
                    stop.Cancel();
                    return;
                }
                if (command.Length > 0)
                    Console.Error.WriteLine("Type quit to stop. To render something else, edit the recipe, the camera or the options file.");
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // No console to read from: Ctrl+C still stops the watch.
        }
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
        Console.Error.WriteLine(WatchCommandLine.Usage);
        Console.Error.WriteLine(WatchCheck.Usage);
        Console.Error.WriteLine($"       Vista.App {SelfTestFlag}   (determinism and LUT checks on the GPU)");
        Console.Error.WriteLine(AtmosphereCheck.Usage);
        Console.Error.WriteLine(AtmosphereCompare.Usage);
        Console.Error.WriteLine(SamplingCompare.Usage);
        Console.Error.WriteLine(DenoiseCompare.Usage);
        Console.Error.WriteLine(ErosionMeasure.Usage);
        Console.Error.WriteLine(MeshMeasure.Usage);
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
        catch (InsufficientMemoryException e)
        {
            Console.Error.WriteLine($"error: {e.Message}");
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
            string reading = (exposure.Incident, exposure.Average) switch
            {
                ({ } card, _) => string.Create(CultureInfo.InvariantCulture,
                    $"incident: card at {card.CardElevationDegrees:0.0}° gets {card.IlluminanceLux:G3} lux (sun {card.SunLux:G3}, sky {card.SkyLux:G3})"),
                (_, { } average) => string.Create(CultureInfo.InvariantCulture, $"average: {average.LogAverageLuminanceCdM2:G3} cd/m²"),
                _ => "none",
            };
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"exposure: {reading} (EV100 {(exposure.Ev100 is double ev ? ev.ToString("0.0", CultureInfo.InvariantCulture) : "none: no light")}), " +
                $"scaled by {exposure.AppliedEv:+0.0;-0.0} EV, display {sidecar.Image.Display.ToString().ToLowerInvariant()}; atmosphere {sidecar.Atmosphere.Mode.ToString().ToLowerInvariant()}"));
        }
        if (sidecar.World.Erosion is { } erosion)
        {
            string how = erosion.Source switch
            {
                ErosionSource.Eroded when erosion.Stats is { } st => string.Create(CultureInfo.InvariantCulture,
                    $"eroded on {erosion.Device} in {st.Seconds:0.00} s GPU ({st.WallSeconds:0.00} s wall, {st.StateBytes / 1048576.0:0} MiB of state)"),
                ErosionSource.Cache => $"read from the cache (eroded on {erosion.Device}, driver {erosion.Driver}, {erosion.ErodedUtc})",
                _ => erosion.Source.ToString().ToLowerInvariant(),
            };
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"world: {sidecar.World.TerrainProfile}, {sidecar.World.GridSize}² × {sidecar.World.CellSizeMetres} m, {how}; hash {sidecar.World.HeightFieldHash}"));
        }
        if (sidecar.World.Mesh is { } mesh)
        {
            string kind = mesh.Kind == MeshKind.View ? string.Create(CultureInfo.InvariantCulture, $"view-dependent at {mesh.ErrorPixels:0.##} px") : "uniform";
            string hierarchy = mesh.HierarchySeconds > 0.0
                ? string.Create(CultureInfo.InvariantCulture, $" (and {mesh.HierarchySeconds:0.00} s for the world's hierarchy)")
                : string.Empty;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"mesh: {kind}, {mesh.Triangles:N0} triangles ({mesh.DetailTriangles:N0} below the grid, {mesh.FarTriangles:N0} beyond it), " +
                $"{mesh.Bytes / 1048576.0:0.0} MiB, reach {mesh.ReachMetres / 1000.0:0} km; {mesh.RefineSeconds + mesh.VertexSeconds:0.00} s{hierarchy}"));
        }
        if (sidecar.Render is { } render)
        {
            RenderStats s = render.Stats;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"rendered on {sidecar.Engine.Device}: {s.RaysTraced:N0} rays, {render.RaysPerSample:0.00} per sample, " +
                $"{render.RaysPerSecond / 1e6:0.0} Mrays/s; {s.Dispatches} dispatches, {s.TraceSeconds:0.000} s GPU, " +
                $"longest dispatch {s.LongestDispatchSeconds * 1e3:0.0} ms"));
        }
        if (sidecar.Denoise?.Run is { } denoise)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"denoised by {denoise.Library} {denoise.Version} on {denoise.Device}: {denoise.Filter}; " +
                $"{denoise.CommitSeconds * 1e3:0} ms to prepare, {denoise.ExecuteSeconds * 1e3:0} ms to filter"));
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
