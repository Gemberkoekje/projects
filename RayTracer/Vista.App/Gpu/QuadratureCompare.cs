using System.Globalization;
using Vista.Cli;
using Vista.Output;
using Vista.Rendering;
using Vista.World;

namespace Vista.App.Gpu;

/// <summary>
/// <c>Vista.App --quadrature-compare</c> (vista-plan §10 M1.12a verify): one view rendered through the engine's wavelength table and
/// through Vista's own bins, each as the preview renders it (its own incident meter, its files beside the output), and how far apart
/// their colours are: a map of the ΔE₇₆ of every pixel (each judged at the luminance of white, as <see cref="QuadratureCheck"/> judges
/// a spectrum), the mean of those, the ΔE₇₆ of the frame's mean colour and which way it moved, the exposure each meter picked, and
/// the share of pixels whose 8-bit PNG (undithered) has its red channel at 0. It takes the preview's own options, but for
/// <c>--quadrature</c>:
/// <code>
/// Vista.App --quadrature-compare --recipe r.json --seed 42 [--camera c.json] --out dir/view.png [any other --preview option]
/// </code>
/// It writes <c>view.engine.*</c> and <c>view.bins.*</c> (PNG, EXR, sidecar) and <c>view.deltaE.png</c>, and first prints the shift the
/// CI's spectra predict for each light (the engine's table against the bins).
/// </summary>
internal static class QuadratureCompare
{
    /// <summary>The flag that selects this mode.</summary>
    public const string Flag = "--quadrature-compare";

    /// <summary>The ΔE₇₆ the map shows as white; 0 is black.</summary>
    public const double MapTop = 6.0;

    /// <summary>Usage text.</summary>
    public const string Usage =
        "       Vista.App --quadrature-compare --recipe r.json --seed 42 [--camera c.json] --out dir/view.png [other --preview options]\n" +
        "                 (the engine's hero wavelengths against Vista's bins: a dE76 map, the mean shift, both exposures)";

