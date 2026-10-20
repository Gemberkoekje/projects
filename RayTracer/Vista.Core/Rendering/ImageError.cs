namespace Vista.Rendering;

/// <summary>
/// The relative mean squared error of an image against a reference (vista-plan §10 M1.6), with the reference's own noise taken
/// out: <see cref="Corrected"/> estimates the error against the noise-free image, <see cref="Measured"/> is the error against
/// the reference as it is, and <see cref="ReferenceNoise"/> is the difference, the reference's own share.
/// </summary>
/// <param name="Corrected">The error against the noise-free image (may come out slightly negative when it is below the reference's noise).</param>
/// <param name="Measured">The error against the reference.</param>
/// <param name="ReferenceNoise">The reference's own noise in the same units.</param>
public readonly record struct RelativeError(double Corrected, double Measured, double ReferenceNoise);

/// <summary>
/// How far one image is from another (vista-plan §10 M1.6): the denoiser's checks. Every function takes interleaved linear RGB
/// (or 8-bit display RGB) of one size.
/// </summary>
public static class ImageError
{
    /// <summary>The ε of relMSE's denominator, <c>(x − r)² / (r² + ε)</c>, for values exposed so 0.18 is middle grey.</summary>
    public const double RelativeEpsilon = 0.01;

    /// <summary>
    /// relMSE of <paramref name="image"/> against the mean <c>r</c> of two independent halves of a reference,
    /// <paramref name="halfA"/> and <paramref name="halfB"/>, all multiplied by <paramref name="scale"/> first (the display
    /// exposure, so 0.18 is middle grey): the mean over every value of <c>((x − r)² − (a − b)²/4) / (r² + ε)</c>. The subtracted
    /// term is the reference's own noise, because <c>(a − b)²/4</c> estimates the variance of their mean; without it a denoised
    /// image's error would be mostly the reference's.
    /// </summary>
    public static RelativeError RelativeMse(ReadOnlySpan<float> image, ReadOnlySpan<float> halfA, ReadOnlySpan<float> halfB, double scale)
    {
        if (image.Length != halfA.Length || image.Length != halfB.Length)
            throw new ArgumentException($"Images differ in size: {image.Length}, {halfA.Length} and {halfB.Length} values.", nameof(image));
        if (image.Length == 0)
            throw new ArgumentException("The images are empty.", nameof(image));
        double measured = 0.0, noise = 0.0;
        for (int i = 0; i < image.Length; i++)
        {
            double a = halfA[i] * scale, b = halfB[i] * scale;
            double r = 0.5 * (a + b);
            double d = image[i] * scale - r;
            double denominator = r * r + RelativeEpsilon;
            measured += d * d / denominator;
            noise += 0.25 * (a - b) * (a - b) / denominator;
        }
        measured /= image.Length;
        noise /= image.Length;
        return new RelativeError(measured - noise, measured, noise);
    }

    /// <summary>The root mean square difference of two 8-bit images, in 8-bit steps.</summary>
    public static double DisplayRmse(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length || a.Length == 0)
            throw new ArgumentException($"Images must be the same non-zero size: {a.Length} and {b.Length} bytes.", nameof(a));
        double sum = 0.0;
        for (int i = 0; i < a.Length; i++)
        {
            double d = a[i] - b[i];
            sum += d * d;
        }
        return Math.Sqrt(sum / a.Length);
    }

    /// <summary>The image's mean Rec.709 luminance over the reference's, minus 1: what the image gained or lost in all.</summary>
    public static double MeanLuminanceBias(ReadOnlySpan<float> image, ReadOnlySpan<float> reference)
    {
        if (image.Length != reference.Length || image.Length % 3 != 0 || image.Length == 0)
            throw new ArgumentException($"Expected two RGB images of one size, got {image.Length} and {reference.Length} values.", nameof(image));
        double x = 0.0, r = 0.0;
        for (int p = 0; p < image.Length; p += 3)
        {
            x += Luminance(image, p);
            r += Luminance(reference, p);
        }
        return r > 0.0 ? x / r - 1.0 : 0.0;
    }

    /// <summary>
    /// The samples per pixel at which the raw render would reach <paramref name="error"/>, given that it has
    /// <paramref name="rawError"/> at <paramref name="spp"/>: an unbiased render's relMSE falls as 1 / spp.
    /// </summary>
    public static double EqualErrorSpp(int spp, double rawError, double error) =>
        error > 0.0 ? spp * rawError / error : double.PositiveInfinity;

    /// <summary>The largest difference between two images, in units of the larger magnitude of the pair (0 when both are 0).</summary>
    public static double MaxRelativeDifference(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException($"Images differ in size: {a.Length} and {b.Length} values.", nameof(a));
        double max = 0.0;
        for (int i = 0; i < a.Length; i++)
        {
            double m = Math.Max(Math.Abs(a[i]), Math.Abs(b[i]));
            if (m > 0.0)
                max = Math.Max(max, Math.Abs(a[i] - b[i]) / m);
        }
        return max;
    }

    /// <summary>True when the two buffers hold the same bits.</summary>
    public static bool BitIdentical(ReadOnlySpan<float> a, ReadOnlySpan<float> b) =>
        System.Runtime.InteropServices.MemoryMarshal.AsBytes(a).SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(b));

    private static double Luminance(ReadOnlySpan<float> rgb, int p) => 0.2126 * rgb[p] + 0.7152 * rgb[p + 1] + 0.0722 * rgb[p + 2];
}
