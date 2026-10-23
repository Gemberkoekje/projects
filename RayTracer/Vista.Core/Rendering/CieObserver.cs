using System.Globalization;
using RayTracer;

namespace Vista.Rendering;

/// <summary>
/// The CIE 1931 2° colour-matching functions at every whole nanometre from 360 to 830 nm, in double precision: the engine's
/// embedded table (<c>RayTracer.Data.CIE_xyz_1931_2deg.csv</c>, the one <see cref="WavelengthLookup"/> reads), parsed once. Vista's
/// wavelength table (<see cref="WavelengthTable"/>, vista-plan §10 M1.12a) is cut from it, and dense integration over it is the
/// reference that table is checked against (<see cref="QuadratureCheck"/>).
/// </summary>
public static class CieObserver
{
    private static readonly Lazy<(double[] X, double[] Y, double[] Z)> Shared = new(Read);

    /// <summary>The first wavelength of the table, nm.</summary>
    public const int FirstNm = 360;

    /// <summary>The last wavelength of the table, nm.</summary>
    public const int LastNm = 830;

    /// <summary>Entries in the table, one per nanometre.</summary>
    public const int Count = LastNm - FirstNm + 1;

    /// <summary>x̄ at <c>FirstNm + i</c>.</summary>
    public static IReadOnlyList<double> X => Shared.Value.X;

    /// <summary>ȳ at <c>FirstNm + i</c>.</summary>
    public static IReadOnlyList<double> Y => Shared.Value.Y;

    /// <summary>z̄ at <c>FirstNm + i</c>.</summary>
    public static IReadOnlyList<double> Z => Shared.Value.Z;

    /// <summary>
    /// The tristimulus of <paramref name="spectrum"/> (a function of the wavelength in nm) by dense integration, 1 nm steps over the
    /// whole table, scaled as the integrator scales its own: Y is the ȳ-weighted mean, so a flat spectrum of 1 has Y = 1.
    /// </summary>
    public static (double X, double Y, double Z) Integrate(Func<double, double> spectrum)
    {
        ArgumentNullException.ThrowIfNull(spectrum);
        (double[] x, double[] y, double[] z) = Shared.Value;
        double sx = 0.0, sy = 0.0, sz = 0.0, norm = 0.0;
        for (int i = 0; i < Count; i++)
        {
            double s = spectrum(FirstNm + i);
            sx += x[i] * s;
            sy += y[i] * s;
            sz += z[i] * s;
            norm += y[i];
        }
        return (sx / norm, sy / norm, sz / norm);
    }

    private static (double[] X, double[] Y, double[] Z) Read()
    {
        const string resource = "RayTracer.Data.CIE_xyz_1931_2deg.csv";
        using Stream stream = typeof(WavelengthLookup).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The engine's embedded resource '{resource}' was not found.");
        using var reader = new StreamReader(stream);
        var x = new double[Count];
        var y = new double[Count];
        var z = new double[Count];
        var seen = new bool[Count];
        while (reader.ReadLine() is { } line)
        {
            string[] parts = line.Split(',');
            if (parts.Length != 4 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int nm) || nm < FirstNm || nm > LastNm)
                continue;
            int i = nm - FirstNm;
            x[i] = double.Parse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture);
            y[i] = double.Parse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture);
            z[i] = double.Parse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture);
            seen[i] = true;
        }
        int missing = Array.IndexOf(seen, false);
        if (missing >= 0)
            throw new InvalidOperationException($"The CIE table has no entry at {FirstNm + missing} nm.");
        return (x, y, z);
    }
}
