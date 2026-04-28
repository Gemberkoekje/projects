namespace RayTracer;

public partial class JobSystem
{
    private static void ValidateCoreInputs(int width, int height, int stride)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be greater than zero.");

        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be greater than zero.");

        if (stride < width * 4)
            throw new ArgumentOutOfRangeException(nameof(stride), stride, "Stride must be at least width * 4 for 32bpp buffers.");
    }

    private static void ValidateOptions(RenderOptions renderOptions, SamplingOptions samplingOptions, DenoiseOptions denoiseOptions)
    {
        if (renderOptions.TileSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(renderOptions), renderOptions.TileSize, "TileSize must be greater than zero.");

        if (renderOptions.SppPerJob <= 0)
            throw new ArgumentOutOfRangeException(nameof(renderOptions), renderOptions.SppPerJob, "SppPerJob must be greater than zero.");

        if (renderOptions.MaxSampleCount == 0)
            throw new ArgumentOutOfRangeException(nameof(renderOptions), renderOptions.MaxSampleCount, "MaxSampleCount must be greater than zero.");

        if (samplingOptions.MotionSampleCap == 0)
            throw new ArgumentOutOfRangeException(nameof(samplingOptions), samplingOptions.MotionSampleCap, "MotionSampleCap must be greater than zero.");

        if (denoiseOptions.FilterRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(denoiseOptions), denoiseOptions.FilterRadius, "FilterRadius cannot be negative.");

        if (denoiseOptions.TemporalBlendAlpha is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(denoiseOptions), denoiseOptions.TemporalBlendAlpha, "TemporalBlendAlpha must be in [0, 1].");

        if (denoiseOptions.SampleClamp < 0f)
            throw new ArgumentOutOfRangeException(nameof(denoiseOptions), denoiseOptions.SampleClamp, "SampleClamp cannot be negative.");

        if (denoiseOptions.DiffuseCacheCellSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(denoiseOptions), denoiseOptions.DiffuseCacheCellSize, "DiffuseCacheCellSize must be greater than zero.");

        if (denoiseOptions.DiffuseCacheMinSamples == 0)
            throw new ArgumentOutOfRangeException(nameof(denoiseOptions), denoiseOptions.DiffuseCacheMinSamples, "DiffuseCacheMinSamples must be greater than zero.");

        ValidateVolumetricOptions(denoiseOptions.Volumetrics);
    }

    private static void ValidateVolumetricOptions(VolumetricOptions volumetrics)
    {
        if (volumetrics.MarchSteps < 0)
            throw new ArgumentOutOfRangeException(nameof(volumetrics), volumetrics.MarchSteps, "MarchSteps cannot be negative.");

        if (volumetrics.MaxMarchDistance < 0f)
            throw new ArgumentOutOfRangeException(nameof(volumetrics), volumetrics.MaxMarchDistance, "MaxMarchDistance cannot be negative.");

        if (volumetrics.SigmaScaleFog < 0f)
            throw new ArgumentOutOfRangeException(nameof(volumetrics), volumetrics.SigmaScaleFog, "SigmaScaleFog cannot be negative.");

        if (volumetrics.SigmaScaleGround < 0f)
            throw new ArgumentOutOfRangeException(nameof(volumetrics), volumetrics.SigmaScaleGround, "SigmaScaleGround cannot be negative.");

        if (volumetrics.AnisotropyG is < -0.95f or > 0.95f)
            throw new ArgumentOutOfRangeException(nameof(volumetrics), volumetrics.AnisotropyG, "AnisotropyG must be in [-0.95, 0.95].");

        if (volumetrics.InscatterStrength < 0f)
            throw new ArgumentOutOfRangeException(nameof(volumetrics), volumetrics.InscatterStrength, "InscatterStrength cannot be negative.");

        if (volumetrics.EarlyOutTransmittance is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(volumetrics), volumetrics.EarlyOutTransmittance, "EarlyOutTransmittance must be in [0, 1].");
    }
}
