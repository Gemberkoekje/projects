using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

/// <summary>
/// Phase 0.4 of the spectral &amp; optical effects plan: the analytic <see cref="Sphere"/>
/// primitive. Pins the ray/sphere intersection, the smooth outward normal, and the AABB
/// bounds that let the BVH accept it unchanged.
/// </summary>
[TestClass]
public sealed class SphereTests
{
    private static MaterialData Diffuse()
    {
        var wl = new List<int>();
        var v = new List<float>();
        for (int w = 360; w <= 830; w += 5) { wl.Add(w); v.Add(0.7f); }
        float[] a = v.ToArray();
        return new MaterialData("s", "s", null, null, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
            new SpectralData(wl.ToArray(), a, (float[])a.Clone()), SurfaceKind.Diffuse);
    }

    private static Ray RayTo(Vector3 origin, Vector3 target)
        => new() { Origin = origin, Direction = Vector3.Normalize(target - origin), Wavelength = 550f, Intensity = 1f };

    [TestMethod]
    public void Intersect_HitsFrontFace_NearRootWithOutwardNormal()
    {
        var s = new Sphere(new Vector3(0, 0, 5), 1f, Diffuse());
        HitInfo? h = s.Intersect(RayTo(Vector3.Zero, new Vector3(0, 0, 5)));

        Assert.IsTrue(h.HasValue);
        Assert.AreEqual(4f, h.Value.T, 1e-4f);                       // front face at z=4
        Assert.AreEqual(new Vector3(0, 0, 5) + new Vector3(0, 0, -1), h.Value.Location, "hit at (0,0,4)");
        // Outward normal at the front face points back toward the camera (−Z), and is oriented against the ray.
        Assert.IsTrue(Vector3.Dot(h.Value.Normal, new Vector3(0, 0, -1)) > 0.999f);
        Assert.AreEqual(1f, h.Value.Normal.Length(), 1e-4f);
    }

    [TestMethod]
    public void Intersect_Miss_ReturnsNull()
    {
        var s = new Sphere(new Vector3(0, 0, 5), 1f, Diffuse());
        Assert.IsNull(s.Intersect(RayTo(Vector3.Zero, new Vector3(3, 0, 5)))); // aimed well past the rim
        Assert.IsNull(s.Intersect(RayTo(Vector3.Zero, new Vector3(0, 0, -5)))); // behind the camera
    }

    [TestMethod]
    public void Intersect_FromInside_ReturnsFarRoot()
    {
        var s = new Sphere(Vector3.Zero, 2f, Diffuse());
        HitInfo? h = s.Intersect(new Ray { Origin = Vector3.Zero, Direction = new Vector3(0, 0, 1), Wavelength = 550f });
        Assert.IsTrue(h.HasValue);
        Assert.AreEqual(2f, h.Value.T, 1e-4f);                       // exits at z=2
        // Oriented against the ray → points back inward (−Z) even though the geometric normal is +Z.
        Assert.IsTrue(Vector3.Dot(h.Value.Normal, new Vector3(0, 0, -1)) > 0.999f);
    }

    [TestMethod]
    public void Bounds_EncloseTheSphere()
    {
        var s = new Sphere(new Vector3(1, 2, 3), 0.5f, Diffuse());
        Assert.AreEqual(new Vector3(0.5f, 1.5f, 2.5f), s.Bounds.Min);
        Assert.AreEqual(new Vector3(1.5f, 2.5f, 3.5f), s.Bounds.Max);
    }

    [TestMethod]
    public void Normal_IsUnitAndRadial_AcrossTheSurface()
    {
        var center = new Vector3(0, 0, 6);
        var s = new Sphere(center, 1.5f, Diffuse());
        // Fan several rays across the disc; each hit's normal must be unit and point radially.
        for (float off = -1f; off <= 1f; off += 0.5f)
        {
            HitInfo? h = s.Intersect(RayTo(Vector3.Zero, center + new Vector3(off, 0, 0)));
            if (!h.HasValue) continue;
            Assert.AreEqual(1f, h.Value.Normal.Length(), 1e-3f, $"unit normal at off={off}");
            Vector3 radial = Vector3.Normalize(h.Value.Location - center);
            // The oriented normal equals the outward radial (front-facing hits from outside).
            Assert.IsTrue(Vector3.Dot(h.Value.Normal, radial) > 0.999f, $"radial normal at off={off}");
        }
    }
}
