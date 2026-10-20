using Vista.Recipes;

namespace Vista.World;

/// <summary>
/// The world and mesh stages of a <see cref="Cli.PreviewSession"/> (vista-plan §7.4, §10 M1.8, M1.9): keeps the most recent
/// world, its <see cref="TerrainHierarchy"/> and its last mesh, so renders that share them — a contact sheet across time of day
/// or cameras, or a watch session whose camera turned — build and mesh the world once. The world's key is what the world stage
/// reads: the recipe's <see cref="TerrainProfile"/>, the seed, the grid, and whether it is eroded (and could be); time, season,
/// name and camera never move the world. The hierarchy follows the world and the detail; the mesh follows the hierarchy, the
/// camera's position, the frame's pixels per radian, the ground sphere and the mesh settings, never the camera's heading
/// (<see cref="ViewMesher"/>). The same mesh instance also lets a renderer keep its acceleration structure. One world is kept,
/// not several: a seed the session saw before comes back from the erosion cache on disk in about a second.
/// </summary>
public sealed class WorldCache
{
    private (TerrainProfile Profile, ulong Seed, WorldGrid Grid, bool Erode, bool CanErode)? _key;
    private TerrainWorld? _world;
    private TerrainHierarchy? _hierarchy;
    private (MeshView View, MeshSettings Settings)? _meshKey;
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
            (_hierarchy, _meshKey, _mesh) = (null, null, null);
        }
        return _world!;
    }

    /// <summary>The mesh of the cached world for <paramref name="view"/> under <paramref name="settings"/>; reused when the last one was built for the same.</summary>
    public TerrainMesh Mesh(MeshView view, MeshSettings settings) => Mesh(view, settings, out _);

    /// <summary>
    /// As <see cref="Mesh(MeshView, MeshSettings)"/>; <paramref name="reused"/> says whether it was the last one. The world must
    /// have been asked for first.
    /// </summary>
    public TerrainMesh Mesh(MeshView view, MeshSettings settings, out bool reused)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_world is null || _key is null)
            throw new InvalidOperationException("Ask for the world before its mesh.");
        reused = _mesh is not null && _meshKey == (view, settings);
        if (reused)
            return _mesh!;

        double hierarchySeconds = 0.0;
        TerrainHierarchy hierarchy = Hierarchy(settings, ref hierarchySeconds);
        TerrainMesh mesh = ViewMesher.Build(hierarchy, view, settings);
        mesh.Info = mesh.Info with { HierarchySeconds = hierarchySeconds };
        (_mesh, _meshKey) = (mesh, (view, settings));
        return mesh;
    }

    /// <summary>The cached world's hierarchy with the detail <paramref name="settings"/> ask for, built when it is missing.</summary>
    private TerrainHierarchy Hierarchy(MeshSettings settings, ref double seconds)
    {
        (TerrainProfile profile, ulong seed, _, _, _) = _key!.Value;
        bool detail = settings.Kind == MeshKind.View && settings.Detail;
        if (_hierarchy is null || _hierarchy.Detail.IsNone == detail)
        {
            TerrainDetail wanted = detail ? new TerrainDetail(seed, _world!.Surface.CellSize) : TerrainDetail.None;
            _hierarchy = TerrainHierarchy.Build(_world!.Surface, profile, seed, wanted);
            seconds = _hierarchy.BuildSeconds;
        }
        return _hierarchy;
    }
}
