using Vista.Cli;

namespace Vista.Output;

/// <summary>How a render compared with its golden (vista-plan §10 M1.17).</summary>
public enum GoldenVerdict
{
    /// <summary>Not compared.</summary>
    None,

    /// <summary>The PNG's bytes and the EXR's values are the golden's, every one.</summary>
    Identical,

    /// <summary>Not identical, but inside the suite's tolerance (<see cref="GoldenTolerance"/>).</summary>
    WithinTolerance,

    /// <summary>Outside the tolerance: the look or the physics moved, or the files' sizes differ.</summary>
    Different,

    /// <summary>The suite's folder has no golden of this name yet.</summary>
    Missing,
}

/// <summary>
/// A render against its golden (vista-plan §10 M1.17): the verdict and the measures it was reached by.
/// </summary>
/// <param name="Verdict">How it compared.</param>
/// <param name="MeanRelative">The EXR's mean luminance against the golden's, as a share of it (signed: positive is brighter).</param>
/// <param name="BlockRelative">The EXR's blocks' luminance against the golden's, root mean square, as a share of the golden's mean.</param>
/// <param name="PngMeanLevels">
/// The PNG's channel means against the golden's, in 8-bit levels: the channel that moved most, signed. Per channel, so a change of colour
/// that keeps the luminance (a white balance) shows too.
/// </param>
/// <param name="PngChangedShare">The share of the PNG's channel values that differ from the golden's at all.</param>
/// <param name="Note">Why it is different or missing, in words; null when it passed.</param>
public sealed record GoldenResult(GoldenVerdict Verdict, double MeanRelative, double BlockRelative, double PngMeanLevels, double PngChangedShare, string? Note)
{
    /// <summary>True when the render passes: identical, or within the tolerance.</summary>
    public bool Passed => Verdict is GoldenVerdict.Identical or GoldenVerdict.WithinTolerance;
}

/// <summary>Compares a render's PNG and EXR with its goldens (vista-plan §10 M1.17; <see cref="GoldenTolerance"/>).</summary>
public static class GoldenComparison
{
    /// <summary>Rec.709's luminance weights, for the EXR's linear Rec.709.</summary>
    private static readonly (float R, float G, float B) Luma = (0.2126f, 0.7152f, 0.0722f);

    /// <summary>Compares the render <paramref name="png"/> / <paramref name="exr"/> with the goldens <paramref name="goldenPng"/> / <paramref name="goldenExr"/>.</summary>
    public static GoldenResult Compare(string png, string exr, string goldenPng, string goldenExr, GoldenTolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(tolerance);
        if (!File.Exists(goldenPng) || !File.Exists(goldenExr))
            return new GoldenResult(GoldenVerdict.Missing, double.NaN, double.NaN, double.NaN, double.NaN, "no golden yet");
        return Compare(PngDecoder.Read(png), ExrDecoder.Read(exr), PngDecoder.Read(goldenPng), ExrDecoder.Read(goldenExr), tolerance);
    }

