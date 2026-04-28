using System;

namespace RayTracer;

public partial class JobSystem
{
    private const uint StopAccumulationSampleCount = 8;

    /// <summary>
    /// Clears the accumulation buffers so the image reconverges quickly
    /// after a camera movement or rotation.
    /// </summary>
    public void ResetAccumulation()
    {
        _accumulationBuffer.ResetAccumulation();
    }

    internal void ResetAccumulationCore()
    {
        Array.Clear(_buffers.AccumXYZ);
        Array.Clear(_buffers.SampleCount);
        Array.Clear(_buffers.LastHit);
        Array.Clear(_buffers.WavelengthCounter);
        Array.Clear(_buffers.HitPointWorld);
        Array.Clear(_buffers.LumaM2);
        Array.Clear(_buffers.LumaVariance);
        Array.Clear(_buffers.LumaDirectM2);
        Array.Clear(_buffers.LumaIndirectM2);
        Array.Clear(_buffers.LumaDirectVariance);
        Array.Clear(_buffers.LumaIndirectVariance);
        Array.Clear(_buffers.HistoryWeight);
        Array.Clear(_buffers.HistoryRejected);
        Array.Clear(_buffers.ClampAmount);
        Array.Clear(_buffers.ClampHitFrame);
        Array.Clear(_buffers.DepthDistance);
        Array.Clear(_buffers.AlbedoScalar);
        Array.Clear(_buffers.NormalWorld);
        Array.Clear(_buffers.DirectLightingXYZ);
        Array.Clear(_buffers.IndirectLightingXYZ);
        Array.Clear(_buffers.EmissiveLightingXYZ);
        Array.Clear(_buffers.Bounce0XYZ);
        Array.Clear(_buffers.Bounce1XYZ);
        Array.Clear(_buffers.Bounce2PlusXYZ);
        Array.Clear(_buffers.DiffCurrentVsAccum);
        Array.Clear(_buffers.DiffUnfilteredVsFiltered);
        Array.Clear(_buffers.DiffReprojectedVsCurrent);
        Array.Clear(_buffers.LastUpdatedFrame);
        Array.Clear(_buffers.DiffuseCacheState);
        Array.Clear(_taaHistoryXYZ);
        Array.Clear(_taaHistoryHitPoint);
        Array.Clear(_taaHistoryValid);
        Array.Clear(_buffers.TaaNextXYZ);
        Array.Clear(_buffers.TaaNextHitPoint);
        Array.Clear(_buffers.TaaNextValid);
        _irradianceCache.Clear();
        _taaHasPrevCamera = false;
        _clampEventCount = 0;
        RejectedHistoryPercent = 0;
        ClampedPixelPercent = 0;
        AverageVariance = 0;
        AverageHistoryWeight = 0;
        AverageEffectiveSpp = 0;
        MaxObservedSampleCount = 0;
        DiffuseCacheEntryCount = 0;
        DiffuseCacheHitRate = 0;
        FrameIndex = 0;
    }

    /// <summary>
    /// Invalidate only the TAA/history buffers so reprojection is disabled
    /// for the next resolve without clearing the accumulation buffers.
    /// Useful for rotations where history should not be reused but
    /// preserving accumulated samples avoids large black holes when using
    /// checkerboard/motion optimizations.
    /// </summary>
    public void InvalidateTaaHistory()
    {
        Array.Clear(_taaHistoryValid);
        _taaHasPrevCamera = false;
    }

    /// <summary>
    /// Caps every sample count to <see cref="MotionSampleCap"/> so that
    /// new samples quickly dominate stale data. Unlike a multiplicative
    /// decay this is frame-rate-independent: the effective window is
    /// always exactly <see cref="MotionSampleCap"/> samples regardless
    /// of how often pixels are revisited.
    /// </summary>
    public void SoftResetAccumulation()
    {
        _accumulationBuffer.SoftResetAccumulation();
    }

    internal void SoftResetAccumulationCore()
    {
        _checkerPhase ^= 1;
        for (int i = 0; i < _buffers.SampleCount.Length; i++)
            if (_buffers.SampleCount[i] > MotionSampleCap)
                _buffers.SampleCount[i] = MotionSampleCap;
    }

    /// <summary>
    /// Reduces accumulation to a small effective sample count per pixel while
    /// preserving the current mean color. This allows new samples to take over
    /// quickly without a full blackout reset.
    /// </summary>
    public void CollapseAccumulationForStoppedMotion()
    {
        _accumulationBuffer.CollapseAccumulationForStoppedMotion();
    }

    internal void CollapseAccumulationForStoppedMotionCore()
    {
        _checkerPhase ^= 1;

        for (int i = 0; i < _buffers.SampleCount.Length; i++)
        {
            uint count = _buffers.SampleCount[i];
            if (count <= StopAccumulationSampleCount)
                continue;

            float scale = StopAccumulationSampleCount / (float)count;
            _buffers.LumaM2[i] *= scale;
            _buffers.LumaDirectM2[i] *= scale;
            _buffers.LumaIndirectM2[i] *= scale;
            _buffers.ClampAmount[i] *= scale;
            _buffers.SampleCount[i] = StopAccumulationSampleCount;
            _buffers.WavelengthCounter[i] = Math.Min(_buffers.WavelengthCounter[i], StopAccumulationSampleCount);
        }
    }

    private sealed class AccumulationBuffer(JobSystem owner)
    {
        private readonly JobSystem _owner = owner;

        public void ResetAccumulation()
        {
            _owner.ResetAccumulationCore();
        }

        public void SoftResetAccumulation()
        {
            _owner.SoftResetAccumulationCore();
        }

        public void CollapseAccumulationForStoppedMotion()
        {
            _owner.CollapseAccumulationForStoppedMotionCore();
        }
    }
}
