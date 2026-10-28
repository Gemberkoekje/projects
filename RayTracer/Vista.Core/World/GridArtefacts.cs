namespace Vista.World;

/// <summary>
/// The world grid's own patterns in a height field (vista-plan §10 M1a.1): the two measures M1a's fixes are checked against, because
/// the hero's grooves and dots are both patterns of the grid and not of the ground.
/// <list type="bullet">
///   <item><b>Alignment</b> (the grooves): which way the relief runs, cell by cell. At each sample the height field's Hessian (its
///   second differences) has a principal axis, across a gully or a rib; the measure is the share of the relief, each sample weighed by
///   how much more it curves one way than the other, whose axis lies within <see cref="AlignedHalfWidthDegrees"/> of one of the grid's
///   eight directions (its rows, columns and diagonals). Relief that runs every way puts about <see cref="IsotropicShare"/> there
///   (a little more: the differences favour the grid slightly; an isotropic fractal measures 14.8%); gullies the grid steers drive it up.</item>
///   <item><b>Ripple</b> (the dots): how much of the field is a regular pattern of a few cells. Windows of
///   <see cref="WindowCells"/>² samples, <see cref="WindowStride"/> apart, have their plane taken off and their spectrum taken under a
///   Hann window; a window ripples when one peak at wavelengths of <see cref="RippleShortestCells"/> to <see cref="RippleLongestCells"/>
///   cells (the bin and its eight neighbours, and its mirror) holds more than <see cref="RipplePeakShare"/> of its power at wavelengths
///   under <see cref="FineLongestCells"/> cells. Natural relief spreads that power (an isotropic fractal ripples in under 0.2% of its
///   windows); a washboard or a comb of rills a few cells apart concentrates it.</item>
/// </list>
/// </summary>
public sealed record GridArtefacts
{
    /// <summary>The grid's cell, metres.</summary>
    public required double SpacingMetres { get; init; }

    /// <summary>The samples the alignment weighed (those the filter kept, away from the edge).</summary>
    public required long Cells { get; init; }

    /// <summary>The share of the relief running along one of the grid's eight directions.</summary>
    public required double AlignedShare { get; init; }

    /// <summary>The windows the ripple looked at (those whose centre the filter kept).</summary>
    public required long Windows { get; init; }

    /// <summary>The share of those windows that ripple.</summary>
    public required double RippleShare { get; init; }

    /// <summary>Relief whose axis lies this close to one of the grid's directions runs along it, degrees.</summary>
    public const double AlignedHalfWidthDegrees = 3.0;

    /// <summary>The window of the ripple measure, samples a side.</summary>
    public const int WindowCells = 16;

    /// <summary>The spacing of the ripple measure's windows, samples.</summary>
    public const int WindowStride = 8;

    /// <summary>The shortest wavelength a ripple has, cells: the grid's own.</summary>
    public const double RippleShortestCells = 2.0;

    /// <summary>The longest wavelength a ripple has, cells.</summary>
    public const double RippleLongestCells = 4.0;

    /// <summary>The longest wavelength of the fine relief a ripple's peak is a share of, cells.</summary>
    public const double FineLongestCells = 10.0;

    /// <summary>The share of a window's fine power one peak must hold for the window to ripple.</summary>
    public const double RipplePeakShare = 0.4;

    /// <summary>The share of relief running every way that falls within <see cref="AlignedHalfWidthDegrees"/> of the eight directions.</summary>
    public static double IsotropicShare => 4.0 * 2.0 * AlignedHalfWidthDegrees / 180.0;

    /// <summary>
    /// The measures of <paramref name="field"/>, over the samples <paramref name="where"/> keeps (by row-major index; all of them when
    /// null). A share with nothing to weigh is NaN.
    /// </summary>
    public static GridArtefacts Measure(HeightField field, Func<int, bool>? where = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        (long cells, double aligned) = Alignment(field, where);
        (long windows, double ripple) = Ripple(field, where);
        return new GridArtefacts { SpacingMetres = field.CellSize, Cells = cells, AlignedShare = aligned, Windows = windows, RippleShare = ripple };
    }

    /// <summary>The share of relief along the grid's directions, of the samples kept; and how many there were.</summary>
    private static (long Cells, double Share) Alignment(HeightField field, Func<int, bool>? where)
    {
        int n = field.Size;
        var total = new double[n];
        var along = new double[n];
        var counts = new long[n];
        Parallel.For(1, n - 1, j =>
        {
            double t = 0.0, a = 0.0;
            long c = 0;
            for (int i = 1; i < n - 1; i++)
            {
                if (where is not null && !where(j * n + i))
                    continue;
                double h = field[i, j];
                double hxx = field[i + 1, j] - 2.0 * h + field[i - 1, j];
                double hzz = field[i, j + 1] - 2.0 * h + field[i, j - 1];
                double hxz = 0.25 * (field[i + 1, j + 1] - field[i - 1, j + 1] - field[i + 1, j - 1] + field[i - 1, j - 1]);
                double half = 0.5 * (hxx - hzz);
                double weight = Math.Sqrt(half * half + hxz * hxz);
                c++;
                if (weight <= 0.0)
                    continue;
                double axis = 0.5 * Math.Atan2(2.0 * hxz, hxx - hzz) * (180.0 / Math.PI);
                double off = ((axis % 45.0) + 45.0) % 45.0;
                t += weight;
                if (Math.Min(off, 45.0 - off) < AlignedHalfWidthDegrees)
                    a += weight;
            }
            total[j] = t;
            along[j] = a;
            counts[j] = c;
        });
        double sum = 0.0, sumAlong = 0.0;
        long cells = 0;
        for (int j = 0; j < n; j++)
        {
            sum += total[j];
            sumAlong += along[j];
            cells += counts[j];
        }
        return (cells, sum > 0.0 ? sumAlong / sum : double.NaN);
    }

