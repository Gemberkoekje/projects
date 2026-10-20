using Vista.Recipes;

namespace Vista.World;

/// <summary>
/// The world and mesh stages of a <see cref="Cli.PreviewSession"/> (vista-plan §7.4, §10 M1.8): keeps the most recent world and
/// its mesh, so renders that share one — a contact sheet across time of day or cameras, or a watch session whose camera moved —
/// build and mesh it once. The key is what the world stage reads: the recipe's <see cref="TerrainProfile"/>, the seed, the grid,
/// and whether it is eroded (and could be); time, season, name and camera never move the world. The same mesh instance also lets
/// a renderer keep its acceleration structure. One world is kept, not several: a seed the session saw before comes back from the
/// erosion cache on disk in about a second, and a second mesh of the full grid would hold most of another gigabyte.
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
    public TerrainWorld World(Recipe recipe, ulong seed, WorldGrid grid, ITerrainEroder? eroder = null, ErosionCache? cache = null, bool erode = true) =>
        World(recipe, seed, grid, eroder, cache, erode, out _);

    /// <summary>As <see cref="World(Recipe, ulong, WorldGrid, ITerrainEroder?, ErosionCache?, bool)"/>; <paramref name="reused"/> says whether it was the last one.</summary>
    public TerrainWorld World(Recipe recipe, ulong seed, WorldGrid grid, ITerrainEroder? eroder, ErosionCache? cache, bool erode, out bool reused)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(grid);
        var key = (TerrainProfile.For(recipe.SetPieces), seed, grid, erode, eroder is not null);
        reused = _world is not null && _key == key;
        if (!reused)
        {
            _world = WorldBuilder.Build(recipe, seed, grid, eroder, cache, erode);
            _key = key;
            _mesh = null;
        }
        return _world!;
    }

    /// <summary>The mesh of <paramref name="terrain"/>, reused when it is the cached world's surface.</summary>
    public TerrainMesh Mesh(HeightField terrain) => Mesh(terrain, out _);

    /// <summary>As <see cref="Mesh(HeightField)"/>; <paramref name="reused"/> says whether it was already built.</summary>
    public TerrainMesh Mesh(HeightField terrain, out bool reused)
    {
        // M1.9's view-dependent mesh will key this on the camera too.
        ArgumentNullException.ThrowIfNull(terrain);
        reused = false;
        if (!ReferenceEquals(terrain, _world?.Surface))
            return HeightFieldMesher.Build(terrain);
        reused = _mesh is not null;
        return _mesh ??= HeightFieldMesher.Build(terrain);
    }
}
