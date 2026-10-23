using System.Numerics;
using RayTracer;
using Vista.Atmosphere;

namespace Vista.Rendering;

/// <summary>Which hero wavelengths the integrator traces and what each weighs in a pixel's colour (vista-plan §10 M1.12a).</summary>
public enum Quadrature
{
    /// <summary>Not chosen.</summary>
    None,
    /// <summary>
    /// Vista's own (<see cref="WavelengthTable.Bins"/>): 50 bins of equal x̄ + ȳ + z̄ mass, each wavelength at its bin's median and
    /// weighted by the bin's own integrals of x̄, ȳ and z̄, so colour integrates true.
    /// </summary>
    Bins,
    /// <summary>
    /// The engine's (<see cref="WavelengthTable.Engine"/>, the maze's <see cref="WavelengthLookup"/>): wavelengths where the functions'
    /// cumulative mass passes i/51, each weighted by the inverse of their sum at its own wavelength. Every colour carries a cast of
    /// 1.7–4.1 ΔE₇₆; kept so <c>main</c>'s images can be rendered again byte for byte.
    /// </summary>
    Engine,
}

/// <summary>
/// The integrator's hero wavelengths and their tristimulus weights (vista-plan §10 M1.12a): 50 entries in ascending wavelength,
/// which every spectral table of a render is laid out on (<see cref="IntegratorTables"/>, <see cref="AtmosphereTables"/>), so entry
/// <c>i</c> of one lines up with entry <c>i</c> of every other. A pixel-sample's XYZ is <c>Σₖ Cie[iₖ].xyz · L(λᵢₖ) / 4 ·
/// DeterministicCorrection</c> over its four companions, and 50 samples visit every entry four times, so over a cycle a pixel's
/// colour is <c>Σᵢ Cie[i].xyz · L(λᵢ) · DeterministicCorrection / 50</c>: a quadrature of the colour-matching integrals.
///
/// <para><see cref="Bins"/> cuts the CIE 1931 functions (<see cref="CieObserver"/>, 360–830 nm) into 50 bins of equal x̄ + ȳ + z̄
/// mass, each 1 nm sample's mass shared between the bins its interval straddles; each entry is the whole nanometre holding its bin's
/// median, weighted by the bin's own integrals of x̄, ȳ and z̄. The weights sum to the functions' whole integrals, so a flat spectrum
/// integrates exactly to equal-energy white, and a smooth one nearly so: each weight belongs to the wavelengths around its own. The
/// engine's table (<see cref="Engine"/>) weights each wavelength by the inverse of x̄ + ȳ + z̄ at that one point, which stands in for
/// its share of the mass only where x̄, ȳ and z̄ change alike across it.</para>
/// </summary>
public sealed class WavelengthTable
{
    /// <summary>Entries in a table (the atmosphere's LUTs and the shader are laid out for 50).</summary>
    public const int Count = 50;

    private static readonly Lazy<WavelengthTable> SharedBins = new(BuildBins);
    private static readonly Lazy<WavelengthTable> SharedEngine = new(BuildEngine);

    private WavelengthTable(Quadrature quadrature, int[] nanometres, float[] cie, float correction, double[] edges)
    {
        Quadrature = quadrature;
        Nanometres = nanometres;
        Cie = cie;
        DeterministicCorrection = correction;
        EdgesNm = edges;
        MsPositions = AtmosphereScattering.PositionsFor(i => nanometres[i]);
    }

    /// <summary>The default quadrature of every render (vista-plan §10 M1.12a).</summary>
    public const Quadrature DefaultQuadrature = Quadrature.Bins;

    /// <summary>Vista's own table: 50 bins of equal mass.</summary>
    public static WavelengthTable Bins => SharedBins.Value;

    /// <summary>The engine's table, as <see cref="SpectralResourceBaker"/> bakes it for the maze, to the bit.</summary>
    public static WavelengthTable Engine => SharedEngine.Value;

    /// <summary>The table of <paramref name="quadrature"/>.</summary>
    public static WavelengthTable For(Quadrature quadrature) => quadrature switch
    {
        Quadrature.Bins => Bins,
        Quadrature.Engine => Engine,
        _ => throw new ArgumentOutOfRangeException(nameof(quadrature), quadrature, "No wavelength table for this quadrature."),
    };

    /// <summary>Which quadrature this is.</summary>
    public Quadrature Quadrature { get; }

    /// <summary>The wavelength of each entry, nm, ascending.</summary>
    public IReadOnlyList<int> Nanometres { get; }

    /// <summary>
    /// The tristimulus weight and the wavelength of each entry, 4 floats per entry (x̄, ȳ, z̄, nm): the engine's <c>DeterXYZ</c>
    /// layout, which the shader reads as <c>Cie</c>.
    /// </summary>
    public float[] Cie { get; }

    /// <summary>Scales the weights' sum so a flat spectrum of 1 integrates to luminance 1 over a cycle.</summary>
    public float DeterministicCorrection { get; }

    /// <summary>
    /// The bins' edges, nm, 51 of them, where the functions' cumulative mass passes i/50 (each bin's entry lies between its two);
    /// empty for <see cref="Engine"/>, whose wavelengths have no bins.
    /// </summary>
    public IReadOnlyList<double> EdgesNm { get; }

