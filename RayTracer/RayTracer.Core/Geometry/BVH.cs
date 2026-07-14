using System.Numerics;
using System.Runtime.InteropServices;

namespace RayTracer;

/// <summary>
/// A bounding-volume hierarchy that accelerates closest-hit ray queries
/// from O(n) to O(log n). Uses a flat struct array for cache-friendly
/// iterative traversal. Built once from a <see cref="Tracable"/> array
/// using top-down midpoint splitting on the longest axis.
/// </summary>
public sealed class BVH
{
    private const int LeafSize = 4;
    private const int MaxStackDepth = 64;

    [StructLayout(LayoutKind.Sequential)]
    private struct FlatNode
    {
        public AABB Bounds;
        public int Offset;  // internal: right child index; leaf: _primitives start
        public int Count;   // 0 = internal node; >0 = leaf primitive count
    }

    private readonly FlatNode[] _nodes;
    private readonly Tracable[] _primitives;

    public BVH(Tracable[] scene)
    {
        var copy = (Tracable[])scene.Clone();
        var nodes = new List<FlatNode>();
        var orderedPrims = new List<Tracable>();
        Build(nodes, copy, orderedPrims, 0, copy.Length);
        _nodes = nodes.ToArray();
        _primitives = orderedPrims.ToArray();
    }

    /// <summary>
    /// Finds the closest intersection in the BVH.
    /// Returns the spectral reflectance, hit point, surface normal,
    /// surface roughness, whether any hit occurred, the hit primitive
    /// (for companion wavelength evaluation), the surface kind
    /// (so the integrator can branch into wavelength-dependent effects),
    /// and the index of refraction resolved at the ray's wavelength
    /// (for dielectric refraction).
    /// </summary>
    public (float reflectance, Vector3 hitPoint, Vector3 hitNormal, float roughness, bool hit, Tracable? hitPrimitive, SurfaceKind surface, float ior, float extinction) FindClosest(Ray ray)
    {
        float closestT = float.MaxValue;
        float closestReflectance = 0f;
        float closestRoughness = 0f;
        Vector3 closestPoint = Vector3.Zero;
        Vector3 closestNormal = Vector3.Zero;
        SurfaceKind closestSurface = SurfaceKind.Diffuse;
        float closestIor = 1f;
        float closestExtinction = 0f;
        bool anyHit = false;
        Tracable? closestPrimitive = null;
        Vector3 origin = ray.Origin;
        Vector3 invDir = new(1f / ray.Direction.X, 1f / ray.Direction.Y, 1f / ray.Direction.Z);

        Span<int> stack = stackalloc int[MaxStackDepth];
        int stackPtr = 0;
        int current = 0;

        while (true)
        {
            ref FlatNode node = ref _nodes[current];

            if (node.Bounds.Intersects(origin, invDir, closestT))
            {
                if (node.Count > 0)
                {
                    // Leaf node — test primitives directly using parametric t.
                    int end = node.Offset + node.Count;
                    for (int i = node.Offset; i < end; i++)
                    {
                        var intersect = _primitives[i].Intersect(ray);
                        if (intersect.HasValue && intersect.Value.T < closestT)
                        {
                            var h = intersect.Value;
                            closestT = h.T;
                            closestReflectance = h.Reflectance;
                            closestRoughness = h.Roughness;
                            closestPoint = h.Location;
                            closestNormal = h.Normal;
                            closestSurface = h.Surface;
                            closestIor = h.Ior;
                            closestExtinction = h.Extinction;
                            closestPrimitive = _primitives[i];
                            anyHit = true;
                        }
                    }
                }
                else
                {
                    // Internal node — push right child, visit left (index + 1).
                    stack[stackPtr++] = node.Offset;
                    current++;
                    continue;
                }
            }

            if (stackPtr == 0) break;
            current = stack[--stackPtr];
        }

        return (closestReflectance, closestPoint, closestNormal, closestRoughness, anyHit, closestPrimitive, closestSurface, closestIor, closestExtinction);
    }

    /// <summary>
    /// Returns true if any geometry occludes the ray before
    /// <paramref name="maxDist"/>. Used for shadow rays in NEE.
    /// </summary>
    public bool IsOccluded(Ray ray, float maxDist)
    {
        Vector3 origin = ray.Origin;
        Vector3 invDir = new(1f / ray.Direction.X, 1f / ray.Direction.Y, 1f / ray.Direction.Z);

        Span<int> stack = stackalloc int[MaxStackDepth];
        int stackPtr = 0;
        int current = 0;

        while (true)
        {
            ref FlatNode node = ref _nodes[current];

            if (node.Bounds.Intersects(origin, invDir, maxDist))
            {
                if (node.Count > 0)
                {
                    int end = node.Offset + node.Count;
                    for (int i = node.Offset; i < end; i++)
                    {
                        var intersect = _primitives[i].Intersect(ray);
                        if (intersect.HasValue && intersect.Value.T < maxDist)
                            return true;
                    }
                }
                else
                {
                    stack[stackPtr++] = node.Offset;
                    current++;
                    continue;
                }
            }

            if (stackPtr == 0) break;
            current = stack[--stackPtr];
        }

        return false;
    }