    /// <summary>Runs the comparison; returns 0, or 1 for bad arguments.</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter log)
    {
        if (args.Contains("--quadrature") || args.Contains("--dry-run"))
        {
            Console.Error.WriteLine("--quadrature-compare chooses the quadrature itself and renders: leave out --quadrature and --dry-run.");
            Console.Error.WriteLine(Usage);
            return 1;
        }
        if (!PreviewCommandLine.TryParse(["--preview", .. args.Skip(1)], out PreviewRequest? parsed, out IReadOnlyList<string> errors))
        {
            foreach (string error in errors)
                Console.Error.WriteLine(error);
            Console.Error.WriteLine(Usage);
            return 1;
        }
        PreviewRequest request = parsed!;
        string directory = Path.GetDirectoryName(Path.GetFullPath(request.OutPath))!;
        string name = Path.GetFileNameWithoutExtension(request.OutPath);

        log.WriteLine("The shift the CI's spectra predict, the engine's table against the bins (dE76 at the luminance of white):");
        foreach (QuadratureCase spectrum in QuadratureCheck.Cases)
        {
            QuadratureError shift = QuadratureCheck.Difference(WavelengthTable.Bins.Integrate(spectrum.Spectrum), WavelengthTable.Engine.Integrate(spectrum.Spectrum));
            log.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {spectrum.Name,-32} {shift.DeltaE76,5:0.00}  a* {shift.DeltaA,6:+0.00;-0.00}  b* {shift.DeltaB,6:+0.00;-0.00}  Y {shift.LuminanceError,7:+0.00%;-0.00%}"));
        }

        using var renderer = new D3D12VistaRenderer();
        log.WriteLine($"Device: {renderer.DeviceName}");
        var session = new PreviewSession(renderer, eroder: renderer, erosionCache: ErosionCache.Default);
        (Sidecar engine, float[] engineXyz) = Render(session, request with { Quadrature = Quadrature.Engine, OutPath = Path.Combine(directory, name + ".engine.png") });
        (Sidecar bins, float[] binsXyz) = Render(session, request with { Quadrature = Quadrature.Bins, OutPath = Path.Combine(directory, name + ".bins.png") });
        PixelWindow window = request.Loupe.IsEmpty ? PixelWindow.Full(request.Width, request.Height) : request.Loupe;

        int pixels = window.Pixels, counted = 0;
        double sumDe = 0.0;
        (double X, double Y, double Z) meanEngine = (0.0, 0.0, 0.0), meanBins = (0.0, 0.0, 0.0);
        var map = new byte[pixels * 3];
        for (int p = 0; p < pixels; p++)
        {
            (double X, double Y, double Z) b = (binsXyz[p * 3], binsXyz[p * 3 + 1], binsXyz[p * 3 + 2]);
            (double X, double Y, double Z) e = (engineXyz[p * 3], engineXyz[p * 3 + 1], engineXyz[p * 3 + 2]);
            meanBins = (meanBins.X + b.X, meanBins.Y + b.Y, meanBins.Z + b.Z);
            meanEngine = (meanEngine.X + e.X, meanEngine.Y + e.Y, meanEngine.Z + e.Z);
            if (!(b.Y > 0.0))
                continue;
            double de = QuadratureCheck.Difference(b, e).DeltaE76;
            sumDe += de;
            counted++;
            (map[p * 3], map[p * 3 + 1], map[p * 3 + 2]) = Ramp(de / MapTop);
        }
        PngEncoder.Write(Path.Combine(directory, name + ".deltaE.png"), map, window.Width, window.Height, PngColor.Rgb);
        QuadratureError frame = QuadratureCheck.Difference(meanBins, meanEngine);

        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{name}: dE76 per pixel {sumDe / Math.Max(counted, 1):0.00} mean; the frame's mean colour {frame.DeltaE76:0.00} " +
            $"(engine against bins: a* {frame.DeltaA:+0.00;-0.00}, b* {frame.DeltaB:+0.00;-0.00}, Y {frame.LuminanceError:+0.00%;-0.00%})"));
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{name}: exposure {engine.Exposure!.AppliedEv:0.000} EV engine, {bins.Exposure!.AppliedEv:0.000} EV bins " +
            $"({bins.Exposure.AppliedEv - engine.Exposure.AppliedEv:+0.000;-0.000} EV, the card's light {Math.Pow(2.0, engine.Exposure.AppliedEv - bins.Exposure.AppliedEv) - 1.0:+0.00%;-0.00%})"));
        (double engineRed, double engineAny) = AtZero(engineXyz, engine.Exposure.AppliedEv, request);
        (double binsRed, double binsAny) = AtZero(binsXyz, bins.Exposure.AppliedEv, request);
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{name}: red at 0 in the undithered PNG {engineRed:0.0%} engine, {binsRed:0.0%} bins; any channel at 0 {engineAny:0.0%} engine, {binsAny:0.0%} bins"));
        return 0;
    }

    /// <summary>Runs one preview of the session and returns its sidecar and the XYZ it rendered.</summary>
    private static (Sidecar Sidecar, float[] Xyz) Render(PreviewSession session, PreviewRequest request)
    {
        Sidecar sidecar = session.Run(request);
        return (sidecar, (float[])session.LastRender!.Xyz.Clone());
    }

    /// <summary>The shares of pixels whose undithered 8-bit PNG has its red channel at 0, and any channel at 0.</summary>
    private static (double Red, double Any) AtZero(float[] xyz, double ev, PreviewRequest request)
    {
        byte[] rgb = DisplayTransform.ToSrgb8(DisplayTransform.XyzToLinearRec709(xyz), ev, request.Display, request.Look);
        int pixels = rgb.Length / 3, red = 0, any = 0;
        for (int p = 0; p < pixels; p++)
        {
            if (rgb[p * 3] == 0)
                red++;
            if (rgb[p * 3] == 0 || rgb[p * 3 + 1] == 0 || rgb[p * 3 + 2] == 0)
                any++;
        }
        return ((double)red / pixels, (double)any / pixels);
    }

    /// <summary>The map's colour at <paramref name="t"/> of <see cref="MapTop"/>: black, blue, magenta, orange, then white at the top.</summary>
    private static (byte R, byte G, byte B) Ramp(double t)
    {
        double x = Math.Clamp(t, 0.0, 1.0) * 4.0;
        int i = Math.Min((int)Math.Floor(x), 3);
        double f = x - i;
        byte Channel(int c) => (byte)Math.Round(255.0 * (RampStops[i * 3 + c] + f * (RampStops[(i + 1) * 3 + c] - RampStops[i * 3 + c])));
        return (Channel(0), Channel(1), Channel(2));
    }

    // The ramp's five colours, RGB from 0 to 1, evenly over 0 to MapTop.
    private static readonly double[] RampStops = [0, 0, 0, 0.1, 0.2, 0.75, 0.75, 0.2, 0.6, 1.0, 0.6, 0.1, 1.0, 1.0, 1.0];
}
