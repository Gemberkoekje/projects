namespace Vista.World;

/// <summary>
/// Today's glaciers on a finished world (vista-plan §4.5, §10 M1.13): the recipe's own, from its equilibrium line in the world's frame
/// (<see cref="ErosionSettings.GlacierElaMetres"/>), so they stand whatever the place, the season and the snow slider. The steady ice of
/// that climate (<see cref="GlacierFlow"/>) is solved on the eroded ground averaged onto the incision's grid (a glacier is hundreds of
/// metres wide; the 8 m gullies under it do not steer it), and its surface, bilinear between those samples, is laid over the 8 m ground:
/// the ice is the height of that smooth surface above the ground, faded out where the solve's ice thins to nothing, so rock ribs stand
/// through it and its edges meet the ground. A steady glacier's surface holds no closed hollow (its ice would flow into it), so where
/// the laid surface does (between the solve's samples, behind a rib, at an edge) the ice fills it to its spill. The world's surface is
/// the ground plus the ice; its drainage stays the ground's: meltwater, and the rivers the ice would dam, run under it along its bed
/// and leave at its snout.
/// </summary>
public static class Glaciers
{
    /// <summary>The ice solve's iterations, from no ice.</summary>
    public const int Iterations = 60;

    /// <summary>The solve's thickness over which the laid ice fades in at a glacier's edge, m.</summary>
    public const float EdgeMetres = 5f;

    /// <summary>
    /// <paramref name="eroded"/> with today's glaciers of <paramref name="settings"/> laid on it (<see cref="ErosionResult.Ice"/>), or
    /// <paramref name="eroded"/> itself when the settings lay none.
    /// </summary>
    public static ErosionResult Lay(ErosionResult eroded, ErosionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(eroded);
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.GlacierElaMetres <= 0f)
            return eroded;
        HeightField ground = eroded.Surface;
        int n = ground.Size;
        int factor = StreamPower.CoarseFactor(n, ground.CellSize, settings.IncisionCell);
        HeightField coarse = Average(ground, factor);
        IceField ice = GlacierFlow.Solve(coarse, settings.PresentClimate, GlacierFlowSettings.Default, null, Iterations);
        int nc = coarse.Size;
        var top = new float[nc * nc];
        for (int c = 0; c < top.Length; c++)
            top[c] = coarse.Heights[c] + ice.Thickness[c];
        float[] surface = StreamPower.Upsample(new HeightField(nc, coarse.CellSize, top), factor);
        float[] solved = StreamPower.Upsample(new HeightField(nc, coarse.CellSize, ice.Thickness), factor);

        float[] bed = ground.Heights.ToArray();
        float[] heights = ground.Heights.ToArray();
        var thickness = new float[n * n];
        Parallel.For(0, n, j =>
        {
            for (int c = j * n; c < (j + 1) * n; c++)
            {
                float x = Math.Clamp(solved[c] / EdgeMetres, 0f, 1f);
                float edge = x * x * (3f - 2f * x);
                float laid = ErosionResult.ToHalf(Math.Max(surface[c] - heights[c], 0f) * edge);
                thickness[c] = laid;
                heights[c] += laid;
            }
        });
        // The ice fills its surface's hollows (on seed 42's world 0.3% of the grid stood in ponds on the ice before it did).
        float[] fill = DrainageMap.Of(new HeightField(n, ground.CellSize, heights)).FillDepth;
        Parallel.For(0, n, j =>
        {
            for (int c = j * n; c < (j + 1) * n; c++)
            {
                if (thickness[c] > 0f && fill[c] > 0f)
                {
                    float laid = ErosionResult.ToHalf(thickness[c] + fill[c]);
                    thickness[c] = laid;
                    heights[c] = bed[c] + laid;
                }
            }
        });
        var withIce = new HeightField(n, ground.CellSize, heights);
        return new ErosionResult(withIce, eroded.Loose, eroded.Discharge, eroded.WaterDepth, eroded.StandingWater, eroded.Steps,
            eroded.Drainage, eroded.Hardness, eroded.Strata)
        {
            Ice = thickness,
        };
    }

    /// <summary>
    /// <paramref name="fine"/> on the grid <paramref name="factor"/> times coarser that nests in it: each coarse sample the mean of the
    /// fine samples within half a coarse cell of it (fewer at the edges).
    /// </summary>
    public static HeightField Average(HeightField fine, int factor)
    {
        ArgumentNullException.ThrowIfNull(fine);
        ArgumentOutOfRangeException.ThrowIfLessThan(factor, 1);
        int n = fine.Size;
        int nc = (n - 1) / factor + 1;
        int half = factor / 2;
        var heights = new float[nc * nc];
        Parallel.For(0, nc, jc =>
        {
            for (int ic = 0; ic < nc; ic++)
            {
                double sum = 0.0;
                int count = 0;
                for (int j = Math.Max(jc * factor - half, 0); j <= Math.Min(jc * factor + half, n - 1); j++)
                {
                    for (int i = Math.Max(ic * factor - half, 0); i <= Math.Min(ic * factor + half, n - 1); i++)
                    {
                        sum += fine[i, j];
                        count++;
                    }
                }
                heights[jc * nc + ic] = (float)(sum / count);
            }
        });
        return new HeightField(nc, fine.CellSize * factor, heights);
    }
}
