using System.Collections.Generic;
using System.Numerics;

namespace RayTracer;

/// <summary>
/// A caustic photon map (shadows-and-caustics-plan §B): stores <see cref="Photon"/>s deposited by
/// the forward <see cref="PhotonTracer"/> and answers density queries at shade time. Photons are
/// bucketed into a uniform spatial hash sized to the gather radius, so a query only scans the
/// handful of cells that can overlap its search sphere rather than the whole map.
/// </summary>
public sealed class PhotonMap
{
    private readonly List<Photon> _photons = [];
    private readonly Dictionary<(int, int, int), List<int>> _grid = [];
    private readonly float _cellSize;

    /// <param name="gatherRadius">Nominal density-estimate radius; also the spatial-hash cell size.
    /// Larger values smooth the caustic (less noise, more blur).</param>
    public PhotonMap(float gatherRadius)
    {
        _cellSize = MathF.Max(gatherRadius, 1e-3f);
    }

    /// <summary>Number of photons stored.</summary>
    public int Count => _photons.Count;

    /// <summary>The stored photons, for diagnostics and tests (e.g. measuring caustic spread).</summary>
    internal IReadOnlyList<Photon> Photons => _photons;

    /// <summary>Total power stored across all photons (used to check energy conservation).</summary>
    public float TotalPower
    {
        get
        {
            float sum = 0f;
            for (int i = 0; i < _photons.Count; i++)
                sum += _photons[i].Power;
            return sum;
        }
    }

    /// <summary>Adds a photon and indexes it in the spatial hash.</summary>
    public void Store(Photon photon)
    {
        int index = _photons.Count;
        _photons.Add(photon);
        (int, int, int) cell = CellOf(photon.Position);
        if (!_grid.TryGetValue(cell, out List<int>? bucket))
        {
            bucket = [];
            _grid[cell] = bucket;
        }

        bucket.Add(index);
    }

    /// <summary>
    /// Caustic XYZ irradiance at <paramref name="position"/> on a surface with the given
    /// <paramref name="normal"/>: a constant-kernel density estimate that gathers photons within
    /// <paramref name="radius"/> whose surface normal aligns with the receiver, and returns
    /// <c>Σ power·XYZ(λ) / (π r²)</c>. The per-photon spectral colour comes from
    /// <paramref name="wavelengths"/>, so a dispersed prism caustic renders its rainbow naturally.
    /// Multiply the result by the receiver's albedo/π to turn it into outgoing radiance.
    /// </summary>
    public Vector3 EstimateXyz(Vector3 position, Vector3 normal, float radius, WavelengthLookup wavelengths)
    {
        System.ArgumentNullException.ThrowIfNull(wavelengths);
        if (_photons.Count == 0 || radius <= 0f)
            return Vector3.Zero;

        float radiusSq = radius * radius;
        int span = Math.Max(1, (int)MathF.Ceiling(radius / _cellSize));
        int cx = (int)MathF.Floor(position.X / _cellSize);
        int cy = (int)MathF.Floor(position.Y / _cellSize);
        int cz = (int)MathF.Floor(position.Z / _cellSize);

        Vector3 sum = Vector3.Zero;
        for (int dx = -span; dx <= span; dx++)
        {
            for (int dy = -span; dy <= span; dy++)
            {
                for (int dz = -span; dz <= span; dz++)
                {
                    if (!_grid.TryGetValue((cx + dx, cy + dy, cz + dz), out List<int>? bucket))
                        continue;

                    for (int b = 0; b < bucket.Count; b++)
                    {
                        Photon ph = _photons[bucket[b]];
                        if (Vector3.Dot(ph.Normal, normal) < 0.5f)
                            continue;
                        if (Vector3.DistanceSquared(ph.Position, position) > radiusSq)
                            continue;

                        if (wavelengths.TryGet(ph.Wavelength, out Vector3 xyz))
                            sum += xyz * ph.Power;
                    }
                }
            }
        }

        return sum / (MathF.PI * radiusSq);
    }

    /// <summary>Number of photons within <paramref name="radius"/> of <paramref name="position"/>
    /// (ignoring orientation) — a diagnostic used to verify caustic focusing.</summary>
    public int CountNear(Vector3 position, float radius)
    {
        if (_photons.Count == 0 || radius <= 0f)
            return 0;

        float radiusSq = radius * radius;
        int span = Math.Max(1, (int)MathF.Ceiling(radius / _cellSize));
        int cx = (int)MathF.Floor(position.X / _cellSize);
        int cy = (int)MathF.Floor(position.Y / _cellSize);
        int cz = (int)MathF.Floor(position.Z / _cellSize);

        int count = 0;
        for (int dx = -span; dx <= span; dx++)
        {
            for (int dy = -span; dy <= span; dy++)
            {
                for (int dz = -span; dz <= span; dz++)
                {
                    if (!_grid.TryGetValue((cx + dx, cy + dy, cz + dz), out List<int>? bucket))
                        continue;

                    for (int b = 0; b < bucket.Count; b++)
                    {
                        if (Vector3.DistanceSquared(_photons[bucket[b]].Position, position) <= radiusSq)
                            count++;
                    }
                }
            }
        }

        return count;
    }

    private (int, int, int) CellOf(Vector3 p) => (
        (int)MathF.Floor(p.X / _cellSize),
        (int)MathF.Floor(p.Y / _cellSize),
        (int)MathF.Floor(p.Z / _cellSize));
}
