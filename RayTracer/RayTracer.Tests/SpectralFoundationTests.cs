using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// Phase 0 of the spectral &amp; optical effects plan (spectral-effects-plan.md):
/// the shared foundation. These pin the physics of the new optics helpers
/// (Snell refraction, Fresnel, Cauchy dispersion), the optical channels on
/// <see cref="HitInfo"/> / <see cref="MaterialData"/>, and the wavelength-dependent
/// path classification that flagships rely on. A determinism guard confirms the
/// diffuse render path is unchanged and reproducible.
/// </summary>
[TestClass]
public sealed class SpectralFoundationTests
{
    // ── 0.2 Refraction (Snell's law) ──────────────────────────────────

    /// <summary>
    /// A ray refracting from air into glass must satisfy Snell's law
    /// n₁·sinθ₁ = n₂·sinθ₂ and bend toward the normal.
    /// </summary>
    [TestMethod]
    public void Refract_AirToGlass_ObeysSnellsLaw()
    {
        const float n1 = 1.0f;
        const float n2 = 1.5f;
        Vector3 normal = new(0f, 1f, 0f);
        // Incident ray descending at 45° toward the interface.
        Vector3 incident = Vector3.Normalize(new Vector3(1f, -1f, 0f));

        bool transmitted = Optics.Refract(incident, normal, n1, n2, out Vector3 refracted);

        Assert.IsTrue(transmitted, "45° into denser medium should transmit, not TIR.");

        float sinIn = SinAngleFromAxis(incident, new Vector3(0f, -1f, 0f));
        float sinOut = SinAngleFromAxis(refracted, new Vector3(0f, -1f, 0f));

        Assert.AreEqual(n1 * sinIn, n2 * sinOut, 1e-4f, "Snell's law must hold across the boundary.");
        Assert.IsTrue(sinOut < sinIn, "Ray entering a denser medium bends toward the normal.");
        Assert.AreEqual(1f, refracted.Length(), 1e-5f, "Refracted direction must be unit length.");
    }

    /// <summary>
    /// Past the critical angle a ray inside glass cannot escape into air:
    /// <see cref="Optics.Refract"/> reports total internal reflection.
    /// </summary>
    [TestMethod]
    public void Refract_GlassToAir_BeyondCriticalAngle_IsTotalInternalReflection()
    {
        const float n1 = 1.5f;
        const float n2 = 1.0f;
        // Critical angle for 1.5→1.0 is asin(1/1.5) ≈ 41.8°; use 60°.
        float theta = 60f * MathF.PI / 180f;
        Vector3 normal = new(0f, 1f, 0f);
        Vector3 incident = Vector3.Normalize(new Vector3(MathF.Sin(theta), -MathF.Cos(theta), 0f));

        bool transmitted = Optics.Refract(incident, normal, n1, n2, out Vector3 reflected);

        Assert.IsFalse(transmitted, "Beyond the critical angle there is no transmitted ray.");
        // On TIR the out-parameter carries the mirror reflection (y flips up).
        Assert.IsTrue(reflected.Y > 0f, "TIR should reflect the ray back into the medium.");
        Assert.AreEqual(1f, reflected.Length(), 1e-5f);
    }

    // ── 0.2 Fresnel reflectance ───────────────────────────────────────

    [TestMethod]
    public void FresnelDielectric_NormalIncidence_Matches_R0()
    {
        // R0 = ((n1-n2)/(n1+n2))² = (0.5/2.5)² = 0.04 for air→glass.
        float r = Optics.FresnelDielectric(1f, 1.0f, 1.5f);
        Assert.AreEqual(0.04f, r, 1e-4f);

        // Reflectance at normal incidence is symmetric in the two media.
        float rReversed = Optics.FresnelDielectric(1f, 1.5f, 1.0f);
        Assert.AreEqual(r, rReversed, 1e-4f);
    }

    [TestMethod]
    public void FresnelDielectric_GrazingIncidence_IsFullyReflective()
    {
        float r = Optics.FresnelDielectric(0f, 1.0f, 1.5f);
        Assert.AreEqual(1f, r, 1e-4f, "At grazing incidence a dielectric reflects everything.");
    }

