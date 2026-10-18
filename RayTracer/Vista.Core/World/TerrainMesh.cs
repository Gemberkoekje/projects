namespace Vista.World;

/// <summary>
/// An indexed triangle mesh ready for a DXR bottom-level acceleration structure: <c>float3</c> positions
/// (<c>R32G32B32_FLOAT</c>), matching <c>float3</c> unit normals, and <c>R32_UINT</c> indices.
/// <b>Winding:</b> for every triangle <c>(v0, v1, v2)</c> the component cross product
/// <c>(v1 − v0) × (v2 − v0)</c> points up (+Y) — the same formula the integrator must use if it derives
/// geometric normals, so front faces face the sky.
/// </summary>
public sealed class TerrainMesh
{
    internal TerrainMesh(float[] positions, float[] normals, uint[] indices)
    {
        Positions = positions;
        Normals = normals;
        Indices = indices;
    }

    /// <summary>Vertex positions, three floats (x, y, z) per vertex.</summary>
    public float[] Positions { get; }

    /// <summary>Unit vertex normals, three floats per vertex.</summary>
    public float[] Normals { get; }

    /// <summary>Triangle indices, three per triangle.</summary>
    public uint[] Indices { get; }

    /// <summary>Number of vertices.</summary>
    public int VertexCount => Positions.Length / 3;

    /// <summary>Number of triangles.</summary>
    public int TriangleCount => Indices.Length / 3;

    /// <summary>Upload size in bytes of the three buffers (before the acceleration structure).</summary>
    public long ByteSize => (long)Positions.Length * sizeof(float) + (long)Normals.Length * sizeof(float) + (long)Indices.Length * sizeof(uint);
}

/// <summary>
/// Turns a <see cref="HeightField"/> into a <see cref="TerrainMesh"/> (vista-plan §10 M0 "height field →
/// mesh"): one vertex per sample, two triangles per cell, smooth normals from central differences. M0
/// meshes the whole grid uniformly; the view-dependent mesh to a screen-space error (§4.3) replaces this in
/// M1 — or a height-field intersection does, whichever the 3070 measurements favour.
/// </summary>
public static class HeightFieldMesher
{
    /// <summary>Meshes the whole height field.</summary>
    public static TerrainMesh Build(HeightField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        int n = field.Size;
        long vertexCount = (long)n * n;
        long indexCount = (long)(n - 1) * (n - 1) * 6;
        if (vertexCount * 3 > Array.MaxLength || indexCount > Array.MaxLength)
            throw new ArgumentException($"A {n}² height field is too large to mesh in one piece.", nameof(field));

        var positions = new float[vertexCount * 3];
        var normals = new float[vertexCount * 3];
        var indices = new uint[indexCount];
        double cell = field.CellSize;

        Parallel.For(0, n, j =>
        {
            for (int i = 0; i < n; i++)
            {
                int v = (j * n + i) * 3;
                positions[v] = (float)field.XOf(i);
                positions[v + 1] = field[i, j];
                positions[v + 2] = (float)field.ZOf(j);

                // Central differences inside the grid, one-sided at its edges: exact for planar terrain.
                int i0 = Math.Max(i - 1, 0), i1 = Math.Min(i + 1, n - 1);
                int j0 = Math.Max(j - 1, 0), j1 = Math.Min(j + 1, n - 1);
                double dhdx = (field[i1, j] - field[i0, j]) / ((i1 - i0) * cell);
                double dhdz = (field[i, j1] - field[i, j0]) / ((j1 - j0) * cell);
                double inv = 1.0 / Math.Sqrt(dhdx * dhdx + 1.0 + dhdz * dhdz);
                normals[v] = (float)(-dhdx * inv);
                normals[v + 1] = (float)inv;
                normals[v + 2] = (float)(-dhdz * inv);
            }

            if (j == n - 1)
                return;
            long t = (long)j * (n - 1) * 6;
            for (int i = 0; i < n - 1; i++, t += 6)
            {
                uint v00 = (uint)(j * n + i);
                uint v10 = v00 + 1;
                uint v01 = v00 + (uint)n;
                uint v11 = v01 + 1;
                // (v00, v01, v10) and (v10, v01, v11): both have an upward (v1 − v0) × (v2 − v0).
                indices[t] = v00;
                indices[t + 1] = v01;
                indices[t + 2] = v10;
                indices[t + 3] = v10;
                indices[t + 4] = v01;
                indices[t + 5] = v11;
            }
        });
        return new TerrainMesh(positions, normals, indices);
    }
}
