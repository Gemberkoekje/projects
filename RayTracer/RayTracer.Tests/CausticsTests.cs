using System.Collections.Generic;
using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// Phase B of the shadows-and-caustics plan: forward light transport for caustics. Pins the caustic
/// photon map (storage + spectral density estimate) and the forward <see cref="PhotonTracer"/> that
/// bends photons through glass/prisms onto diffuse receivers — the transport a backward path tracer
/// with point-light NEE cannot do. These validate the CPU foundation; compositing the map into the
/// render and the GPU port follow.
/// </summary>
[TestClass]
public sealed class CausticsTests
{
    private static readonly WavelengthLookup Wl = new();

    private static SpectralData Flat(float value)
    {
        var wavelengths = new List<int>();
        var values = new List<float>();
        for (int w = 360; w <= 830; w += 5) { wavelengths.Add(w); values.Add(value); }
        float[] a = values.ToArray();
        return new SpectralData(wavelengths.ToArray(), a, (float[])a.Clone());
    }

    private static MaterialData DiffuseMat()
        => new("diffuse", "Diffuse", null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, Flat(0.7f), SurfaceKind.Diffuse);

    private static MaterialData GlassMat(float cauchyB)
        => new("glass", "Glass", null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
            spectralData: null, surface: SurfaceKind.Dielectric, transmission: 0.95f, cauchyA: 1.5f, cauchyB: cauchyB);

    // A large diffuse floor in the XZ plane at y = 0, facing up.
    private static TracableRectangle Floor()
        => new((new Vector3(-20f, 0f, -20f), new Vector3(-20f, 0f, 20f), new Vector3(20f, 0f, -20f)), DiffuseMat());

    private static float MeanX(IReadOnlyList<Photon> photons)
    {
        if (photons.Count == 0)
            return 0f;
        float sum = 0f;
        for (int i = 0; i < photons.Count; i++)
            sum += photons[i].Position.X;
        return sum / photons.Count;
    }

    // ── PhotonMap: storage + density estimate ──────────────────────────────

    [TestMethod]
    public void EstimateXyz_Empty_IsZero()
    {
        var map = new PhotonMap(1f);
        Assert.AreEqual(0f, map.EstimateXyz(Vector3.Zero, Vector3.UnitY, 1f, Wl).Length(), 1e-9f);
    }

    [TestMethod]
    public void EstimateXyz_GathersWithinRadiusOnly()
    {
        int lambda = Wl.GetDeterministicWavelength(5);
        var map = new PhotonMap(1f);
        map.Store(new Photon(Vector3.Zero, Vector3.UnitY, 1f, lambda));

        Assert.IsTrue(map.EstimateXyz(Vector3.Zero, Vector3.UnitY, 1f, Wl).Length() > 0f, "photon within radius contributes");
        Assert.AreEqual(0f, map.EstimateXyz(new Vector3(10f, 0f, 0f), Vector3.UnitY, 1f, Wl).Length(), 1e-9f,
            "a photon well outside the radius contributes nothing");
    }

    [TestMethod]
    public void EstimateXyz_ScalesLinearlyWithPhotonCount()
    {
        int lambda = Wl.GetDeterministicWavelength(5);
        var one = new PhotonMap(1f);
        one.Store(new Photon(Vector3.Zero, Vector3.UnitY, 1f, lambda));
        var two = new PhotonMap(1f);
        two.Store(new Photon(Vector3.Zero, Vector3.UnitY, 1f, lambda));
        two.Store(new Photon(Vector3.Zero, Vector3.UnitY, 1f, lambda));

        float e1 = one.EstimateXyz(Vector3.Zero, Vector3.UnitY, 1f, Wl).Length();
        float e2 = two.EstimateXyz(Vector3.Zero, Vector3.UnitY, 1f, Wl).Length();
        Assert.AreEqual(2f * e1, e2, 1e-4f * e2 + 1e-6f, "twice the photons, twice the estimated irradiance");
    }

    [TestMethod]
    public void EstimateXyz_RejectsMisalignedNormal()
    {
        int lambda = Wl.GetDeterministicWavelength(5);
        var map = new PhotonMap(1f);
        map.Store(new Photon(Vector3.Zero, Vector3.UnitY, 1f, lambda));
        // Query a receiver facing the opposite way — the photon belongs to another surface.
        Assert.AreEqual(0f, map.EstimateXyz(Vector3.Zero, -Vector3.UnitY, 1f, Wl).Length(), 1e-9f);
    }

