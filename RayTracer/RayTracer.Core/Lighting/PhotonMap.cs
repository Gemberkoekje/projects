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

    /// <summary>
    /// Flattens the dictionary spatial hash into a GPU-uploadable uniform grid (shadows-and-caustics
    /// plan §B4): the photons sorted into cell order, plus per-cell <c>(start, count)</c> ranges into
    /// that array, over a bounded box that exactly covers the occupied cells. The cell size and cell
    /// keys (<c>floor(coord / cellSize)</c>) match this map's own, so a gather over the grid returns
    /// the identical density estimate — that equivalence is the CPU/GPU contract the caustic compute
    /// shader is a line-for-line port of, pinned by <c>CausticReference</c> in CI. Cells outside the
    /// box are empty; a query there sees no photons, exactly as in the sparse hash.
    /// </summary>
    public CausticGrid BuildGrid()
    {
        if (_photons.Count == 0)
            return CausticGrid.Empty(_cellSize);

        // Bounding cell range over all photons (absolute floor(coord/cellSize), like CellOf).
        (int cx0, int cy0, int cz0) = CellOf(_photons[0].Position);
        int minX = cx0, minY = cy0, minZ = cz0, maxX = cx0, maxY = cy0, maxZ = cz0;
        for (int i = 1; i < _photons.Count; i++)
        {
            (int cx, int cy, int cz) = CellOf(_photons[i].Position);
            if (cx < minX) minX = cx; else if (cx > maxX) maxX = cx;
            if (cy < minY) minY = cy; else if (cy > maxY) maxY = cy;
            if (cz < minZ) minZ = cz; else if (cz > maxZ) maxZ = cz;
        }

        int dimX = maxX - minX + 1;
        int dimY = maxY - minY + 1;
        int dimZ = maxZ - minZ + 1;
        int cellCount = dimX * dimY * dimZ;

        int Linear(int cx, int cy, int cz) => (cx - minX) + dimX * ((cy - minY) + dimY * (cz - minZ));

        // Counting sort into cell order: first tally per-cell counts, then prefix-sum to starts.
        var counts = new int[cellCount];
        for (int i = 0; i < _photons.Count; i++)
        {
            (int cx, int cy, int cz) = CellOf(_photons[i].Position);
            counts[Linear(cx, cy, cz)]++;
        }

        var starts = new int[cellCount];
        int running = 0;
        for (int c = 0; c < cellCount; c++)
        {
            starts[c] = running;
            running += counts[c];
        }

        var sorted = new Photon[_photons.Count];
        var cursor = (int[])starts.Clone();
        for (int i = 0; i < _photons.Count; i++)
        {
            Photon ph = _photons[i];
            (int cx, int cy, int cz) = CellOf(ph.Position);
            sorted[cursor[Linear(cx, cy, cz)]++] = ph;
        }

        return new CausticGrid(sorted, starts, counts, minX, minY, minZ, dimX, dimY, dimZ, _cellSize);
    }
}
