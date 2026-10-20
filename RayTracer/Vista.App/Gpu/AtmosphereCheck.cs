using System.Globalization;
using System.Numerics;
using Vista.Atmosphere;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;

namespace Vista.App.Gpu;

/// <summary>
/// <c>Vista.App --atmosphere-check</c> (vista-plan §10 M1.3 verify): the LUT sky against the reference atmosphere
/// at chosen sun elevations, error per wavelength band, on the RTX 3070.
/// <code>
/// Vista.App --atmosphere-check [--sun-elevations 30,5,-2,-6] [--paths 65536] [--ground-altitude 1608]
///                              [--observer-height 2] [--out check.json]
/// </code>
/// The observer stands <c>--observer-height</c> above a ground sphere at <c>--ground-altitude</c> (Zermatt's valley
/// floor by default) under the default atmosphere; see <see cref="SkyCheck"/> for the directions and bands.
/// </summary>
internal static class AtmosphereCheck
{
    /// <summary>The flag that selects this mode.</summary>
    public const string Flag = "--atmosphere-check";

    /// <summary>Usage text.</summary>
    public const string Usage =
        "       Vista.App --atmosphere-check [--sun-elevations 30,5,-2,-6] [--paths 65536] [--ground-altitude 1608]\n" +
        "                 [--observer-height 2] [--below-horizon] [--ground-albedo 0.2] [--aerosol-depth 0.1] [--segments 1000,4000]\n" +
        "                 [--out check.json]  (the LUT sky, or with --segments the air along segments, against the reference)";

    private const int Chunks = 64;
    private const int PathsPerDispatch = 16;

    /// <summary>Runs the check; returns 0, or 1 for bad arguments.</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter log)
    {
        var problems = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 1; i < args.Count; i++)
        {
            if (i + 1 < args.Count && args[i] is "--sun-elevations" or "--paths" or "--ground-altitude" or "--observer-height" or "--out"
                    or "--ground-albedo" or "--aerosol-depth" or "--segments")
                values[args[i]] = args[++i];
            else if (args[i] == "--below-horizon")
                values[args[i]] = "true";
            else
                problems.Add($"Unknown or valueless argument '{args[i]}'.");
        }
        double[] elevations = values.TryGetValue("--sun-elevations", out string? list)
            ? list.Split(',').Select(v => Number(v, problems)).ToArray()
            : [.. SkyCheck.DefaultSunElevations];
        int paths = values.TryGetValue("--paths", out string? p) ? (int)Number(p, problems) : 65536;
        double ground = values.TryGetValue("--ground-altitude", out string? g) ? Number(g, problems) : 1608.0;
        double observer = values.TryGetValue("--observer-height", out string? o) ? Number(o, problems) : 2.0;
        double[] lengths = values.TryGetValue("--segments", out string? segments)
            ? segments.Split(',').Select(v => Number(v, problems)).ToArray()
            : [0.0];
        if (lengths.Any(l => !(l > 0.0)) && values.ContainsKey("--segments"))
            problems.Add("--segments takes positive lengths in metres.");
        if (paths < Chunks || paths % (Chunks * PathsPerDispatch) != 0)
            problems.Add($"--paths must be a positive multiple of {Chunks * PathsPerDispatch}.");
        if (problems.Count > 0)
        {
            foreach (string problem in problems)
                Console.Error.WriteLine(problem);
            Console.Error.WriteLine(Usage);
            return 1;
        }

        AtmosphereModel model = AtmosphereModel.Default;
        if (values.TryGetValue("--ground-albedo", out string? albedo))
            model = model with { GroundAlbedo = Number(albedo, problems) };
        if (values.TryGetValue("--aerosol-depth", out string? haze))
            model = model with { AerosolOpticalDepth550 = Number(haze, problems) };
        AtmosphereTables tables = AtmosphereTables.Build(model);
        float[] weights = SkyCheck.LuminanceWeights(IntegratorTables.Build(MaterialCatalog.M0));
        bool below = values.ContainsKey("--below-horizon");
        IReadOnlyList<Vector3> directions = [.. SkyCheck.Directions(below).Select(d => d.Direction)];

        using var renderer = new D3D12VistaRenderer();
        log.WriteLine($"Device: {renderer.DeviceName}");
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Observer {observer:0.#} m above a ground sphere at {ground:0} m; {directions.Count} directions × {AtmosphereLuts.Wavelengths} wavelengths × {paths} reference paths."));
        var results = new List<SkyCheckElevation>();
        foreach (double elevation in elevations)
        {
            foreach (double length in lengths)
            {
                // The terrain slices span the observer's height, so a segment's far end has a sky view to difference against.
                AtmosphereFrame frame = AtmosphereFrame.For(model, ground, SkyCheck.SunDirection(elevation), observer, 0.0, observer);
                SkyCheckRaw raw = renderer.RunSkyCheck(frame, directions, Chunks, paths / Chunks, PathsPerDispatch, length);
                SkyCheckElevation result = SkyCheck.Aggregate(elevation, raw.Sums, Chunks, paths / Chunks, tables.WavelengthsNm, weights, below)
                    with { SegmentLengthMetres = length };
                results.Add(result);
                string what = length > 0.0 ? string.Create(CultureInfo.InvariantCulture, $", segments of {length / 1000.0:0.###} km") : string.Empty;
                log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"sun {elevation:+0.#;-0.#;0}°{what}: {raw.Dispatches} dispatches, {raw.GpuSeconds:0.0} s GPU; luminance error mean {result.MeanAbsoluteLuminanceError:P2}, worst {result.WorstLuminanceError:+0.00%;-0.00%}"));
                foreach (SkyCheckBand band in result.Bands)
                {
                    log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"  {band.Band,-12} ({band.Wavelengths,2} λ): mean |error| {band.MeanAbsoluteError:P2}, worst {band.WorstError:+0.00%;-0.00%} at {band.WorstElevation:0.#}° up, {band.WorstAzimuth:0}° from the sun; reference noise {band.MeanStandardError:P2}"));
                }
            }
        }

        if (values.TryGetValue("--out", out string? outPath))
        {
            File.WriteAllText(outPath, VistaJson.Serialize(new AtmosphereCheckReport(
                renderer.DeviceName, ground, observer, paths, model, [.. tables.WavelengthsNm], results)));
            log.WriteLine($"report: {outPath}");
        }
        return 0;
    }

    private static double Number(string text, List<string> problems)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value))
            return value;
        problems.Add($"'{text}' is not a number.");
        return 0.0;
    }
}

/// <summary>What <c>--atmosphere-check --out</c> writes.</summary>
/// <param name="Device">The GPU.</param>
/// <param name="GroundAltitudeMetres">The ground sphere's altitude above sea level.</param>
/// <param name="ObserverHeightMetres">The observer's height above it.</param>
/// <param name="PathsPerValue">Reference paths per (direction, wavelength).</param>
/// <param name="Model">The atmosphere.</param>
/// <param name="WavelengthsNm">The hero wavelengths.</param>
/// <param name="Elevations">One comparison per sun elevation.</param>
internal sealed record AtmosphereCheckReport(
    string Device,
    double GroundAltitudeMetres,
    double ObserverHeightMetres,
    int PathsPerValue,
    AtmosphereModel Model,
    IReadOnlyList<float> WavelengthsNm,
    IReadOnlyList<SkyCheckElevation> Elevations);
