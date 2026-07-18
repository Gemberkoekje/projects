using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// Pins the physical-camera core and the three lens characters (plan §4).
///
/// <para>The load-bearing test here is <see cref="Pinhole_RenderIsBitIdenticalToNoLens"/>: working convention 2
/// says a feature that is off must leave the goldens unmoved, and the lens is the riskiest such feature yet
/// because it sits on the primary ray — every pixel of every scene goes through it. The guard is that the
/// pinhole path draws <b>no random numbers</b>, so it cannot shift the wavelength/RNG stream downstream.</para>
/// </summary>
[TestClass]
public sealed class LensTests
{
    private const float ImgPlaneZ = 1f;
    private const float Fov = MathF.PI / 3f;   // the engine's standard 60° vertical FOV

    // ── §4.1 the pinhole must stay exactly historical ─────────────────────────

    [TestMethod]
    public void Pinhole_ReproducesHistoricalRayExactly()
    {
        var lens = LensOptions.Pinhole;
        uint rng = 12345u;
        const float px = 0.31f, py = -0.22f;

        LensSampler.GenerateLocalRay(
            px, py, ImgPlaneZ, lens, apertureRadius: 0f, ref rng,
            out Vector3 origin, out Vector3 dir);

        // Bit-exact, not approximate: this is the expression TraceCore used before the lens existed.
        Assert.AreEqual(Vector3.Zero, origin, "a pinhole ray must start exactly at the camera position");
        Assert.AreEqual(new Vector3(px, py, ImgPlaneZ), dir, "a pinhole ray direction must be the raw image-plane offset");
    }

    [TestMethod]
    public void Pinhole_DrawsNoRandomNumbers()
    {
        var lens = LensOptions.Pinhole;
        uint rng = 0xDEADBEEF;
        uint before = rng;

        LensSampler.GenerateLocalRay(0.4f, 0.1f, ImgPlaneZ, lens, apertureRadius: 0f, ref rng, out _, out _);

        // If the pinhole consumed randoms, every downstream stream (wavelength, specular, shadow) would
        // shift and every golden image would move. This is the whole no-effect-path guarantee.
        Assert.AreEqual(before, rng, "the pinhole path must not touch the RNG stream");
    }

    [TestMethod]
    public void UnsetLensOptions_FallsBackToPinhole()
    {
        // The record-struct zero-init trap: `default` bypasses the primary constructor's defaults, so
        // SensorHeightMm/ApertureScale would be 0. JobSystem must substitute the real pinhole.
        var js = BuildJobSystem(new SamplingOptions(SubPixelJitter: false));
        Assert.AreEqual(LensModel.Pinhole, js.Lens.Model);
        Assert.AreEqual(24f, js.Lens.SensorHeightMm, "an unset lens must not keep the zero-init sensor size");
        Assert.AreEqual(1f, js.Lens.ApertureScale, "an unset lens must not keep the zero-init aperture scale");
    }

    [TestMethod]
    public void Pinhole_RenderIsBitIdenticalToNoLens()
    {
        // Two renders of a real scene: one with no lens specified at all (the historical path), one with an
        // explicit pinhole. Every accumulated sample must match bit-for-bit — the golden-image guarantee.
        var noLens = RenderScene(new SamplingOptions(SubPixelJitter: true));
        var pinhole = RenderScene(new SamplingOptions(SubPixelJitter: true, Lens: LensOptions.Pinhole));

        for (int i = 0; i < noLens.Length; i++)
        {
            Assert.AreEqual(noLens[i].X, pinhole[i].X, $"pixel {i} X moved");
            Assert.AreEqual(noLens[i].Y, pinhole[i].Y, $"pixel {i} Y moved");
            Assert.AreEqual(noLens[i].Z, pinhole[i].Z, $"pixel {i} Z moved");
        }
    }

    // ── §4.1 thin-lens focus ──────────────────────────────────────────────────