    /// <summary>Compares decoded images (<see cref="Compare(string, string, string, string, GoldenTolerance)"/>).</summary>
    public static GoldenResult Compare(PngImage png, ExrImage exr, PngImage goldenPng, ExrImage goldenExr, GoldenTolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(png);
        ArgumentNullException.ThrowIfNull(exr);
        ArgumentNullException.ThrowIfNull(goldenPng);
        ArgumentNullException.ThrowIfNull(goldenExr);
        ArgumentNullException.ThrowIfNull(tolerance);
        if (png.Width != goldenPng.Width || png.Height != goldenPng.Height || png.Color != goldenPng.Color || exr.Width != goldenExr.Width
            || exr.Height != goldenExr.Height || png.Pixels.Length != goldenPng.Pixels.Length)
        {
            return new GoldenResult(GoldenVerdict.Different, double.NaN, double.NaN, double.NaN, double.NaN,
                $"the size or format changed: {png.Width}×{png.Height} against the golden's {goldenPng.Width}×{goldenPng.Height}");
        }

        int channels = png.Pixels.Length / (png.Width * png.Height);
        long changed = 0;
        var sums = new long[channels];
        for (int i = 0; i < png.Pixels.Length; i++)
        {
            changed += png.Pixels[i] == goldenPng.Pixels[i] ? 0 : 1;
            sums[i % channels] += png.Pixels[i] - goldenPng.Pixels[i];
        }
        double pngMean = sums.Select(s => (double)s * channels / png.Pixels.Length).MaxBy(Math.Abs);
        double changedShare = (double)changed / png.Pixels.Length;

        float[] y = Luminance(exr), goldenY = Luminance(goldenExr);
        bool exrIdentical = y.AsSpan().SequenceEqual(goldenY) && SameRgb(exr, goldenExr);
        double mean = y.Average(v => (double)v), goldenMean = goldenY.Average(v => (double)v);
        double meanRelative = goldenMean > 0.0 ? (mean - goldenMean) / goldenMean : mean == goldenMean ? 0.0 : double.PositiveInfinity;
        double blockRelative = BlockRms(y, goldenY, exr.Width, exr.Height, tolerance.BlockPixels) / Math.Max(goldenMean, double.Epsilon);

        if (changed == 0 && exrIdentical)
            return new GoldenResult(GoldenVerdict.Identical, 0.0, 0.0, 0.0, 0.0, null);
        var reasons = new List<string>();
        if (!(Math.Abs(meanRelative) <= tolerance.MeanRelative))
            reasons.Add($"mean luminance {Signed(meanRelative * 100.0)}% (tolerance ±{tolerance.MeanRelative * 100.0:0.##}%)");
        if (!(blockRelative <= tolerance.BlockRelative))
            reasons.Add($"blocks {blockRelative * 100.0:0.00}% rms (tolerance {tolerance.BlockRelative * 100.0:0.##}%)");
        if (!(Math.Abs(pngMean) <= tolerance.PngMeanLevels))
            reasons.Add($"PNG channel mean {Signed(pngMean)} levels (tolerance ±{tolerance.PngMeanLevels:0.##})");
        return new GoldenResult(reasons.Count == 0 ? GoldenVerdict.WithinTolerance : GoldenVerdict.Different, meanRelative, blockRelative, pngMean,
            changedShare, reasons.Count == 0 ? null : string.Join("; ", reasons));
    }

    /// <summary><paramref name="value"/> to two decimals with its sign, and ±0.00 for a value that rounds to zero.</summary>
    public static string Signed(double value) =>
        Math.Abs(value) < 0.005 ? "±0.00" : value.ToString("+0.00;-0.00", System.Globalization.CultureInfo.InvariantCulture);

    private static float[] Luminance(ExrImage image)
    {
        float[] r = image.Channel("R").Data, g = image.Channel("G").Data, b = image.Channel("B").Data;
        var y = new float[r.Length];
        for (int p = 0; p < y.Length; p++)
            y[p] = Luma.R * r[p] + Luma.G * g[p] + Luma.B * b[p];
        return y;
    }

    private static bool SameRgb(ExrImage a, ExrImage b) =>
        new[] { "R", "G", "B" }.All(name => a.Channel(name).Data.AsSpan().SequenceEqual(b.Channel(name).Data));

    /// <summary>The root mean square of the blocks' mean luminance differences (<paramref name="block"/> pixels a side, the edge blocks smaller).</summary>
    private static double BlockRms(float[] y, float[] golden, int width, int height, int block)
    {
        double squares = 0.0;
        int blocks = 0;
        for (int by = 0; by < height; by += block)
        {
            for (int bx = 0; bx < width; bx += block)
            {
                double difference = 0.0;
                int pixels = 0;
                for (int yy = by; yy < Math.Min(by + block, height); yy++)
                {
                    for (int xx = bx; xx < Math.Min(bx + block, width); xx++)
                    {
                        difference += y[yy * width + xx] - golden[yy * width + xx];
                        pixels++;
                    }
                }
                double mean = difference / pixels;
                squares += mean * mean;
                blocks++;
            }
        }
        return Math.Sqrt(squares / blocks);
    }
}
