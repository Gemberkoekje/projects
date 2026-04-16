using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

[TestClass]
public class AccumulationSpectralTests
{
    private static readonly WavelengthLookup Wl = new();

    [TestMethod]
    public void WavelengthCycling_ContinuesAfterSampleCountCap()
    {
        uint maxSamples = 50;
        long wavelengthCounter = 0;
        uint sampleCount = 0;
        uint pixelId = 42;

        var wavelengthsAfterCap = new HashSet<int>();

        for (int s = 0; s < 200; s++)
        {
            int wl = Wl.GetHeroWavelength(pixelId, wavelengthCounter);
            wavelengthCounter++;

            if (sampleCount < maxSamples)
                sampleCount++;

            if (s >= 50)
                wavelengthsAfterCap.Add(wl);
        }

        Assert.IsTrue(wavelengthsAfterCap.Count > 1);
    }

    [TestMethod]
    public void LockedWavelength_WouldCauseDrift()
    {
        uint maxSamples = 50;
        uint sampleCount = 0;
        uint pixelId = 42;

        var wavelengthsAfterCap = new HashSet<int>();

        for (int s = 0; s < 200; s++)
        {
            int wl = Wl.GetHeroWavelength(pixelId, sampleCount);

            if (sampleCount < maxSamples)
                sampleCount++;

            if (s >= 50)
                wavelengthsAfterCap.Add(wl);
        }

        Assert.AreEqual(1, wavelengthsAfterCap.Count);
    }

    [TestMethod]
    public void LockedWavelength_DriftsAccumulatedColor()
    {
        uint maxSamples = 100;
        uint sampleCount = 0;
        long wavelengthCounter = 0;
        var accum = Vector3.Zero;

        for (int s = 0; s < 100; s++)
        {
            int wl = Wl.GetHeroWavelength(0, wavelengthCounter);
            wavelengthCounter++;
            Wl.TryGet(wl, out var xyz);
            var corrected = xyz * Wl.DeterministicCorrection;
            if (sampleCount < maxSamples) sampleCount++;
            accum += (corrected - accum) / sampleCount;
        }

        var convergedY = accum.Y;

        int lockedWl = Wl.GetHeroWavelength(0, sampleCount);
        Wl.TryGet(lockedWl, out var lockedXyz);
        var lockedCorrected = lockedXyz * Wl.DeterministicCorrection;

        for (int s = 0; s < 500; s++)
            accum += (lockedCorrected - accum) / sampleCount;

        float drift = MathF.Abs(accum.Y - convergedY);
        Assert.IsTrue(drift > 0.05f);
    }

    [TestMethod]
    public void GetHeroWavelength_CyclesThrough50Wavelengths()
    {
        var wavelengths = new HashSet<int>();
        for (long s = 0; s < 50; s++)
            wavelengths.Add(Wl.GetHeroWavelength(0, s));

        Assert.AreEqual(50, wavelengths.Count);
    }

    [TestMethod]
    public void GetHeroWavelength_DifferentPixels_DifferentStartOffsets()
    {
        var first10Px0 = Enumerable.Range(0, 10).Select(s => Wl.GetHeroWavelength(0, s)).ToList();
        var first10Px1 = Enumerable.Range(0, 10).Select(s => Wl.GetHeroWavelength(12345, s)).ToList();

        Assert.IsFalse(first10Px0.SequenceEqual(first10Px1));
    }

    [TestMethod]
    public void DeterministicCorrection_IsPositiveAndReasonable()
    {
        Assert.IsTrue(Wl.DeterministicCorrection > 0);
        Assert.IsTrue(Wl.DeterministicCorrection < 100);
    }
}
