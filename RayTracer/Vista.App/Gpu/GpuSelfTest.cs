using System.Globalization;
using System.Runtime.InteropServices;
using Vista.Astronomy;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;
using Vista.World;

namespace Vista.App.Gpu;

/// <summary>
/// <c>Vista.App --gpu-selftest</c>: the M0 checks that need the RTX 3070 (vista-plan §10), headless with a
/// pass/fail exit code, like the maze's <c>--phase6-selftest</c>. One fixed scene (snow-capped mountains at
/// golden hour, seed 42, the default camera, 320×180, 16 spp, every debug output) is rendered four times:
/// <list type="number">
///   <item>the reference, at the default dispatch budget — it must be sane (finite, lit, sky and terrain both
///   in frame, more rays than pixel-samples);</item>
///   <item>the same again — bit-identical, same ray count;</item>
///   <item>under a 0.05 ms budget, so row bands of one sample replace full-frame passes of many — bit-identical,
///   same ray count (the batch partition must not change the image);</item>
///   <item>with no debug outputs — the beauty is bit-identical (asking for debug images never changes it).</item>
/// </list>
/// Then the loupe (M1.1): a 640×360 frame, a 64×64 window of it straddling a peak and the sky, and that window
/// again under the 0.05 ms budget. The window must equal the same pixels cut from the frame, bit for bit in
/// the beauty and every debug output, however it is split into dispatches.
/// </summary>
internal static class GpuSelfTest
{
    /// <summary>Runs the checks, writing a report to <paramref name="log"/>; returns 0 when every check passes.</summary>
    public static int Run(TextWriter log)
    {
        var recipe = new Recipe
        {
            Name = "gpu self-test",
            SetPieces = [SetPiece.SnowCappedMountains],
            Time = TimeOfDay.GoldenHour,
        };
        const ulong seed = 42;
        HeightField terrain = TerrainGenerator.Generate(recipe, seed);
        TerrainMesh mesh = HeightFieldMesher.Build(terrain);
        var scene = new VistaScene(recipe, seed, DefaultCamera.For(terrain), terrain, mesh, Almanac.For(recipe).Sun, MaterialCatalog.M0);
        var settings = new RenderSettings { Width = 320, Height = 180, SamplesPerPixel = 16, MaxBounces = 6 };
        var allAovs = new HashSet<Aov>(Enum.GetValues<Aov>());

        using var renderer = new D3D12VistaRenderer();
        log.WriteLine($"Device: {renderer.DeviceName}");
        RenderOutput reference = Render(renderer, scene, settings, allAovs, new BatchPlannerOptions(), log, "reference");
        RenderOutput again = Render(renderer, scene, settings, allAovs, new BatchPlannerOptions(), log, "again");
        // A pessimistic first guess keeps the early dispatches to a few rows each.
        var tight = new BatchPlannerOptions { DispatchBudgetMs = 0.05, InitialCostNs = 20000 };
        RenderOutput split = Render(renderer, scene, settings, allAovs, tight, log, "0.05 ms budget");
        RenderOutput bare = Render(renderer, scene, settings, new HashSet<Aov>(), new BatchPlannerOptions(), log, "no debug outputs");

        bool pass = true;
        pass &= Check(log, "reference is sane", Sanity(reference, settings));
        pass &= Check(log, "same render twice is bit-identical", Same(reference, again, compareAovs: true));
        // A dispatch covers whole rows and at least one sample: more dispatches than samples means some pass
        // was split into row bands, fewer means some dispatch took several samples at once.
        pass &= Check(log, "the two budgets split the render differently",
            reference.Stats.Dispatches < settings.SamplesPerPixel && split.Stats.Dispatches > settings.SamplesPerPixel
                ? null
                : $"{reference.Stats.Dispatches} and {split.Stats.Dispatches} dispatches for {settings.SamplesPerPixel} samples");
        pass &= Check(log, "0.05 ms budget is bit-identical", Same(reference, split, compareAovs: true));
        pass &= Check(log, "debug outputs never change the beauty", Same(reference, bare, compareAovs: false));

        // The loupe: off the 8-pixel thread-group grid, with the central peak and sky in it.
        var frame = settings with { Width = 640, Height = 360 };
        var window = new PixelWindow(291, 137, 64, 64);
        RenderOutput full = Render(renderer, scene, frame, allAovs, new BatchPlannerOptions(), log, "640×360 frame");
        RenderOutput loupe = Render(renderer, scene, frame with { Window = window }, allAovs, new BatchPlannerOptions(), log, $"loupe {window}");
        RenderOutput loupeSplit = Render(renderer, scene, frame with { Window = window }, allAovs, tight, log, $"loupe {window}, 0.05 ms budget");
        pass &= Check(log, "the loupe sees both sky and terrain", Mixed(loupe));
        pass &= Check(log, "the loupe is that window of the full frame, bit for bit", SameAsCrop(full, loupe, window));
        pass &= Check(log, "the loupe under a 0.05 ms budget is bit-identical", Same(loupe, loupeSplit, compareAovs: true));
        log.WriteLine(pass ? "PASS" : "FAIL");
        return pass ? 0 : 1;
    }

