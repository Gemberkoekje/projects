using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

[TestClass]
public class SpectralColorTests
{
    private static readonly WavelengthLookup _wavelengthLookup = new();

    // ── Helper ──────────────────────────────────────────────────────
    private static float LinearToSRGB(float linear)
    {
        if (linear <= 0.0031308f)
            return 12.92f * linear;
        else
            return 1.055f * MathF.Pow(linear, 1.0f / 2.4f) - 0.055f;
    }

    private static Vector3 GammaCorrect(Vector3 linear) =>
        new(LinearToSRGB(linear.X), LinearToSRGB(linear.Y), LinearToSRGB(linear.Z));

    private static (byte R, byte G, byte B) ToByteRGB(Vector3 gammaCorrected) =>
    (
        (byte)Math.Clamp(gammaCorrected.X * 255f, 0, 255),
        (byte)Math.Clamp(gammaCorrected.Y * 255f, 0, 255),
        (byte)Math.Clamp(gammaCorrected.Z * 255f, 0, 255)
    );

    /// <summary>
    /// Simulates the full single-wavelength pipeline:
    ///   CIE XYZ(λ) × reflectance × DeterministicCorrection → XYZ → linear sRGB
    /// </summary>
    private static Vector3 SingleWavelengthToLinearSRGB(int wavelength, float reflectance = 1f)
    {
        _wavelengthLookup.TryGet(wavelength, out var xyz);
        var corrected = xyz * reflectance * _wavelengthLookup.DeterministicCorrection;
        return JobSystem.ToSRGB(corrected);
    }

    // ────────────────────────────────────────────────────────────────
    //  1.  XYZ → sRGB matrix: D65 white point must map to (1,1,1)
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ToSRGB_D65WhitePoint_MapsToLinearWhite()
    {
        // D65 reference white in CIE XYZ
        var d65 = new Vector3(0.95047f, 1.0f, 1.08883f);
        var rgb = JobSystem.ToSRGB(d65);

        Assert.AreEqual(1.0f, rgb.X, 0.01f, "R channel for D65 white");
        Assert.AreEqual(1.0f, rgb.Y, 0.01f, "G channel for D65 white");
        Assert.AreEqual(1.0f, rgb.Z, 0.01f, "B channel for D65 white");
    }

    [TestMethod]
    public void ToSRGB_Origin_MapsToBlack()
    {
        var rgb = JobSystem.ToSRGB(Vector3.Zero);
        Assert.AreEqual(0f, rgb.X);
        Assert.AreEqual(0f, rgb.Y);
        Assert.AreEqual(0f, rgb.Z);
    }