    /// <summary>For each entry, its place among the multiple-scattering tables' wavelengths (<see cref="AtmosphereScattering.PositionsFor"/>).</summary>
    public IReadOnlyList<float> MsPositions { get; }

    /// <summary>The first wavelength, nm.</summary>
    public int FirstNm => Nanometres[0];

    /// <summary>The last wavelength, nm.</summary>
    public int LastNm => Nanometres[^1];

    /// <summary>
    /// The XYZ of <paramref name="spectrum"/> (a function of the wavelength in nm) through this quadrature, over a whole cycle: what a
    /// pixel converges to where every sample sees the same spectrum. Scaled as <see cref="CieObserver.Integrate"/>, so a flat 1 is
    /// luminance 1.
    /// </summary>
    public (double X, double Y, double Z) Integrate(Func<double, double> spectrum)
    {
        ArgumentNullException.ThrowIfNull(spectrum);
        double x = 0.0, y = 0.0, z = 0.0;
        for (int i = 0; i < Count; i++)
        {
            double s = spectrum(Nanometres[i]);
            x += Cie[i * 4] * s;
            y += Cie[i * 4 + 1] * s;
            z += Cie[i * 4 + 2] * s;
        }
        double scale = (double)DeterministicCorrection / Count;
        return (x * scale, y * scale, z * scale);
    }

    /// <summary>
    /// The linear-sRGB colour of a reflectance spectrum (pairs as <see cref="IntegratorTables.ReflectanceAt"/>) under light of equal
    /// energy, through this quadrature in the integrator's own single precision: the colour a material renders with, for its albedo.
    /// </summary>
    public (float R, float G, float B) LinearRgbOf(IReadOnlyList<double> spectrum)
    {
        ArgumentNullException.ThrowIfNull(spectrum);
        Vector3 xyz = Vector3.Zero;
        for (int i = 0; i < Count; i++)
            xyz += new Vector3(Cie[i * 4], Cie[i * 4 + 1], Cie[i * 4 + 2]) * (float)IntegratorTables.ReflectanceAt(spectrum, Nanometres[i]);
        xyz *= DeterministicCorrection / Count;
        float[] rgb = Output.DisplayTransform.XyzToLinearRec709([xyz.X, xyz.Y, xyz.Z]);
        return (rgb[0], rgb[1], rgb[2]);
    }

    private static WavelengthTable BuildEngine()
    {
        var lookup = new WavelengthLookup();
        if (lookup.DeterministicCount != Count)
            throw new InvalidOperationException($"The engine cycles {lookup.DeterministicCount} wavelengths; Vista's tables are laid out for {Count}.");
        SpectralResources spectral = SpectralResourceBaker.Bake(lookup, []);
        return new WavelengthTable(Quadrature.Engine, (int[])spectral.DeterWavelengths.Clone(), (float[])spectral.DeterXYZ.Clone(),
            spectral.DeterministicCorrection, []);
    }

    private static WavelengthTable BuildBins()
    {
        IReadOnlyList<double> x = CieObserver.X, y = CieObserver.Y, z = CieObserver.Z;
        int n = CieObserver.Count;

        // The cumulative mass at the edges of the 1 nm samples: sample i holds its mass evenly over [λᵢ − ½, λᵢ + ½].
        var cumulative = new double[n + 1];
        for (int i = 0; i < n; i++)
            cumulative[i + 1] = cumulative[i] + x[i] + y[i] + z[i];
        double total = cumulative[n];

        var nanometres = new int[Count];
        var cie = new float[Count * 4];
        var edges = new double[Count + 1];
        double sumY = 0.0;
        int sample = 0;
        for (int k = 0; k < Count; k++)
        {
            double low = total * k / Count, high = total * (k + 1) / Count, median = total * (k + 0.5) / Count;
            double bx = 0.0, by = 0.0, bz = 0.0;
            for (int i = 0; i < n; i++)
            {
                double mass = cumulative[i + 1] - cumulative[i];
                if (mass <= 0.0)
                    continue;
                double share = (Math.Min(cumulative[i + 1], high) - Math.Max(cumulative[i], low)) / mass;
                if (share <= 0.0)
                    continue;
                bx += x[i] * share;
                by += y[i] * share;
                bz += z[i] * share;
            }
            while (sample < n - 1 && cumulative[sample + 1] <= median)
                sample++;
            nanometres[k] = CieObserver.FirstNm + sample;
            edges[k] = EdgeAt(cumulative, low);
            cie[k * 4] = (float)bx;
            cie[k * 4 + 1] = (float)by;
            cie[k * 4 + 2] = (float)bz;
            cie[k * 4 + 3] = nanometres[k];
            sumY += (float)by;
        }
        edges[Count] = EdgeAt(cumulative, total);
        return new WavelengthTable(Quadrature.Bins, nanometres, cie, (float)(Count / sumY), edges);
    }

    /// <summary>The wavelength, nm, where the cumulative mass reaches <paramref name="mass"/>, linear within a 1 nm sample.</summary>
    private static double EdgeAt(double[] cumulative, double mass)
    {
        int i = 0;
        while (i < cumulative.Length - 2 && cumulative[i + 1] < mass)
            i++;
        double width = cumulative[i + 1] - cumulative[i];
        double f = width > 0.0 ? Math.Clamp((mass - cumulative[i]) / width, 0.0, 1.0) : 0.0;
        return CieObserver.FirstNm + i - 0.5 + f;
    }
}
