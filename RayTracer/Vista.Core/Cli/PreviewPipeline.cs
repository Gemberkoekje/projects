using Vista.Output;
using Vista.Rendering;
using Vista.World;

namespace Vista.Cli;

/// <summary>
/// The headless preview, end to end except for the GPU (vista-plan §7.4): one render through a fresh <see cref="PreviewSession"/>,
/// which builds every stage. Renders that share stages — a contact sheet's cells, a watch session — run through one session
/// instead (vista-plan §10 M1.8).
/// </summary>
public static class PreviewPipeline
{
    /// <summary>
    /// Runs <paramref name="request"/> in a fresh session; returns the sidecar that was written. A request that denoises needs a
    /// <paramref name="denoiser"/>; with no <paramref name="renderer"/> only a dry run can run.
    /// </summary>
    public static Sidecar Run(PreviewRequest request, IVistaRenderer? renderer, TimeProvider? time = null, IDenoiser? denoiser = null,
        ITerrainEroder? eroder = null, ErosionCache? erosionCache = null) =>
        new PreviewSession(renderer, denoiser, eroder, erosionCache, time).Run(request);

    /// <summary>The renderer outputs <paramref name="debug"/> needs (the tier map is derived from material ids).</summary>
    public static IReadOnlySet<Aov> AovsFor(IEnumerable<DebugOutput> debug)
    {
        ArgumentNullException.ThrowIfNull(debug);
        var aovs = new HashSet<Aov>();
        foreach (DebugOutput d in debug)
        {
            aovs.Add(d switch
            {
                DebugOutput.Albedo => Aov.Albedo,
                DebugOutput.Normal => Aov.Normal,
                DebugOutput.Depth => Aov.Depth,
                DebugOutput.Material or DebugOutput.Tier => Aov.MaterialId,
                DebugOutput.Variance => Aov.Variance,
                _ => throw new ArgumentOutOfRangeException(nameof(debug), d, "Unknown debug output."),
            });
        }
        return aovs;
    }
}