    [TestMethod]
    public void EveryApertureSample_ConvergesOnTheFocusPoint()
    {
        // The defining property of a thin lens: whatever point of the aperture a ray leaves from, it passes
        // through the same point on the focus plane. That is *why* the focus plane is sharp and everything
        // else is not — so if this holds, the depth of field is correct by construction.
        var lens = LensOptions.FromModel(LensModel.Cine) with { FocusDistance = 3f };
        float radius = lens.ApertureRadiusWorld(Fov);
        Assert.IsTrue(radius > 0f, "the cine lens must have a real aperture to test");

        const float px = 0.2f, py = 0.15f;
        Vector3 expected = new Vector3(px, py, ImgPlaneZ) * (lens.FocusDistance / ImgPlaneZ);

        uint rng = 7u;
        for (int i = 0; i < 200; i++)
        {
            LensSampler.GenerateLocalRay(px, py, ImgPlaneZ, lens, radius, ref rng,
                out Vector3 origin, out Vector3 dir);

            // March the ray to the focus plane (z = FocusDistance) and check where it lands.
            float t = (lens.FocusDistance - origin.Z) / dir.Z;
            Vector3 landed = origin + dir * t;
            Assert.AreEqual(expected.X, landed.X, 1e-4f, $"sample {i} missed the focus point in X");
            Assert.AreEqual(expected.Y, landed.Y, 1e-4f, $"sample {i} missed the focus point in Y");
        }
    }

    [TestMethod]
    public void ApertureSamples_StayWithinTheApertureAndSpread()
    {
        var lens = LensOptions.FromModel(LensModel.Cine);
        float radius = lens.ApertureRadiusWorld(Fov);

        uint rng = 99u;
        float maxSeen = 0f;
        for (int i = 0; i < 500; i++)
        {
            LensSampler.GenerateLocalRay(0f, 0f, ImgPlaneZ, lens, radius, ref rng, out Vector3 o, out _);
            float r = MathF.Sqrt(o.X * o.X + o.Y * o.Y);
            Assert.IsTrue(r <= radius + 1e-6f, $"sample {i} fell outside the aperture (r={r}, R={radius})");
            Assert.AreEqual(0f, o.Z, "the aperture is a flat disc at the camera plane");
            maxSeen = MathF.Max(maxSeen, r);
        }

        // Guards against a degenerate aperture that samples only the centre (which would silently disable DoF).
        Assert.IsTrue(maxSeen > radius * 0.5f, $"aperture samples never reached the rim (max={maxSeen}, R={radius})");
    }

    [TestMethod]
    public void BladedIris_ProducesAPolygonNotADisc()
    {
        // A 3-blade iris is the easiest polygon to falsify: sampling a disc would put ~fraction of samples
        // beyond the triangle's inradius in every direction, whereas the triangle is bounded by its edges.
        const int blades = 3;
        var lens = LensOptions.FromModel(LensModel.Cine) with { ApertureBlades = blades, BladeRotation = 0f };
        float radius = lens.ApertureRadiusWorld(Fov);

        uint rng = 4242u;
        float step = 2f * MathF.PI / blades;
        for (int i = 0; i < 500; i++)
        {
            LensSampler.SampleAperture(radius, blades, 0f, ref rng, out float lx, out float ly);

            // Inside a regular N-gon ⇔ inside every edge half-plane. Each edge's outward normal points at the
            // midpoint angle between two adjacent vertices, at distance = the inradius R·cos(π/N).
            float inradius = radius * MathF.Cos(MathF.PI / blades);
            for (int e = 0; e < blades; e++)
            {
                float a = step * e + step * 0.5f;
                float d = lx * MathF.Cos(a) + ly * MathF.Sin(a);
                Assert.IsTrue(d <= inradius + 1e-5f,
                    $"sample {i} escaped the {blades}-gon across edge {e} (d={d}, inradius={inradius})");
            }
        }
    }

    // ── §4.1 focal length ⇄ field of view ─────────────────────────────────────

    [TestMethod]
    public void LensWithoutZoom_DoesNotReframeTheShot()
    {
        // Switching a lens on must not change the framing — only a zoom (an explicit focal length) may.
        foreach (LensModel model in new[] { LensModel.Phone, LensModel.Simple, LensModel.Cine })
        {
            var lens = LensOptions.FromModel(model);
            Assert.AreEqual(Fov, lens.EffectiveFov(Fov), 1e-6f, $"{model} reframed the shot without a zoom");
        }
    }

    [TestMethod]
    public void Zoom_NarrowsTheFieldOfView()
    {
        var wide = LensOptions.FromModel(LensModel.Cine) with { FocalLengthMm = 18f };
        var tele = LensOptions.FromModel(LensModel.Cine) with { FocalLengthMm = 85f };

        Assert.IsTrue(tele.EffectiveFov(Fov) < wide.EffectiveFov(Fov),
            "a longer focal length must give a narrower field of view");

        // f = h / (2·tan(fov/2)) inverted — the textbook relationship, on Super35's 18.66 mm height.
        float expected = 2f * MathF.Atan(18.66f / (2f * 85f));
        Assert.AreEqual(expected, tele.EffectiveFov(Fov), 1e-5f);
    }