    /// <summary>The share of windows that ripple, of those whose centre is kept; and how many there were.</summary>
    private static (long Windows, double Share) Ripple(HeightField field, Func<int, bool>? where)
    {
        const int w = WindowCells;
        int n = field.Size;
        int across = n >= w ? (n - w) / WindowStride + 1 : 0;
        // The Hann window (zero at both ends, as numpy's), the transform's twiddles, and which bins count.
        var hann = new double[w];
        var cos = new double[w];
        var sin = new double[w];
        for (int k = 0; k < w; k++)
        {
            hann[k] = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * k / (w - 1));
            cos[k] = Math.Cos(2.0 * Math.PI * k / w);
            sin[k] = Math.Sin(2.0 * Math.PI * k / w);
        }
        var band = new bool[w * w];
        var fine = new bool[w * w];
        for (int ky = 0; ky < w; ky++)
        {
            for (int kx = 0; kx < w; kx++)
            {
                double fx = (kx <= w / 2 ? kx : kx - w) / (double)w, fy = (ky <= w / 2 ? ky : ky - w) / (double)w;
                double f = Math.Sqrt(fx * fx + fy * fy);
                band[ky * w + kx] = f >= 1.0 / RippleLongestCells && f <= 1.0 / RippleShortestCells + 1e-9;
                fine[ky * w + kx] = f > 1.0 / FineLongestCells;
            }
        }
        var looked = new long[across];
        var rippled = new long[across];
        Parallel.For(0, across, wj =>
        {
            var d = new double[w * w];
            var re = new double[w * w];
            var im = new double[w * w];
            var power = new double[w * w];
            long l = 0, r = 0;
            for (int wi = 0; wi < across; wi++)
            {
                int i0 = wi * WindowStride, j0 = wj * WindowStride;
                if (where is not null && !where((j0 + w / 2) * n + i0 + w / 2))
                    continue;
                l++;
                if (Ripples(field, i0, j0, hann, cos, sin, band, fine, d, re, im, power))
                    r++;
            }
            looked[wj] = l;
            rippled[wj] = r;
        });
        long windows = looked.Sum(), ripples = rippled.Sum();
        return (windows, windows > 0 ? (double)ripples / windows : double.NaN);
    }

    /// <summary>Whether the window at (<paramref name="i0"/>, <paramref name="j0"/>) ripples (see the type's remarks).</summary>
    private static bool Ripples(HeightField field, int i0, int j0, double[] hann, double[] cos, double[] sin, bool[] band, bool[] fine,
        double[] d, double[] re, double[] im, double[] power)
    {
        const int w = WindowCells;
        // The plane through the window by least squares (on a square grid its three terms separate), taken off.
        double mean = 0.0, sx = 0.0, sz = 0.0, sxx = 0.0;
        for (int j = 0; j < w; j++)
        {
            for (int i = 0; i < w; i++)
            {
                double h = field[i0 + i, j0 + j];
                double x = i - 0.5 * (w - 1), z = j - 0.5 * (w - 1);
                mean += h;
                sx += x * h;
                sz += z * h;
                sxx += x * x;
            }
        }
        mean /= w * w;
        double gx = sx / sxx, gz = sz / sxx;
        for (int j = 0; j < w; j++)
        {
            for (int i = 0; i < w; i++)
                d[j * w + i] = (field[i0 + i, j0 + j] - mean - gx * (i - 0.5 * (w - 1)) - gz * (j - 0.5 * (w - 1))) * hann[i] * hann[j];
        }
        // The 2-D transform by rows, then columns.
        for (int j = 0; j < w; j++)
        {
            for (int kx = 0; kx < w; kx++)
            {
                double a = 0.0, b = 0.0;
                for (int i = 0; i < w; i++)
                {
                    int t = (kx * i) % w;
                    a += d[j * w + i] * cos[t];
                    b -= d[j * w + i] * sin[t];
                }
                re[j * w + kx] = a;
                im[j * w + kx] = b;
            }
        }
        double finePower = 0.0, best = -1.0;
        int peak = 0;
        for (int kx = 0; kx < w; kx++)
        {
            for (int ky = 0; ky < w; ky++)
            {
                double a = 0.0, b = 0.0;
                for (int j = 0; j < w; j++)
                {
                    int t = (ky * j) % w;
                    double c = cos[t], s = sin[t];
                    a += re[j * w + kx] * c + im[j * w + kx] * s;
                    b += im[j * w + kx] * c - re[j * w + kx] * s;
                }
                int bin = ky * w + kx;
                double p = a * a + b * b;
                power[bin] = p;
                if (fine[bin])
                    finePower += p;
                if (band[bin] && p > best)
                {
                    best = p;
                    peak = bin;
                }
            }
        }
        if (finePower <= 0.0)
            return false;
        int py = peak / w, px = peak % w;
        double around = 0.0;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
                around += power[((py + dy + w) % w) * w + (px + dx + w) % w];
        }
        return Math.Min(2.0 * around / finePower, 1.0) > RipplePeakShare;
    }
}
