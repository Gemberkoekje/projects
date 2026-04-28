namespace RayTracer;

using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading.Channels;

/// <summary>
/// Factory for creating and configuring JobSystem instances.
/// Extracted to keep JobSystem constructor lean and isolated configuration logic.
/// </summary>
internal static class JobSystemFactory
{
    /// <summary>
    /// Creates a fully configured JobSystem instance with validated options.
    /// </summary>
    internal static JobSystem Create(
        int width,
        int height,
        Tracable[] scene,
        Camera camera,
        int stride,
        Light[]? lights = null,
        RenderOptions? renderOptions = null,
        SamplingOptions? samplingOptions = null,
        DenoiseOptions? denoiseOptions = null,
        DebugOptions? debugOptions = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);
        ValidateCoreInputs(width, height, stride);

        RenderOptions effectiveRenderOptions = renderOptions ?? new RenderOptions();
        SamplingOptions effectiveSamplingOptions = samplingOptions ?? new SamplingOptions();
        DenoiseOptions effectiveDenoiseOptions = denoiseOptions ?? new DenoiseOptions();
        DebugOptions effectiveDebugOptions = debugOptions ?? new DebugOptions();

        ValidateOptions(effectiveRenderOptions, effectiveSamplingOptions, effectiveDenoiseOptions);

        // Create the JobSystem with validated inputs
        var jobSystem = new JobSystem(
            width,
            height,
            scene,
            camera,
            stride,
            lights,
            effectiveRenderOptions,
            effectiveSamplingOptions,
            effectiveDenoiseOptions,
            effectiveDebugOptions);

        return jobSystem;
    }

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

        if (denoiseOptions.Volumetrics.MarchSteps < 0)
            throw new ArgumentOutOfRangeException(nameof(denoiseOptions), denoiseOptions.Volumetrics.MarchSteps, "MarchSteps cannot be negative.");
    }
}