    /// <summary>
    /// Fraction of light in [0,1] that survives a shadow ray up to
    /// <paramref name="maxDist"/>. Unlike the binary <see cref="IsOccluded"/>, thin
    /// dielectrics do not fully block the ray: it returns 0 as soon as an opaque
    /// primitive is hit, otherwise the product of the transmitted fractions through
    /// each bubble/glass it passes — a bubble keeps <c>1 − reflectProbability</c>
    /// (thin-film-tinted, via <see cref="Optics.BubbleReflectProbability"/>), a
    /// dielectric keeps <c>(1 − Fresnel)·exp(−σ(λ)·d)</c> so coloured (stained) glass
    /// tints the shadow toward its transmitted hue (Beer–Lambert, §2.3, over
    /// <see cref="Optics.GlassShadowInterfaceThickness"/> per interface) while clear
    /// glass keeps just the Fresnel dim. This lets bubbles and glass cast soft,
    /// tinted shadows instead of hard black discs (shadows-and-caustics-plan §A).
    /// The result is order-independent because an opaque hit short-circuits to 0.
    /// Refractive bending (caustics) is out of scope here — that is forward
    /// transport (plan §B); this only softens the occlusion term.
    /// </summary>
    /// <remarks>
    /// For a scene of only opaque primitives this returns exactly 0 (blocked) or 1
    /// (clear), matching <c>!IsOccluded</c> bit-for-bit, so existing renders are
    /// unchanged where no transmissive geometry lies on the shadow ray.
    /// </remarks>
    public float Transmittance(Ray ray, float maxDist)
    {
        Vector3 origin = ray.Origin;
        Vector3 invDir = new(1f / ray.Direction.X, 1f / ray.Direction.Y, 1f / ray.Direction.Z);
        Vector3 dir = Vector3.Normalize(ray.Direction);

        float transmittance = 1f;

        Span<int> stack = stackalloc int[MaxStackDepth];
        int stackPtr = 0;
        int current = 0;

        while (true)
        {
            ref FlatNode node = ref _nodes[current];

            if (node.Bounds.Intersects(origin, invDir, maxDist))
            {
                if (node.Count > 0)
                {
                    int end = node.Offset + node.Count;
                    for (int i = node.Offset; i < end; i++)
                    {
                        var intersect = _primitives[i].Intersect(ray);
                        if (!intersect.HasValue || intersect.Value.T >= maxDist)
                            continue;

                        var h = intersect.Value;
                        float cos = MathF.Abs(Vector3.Dot(dir, h.Normal));
                        switch (h.Surface)
                        {
                            case SurfaceKind.Bubble:
                                transmittance *= 1f - Optics.BubbleReflectProbability(cos, h.Ior, h.Reflectance);
                                break;
                            case SurfaceKind.Dielectric:
                                // 1 − Fresnel (the reflected loss) × Beer–Lambert absorption through
                                // the glass, so coloured (stained) glass casts a shadow tinted toward
                                // its transmitted hue; clear glass (σ = 0) keeps just the Fresnel dim.
                                transmittance *= (1f - Optics.FresnelDielectric(cos, 1f, h.Ior))
                                    * MathF.Exp(-h.Extinction * Optics.GlassShadowInterfaceThickness);
                                break;
                            default:
                                // Opaque (diffuse, mirror, oil-slick thin-film, emissive, …) → hard shadow.
                                return 0f;
                        }

                        if (transmittance <= 1e-4f)
                            return 0f;
                    }
                }
                else
                {
                    stack[stackPtr++] = node.Offset;
                    current++;
                    continue;
                }
            }

            if (stackPtr == 0) break;
            current = stack[--stackPtr];
        }

        return transmittance;
    }

    // ── Build ──────────────────────────────────────────────────────

    private static void Build(List<FlatNode> nodes, Tracable[] scene, List<Tracable> orderedPrims, int start, int count)
    {
        int nodeIndex = nodes.Count;
        nodes.Add(default); // placeholder

        AABB bounds = scene[start].Bounds;
        for (int i = 1; i < count; i++)
            bounds = AABB.Union(bounds, scene[start + i].Bounds);

        if (count <= LeafSize)
        {
            int primStart = orderedPrims.Count;
            for (int i = 0; i < count; i++)
                orderedPrims.Add(scene[start + i]);
            nodes[nodeIndex] = new FlatNode
            {
                Bounds = bounds,
                Offset = primStart,
                Count = count
            };
            return;
        }

        // Split on the longest axis at the median.
        Vector3 extent = bounds.Max - bounds.Min;
        int axis;
        if (extent.X >= extent.Y && extent.X >= extent.Z) axis = 0;
        else if (extent.Y >= extent.Z) axis = 1;
        else axis = 2;

        Array.Sort(scene, start, count, Comparer<Tracable>.Create((a, b) =>
        {
            float ca = GetAxis(a.Bounds.Center, axis);
            float cb = GetAxis(b.Bounds.Center, axis);
            return ca.CompareTo(cb);
        }));

        int mid = count / 2;
        Build(nodes, scene, orderedPrims, start, mid);
        int rightChild = nodes.Count;
        Build(nodes, scene, orderedPrims, start + mid, count - mid);

        nodes[nodeIndex] = new FlatNode
        {
            Bounds = bounds,
            Offset = rightChild,
            Count = 0
        };
    }

    private static float GetAxis(Vector3 v, int axis) => axis switch
    {
        0 => v.X,
        1 => v.Y,
        _ => v.Z,
    };
}
