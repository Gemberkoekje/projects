namespace Pinball.Physics;

/// <summary>
/// A double-precision axis-aligned bounding box for physics broadphase (pinball-plan §6.1.2). Colliders
/// expose one so a swept-sphere query can cull candidates; slice-1 uses a linear scan, but the box is the
/// same conservative primitive the float BVH broadphase (<c>BVH.OverlapSweep</c>) will use later.
/// </summary>
public readonly struct AabbD
{
    /// <summary>Minimum corner.</summary>
    public readonly Vector3D Min;
    /// <summary>Maximum corner.</summary>
    public readonly Vector3D Max;

    /// <summary>Constructs a box from its corners (assumes <paramref name="min"/> ≤ <paramref name="max"/>).</summary>
    public AabbD(Vector3D min, Vector3D max) { Min = min; Max = max; }

    /// <summary>An unbounded box (e.g. an infinite plane) — always a broadphase candidate.</summary>
    public static AabbD Infinite => new(
        new Vector3D(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity),
        new Vector3D(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity));

    /// <summary>Grows the box by <paramref name="margin"/> on every side.</summary>
    public AabbD Expanded(double margin) => new(
        new Vector3D(Min.X - margin, Min.Y - margin, Min.Z - margin),
        new Vector3D(Max.X + margin, Max.Y + margin, Max.Z + margin));

    /// <summary>True if this box overlaps <paramref name="other"/> (slab test).</summary>
    public bool Intersects(AabbD other) =>
        Min.X <= other.Max.X && Max.X >= other.Min.X &&
        Min.Y <= other.Max.Y && Max.Y >= other.Min.Y &&
        Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;

    /// <summary>The AABB of a sphere swept from <paramref name="a"/> to <paramref name="b"/>, inflated by <paramref name="radius"/>.</summary>
    public static AabbD SweptSphere(Vector3D a, Vector3D b, double radius) =>
        new(Vector3D.Min(a, b) - new Vector3D(radius, radius, radius),
            Vector3D.Max(a, b) + new Vector3D(radius, radius, radius));
}
