using System.Globalization;
using Vista.Recipes;
using Vista.World;

namespace Vista.App.Gpu;

/// <summary>
/// The GPU erosion against its C# reference (vista-plan §10 M1.7 verify: "HLSL against the C# reference on a 65² grid within
/// tolerance"). The grid is a 65² patch of the alpine range at the world grid's cell size, so its slopes are the real ones.
/// The reference runs a few dozen steps from the bare ground to reach a state with water, sediment, flux and loose material in
/// it; from that state each of the five passes runs once on both sides and their outputs are compared; then both run the whole
/// schedule's first steps from the bare ground and their surfaces are compared. Differences come from float rounding alone (the
/// GPU may fuse a multiply and an add, and its square root and division round differently), so a pass agrees to a few units in
/// the last place, and a run of steps a little less, since erosion amplifies differences.
/// </summary>
internal static class ErosionParity
{
    /// <summary>Samples per side of the parity grid.</summary>
    public const int Size = 65;

    /// <summary>Steps the reference runs to reach the state the passes are compared from.</summary>
    public const int WarmUpSteps = 40;

    /// <summary>Steps both sides run from the bare ground for the run comparison.</summary>
    public const int RunSteps = 200;

    /// <summary>The largest difference a single pass may leave, relative to the largest value of what it wrote.</summary>
    public const double PassTolerance = 1e-4;

    /// <summary>The largest surface difference a run may leave, relative to the patch's relief.</summary>
    public const double RunTolerance = 1e-3;

    /// <summary>Runs the comparisons on <paramref name="renderer"/>; writes one line per check; returns whether all pass.</summary>
    public static bool Run(D3D12VistaRenderer renderer, ErosionSettings settings, double cellSize, TextWriter log)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(settings);
        HeightField patch = TerrainGenerator.Generate(new Recipe { SetPieces = [SetPiece.AlpineRange] }, 42, new WorldGrid(Size, cellSize));
        var warm = new ErosionState(patch);
        ErosionSettings averaging = settings with { AverageFrom = 0 };
        ErosionReference.Run(warm, averaging, WarmUpSteps);
        ErosionConstants k = ErosionConstants.For(averaging, Size, cellSize);
        k.Accumulate = 1;
        int cells = Size * Size;
        bool pass = true;

        foreach (ErosionPass p in new[] { ErosionPass.Flux, ErosionPass.Water, ErosionPass.Transport, ErosionPass.ThermalOut, ErosionPass.ThermalApply })
        {
            ErosionState cpu = warm.Clone();
            float[][] expected;
            switch (p)
            {
                case ErosionPass.Flux:
                    ErosionReference.Flux(k, cpu.Terrain, cpu.Water, cpu.Flux);
                    expected = [cpu.Flux];
                    break;
                case ErosionPass.Water:
                    float[] terrain = new float[cells * 2], water = new float[cells], sediment = new float[cells];
                    ErosionReference.Water(k, cpu.Terrain, cpu.Water, cpu.Sediment, cpu.Flux, terrain, water, sediment, cpu.Flow);
                    expected = [terrain, water, sediment, cpu.Flow];
                    break;
                case ErosionPass.Transport:
                    float[] moved = new float[cells];
                    ErosionReference.Transport(k, cpu.Water, cpu.Flux, cpu.Sediment, moved);
                    expected = [moved];
                    break;
                case ErosionPass.ThermalOut:
                    ErosionReference.ThermalOut(k, cpu.Terrain, cpu.Thermal);
                    expected = [cpu.Thermal];
                    break;
                default:
                    float[] settled = new float[cells * 2];
                    ErosionReference.ThermalApply(k, cpu.Terrain, cpu.Thermal, settled);
                    expected = [settled];
                    break;
            }
            float[][] actual = renderer.RunErosionPass(warm, averaging, p, accumulate: true);
            double worst = 0.0;
            for (int b = 0; b < expected.Length; b++)
                worst = Math.Max(worst, RelativeDifference(expected[b], actual[b]));
            pass &= GpuSelfTest.Check(log, $"erosion pass {p} matches the C# reference",
                worst <= PassTolerance ? null : string.Create(CultureInfo.InvariantCulture, $"largest difference {worst:E2} of the largest value"));
            log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {p}: largest difference {worst:E2} of the largest value"));
        }

        var bare = new ErosionState(patch);
        ErosionState cpuRun = bare.Clone();
        ErosionReference.Run(cpuRun, settings, RunSteps);
        ErosionState gpuRun = renderer.RunErosion(bare, settings, RunSteps);
        ErosionResult a = ErosionResult.From(cpuRun), b2 = ErosionResult.From(gpuRun);
        double relief = patch.MaxHeight - patch.MinHeight;
        double max = 0.0, sum = 0.0, moved2 = 0.0;
        for (int c = 0; c < cells; c++)
        {
            double d = Math.Abs(a.Surface.Heights[c] - b2.Surface.Heights[c]);
            max = Math.Max(max, d);
            sum += d;
            moved2 += Math.Abs(a.Surface.Heights[c] - patch.Heights[c]);
        }
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  {RunSteps} steps on {Size}²: surfaces differ by {max:0.000} m at most, {sum / cells * 1e3:0.000} mm on average; erosion moved the ground {moved2 / cells:0.00} m on average; relief {relief:0} m"));
        pass &= GpuSelfTest.Check(log, $"{RunSteps} erosion steps match the C# reference",
            max <= RunTolerance * relief ? null : string.Create(CultureInfo.InvariantCulture, $"surfaces differ by {max:0.000} m, more than {RunTolerance * relief:0.000} m"));
        pass &= GpuSelfTest.Check(log, "the GPU's erosion is finite", gpuRun.Terrain.All(float.IsFinite) && gpuRun.Water.All(float.IsFinite) && gpuRun.Sediment.All(float.IsFinite) ? null : "a value is not finite");
        return pass;
    }

    /// <summary>The largest |a − b| relative to the largest |a| (or 1e-30 when everything is zero).</summary>
    public static double RelativeDifference(float[] expected, float[] actual)
    {
        if (expected.Length != actual.Length)
            return double.PositiveInfinity;
        double scale = 1e-30, worst = 0.0;
        for (int i = 0; i < expected.Length; i++)
        {
            if (!float.IsFinite(actual[i]))
                return double.PositiveInfinity;
            scale = Math.Max(scale, Math.Abs(expected[i]));
            worst = Math.Max(worst, Math.Abs((double)expected[i] - actual[i]));
        }
        return worst / scale;
    }
}
