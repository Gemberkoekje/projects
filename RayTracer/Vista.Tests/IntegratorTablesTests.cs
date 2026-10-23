using System.Numerics;
using RayTracer;
using Vista.Rendering;
using Vista.Scene;

namespace Vista.Tests;

/// <summary>The surface tables the integrator uploads (vista-plan §5.1, §6.4); the light's are <c>AtmosphereTables</c>.</summary>
[TestClass]
public sealed class IntegratorTablesTests
{
    [TestMethod]
    public void Layout_IsTheWavelengthTables()
    {
        foreach (WavelengthTable wavelengths in new[] { WavelengthTable.Bins, WavelengthTable.Engine })
        {
            IntegratorTables tables = IntegratorTables.Build(MaterialCatalog.M0, wavelengths);
            Assert.AreEqual(50, tables.Count);
            Assert.AreEqual(12, tables.CompanionStride, "the shader hard-codes COMPANION_STRIDE 12");
            Assert.AreSame(wavelengths, tables.Wavelengths);
            CollectionAssert.AreEqual(wavelengths.Cie, tables.Cie);
            Assert.AreEqual(wavelengths.DeterministicCorrection, tables.DeterministicCorrection);
            Assert.AreEqual(MaterialCatalog.M0.Count * tables.Count, tables.Reflectance.Length);
            Assert.AreEqual(MaterialCatalog.M0.Count * 4, tables.Albedo.Length);
        }
        SpectralResources engine = SpectralResourceBaker.Bake(new WavelengthLookup(), []);
        CollectionAssert.AreEqual(engine.DeterXYZ, IntegratorTables.Build(MaterialCatalog.M0, WavelengthTable.Engine).Cie, "the engine's, to the bit");
    }

    [TestMethod]
    public void LuminancePerY_IsTheCieYIntegralOfTheEnginesTable()
    {
        // Y is the ȳ-weighted mean spectral radiance; 683 lm/W times ∫ȳ dλ over the engine's own 1 nm CIE table
        // turns it into cd/m².
        using Stream stream = typeof(WavelengthLookup).Assembly.GetManifestResourceStream("RayTracer.Data.CIE_xyz_1931_2deg.csv")!;
        using var reader = new StreamReader(stream);
        double integral = 0.0;
        while (reader.ReadLine() is { } line)
        {
            string[] parts = line.Split(',');
            if (parts.Length == 4 && double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double y))
                integral += y;
        }
        Assert.AreEqual(IntegratorTables.LuminancePerY, 683.0 * integral, 683.0 * 1e-3);
    }

    [TestMethod]
    public void FlatSpectrum_OverTheWholeCycle_IsLuminanceOne()
    {
        foreach (WavelengthTable wavelengths in new[] { WavelengthTable.Bins, WavelengthTable.Engine })
        {
            IntegratorTables tables = IntegratorTables.Build(MaterialCatalog.M0, wavelengths);
            Vector3 sum = Vector3.Zero;
            for (int hero = 0; hero < tables.Count; hero++)
                sum += tables.SampleXyz(hero, [1f, 1f, 1f, 1f]);
            Vector3 mean = sum / tables.Count;
            Assert.AreEqual(1f, mean.Y, 1e-5f, wavelengths.Quadrature.ToString());
        }
    }

    [TestMethod]
    public void TerrainReflectance_IsPhysicalAndRoundTripsToItsAlbedo()
    {
        foreach (WavelengthTable wavelengths in new[] { WavelengthTable.Bins, WavelengthTable.Engine })
        {
            IntegratorTables tables = IntegratorTables.Build(MaterialCatalog.M0, wavelengths);
            VistaMaterial terrain = MaterialCatalog.Terrain;
            ReflectanceBasis basis = ReflectanceBasis.Build(wavelengths);
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
    }

    [TestMethod]
    public void SaturatedColour_IsClampedToPhysicalReflectance()
    {
        VistaMaterial[] materials = [new VistaMaterial(0, "saturated", DataTier.BuiltIn, (0.9f, 0.02f, 0.02f))];
        IntegratorTables tables = IntegratorTables.Build(materials, WavelengthTable.Bins);
        Assert.IsTrue(tables.Reflectance.All(v => v is >= 0f and <= 1f));
    }

    [TestMethod]
    public void MaterialIds_MustEqualTheirIndex() =>
        Assert.ThrowsExactly<ArgumentException>(() =>
            IntegratorTables.Build([new VistaMaterial(3, "misplaced", DataTier.BuiltIn, (0.5f, 0.5f, 0.5f))], WavelengthTable.Bins));
}
