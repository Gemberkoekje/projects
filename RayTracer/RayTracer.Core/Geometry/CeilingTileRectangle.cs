using System.Numerics;

namespace RayTracer;

/// <summary>
/// A rectangle with a regular grid pattern for ceiling tiles. Uses UV
/// coordinates to determine tile position and applies a smooth bevel
/// at tile edges via normal-based diffuse attenuation, creating the
/// appearance of raised tile borders.
/// </summary>
public class CeilingTileRectangle : Plane, Tracable, IQuadPrimitive
{
    private readonly MaterialData _tileMaterial;
    private readonly Vector3 _l1;
    private readonly Vector3 _edge1;
    private readonly Vector3 _edge2;
    private readonly float _edge1LenSq;
    private readonly float _edge2LenSq;
    private readonly AABB _bounds;
    private readonly float _tilesAcross;
    private readonly float _tilesDown;
    private readonly float _bevelFraction;

    public CeilingTileRectangle(
        (Vector3 l1, Vector3 l2, Vector3 l3) location,
        MaterialData tileMaterial,
        MaterialData gridMaterial,
        float tilesAcross = 4f,
        float tilesDown = 4f,
        float bevelFraction = 0.08f)
        : base(location.l1,
               Vector3.Cross(location.l2 - location.l1, location.l3 - location.l1),
               tileMaterial)
    {
        _l1 = location.l1;
        _tileMaterial = tileMaterial;
        _tilesAcross = tilesAcross;
        _tilesDown = tilesDown;
        _bevelFraction = bevelFraction;
        _edge1 = location.l2 - location.l1;
        _edge2 = location.l3 - location.l1;
        _edge1LenSq = Vector3.Dot(_edge1, _edge1);
        _edge2LenSq = Vector3.Dot(_edge2, _edge2);
        _bounds = ComputeBounds(location);
    }

    public override AABB Bounds => _bounds;

    /// <inheritdoc/>
    public Vector3 L1 => _l1;
    /// <inheritdoc/>
    public Vector3 Edge1 => _edge1;
    /// <inheritdoc/>
    public Vector3 Edge2 => _edge2;
    /// <inheritdoc/>
    public Vector3 QuadNormal => Normal;
    /// <inheritdoc/>
    public QuadPattern Pattern => QuadPattern.CeilingTile;
    /// <inheritdoc/>
    public MaterialData PrimaryMaterial => _tileMaterial;
    /// <summary>Ceiling tiles use a single material; the "grid" is the bevel
    /// darkening the tile material, so there is no secondary material.</summary>
    public MaterialData? SecondaryMaterial => null;
    /// <inheritdoc/>
    public float PatternP0 => _tilesAcross;
    /// <inheritdoc/>
    public float PatternP1 => _tilesDown;
    /// <inheritdoc/>
    public float PatternP2 => _bevelFraction;
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

        var v = hitPoint - _l1;
        float u = Vector3.Dot(v, _edge1) / _edge1LenSq;
        if (u < 0f || u > 1f) return null;
        float w = Vector3.Dot(v, _edge2) / _edge2LenSq;
        if (w < 0f || w > 1f) return null;

        // Normal-based diffuse attenuation: straight-on → 1.0, grazing → 0.4
        float cosTheta = MathF.Abs(denom);
        float attenuation = 0.4f + 0.6f * cosTheta;

        // Smooth bevel at tile edges — darken gradually near the border
        float bevelFactor = BevelFactor(u, w);
        attenuation *= bevelFactor;

        float reflectance = _tileMaterial.GetSpectralReflectance((int)ray.Wavelength) * attenuation;
        var faceNormal = denom < 0 ? Normal : -Normal;
        return HitInfo.FromMaterial(d, hitPoint, faceNormal, new Vector2(u, w), reflectance, _tileMaterial, (int)ray.Wavelength);
    }

    /// <summary>
    /// Returns 1.0 at the tile centre and smoothly drops to ~0.4 at tile
    /// edges, producing a bevelled appearance without a hard grid line.
    /// </summary>
    private float BevelFactor(float u, float w)
    {
        float tileU = u * _tilesAcross;
        float tileV = w * _tilesDown;
        float cellU = tileU - MathF.Floor(tileU);
        float cellV = tileV - MathF.Floor(tileV);

        // Distance from the nearest edge (0 at edge, 0.5 at centre)
        float distU = MathF.Min(cellU, 1f - cellU);
        float distV = MathF.Min(cellV, 1f - cellV);
        float edgeDist = MathF.Min(distU, distV);

        // Smoothstep from edge (0) to bevelFraction (1)
        if (edgeDist >= _bevelFraction)
            return 1f;

        float t = edgeDist / _bevelFraction;
        // Hermite smoothstep: 3t² − 2t³
        float smooth = t * t * (3f - 2f * t);
        // Map from [0.4, 1.0] — deepest bevel is 40% brightness
        return 0.4f + 0.6f * smooth;
    }

    private static AABB ComputeBounds((Vector3 l1, Vector3 l2, Vector3 l3) loc)
    {
        var l4 = loc.l2 + loc.l3 - loc.l1;
        return new AABB(
            Vector3.Min(Vector3.Min(loc.l1, loc.l2), Vector3.Min(loc.l3, l4)),
            Vector3.Max(Vector3.Max(loc.l1, loc.l2), Vector3.Max(loc.l3, l4)));
    }
}
