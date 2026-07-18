using System.Numerics;

namespace RayTracer;

/// <summary>
/// A GPU-uploadable flattening of a <see cref="PhotonMap"/> (shadows-and-caustics-plan §B4): the
/// caustic photons sorted into cell order plus per-cell <c>(start, count)</c> ranges over a bounded
/// uniform grid. Unlike the map's dictionary hash (unbounded, not addressable from a shader), this is
/// three flat arrays + a few scalars that upload straight into structured buffers, and the density
/// gather over it (<see cref="CausticReference.EstimateXyz"/>) is a fixed-loop the compute shader can
/// port line-for-line. Built by <see cref="PhotonMap.BuildGrid"/>.
/// </summary>
/// <param name="Photons">Caustic photons sorted so each cell's photons are contiguous.</param>
/// <param name="CellStart">Per linear-cell index, the first photon offset into <see cref="Photons"/>.</param>
/// <param name="CellCount">Per linear-cell index, how many photons that cell holds.</param>
/// <param name="MinCellX">Grid origin cell (inclusive) on X — cell keys are <c>floor(coord/cellSize)</c>.</param>
/// <param name="MinCellY">Grid origin cell (inclusive) on Y.</param>
/// <param name="MinCellZ">Grid origin cell (inclusive) on Z.</param>
/// <param name="DimX">Cell count along X.</param>
/// <param name="DimY">Cell count along Y.</param>
/// <param name="DimZ">Cell count along Z.</param>
/// <param name="CellSize">World size of a cell edge (equal to the map's gather radius).</param>
public sealed record CausticGrid(
    Photon[] Photons,
    int[] CellStart,
    int[] CellCount,
    int MinCellX, int MinCellY, int MinCellZ,
    int DimX, int DimY, int DimZ,
    float CellSize)
{
    /// <summary>True when no photons were stored — the gather short-circuits to zero.</summary>
    public bool IsEmpty => Photons.Length == 0;

    /// <summary>An empty grid (no photons) sized for the given cell edge.</summary>
    public static CausticGrid Empty(float cellSize)
        => new([], [], [], 0, 0, 0, 0, 0, 0, MathF.Max(cellSize, 1e-3f));
}

/// <summary>
/// Pure-C# replica of the caustic density gather the GPU compute/gather shader runs
/// (shadows-and-caustics-plan §B4). <see cref="EstimateXyz"/> is the contract the HLSL is a
/// line-for-line port of, and the unit tests pin it to <see cref="PhotonMap.EstimateXyz"/> (the tested
/// CPU renderer path) over the same photons, so CPU/GPU caustic parity is testable in plain C# where
/// the GPU cannot run — the same discipline as the other <c>*Reference</c> replicas.
/// </summary>
public static class CausticReference
{
    /// <summary>
    /// Caustic XYZ irradiance at <paramref name="position"/> on a surface facing
    /// <paramref name="normal"/>, gathered over the flattened <paramref name="grid"/>: the constant
    /// kernel <c>Σ power·XYZ(λ) / (π r²)</c> over photons within <paramref name="radius"/> whose
    /// stored normal aligns with the receiver. Scans exactly the cells the map's own estimate does
    /// (<c>±ceil(radius/cellSize)</c> around <c>floor(coord/cellSize)</c>, clamped to the grid), so it
    /// returns the identical value to <see cref="PhotonMap.EstimateXyz"/>.
    /// §8 SpectralAlbedo: <paramref name="spectralAlbedo"/> mirrors <see cref="PhotonMap.EstimateXyz"/> —
    /// when non-null each photon is weighted by the receiver reflectance at its own wavelength (nm) so
    /// the tint is per-bundle; <c>null</c> reproduces the achromatic gather. The HLSL
    /// <c>TraceCausticEstimate</c> ports this via <c>HeroReflectance(prim, dir, pos, ph.WlIndex)</c>.
    /// </summary>
    public static Vector3 EstimateXyz(CausticGrid grid, Vector3 position, Vector3 normal, float radius, WavelengthLookup wavelengths,
        System.Func<int, float>? spectralAlbedo = null)
    {
        System.ArgumentNullException.ThrowIfNull(grid);
        System.ArgumentNullException.ThrowIfNull(wavelengths);
        if (grid.IsEmpty || radius <= 0f)
            return Vector3.Zero;

        float cellSize = grid.CellSize;
        float radiusSq = radius * radius;
        int span = Math.Max(1, (int)MathF.Ceiling(radius / cellSize));
        int cx = (int)MathF.Floor(position.X / cellSize);
        int cy = (int)MathF.Floor(position.Y / cellSize);
        int cz = (int)MathF.Floor(position.Z / cellSize);

        Vector3 sum = Vector3.Zero;
        for (int dz = -span; dz <= span; dz++)
        {
            int gz = cz + dz - grid.MinCellZ;
            if (gz < 0 || gz >= grid.DimZ)
                continue;
            for (int dy = -span; dy <= span; dy++)
            {
                int gy = cy + dy - grid.MinCellY;
                if (gy < 0 || gy >= grid.DimY)
                    continue;
                for (int dx = -span; dx <= span; dx++)
                {
                    int gx = cx + dx - grid.MinCellX;
                    if (gx < 0 || gx >= grid.DimX)
                        continue;

                    int cell = gx + grid.DimX * (gy + grid.DimY * gz);
                    int start = grid.CellStart[cell];
                    int end = start + grid.CellCount[cell];
                    for (int i = start; i < end; i++)
                    {
                        Photon ph = grid.Photons[i];
                        if (Vector3.Dot(ph.Normal, normal) < 0.5f)
                            continue;
                        if (Vector3.DistanceSquared(ph.Position, position) > radiusSq)
                            continue;
                        if (wavelengths.TryGet(ph.Wavelength, out Vector3 xyz))
                        {
                            float albedo = spectralAlbedo?.Invoke(ph.Wavelength) ?? 1f;
                            sum += xyz * (ph.Power * albedo);
                        }
                    }
                }
            }
        }

        return sum / (MathF.PI * radiusSq);
    }
}
