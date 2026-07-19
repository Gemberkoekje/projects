using System.Collections.Generic;
using System.Numerics;

namespace RayTracer;

/// <summary>
/// A <see cref="Phase2Reference.ISceneTracer"/> backed by the CPU
/// <see cref="BVH"/>, mapping each hit <see cref="Tracable"/> back to its packer
/// quad index. This lets the Phase 2 shading replica run over the exact same
/// geometry and traversal the CPU renderer uses, so parity tests (and the GPU
/// headless self-test) isolate the shading arithmetic from any ray-cast
/// differences. On the GPU the equivalent hits come from hardware RayQuery.
/// </summary>
public sealed class BvhSceneTracer : Phase2Reference.ISceneTracer
{
    private readonly BVH _bvh;
    private readonly Dictionary<Tracable, int> _quadIndex;
    private readonly int _wavelength;

    /// <param name="scene">The same scene the packer flattened.</param>
    /// <param name="wavelength">Any valid wavelength; only used to satisfy
    /// <see cref="Ray"/> — geometry and occlusion are wavelength-independent and
    /// the reflectance the BVH computes is discarded (the replica reconstructs it).</param>
    public BvhSceneTracer(Tracable[] scene, int wavelength)
    {
        System.ArgumentNullException.ThrowIfNull(scene);

        _bvh = new BVH(scene);
        // Map each quad to its PACKED row, not its scene-order position: §4.2 moves Dynamic quads to the
        // tail of Primitives, so a hit quad's Primitives[] row is its index in GpuScenePacker.OrderQuads,
        // the single source of truth the packer also uses. Scene order would desync for a mid-scene mover.
        _quadIndex = new Dictionary<Tracable, int>(ReferenceEqualityComparer.Instance);
        List<IQuadPrimitive> quads = GpuScenePacker.OrderQuads(scene, out _);
        for (int qi = 0; qi < quads.Count; qi++)
            _quadIndex[(Tracable)quads[qi]] = qi;
        _wavelength = wavelength;
    }

    /// <inheritdoc/>
    public bool ClosestHit(Vector3 origin, Vector3 dir, out int quadIndex, out Vector3 hitPoint)
    {
        var ray = new Ray { Origin = origin, Direction = dir, Wavelength = _wavelength, Intensity = 1f };
        var hit = _bvh.FindClosest(ray);
        Tracable? hitPrimitive = hit.hitPrimitive;
        if (hit.hit && hitPrimitive is not null && _quadIndex.TryGetValue(hitPrimitive, out quadIndex))
        {
            hitPoint = hit.hitPoint;
            return true;
        }

        quadIndex = 0;
        hitPoint = Vector3.Zero;
        return false;
    }

    /// <inheritdoc/>
    public bool Occluded(Vector3 origin, Vector3 dir, float maxDist)
    {
        var ray = new Ray { Origin = origin, Direction = dir, Wavelength = _wavelength, Intensity = 1f };
        return _bvh.IsOccluded(ray, maxDist);
    }
}