    private static RenderOutput Render(D3D12VistaRenderer renderer, VistaScene scene, RenderSettings settings,
        IReadOnlySet<Aov> aovs, BatchPlannerOptions batching, TextWriter log, string label)
    {
        var planner = new BatchPlanner(settings.Rendered.Width, settings.Rendered.Height, settings.SamplesPerPixel, batching);
        RenderOutput output = renderer.Render(scene, settings with { Batching = batching }, aovs, planner);
        RenderStats s = output.Stats;
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  {label}: {s.Dispatches} dispatches, {s.RaysTraced:N0} rays ({s.RaysPerSample():0.00}/sample), " +
            $"{s.TraceSeconds * 1e3:0.0} ms GPU, longest dispatch {s.LongestDispatchSeconds * 1e3:0.00} ms"));
        return output;
    }

    private static bool Check(TextWriter log, string name, string? failure)
    {
        log.WriteLine(failure is null ? $"[ok]   {name}" : $"[FAIL] {name}: {failure}");
        return failure is null;
    }

    private static string? Sanity(RenderOutput output, RenderSettings settings)
    {
        double sumY = 0.0;
        for (int p = 0; p < output.Xyz.Length; p += 3)
        {
            if (!float.IsFinite(output.Xyz[p]) || !float.IsFinite(output.Xyz[p + 1]) || !float.IsFinite(output.Xyz[p + 2]))
                return $"pixel {p / 3} is not finite";
            sumY += output.Xyz[p + 1];
        }
        if (sumY <= 0.0)
            return "the image is black";
        float[] ids = output.Aovs[Aov.MaterialId];
        int sky = ids.Count(id => id < 0f);
        if (sky == 0 || sky == ids.Length)
            return $"{sky} of {ids.Length} pixels see sky; expected both sky and terrain in frame";
        long pixelSamples = (long)settings.Width * settings.Height * settings.SamplesPerPixel;
        if (output.Stats.PixelSamples != pixelSamples)
            return $"{output.Stats.PixelSamples} pixel-samples traced, expected {pixelSamples}";
        if (output.Stats.RaysTraced <= (ulong)pixelSamples)
            return $"{output.Stats.RaysTraced} rays for {pixelSamples} pixel-samples; the ray counter is not counting";
        return null;
    }

    private static string? Mixed(RenderOutput output)
    {
        float[] ids = output.Aovs[Aov.MaterialId];
        int sky = ids.Count(id => id < 0f);
        return sky > 0 && sky < ids.Length ? null : $"{sky} of {ids.Length} pixels see sky; move the window onto the horizon";
    }

    private static string? SameAsCrop(RenderOutput frame, RenderOutput loupe, PixelWindow window)
    {
        if (loupe.Width != window.Width || loupe.Height != window.Height)
            return $"the loupe is {loupe.Width}×{loupe.Height}, expected {window.Width}×{window.Height}";
        if (Differs(window.Crop(frame.Xyz, frame.Width, 3), loupe.Xyz) is { } xyz)
            return $"XYZ {xyz}";
        foreach ((Aov aov, float[] data) in frame.Aovs)
        {
            if (Differs(window.Crop(data, frame.Width, AovLayout.Channels(aov)), loupe.Aovs[aov]) is { } diff)
                return $"{aov} {diff}";
        }
        return null;
    }

    private static string? Same(RenderOutput a, RenderOutput b, bool compareAovs)
    {
        if (Differs(a.Xyz, b.Xyz) is { } xyz)
            return $"XYZ {xyz}";
        if (a.Stats.RaysTraced != b.Stats.RaysTraced)
            return $"ray counts differ ({a.Stats.RaysTraced} vs {b.Stats.RaysTraced})";
        if (!compareAovs)
            return null;
        foreach ((Aov aov, float[] data) in a.Aovs)
        {
            if (Differs(data, b.Aovs[aov]) is { } diff)
                return $"{aov} {diff}";
        }
        return null;
    }

    private static string? Differs(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            return $"lengths differ ({a.Length} vs {b.Length})";
        if (MemoryMarshal.AsBytes(a.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(b.AsSpan())))
            return null;
        int count = 0;
        double worst = 0.0;
        for (int i = 0; i < a.Length; i++)
        {
            if (BitConverter.SingleToInt32Bits(a[i]) == BitConverter.SingleToInt32Bits(b[i]))
                continue;
            count++;
            worst = Math.Max(worst, Math.Abs((double)a[i] - b[i]));
        }
        return string.Create(CultureInfo.InvariantCulture, $"differs in {count} of {a.Length} floats (max |Δ| {worst:G4})");
    }
}
