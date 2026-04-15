namespace RayTracer;

/// <summary>
/// Lightweight per-frame snapshot emitted at the end of each
/// <see cref="JobSystem.ResolveDisplayBufferWithTaa"/> call.
/// Capture timing, quality metrics, and throughput counters in a
/// single zero-allocation struct for runtime logging and before/after comparisons.
/// </summary>
public readonly record struct FrameDiagnostics
{
    /// <summary>Elapsed time for the full TAA resolve pass in milliseconds.</summary>
    public double ResolveTaaMs { get; init; }

    /// <summary>Frame index at which this snapshot was taken (equals FrameIndex after the resolve).</summary>
    public int FrameIndex { get; init; }

    /// <summary>Fraction of pixels whose reprojected history was rejected (0–100).</summary>
    public double RejectedHistoryPercent { get; init; }

    /// <summary>Average history blend weight across all pixels (0–1).</summary>
    public double AverageHistoryWeight { get; init; }

    /// <summary>Average per-pixel luminance variance.</summary>
    public double AverageVariance { get; init; }

    /// <summary>Average effective sample count across all pixels.</summary>
    public double AverageEffectiveSpp { get; init; }

    /// <summary>Fraction of pixels that hit the sample clamp this frame (0–100).</summary>
    public double ClampedPixelPercent { get; init; }

    /// <summary>Total ray traces accumulated since the <see cref="JobSystem"/> was created.</summary>
    public long TotalRays { get; init; }

    /// <summary>Total tile completions accumulated since the <see cref="JobSystem"/> was created.</summary>
    public long TotalTileCompletions { get; init; }
}
