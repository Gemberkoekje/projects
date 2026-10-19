using Vista.Recipes;

namespace Vista.World;

/// <summary>
/// Keeps the most recent world, so renders that share one — a contact sheet across time of day or cameras
/// (vista-plan §7.4) — generate and mesh it once. The key is what <see cref="TerrainGenerator"/> reads: the
/// recipe's <see cref="TerrainProfile"/> and the seed; time, season, name and camera never move the world.
/// The same mesh instance also lets a renderer keep its acceleration structure. Watch mode (M1.8) replaces
/// this with a cache per pipeline stage.
/// </summary>
public sealed class WorldCache
{
    private TerrainProfile? _profile;
    private ulong _seed;
    private HeightField? _terrain;
    private TerrainMesh? _mesh;

    /// <summary>The height field for <paramref name="recipe"/> and <paramref name="seed"/>, reused when the last one matches.</summary>
    public HeightField Terrain(Recipe recipe, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        TerrainProfile profile = TerrainProfile.For(recipe.SetPieces);
        if (_terrain is null || _profile != profile || _seed != seed)
        {
            _terrain = TerrainGenerator.Generate(recipe, seed);
            _profile = profile;
            _seed = seed;
            _mesh = null;
        }
        return _terrain;
    }

    /// <summary>The mesh of <paramref name="terrain"/>, reused when it is the cached height field.</summary>
    public TerrainMesh Mesh(HeightField terrain)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        if (!ReferenceEquals(terrain, _terrain))
            return HeightFieldMesher.Build(terrain);
        return _mesh ??= HeightFieldMesher.Build(terrain);
    }
}
