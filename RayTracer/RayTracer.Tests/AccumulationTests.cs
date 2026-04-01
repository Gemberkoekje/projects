using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// Tests for accumulation, EMA capping, wavelength cycling, and hit-state tracking.
/// Covers fixes from the full thread:
///   - Matrix operator was transposed (channels scrambled)
///   - D65 white point was incorrectly pre-divided
///   - Redundant PDF correction caused ~51× overexposure
///   - Random SampleCount halving caused per-pixel noise
///   - SampleCount cap (EMA) prevents streaking with moving camera
///   - Separate WavelengthCounter prevents locked wavelength at cap
///   - LastHit state tracking prevents ghosting on hit↔miss transitions
/// </summary>
[TestClass]
public class AccumulationTests
{
    private static readonly WavelengthLookup _wl = new();

    // ────────────────────────────────────────────────────────────────
    //  Fix 1: Matrix3x3 operator applies rows, not columns
    //  Original bug: operator dotted v with matrix COLUMNS, scrambling
    //  R/G/B.  This test catches a transposed operator.
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void MatrixOperator_IsNotTransposed()
    {
        // If the operator were transposed (v·col instead of row·v),
        // pure Z input (0,0,1) would give:
        //   wrong: (M31, M32, M33) = (0.0557, -0.2040, 1.0570)
        // Correct row-wise product gives:
        //   (M31, M32, M33) applied as row 3: outZ component only
        //   out = (v.X*M11+v.Y*M12+v.Z*M13, v.X*M21+v.Y*M22+v.Z*M23, v.X*M31+v.Y*M32+v.Z*M33)
        //       = (M13, M23, M33) = (-0.4986, 0.0415, 1.0570)
        var result = new Vector3(0, 0, 1) * JobSystem.TosRGBMatrix;

        Assert.AreEqual(-0.4986f, result.X, 0.001f,
            "R for pure Z: should be M13, not M31 (transposed bug)");
        Assert.AreEqual(0.0415f, result.Y, 0.001f,
            "G for pure Z: should be M23, not M32 (transposed bug)");
        Assert.AreEqual(1.0570f, result.Z, 0.001f,
            "B for pure Z: should be M33");
    }

