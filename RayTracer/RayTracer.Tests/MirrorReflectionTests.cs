using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// Phase 1.1 of the spectral &amp; optical effects plan (spectral-effects-plan.md):
/// specular mirror surfaces. These drive the CPU path tracer over explicit
/// mirror/wall scenes and check that a mirror reflects the scene behind the
/// viewer, that the metal's spectral reflectance curve — not a hardcoded tint —
/// colours the reflection (gold warm vs silver neutral), that a mirror facing
/// nothing is black, and that facing mirrors terminate at the bounce cap.
/// </summary>
[TestClass]
public sealed class MirrorReflectionTests
{
    // ── Helpers ───────────────────────────────────────────────────────

    private static SpectralData Spectrum(Func<int, float> reflectance, int min = 360, int max = 830, int step = 5)
    {
        var wavelengths = new List<int>();
        var values = new List<float>();
        for (int w = min; w <= max; w += step)
        {
            wavelengths.Add(w);
            values.Add(reflectance(w));
        }

        float[] v = values.ToArray();
        return new SpectralData(wavelengths.ToArray(), v, (float[])v.Clone());
    }

    private static MaterialData Material(SurfaceKind kind, SpectralData spectrum)
        => new("x", "X", null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, spectrum, kind);

    private static SpectralData Flat(float value) => Spectrum(_ => value);

    // A large axis-aligned quad at z = zPlane. faceUp==true gives normal +Z,
    // otherwise −Z, so the quad can face the camera from either side.
    private static TracableRectangle Wall(float zPlane, bool normalTowardNegZ, MaterialData material)
    {
        const float e = 20f;
        // edge1 × edge2 must point along the desired normal.
        // normal −Z: edge1 = +Y, edge2 = +X. normal +Z: edge1 = +X, edge2 = +Y.
        Vector3 l1 = new(-e, -e, zPlane);
        Vector3 l2 = normalTowardNegZ ? new Vector3(-e, e, zPlane) : new Vector3(e, -e, zPlane);
        Vector3 l3 = normalTowardNegZ ? new Vector3(e, -e, zPlane) : new Vector3(-e, e, zPlane);
        return new TracableRectangle((l1, l2, l3), material);
    }

    private static Vector3 RenderCenter(Tracable[] scene, int frames = 100, int size = 16)
    {
        var camera = new Camera
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity, // looks down +Z
            Fov = MathF.PI / 3f,
            Aspect = 1f,
            ImgPlaneZ = 1f,
        };

        var js = new JobSystem(
            size, size, scene, camera, stride: size * 4, lights: null,
            renderOptions: new RenderOptions(Lighting: LightingMode.None, MaxSampleCount: 500),
            samplingOptions: new SamplingOptions(SubPixelJitter: false),
            denoiseOptions: new DenoiseOptions(
                EnableTaa: false, EnableDiffuseCache: false, SmokeMode: SmokeMode.None),
            debugOptions: new DebugOptions());

        for (int f = 0; f < frames; f++)
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    js.TraceCore(camera, y, x);

