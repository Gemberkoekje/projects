using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Vista.Astronomy;
using Vista.Atmosphere;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;
using Vista.World;

namespace Vista.App.Gpu;

/// <summary>
/// <c>Vista.App --gpu-selftest</c>: the checks that need the RTX 3070 (vista-plan §10), headless with a pass/fail
/// exit code, like the maze's <c>--phase6-selftest</c>. One fixed scene (snow-capped mountains at golden hour,
/// seed 42, the default camera, 320×180, 16 spp, every debug output) is rendered four times:
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
///
/// <para>Then the atmosphere (M1.3, M1.4): the LUT build is bit-identical twice; texels of each GPU LUT match their C#
/// reference (<see cref="AtmosphereLuts"/>) fed the same input LUTs, and so does the aerial perspective of chosen world
/// segments (<see cref="AerialPerspective"/>); a twilight render (the sun 4° below the horizon) is lit, from the sky
/// alone; and the reference atmosphere, with and without the terrain's shadow in the air, is bit-identical twice and
/// under the 0.05 ms budget.</para>
/// </summary>
internal static class GpuSelfTest
{
    /// <summary>Largest relative difference a GPU LUT texel may have from its C# reference (float against double).</summary>
    private const double LutTolerance = 2e-3;

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
        Almanac almanac = Almanac.For(recipe);
        var scene = new VistaScene(recipe, seed, DefaultCamera.For(terrain), terrain, mesh, almanac.Sun, MaterialCatalog.M0,
            AtmosphereModel.Default, Places.BaseAltitude(recipe, almanac.Place).Metres);
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

