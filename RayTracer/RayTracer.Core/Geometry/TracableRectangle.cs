using System.Numerics;

namespace RayTracer;

public class TracableRectangle((Vector3 l1, Vector3 l2, Vector3 l3) Location, MaterialData material) : Plane(
        Location.l1,
        Vector3.Cross(Location.l2 - Location.l1, Location.l3 - Location.l1),
        material
        ), Tracable, IQuadPrimitive
{
    // Precomputed at construction time - avoids recomputing per ray.
    private readonly Vector3 _edge1 = Location.l2 - Location.l1;
    private readonly Vector3 _edge2 = Location.l3 - Location.l1;
    private readonly float _edge1LenSq = Vector3.Dot(Location.l2 - Location.l1, Location.l2 - Location.l1);
    private readonly float _edge2LenSq = Vector3.Dot(Location.l3 - Location.l1, Location.l3 - Location.l1);
    private readonly AABB _bounds = ComputeBounds(Location);

    public override AABB Bounds => _bounds;

    /// <inheritdoc/>
    public Vector3 L1 => Location.l1;
    /// <inheritdoc/>
    public Vector3 Edge1 => _edge1;
    /// <inheritdoc/>
    public Vector3 Edge2 => _edge2;
    /// <inheritdoc/>
    public Vector3 QuadNormal => Normal;
    /// <inheritdoc/>
    public QuadPattern Pattern => QuadPattern.Plain;
    /// <inheritdoc/>
    public MaterialData PrimaryMaterial => material;
    /// <inheritdoc/>
    public MaterialData? SecondaryMaterial => null;
    /// <inheritdoc/>
    public float PatternP0 => 0f;
    /// <inheritdoc/>
    public float PatternP1 => 0f;
    /// <inheritdoc/>
    public float PatternP2 => 0f;
    /// <inheritdoc/>
    public float PatternP3 => 0f;

    /// <inheritdoc/>
    public override HitInfo? Intersect(Ray ray)
    {
        // Inline plane intersection - avoids the base.Intersect() material
        // lookup that was wasted on every plane hit that fell outside the
        // rectangle bounds.
        var denom = Vector3.Dot(ray.Direction, Normal);
        if (MathF.Abs(denom) < 1e-6f)
            return null;

        var d = Vector3.Dot(Point - ray.Origin, Normal) / denom;
        if (d < 1e-4f)
            return null;

        var hitPoint = ray.Origin + ray.Direction * d;

        // Rectangle bounds check with precomputed edges (early-out per axis).
        var v = hitPoint - Location.l1;
        float u = Vector3.Dot(v, _edge1) / _edge1LenSq;
        if (u < 0f || u > 1f) return null;
        float w = Vector3.Dot(v, _edge2) / _edge2LenSq;
        if (w < 0f || w > 1f) return null;

        // Material lookup ONLY for confirmed rectangle hits.
        var faceNormal = denom < 0 ? Normal : -Normal;
        return HitInfo.FromMaterial(d, hitPoint, faceNormal, new Vector2(u, w),
            material.GetSpectralReflectance((int)ray.Wavelength), material, (int)ray.Wavelength);
    }

    private static AABB ComputeBounds((Vector3 l1, Vector3 l2, Vector3 l3) loc)
    {
        var l4 = loc.l2 + loc.l3 - loc.l1;
        return new AABB(
            Vector3.Min(Vector3.Min(loc.l1, loc.l2), Vector3.Min(loc.l3, l4)),
            Vector3.Max(Vector3.Max(loc.l1, loc.l2), Vector3.Max(loc.l3, l4)));
    }
}