    [TestMethod]
    public void ImpliedFocalLength_RoundTripsWithTheCameraFov()
    {
        // With no zoom the lens derives its focal length from the scene's FOV; feeding that focal length back
        // must reproduce the same FOV, which is what makes "lens on ⇒ same framing" exact rather than close.
        var lens = LensOptions.FromModel(LensModel.Simple);
        float f = lens.EffectiveFocalLengthMm(Fov);
        float backToFov = 2f * MathF.Atan(lens.SensorHeightMm / (2f * f));
        Assert.AreEqual(Fov, backToFov, 1e-5f);
    }

    [TestMethod]
    public void ApertureRadius_FollowsTheFNumberDefinition()
    {
        // f/N means diameter = f/N, so radius = f/2N — in metres, since the world is modelled at human scale.
        var lens = LensOptions.FromModel(LensModel.Cine) with { FocalLengthMm = 50f, FStop = 2f };
        Assert.AreEqual(0.0125f, lens.ApertureRadiusWorld(Fov), 1e-6f, "50 mm at f/2 is a 12.5 mm aperture radius");

        // Opening up one stop (√2×) must widen the aperture by √2.
        var wider = lens with { FStop = 2f / MathF.Sqrt(2f) };
        Assert.AreEqual(MathF.Sqrt(2f), wider.ApertureRadiusWorld(Fov) / lens.ApertureRadiusWorld(Fov), 1e-4f);
    }

    [TestMethod]
    public void UnsetFStop_IsAPinholeAndSkipsApertureSampling()
    {
        var lens = LensOptions.FromModel(LensModel.Cine) with { FStop = 0f };
        Assert.AreEqual(0f, lens.ApertureRadiusWorld(Fov), "an unset f-stop must degenerate to a pinhole");
    }

    // ── §4.2/§4.4 the lens characters ────────────────────────────────────────

    [TestMethod]
    public void Phone_HasFarLessDefocusThanCine_AtTheSameFraming()
    {
        // The physical claim behind plan §4.2: a phone's tiny sensor forces a short focal length for the same
        // field of view, and a short focal length at the same f-stop is a *much* smaller aperture — which is
        // why a real phone has near-infinite depth of field and its portrait mode has to be synthetic.
        var phone = LensOptions.FromModel(LensModel.Phone);
        var cine = LensOptions.FromModel(LensModel.Cine);

        float phoneR = phone.ApertureRadiusWorld(Fov);
        float cineR = cine.ApertureRadiusWorld(Fov);

        Assert.IsTrue(phoneR > 0f && cineR > 0f);
        Assert.IsTrue(cineR > phoneR * 2f,
            $"the cine lens must defocus far more than the phone at equal framing (phone={phoneR}, cine={cineR})");
    }

    [TestMethod]
    public void PhonePortrait_DefocusesFarMoreThanThePhysicalPhone()
    {
        var physical = LensOptions.FromModel(LensModel.Phone);
        var portrait = LensOptions.PhonePortrait(scale: 12f);

        Assert.AreEqual(12f, portrait.ApertureRadiusWorld(Fov) / physical.ApertureRadiusWorld(Fov), 1e-3f,
            "portrait mode must scale the aperture by exactly the synthetic factor");
    }

    [TestMethod]
    public void ApertureScale_IsSyntheticAndDoesNotChangeFraming()
    {
        var portrait = LensOptions.PhonePortrait(scale: 12f);
        // The synthetic aperture is a cheat on the *blur*, not on the optics: the framing must not move.
        Assert.AreEqual(Fov, portrait.EffectiveFov(Fov), 1e-6f);
    }

    // ── §4.3 distortion ──────────────────────────────────────────────────────

    [TestMethod]
    public void BarrelDistortion_BendsEdgeRaysTowardTheAxis()
    {
        // Negative k1 pulls the sampled ray angle in toward the optical axis, which magnifies the scene at the
        // corners and bows straight lines outward — the barrel look.
        float px = 0.5f, py = 0.5f;
        float r0 = MathF.Sqrt(px * px + py * py);
        LensSampler.Distort(ref px, ref py, k1: -0.12f, k2: 0f);
        float r1 = MathF.Sqrt(px * px + py * py);

        Assert.IsTrue(r1 < r0, $"barrel distortion must shrink the image-plane radius ({r1} !< {r0})");
    }

