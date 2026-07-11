using System.Numerics;

namespace RayTracer;

/// <summary>
/// A textured decal quad (plan §3): a wall-mounted plaque, sign, or logo whose surface
/// colour comes from a layer of the decal atlas rather than a spectral material. On the
/// GPU it is <see cref="QuadPattern.Decal"/> and the atlas layer index rides in
/// <see cref="PatternP0"/>; the shader samples the atlas texel and turns it into a
/// per-wavelength reflectance via the RGB→reflectance basis, so the decal shades through
/// the same spectral pipeline as the maze. The supplied <paramref name="material"/> only
/// satisfies the packer's material table — the decal shading path never reads it.
/// </summary>
public sealed class DecalRectangle : Plane, Tracable, IQuadPrimitive
{
    private readonly (Vector3 l1, Vector3 l2, Vector3 l3) _loc;
    private readonly MaterialData _material;
    private readonly int _layer;
    private readonly Vector3 _edge1;
    private readonly Vector3 _edge2;
    private readonly float _edge1LenSq;
    private readonly float _edge2LenSq;
    private readonly AABB _bounds;

    /// <param name="location">Quad corners (l1 = origin, l2 = +U corner, l3 = +V corner).</param>
    /// <param name="material">Placeholder material (unused by decal shading; only kept so
    /// the packer's material table and the CPU intersection have something to return).</param>
    /// <param name="layer">Atlas layer index for this decal image.</param>
    public DecalRectangle((Vector3 l1, Vector3 l2, Vector3 l3) location, MaterialData material, int layer)
        : base(location.l1,
               Vector3.Cross(location.l2 - location.l1, location.l3 - location.l1),
               material)
    {
        _loc = location;
        _material = material;
        _layer = layer;
        _edge1 = location.l2 - location.l1;
        _edge2 = location.l3 - location.l1;
        _edge1LenSq = Vector3.Dot(_edge1, _edge1);
        _edge2LenSq = Vector3.Dot(_edge2, _edge2);
        _bounds = ComputeBounds(location);
    }

    public override AABB Bounds => _bounds;

    /// <summary>Atlas layer index for this decal.</summary>
    public int Layer => _layer;

    /// <inheritdoc/>
    public Vector3 L1 => _loc.l1;
    /// <inheritdoc/>
    public Vector3 Edge1 => _edge1;
    /// <inheritdoc/>
    public Vector3 Edge2 => _edge2;
    /// <inheritdoc/>
    public Vector3 QuadNormal => Normal;
    /// <inheritdoc/>
    public QuadPattern Pattern => QuadPattern.Decal;
    /// <inheritdoc/>
    public MaterialData PrimaryMaterial => _material;
    /// <inheritdoc/>
    public MaterialData? SecondaryMaterial => null;
    /// <inheritdoc/>
    public float PatternP0 => _layer;
    /// <inheritdoc/>
    public float PatternP1 => 0f;
    /// <inheritdoc/>
    public float PatternP2 => 0f;
    /// <inheritdoc/>
    public float PatternP3 => 0f;

    /// <inheritdoc/>
    public override HitInfo? Intersect(Ray ray)
    {
        var denom = Vector3.Dot(ray.Direction, Normal);
        if (MathF.Abs(denom) < 1e-6f)
            return null;

        var d = Vector3.Dot(Point - ray.Origin, Normal) / denom;
        if (d < 1e-4f)
            return null;

        var hitPoint = ray.Origin + ray.Direction * d;
        var v = hitPoint - _loc.l1;
        float u = Vector3.Dot(v, _edge1) / _edge1LenSq;
        if (u < 0f || u > 1f) return null;
        float w = Vector3.Dot(v, _edge2) / _edge2LenSq;
        if (w < 0f || w > 1f) return null;

        var faceNormal = denom < 0 ? Normal : -Normal;
        return HitInfo.FromMaterial(d, hitPoint, faceNormal, new Vector2(u, w),
            _material.GetSpectralReflectance((int)ray.Wavelength), _material, (int)ray.Wavelength, ray.Direction);
    }

    private static AABB ComputeBounds((Vector3 l1, Vector3 l2, Vector3 l3) loc)
    {
        var l4 = loc.l2 + loc.l3 - loc.l1;
        return new AABB(
            Vector3.Min(Vector3.Min(loc.l1, loc.l2), Vector3.Min(loc.l3, l4)),
            Vector3.Max(Vector3.Max(loc.l1, loc.l2), Vector3.Max(loc.l3, l4)));
    }
}