    [TestMethod]
    public void FresnelSchlick_NormalIncidence_ApproximatesExact()
    {
        // Schlick agrees with the exact solution at normal incidence.
        Assert.AreEqual(0.04f, Optics.FresnelSchlick(1f, 1.0f, 1.5f), 1e-4f);
        Assert.AreEqual(1f, Optics.FresnelSchlick(0f, 1.0f, 1.5f), 1e-4f);
    }

    // ── 0.3 Wavelength-dependent path classification ──────────────────

    [TestMethod]
    public void IsWavelengthDependent_OnlyForPathAlteringSurfaces()
    {
        Assert.IsFalse(Optics.IsWavelengthDependent(SurfaceKind.Diffuse));
        Assert.IsFalse(Optics.IsWavelengthDependent(SurfaceKind.Mirror));
        Assert.IsFalse(Optics.IsWavelengthDependent(SurfaceKind.Emissive));
        // Thin-film modulates reflectance only; its reflection geometry is the same for
        // every wavelength, so companions validly share the path (§2.2) — not hero-only.
        Assert.IsFalse(Optics.IsWavelengthDependent(SurfaceKind.ThinFilm));

        Assert.IsTrue(Optics.IsWavelengthDependent(SurfaceKind.Dielectric));
        Assert.IsTrue(Optics.IsWavelengthDependent(SurfaceKind.Grating));
        Assert.IsTrue(Optics.IsWavelengthDependent(SurfaceKind.Fluorescent));
    }

    // ── 0.1 MaterialData optical channels (Cauchy dispersion) ─────────

    [TestMethod]
    public void IorAt_CauchyDispersion_BendsBlueMoreThanRed()
    {
        var glass = MakeGlass(cauchyA: 1.5f, cauchyB: 0.004f);

        float nBlue = glass.IorAt(400f);
        float nRed = glass.IorAt(700f);

        // n(λ) = A + B/λ²(µm): 1.5 + 0.004/0.16 = 1.525 at 400nm.
        Assert.AreEqual(1.525f, nBlue, 1e-3f);
        Assert.AreEqual(1.5f + 0.004f / (0.7f * 0.7f), nRed, 1e-4f);
        Assert.IsTrue(nBlue > nRed, "Shorter wavelengths must have a higher index (normal dispersion).");
    }

    [TestMethod]
    public void IorAt_NonDispersive_ReturnsBaseIndexExactly()
    {
        var water = MakeGlass(cauchyA: 1.33f, cauchyB: 0f);
        Assert.AreEqual(1.33f, water.IorAt(400f));
        Assert.AreEqual(1.33f, water.IorAt(700f));
    }

    // ── 0.1 HitInfo optical channels ──────────────────────────────────

    [TestMethod]
    public void HitInfo_DefaultsToOpaqueDiffuse()
    {
        var hit = new HitInfo(1f, Vector3.Zero, Vector3.UnitY, null, 0.5f, 0.1f);

        Assert.AreEqual(SurfaceKind.Diffuse, hit.Surface);
        Assert.AreEqual(0f, hit.Transmission);
        Assert.AreEqual(1f, hit.Ior);
        Assert.AreEqual(0f, hit.Extinction);
    }

    [TestMethod]
    public void FromMaterial_DiffuseMaterial_LeavesOpticalDefaults()
    {
        var diffuse = MakeDiffuse(roughness: 0.3f);
        var hit = HitInfo.FromMaterial(2f, Vector3.Zero, Vector3.UnitY, null, 0.42f, diffuse, 550, -Vector3.UnitY);

        Assert.AreEqual(0.42f, hit.Reflectance);
        Assert.AreEqual(0.3f, hit.Roughness);
        Assert.AreEqual(SurfaceKind.Diffuse, hit.Surface);
        Assert.AreEqual(1f, hit.Ior);
        Assert.AreEqual(0f, hit.Transmission);
    }

    [TestMethod]
    public void FromMaterial_DielectricMaterial_ResolvesOpticalChannels()
    {
        var glass = MakeGlass(cauchyA: 1.5f, cauchyB: 0.004f, transmission: 0.95f);
        var hit = HitInfo.FromMaterial(2f, Vector3.Zero, Vector3.UnitY, null, 0.05f, glass, 400, -Vector3.UnitY);

        Assert.AreEqual(SurfaceKind.Dielectric, hit.Surface);
        Assert.AreEqual(0.95f, hit.Transmission, 1e-6f);
        Assert.AreEqual(glass.IorAt(400), hit.Ior, 1e-6f);
    }

