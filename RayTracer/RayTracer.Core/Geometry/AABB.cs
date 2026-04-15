using System.Numerics;
using System.Runtime.CompilerServices;

namespace RayTracer;

/// <summary>
/// Axis-aligned bounding box used by the BVH for fast ray culling.
/// </summary>
public readonly struct AABB
{
    public readonly Vector3 Min;
    public readonly Vector3 Max;

    public AABB(Vector3 min, Vector3 max)
    {
        Min = min;
        Max = max;
    }

    public Vector3 Center => (Min + Max) * 0.5f;

    public static AABB Union(AABB a, AABB b) => new(
        Vector3.Min(a.Min, b.Min),
        Vector3.Max(a.Max, b.Max));

    /// <summary>
    /// Slab-method ray–AABB intersection test (SIMD-friendly).
    /// Returns true if the ray hits the box at a t value in [0, tMax).
    /// <paramref name="invDir"/> must be <c>1 / ray.Direction</c>,
    /// pre-computed for the whole traversal.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Intersects(Vector3 origin, Vector3 invDir, float tMax)
    {
        Vector3 t1 = (Min - origin) * invDir;
        Vector3 t2 = (Max - origin) * invDir;
        Vector3 tSmall = Vector3.Min(t1, t2);
        Vector3 tBig = Vector3.Max(t1, t2);

        float tMin = MathF.Max(MathF.Max(tSmall.X, tSmall.Y), tSmall.Z);
        float tMaxVal = MathF.Min(MathF.Min(tBig.X, tBig.Y), tBig.Z);

        return tMaxVal >= MathF.Max(tMin, 0f) && tMin < tMax;
    }
}
