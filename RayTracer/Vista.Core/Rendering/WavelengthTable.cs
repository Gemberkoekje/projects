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
    /// <summary>
    /// <see cref="Bins"/> with more of the 50 in the red tail (vista-plan §10 M1.13, <see cref="WavelengthTable.RedTail"/>): 46 bins of
    /// equal mass up to <see cref="WavelengthTable.TailFromNm"/> and 4 sharing the mass beyond it, so the light alpenglow is made of has
    /// wavelengths of its own.
    /// </summary>
    RedTail,
    /// <summary>
    /// Bins of equal mass under a blend of white light and the deepest twilight sun the renders show (vista-plan §10 M1.13,
    /// <see cref="WavelengthTable.Blend"/>), each weighted by its bin's own integrals of the functions times the blend over the blend
    /// at its wavelength: an importance-sampled quadrature, exact for the blend itself. The default since M1.13.
    /// </summary>
    Blend,
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
    private static readonly Lazy<WavelengthTable> SharedRedTail = new(BuildRedTail);
    private static readonly Lazy<WavelengthTable> SharedBlend = new(BuildBlend);

    /// <summary>Where <see cref="RedTail"/>'s tail begins, nm: the bins' 49th edge of M1.12a, past which one wavelength stood for all.</summary>
    public const double TailFromNm = 650.0;

    /// <summary>The bins <see cref="RedTail"/> gives the tail.</summary>
    public const int TailBins = 4;

    /// <summary>
    /// The twilight sun <see cref="Blend"/> is tuned to, its elevation in degrees: 1.8° below the horizontal, about the last of the direct
    /// light on a 4000 m summit (vista-plan §10 M1.13).
    /// </summary>
    public const double BlendSunElevationDegrees = -1.8;

    /// <summary>The twilight sun's share of <see cref="Blend"/>'s light (the rest white), by colour-matching mass.</summary>
    public const double BlendTwilightShare = 0.3;

    /// <summary>The summit the blend's twilight sun lights, m above sea level.</summary>
    public const double BlendSummitMetres = 4000.0;

    private WavelengthTable(Quadrature quadrature, int[] nanometres, float[] cie, float correction, double[] edges)
    {
        Quadrature = quadrature;
        Nanometres = nanometres;
        Cie = cie;
        DeterministicCorrection = correction;
        EdgesNm = edges;
        MsPositions = AtmosphereScattering.PositionsFor(i => nanometres[i]);
    }

    /// <summary>
    /// The default quadrature of every render: Vista's bins since M1.12a, their blend with the twilight sun since M1.13 (vista-plan §10
    /// M1.13, the red end's decision), which integrates alpenglow's light within 1 ΔE₇₆ where the equal bins missed it by up to 6.7.
    /// </summary>
    public const Quadrature DefaultQuadrature = Quadrature.Blend;

    /// <summary>Vista's own table: 50 bins of equal mass.</summary>
    public static WavelengthTable Bins => SharedBins.Value;

    /// <summary>The engine's table, as <see cref="SpectralResourceBaker"/> bakes it for the maze, to the bit.</summary>
    public static WavelengthTable Engine => SharedEngine.Value;

    /// <summary>The red-tail table (vista-plan §10 M1.13): 46 bins of equal mass to 650 nm and 4 beyond.</summary>
    public static WavelengthTable RedTail => SharedRedTail.Value;

    /// <summary>The blend's table (vista-plan §10 M1.13): bins of equal mass under white light with 30% of the −1.8° twilight sun.</summary>
    public static WavelengthTable Blend => SharedBlend.Value;

    /// <summary>The table of <paramref name="quadrature"/>.</summary>
    public static WavelengthTable For(Quadrature quadrature) => quadrature switch
    {
        Quadrature.Bins => Bins,
        Quadrature.Engine => Engine,
        Quadrature.RedTail => RedTail,
        Quadrature.Blend => Blend,
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

    private static WavelengthTable BuildBins() => Build(Quadrature.Bins, null, total => k => total * k / Count);

    /// <summary>46 bins of equal mass to <see cref="TailFromNm"/>, then <see cref="TailBins"/> of equal mass beyond it.</summary>
    private static WavelengthTable BuildRedTail()
    {
        double tail = CumulativeMassAt(TailFromNm);
        int head = Count - TailBins;
        return Build(Quadrature.RedTail, null, total => k => k <= head ? tail * k / head : tail + (total - tail) * (k - head) / TailBins);
    }

    /// <summary>
    /// Bins of equal mass under white light and <see cref="BlendTwilightShare"/> of the twilight sun (<see cref="TwilightLight.Sun"/> at
    /// <see cref="BlendSunElevationDegrees"/>), the sun scaled to white's colour-matching mass.
    /// </summary>
    private static WavelengthTable BuildBlend()
    {
        IReadOnlyList<double> x = CieObserver.X, y = CieObserver.Y, z = CieObserver.Z;
        Func<double, double> sun = TwilightLight.Sun(BlendSunElevationDegrees, BlendSummitMetres).Spectrum;
        int n = CieObserver.Count;
        double white = 0.0, lit = 0.0;
        var light = new double[n];
        for (int i = 0; i < n; i++)
        {
            light[i] = sun(CieObserver.FirstNm + i);
            white += x[i] + y[i] + z[i];
            lit += (x[i] + y[i] + z[i]) * light[i];
        }
        var weight = new double[n];
        for (int i = 0; i < n; i++)
            weight[i] = 1.0 - BlendTwilightShare + BlendTwilightShare * light[i] * white / lit;
        return Build(Quadrature.Blend, weight, total => k => total * k / Count);
    }

    /// <summary>The functions' cumulative x̄ + ȳ + z̄ mass up to <paramref name="nm"/> (each 1 nm sample's spread over its own nanometre).</summary>
    private static double CumulativeMassAt(double nm)
    {
        double sum = 0.0;
        for (int i = 0; i < CieObserver.Count; i++)
        {
            double from = CieObserver.FirstNm + i - 0.5;
            double share = Math.Clamp(nm - from, 0.0, 1.0);
            sum += (CieObserver.X[i] + CieObserver.Y[i] + CieObserver.Z[i]) * share;
        }
        return sum;
    }

    /// <summary>
    /// A table of <see cref="Count"/> bins of the functions' mass times <paramref name="weight"/> (per 1 nm sample; null: 1), cut where
    /// the cumulative mass reaches <paramref name="edges"/>(total)(k) for k = 0…50 (its median at k + ½), each entry the whole nanometre holding its bin's
    /// median and weighted by the bin's own integrals of x̄, ȳ and z̄ times the weight, over the weight at that nanometre. With no
    /// weight that is M1.12a's bins, operation for operation.
    /// </summary>
    private static WavelengthTable Build(Quadrature quadrature, double[]? weight, Func<double, Func<double, double>> edges)
    {
        IReadOnlyList<double> x = CieObserver.X, y = CieObserver.Y, z = CieObserver.Z;
        int n = CieObserver.Count;

        // The cumulative mass at the edges of the 1 nm samples: sample i holds its mass evenly over [λᵢ − ½, λᵢ + ½].
        var cumulative = new double[n + 1];
        for (int i = 0; i < n; i++)
            cumulative[i + 1] = cumulative[i] + (weight is null ? x[i] + y[i] + z[i] : (x[i] + y[i] + z[i]) * weight[i]);
        double total = cumulative[n];
        Func<double, double> edge = edges(total);

        var nanometres = new int[Count];
        var cie = new float[Count * 4];
        var binEdges = new double[Count + 1];
        double sumY = 0.0;
        int sample = 0;
        for (int k = 0; k < Count; k++)
        {
            double low = edge(k), high = edge(k + 1), median = edge(k + 0.5);
            double bx = 0.0, by = 0.0, bz = 0.0;
            for (int i = 0; i < n; i++)
            {
                double mass = cumulative[i + 1] - cumulative[i];
                if (mass <= 0.0)
                    continue;
                double share = (Math.Min(cumulative[i + 1], high) - Math.Max(cumulative[i], low)) / mass;
                if (share <= 0.0)
                    continue;
                if (weight is null)
                {
                    bx += x[i] * share;
                    by += y[i] * share;
                    bz += z[i] * share;
                }
                else
                {
                    bx += x[i] * weight[i] * share;
                    by += y[i] * weight[i] * share;
                    bz += z[i] * weight[i] * share;
                }
            }
            while (sample < n - 1 && cumulative[sample + 1] <= median)
                sample++;
            nanometres[k] = CieObserver.FirstNm + sample;
            if (weight is not null)
            {
                bx /= weight[sample];
                by /= weight[sample];
                bz /= weight[sample];
            }
            binEdges[k] = EdgeAt(cumulative, low);
            cie[k * 4] = (float)bx;
            cie[k * 4 + 1] = (float)by;
            cie[k * 4 + 2] = (float)bz;
            cie[k * 4 + 3] = nanometres[k];
            sumY += (float)by;
        }
        binEdges[Count] = EdgeAt(cumulative, total);
        return new WavelengthTable(quadrature, nanometres, cie, (float)(Count / sumY), binEdges);
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
