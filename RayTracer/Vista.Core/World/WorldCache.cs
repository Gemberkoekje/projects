using Vista.Recipes;

namespace Vista.World;

/// <summary>
/// Keeps the most recent world, so renders that share one — a contact sheet across time of day or cameras
/// (vista-plan §7.4) — build and mesh it once. The key is what the world stage reads: the recipe's <see cref="TerrainProfile"/>,
/// the seed, the grid, and whether it is eroded (and could be); time, season, name and camera never move the world. The same
/// mesh instance also lets a renderer keep its acceleration structure. Watch mode (M1.8) replaces this with a cache per stage.
/// </summary>
public sealed class WorldCache
{
    private (TerrainProfile Profile, ulong Seed, WorldGrid Grid, bool Erode, bool CanErode)? _key;
    private TerrainWorld? _world;
    private TerrainMesh? _mesh;

    /// <summary>
    /// The world for <paramref name="recipe"/> and <paramref name="seed"/> on <paramref name="grid"/> (<see cref="WorldBuilder"/>),
    /// reused when the last one matches.
    /// </summary>
    public TerrainWorld World(Recipe recipe, ulong seed, WorldGrid grid, ITerrainEroder? eroder = null, ErosionCache? cache = null, bool erode = true)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(grid);
        var key = (TerrainProfile.For(recipe.SetPieces), seed, grid, erode, eroder is not null);
        if (_world is null || _key != key)
        {
            _world = WorldBuilder.Build(recipe, seed, grid, eroder, cache, erode);
            _key = key;
            _mesh = null;
        }
        return _world;
    }

    /// <summary>The mesh of <paramref name="terrain"/>, reused when it is the cached world's surface.</summary>
    public TerrainMesh Mesh(HeightField terrain)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        if (!ReferenceEquals(terrain, _world?.Surface))
            return HeightFieldMesher.Build(terrain);
        return _mesh ??= HeightFieldMesher.Build(terrain);
    }
}
