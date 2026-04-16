using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

[TestClass]
public class AccumulationConvergenceTests
{
    private static readonly WavelengthLookup Wl = new();

    [TestMethod]
    public void MatrixOperator_IsNotTransposed()
    {
        var result = new Vector3(0, 0, 1) * JobSystem.TosRGBMatrix;

        Assert.AreEqual(-0.4986f, result.X, 0.001f);
        Assert.AreEqual(0.0415f, result.Y, 0.001f);
        Assert.AreEqual(1.0570f, result.Z, 0.001f);
    }

    [TestMethod]
    public void MatrixOperator_FullVector_MatchesHandCalculation()
    {
        var v = new Vector3(0.4f, 0.3f, 0.2f);
        var m = JobSystem.TosRGBMatrix;

        float expectedR = v.X * m.M11 + v.Y * m.M12 + v.Z * m.M13;
        float expectedG = v.X * m.M21 + v.Y * m.M22 + v.Z * m.M23;
        float expectedB = v.X * m.M31 + v.Y * m.M32 + v.Z * m.M33;

        var result = v * m;

        Assert.AreEqual(expectedR, result.X, 0.0001f);
        Assert.AreEqual(expectedG, result.Y, 0.0001f);
        Assert.AreEqual(expectedB, result.Z, 0.0001f);
    }

    [TestMethod]
    public void ToSRGB_DoesNotDivideByD65()
    {
        var d65 = new Vector3(0.95047f, 1.0f, 1.08883f);
        var rgb = JobSystem.ToSRGB(d65);

        Assert.AreEqual(1.0f, rgb.X, 0.01f);
        Assert.AreEqual(1.0f, rgb.Y, 0.01f);
        Assert.AreEqual(1.0f, rgb.Z, 0.01f);
    }

    [TestMethod]
    public void DeterministicCorrection_AloneNormalizesY()
    {
        var accumXYZ = Vector3.Zero;
        uint count = 0;

        for (int s = 0; s < 50; s++)
        {
            int wavelength = Wl.GetHeroWavelength(0, s);
            Wl.TryGet(wavelength, out var xyz);
            var corrected = xyz * Wl.DeterministicCorrection;
            count++;
            accumXYZ += (corrected - accumXYZ) / count;
        }

        Assert.AreEqual(1.0f, accumXYZ.Y, 0.05f);
    }

    [TestMethod]
    public void PdfCorrection_WouldCauseMassiveOverexposure()
    {
        var accumXYZ = Vector3.Zero;
        uint count = 0;

        for (int s = 0; s < 50; s++)
        {
            int wavelength = Wl.GetHeroWavelength(0, s);
            Wl.TryGet(wavelength, out var xyz);
            float pdfCorrection = 1f / Wl.GetHeroWavelengthProbability(wavelength);
            var corrected = xyz * Wl.DeterministicCorrection * pdfCorrection;
            count++;
            accumXYZ += (corrected - accumXYZ) / count;
        }

        Assert.IsTrue(accumXYZ.Y > 10f);
    }

    [TestMethod]
    public void RunningAverage_IsStable_WithoutHalving()
    {
        var accumXYZ = Vector3.Zero;
        uint count = 0;
        float maxDeviation = 0;

        for (int s = 0; s < 500; s++)
        {
            int wavelength = Wl.GetHeroWavelength(0, s);
            Wl.TryGet(wavelength, out var xyz);
            var corrected = xyz * Wl.DeterministicCorrection;

            count++;
            accumXYZ += (corrected - accumXYZ) / count;

            if (s >= 100)
                maxDeviation = MathF.Max(maxDeviation, MathF.Abs(accumXYZ.Y - 1.0f));
        }

        Assert.IsTrue(maxDeviation < 0.15f);
    }

    [TestMethod]
    public void HalvingSampleCount_CorruptsAverage()
    {
        var accumXYZ = Vector3.Zero;
        uint count = 0;

        for (int s = 0; s < 100; s++)
        {
            int wavelength = Wl.GetHeroWavelength(0, s);
            Wl.TryGet(wavelength, out var xyz);
            var corrected = xyz * Wl.DeterministicCorrection;
            count++;
            accumXYZ += (corrected - accumXYZ) / count;
        }

        var stableY = accumXYZ.Y;
        count /= 2;

        for (int s = 100; s < 110; s++)
        {
            int wavelength = Wl.GetHeroWavelength(0, s);
            Wl.TryGet(wavelength, out var xyz);
            var corrected = xyz * Wl.DeterministicCorrection;
            count++;
            accumXYZ += (corrected - accumXYZ) / count;
        }

        float deviation = MathF.Abs(accumXYZ.Y - stableY);
        Assert.IsTrue(deviation > 0.001f);
    }

    [TestMethod]
    public void EMA_SampleCountCapsAtMax()
    {
        uint maxSamples = 500;
        uint count = 0;
        var accum = Vector3.Zero;

        for (int s = 0; s < 2000; s++)
        {
            int wavelength = Wl.GetHeroWavelength(0, s);
            Wl.TryGet(wavelength, out var xyz);
            var corrected = xyz * Wl.DeterministicCorrection;

            if (count < maxSamples)
                count++;
            accum += (corrected - accum) / count;
        }

        Assert.AreEqual(maxSamples, count);
    }

    [TestMethod]
    public void EMA_AverageStillUpdatesAfterCap()
    {
        uint maxSamples = 100;
        uint count = 0;
        var accum = Vector3.Zero;

        for (int s = 0; s < 200; s++)
        {
            int wavelength = Wl.GetHeroWavelength(0, s);
            Wl.TryGet(wavelength, out var xyz);
            var corrected = xyz * Wl.DeterministicCorrection;
            if (count < maxSamples) count++;
            accum += (corrected - accum) / count;
        }

        var whiteY = accum.Y;

        for (int s = 200; s < 1200; s++)
        {
            int wavelength = Wl.GetHeroWavelength(0, s);
            Wl.TryGet(wavelength, out var xyz);
            var corrected = xyz * 0.5f * Wl.DeterministicCorrection;
            accum += (corrected - accum) / count;
        }

        Assert.IsTrue(accum.Y < whiteY * 0.8f);
        Assert.IsTrue(accum.Y > 0.3f);
    }
}