    // ── Phase 0 verify: diffuse render is unchanged & deterministic ────

    /// <summary>
    /// A plain diffuse maze must render identically on repeated runs (the
    /// determinism the accumulation buffer relies on to converge to the correct
    /// spectrum) and produce non-trivial illuminated output. Two independent
    /// job systems fed identical inputs must accumulate bit-for-bit equal
    /// buffers.
    /// </summary>
    [TestMethod]
    public void DiffuseScene_RendersDeterministically_AndNonTrivially()
    {
        const int width = 48;
        const int height = 36;
        const int frames = 3;

        Vector3[] first = RenderDiffuseMaze(width, height, frames);
        Vector3[] second = RenderDiffuseMaze(width, height, frames);

        Assert.AreEqual(first.Length, second.Length);

        bool anyLit = false;
        for (int i = 0; i < first.Length; i++)
        {
            Assert.AreEqual(first[i].X, second[i].X, $"X mismatch at {i} — render is non-deterministic.");
            Assert.AreEqual(first[i].Y, second[i].Y, $"Y mismatch at {i} — render is non-deterministic.");
            Assert.AreEqual(first[i].Z, second[i].Z, $"Z mismatch at {i} — render is non-deterministic.");
            if (first[i].Y > 1e-4f)
                anyLit = true;
        }

        Assert.IsTrue(anyLit, "Diffuse maze render produced no illuminated pixels.");
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static float SinAngleFromAxis(Vector3 dir, Vector3 axis)
    {
        float cos = Math.Clamp(Vector3.Dot(Vector3.Normalize(dir), Vector3.Normalize(axis)), -1f, 1f);
        return MathF.Sqrt(MathF.Max(0f, 1f - cos * cos));
    }

    private static MaterialData MakeDiffuse(float roughness)
        => new("t", "Test", null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, roughness);

    private static MaterialData MakeGlass(float cauchyA, float cauchyB, float transmission = 0f)
        => new("g", "Glass", null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0.0f,
            spectralData: null, surface: SurfaceKind.Dielectric,
            transmission: transmission, cauchyA: cauchyA, cauchyB: cauchyB);

    private static Vector3[] RenderDiffuseMaze(int width, int height, int frames)
    {
        var materials = new MaterialsLookup();
        var maze = new Maze(6, 6, seed: 777);

        materials.TryGetMaterial("01085", out var mortar);
        materials.TryGetMaterial("01138", out var grid);

        var scene = MazeGeometryBuilder.Build(
            maze,
            wallMaterial: materials["00115"],
            floorMaterial: materials["01138"],
            ceilingMaterial: materials["01085"],
            mortarMaterial: mortar,
            ceilingGridMaterial: grid,
            goalMaterial: null);

        var lights = MazeGeometryBuilder.BuildLights(maze);

        float cs = MazeGeometryBuilder.CellSize;
        float wh = MazeGeometryBuilder.WallHeight;
        var camera = new Camera
        {
            Position = new Vector3(cs * 0.5f, wh * 0.5f, cs * 0.5f),
            Rotation = CameraController.HeadingToQuaternion(Direction.South),
            Fov = MathF.PI / 3f,
            Aspect = (float)width / height,
            ImgPlaneZ = 1f,
        };

        var js = new JobSystem(
            width, height, scene, camera, stride: width * 4, lights: lights,
            renderOptions: new RenderOptions(Lighting: LightingMode.NEE, MaxSampleCount: 500),
            samplingOptions: new SamplingOptions(SubPixelJitter: false),
            denoiseOptions: new DenoiseOptions(
                EnableTaa: false, EnableDiffuseCache: false, SmokeMode: SmokeMode.None),
            debugOptions: new DebugOptions());

        for (int f = 0; f < frames; f++)
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    js.TraceCore(camera, y, x);

        return (Vector3[])js.AccumXYZ.Clone();
    }
}
