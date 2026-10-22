namespace Vista.World;

/// <summary>
/// An indexed triangle mesh ready for a DXR bottom-level acceleration structure: <c>float3</c> positions
/// (<c>R32G32B32_FLOAT</c>), matching <c>float3</c> unit normals, and <c>R32_UINT</c> indices.
/// <b>Winding:</b> for every triangle <c>(v0, v1, v2)</c> the component cross product
/// <c>(v1 − v0) × (v2 − v0)</c> points up (+Y) — the same formula the integrator must use if it derives
/// geometric normals, so front faces face the sky.
///
/// <para>Since M1.9 a mesh belongs to a view (<see cref="ViewMesher"/>): its positions are in the <see cref="Frame"/> bent onto
/// the Earth around the camera, which stands at <c>(0, height, 0)</c> in it, and it knows how high its vertices reach above the
/// ground sphere and how far from the camera — what the atmosphere's sky-view slices and the sun's reach are built for.</para>
/// </summary>
public sealed class TerrainMesh
{
    internal TerrainMesh(float[] positions, float[] normals, uint[] indices, uint[]? mapNormals = null, IReadOnlyList<NormalMap>? normalMaps = null,
        MaterialField? materials = null)
    {
        Positions = positions;
        Normals = normals;
        Indices = indices;
        MapNormals = mapNormals ?? [];
        NormalMaps = normalMaps ?? [];
        Materials = materials;
    }

    /// <summary>Vertex positions, three floats (x, y, z) per vertex, in <see cref="Frame"/>.</summary>
    public float[] Positions { get; }

    /// <summary>Unit vertex normals, three floats per vertex.</summary>
    public float[] Normals { get; }

    /// <summary>Triangle indices, three per triangle.</summary>
    public uint[] Indices { get; }

    /// <summary>
    /// Per vertex, the <see cref="NormalMaps"/>' normal at the vertex's world place (x and z as two halves, x low): what the shading
    /// subtracts from the map's normal at a hit, so it adds only what the triangle's corners cannot carry (vista-plan §10 M1.9).
    /// Empty when the mesh has no maps (the uniform mesh, every sample of which is a vertex).
    /// </summary>
    public uint[] MapNormals { get; }

    /// <summary>The smooth normals of the world's grids, finest first (shared by every mesh of the world); empty for none.</summary>
    public IReadOnlyList<NormalMap> NormalMaps { get; }

    /// <summary>
    /// The world's material field (vista-plan §10 M1.12; shared by every mesh of the world): what the shader reads to put rock, scree,
    /// soil and grass where the rules say. Null for M0's single material everywhere (<see cref="MeshSettings.Materials"/> off).
    /// </summary>
    public MaterialField? Materials { get; }

    /// <summary>Number of vertices.</summary>
    public int VertexCount => Positions.Length / 3;

    /// <summary>Number of triangles.</summary>
    public int TriangleCount => Indices.Length / 3;

    /// <summary>Upload size in bytes of the vertex and index buffers (before the acceleration structure and the normal maps).</summary>
    public long ByteSize => (long)Positions.Length * sizeof(float) + (long)Normals.Length * sizeof(float) + (long)Indices.Length * sizeof(uint)
        + (long)MapNormals.Length * sizeof(uint);

    /// <summary>The frame the positions are in: the world bent onto the ground sphere around the camera.</summary>
    public EarthFrame Frame { get; init; }

    /// <summary>The lowest the meshed world can reach above the ground sphere, m (its grids' lowest, less the detail's amplitude).</summary>
    public double MinHeight { get; init; }

    /// <summary>The highest the meshed world can reach above the ground sphere, m.</summary>
    public double MaxHeight { get; init; }

    /// <summary>
    /// How far the mesh reaches from the camera, m: the far field's horizon reach, else the world grid's farthest corner. Like the
    /// heights it depends on where the camera stands, never on where it looks, so a turn rebuilds no part of the air.
    /// </summary>
    public double ReachMetres { get; init; }

    /// <summary>What the mesh is and what it cost.</summary>
    public MeshInfo Info { get; internal set; } = new() { Kind = MeshKind.None };
}
