using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

[TestClass]
public class AccumulationLifecycleTests
{
    private static readonly WavelengthLookup Wl = new();

    [TestMethod]
    public void HitToMiss_ResetsAccumulation()
    {
        var accum = new Vector3(0.5f, 0.4f, 0.3f);
        uint sampleCount = 500;
        bool lastHit = true;

        bool currentHit = false;
        if (currentHit != lastHit)
        {
            accum = Vector3.Zero;
            sampleCount = 0;
            lastHit = currentHit;
        }

        Assert.AreEqual(Vector3.Zero, accum);
        Assert.AreEqual(0u, sampleCount);
        Assert.IsFalse(lastHit);
    }

    [TestMethod]
    public void MissToHit_ResetsAccumulation()
    {
        var accum = Vector3.Zero;
        uint sampleCount = 500;
        bool lastHit = false;

        bool currentHit = true;
        if (currentHit != lastHit)
        {
            accum = Vector3.Zero;
            sampleCount = 0;
            lastHit = currentHit;
        }

        Assert.AreEqual(0u, sampleCount);
        Assert.IsTrue(lastHit);
    }

    [TestMethod]
    public void SameState_DoesNotReset()
    {
        var accum = new Vector3(0.5f, 0.4f, 0.3f);
        uint sampleCount = 200;
        bool lastHit = true;

        bool currentHit = true;
        if (currentHit != lastHit)
        {
            accum = Vector3.Zero;
            sampleCount = 0;
            lastHit = currentHit;
        }

        Assert.AreEqual(200u, sampleCount);
        Assert.AreEqual(new Vector3(0.5f, 0.4f, 0.3f), accum);
    }

    [TestMethod]
    public void HitToMiss_ConvergesToBlackQuickly()
    {
        var accum = Vector3.Zero;
        uint sampleCount = 0;
        long wavelengthCounter = 0;
        bool lastHit = false;
        uint maxSamples = 500;

        for (int s = 0; s < 200; s++)
        {
            bool hit = true;
            int wl = Wl.GetHeroWavelength(0, wavelengthCounter);
            wavelengthCounter++;
            Wl.TryGet(wl, out var xyz);
            var corrected = xyz * Wl.DeterministicCorrection;

            if (hit != lastHit) { accum = Vector3.Zero; sampleCount = 0; lastHit = hit; }
            if (sampleCount < maxSamples) sampleCount++;
            accum += (corrected - accum) / sampleCount;
        }

        Assert.IsTrue(accum.Y > 0.5f);

        for (int s = 0; s < 10; s++)
        {
            bool hit = false;
            var corrected = Vector3.Zero;

            if (hit != lastHit) { accum = Vector3.Zero; sampleCount = 0; lastHit = hit; }
            if (sampleCount < maxSamples) sampleCount++;
            accum += (corrected - accum) / sampleCount;
        }

        Assert.IsTrue(accum.Y < 0.01f);
    }

    [TestMethod]
    public void WithoutReset_GhostingPersists()
    {
        var accum = Vector3.Zero;
        uint sampleCount = 0;
        long wavelengthCounter = 0;
        uint maxSamples = 500;

        for (int s = 0; s < 500; s++)
        {
            int wl = Wl.GetHeroWavelength(0, wavelengthCounter);
            wavelengthCounter++;
            Wl.TryGet(wl, out var xyz);
            var corrected = xyz * Wl.DeterministicCorrection;
            if (sampleCount < maxSamples) sampleCount++;
            accum += (corrected - accum) / sampleCount;
        }

        var colorBeforeMiss = accum.Y;

        for (int s = 0; s < 100; s++)
        {
            var corrected = Vector3.Zero;
            if (sampleCount < maxSamples) sampleCount++;
            accum += (corrected - accum) / sampleCount;
        }

        Assert.IsTrue(accum.Y > colorBeforeMiss * 0.5f);
    }

    [TestMethod]
    public void FullPixelLifecycle_MissHitMiss()
    {
        var accum = Vector3.Zero;
        uint sampleCount = 0;
        long wavelengthCounter = 0;
        bool lastHit = false;
        uint maxSamples = 500;

        for (int s = 0; s < 50; s++)
        {
            bool hit = false;
            var corrected = Vector3.Zero;
            if (hit != lastHit) { accum = Vector3.Zero; sampleCount = 0; lastHit = hit; }
            if (sampleCount < maxSamples) sampleCount++;
            accum += (corrected - accum) / sampleCount;
            wavelengthCounter++;
        }

        Assert.AreEqual(0f, accum.Y, 0.001f);

        for (int s = 0; s < 200; s++)
        {
            bool hit = true;
            int wl = Wl.GetHeroWavelength(0, wavelengthCounter);
            wavelengthCounter++;
            Wl.TryGet(wl, out var xyz);
            var corrected = xyz * Wl.DeterministicCorrection;

            if (hit != lastHit) { accum = Vector3.Zero; sampleCount = 0; lastHit = hit; }
            if (sampleCount < maxSamples) sampleCount++;
            accum += (corrected - accum) / sampleCount;
        }

        Assert.IsTrue(accum.Y > 0.8f);

        {
            bool hit = false;
            var corrected = Vector3.Zero;
            if (hit != lastHit) { accum = Vector3.Zero; sampleCount = 0; lastHit = hit; }
            if (sampleCount < maxSamples) sampleCount++;
            accum += (corrected - accum) / sampleCount;
        }

        Assert.AreEqual(0f, accum.Y, 0.001f);
    }
}
