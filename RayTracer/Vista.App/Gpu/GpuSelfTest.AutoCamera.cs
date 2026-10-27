using System.Globalization;
using Vista.Composition;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;
using Vista.World;

namespace Vista.App.Gpu;

/// <summary>The auto camera's CPU composition buffer against the renderer's own outputs (vista-plan §10 M1.14).</summary>
internal static partial class GpuSelfTest
{
    /// <summary>
    /// The composition buffer the auto camera traces on the CPU (<see cref="TerrainSight"/>) for the self-test's view, against the
    /// reference render's depth and material outputs of the same view: which pixels are sky, how far the ground is, what it is, and where
    /// the skyline runs. They cannot agree everywhere: the renderer's pixel is sample 0's, somewhere inside it, its mesh carries the detail
    /// below the grid and the far field out past the horizon, and its materials the rules' noise; so the check is on shares.
    /// </summary>
    private static bool AutoCameraChecks(Recipe recipe, ulong seed, ErosionResult eroded, MaterialField materials, VistaScene scene, RenderOutput reference,
        TextWriter log)
    {
        GroundCover cover = scene.Ground;
        MaterialConstants k = materials.Apply(MaterialRules.Default.Constants((float)cover.GrassLineMetres, cover.Poleward, materials.NoiseSeed,
            (float)cover.TreeLineMetres, cover.Forest)).WithSnow(cover.SnowPlacement);
        double radius = scene.Air.PlanetRadiusMetres + scene.GroundAltitudeMetres;
        var sight = new TerrainSight(eroded.Surface, PoolMap.FarTerrain(recipe, seed, WorldGrid.Default), radius,
            new MaterialClassifier(materials, k, (float)scene.GroundAltitudeMetres));
        int w = reference.Width, h = reference.Height;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        CompositionBuffer cpu = sight.Trace(scene.Camera, w, h, parallel: true);
        double seconds = stopwatch.Elapsed.TotalSeconds;
        CompositionBuffer gpu = CompositionBuffer.FromRender(reference, scene.Camera, w, h, null, radius);

        int agree = 0, ground = 0, sameClass = 0;
        var errors = new List<double>();
        for (int p = 0; p < w * h; p++)
        {
            bool a = cpu.Class[p] != GroundClass.Sky, b = gpu.Class[p] != GroundClass.Sky;
            if (a == b)
                agree++;
            if (!(a && b))
                continue;
            ground++;
            errors.Add(Math.Abs(cpu.Depth[p] - gpu.Depth[p]) / gpu.Depth[p]);
            if (cpu.Class[p] == gpu.Class[p])
                sameClass++;
        }
        errors.Sort();
        double median = errors.Count > 0 ? errors[errors.Count / 2] : double.NaN;
        double tail = errors.Count > 0 ? errors[(int)(0.9 * (errors.Count - 1))] : double.NaN;
        int[] cpuSkyline = CompositionScorer.Skyline(cpu), gpuSkyline = CompositionScorer.Skyline(gpu);
        double skyline = cpuSkyline.Zip(gpuSkyline, (x, y) => Math.Abs(x - y)).Average();
        double agreeShare = (double)agree / (w * h), classShare = ground > 0 ? (double)sameClass / ground : 0.0;
        log.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"  the auto camera's buffer ({w}×{h}, {seconds:0.00} s on the CPU): sky or ground agree at {agreeShare:P1}; depth off by {median:P2} (median) and {tail:P1} (90th percentile) of the ground; the same class at {classShare:P1}; the skyline {skyline:0.00} rows apart"));
        bool pass = Check(log, "the auto camera's buffer sees the sky and the ground where the render does", agreeShare >= 0.97 ? null : $"{agreeShare:P1} agree");
        pass &= Check(log, "the auto camera's buffer finds the ground as far as the render does",
            median <= 0.02 && tail <= 0.1 ? null : string.Create(CultureInfo.InvariantCulture, $"median {median:P2}, 90th percentile {tail:P1}"));
        pass &= Check(log, "the auto camera's buffer runs its skyline where the render's runs", skyline <= 1.0 ? null : $"{skyline:0.00} rows apart on average");
        pass &= Check(log, "the auto camera's buffer names the ground as the render does", classShare >= 0.7 ? null : $"{classShare:P1} the same");
        return pass;
    }
}
