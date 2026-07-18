using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// Pins the GPU C#-reference replica of the lens (plan §4.1) — <see cref="Phase1Reference.PrimaryRayThroughLens"/>
/// — against the CPU's own ray generation. CI has no GPU, so this replica <i>is</i> the contract that
/// <c>PathTracePhase6.hlsl</c>'s <c>GenerateLensRay</c> is a line-for-line port of (working convention 1).
/// </summary>
[TestClass]
public sealed class GpuLensTests
{
    private const int Width = 64;
    private const int Height = 48;
    private const float ImgPlaneZ = 1f;

    private static Camera TestCamera() => new()
    {
        Position = new Vector3(1.5f, 1f, -2f),
        Rotation = Quaternion.CreateFromYawPitchRoll(0.4f, -0.2f, 0f),
        Fov = MathF.PI / 3f,
        Aspect = (float)Width / Height,
        ImgPlaneZ = ImgPlaneZ,
    };

    private static (float tan, float aspectTan, Vector4 rot) Basis(Camera c, float fov)
    {
        float tan = MathF.Tan(fov * 0.5f);
        return (tan, c.Aspect * tan, new Vector4(c.Rotation.X, c.Rotation.Y, c.Rotation.Z, c.Rotation.W));
    }

    [TestMethod]
    public void PinholeLens_ReferenceMatchesTheHistoricalReferencePath()
    {
        // The lens-aware entry point must collapse onto the pre-lens PrimaryRayDirection exactly, or the
        // shader's no-effect path would drift from the goldens the moment it is ported.
        var camera = TestCamera();
        var (tan, aspectTan, rot) = Basis(camera, camera.Fov);
        var lens = LensOptions.Pinhole;

        for (int y = 3; y < Height; y += 7)
        {
            for (int x = 3; x < Width; x += 7)
            {
                for (uint s = 0; s < 3; s++)
                {
                    Vector3 expected = Phase1Reference.PrimaryRayDirection(
                        x, y, s, rot, ImgPlaneZ, tan, aspectTan, 1f / Width, 1f / Height, subPixelJitter: true);

                    Phase1Reference.PrimaryRayThroughLens(
                        x, y, s, camera.Position, rot, ImgPlaneZ, tan, aspectTan,
                        1f / Width, 1f / Height, subPixelJitter: true,
                        lens, apertureRadius: 0f,
                        out Vector3 origin, out Vector3 dir, out float vignette);

                    string at = $"px=({x},{y}) sample={s}";
                    Assert.AreEqual(camera.Position, origin, $"pinhole origin moved at {at}");
                    Assert.AreEqual(expected.X, dir.X, $"pinhole dir X moved at {at}");
                    Assert.AreEqual(expected.Y, dir.Y, $"pinhole dir Y moved at {at}");
                    Assert.AreEqual(expected.Z, dir.Z, $"pinhole dir Z moved at {at}");
                    Assert.AreEqual(1f, vignette, $"pinhole must not vignette at {at}");
                }
            }
        }
    }

    [TestMethod]
    public void LensRays_LeaveTheApertureAndConvergeOnTheFocusPlaneInWorldSpace()
    {
        // The thin-lens property, checked after the rotation/translation into world space — which is the part
        // the reference adds on top of the shared camera-space math, and therefore the part worth pinning.
        var camera = TestCamera();
        var lens = LensOptions.FromModel(LensModel.Cine) with { FocusDistance = 4f };
        float fov = lens.EffectiveFov(camera.Fov);
        var (tan, aspectTan, rot) = Basis(camera, fov);
        float radius = lens.ApertureRadiusWorld(fov);

        // The camera's forward axis in world space — the focus plane sits FocusDistance along it.
        Vector3 forward = Vector3.Normalize(Phase1Reference.RotateByQuaternion(rot, new Vector3(0f, 0f, ImgPlaneZ)));

        const int x = 20, y = 15;
        Vector3? reference = null;
        for (uint s = 0; s < 64; s++)
        {
            Phase1Reference.PrimaryRayThroughLens(
                x, y, s, camera.Position, rot, ImgPlaneZ, tan, aspectTan,
                1f / Width, 1f / Height, subPixelJitter: false,
                lens, radius, out Vector3 origin, out Vector3 dir, out _);

            // Every ray starts on the physical aperture, which is a real disc around the camera position.
            float offset = Vector3.Distance(origin, camera.Position);
            Assert.IsTrue(offset <= radius + 1e-5f, $"sample {s} started outside the aperture (offset={offset})");

            // ...and lands on the same world point of the focus plane, whichever part of the aperture it left.
            float t = lens.FocusDistance / Vector3.Dot(dir, forward);
            Vector3 landed = origin + dir * t;
            if (reference is null)
            {
                reference = landed;
                continue;
            }

            Assert.AreEqual(0f, Vector3.Distance(reference.Value, landed), 1e-3f,
                $"sample {s} did not converge on the focus point ({landed} vs {reference})");
        }

        Assert.IsNotNull(reference);
    }

