using System.Numerics;
using RayTracer;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;

namespace Vista.Tests;

/// <summary>The spectral tables integrator v0 uploads (vista-plan §5.1, §6.4).</summary>
[TestClass]
public sealed class IntegratorTablesTests
{
    private static readonly SkyLight Light = SkyLight.For(SunPlaceholder.For(TimeOfDay.GoldenHour));

    [TestMethod]
    public void Layout_IsTheEnginesHeroCycle()
    {
        IntegratorTables tables = IntegratorTables.Build(MaterialCatalog.M0, Light);
        SpectralResources engine = SpectralResourceBaker.Bake(IntegratorTables.Wavelengths, []);

        Assert.AreEqual(50, tables.Count);
        Assert.AreEqual(12, tables.CompanionStride, "the shader hard-codes COMPANION_STRIDE 12");
        CollectionAssert.AreEqual(engine.DeterXYZ, tables.Cie);
        Assert.AreEqual(engine.DeterministicCorrection, tables.DeterministicCorrection);
        Assert.AreEqual(tables.Count * 4, tables.Light.Length);
        Assert.AreEqual(MaterialCatalog.M0.Count * tables.Count, tables.Reflectance.Length);
        Assert.AreEqual(MaterialCatalog.M0.Count * 4, tables.Albedo.Length);
    }

    [TestMethod]
    public void LightTable_HoldsTheSkyLightTerms()
    {
        IntegratorTables tables = IntegratorTables.Build(MaterialCatalog.M0, Light);
        for (int i = 0; i < tables.Count; i++)
        {
            float nm = tables.WavelengthNm(i);
            float shape = Light.SolarShape(nm, IntegratorTables.Wavelengths);
            Assert.AreEqual(shape, tables.Light[i * 4], 1e-6f);
            Assert.AreEqual(SkyLight.RayleighRelative(nm), tables.Light[i * 4 + 1], 1e-6f);
            Assert.AreEqual(Light.SunSpectralIrradiance(shape), tables.Light[i * 4 + 2], 1e-5f);
            Assert.AreEqual(Light.SunDiskRadiance(shape), tables.Light[i * 4 + 3], tables.Light[i * 4 + 3] * 1e-6f);
        }
    }

    [TestMethod]
    public void FlatSpectrum_OverTheWholeCycle_IsLuminanceOne()
    {
        IntegratorTables tables = IntegratorTables.Build(MaterialCatalog.M0, Light);
        Vector3 sum = Vector3.Zero;
        for (int hero = 0; hero < tables.Count; hero++)
            sum += tables.SampleXyz(hero, [1f, 1f, 1f, 1f]);
        Vector3 mean = sum / tables.Count;
        Assert.AreEqual(1f, mean.Y, 1e-5f);
    }

    [TestMethod]
    public void TerrainReflectance_IsPhysicalAndRoundTripsToItsAlbedo()
    {
        IntegratorTables tables = IntegratorTables.Build(MaterialCatalog.M0, Light);
        VistaMaterial terrain = MaterialCatalog.Terrain;
        SpectralResources spectral = SpectralResourceBaker.Bake(IntegratorTables.Wavelengths, []);
        RgbToReflectanceBasis basis = RgbToReflectanceBasis.Build(spectral);
        (float r, float g, float b) = terrain.LinearRgbAlbedo;

        float[] unclamped = basis.Reflectance(new Vector3(r, g, b));
        float[] row = tables.Reflectance[(terrain.Id * tables.Count)..((terrain.Id + 1) * tables.Count)];
        CollectionAssert.AreEqual(unclamped, row, "the M0 albedo needs no clamping");
        Assert.IsTrue(row.All(v => v is >= 0f and <= 1f));

        Vector3 back = basis.ForwardLinearRgb(row);
        Assert.AreEqual(r, back.X, 1e-4f);
        Assert.AreEqual(g, back.Y, 1e-4f);
        Assert.AreEqual(b, back.Z, 1e-4f);
        CollectionAssert.AreEqual(new[] { r, g, b, 0f }, tables.Albedo[(terrain.Id * 4)..((terrain.Id + 1) * 4)]);
    }

    [TestMethod]
    public void SaturatedColour_IsClampedToPhysicalReflectance()
    {
        VistaMaterial[] materials = [new VistaMaterial(0, "saturated", DataTier.BuiltIn, (0.9f, 0.02f, 0.02f))];
        IntegratorTables tables = IntegratorTables.Build(materials, Light);
        Assert.IsTrue(tables.Reflectance.All(v => v is >= 0f and <= 1f));
    }

    [TestMethod]
    public void MaterialIds_MustEqualTheirIndex() =>
        Assert.ThrowsExactly<ArgumentException>(() =>
            IntegratorTables.Build([new VistaMaterial(3, "misplaced", DataTier.BuiltIn, (0.5f, 0.5f, 0.5f))], Light));
}