        int center = (size / 2) * size + (size / 2);
        return js.AccumXYZ[center];
    }

    private static (float x, float y) Chroma(Vector3 xyz)
    {
        float sum = xyz.X + xyz.Y + xyz.Z;
        return sum > 1e-8f ? (xyz.X / sum, xyz.Y / sum) : (0f, 0f);
    }

    // ── Tests ─────────────────────────────────────────────────────────

    /// <summary>
    /// A neutral mirror in front of the camera reflects a coloured wall placed
    /// behind the camera: the mirror pixel takes on the wall's hue, scaled by the
    /// mirror's (flat) reflectance.
    /// </summary>
    [TestMethod]
    public void Mirror_ReflectsColoredWallBehindCamera_ShowsWallHue()
    {
        // A red-ish wall: low reflectance in the blue, high in the red.
        var redWall = Material(SurfaceKind.Diffuse,
            Spectrum(w => 0.05f + 0.80f * Math.Clamp((w - 500f) / 150f, 0f, 1f)));
        var mirror = Material(SurfaceKind.Mirror, Flat(0.9f));

        // Mirror in front (+Z), wall behind the camera (−Z). The mirror reflects
        // the ray back toward −Z onto the wall.
        var mirrorScene = new Tracable[]
        {
            Wall(5f, normalTowardNegZ: true, mirror),
            Wall(-5f, normalTowardNegZ: false, redWall),
        };

        // Reference: the same wall viewed directly (in front of the camera).
        var directScene = new Tracable[] { Wall(5f, normalTowardNegZ: true, redWall) };

        Vector3 mirrored = RenderCenter(mirrorScene);
        Vector3 direct = RenderCenter(directScene);

        Assert.IsTrue(mirrored.Y > 1e-3f, "Mirror reflecting a lit wall must not be black.");

        var (mx, my) = Chroma(mirrored);
        var (dx, dy) = Chroma(direct);
        Assert.AreEqual(dx, mx, 2e-3f, "Reflected hue (x) should match the wall seen directly.");
        Assert.AreEqual(dy, my, 2e-3f, "Reflected hue (y) should match the wall seen directly.");

        // A flat 0.9 mirror scales the reflected radiance by 0.9.
        Assert.AreEqual(0.9f * direct.Y, mirrored.Y, 1e-2f * direct.Y + 1e-4f,
            "A neutral 0.9 mirror should reflect ~90% of the wall's brightness.");
    }

    /// <summary>Gold and silver mirrors reflecting the same white wall must differ,
    /// with gold reading warmer (higher CIE x) — emerging from the reflectance
    /// curves, with no hardcoded tint.</summary>
    [TestMethod]
    public void GoldVsSilverMirror_DifferUnderWhiteLight_GoldIsWarmer()
    {
        var whiteWall = Material(SurfaceKind.Diffuse, Flat(0.9f));

        // Silver: nearly flat, high reflectance. Gold: suppressed in the blue,
        // high in the red (a simplified metal curve).
        var silver = Material(SurfaceKind.Mirror, Flat(0.92f));
        var gold = Material(SurfaceKind.Mirror,
            Spectrum(w => 0.35f + 0.60f * Math.Clamp((w - 450f) / 150f, 0f, 1f)));

        Vector3 silverReflect = RenderCenter(new Tracable[]
        {
            Wall(5f, normalTowardNegZ: true, silver),
            Wall(-5f, normalTowardNegZ: false, whiteWall),
        });
        Vector3 goldReflect = RenderCenter(new Tracable[]
        {
            Wall(5f, normalTowardNegZ: true, gold),
            Wall(-5f, normalTowardNegZ: false, whiteWall),
        });

        Assert.IsTrue(silverReflect.Y > 1e-3f, "Silver mirror reflection must not be black.");
        Assert.IsTrue(goldReflect.Y > 1e-3f, "Gold mirror reflection must not be black.");

        var (sx, _) = Chroma(silverReflect);
        var (gx, _) = Chroma(goldReflect);
        Assert.IsTrue(gx > sx + 0.01f,
            $"Gold should read warmer than silver (gold x={gx:F4} should exceed silver x={sx:F4}).");
    }

    /// <summary>A mirror facing empty space has nothing to reflect and reads black.</summary>
    [TestMethod]
    public void Mirror_FacingNothing_IsBlack()
    {
        var mirror = Material(SurfaceKind.Mirror, Flat(0.9f));
        var scene = new Tracable[] { Wall(5f, normalTowardNegZ: true, mirror) };

        Vector3 result = RenderCenter(scene);

        Assert.IsTrue(result.Y < 1e-4f, $"Mirror with nothing behind the camera should be ~black (Y={result.Y}).");
    }

    /// <summary>Two mirrors facing each other must terminate at the bounce cap
    /// rather than recursing forever, producing finite, non-negative output.</summary>
    [TestMethod]
    public void FacingMirrors_TerminateAtBounceCap()
    {
        var mirror = Material(SurfaceKind.Mirror, Flat(0.9f));
        var scene = new Tracable[]
        {
            Wall(5f, normalTowardNegZ: true, mirror),   // in front, faces camera
            Wall(-5f, normalTowardNegZ: false, mirror), // behind, faces the first
        };

        Vector3 result = RenderCenter(scene);

        Assert.IsTrue(float.IsFinite(result.X) && float.IsFinite(result.Y) && float.IsFinite(result.Z),
            "Facing mirrors must not diverge or overflow.");
        Assert.IsTrue(result.Y >= 0f, "Radiance must be non-negative.");
    }
}
