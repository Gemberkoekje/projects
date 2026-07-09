using System.Numerics;

namespace RayTracer;

/// <summary>
/// A rectangle with a running-bond brick pattern. Uses UV coordinates to
/// determine whether each point is brick or mortar, and applies normal-based
/// diffuse attenuation to simulate depth (mortar appears recessed).
/// </summary>
public class BrickRectangle : Plane, Tracable, IQuadPrimitive
{
    private readonly MaterialData _brickMaterial;
    private readonly MaterialData _mortarMaterial;
    private readonly Vector3 _l1;
    private readonly Vector3 _edge1;
    private readonly Vector3 _edge2;
    private readonly float _edge1LenSq;
    private readonly float _edge2LenSq;
    private readonly AABB _bounds;
    private readonly float _bricksAcross;
    private readonly float _bricksDown;
    private readonly float _mortarFractionU;
    private readonly float _mortarFractionV;

    public BrickRectangle(
        (Vector3 l1, Vector3 l2, Vector3 l3) location,
        MaterialData brickMaterial,
        MaterialData mortarMaterial,
        float bricksAcross = 2f,
        float bricksDown = 5f,
        float mortarFractionU = 0.05f,
        float mortarFractionV = 0.06f)
        : base(location.l1,
               Vector3.Cross(location.l2 - location.l1, location.l3 - location.l1),
               brickMaterial)
    {
        _l1 = location.l1;
        _brickMaterial = brickMaterial;
        _mortarMaterial = mortarMaterial;
        _bricksAcross = bricksAcross;
        _bricksDown = bricksDown;
        _mortarFractionU = mortarFractionU;
        _mortarFractionV = mortarFractionV;
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
    public QuadPattern Pattern => QuadPattern.Brick;
    /// <inheritdoc/>
    public MaterialData PrimaryMaterial => _brickMaterial;
    /// <inheritdoc/>
    public MaterialData? SecondaryMaterial => _mortarMaterial;
    /// <inheritdoc/>
    public float PatternP0 => _bricksAcross;
    /// <inheritdoc/>
    public float PatternP1 => _bricksDown;
    /// <inheritdoc/>
    public float PatternP2 => _mortarFractionU;
    /// <inheritdoc/>
    public float PatternP3 => _mortarFractionV;

    public override (float t, Vector3 location, Vector3 normal, Vector2? UV, float reflectance, float roughness)? Intersect(Ray ray)
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

        // Determine brick vs mortar using running-bond pattern
        bool isMortar = IsMortar(u, w);
        var mat = isMortar ? _mortarMaterial : _brickMaterial;

        // Normal-based diffuse attenuation: straight-on → 1.0, grazing → 0.4
        float cosTheta = MathF.Abs(denom);
        float attenuation = 0.4f + 0.6f * cosTheta;

        // Mortar is recessed — darken it further
        if (isMortar)
            attenuation *= 0.6f;

        float reflectance = mat.GetSpectralReflectance((int)ray.Wavelength) * attenuation;
        var faceNormal = denom < 0 ? Normal : -Normal;
        return (d, hitPoint, faceNormal, new Vector2(u, w), reflectance, mat.Roughness);
    }

    private bool IsMortar(float u, float w)
    {
        float brickU = u * _bricksAcross;
        float brickV = w * _bricksDown;
        int row = (int)MathF.Floor(brickV);

        // Offset every other row by half a brick width (running bond)
        float offsetU = brickU + (row % 2 == 0 ? 0f : 0.5f);

        // Position within the current brick cell (0–1)
        float cellU = offsetU - MathF.Floor(offsetU);
        float cellV = brickV - MathF.Floor(brickV);

        return cellU < _mortarFractionU || cellU > (1f - _mortarFractionU)
            || cellV < _mortarFractionV || cellV > (1f - _mortarFractionV);
    }

    private static AABB ComputeBounds((Vector3 l1, Vector3 l2, Vector3 l3) loc)
    {
        var l4 = loc.l2 + loc.l3 - loc.l1;
        return new AABB(
            Vector3.Min(Vector3.Min(loc.l1, loc.l2), Vector3.Min(loc.l3, l4)),
            Vector3.Max(Vector3.Max(loc.l1, loc.l2), Vector3.Max(loc.l3, l4)));
    }
}
