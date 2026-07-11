using System.Numerics;

namespace RayTracer;

public class Plane(Vector3 point, Vector3 normal, MaterialData material) : Tracable
{
    public Vector3 Point = point;
    public Vector3 Normal = Vector3.Normalize(normal);

    public virtual AABB Bounds => new(new Vector3(-1e10f), new Vector3(1e10f));

    /// <inheritdoc/>
    public virtual HitInfo? Intersect(Ray ray)
    {
        var p0 = Point;
        var l0 = ray.Origin;
        var n = Normal;
        var l = ray.Direction;

        var denom = Vector3.Dot(l, n);
        if (MathF.Abs(denom) < 1e-6)
        {
            return null;
        }

        var d = Vector3.Dot((p0 - l0), n) / denom;
        if (d < 1e-4f)
            return null;
        var faceNormal = denom < 0 ? n : -n;
        return HitInfo.FromMaterial(d, l0 + l * d, faceNormal, null,
            material.GetSpectralReflectance((int)ray.Wavelength), material, (int)ray.Wavelength);
    }
}