    [TestMethod]
    public void PincushionDistortion_BendsEdgeRaysAwayFromTheAxis()
    {
        float px = 0.5f, py = 0.5f;
        float r0 = MathF.Sqrt(px * px + py * py);
        LensSampler.Distort(ref px, ref py, k1: 0.12f, k2: 0f);
        Assert.IsTrue(MathF.Sqrt(px * px + py * py) > r0, "pincushion distortion must grow the image-plane radius");
    }

    [TestMethod]
    public void Distortion_LeavesTheOpticalAxisFixed()
    {
        // Radial distortion is radial: the centre pixel must not move for any k, or the whole image would slide.
        float px = 0f, py = 0f;
        LensSampler.Distort(ref px, ref py, k1: -0.5f, k2: 0.3f);
        Assert.AreEqual(0f, px);
        Assert.AreEqual(0f, py);
    }

    [TestMethod]
    public void Distortion_GrowsWithRadius()
    {
        // The corners must be distorted more than the centre — that is what makes it read as a lens rather
        // than a uniform zoom.
        float Displacement(float r)
        {
            float x = r, y = 0f;
            LensSampler.Distort(ref x, ref y, k1: -0.12f, k2: -0.03f);
            return MathF.Abs(r - x);
        }

        Assert.IsTrue(Displacement(0.8f) > Displacement(0.3f), "distortion must increase toward the corners");
    }

    // ── §4.1 vignetting ──────────────────────────────────────────────────────

    [TestMethod]
    public void Vignette_IsExactlyOffAtZeroStrength()
    {
        // Must be exactly 1 — not approximately — or the pinhole's goldens would drift.
        Assert.AreEqual(1f, LensSampler.VignetteFactor(0.9f, 0.9f, ImgPlaneZ, strength: 0f));
    }

    [TestMethod]
    public void Vignette_DarkensCornersButNotTheCentre()
    {
        float centre = LensSampler.VignetteFactor(0f, 0f, ImgPlaneZ, strength: 1f);
        float corner = LensSampler.VignetteFactor(0.9f, 0.9f, ImgPlaneZ, strength: 1f);

        Assert.AreEqual(1f, centre, 1e-6f, "the optical axis must not be vignetted");
        Assert.IsTrue(corner < centre, "the corners must be darker than the centre");
    }

    [TestMethod]
    public void Vignette_AtStrengthOne_IsThePhysicalCosFourthLaw()
    {
        // cos⁴θ is the natural illumination falloff; strength 1 must be exactly that, not an approximation of it.
        const float px = 0.6f, py = 0.4f;
        float cos2 = (ImgPlaneZ * ImgPlaneZ) / (px * px + py * py + ImgPlaneZ * ImgPlaneZ);
        Assert.AreEqual(cos2 * cos2, LensSampler.VignetteFactor(px, py, ImgPlaneZ, 1f), 1e-6f);
    }

    [TestMethod]
    public void Vignette_StrengthAboveOne_ExaggeratesTheFalloff()
    {
        float physical = LensSampler.VignetteFactor(0.7f, 0.7f, ImgPlaneZ, strength: 1f);
        float exaggerated = LensSampler.VignetteFactor(0.7f, 0.7f, ImgPlaneZ, strength: 2.2f);
        Assert.IsTrue(exaggerated < physical, "the old camera's deep barrel must darken more than physics alone");
    }

    [TestMethod]
    public void SimpleCamera_VignettesHarderThanThePhone()
    {
        var phone = LensOptions.FromModel(LensModel.Phone);
        var simple = LensOptions.FromModel(LensModel.Simple);

        float phoneCorner = LensSampler.VignetteFactor(0.8f, 0.8f, ImgPlaneZ, phone.VignetteStrength);
        float simpleCorner = LensSampler.VignetteFactor(0.8f, 0.8f, ImgPlaneZ, simple.VignetteStrength);

        Assert.IsTrue(simpleCorner < phoneCorner, "the old camera is the heavy-vignette character");
    }

    // ── §4.4 cat's-eye ───────────────────────────────────────────────────────

