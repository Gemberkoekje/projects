namespace Vista.Rendering;

/// <summary>
/// The raw per-pixel buffers integrator v0 accumulates on the GPU, and their conversion to the layouts
/// <see cref="RenderOutput"/> promises (<see cref="Aov"/>). The shader only ever adds samples in sample-index
/// order, starting from zero at sample 0, so these sums are independent of how the render was split into
/// dispatches; dividing by the sample count happens here, once, on the CPU.
/// </summary>
public static class IntegratorReadback
{
    /// <summary>Floats per pixel of the XYZ sum (x, y, z, unused).</summary>
    public const int XyzSumStride = 4;

    /// <summary>Floats per pixel of the luminance statistics (Welford mean, M2).</summary>
    public const int LumaStatsStride = 2;

    /// <summary>Floats per pixel of the albedo and normal sums (r/x, g/y, b/z, unused).</summary>
    public const int VectorSumStride = 4;

    /// <summary>Floats per pixel of the first-hit record (depth, material id) of sample 0.</summary>
    public const int FirstHitStride = 2;

    /// <summary>Mean CIE XYZ, 3 floats per pixel, from the per-pixel sums of <paramref name="samples"/> samples.</summary>
    public static float[] MeanXyz(ReadOnlySpan<float> xyzSum, int pixels, int samples) =>
        MeanOf3(xyzSum, pixels, samples, XyzSumStride);

    /// <summary>Mean linear-sRGB first-hit albedo, 3 floats per pixel (sky samples count as 0).</summary>
    public static float[] MeanAlbedo(ReadOnlySpan<float> albedoSum, int pixels, int samples) =>
        MeanOf3(albedoSum, pixels, samples, VectorSumStride);

    /// <summary>The normalised mean of the first-hit normals, 3 floats per pixel; 0 where every sample saw sky.</summary>
    public static float[] MeanNormal(ReadOnlySpan<float> normalSum, int pixels)
    {
        Check(normalSum, pixels, VectorSumStride);
        var normals = new float[pixels * 3];
        for (int p = 0; p < pixels; p++)
        {
            float x = normalSum[p * VectorSumStride];
            float y = normalSum[p * VectorSumStride + 1];
            float z = normalSum[p * VectorSumStride + 2];
            float length = MathF.Sqrt(x * x + y * y + z * z);
            if (length <= 1e-12f || !float.IsFinite(length))
                continue;
            normals[p * 3] = x / length;
            normals[p * 3 + 1] = y / length;
            normals[p * 3 + 2] = z / length;
        }
        return normals;
    }

    /// <summary>
    /// Variance of each pixel's mean luminance: the sample variance <c>M2 / (n − 1)</c> divided by <c>n</c>
    /// (0 for a single sample), 1 float per pixel.
    /// </summary>
    public static float[] Variance(ReadOnlySpan<float> lumaStats, int pixels, int samples)
    {
        Check(lumaStats, pixels, LumaStatsStride);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(samples);
        var variance = new float[pixels];
        if (samples < 2)
            return variance;
        double scale = 1.0 / ((double)samples * (samples - 1));
        for (int p = 0; p < pixels; p++)
            variance[p] = (float)(lumaStats[p * LumaStatsStride + 1] * scale);
        return variance;
    }

    /// <summary>Depth along the primary ray of sample 0 (+∞ for sky), 1 float per pixel.</summary>
    public static float[] Depth(ReadOnlySpan<float> firstHit, int pixels) => Channel(firstHit, pixels, 0);

    /// <summary>Material id of sample 0's first hit as a float (−1 for sky), 1 float per pixel.</summary>
    public static float[] MaterialId(ReadOnlySpan<float> firstHit, int pixels) => Channel(firstHit, pixels, 1);

    private static float[] MeanOf3(ReadOnlySpan<float> sum, int pixels, int samples, int stride)
    {
        Check(sum, pixels, stride);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(samples);
        var mean = new float[pixels * 3];
        float n = samples;
        for (int p = 0; p < pixels; p++)
        {
            mean[p * 3] = sum[p * stride] / n;
            mean[p * 3 + 1] = sum[p * stride + 1] / n;
            mean[p * 3 + 2] = sum[p * stride + 2] / n;
        }
        return mean;
    }

    private static float[] Channel(ReadOnlySpan<float> data, int pixels, int channel)
    {
        Check(data, pixels, FirstHitStride);
        var values = new float[pixels];
        for (int p = 0; p < pixels; p++)
            values[p] = data[p * FirstHitStride + channel];
        return values;
    }

    private static void Check(ReadOnlySpan<float> data, int pixels, int stride)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pixels);
        if (data.Length != (long)pixels * stride)
            throw new ArgumentException($"Expected {(long)pixels * stride} floats ({stride} per pixel), got {data.Length}.", nameof(data));
    }
}
