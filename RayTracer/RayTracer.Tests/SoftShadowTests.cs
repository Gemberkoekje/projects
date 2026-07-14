using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// Phase C1 of the shadows-and-caustics plan: spherical area lights and the soft shadows they cast.
/// A point light (<see cref="Light.Radius"/> == 0) targets its centre and casts a razor-sharp shadow
/// (byte-identical to before — no RNG is drawn); a light with a radius has each NEE sample target a
/// random point on its sphere (<see cref="Light.SamplePoint"/>), so the accumulated shadow-ray
/// visibility softens into a penumbra. These pin the sampler and the visibility-averaging that the
/// path tracer's NEE sites run — the actual soft-shadow mechanism, deterministically and without the
/// falloff/GI confounds of a full render.
/// </summary>
[TestClass]
public sealed class SoftShadowTests
{
    private static MaterialData Diffuse()
    {
        var wl = new List<int>();
        var v = new List<float>();
        for (int w = 360; w <= 830; w += 5) { wl.Add(w); v.Add(0.5f); }
        float[] a = v.ToArray();
        return new("occ", "occ", null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
            new SpectralData(wl.ToArray(), a, (float[])a.Clone()), SurfaceKind.Diffuse);
    }

    // The fraction of NEE samples that reach the light unblocked — the accumulated NEE visibility at a
    // receiver point, computed exactly as the path tracer does: sample a point on the light, trace the
    // shadow ray to it, and average the BVH transmittance over many samples.
    private static float AverageVisibility(BVH bvh, Vector3 point, Vector3 normal, Light light, int samples, uint seed)
    {
        uint rng = seed;
        float sum = 0f;
        for (int i = 0; i < samples; i++)
        {
            Vector3 toLight = light.SamplePoint(ref rng) - point;
            float dist = toLight.Length();
            var ray = new Ray { Origin = point + normal * 1e-3f, Direction = toLight / dist, Wavelength = 550, Intensity = 1f };
            sum += bvh.Transmittance(ray, dist - 2e-3f);
        }
        return sum / samples;
    }

    // ── Light.SamplePoint ─────────────────────────────────────────────────

    [TestMethod]
    public void SamplePoint_PointLight_ReturnsCentreAndDrawsNoRng()
    {
        var light = new Light { Position = new Vector3(1f, 2f, 3f), Color = Vector3.One, Radius = 0f };
        uint rng = 12345u;
        Vector3 p = light.SamplePoint(ref rng);
        Assert.AreEqual(light.Position, p, "A point light samples its exact centre.");
        Assert.AreEqual(12345u, rng, "A point light must draw no RNG, so the random stream is unchanged.");
    }

    [TestMethod]
    public void SamplePoint_AreaLight_LiesOnSphereAndAdvancesRng()
    {
        var light = new Light { Position = new Vector3(0f, 5f, 0f), Color = Vector3.One, Radius = 2f };
        uint rng = 1u;
        for (int i = 0; i < 64; i++)
        {
            uint before = rng;
            Vector3 p = light.SamplePoint(ref rng);
            Assert.AreEqual(2f, (p - light.Position).Length(), 1e-4f, "Sampled point must lie on the light's sphere.");
            Assert.AreNotEqual(before, rng, "An area light must advance the RNG.");
        }
    }

    // ── Soft shadows through a real occluder ───────────────────────────────

    [TestMethod]
    public void PointLight_CastsHardShadow_BinaryVisibility()
    {
        // Occluder sphere on the axis between the receiver and the light centre.
        var bvh = new BVH([new Sphere(new Vector3(0f, 0f, 5f), 1f, Diffuse())]);
        var point = Vector3.Zero;
        var normal = Vector3.UnitZ;

        var umbra = new Light { Position = new Vector3(0f, 0f, 10f), Color = Vector3.One, Radius = 0f };
        Assert.AreEqual(0f, AverageVisibility(bvh, point, normal, umbra, 256, 7u), "Point light directly behind the occluder → fully shadowed.");

        // A receiver well to the side sees the light with nothing in the way.
        var clear = new Light { Position = new Vector3(8f, 0f, 10f), Color = Vector3.One, Radius = 0f };
        Assert.AreEqual(1f, AverageVisibility(bvh, new Vector3(8f, 0f, 0f), normal, clear, 256, 7u), "Unoccluded point light → fully lit.");
    }

    [TestMethod]
    public void AreaLight_CastsPenumbra_FractionalVisibility()
    {
        // Same axis occluder; a large light whose rim peeks past the occluder puts the on-axis
        // receiver in the penumbra — some samples clear the occluder, some do not.
        var bvh = new BVH([new Sphere(new Vector3(0f, 0f, 5f), 1f, Diffuse())]);
        var point = Vector3.Zero;
        var normal = Vector3.UnitZ;

        var small = new Light { Position = new Vector3(0f, 0f, 10f), Color = Vector3.One, Radius = 1f };
        var large = new Light { Position = new Vector3(0f, 0f, 10f), Color = Vector3.One, Radius = 3f };

        float vSmall = AverageVisibility(bvh, point, normal, small, 4096, 99u);
        float vLarge = AverageVisibility(bvh, point, normal, large, 4096, 99u);

        // A radius-1 light is still fully blocked here (its rim can't clear the unit occluder), but a
        // radius-3 light peeks past → a fractional, penumbral visibility strictly between dark and lit,
        // and larger than the smaller light's.
        Assert.AreEqual(0f, vSmall, $"A small area light is still fully occluded here (v={vSmall}).");
        Assert.IsTrue(vLarge > 0.02f && vLarge < 0.98f, $"A large area light casts a penumbra — fractional visibility (v={vLarge}).");
        Assert.IsTrue(vLarge > vSmall, $"A wider light lets more of the receiver see past the occluder (small {vSmall}, large {vLarge}).");
    }
}
