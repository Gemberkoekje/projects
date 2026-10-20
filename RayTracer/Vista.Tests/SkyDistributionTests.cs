using System.Numerics;
using Vista.Atmosphere;
using Vista.Rendering;
using Vista.Scene;

namespace Vista.Tests;

/// <summary>
/// The sky as a light (vista-plan §10 M1.5): the distribution's layout, that its pdf is a density over the sphere, that the
/// sampler and the pdf agree (the shader relies on it for MIS), that it follows the sky's luminance with the floor and the
/// below-horizon weight, that it is unbiased as an importance sampler, and the luminance measurement's C# reference.
/// </summary>
[TestClass]
public sealed class SkyDistributionTests
{
    private static readonly Vector3 LowSun = Vector3.Normalize(new Vector3(0.6f, 0.1f, -0.79f));

    private static double[] Uniform(double value = 1.0) => [.. Enumerable.Repeat(value, SkyDistribution.Cells)];

    /// <summary>A sky with a bright patch round the sun's azimuth just above the horizon, a dim zenith and dim land below.</summary>
    private static double[] Glowing()
    {
        var l = new double[SkyDistribution.Cells];
        for (int band = 0; band < SkyDistribution.ZenithBands; band++)
        {
            for (int column = 0; column < SkyDistribution.AzimuthBands; column++)
            {
                double above = band < SkyDistribution.ZenithBands / 2 ? 1.0 : 0.2;
                double glow = band is >= 55 and < 64 && column < 8 ? 50.0 : 0.0;
                l[band * SkyDistribution.AzimuthBands + column] = above + glow;
            }
        }
        return l;
    }

    private static double SolidAngle(int band) => 2.0 * SkyDistribution.ColumnWidth * (SkyDistribution.EdgeCos(band) - SkyDistribution.EdgeCos(band + 1));