    // ────────────────────────────────────────────────────────────────
    //  2.  Single wavelength → expected dominant channel
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(450, DisplayName = "450 nm → blue dominant")]
    [DataRow(470, DisplayName = "470 nm → blue dominant")]
    public void SingleWavelength_Blue_HasHighestBChannel(int wavelength)
    {
        var rgb = SingleWavelengthToLinearSRGB(wavelength);
        Assert.IsTrue(rgb.Z > rgb.X && rgb.Z > rgb.Y,
            $"At {wavelength} nm: B={rgb.Z} should be > R={rgb.X} and G={rgb.Y}");
    }

    [TestMethod]
    [DataRow(530, DisplayName = "530 nm → green dominant")]
    [DataRow(550, DisplayName = "550 nm → green dominant")]
    public void SingleWavelength_Green_HasHighestGChannel(int wavelength)
    {
        var rgb = SingleWavelengthToLinearSRGB(wavelength);
        Assert.IsTrue(rgb.Y > rgb.X && rgb.Y > rgb.Z,
            $"At {wavelength} nm: G={rgb.Y} should be > R={rgb.X} and B={rgb.Z}");
    }

    [TestMethod]
    [DataRow(620, DisplayName = "620 nm → red dominant")]
    [DataRow(650, DisplayName = "650 nm → red dominant")]
    public void SingleWavelength_Red_HasHighestRChannel(int wavelength)
    {
        var rgb = SingleWavelengthToLinearSRGB(wavelength);
        Assert.IsTrue(rgb.X > rgb.Y && rgb.X > rgb.Z,
            $"At {wavelength} nm: R={rgb.X} should be > G={rgb.Y} and B={rgb.Z}");
    }

    // ────────────────────────────────────────────────────────────────
    //  3.  Full deterministic cycle with reflectance = 1 → white
    //      Proves the complete accumulation pipeline.
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void FullCycle_WhiteSurface_ConvergesToWhite()
    {
        // Simulate cycling through all 50 hero wavelengths with reflectance = 1
        // using the same running-average accumulation as the renderer.
        var accumXYZ = Vector3.Zero;
        uint sampleCount = 0;

        for (int s = 0; s < 50; s++)
        {
            int wavelength = _wavelengthLookup.GetHeroWavelength(0, s);
            _wavelengthLookup.TryGet(wavelength, out var xyz);

            var corrected = xyz * 1.0f * _wavelengthLookup.DeterministicCorrection;

            sampleCount++;
            accumXYZ += (corrected - accumXYZ) / sampleCount;
        }

        // After one full cycle the accumulated Y should be ≈ 1
        Assert.AreEqual(1.0f, accumXYZ.Y, 0.05f,
            $"Accumulated Y = {accumXYZ.Y}, expected ≈ 1.0");

        // Converting to sRGB: the renderer uses an implicit equal-energy illuminant (E),
        // not D65, so the sRGB values won't be exactly (1,1,1). R is slightly > 1
        // because E is "warmer" than D65.  We verify all channels are close to 1
        // and roughly neutral (channels within 0.5 of each other).
        var rgb = JobSystem.ToSRGB(accumXYZ);
        Assert.IsTrue(rgb.X > 0.5f && rgb.X < 2.0f, $"R = {rgb.X}, expected near 1");
        Assert.IsTrue(rgb.Y > 0.5f && rgb.Y < 2.0f, $"G = {rgb.Y}, expected near 1");
        Assert.IsTrue(rgb.Z > 0.5f && rgb.Z < 2.0f, $"B = {rgb.Z}, expected near 1");
        Assert.IsTrue(MathF.Abs(rgb.X - rgb.Y) < 0.5f, $"R-G difference = {MathF.Abs(rgb.X - rgb.Y)}, expected < 0.5");
        Assert.IsTrue(MathF.Abs(rgb.Y - rgb.Z) < 0.5f, $"G-B difference = {MathF.Abs(rgb.Y - rgb.Z)}, expected < 0.5");
    }

    [TestMethod]
    public void FullCycle_WhiteSurface_MultipleCycles_ConvergesCloser()
    {
        // Running multiple full cycles should stay stable around white.
        var accumXYZ = Vector3.Zero;
        uint sampleCount = 0;
        int totalCycles = 10;

        for (int c = 0; c < totalCycles; c++)
        {
            for (int s = 0; s < 50; s++)
            {
                int wavelength = _wavelengthLookup.GetHeroWavelength(0, sampleCount);
                _wavelengthLookup.TryGet(wavelength, out var xyz);

                var corrected = xyz * 1.0f * _wavelengthLookup.DeterministicCorrection;

                sampleCount++;
                accumXYZ += (corrected - accumXYZ) / sampleCount;
            }
        }

        Assert.AreEqual(1.0f, accumXYZ.Y, 0.02f,
            $"After {totalCycles} cycles: Y = {accumXYZ.Y}");

        var rgb = JobSystem.ToSRGB(accumXYZ);
        Assert.IsTrue(rgb.X > 0.5f && rgb.X < 2.0f, $"R = {rgb.X}, expected near 1");
        Assert.IsTrue(rgb.Y > 0.5f && rgb.Y < 2.0f, $"G = {rgb.Y}, expected near 1");
        Assert.IsTrue(rgb.Z > 0.5f && rgb.Z < 2.0f, $"B = {rgb.Z}, expected near 1");
        Assert.IsTrue(MathF.Abs(rgb.X - rgb.Y) < 0.5f, $"R-G difference = {MathF.Abs(rgb.X - rgb.Y)}, expected < 0.5");
        Assert.IsTrue(MathF.Abs(rgb.Y - rgb.Z) < 0.5f, $"G-B difference = {MathF.Abs(rgb.Y - rgb.Z)}, expected < 0.5");
    }

    // ────────────────────────────────────────────────────────────────
    //  4.  Uniform 50 % reflectance → half-brightness, still neutral
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void FullCycle_HalfReflectance_HalfBrightness()
    {
        var accumXYZ = Vector3.Zero;
        uint sampleCount = 0;

        for (int c = 0; c < 10; c++)
        {
            for (int s = 0; s < 50; s++)
            {
                int wavelength = _wavelengthLookup.GetHeroWavelength(0, sampleCount);
                _wavelengthLookup.TryGet(wavelength, out var xyz);

                var corrected = xyz * 0.5f * _wavelengthLookup.DeterministicCorrection;

                sampleCount++;
                accumXYZ += (corrected - accumXYZ) / sampleCount;
            }
        }

        // Y should be ≈ 0.5
        Assert.AreEqual(0.5f, accumXYZ.Y, 0.05f,
            $"Y = {accumXYZ.Y}, expected ≈ 0.5 for 50% reflectance");
    }

    // ────────────────────────────────────────────────────────────────
    //  5.  Red-only reflectance → dominant red sRGB
    //      Reflectance = 1 for λ ∈ [600, 700], 0 otherwise.
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void FullCycle_RedReflectance_ProducesRedDominantColor()
    {
        var accumXYZ = Vector3.Zero;
        uint sampleCount = 0;

        for (int c = 0; c < 10; c++)
        {
            for (int s = 0; s < 50; s++)
            {
                int wavelength = _wavelengthLookup.GetHeroWavelength(0, sampleCount);
                _wavelengthLookup.TryGet(wavelength, out var xyz);

                float reflectance = (wavelength >= 600 && wavelength <= 700) ? 1.0f : 0.0f;
                var corrected = xyz * reflectance * _wavelengthLookup.DeterministicCorrection;

                sampleCount++;
                accumXYZ += (corrected - accumXYZ) / sampleCount;
            }
        }

        var rgb = JobSystem.ToSRGB(accumXYZ);
        Assert.IsTrue(rgb.X > 0, "Red channel should be positive");
        Assert.IsTrue(rgb.X > rgb.Y, "R should be > G for red reflectance");
        Assert.IsTrue(rgb.X > rgb.Z, "R should be > B for red reflectance");
    }

    // ────────────────────────────────────────────────────────────────
    //  6.  Blue-only reflectance → dominant blue sRGB
    //      Reflectance = 1 for λ ∈ [400, 490], 0 otherwise.
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void FullCycle_BlueReflectance_ProducesBlueDominantColor()
    {
        var accumXYZ = Vector3.Zero;
        uint sampleCount = 0;

        for (int c = 0; c < 10; c++)
        {
            for (int s = 0; s < 50; s++)
            {
                int wavelength = _wavelengthLookup.GetHeroWavelength(0, sampleCount);
                _wavelengthLookup.TryGet(wavelength, out var xyz);

                float reflectance = (wavelength >= 400 && wavelength <= 490) ? 1.0f : 0.0f;
                var corrected = xyz * reflectance * _wavelengthLookup.DeterministicCorrection;

                sampleCount++;
                accumXYZ += (corrected - accumXYZ) / sampleCount;
            }
        }

        var rgb = JobSystem.ToSRGB(accumXYZ);
        Assert.IsTrue(rgb.Z > 0, "Blue channel should be positive");
        Assert.IsTrue(rgb.Z > rgb.Y, "B should be > G for blue reflectance");
    }

    // ────────────────────────────────────────────────────────────────
    //  7.  Green-only reflectance → dominant green sRGB
    //      Reflectance = 1 for λ ∈ [500, 570], 0 otherwise.
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void FullCycle_GreenReflectance_ProducesGreenDominantColor()
    {
        var accumXYZ = Vector3.Zero;
        uint sampleCount = 0;

        for (int c = 0; c < 10; c++)
        {
            for (int s = 0; s < 50; s++)
            {
                int wavelength = _wavelengthLookup.GetHeroWavelength(0, sampleCount);
                _wavelengthLookup.TryGet(wavelength, out var xyz);

                float reflectance = (wavelength >= 500 && wavelength <= 570) ? 1.0f : 0.0f;
                var corrected = xyz * reflectance * _wavelengthLookup.DeterministicCorrection;

                sampleCount++;
                accumXYZ += (corrected - accumXYZ) / sampleCount;
            }
        }

        var rgb = JobSystem.ToSRGB(accumXYZ);
        Assert.IsTrue(rgb.Y > 0, "Green channel should be positive");
        Assert.IsTrue(rgb.Y > rgb.Z, "G should be > B for green reflectance");
    }

    // ────────────────────────────────────────────────────────────────
    //  8.  Matrix3x3 sanity: known hand-calculated XYZ → sRGB
    //      Using the standard matrix row-vector product.
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Matrix_PureYInput_ProducesCorrectSRGB()
    {
        // Pure luminance (0, 1, 0) through the sRGB matrix
        // Row 1: 0*3.2406 + 1*(-1.5372) + 0*(-0.4986) = -1.5372
        // Row 2: 0*(-0.9689) + 1*1.8758 + 0*0.0415   =  1.8758
        // Row 3: 0*0.0557 + 1*(-0.2040) + 0*1.0570    = -0.2040
        var result = JobSystem.ToSRGB(new Vector3(0, 1, 0));

        Assert.AreEqual(-1.5372f, result.X, 0.001f, "R for pure Y");
        Assert.AreEqual(1.8758f, result.Y, 0.001f, "G for pure Y");
        Assert.AreEqual(-0.2040f, result.Z, 0.001f, "B for pure Y");
    }

    [TestMethod]
    public void Matrix_PureXInput_ProducesCorrectSRGB()
    {
        // Pure X = (1, 0, 0) through the sRGB matrix
        // Row 1: 1*3.2406 = 3.2406
        // Row 2: 1*(-0.9689) = -0.9689
        // Row 3: 1*0.0557 = 0.0557
        var result = JobSystem.ToSRGB(new Vector3(1, 0, 0));

        Assert.AreEqual(3.2406f, result.X, 0.001f, "R for pure X");
        Assert.AreEqual(-0.9689f, result.Y, 0.001f, "G for pure X");
        Assert.AreEqual(0.0557f, result.Z, 0.001f, "B for pure X");
    }

    // ────────────────────────────────────────────────────────────────
    //  9.  No reflectance → black
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void FullCycle_ZeroReflectance_ProducesBlack()
    {
        var accumXYZ = Vector3.Zero;
        uint sampleCount = 0;

        for (int s = 0; s < 50; s++)
        {
            int wavelength = _wavelengthLookup.GetHeroWavelength(0, s);
            _wavelengthLookup.TryGet(wavelength, out var xyz);

            var corrected = xyz * 0.0f * _wavelengthLookup.DeterministicCorrection;

            sampleCount++;
            accumXYZ += (corrected - accumXYZ) / sampleCount;
        }

        var rgb = JobSystem.ToSRGB(accumXYZ);
        Assert.AreEqual(0f, rgb.X, 0.001f, "R should be 0");
        Assert.AreEqual(0f, rgb.Y, 0.001f, "G should be 0");
        Assert.AreEqual(0f, rgb.Z, 0.001f, "B should be 0");
    }

    // ────────────────────────────────────────────────────────────────
    // 10.  Gamma correction: known linear → sRGB byte values
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void GammaCorrection_KnownValues()
    {
        // Linear 0 → 0
        Assert.AreEqual(0f, LinearToSRGB(0f), 0.001f);

        // Linear 1 → 1
        Assert.AreEqual(1f, LinearToSRGB(1f), 0.001f);

        // Linear 0.5 → ~0.735 (sRGB gamma)
        float half = LinearToSRGB(0.5f);
        Assert.IsTrue(half > 0.7f && half < 0.76f,
            $"LinearToSRGB(0.5) = {half}, expected ~0.735");
    }

    // ────────────────────────────────────────────────────────────────
    // 11.  Full pipeline: white surface → byte (255, 255, 255)
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void FullPipeline_WhiteSurface_ProducesWhiteBytes()
    {
        var accumXYZ = Vector3.Zero;
        uint sampleCount = 0;

        for (int c = 0; c < 10; c++)
        {
            for (int s = 0; s < 50; s++)
            {
                int wavelength = _wavelengthLookup.GetHeroWavelength(0, sampleCount);
                _wavelengthLookup.TryGet(wavelength, out var xyz);

                var corrected = xyz * 1.0f * _wavelengthLookup.DeterministicCorrection;

                sampleCount++;
                accumXYZ += (corrected - accumXYZ) / sampleCount;
            }
        }

        var linear = JobSystem.ToSRGB(accumXYZ);
        var gamma = GammaCorrect(linear);
        var (R, G, B) = ToByteRGB(gamma);

        // For equal-energy white, sRGB channels should be near 255
        Assert.IsTrue(R >= 230, $"R = {R}, expected ≥ 230");
        Assert.IsTrue(G >= 230, $"G = {G}, expected ≥ 230");
        Assert.IsTrue(B >= 230, $"B = {B}, expected ≥ 230");
    }
}
