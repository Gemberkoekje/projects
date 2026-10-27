using Vista.Recipes;

namespace Vista.World;

/// <summary>
/// The world and mesh stages of a <see cref="Cli.PreviewSession"/> (vista-plan §7.4, §10 M1.8, M1.9): keeps the most recent
/// world, its <see cref="TerrainHierarchy"/> and its last mesh, so renders that share them — a contact sheet across time of day
/// or cameras, or a watch session whose camera turned — build and mesh the world once. The world's key is what the world stage
/// reads: the recipe's <see cref="TerrainProfile"/>, the seed, the grid, and whether it is eroded (and could be); time, season,
/// name and camera never move the world. The hierarchy follows the world, the detail and the forest's placement (the scene's tree
/// line and altitude, vista-plan §10 M1.12b); the mesh follows the hierarchy, the
/// camera's position, the frame's pixels per radian, the ground sphere and the mesh settings, never the camera's heading
/// (<see cref="ViewMesher"/>). The same mesh instance also lets a renderer keep its acceleration structure. One world is kept,
/// not several: a seed the session saw before comes back from the erosion cache on disk in about a second.
/// </summary>
public sealed class WorldCache
{
    private (TerrainProfile Profile, ulong Seed, WorldGrid Grid, bool Erode, bool CanErode)? _key;
    private Recipe? _recipe;
    private TerrainWorld? _world;
    private MaterialField? _materials;
    private MaterialField? _forested;
    private PoolMap? _pools;
    private HeightField? _far;
    private TerrainHierarchy? _hierarchy;
    private (bool Detail, bool Materials, ForestPlacement Forest)? _hierarchyKey;
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
            (_key, _recipe) = (key, recipe);
            (_materials, _forested, _hierarchy, _hierarchyKey, _meshKey, _mesh, _pools, _far) = (null, null, null, null, null, null, null, null);
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

    /// <summary>
    /// The cached world's material field (vista-plan §10 M1.12), built the first time a mesh asks for materials: its rock laid out
    /// again from the seed and the frame the erosion left (<see cref="WorldBuilder.StrataOf"/>), its maps from the erosion.
    /// </summary>
    public MaterialField Materials()
    {
        if (_world is null || _key is null || _recipe is null)
            throw new InvalidOperationException("Ask for the world before its materials.");
        if (_materials is null)
        {
            ulong seed = _key.Value.Seed;
            StrataGrid strata = WorldBuilder.StrataOf(_recipe, seed, _world.Surface, _world.Erosion?.Strata ?? StrataFrame.Identity, out RockHardness rock);
            _materials = MaterialField.For(_world.Surface, _world.Erosion, strata, rock.Tints, MaterialRules.Default, MaterialNoise.SeedOf(seed));
        }
        return _materials;
    }

    /// <summary>
    /// The cached world's pool map (vista-plan §10 M1.13b): where its cold air pools, from its drainage (worked out here for a world that
    /// was not eroded), and around it the far field's base terrain; built the first time a render asks for it, about two seconds.
    /// </summary>
    public PoolMap Pools()
    {
        if (_world is null || _key is null || _recipe is null)
            throw new InvalidOperationException("Ask for the world before its pools.");
        if (_pools is null)
        {
            (_, ulong seed, WorldGrid grid, _, _) = _key.Value;
            DrainageMap drainage = _world.Erosion?.Drainage ?? DrainageMap.Of(_world.Surface);
            _pools = PoolMap.For(_world.Surface, drainage, FarTerrain());
        }
        return _pools;
    }

    /// <summary>
    /// The cached world's far field (<see cref="PoolMap.FarTerrain"/>: its base terrain around it on a 128 m grid), built the first time
    /// the pools or the auto camera (vista-plan §10 M1.14) ask for it.
    /// </summary>
    public HeightField FarTerrain()
    {
        if (_world is null || _key is null || _recipe is null)
            throw new InvalidOperationException("Ask for the world before its far field.");
        if (_far is null)
        {
            (_, ulong seed, WorldGrid grid, _, _) = _key.Value;
            _far = PoolMap.FarTerrain(_recipe, seed, grid);
        }
        return _far;
    }

    /// <summary>
    /// How many triangles the cached world's mesh for <paramref name="view"/> under <paramref name="settings"/> would take
    /// (<see cref="ViewMesher.CountTriangles"/>), without building it or replacing the kept mesh.
    /// </summary>
    public long CountTriangles(MeshView view, MeshSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_world is null || _key is null)
            throw new InvalidOperationException("Ask for the world before its mesh.");
        double seconds = 0.0;
        return ViewMesher.CountTriangles(Hierarchy(settings, ref seconds), view, settings);
    }

    /// <summary>
    /// The cached world's material field with the detail weights of the forest <paramref name="forest"/> places (vista-plan §10
    /// M1.12b; <see cref="MaterialField.WithForest"/>), kept for the last placement; the world's own field without forest.
    /// </summary>
    public MaterialField Materials(ForestPlacement forest)
    {
        ArgumentNullException.ThrowIfNull(forest);
        MaterialField field = Materials();
        if (!forest.Grows)
            return field;
        if (_forested is null || _forested.Forest != forest)
            _forested = field.WithForest(_world!.Surface, forest, MaterialRules.Default);
        return _forested;
    }

    /// <summary>
    /// The cached world's hierarchy with the detail and the materials <paramref name="settings"/> ask for, built when it is missing:
    /// the detail by material (<see cref="MaterialDetail"/>) with the materials and the scene's forest, M1.9's smooth detail without them.
    /// </summary>
    private TerrainHierarchy Hierarchy(MeshSettings settings, ref double seconds)
    {
        (TerrainProfile profile, ulong seed, _, _, _) = _key!.Value;
        bool detail = settings.Kind == MeshKind.View && settings.Detail;
        ForestPlacement forest = settings.Materials ? settings.Forest : ForestPlacement.None;
        var key = (detail, settings.Materials, forest);
        if (_hierarchy is null || _hierarchyKey != key)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            MaterialField? materials = settings.Materials ? Materials(forest) : null;
            ISurfaceDetail wanted = !detail ? TerrainDetail.None
                : materials is not null ? new MaterialDetail(seed, materials)
                : new TerrainDetail(seed, _world!.Surface.CellSize);
            _hierarchy = TerrainHierarchy.Build(_world!.Surface, profile, seed, wanted, materials);
            _hierarchyKey = key;
            seconds = stopwatch.Elapsed.TotalSeconds;
        }
        return _hierarchy;
    }
}