    [TestMethod]
    public void TotalPower_And_CountNear()
    {
        int lambda = Wl.GetDeterministicWavelength(5);
        var map = new PhotonMap(0.5f);
        map.Store(new Photon(Vector3.Zero, Vector3.UnitY, 0.3f, lambda));
        map.Store(new Photon(new Vector3(0.2f, 0f, 0f), Vector3.UnitY, 0.7f, lambda));
        map.Store(new Photon(new Vector3(5f, 0f, 0f), Vector3.UnitY, 1f, lambda));

        Assert.AreEqual(2.0f, map.TotalPower, 1e-6f);
        Assert.AreEqual(2, map.CountNear(Vector3.Zero, 1f), "two photons within a unit of the origin");
        Assert.AreEqual(1, map.CountNear(new Vector3(5f, 0f, 0f), 1f), "one photon near (5,0,0)");
    }

    // ── PhotonTracer: forward transport ────────────────────────────────────

    [TestMethod]
    public void Emit_DiffuseOnlyScene_StoresNoCaustics()
    {
        var scene = new Tracable[] { Floor() };
        var bvh = new BVH(scene);
        Light[] lights = [new Light { Position = new Vector3(0f, 6f, 0f), Color = Vector3.One }];

        PhotonMap map = PhotonTracer.Emit(lights, bvh, Floor().Bounds, Wl, photonsPerLight: 500, gatherRadius: 0.5f);
        Assert.AreEqual(0, map.Count, "photons that strike only diffuse geometry are direct light, not caustics");
    }

    [TestMethod]
    public void Emit_GlassSphereOverFloor_DepositsAndFocusesCaustic()
    {
        var sphere = new Sphere(new Vector3(0f, 2f, 0f), 1f, GlassMat(cauchyB: 0f));
        var scene = new Tracable[] { Floor(), sphere };
        var bvh = new BVH(scene);
        Light[] lights = [new Light { Position = new Vector3(0f, 6f, 0f), Color = Vector3.One }];

        PhotonMap map = PhotonTracer.Emit(lights, bvh, sphere.Bounds, Wl, photonsPerLight: 4000, gatherRadius: 0.25f);

        Assert.IsTrue(map.Count > 0, "a glass sphere over a floor focuses caustic photons onto it");
        Assert.IsTrue(map.CountNear(Vector3.Zero, 3f) > 0, "the caustic lands under the sphere");
        Assert.AreEqual(0, map.CountNear(new Vector3(15f, 0f, 15f), 2f), "no caustic far from the sphere");
    }

    [TestMethod]
    public void Emit_EnergyIsBounded()
    {
        var sphere = new Sphere(new Vector3(0f, 2f, 0f), 1f, GlassMat(cauchyB: 0f));
        var scene = new Tracable[] { Floor(), sphere };
        var bvh = new BVH(scene);
        Light[] lights = [new Light { Position = new Vector3(0f, 6f, 0f), Color = Vector3.One }];

        PhotonMap map = PhotonTracer.Emit(lights, bvh, sphere.Bounds, Wl, photonsPerLight: 2000, gatherRadius: 0.25f);

        // Each photon carries 1/photonsPerLight, so one light emits ~1 toward the caster; deposits
        // can only lose energy (Fresnel reflection, escapes, absorption), never gain it.
        Assert.IsTrue(map.TotalPower > 0f, "some caustic energy should be deposited");
        Assert.IsTrue(map.TotalPower <= 1.0001f, $"deposited power {map.TotalPower} must not exceed the emitted 1.0");
    }

    [TestMethod]
    public void Emit_DispersiveGlass_ShiftsCausticByWavelength()
    {
        // An off-axis light through a strongly dispersive sphere: blue (higher index) bends more than
        // red, so the two wavelengths' caustics land at systematically different positions — the
        // physical basis of the prism rainbow, and something an RGB renderer cannot separate.
        var sphere = new Sphere(new Vector3(0f, 2f, 0f), 1f, GlassMat(cauchyB: 0.05f));
        var scene = new Tracable[] { Floor(), sphere };
        var bvh = new BVH(scene);
        Light[] lights = [new Light { Position = new Vector3(2f, 6f, 0f), Color = Vector3.One }];

        PhotonMap blue = PhotonTracer.Emit(lights, bvh, sphere.Bounds, Wl, 5000, 0.25f, wavelengthOverride: 450);
        PhotonMap red = PhotonTracer.Emit(lights, bvh, sphere.Bounds, Wl, 5000, 0.25f, wavelengthOverride: 650);

        Assert.IsTrue(blue.Count > 50 && red.Count > 50, $"enough caustic photons for both (blue={blue.Count}, red={red.Count})");
        float shift = MathF.Abs(MeanX(blue.Photons) - MeanX(red.Photons));
        Assert.IsTrue(shift > 0.01f, $"dispersion must shift the caustic by wavelength (|Δmean X|={shift})");
    }
}
