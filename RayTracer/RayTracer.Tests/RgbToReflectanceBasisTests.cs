using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// Pins the RGB→reflectance basis (plan §3.1) used to shade image decals through the
/// spectral pipeline: a linear-sRGB colour must round-trip through the pipeline's
/// reflectance→colour forward map, since the basis is that map's exact right inverse.
/// </summary>
[TestClass]
public sealed class RgbToReflectanceBasisTests
{
    private static RgbToReflectanceBasis BuildBasis()
    {
        var wavelengths = new WavelengthLookup();
        // The basis only needs DeterXYZ + correction, so an empty material list is fine.
        SpectralResources spectral = SpectralResourceBaker.Bake(wavelengths, new List<MaterialData>());
        return RgbToReflectanceBasis.Build(spectral);
    }

    [TestMethod]
    [DataRow(1f, 1f, 1f, DisplayName = "white")]
    [DataRow(0.5f, 0.5f, 0.5f, DisplayName = "gray")]
    [DataRow(1f, 0f, 0f, DisplayName = "red")]
    [DataRow(0f, 1f, 0f, DisplayName = "green")]
    [DataRow(0f, 0f, 1f, DisplayName = "blue")]
    [DataRow(0.8f, 0.3f, 0.1f, DisplayName = "brick-ish")]
    [DataRow(0.1f, 0.2f, 0.7f, DisplayName = "blue-ish")]
    public void Reflectance_RoundTripsThroughForwardMap(float r, float g, float b)
    {
        RgbToReflectanceBasis basis = BuildBasis();
        var c = new Vector3(r, g, b);

        float[] refl = basis.Reflectance(c);
        Vector3 back = basis.ForwardLinearRgb(refl);

        Assert.AreEqual(c.X, back.X, 1e-3f, "R round-trip");
        Assert.AreEqual(c.Y, back.Y, 1e-3f, "G round-trip");
        Assert.AreEqual(c.Z, back.Z, 1e-3f, "B round-trip");
    }

    [TestMethod]
    public void Basis_HasExpectedShape()
    {
        RgbToReflectanceBasis basis = BuildBasis();
        Assert.AreEqual(50, basis.Count);
        Assert.AreEqual(basis.Count * 3, basis.Data.Length);
    }

    [TestMethod]
    public void White_HasPositivePlausibleMeanReflectance()
    {
        RgbToReflectanceBasis basis = BuildBasis();
        float[] refl = basis.Reflectance(new Vector3(1, 1, 1));
        float mean = refl.Average();
        // White integrates to luminance ~1, so its reflectance is broadly ~1.
        Assert.IsTrue(mean is > 0.2f and < 2f, $"White mean reflectance {mean} out of plausible range.");
    }
}
