using Vista.Recipes;

namespace Vista.World;

/// <summary>
/// Where the ground rises while the rivers cut (vista-plan §10 M1.11d): the incision's uplift per step at a place, from the base
/// terrain's large-scale shape (its lowest <see cref="ErosionSettings.UpliftOctaves"/> octaves, <see cref="TerrainProfile.Shape"/>)
/// raised to <see cref="ErosionSettings.UpliftContrast"/>, times <see cref="ErosionSettings.IncisionUplift"/>. Cordonnier et al.
/// 2016 take an uplift map as their main input: in the steady state of uplift and incision a river's slope grows with the uplift,
/// so where it is high the valleys stand high and the massifs rise, and where it is low the foothills stay low. A pure function
/// of the seed and the position; the grid's edge, the base level, does not rise. <see cref="ErosionSettings.UpliftOctaves"/> 0 is
/// M1.11a's uplift instead: in proportion to the base terrain's own height on the incision grid.
/// </summary>
public static class UpliftMap
{
    /// <summary>The uplift per step in metres at world <c>(x, z)</c> of the world of <paramref name="recipe"/> and <paramref name="seed"/>.</summary>
    public static double At(Recipe recipe, ulong seed, ErosionSettings settings, double x, double z)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return At(TerrainProfile.For(recipe.SetPieces), SeedDerivation.Derive(seed, SeedDerivation.TerrainBase), settings, x, z);
    }

    /// <summary>The same for the base terrain <paramref name="profile"/> under its own stream <paramref name="terrainSeed"/>.</summary>
    public static double At(TerrainProfile profile, ulong terrainSeed, ErosionSettings settings, double x, double z)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.UpliftOctaves < 1)
            throw new ArgumentException("M1.11a's uplift follows the incision grid's heights, not a position (UpliftMap.On).", nameof(settings));
        double shape = profile.Shape(terrainSeed, x, z, settings.UpliftOctaves);
        // Contrasts 1, 1.5 and 2 done exactly (a square root is correctly rounded); Math.Pow's last bit is the C library's.
        double share = settings.UpliftContrast switch
        {
            1f => shape,
            1.5f => shape * Math.Sqrt(shape),
            2f => shape * shape,
            _ => Math.Pow(shape, settings.UpliftContrast),
        };
        return settings.IncisionUplift * share;
    }

    /// <summary>
    /// The uplift per step on the grid of <paramref name="ground"/> (the base terrain on the incision grid; row-major, centred on the
    /// origin as <see cref="HeightField"/>), 0 on its edge.
    /// </summary>
    public static float[] On(Recipe recipe, ulong seed, ErosionSettings settings, HeightField ground)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(ground);
        int size = ground.Size;
        var uplift = new float[size * size];
        if (settings.UpliftOctaves < 1)
        {
            // M1.11a: in proportion to the base terrain's height, the ranges rising where it is high.
            float low = ground.MinHeight, span = Math.Max(ground.MaxHeight - low, 1e-3f);
            for (int j = 1; j < size - 1; j++)
            {
                for (int i = 1; i < size - 1; i++)
                    uplift[j * size + i] = settings.IncisionUplift * ((ground[i, j] - low) / span);
            }
            return uplift;
        }
        TerrainProfile profile = TerrainProfile.For(recipe.SetPieces);
        ulong terrainSeed = SeedDerivation.Derive(seed, SeedDerivation.TerrainBase);
        Parallel.For(1, size - 1, j =>
        {
            for (int i = 1; i < size - 1; i++)
                uplift[j * size + i] = (float)At(profile, terrainSeed, settings, ground.XOf(i), ground.ZOf(j));
        });
        return uplift;
    }
}