        pass &= AtmosphereChecks(renderer, scene, settings, tight, log);
        log.WriteLine(pass ? "PASS" : "FAIL");
        return pass ? 0 : 1;
    }

    private static bool AtmosphereChecks(D3D12VistaRenderer renderer, VistaScene scene, RenderSettings settings, BatchPlannerOptions tight, TextWriter log)
    {
        bool pass = true;
        AtmosphereFrame air = scene.AirFrame();
        renderer.ForgetAtmosphere();
        AtmosphereLutData luts = renderer.BuildAtmosphere(air, captureOrder2: true, out double seconds);
        log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  atmosphere LUTs: {seconds * 1e3:0.0} ms GPU"));
        renderer.ForgetAtmosphere();
        AtmosphereLutData rebuilt = renderer.BuildAtmosphere(air, captureOrder2: false, out _);
        pass &= Check(log, "the LUT build is bit-identical twice",
            Differs(luts.Columns, rebuilt.Columns) ?? Differs(luts.Orders, rebuilt.Orders)
            ?? Differs(luts.GroundIrradiance, rebuilt.GroundIrradiance) ?? Differs(luts.SkyView, rebuilt.SkyView));

        AtmosphereTables tables = AtmosphereTables.Build(air.Model);
        ScatteringOrder2 order2 = luts.Order2!;
        pass &= Check(log, "columns LUT texels match the C# reference", ColumnsParity(air, luts));
        pass &= Check(log, "direct and order-1 ground irradiance match the C# reference", IrradianceParity(air, luts, order2, tables));
        pass &= Check(log, "single-scattering texels match the C# reference", SingleParity(air, luts, order2, tables));
        pass &= Check(log, "order-2 density texels match the C# reference", DensityParity(air, luts, order2, tables));
        pass &= Check(log, "order-2 radiance texels match the C# reference", RadianceParity(air, order2, tables));
        pass &= Check(log, "ground-irradiance entries match the C# reference", GroundParity(air, luts, tables));
        pass &= Check(log, "sky-view LUT texels match the C# reference", SkyParity(air, luts, tables));
        pass &= Check(log, "aerial perspective along world segments matches the C# reference", AerialParity(renderer, scene, air, luts, tables));

        // Twilight: 4° below the horizon the sun lights no surface, so all the light is the sky's.
        Almanac almanac = Almanac.For(scene.Recipe);
        double latitude = almanac.Place.LatitudeDegrees;
        double hours = SolarPosition.HoursAtElevation(latitude, almanac.Date, -4.0, morning: false).Hours;
        VistaScene twilight = scene with { Sun = SolarPosition.At(latitude, almanac.Date, hours) };
        var aovs = new HashSet<Aov> { Aov.MaterialId };
        RenderOutput dim = Render(renderer, twilight, settings, aovs, new BatchPlannerOptions(), log, "twilight, sun at −4°");
        pass &= Check(log, "twilight is lit by the sky alone", Twilight(dim));

        // The reference atmosphere: deterministic like the rest, at a few samples.
        var small = settings with { SamplesPerPixel = 4, Atmosphere = AtmosphereMode.Reference };
        RenderOutput refA = Render(renderer, scene, small, aovs, new BatchPlannerOptions(), log, "reference atmosphere");
        RenderOutput refB = Render(renderer, scene, small, aovs, new BatchPlannerOptions(), log, "reference atmosphere again");
        RenderOutput refSplit = Render(renderer, scene, small, aovs, tight, log, "reference atmosphere, 0.05 ms budget");
        pass &= Check(log, "the reference atmosphere is sane", Sanity(refA, small));
        pass &= Check(log, "the reference atmosphere is bit-identical twice", Same(refA, refB, compareAovs: true));
        pass &= Check(log, "the reference atmosphere under a 0.05 ms budget is bit-identical", Same(refA, refSplit, compareAovs: true));

        // The reference with the terrain's shadow in the air (M1.4): deterministic too, and darker or equal on average.
        var shadowed = small with { Atmosphere = AtmosphereMode.ReferenceShadowed };
        RenderOutput shadA = Render(renderer, scene, shadowed, aovs, new BatchPlannerOptions(), log, "shadowed reference");
        RenderOutput shadB = Render(renderer, scene, shadowed, aovs, tight, log, "shadowed reference, 0.05 ms budget");
        pass &= Check(log, "the shadowed reference is sane", Sanity(shadA, shadowed));
        pass &= Check(log, "the shadowed reference under a 0.05 ms budget is bit-identical", Same(shadA, shadB, compareAovs: true));
        return pass;
    }

    /// <summary>
    /// The GPU's aerial perspective (<c>CSAerialProbe</c>: the world bent around the camera, the chord, the march) against
    /// <see cref="AerialPerspective"/> fed the GPU's own LUTs, on segments like the terrain's: down from the camera, a short
    /// bounce off to the side, a long grazing one across the tile, and one up a slope; at three wavelengths.
    /// </summary>
    private static string? AerialParity(D3D12VistaRenderer renderer, VistaScene scene, AtmosphereFrame air, AtmosphereLutData luts, AtmosphereTables tables)
    {
        Vector3D c = new(scene.Camera.Position.X, scene.Camera.Position.Y, scene.Camera.Position.Z);
        var segments = new (Vector3D A, Vector3D B)[]
        {
            (c, new Vector3D(c.X + 3000.0, 600.0, c.Z + 15_000.0)),
            (c, new Vector3D(c.X + 200.0, c.Y - 300.0, c.Z + 400.0)),
            (new Vector3D(c.X - 4000.0, 800.0, c.Z + 6000.0), new Vector3D(c.X - 3800.0, 950.0, c.Z + 6300.0)),
            (new Vector3D(c.X + 4000.0, 2400.0, c.Z + 4000.0), new Vector3D(c.X - 20_000.0, 10.0, c.Z + 20_000.0)),
            (new Vector3D(c.X, 520.0, c.Z + 2000.0), new Vector3D(c.X + 500.0, 2300.0, c.Z + 5000.0)),
        };
        var probes = new List<(Vector3 Start, Vector3 End, int Lambda)>();
        foreach ((Vector3D a, Vector3D b) in segments)
        {
            foreach (int lambda in ParityWavelengths)
                probes.Add((new Vector3((float)a.X, (float)a.Y, (float)a.Z), new Vector3((float)b.X, (float)b.Y, (float)b.Z), lambda));
        }
        float[] gpu = renderer.RunAerialProbe(air, new Vector3((float)c.X, (float)c.Y, (float)c.Z), probes);
        var worst = new Worst();
        var geometry = new Worst();
        for (int i = 0; i < probes.Count; i++)
        {
            (Vector3 a, Vector3 b, int lambda) = probes[i];
            AerialPerspective.Segment seg = AerialPerspective.Between(air, (float)c.X, (float)c.Z, new Vector3D(a.X, a.Y, a.Z), new Vector3D(b.X, b.Y, b.Z));
            (double t, double s) = AerialPerspective.Integrate(luts.Columns, luts.Orders, air, tables.AsUploaded(lambda), tables.Sun[lambda * 4 + 3], seg);
            string where = string.Create(CultureInfo.InvariantCulture, $"segment {i / ParityWavelengths.Length} λ{lambda}");
            worst.Add($"{where} transmittance", gpu[i * 8], t, 1e-6);
            worst.Add($"{where} in-scattering", gpu[i * 8 + 1], s, 1e-9);
            geometry.Add($"{where} μ", gpu[i * 8 + 2], seg.Mu, 1e-2);
            geometry.Add($"{where} length", gpu[i * 8 + 3], seg.Length, 1.0);
            geometry.Add($"{where} height", gpu[i * 8 + 4], seg.Height, 1.0);
            geometry.Add($"{where} μ_s", gpu[i * 8 + 5], seg.MuS, 1e-2);
            geometry.Add($"{where} ν", gpu[i * 8 + 6], seg.Nu, 1e-2);
        }
        return geometry.Verdict() ?? worst.Verdict();
    }

    private static string? ColumnsParity(AtmosphereFrame air, AtmosphereLutData luts)
    {
        var worst = new Worst();
        foreach (int j in new[] { 0, 1, 7, 31, 64, 128, 200, 255 })
        {
            foreach (int i in new[] { 0, 3, 64, 128, 200, 250, 255 })
            {
                DensityColumns c = AtmosphereLuts.ColumnsTexel(air.Model, i, j);
                int at = (j * AtmosphereLuts.ColumnsMuSize + i) * 4;
                worst.Add($"texel ({i}, {j}) Rayleigh", luts.Columns[at], c.Rayleigh, 1.0);
                worst.Add($"texel ({i}, {j}) haze", luts.Columns[at + 1], c.Aerosol, 1.0);
                worst.Add($"texel ({i}, {j}) ozone", luts.Columns[at + 2], c.Ozone, 1.0);
            }
        }
        return worst.Verdict();
    }

    /// <summary>
    /// 4D texels to spot-check, as fractions of each axis (so they follow the table sizes): every corner region of the
    /// domain, rays to the top (view fraction below ½) and to the ground (above ½).
    /// </summary>
    private static readonly (int H, int Mu, int MuS, int Nu)[] ScatterTexels =
    [
        .. new (double H, double Mu, double MuS, double Nu)[]
        {
            (0.0, 0.0, 1.0, 1.0), (0.0, 0.49, 0.5, 0.4), (0.0, 0.5, 0.62, 0.0), (0.0, 1.0, 1.0, 1.0), (0.2, 0.16, 0.16, 0.57),
            (0.33, 0.63, 0.77, 0.86), (0.5, 0.31, 0.1, 0.14), (0.67, 0.78, 0.9, 0.29), (1.0, 0.0, 0.0, 1.0), (1.0, 0.49, 0.58, 0.71),
            (0.47, 0.52, 0.35, 1.0), (0.13, 0.94, 0.39, 0.0),
        }.Select(f => (
            (int)Math.Round(f.H * (AtmosphereScattering.HeightSize - 1)),
            (int)Math.Round(f.Mu * (AtmosphereScattering.MuSize - 1)),
            (int)Math.Round(f.MuS * (AtmosphereScattering.MuSSize - 1)),
            (int)Math.Round(f.Nu * (AtmosphereScattering.NuSize - 1)))),
    ];

    /// <summary>Hero wavelengths spot-checked in the per-render tables (blue, green, red).</summary>
    private static readonly int[] ParityWavelengths = [0, 27, 49];

    /// <summary>Table wavelengths spot-checked in the 4D tables (<see cref="AtmosphereScattering.HeroOf"/>: hero 0, 25 and 49).</summary>
    private static readonly int[] ParityTableWavelengths = [0, 5, 10];

    private static string? IrradianceParity(AtmosphereFrame air, AtmosphereLutData luts, ScatteringOrder2 order2, AtmosphereTables tables)
    {
        var worst = new Worst();
        foreach (int entry in new[] { 0, 60, 64, 70, 100, 127 })
        {
            foreach (int l in ParityTableWavelengths)
            {
                int at = entry * AtmosphereScattering.MsWavelengths + l;
                worst.Add($"direct, entry {entry} l{l}", order2.Direct[at],
                    AtmosphereScattering.DirectIrradianceEntry(luts.Columns, air, tables.AsUploaded(AtmosphereScattering.HeroOf(l)), entry), 1e-9);
                worst.Add($"order 1, entry {entry} l{l}", order2.Irradiance[at],
                    AtmosphereScattering.IndirectIrradianceEntry(order2.Single, [], firstOrder: true, air, l, entry), 1e-9);
            }
        }
        return worst.Verdict();
    }

    private static string? SingleParity(AtmosphereFrame air, AtmosphereLutData luts, ScatteringOrder2 order2, AtmosphereTables tables)
    {
        var worst = new Worst();
        foreach ((int h, int mu, int mus, int nu) in ScatterTexels)
        {
            foreach (int l in ParityTableWavelengths)
            {
                (double rayleigh, double mie) = AtmosphereScattering.SingleScatteringTexel(luts.Columns, air, tables.AsUploaded(AtmosphereScattering.HeroOf(l)), h, mu, mus, nu);
                int at = AtmosphereScattering.Index(h, mu, mus, nu, l, 2);
                worst.Add($"texel ({h}, {mu}, {mus}, {nu}) l{l} Rayleigh", order2.Single[at], rayleigh, 1e-9);
                worst.Add($"texel ({h}, {mu}, {mus}, {nu}) l{l} haze", order2.Single[at + 1], mie, 1e-9);
            }
        }
        return worst.Verdict();
    }

    private static string? DensityParity(AtmosphereFrame air, AtmosphereLutData luts, ScatteringOrder2 order2, AtmosphereTables tables)
    {
        var worst = new Worst();
        foreach ((int h, int mu, int mus, int nu) in ScatterTexels)
        {
            foreach (int l in ParityTableWavelengths)
            {
                (double rayleigh, double mie) = AtmosphereScattering.DensityTexel(luts.Columns, order2.Single, [], order2.Direct, air,
                    tables.AsUploaded(AtmosphereScattering.HeroOf(l)), 2, l, h, mu, mus, nu);
                int at = AtmosphereScattering.Index(h, mu, mus, nu, l, 2);
                worst.Add($"texel ({h}, {mu}, {mus}, {nu}) l{l} Rayleigh", order2.Density[at], rayleigh, 1e-9);
                worst.Add($"texel ({h}, {mu}, {mus}, {nu}) l{l} haze", order2.Density[at + 1], mie, 1e-9);
            }
        }
        return worst.Verdict();
    }

    private static string? RadianceParity(AtmosphereFrame air, ScatteringOrder2 order2, AtmosphereTables tables)
    {
        var worst = new Worst();
        foreach ((int h, int mu, int mus, int nu) in ScatterTexels)
        {
            foreach (int l in ParityTableWavelengths)
            {
                double expected = AtmosphereScattering.MultipleScatteringTexel(order2.Density, air, tables.AsUploaded(AtmosphereScattering.HeroOf(l)), l, h, mu, mus, nu);
                worst.Add($"texel ({h}, {mu}, {mus}, {nu}) l{l}", order2.Radiance[AtmosphereScattering.Index(h, mu, mus, nu, l, 1)], expected, 1e-9);
            }
        }
        return worst.Verdict();
    }

    private static string? GroundParity(AtmosphereFrame air, AtmosphereLutData luts, AtmosphereTables tables)
    {
        var worst = new Worst();
        foreach (int entry in new[] { 50, 64, 70, 100, 127 })
        {
            foreach (int lambda in new[] { 0, 49 })
            {
                double expected = AtmosphereLuts.GroundIrradianceEntry(luts.Columns, luts.Orders, air, tables.AsUploaded(lambda), lambda, entry);
                worst.Add($"entry {entry} λ{lambda}", luts.GroundIrradiance[entry * AtmosphereLuts.Wavelengths + lambda], expected, 1e-12);
            }
        }
        return worst.Verdict();
    }

    private static string? SkyParity(AtmosphereFrame air, AtmosphereLutData luts, AtmosphereTables tables)
    {
        var worst = new Worst();
        foreach ((int slice, int row, int column) in new[]
        {
            (0, 0, 0), (0, 20, 5), (0, 63, 0), (0, 63, 63), (0, 64, 10), (0, 80, 40), (0, 95, 63),
            (1, 40, 30), (6, 62, 1), (12, 66, 33), (12, 10, 60),
        })
        {
            foreach (int lambda in new[] { 0, 27, 49 })
            {
                (double smooth, double mie) = AtmosphereLuts.SkyViewTexel(luts.Columns, luts.Orders, luts.GroundIrradiance, air,
                    tables.AsUploaded(lambda), lambda, slice, row, column);
                int at = ((((slice * AtmosphereLuts.SkyRows) + row) * AtmosphereLuts.SkyAzimuthSize + column) * AtmosphereLuts.Wavelengths + lambda) * 2;
                worst.Add($"texel ({slice}, {row}, {column}) λ{lambda} smooth", luts.SkyView[at], smooth, 1e-12);
                worst.Add($"texel ({slice}, {row}, {column}) λ{lambda} haze", luts.SkyView[at + 1], mie, 1e-12);
            }
        }
        return worst.Verdict();
    }

    /// <summary>Tracks the largest relative difference of GPU texels from their references.</summary>
    private sealed class Worst
    {
        private double _error;
        private string _where = string.Empty;
        private int _count;

        public void Add(string where, float gpu, double expected, double floor)
        {
            _count++;
            double error = float.IsFinite(gpu) ? Math.Abs(gpu - expected) / Math.Max(Math.Abs(expected), floor) : double.PositiveInfinity;
            if (error > _error || double.IsNaN(error))
                (_error, _where) = (error, string.Create(CultureInfo.InvariantCulture, $"{where}: GPU {gpu:G6}, C# {expected:G6}"));
        }

        public string? Verdict() => _error <= LutTolerance
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"{_count} values, worst relative difference {_error:G3} at {_where}");
    }

    private static string? Twilight(RenderOutput output)
    {
        double sumY = 0.0;
        for (int p = 0; p < output.Xyz.Length; p += 3)
        {
            if (!float.IsFinite(output.Xyz[p]) || !float.IsFinite(output.Xyz[p + 1]) || !float.IsFinite(output.Xyz[p + 2]))
                return $"pixel {p / 3} is not finite";
            sumY += output.Xyz[p + 1];
        }
        return sumY > 0.0 ? null : "the twilight image is black";
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