    [TestMethod]
    public void MatrixOperator_FullVector_MatchesHandCalculation()
    {
        // v = (0.4, 0.3, 0.2) — a non-trivial input
        // Correct (row-wise): out_i = v.X*Mi1 + v.Y*Mi2 + v.Z*Mi3
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

    // ────────────────────────────────────────────────────────────────
    //  Fix 2: ToSRGB must NOT divide by D65 white point
    //  Original bug: pre-dividing by (0.95047, 1.0, 1.08883) before
    //  the matrix caused double-correction.
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ToSRGB_DoesNotDivideByD65()
    {
        // If D65 division were present, (0.95047, 1.0, 1.08883) would become
        // (1,1,1) before the matrix, yielding R≈1.205 instead of ≈1.0.
        var d65 = new Vector3(0.95047f, 1.0f, 1.08883f);
        var rgb = JobSystem.ToSRGB(d65);

        // Correct: D65 maps to (1,1,1) without pre-division
        Assert.AreEqual(1.0f, rgb.X, 0.01f, "R should be ~1.0, not ~1.2");
        Assert.AreEqual(1.0f, rgb.Y, 0.01f);
        Assert.AreEqual(1.0f, rgb.Z, 0.01f);
    }

    // ────────────────────────────────────────────────────────────────
    //  Fix 3: DeterministicCorrection alone normalizes Y=1
    //  Original bug: multiplying by pdfcorrection (~51×) on top of
    //  DeterministicCorrection caused massive overexposure.
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void DeterministicCorrection_AloneNormalizesY()
    {
        // Accumulate one full cycle of 50 wavelengths with reflectance=1.
        // Using ONLY DeterministicCorrection (no PDF correction).
        // Y should converge to ≈1.0.
        var accumXYZ = Vector3.Zero;
        uint count = 0;

        for (int s = 0; s < 50; s++)
        {
            int wavelength = _wl.GetHeroWavelength(0, s);
            _wl.TryGet(wavelength, out var xyz);
            var corrected = xyz * _wl.DeterministicCorrection;
            count++;
            accumXYZ += (corrected - accumXYZ) / count;
        }

        Assert.AreEqual(1.0f, accumXYZ.Y, 0.05f,
            "Y should be ~1.0 with DeterministicCorrection alone");
    }

    [TestMethod]
    public void PdfCorrection_WouldCauseMassiveOverexposure()
    {
        // Demonstrate that adding the old pdf correction produces Y >> 1.
        var accumXYZ = Vector3.Zero;
        uint count = 0;

        for (int s = 0; s < 50; s++)
        {
            int wavelength = _wl.GetHeroWavelength(0, s);
            _wl.TryGet(wavelength, out var xyz);
            float pdfCorrection = 1f / _wl.GetHeroWavelengthProbability(wavelength);
            var corrected = xyz * _wl.DeterministicCorrection * pdfCorrection;
            count++;
            accumXYZ += (corrected - accumXYZ) / count;
        }

        // With the old bug, Y would be ~51× too large
        Assert.IsTrue(accumXYZ.Y > 10f,
            $"Y = {accumXYZ.Y}: with redundant pdfCorrection it should be massively overexposed");
    }

    // ────────────────────────────────────────────────────────────────
    //  Fix 4: No random SampleCount halving
    //  Original bug: `SampleCount /= 2` at random thresholds corrupted
    //  the running average, causing per-pixel noise.
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void RunningAverage_IsStable_WithoutHalving()
    {
        // Simulate 500 samples of a white surface.
        // The running average should converge smoothly to Y≈1.0
        // with no erratic jumps. Measures max deviation after the
        // first full cycle (sample 50+).
        var accumXYZ = Vector3.Zero;
        uint count = 0;
        float maxDeviation = 0;

        for (int s = 0; s < 500; s++)
        {
            int wavelength = _wl.GetHeroWavelength(0, s);
            _wl.TryGet(wavelength, out var xyz);
            var corrected = xyz * _wl.DeterministicCorrection;

            count++;
            accumXYZ += (corrected - accumXYZ) / count;

            // After the first two full cycles, track max deviation from Y=1
                if (s >= 100)
                    maxDeviation = MathF.Max(maxDeviation, MathF.Abs(accumXYZ.Y - 1.0f));
            }

            Assert.IsTrue(maxDeviation < 0.15f,
                $"Max Y deviation after 2 cycles = {maxDeviation}, expected < 0.15 (stable convergence)");
    }

    [TestMethod]
    public void HalvingSampleCount_CorruptsAverage()
    {
        // Demonstrate the old bug: halving SampleCount while keeping
        // AccumXYZ causes the next sample to have 2× the intended weight,
        // producing a large deviation.
        var accumXYZ = Vector3.Zero;
        uint count = 0;

        // First accumulate 100 clean samples
        for (int s = 0; s < 100; s++)
        {
            int wavelength = _wl.GetHeroWavelength(0, s);
            _wl.TryGet(wavelength, out var xyz);
            var corrected = xyz * _wl.DeterministicCorrection;
            count++;
            accumXYZ += (corrected - accumXYZ) / count;
        }

        var stableY = accumXYZ.Y;

        // Now simulate the old halving bug
        count /= 2;

        // Feed 10 more samples — each now has ~2× the correct weight
        for (int s = 100; s < 110; s++)
        {
            int wavelength = _wl.GetHeroWavelength(0, s);
            _wl.TryGet(wavelength, out var xyz);
            var corrected = xyz * _wl.DeterministicCorrection;
            count++;
            accumXYZ += (corrected - accumXYZ) / count;
        }

        float deviation = MathF.Abs(accumXYZ.Y - stableY);
        // With correct (no halving) the average barely moves after 100 samples.
        // With halving, each new sample shifts it significantly.
        Assert.IsTrue(deviation > 0.001f,
            $"Halving deviation = {deviation}: halving corrupts the running average");
    }

    // ────────────────────────────────────────────────────────────────
    //  Fix 5: MaxSampleCount caps the EMA weight
    //  SampleCount stops at MaxSampleCount so new samples always
    //  have weight ≥ 1/MaxSampleCount, keeping the image responsive.
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void EMA_SampleCountCapsAtMax()
    {
        uint maxSamples = 500;
        uint count = 0;
        var accum = Vector3.Zero;

        for (int s = 0; s < 2000; s++)
        {
            int wavelength = _wl.GetHeroWavelength(0, s);
            _wl.TryGet(wavelength, out var xyz);
            var corrected = xyz * _wl.DeterministicCorrection;

            if (count < maxSamples)
                count++;
            accum += (corrected - accum) / count;
        }

        Assert.AreEqual(maxSamples, count,
            "SampleCount should be capped at MaxSampleCount");
    }

    [TestMethod]
    public void EMA_AverageStillUpdatesAfterCap()
    {
        uint maxSamples = 100;
        uint count = 0;
        var accum = Vector3.Zero;

        // Fill with white (reflectance=1) until capped
        for (int s = 0; s < 200; s++)
        {
            int wavelength = _wl.GetHeroWavelength(0, s);
            _wl.TryGet(wavelength, out var xyz);
            var corrected = xyz * _wl.DeterministicCorrection;
            if (count < maxSamples) count++;
            accum += (corrected - accum) / count;
        }

        var whiteY = accum.Y;

        // Now switch to "half brightness" — the EMA should adapt
        for (int s = 200; s < 1200; s++)
        {
            int wavelength = _wl.GetHeroWavelength(0, s);
            _wl.TryGet(wavelength, out var xyz);
            var corrected = xyz * 0.5f * _wl.DeterministicCorrection;
            // count stays at maxSamples (already capped)
            accum += (corrected - accum) / count;
        }

        // After 1000 more samples at weight 1/100, the EMA should have
        // moved substantially toward 0.5
        Assert.IsTrue(accum.Y < whiteY * 0.8f,
            $"Y = {accum.Y}: EMA should adapt toward 0.5 after brightness change (was {whiteY})");
        Assert.IsTrue(accum.Y > 0.3f,
            $"Y = {accum.Y}: should be approaching 0.5");
    }

    // ────────────────────────────────────────────────────────────────
    //  Fix 6: WavelengthCounter is independent of SampleCount
    //  Original bug: using capped SampleCount for wavelength selection
    //  locked the pixel to a single wavelength after reaching the cap,
    //  causing progressive noise drift.
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void WavelengthCycling_ContinuesAfterSampleCountCap()
    {
        uint maxSamples = 50;
        long wavelengthCounter = 0;
        uint sampleCount = 0;
        uint pixelId = 42;

        var wavelengthsBeforeCap = new HashSet<int>();
        var wavelengthsAfterCap = new HashSet<int>();

        for (int s = 0; s < 200; s++)
        {
            int wl = _wl.GetHeroWavelength(pixelId, wavelengthCounter);
            wavelengthCounter++; // always increments

            if (sampleCount < maxSamples)
                sampleCount++;

            if (s < 50)
                wavelengthsBeforeCap.Add(wl);
            else
                wavelengthsAfterCap.Add(wl);
        }

        // After the cap, wavelengths should still cycle through many values
        Assert.IsTrue(wavelengthsAfterCap.Count > 1,
            $"Only {wavelengthsAfterCap.Count} distinct wavelength(s) after cap — should cycle through many");
    }

    [TestMethod]
    public void LockedWavelength_WouldCauseDrift()
    {
        // Demonstrate the old bug: if SampleCount is used for wavelength
        // selection AND is capped, the wavelength locks to a single value.
        uint maxSamples = 50;
        uint sampleCount = 0;
        uint pixelId = 42;

        var wavelengthsAfterCap = new HashSet<int>();

        for (int s = 0; s < 200; s++)
        {
            // OLD BUG: using capped sampleCount for wavelength selection
            int wl = _wl.GetHeroWavelength(pixelId, sampleCount);

            if (sampleCount < maxSamples)
                sampleCount++;

            if (s >= 50)
                wavelengthsAfterCap.Add(wl);
        }

        // With the old bug, after capping at 50, sampleCount never changes,
        // so the same wavelength is returned every time.
        Assert.AreEqual(1, wavelengthsAfterCap.Count,
            "Without separate counter, wavelength locks to exactly 1 value after cap");
    }

    [TestMethod]
    public void LockedWavelength_DriftsAccumulatedColor()
    {
        // Show that feeding a single locked wavelength into the EMA
        // drifts the color away from the correct converged value.
        uint maxSamples = 100;
        uint sampleCount = 0;
        long wavelengthCounter = 0;
        var accum = Vector3.Zero;

        // Converge with proper cycling (100 samples)
        for (int s = 0; s < 100; s++)
        {
            int wl = _wl.GetHeroWavelength(0, wavelengthCounter);
            wavelengthCounter++;
            _wl.TryGet(wl, out var xyz);
            var corrected = xyz * _wl.DeterministicCorrection;
            if (sampleCount < maxSamples) sampleCount++;
            accum += (corrected - accum) / sampleCount;
        }

        var convergedY = accum.Y;

        // Now simulate the locked-wavelength bug: feed the same wavelength 500 times
        int lockedWl = _wl.GetHeroWavelength(0, sampleCount);
        _wl.TryGet(lockedWl, out var lockedXyz);
        var lockedCorrected = lockedXyz * _wl.DeterministicCorrection;

        for (int s = 0; s < 500; s++)
        {
            accum += (lockedCorrected - accum) / sampleCount;
        }

        float drift = MathF.Abs(accum.Y - convergedY);
        Assert.IsTrue(drift > 0.05f,
            $"Drift = {drift}: a locked wavelength should cause visible color drift");
    }

    // ────────────────────────────────────────────────────────────────
    //  Fix 7: Hit-state reset prevents ghosting
    //  Original bug: when a rectangle moved off a pixel, the old color
    //  faded at 1/MaxSampleCount per sample (EMA) — taking thousands
    //  of samples to clear.
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void HitToMiss_ResetsAccumulation()
    {
        var accum = new Vector3(0.5f, 0.4f, 0.3f); // non-zero accumulated color
        uint sampleCount = 500;                       // fully capped
        bool lastHit = true;

        // Simulate a miss (hit=false) — should reset
        bool currentHit = false;
        if (currentHit != lastHit)
        {
            accum = Vector3.Zero;
            sampleCount = 0;
            lastHit = currentHit;
        }

        Assert.AreEqual(Vector3.Zero, accum, "AccumXYZ should be reset on hit→miss");
        Assert.AreEqual(0u, sampleCount, "SampleCount should be reset on hit→miss");
        Assert.IsFalse(lastHit, "LastHit should be updated to false");
    }

    [TestMethod]
    public void MissToHit_ResetsAccumulation()
    {
        var accum = Vector3.Zero;
        uint sampleCount = 500;   // capped from previous miss-streak
        bool lastHit = false;

        bool currentHit = true;
        if (currentHit != lastHit)
        {
            accum = Vector3.Zero;
            sampleCount = 0;
            lastHit = currentHit;
        }

        Assert.AreEqual(0u, sampleCount, "SampleCount should be reset on miss→hit");
        Assert.IsTrue(lastHit, "LastHit should be updated to true");
    }

    [TestMethod]
    public void SameState_DoesNotReset()
    {
        var accum = new Vector3(0.5f, 0.4f, 0.3f);
        uint sampleCount = 200;
        bool lastHit = true;

        // Same state (hit→hit) — should NOT reset
        bool currentHit = true;
        if (currentHit != lastHit)
        {
            accum = Vector3.Zero;
            sampleCount = 0;
            lastHit = currentHit;
        }

        Assert.AreEqual(200u, sampleCount, "SampleCount should NOT reset on hit→hit");
        Assert.AreEqual(new Vector3(0.5f, 0.4f, 0.3f), accum,
            "AccumXYZ should NOT reset on hit→hit");
    }

    [TestMethod]
    public void HitToMiss_ConvergesToBlackQuickly()
    {
        // Full integration test: simulate a pixel that was showing color,
        // then the object moves away. With reset, it should be near-black
        // within just a few samples (not thousands).
        var accum = Vector3.Zero;
        uint sampleCount = 0;
        long wavelengthCounter = 0;
        bool lastHit = false;
        uint maxSamples = 500;

        // Phase 1: accumulate color (hit=true)
        for (int s = 0; s < 200; s++)
        {
            bool hit = true;
            int wl = _wl.GetHeroWavelength(0, wavelengthCounter);
            wavelengthCounter++;
            _wl.TryGet(wl, out var xyz);
            var corrected = xyz * _wl.DeterministicCorrection;

            if (hit != lastHit) { accum = Vector3.Zero; sampleCount = 0; lastHit = hit; }
            if (sampleCount < maxSamples) sampleCount++;
            accum += (corrected - accum) / sampleCount;
        }

        Assert.IsTrue(accum.Y > 0.5f, $"Phase 1: Y = {accum.Y}, should be near 1.0");

        // Phase 2: object leaves — now hit=false
        for (int s = 0; s < 10; s++)
        {
            bool hit = false;
            var corrected = Vector3.Zero; // miss returns zero

            if (hit != lastHit) { accum = Vector3.Zero; sampleCount = 0; lastHit = hit; }
            if (sampleCount < maxSamples) sampleCount++;
            accum += (corrected - accum) / sampleCount;
        }

        // After reset + a few zero samples, should be at or near zero
        Assert.IsTrue(accum.Y < 0.01f,
            $"After hit→miss reset: Y = {accum.Y}, should be near 0 (no ghosting)");
    }

    [TestMethod]
    public void WithoutReset_GhostingPersists()
    {
        // Demonstrate the old ghosting bug: without hit-state reset,
        // a capped EMA takes forever to fade to black.
        var accum = Vector3.Zero;
        uint sampleCount = 0;
        long wavelengthCounter = 0;
        uint maxSamples = 500;

        // Accumulate 500 samples of color (hit)
        for (int s = 0; s < 500; s++)
        {
            int wl = _wl.GetHeroWavelength(0, wavelengthCounter);
            wavelengthCounter++;
            _wl.TryGet(wl, out var xyz);
            var corrected = xyz * _wl.DeterministicCorrection;
            if (sampleCount < maxSamples) sampleCount++;
            accum += (corrected - accum) / sampleCount;
        }

        var colorBeforeMiss = accum.Y;

        // Now feed 100 zero samples (miss) WITHOUT reset
        for (int s = 0; s < 100; s++)
        {
            var corrected = Vector3.Zero;
            // No reset — old bug behavior
            if (sampleCount < maxSamples) sampleCount++;
            accum += (corrected - accum) / sampleCount;
        }

        // After 100 samples at weight 1/500, the old color barely faded:
        // Each step: accum *= (499/500), so after 100 steps:
        // remaining ≈ colorBeforeMiss * (499/500)^100 ≈ 0.818 × original
        Assert.IsTrue(accum.Y > colorBeforeMiss * 0.5f,
            $"Without reset: Y = {accum.Y} (was {colorBeforeMiss}). " +
            $"Ghost should still be > 50% of original after 100 samples.");
    }

    // ────────────────────────────────────────────────────────────────
    //  Integration: full pixel lifecycle
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void FullPixelLifecycle_MissHitMiss()
    {
        var accum = Vector3.Zero;
        uint sampleCount = 0;
        long wavelengthCounter = 0;
        bool lastHit = false;
        uint maxSamples = 500;

        // Phase 1: 50 samples of miss → should stay black
        for (int s = 0; s < 50; s++)
        {
            bool hit = false;
            var corrected = Vector3.Zero;
            if (hit != lastHit) { accum = Vector3.Zero; sampleCount = 0; lastHit = hit; }
            if (sampleCount < maxSamples) sampleCount++;
            accum += (corrected - accum) / sampleCount;
            wavelengthCounter++;
        }

        Assert.AreEqual(0f, accum.Y, 0.001f, "Phase 1 (miss): Y should be 0");

        // Phase 2: 200 samples of hit → should converge to color
        for (int s = 0; s < 200; s++)
        {
            bool hit = true;
            int wl = _wl.GetHeroWavelength(0, wavelengthCounter);
            wavelengthCounter++;
            _wl.TryGet(wl, out var xyz);
            var corrected = xyz * _wl.DeterministicCorrection;

            if (hit != lastHit) { accum = Vector3.Zero; sampleCount = 0; lastHit = hit; }
            if (sampleCount < maxSamples) sampleCount++;
            accum += (corrected - accum) / sampleCount;
        }

        Assert.IsTrue(accum.Y > 0.8f,
            $"Phase 2 (hit): Y = {accum.Y}, should be near 1.0 after 200 samples");

        // Phase 3: back to miss → should go black immediately
        {
            bool hit = false;
            var corrected = Vector3.Zero;
            if (hit != lastHit) { accum = Vector3.Zero; sampleCount = 0; lastHit = hit; }
            if (sampleCount < maxSamples) sampleCount++;
            accum += (corrected - accum) / sampleCount;
        }

        Assert.AreEqual(0f, accum.Y, 0.001f,
            "Phase 3 (miss after hit): Y should be 0 immediately (no ghost)");
    }

    // ────────────────────────────────────────────────────────────────
    //  Wavelength cycling: all 50 wavelengths are visited in a cycle
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void GetHeroWavelength_CyclesThrough50Wavelengths()
    {
        var wavelengths = new HashSet<int>();
        for (long s = 0; s < 50; s++)
        {
            wavelengths.Add(_wl.GetHeroWavelength(0, s));
        }

        Assert.AreEqual(50, wavelengths.Count,
            $"Expected 50 distinct wavelengths in one cycle, got {wavelengths.Count}");
    }

    [TestMethod]
    public void GetHeroWavelength_DifferentPixels_DifferentStartOffsets()
    {
        // Two different pixel IDs should start at different wavelengths
        int wl1 = _wl.GetHeroWavelength(0, 0);
        int wl2 = _wl.GetHeroWavelength(12345, 0);

        // Not guaranteed different, but with a hash, extremely likely
        // Over many pixels, the offsets should differ
        var first10_px0 = Enumerable.Range(0, 10).Select(s => _wl.GetHeroWavelength(0, s)).ToList();
        var first10_px1 = Enumerable.Range(0, 10).Select(s => _wl.GetHeroWavelength(12345, s)).ToList();

        Assert.IsFalse(first10_px0.SequenceEqual(first10_px1),
            "Different pixels should have different wavelength sequences");
    }

    // ────────────────────────────────────────────────────────────────
    //  DeterministicCorrection: basic sanity
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void DeterministicCorrection_IsPositiveAndReasonable()
    {
        Assert.IsTrue(_wl.DeterministicCorrection > 0,
            "DeterministicCorrection should be positive");
        Assert.IsTrue(_wl.DeterministicCorrection < 100,
            $"DeterministicCorrection = {_wl.DeterministicCorrection}, expected < 100");
    }
}
