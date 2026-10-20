using Vista.World;

namespace Vista.Tests;

/// <summary>
/// The terrain's detail below the world grid (vista-plan §10 M1.9): a pure function of position and seed, and a bound on how far it
/// strays from a straight edge that the view-dependent mesh can rely on without looking.
/// </summary>
[TestClass]
public sealed class TerrainDetailTests
{
    [TestMethod]
    public void Height_IsAPureFunctionOfPositionAndSeed()
    {
        var detail = new TerrainDetail(42, 8.0);
        Assert.AreEqual(detail.Height(123.25, -4567.5), new TerrainDetail(42, 8.0).Height(123.25, -4567.5));
        Assert.AreNotEqual(detail.Height(123.25, -4567.5), new TerrainDetail(43, 8.0).Height(123.25, -4567.5), "another seed, other rock");
        Assert.AreEqual(0.0, TerrainDetail.None.Height(123.25, -4567.5));
        Assert.IsTrue(TerrainDetail.None.IsNone);
        Assert.AreEqual(0.0, new TerrainDetail(42, 8.0, octaves: 6, amplitudeRatio: 0.0).Height(1.0, 2.0));
    }

    [TestMethod]
    public void Octaves_StartAtTwiceTheCell()
    {
        var detail = new TerrainDetail(42, 8.0);
        Assert.AreEqual(16.0, detail.CoarsestWavelength);
        Assert.AreEqual(0.5, detail.FinestWavelength);
        Assert.AreEqual(TerrainDetail.DefaultAmplitudeRatio * TerrainDetail.NoiseMax * (16.0 + 8.0 + 4.0 + 2.0 + 1.0 + 0.5), detail.MaxAmplitude, 1e-12);
    }

    [TestMethod]
    public void Height_StaysWithinItsAmplitude()
    {
        var detail = new TerrainDetail(7, 8.0);
        var random = new Random(1);
        double worst = 0.0;
        for (int i = 0; i < 20_000; i++)
            worst = Math.Max(worst, Math.Abs(detail.Height(random.NextDouble() * 4000.0 - 2000.0, random.NextDouble() * 4000.0 - 2000.0)));
        Assert.IsTrue(worst <= detail.MaxAmplitude, $"{worst} > {detail.MaxAmplitude}");
        Assert.IsTrue(worst > 0.2 * detail.MaxAmplitude, "the detail is there");
    }

    [TestMethod]
    public void MidpointBound_HoldsOnRandomSegments_AtEveryLevelOfTheMesh()
    {
        var detail = new TerrainDetail(11, 8.0);
        var random = new Random(2);
        // The mesh's edges below the grid: 8√2, 8, 4√2, … down to 6 cm.
        for (double length = 8.0 * Math.Sqrt(2.0); length > 0.05; length /= Math.Sqrt(2.0))
        {
            double bound = detail.MidpointBound(length);
            for (int i = 0; i < 4000; i++)
            {
                double x = random.NextDouble() * 4000.0, z = random.NextDouble() * 4000.0, angle = random.NextDouble() * 2.0 * Math.PI;
                double bx = x + length * Math.Cos(angle), bz = z + length * Math.Sin(angle);
                double off = detail.Height(0.5 * (x + bx), 0.5 * (z + bz)) - 0.5 * (detail.Height(x, z) + detail.Height(bx, bz));
                Assert.IsTrue(Math.Abs(off) <= bound, $"{length:0.###} m: {off} beyond {bound}");
            }
        }
    }

    [TestMethod]
    public void MidpointBound_ShrinksWithTheEdge_FasterOnceBelowTheFinestOctave()
    {
        var detail = new TerrainDetail(11, 8.0);
        double previous = double.PositiveInfinity;
        for (double length = 64.0; length > 0.01; length *= 0.5)
        {
            double bound = detail.MidpointBound(length);
            Assert.IsTrue(bound <= previous, $"{length} m");
            previous = bound;
        }
        Assert.AreEqual(4.0, detail.MidpointBound(0.02) / detail.MidpointBound(0.01), 1e-9, "quadratic below the finest octave");
        Assert.AreEqual(0.0, TerrainDetail.None.MidpointBound(10.0));
    }

    [TestMethod]
    public void TheNoisesBounds_HoldWhereMeasured()
    {
        // TerrainDetail's bound rests on GradientNoise's largest value and second derivative (vista-plan §10 M1.9).
        var random = new Random(3);
        const double h = 1e-4;
        for (int i = 0; i < 200_000; i++)
        {
            double x = random.NextDouble() * 400.0 - 200.0, y = random.NextDouble() * 400.0 - 200.0, a = random.NextDouble() * 2.0 * Math.PI;
            double n = GradientNoise.Sample(99, x, y);
            Assert.IsTrue(Math.Abs(n) <= TerrainDetail.NoiseMax);
            double dx = Math.Cos(a) * h, dy = Math.Sin(a) * h;
            double plus = GradientNoise.Sample(99, x + dx, y + dy), minus = GradientNoise.Sample(99, x - dx, y - dy);
            Assert.IsTrue(Math.Abs(plus - 2.0 * n + minus) / (h * h) <= TerrainDetail.NoiseCurvatureMax);
        }
    }

    [TestMethod]
    public void TheMeasuredEnvelope_HoldsOnFreshSegments_AndNeverFalls()
    {
        // Other seeds, other places and other lengths than the envelope was measured on, between and beyond its points.
        var random = new Random(12);
        double previous = 0.0;
        for (double t = 0.01; t < 6.0; t *= 1.12)
        {
            double bound = TerrainDetail.UnitMidpointBound(t);
            Assert.IsTrue(bound >= previous, $"t = {t}: the bound fell");
            previous = bound;
            for (int i = 0; i < 20_000; i++)
            {
                ulong seed = (ulong)random.NextInt64();
                double x = random.NextDouble() * 1000.0 - 500.0, y = random.NextDouble() * 1000.0 - 500.0, a = random.NextDouble() * 2.0 * Math.PI;
                double bx = x + t * Math.Cos(a), by = y + t * Math.Sin(a);
                double off = GradientNoise.Sample(seed, 0.5 * (x + bx), 0.5 * (y + by)) - 0.5 * (GradientNoise.Sample(seed, x, y) + GradientNoise.Sample(seed, bx, by));
                Assert.IsTrue(Math.Abs(off) <= bound, $"t = {t}: {off} beyond {bound}");
            }
        }
        Assert.IsTrue(TerrainDetail.UnitMidpointBound(100.0) <= 2.0 * TerrainDetail.NoiseMax, "never beyond what two values can differ by");
    }

    [TestMethod]
    public void Constructor_RejectsNonsense()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TerrainDetail(1, 0.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TerrainDetail(1, 8.0, octaves: -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TerrainDetail(1, 8.0, amplitudeRatio: double.NaN));
    }
}
