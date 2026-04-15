using System.Diagnostics;

namespace RayTracer;

/// <summary>
/// Runs the renderer at the lowest preset for a fixed duration and
/// reports the measured rays-per-second throughput. This lets the
/// application recommend quality settings before the actual maze
/// starts.
/// </summary>
public static class PerformanceCalibrator
{
    /// <summary>
    /// Spins up worker threads, traces rays for <paramref name="duration"/>,
    /// then cancels the workers and returns the throughput.
    /// </summary>
    public static async Task<double> MeasureRaysPerSecondAsync(
        Tracable[] scene,
        Camera camera,
        Light[] lights,
        TimeSpan duration,
        RenderPreset? benchPreset = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var preset = benchPreset ?? RenderPreset.Low;
        int benchW = preset.EffectiveWidth;
        int benchH = preset.EffectiveHeight;
        int stride = benchW * 4; // Format32bppArgb

        // Build a disposable camera with the benchmark resolution's
        // aspect ratio so projection maths stay correct.
        var benchCamera = new Camera
        {
            Position = camera.Position,
            Rotation = camera.Rotation,
            Fov = camera.Fov,
            Aspect = (float)benchW / benchH,
            ImgPlaneZ = camera.ImgPlaneZ
        };

        var js = new JobSystem(
            benchW, benchH,
            preset.TileSize, scene, benchCamera, stride, lights)
        {
            SppPerJob = preset.SppPerJob,
            MaxSampleCount = preset.MaxSampleCount,
            MotionSampleCap = preset.MotionSampleCap,
            SubPixelJitter = preset.SubPixelJitter,
            FilterRadius = preset.FilterRadius,
            EdgeAwareFilter = preset.EdgeAwareFilter,
            Lighting = preset.Lighting
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        js.SetupJobs(cts.Token);
        await js.AddJobs();

        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < duration)
        {
            if (ct.IsCancellationRequested) break;
            progress?.Report(sw.Elapsed.TotalSeconds / duration.TotalSeconds);
            try { await Task.Delay(100, ct); }
            catch (OperationCanceledException) { break; }
        }

        await cts.CancelAsync();
        sw.Stop();

        progress?.Report(1.0);

        double seconds = sw.Elapsed.TotalSeconds;
        return seconds > 0 ? js.TotalRays / seconds : 0;
    }
}