    [TestMethod]
    public void LensRays_AreNotAllIdentical()
    {
        // Guards the boring failure where the aperture silently degenerates and the "lens" is a pinhole with
        // extra steps — every test above would still pass.
        var camera = TestCamera();
        var lens = LensOptions.FromModel(LensModel.Cine);
        float fov = lens.EffectiveFov(camera.Fov);
        var (tan, aspectTan, rot) = Basis(camera, fov);
        float radius = lens.ApertureRadiusWorld(fov);

        Phase1Reference.PrimaryRayThroughLens(
            30, 20, 0u, camera.Position, rot, ImgPlaneZ, tan, aspectTan, 1f / Width, 1f / Height,
            false, lens, radius, out Vector3 o0, out Vector3 d0, out _);
        Phase1Reference.PrimaryRayThroughLens(
            30, 20, 1u, camera.Position, rot, ImgPlaneZ, tan, aspectTan, 1f / Width, 1f / Height,
            false, lens, radius, out Vector3 o1, out Vector3 d1, out _);

        Assert.IsTrue(Vector3.Distance(o0, o1) > 1e-6f, "consecutive samples must land on different aperture points");
        Assert.IsTrue(Vector3.Distance(d0, d1) > 1e-9f, "consecutive samples must take different directions");
    }

    [TestMethod]
    public void ReferenceVignette_MatchesTheSharedSampler()
    {
        var camera = TestCamera();
        var lens = LensOptions.FromModel(LensModel.Simple);
        var (tan, aspectTan, rot) = Basis(camera, camera.Fov);

        // Corner must be darker than centre through the reference's own path (not just the sampler's).
        Phase1Reference.PrimaryRayThroughLens(
            Width / 2, Height / 2, 0u, camera.Position, rot, ImgPlaneZ, tan, aspectTan,
            1f / Width, 1f / Height, false, lens, 0f, out _, out _, out float centre);
        Phase1Reference.PrimaryRayThroughLens(
            0, 0, 0u, camera.Position, rot, ImgPlaneZ, tan, aspectTan,
            1f / Width, 1f / Height, false, lens, 0f, out _, out _, out float corner);

        Assert.IsTrue(corner < centre, $"the old camera must vignette its corners (centre={centre}, corner={corner})");
        Assert.AreEqual(1f, centre, 0.02f, "the frame centre sits on the optical axis and must stay ~unvignetted");
    }

    [TestMethod]
    public void ZoomedLens_ReframesTheReferenceRays()
    {
        // A zoom is the one lens change that legitimately moves the framing; pin that it reaches the rays.
        var camera = TestCamera();
        var tele = LensOptions.FromModel(LensModel.Cine) with { FocalLengthMm = 85f, FStop = 0f };
        float teleFov = tele.EffectiveFov(camera.Fov);
        Assert.IsTrue(teleFov < camera.Fov, "the 85 mm must be narrower than the scene fov to test anything");

        var wideBasis = Basis(camera, camera.Fov);
        var teleBasis = Basis(camera, teleFov);

        Vector3 forward = Vector3.Normalize(
            Phase1Reference.RotateByQuaternion(wideBasis.rot, new Vector3(0f, 0f, ImgPlaneZ)));

        Phase1Reference.PrimaryRayThroughLens(
            0, 0, 0u, camera.Position, wideBasis.rot, ImgPlaneZ, wideBasis.tan, wideBasis.aspectTan,
            1f / Width, 1f / Height, false, LensOptions.Pinhole, 0f, out _, out Vector3 wideDir, out _);
        Phase1Reference.PrimaryRayThroughLens(
            0, 0, 0u, camera.Position, teleBasis.rot, ImgPlaneZ, teleBasis.tan, teleBasis.aspectTan,
            1f / Width, 1f / Height, false, tele, 0f, out _, out Vector3 teleDir, out _);

        // The corner ray of the zoomed lens must sit closer to the optical axis — that is what "narrower" means.
        Assert.IsTrue(Vector3.Dot(teleDir, forward) > Vector3.Dot(wideDir, forward),
            "the telephoto's corner ray must be closer to the axis than the wide's");
    }
}
