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
    public void PhysicalEnergy_RespondsToLightDistanceAndIntensity()
    {
        // §8: with PhysicalEnergy on, a photon's power scales with the light's intensity and the solid
        // angle the caster subtends, so a closer or brighter light deposits more caustic energy — the
        // physical response the historical flat 1/N power (paid over only by Strength) never had.
        var sphere = new Sphere(new Vector3(0f, 2f, 0f), 1f, GlassMat(cauchyB: 0f));
        var bvh = new BVH([Floor(), sphere]);
        Light[] near = [new Light { Position = new Vector3(0f, 4f, 0f), Color = Vector3.One }];
        Light[] far = [new Light { Position = new Vector3(0f, 8f, 0f), Color = Vector3.One }];

        PhotonMap flat = PhotonTracer.Emit(near, bvh, sphere.Bounds, Wl, 4000, 0.25f); // physicalEnergy off
        PhotonMap physNear = PhotonTracer.Emit(near, bvh, sphere.Bounds, Wl, 4000, 0.25f, physicalEnergy: true, lightIntensity: 1.5f);
        PhotonMap physFar = PhotonTracer.Emit(far, bvh, sphere.Bounds, Wl, 4000, 0.25f, physicalEnergy: true, lightIntensity: 1.5f);
        PhotonMap physBright = PhotonTracer.Emit(near, bvh, sphere.Bounds, Wl, 4000, 0.25f, physicalEnergy: true, lightIntensity: 3.0f);

        Assert.IsTrue(flat.TotalPower > 0f && physNear.TotalPower > 0f, "both should deposit energy");
        Assert.IsTrue(physNear.TotalPower > physFar.TotalPower,
            $"a closer light subtends a larger solid angle → more energy (near={physNear.TotalPower}, far={physFar.TotalPower})");
        // Same position + seed ⇒ same photon paths, so doubling the intensity doubles the deposited power.
        Assert.AreEqual(2f, physBright.TotalPower / physNear.TotalPower, 1e-3f,
            "doubling the light intensity doubles the deposited caustic power");
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

    [TestMethod]
    public void EmitInto_AccumulatesMultipleCastersIntoOneMap()
    {
        // Two glass spheres, one map: EmitInto lets a scene with several casters share a single caustic
        // map (what JobSystem does across all its casters) rather than one map per caster.
        var left = new Sphere(new Vector3(-3f, 2f, 0f), 1f, GlassMat(cauchyB: 0f));
        var right = new Sphere(new Vector3(3f, 2f, 0f), 1f, GlassMat(cauchyB: 0f));
        var bvh = new BVH([Floor(), left, right]);
        Light[] lights = [new Light { Position = new Vector3(0f, 6f, 0f), Color = Vector3.One }];

        var map = new PhotonMap(0.25f);
        PhotonTracer.EmitInto(map, lights, bvh, left.Bounds, Wl, photonsPerLight: 3000, seed: 1u);
        int afterFirst = map.Count;
        PhotonTracer.EmitInto(map, lights, bvh, right.Bounds, Wl, photonsPerLight: 3000, seed: 2u);

        Assert.IsTrue(afterFirst > 0, "the first caster deposits caustics into the map");
        Assert.IsTrue(map.Count > afterFirst, "the second caster adds more photons into the same map");
        Assert.IsTrue(map.CountNear(new Vector3(-3f, 0f, 0f), 3f) > 0, "left sphere's caustic lands under it");
        Assert.IsTrue(map.CountNear(new Vector3(3f, 0f, 0f), 3f) > 0, "right sphere's caustic lands under it");
    }

    // ── CausticOptions: quality mapping ────────────────────────────────────

    [TestMethod]
    public void FromQuality_OffOnCheaperTiers_OnForHighUltra()
    {
        Assert.IsFalse(CausticOptions.FromQuality(CausticQuality.Off).Enable);
        Assert.IsFalse(CausticOptions.FromQuality(CausticQuality.Low).Enable);
        Assert.IsFalse(CausticOptions.FromQuality(CausticQuality.Medium).Enable);
        Assert.IsTrue(CausticOptions.FromQuality(CausticQuality.High).Enable, "High enables modest caustics");
        Assert.IsTrue(CausticOptions.FromQuality(CausticQuality.Ultra).Enable, "Ultra enables full caustics");
        Assert.IsTrue(
            CausticOptions.FromQuality(CausticQuality.Ultra).PhotonsPerFrame >
            CausticOptions.FromQuality(CausticQuality.High).PhotonsPerFrame,
            "Ultra traces more photons than High");
    }

    [TestMethod]
    public void DefaultCausticOptions_AreDisabled()
    {
        // The record default must be off so ordinary presets/renders stay byte-identical.
        Assert.IsFalse(default(CausticOptions).Enable);
    }

    // ── Tracable.Surface: caster enumeration ───────────────────────────────

    [TestMethod]
    public void Surface_ExposesMaterialKindForEnumeration()
    {
        Tracable floor = Floor();
        Tracable glass = new Sphere(Vector3.Zero, 1f, GlassMat(cauchyB: 0f));
        Assert.AreEqual(SurfaceKind.Diffuse, floor.Surface, "a diffuse rectangle is not a caster");
        Assert.AreEqual(SurfaceKind.Dielectric, glass.Surface, "a glass sphere is a caster");
    }

    // ── Compositing the map into the render (PathTracer.TraceCore) ──────────

    private static Light[] OverheadLight()
        => [new Light { Position = new Vector3(0f, 6f, 0f), Color = Vector3.One }];

    // A camera above and behind the origin, angled down at the floor spot under the sphere, so the
    // sphere (up at y = 2) does not occlude the caustic that focuses near the origin.
    private static Camera CausticCamera() => new()
    {
        Position = new Vector3(0f, 4f, -7f),
        Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.Atan2(4f, 7f)),
        Fov = MathF.PI / 3f,
        Aspect = 1f,
        ImgPlaneZ = 1f,
    };

    private static Vector3[] RenderFloor(Tracable[] scene, Light[] lights, CausticOptions caustics, int frames = 64, int size = 24)
    {
        Camera camera = CausticCamera();
        var js = new JobSystem(
            size, size, scene, camera, stride: size * 4, lights: lights,
            renderOptions: new RenderOptions(Lighting: LightingMode.NEE, MaxSampleCount: 4000),
            samplingOptions: new SamplingOptions(SubPixelJitter: false),
            denoiseOptions: new DenoiseOptions(
                EnableTaa: false, EnableDiffuseCache: false, SmokeMode: SmokeMode.None, Caustics: caustics),
            debugOptions: new DebugOptions());

        for (int f = 0; f < frames; f++)
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    js.TraceCore(camera, y, x);

        return (Vector3[])js.AccumXYZ.Clone();
    }

    private static float SumY(Vector3[] buffer)
    {
        float sum = 0f;
        for (int i = 0; i < buffer.Length; i++)
            sum += buffer[i].Y;
        return sum;
    }

    [TestMethod]
    public void Composite_GlassSphereOverFloor_BrightensTheCaustic()
    {
        var sphere = new Sphere(new Vector3(0f, 2f, 0f), 1f, GlassMat(cauchyB: 0f));
        Tracable[] scene = [Floor(), sphere];
        Light[] lights = OverheadLight();

        // Dense gather so the focused caustic reads clearly against the lit floor.
        CausticOptions on = CausticOptions.FromQuality(CausticQuality.High) with { GatherRadius = 0.3f };

        Vector3[] off = RenderFloor(scene, lights, default);
        Vector3[] lit = RenderFloor(scene, lights, on);

        float sumOff = SumY(off);
        float sumOn = SumY(lit);
        Assert.IsTrue(sumOn > sumOff * 1.001f,
            $"the glass sphere's caustic must add light to the floor (off={sumOff:F4}, on={sumOn:F4})");

        // Caustics only add energy, never remove it: on ≥ off at every pixel.
        for (int i = 0; i < off.Length; i++)
            Assert.IsTrue(lit[i].Y >= off[i].Y - 1e-4f,
                $"caustics must not darken any pixel (i={i}, off={off[i].Y}, on={lit[i].Y})");
    }

    // ── GPU port foundation (B4): flattened grid + reference gather parity ──

    [TestMethod]
    public void BuildGrid_Empty_GathersZero()
    {
        CausticGrid grid = new PhotonMap(0.5f).BuildGrid();
        Assert.IsTrue(grid.IsEmpty);
        Assert.AreEqual(0f, CausticReference.EstimateXyz(grid, Vector3.Zero, Vector3.UnitY, 0.5f, Wl).Length(), 1e-9f);
    }

    [TestMethod]
    public void GridGather_MatchesPhotonMapEstimate_ForAGlassSphereCaustic()
    {
        // Build a real caustic, flatten it, and assert the GPU-shaped grid gather reproduces the map's
        // own density estimate bit-for-bit at many probe points — the CPU/GPU parity the HLSL relies on.
        var sphere = new Sphere(new Vector3(0f, 2f, 0f), 1f, GlassMat(cauchyB: 0.05f));
        var bvh = new BVH([Floor(), sphere]);
        Light[] lights = [new Light { Position = new Vector3(1f, 6f, 0f), Color = Vector3.One }];

        const float radius = 0.25f;
        PhotonMap map = PhotonTracer.Emit(lights, bvh, sphere.Bounds, Wl, photonsPerLight: 6000, gatherRadius: radius);
        Assert.IsTrue(map.Count > 100, $"need a populated caustic to compare ({map.Count} photons)");

        CausticGrid grid = map.BuildGrid();
        Assert.AreEqual(map.Count, grid.Photons.Length, "every photon survives the flatten");

        int probed = 0, matched = 0;
        for (float x = -4f; x <= 4f; x += 0.5f)
        {
            for (float z = -4f; z <= 4f; z += 0.5f)
            {
                var p = new Vector3(x, 0f, z);
                Vector3 mapXyz = map.EstimateXyz(p, Vector3.UnitY, radius, Wl);
                Vector3 gridXyz = CausticReference.EstimateXyz(grid, p, Vector3.UnitY, radius, Wl);
                probed++;
                Assert.AreEqual(mapXyz.X, gridXyz.X, 1e-4f, $"X mismatch at {p}");
                Assert.AreEqual(mapXyz.Y, gridXyz.Y, 1e-4f, $"Y mismatch at {p}");
                Assert.AreEqual(mapXyz.Z, gridXyz.Z, 1e-4f, $"Z mismatch at {p}");
                if (mapXyz.Length() > 0f) matched++;
            }
        }

        Assert.IsTrue(matched > 0, "at least some probes should land on the caustic (non-zero estimate)");
        Assert.IsTrue(probed > 100, "swept a dense grid of probe points");
    }

    [TestMethod]
    public void SpectralAlbedo_TintsPerWavelength_AndKeepsMapGridParity()
    {
        // §8: the spectral-albedo weight must be applied identically by the map's own gather and the
        // GPU-shaped grid gather (the CPU/GPU contract), and a wavelength-selective receiver albedo must
        // actually change the gathered colour versus the achromatic (null) gather.
        var sphere = new Sphere(new Vector3(0f, 2f, 0f), 1f, GlassMat(cauchyB: 0.05f));
        var bvh = new BVH([Floor(), sphere]);
        Light[] lights = [new Light { Position = new Vector3(1f, 6f, 0f), Color = Vector3.One }];

        const float radius = 0.25f;
        PhotonMap map = PhotonTracer.Emit(lights, bvh, sphere.Bounds, Wl, photonsPerLight: 6000, gatherRadius: radius);
        Assert.IsTrue(map.Count > 100, $"need a populated caustic ({map.Count} photons)");
        CausticGrid grid = map.BuildGrid();

        // A red receiver: reflects long wavelengths, absorbs short ones.
        System.Func<int, float> redAlbedo = wl => wl >= 560 ? 0.9f : 0.1f;

        bool sawTint = false;
        for (float x = -4f; x <= 4f; x += 0.5f)
        {
            for (float z = -4f; z <= 4f; z += 0.5f)
            {
                var p = new Vector3(x, 0f, z);
                Vector3 mapTint = map.EstimateXyz(p, Vector3.UnitY, radius, Wl, redAlbedo);
                Vector3 gridTint = CausticReference.EstimateXyz(grid, p, Vector3.UnitY, radius, Wl, redAlbedo);
                Assert.AreEqual(mapTint.X, gridTint.X, 1e-4f, $"X parity at {p}");
                Assert.AreEqual(mapTint.Y, gridTint.Y, 1e-4f, $"Y parity at {p}");
                Assert.AreEqual(mapTint.Z, gridTint.Z, 1e-4f, $"Z parity at {p}");

                Vector3 plain = map.EstimateXyz(p, Vector3.UnitY, radius, Wl);
                if (plain.Length() > 1e-4f)
                {
                    // The red albedo suppresses more than it keeps, so the tinted estimate is dimmer,
                    // and the surviving energy skews toward X (red) relative to Z (blue).
                    Assert.IsTrue(mapTint.Length() < plain.Length() + 1e-6f, $"tint cannot brighten at {p}");
                    if (mapTint.Length() > 1e-4f && plain.Z > 1e-5f)
                    {
                        float plainXZ = plain.X / plain.Z;
                        float tintXZ = mapTint.X / MathF.Max(mapTint.Z, 1e-6f);
                        if (tintXZ > plainXZ + 1e-3f) sawTint = true;
                    }
                }
            }
        }

        Assert.IsTrue(sawTint, "a wavelength-selective albedo must shift the caustic's colour balance");
    }

    [TestMethod]
    public void GridGather_RespectsNormalRejection_LikeTheMap()
    {
        int lambda = Wl.GetDeterministicWavelength(5);
        var map = new PhotonMap(1f);
        map.Store(new Photon(Vector3.Zero, Vector3.UnitY, 1f, lambda));
        CausticGrid grid = map.BuildGrid();

        // Aligned receiver gathers it; opposite-facing receiver rejects it — matching the map.
        Assert.AreEqual(
            map.EstimateXyz(Vector3.Zero, Vector3.UnitY, 1f, Wl).Length(),
            CausticReference.EstimateXyz(grid, Vector3.Zero, Vector3.UnitY, 1f, Wl).Length(), 1e-6f);
        Assert.AreEqual(0f, CausticReference.EstimateXyz(grid, Vector3.Zero, -Vector3.UnitY, 1f, Wl).Length(), 1e-9f);
    }

    [TestMethod]
    public void Composite_NoCasters_IsByteIdentical()
    {
        // A caster-free scene builds no photon map, so enabling caustics must leave the render exactly
        // as it was — the plan's "no-effect path stays byte-identical" guarantee.
        Tracable[] scene = [Floor()];
        Light[] lights = OverheadLight();

        Vector3[] off = RenderFloor(scene, lights, default);
        Vector3[] on = RenderFloor(scene, lights, CausticOptions.FromQuality(CausticQuality.High));

        for (int i = 0; i < off.Length; i++)
            Assert.AreEqual(off[i], on[i], $"no caster → byte-identical (pixel {i})");
    }
}