    [TestMethod]
    public void EdgeCosines_RunFromTheZenithToTheNadir_ExactAtThePolesAndTheHorizon()
    {
        Assert.AreEqual(1.0, SkyDistribution.EdgeCos(0));
        Assert.AreEqual(0.0, SkyDistribution.EdgeCos(SkyDistribution.ZenithBands / 2));
        Assert.AreEqual(-1.0, SkyDistribution.EdgeCos(SkyDistribution.ZenithBands));
        for (int e = 0; e < SkyDistribution.ZenithBands; e++)
            Assert.IsTrue(SkyDistribution.EdgeCos(e + 1) < SkyDistribution.EdgeCos(e));
        Assert.AreEqual(Math.Cos(Math.PI / 4.0), SkyDistribution.EdgeCos(SkyDistribution.ZenithBands / 4), 1e-15);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SkyDistribution.EdgeCos(SkyDistribution.ZenithBands + 1));
    }

    [TestMethod]
    public void Floats_HoldTheEdgesAndCdfsFromZeroToOne()
    {
        SkyDistribution d = SkyDistribution.Build(Glowing());
        Assert.AreEqual(SkyDistribution.FloatCount, d.Floats.Length);
        Assert.AreEqual(1f, d.Floats[0]);
        Assert.AreEqual(-1f, d.Floats[SkyDistribution.ZenithBands]);
        Assert.AreEqual(0f, d.Floats[SkyDistribution.MarginalOffset]);
        Assert.AreEqual(1f, d.Floats[SkyDistribution.MarginalOffset + SkyDistribution.ZenithBands]);
        for (int i = 0; i < SkyDistribution.ZenithBands; i++)
        {
            Assert.IsTrue(d.Floats[SkyDistribution.MarginalOffset + i + 1] > d.Floats[SkyDistribution.MarginalOffset + i], $"band {i} has a chance");
            int at = SkyDistribution.ConditionalOffset + i * (SkyDistribution.AzimuthBands + 1);
            Assert.AreEqual(0f, d.Floats[at]);
            Assert.AreEqual(1f, d.Floats[at + SkyDistribution.AzimuthBands]);
            for (int j = 0; j < SkyDistribution.AzimuthBands; j++)
                Assert.IsTrue(d.Floats[at + j + 1] > d.Floats[at + j], $"cell ({i}, {j}) has a chance");
        }
    }

    [TestMethod]
    public void Pdf_IsADensityOverTheSphere()
    {
        foreach (double[] sky in new[] { Uniform(), Glowing() })
        {
            SkyDistribution d = SkyDistribution.Build(sky);
            double total = 0.0;
            for (int band = 0; band < SkyDistribution.ZenithBands; band++)
            {
                for (int column = 0; column < SkyDistribution.AzimuthBands; column++)
                    total += d.CellPdf(band, column) * SolidAngle(band);
            }
            Assert.AreEqual(1.0, total, 1e-5);
        }
    }

    [TestMethod]
    public void UniformSky_WeighsTheLandBelowTheHorizonAQuarter()
    {
        SkyDistribution d = SkyDistribution.Build(Uniform());
        double lit = 1.0 - SkyDistribution.UniformFraction;
        double share = 1.0 / (1.0 + SkyDistribution.BelowHorizonWeight);
        double above = (lit * share + SkyDistribution.UniformFraction * 0.5) / (2.0 * Math.PI);
        double below = (lit * (1.0 - share) + SkyDistribution.UniformFraction * 0.5) / (2.0 * Math.PI);
        Assert.AreEqual(above, d.Pdf(new Vector3D(0.3, 0.9, 0.3).Normalized(), LowSun), 1e-4 * above);
        Assert.AreEqual(below, d.Pdf(new Vector3D(0.3, -0.9, 0.3).Normalized(), LowSun), 1e-4 * below);
        Assert.IsTrue(d.HasLight);
        Assert.AreEqual(1.0, d.MeanLuminance, 1e-12, "the mean is the sky's, not the weights'");
    }

    [TestMethod]
    public void NoLight_IsUniform_AndSaysSo()
    {
        SkyDistribution d = SkyDistribution.Build(Uniform(0.0));
        Assert.IsFalse(d.HasLight);
        foreach (Vector3D dir in new[] { new Vector3D(0, 1, 0), new Vector3D(1, 0.01, 0).Normalized(), new Vector3D(0, -1, 0.2).Normalized() })
            Assert.AreEqual(1.0 / (4.0 * Math.PI), d.Pdf(dir, LowSun), 1e-4 / (4.0 * Math.PI));
    }

    [TestMethod]
    public void BrightCells_GetTheirShare_AndNoDirectionFallsBelowTheFloor()
    {
        SkyDistribution d = SkyDistribution.Build(Glowing());
        Vector3D toward = new(LowSun.X, 0.05f, LowSun.Z);
        Vector3D away = new(-LowSun.X, 0.05f, -LowSun.Z);
        double ratio = d.Pdf(toward.Normalized(), LowSun) / d.Pdf(away.Normalized(), LowSun);
        Assert.IsTrue(ratio is > 45.0 and < 51.0, $"the glow is 51 times the sky in its band; the floor, the same for both, narrows that to {ratio:0.0}");
        double floor = SkyDistribution.UniformFraction / (4.0 * Math.PI);
        for (int band = 0; band < SkyDistribution.ZenithBands; band++)
        {
            for (int column = 0; column < SkyDistribution.AzimuthBands; column++)
                Assert.IsTrue(d.CellPdf(band, column) >= floor * 0.999, $"cell ({band}, {column})");
        }
    }

    [TestMethod]
    public void Sample_LandsInItsCell_WithThePdfThePdfFunctionGives()
    {
        SkyDistribution d = SkyDistribution.Build(Glowing());
        var random = new Random(5);
        foreach (Vector3 sun in new[] { LowSun, Vector3.UnitY, Vector3.Normalize(new Vector3(-0.2f, -0.05f, 0.97f)) })
        {
            for (int i = 0; i < 20_000; i++)
            {
                (Vector3D dir, double pdf) = d.Sample(random.NextDouble(), random.NextDouble(), random.NextDouble(), sun);
                Assert.AreEqual(1.0, dir.Length(), 1e-12);
                Assert.AreEqual(pdf, d.Pdf(dir, sun), 1e-9 * pdf, $"sun {sun}, sample {i}");
            }
        }
    }

    [TestMethod]
    public void Sample_MirrorHalves_AreReflectionsInTheSunsPlane()
    {
        SkyDistribution d = SkyDistribution.Build(Glowing());
        (Vector3D left, double p1) = d.Sample(0.4, 0.3, 0.2, LowSun);
        (Vector3D right, double p2) = d.Sample(0.4, 0.3, 0.7, LowSun);
        (double ax, double az, double bx, double bz) = SkyDistribution.SunAxes(LowSun);
        Assert.AreEqual(p1, p2);
        Assert.AreEqual(left.Y, right.Y, 1e-15);
        Assert.AreEqual(left.X * ax + left.Z * az, right.X * ax + right.Z * az, 1e-12, "the same distance along the sun's azimuth");
        Assert.AreEqual(left.X * bx + left.Z * bz, -(right.X * bx + right.Z * bz), 1e-12, "opposite sides of its plane");
    }

    [TestMethod]
    public void ImportanceSampling_IsUnbiased()
    {
        // ∫ f dω for f = 1 + 40·max(ω·s, 0)⁸ (a lobe toward the sun) is 4π + 40·2π/9.
        SkyDistribution d = SkyDistribution.Build(Glowing());
        Vector3D s = new(LowSun.X, LowSun.Y, LowSun.Z);
        double exact = 4.0 * Math.PI + 40.0 * 2.0 * Math.PI / 9.0;
        var random = new Random(11);
        const int n = 400_000;
        double sum = 0.0, sum2 = 0.0;
        for (int i = 0; i < n; i++)
        {
            (Vector3D dir, double pdf) = d.Sample(random.NextDouble(), random.NextDouble(), random.NextDouble(), LowSun);
            double f = 1.0 + 40.0 * Math.Pow(Math.Max(Vector3D.Dot(dir, s), 0.0), 8);
            sum += f / pdf;
            sum2 += f * f / (pdf * pdf);
        }
        double mean = sum / n;
        double error = Math.Sqrt((sum2 / n - mean * mean) / n);
        Assert.AreEqual(exact, mean, 4.0 * error);
    }

    [TestMethod]
    public void CellPoints_AreStratifiedMidpointsInsideTheCell()
    {
        double c0 = SkyDistribution.EdgeCos(10), c1 = SkyDistribution.EdgeCos(11);
        double sum = 0.0;
        for (int a = 0; a < SkyDistribution.SubSamples; a++)
        {
            for (int b = 0; b < SkyDistribution.SubSamples; b++)
            {
                (double mu, double phi) = SkyDistribution.CellPoint(10, 5, a, b);
                Assert.IsTrue(mu < c0 && mu > c1);
                Assert.IsTrue(phi > 5 * SkyDistribution.ColumnWidth && phi < 6 * SkyDistribution.ColumnWidth);
                sum += mu;
            }
        }
        Assert.AreEqual(0.5 * (c0 + c1), sum / (SkyDistribution.SubSamples * SkyDistribution.SubSamples), 1e-15);
    }

    [TestMethod]
    public void CellLuminance_WeighsTheSmoothPartAndTheHazeThroughItsPhase()
    {
        AtmosphereFrame frame = AtmosphereFrame.For(AtmosphereModel.Default, 1608.0, LowSun, 100.0, 0.0, 3000.0);
        var weights = new float[AtmosphereLuts.Wavelengths];
        for (int l = 0; l < weights.Length; l++)
            weights[l] = 0.01f * (l + 1);
        double weightSum = weights.Sum(w => (double)w);
        var sky = new float[AtmosphereLuts.SkyViewFloats];
        for (int i = 0; i < sky.Length; i += 2)
            sky[i] = 1f; // smooth 1, haze 0
        Assert.AreEqual(weightSum, SkyDistribution.CellLuminance(sky, frame, weights, 3, 40, 10), 1e-9);

        for (int i = 0; i < sky.Length; i += 2)
            (sky[i], sky[i + 1]) = (0f, 1f); // haze 1 without its phase
        double muS = frame.SunCosZenith, sinS = Math.Sqrt(1.0 - muS * muS);
        double phase = 0.0;
        for (int a = 0; a < SkyDistribution.SubSamples; a++)
        {
            for (int b = 0; b < SkyDistribution.SubSamples; b++)
            {
                (double mu, double phi) = SkyDistribution.CellPoint(60, 2, a, b);
                phase += AirOptics.HenyeyGreenstein(Math.Sqrt(1.0 - mu * mu) * Math.Cos(phi) * sinS + mu * muS, frame.Model.AerosolAsymmetry);
            }
        }
        phase /= SkyDistribution.SubSamples * SkyDistribution.SubSamples;
        Assert.AreEqual(weightSum * phase, SkyDistribution.CellLuminance(sky, frame, weights, 7, 60, 2), 1e-9 * weightSum * phase);
    }

    [TestMethod]
    public void Build_RejectsBadInput()
    {
        Assert.ThrowsExactly<ArgumentException>(() => SkyDistribution.Build(new double[10]));
        double[] negative = Uniform();
        negative[5] = -1.0;
        Assert.ThrowsExactly<ArgumentException>(() => SkyDistribution.Build(negative));
        double[] nan = Uniform();
        nan[7] = double.NaN;
        Assert.ThrowsExactly<ArgumentException>(() => SkyDistribution.Build(nan));
    }
}
