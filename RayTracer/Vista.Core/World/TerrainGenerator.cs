using Vista.Recipes;

namespace Vista.World;

/// <summary>
/// The fixed global terrain grid (vista-plan §4.1). Since M1.7 the default is 4097² samples 8 m apart, 32.8 km a side, the grid
/// erosion runs on (M0's was 1025² at 16 m, 16.4 km); vista-plan §10 M1.7 records why.
/// </summary>
/// <param name="Size">Samples per side (at least 2).</param>
/// <param name="CellSize">Sample spacing in metres.</param>
public sealed record WorldGrid(int Size = 4097, double CellSize = 8.0)
{
    /// <summary>The grid every world is generated and eroded on.</summary>
    public static WorldGrid Default { get; } = new();
}

/// <summary>
/// Generates the M0 terrain: the recipe's <see cref="TerrainProfile"/> evaluated on the global grid under
/// the seed's <see cref="SeedDerivation.TerrainBase"/> stream. A pure function of (set pieces, seed, grid),
/// independent of the pieces' order: time of day and season never touch it. Erosion, hydrology (lakes
/// carving basins) and snow layer on in M1+.
/// </summary>
public static class TerrainGenerator
{
    /// <summary>The height field for <paramref name="recipe"/> and <paramref name="seed"/>.</summary>
    public static HeightField Generate(Recipe recipe, ulong seed, WorldGrid? grid = null)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        grid ??= WorldGrid.Default;
        TerrainProfile profile = TerrainProfile.For(recipe.SetPieces);
        ulong terrainSeed = SeedDerivation.Derive(seed, SeedDerivation.TerrainBase);

        int size = grid.Size;
        var heights = new float[size * size];
        double origin = -0.5 * (size - 1) * grid.CellSize;
        // Rows are independent pure functions of position, so the parallel split cannot change any value.
        Parallel.For(0, size, j =>
        {
            double z = origin + j * grid.CellSize;
            for (int i = 0; i < size; i++)
                heights[j * size + i] = (float)profile.Height(terrainSeed, origin + i * grid.CellSize, z);
        });
        return new HeightField(size, grid.CellSize, heights);
    }
}