    [TestMethod]
    public void CatEye_SquashesOffAxisBokehButLeavesTheAxisRound()
    {
        // On-axis the aperture stays a full circle; off-axis the barrel clips it into the eye shape.
        float lx = 1f, ly = 0f;
        LensSampler.ApplyCatEye(ref lx, ref ly, fieldX: 0f, fieldY: 0f, fieldRadius: 0f);
        Assert.AreEqual(1f, lx, 1e-6f, "an on-axis aperture must not be squashed");

        lx = 1f; ly = 0f;
        LensSampler.ApplyCatEye(ref lx, ref ly, fieldX: 1f, fieldY: 0f, fieldRadius: 0.9f);
        Assert.IsTrue(lx < 1f, "an off-axis aperture must be squashed along the radial direction");
    }

    [TestMethod]
    public void CatEye_LeavesTheTangentialAxisAlone()
    {
        // The squash is radial only — that is what makes the highlight an eye pointing at the frame centre
        // rather than a uniformly smaller circle.
        float lx = 0f, ly = 1f;   // tangential, for a field direction along +X
        LensSampler.ApplyCatEye(ref lx, ref ly, fieldX: 1f, fieldY: 0f, fieldRadius: 0.9f);

        Assert.AreEqual(0f, lx, 1e-6f);
        Assert.AreEqual(1f, ly, 1e-6f, "the tangential axis of a cat's eye must keep its full width");
    }

    [TestMethod]
    public void CatEye_SquashesMoreTowardTheCorners()
    {
        float Squashed(float fieldRadius)
        {
            float lx = 1f, ly = 0f;
            LensSampler.ApplyCatEye(ref lx, ref ly, 1f, 0f, fieldRadius);
            return lx;
        }

        Assert.IsTrue(Squashed(0.9f) < Squashed(0.3f), "mechanical vignetting must worsen toward the frame edge");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static Camera TestCamera() => new()
    {
        Position = Vector3.Zero,
        Rotation = Quaternion.Identity,
        Fov = Fov,
        Aspect = 1f,
        ImgPlaneZ = ImgPlaneZ,
    };

    // Six inward-facing walls, so every primary ray hits something and every bounce is populated.
    private static Tracable[] Box(float e)
    {
        var mat = new MaterialData("x", "X", null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, Flat(0.6f), SurfaceKind.Diffuse);
        TracableRectangle F(Vector3 l1, Vector3 l2, Vector3 l3) => new((l1, l2, l3), mat);
        return
        [
            F(new(-e, -e, e), new(-e, e, e), new(e, -e, e)),
            F(new(-e, -e, -e), new(e, -e, -e), new(-e, e, -e)),
            F(new(e, -e, -e), new(e, -e, e), new(e, e, -e)),
            F(new(-e, -e, -e), new(-e, e, -e), new(-e, -e, e)),
            F(new(-e, e, -e), new(e, e, -e), new(-e, e, e)),
            F(new(-e, -e, -e), new(-e, -e, e), new(e, -e, -e)),
        ];
    }

    private static SpectralData Flat(float value)
    {
        var wavelengths = new List<int>();
        var values = new List<float>();
        for (int w = 360; w <= 830; w += 5)
        {
            wavelengths.Add(w);
            values.Add(value);
        }
        float[] v = values.ToArray();
        return new SpectralData(wavelengths.ToArray(), v, (float[])v.Clone());
    }

    private static JobSystem BuildJobSystem(SamplingOptions sampling)
    {
        const int size = 8;
        return new JobSystem(
            size, size, Box(10f), TestCamera(), stride: size * 4,
            lights: [new Light { Position = new Vector3(0f, 8f, 0f), Color = Vector3.One }],
            renderOptions: new RenderOptions(Lighting: LightingMode.NEE, MaxSampleCount: 64),
            samplingOptions: sampling,
            denoiseOptions: new DenoiseOptions(EnableTaa: false, EnableDiffuseCache: false, SmokeMode: SmokeMode.None),
            debugOptions: new DebugOptions());
    }

    private static Vector3[] RenderScene(SamplingOptions sampling)
    {
        const int size = 8;
        const int frames = 12;
        var camera = TestCamera();
        var js = BuildJobSystem(sampling);

        for (int f = 0; f < frames; f++)
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    js.TraceCore(camera, y, x);

        var result = new Vector3[size * size];
        for (int i = 0; i < result.Length; i++)
            result[i] = js.AccumXYZ[i];
        return result;
    }
}
