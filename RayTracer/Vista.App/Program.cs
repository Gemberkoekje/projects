using Vista.Cli;

namespace Vista.App;

/// <summary>
/// Vista entry point (vista-plan §7.4). M0 runs the headless preview CLI; <c>--dry-run</c> works today, the
/// GPU path waits for the offline integrator v0 and its D3D12 host, which need the RTX 3070 to build and
/// validate (vista-plan §10, M0).
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (!PreviewCommandLine.TryParse(args, out PreviewRequest? request, out IReadOnlyList<string> errors))
        {
            foreach (string error in errors)
                Console.Error.WriteLine(error);
            Console.Error.WriteLine(PreviewCommandLine.Usage);
            return 1;
        }

        if (!request!.DryRun)
        {
            Console.Error.WriteLine(
                "The GPU renderer (offline integrator v0) is not built yet: it is open M0 work that needs the RTX 3070. " +
                "Use --dry-run to generate the world and write the sidecar and height map.");
            return 2;
        }

        Output.Sidecar sidecar = PreviewPipeline.Run(request, renderer: null);
        foreach ((string role, string file) in sidecar.Outputs)
            Console.WriteLine($"{role}: {file}");
        foreach (string warning in sidecar.Warnings)
            Console.Error.WriteLine($"warning: {warning}");
        return 0;
    }
}
