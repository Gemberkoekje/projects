using Vista.Cli;
using Vista.World;

namespace Vista.Tests;

/// <summary>
/// The rock's structure (vista-plan §10 M1a.4): the gullies and ribs down the fall line and the ledges of the beds, as the shader
/// shades them (<see cref="RockStructure"/> is its twin; <c>--gpu-selftest</c> checks the two agree), and the switch that turns it off.
/// </summary>
[TestClass]
public sealed class RockStructureTests
{
    private const uint Seed = 0x5EEDu;
    private const float Depth = 0.09f;
    private const float Coarsest = 64f;

    /// <summary>The gullies at random places of a face whose height gradient is <c>(gx, gz)</c>, every octave whole.</summary>
    private static List<(float Height, float Gx, float Gz, float Floor)> Sample(float gx, float gz, int octaves, int count = 4000)
    {
        var random = new Random(5);
        var samples = new List<(float, float, float, float)>(count);
        for (int i = 0; i < count; i++)
            samples.Add(RockStructure.Gullies(Seed, (float)(random.NextDouble() * 4000.0 - 2000.0), (float)(random.NextDouble() * 4000.0 - 2000.0), gx, gz,
                Depth, Coarsest, octaves));
        return samples;
    }

    [TestMethod]
    public void TheGullies_RunDownTheFallLine()
    {
        // A face rising toward +x at 60°: the gullies' slope lies across it (along z), not up and down it; with every octave less so,
        // since the finer gullies run down the sides of the coarser ribs, at an angle to the face's own fall line.
        float steep = MathF.Tan(60f * MathF.PI / 180f);
        foreach ((int octaves, double most) in new[] { (1, 0.35), (5, 0.7) })
        {
            double along = 0.0, across = 0.0;
            foreach ((_, float gx, float gz, _) in Sample(steep, 0f, octaves))
            {
                along += gx * gx;
                across += gz * gz;
            }
            Assert.IsTrue(across > 0.0, "the gullies have a slope");
            Assert.IsTrue(Math.Sqrt(along / across) < most, $"{octaves} octaves: the slope along the fall line is {Math.Sqrt(along / across):F2} of the slope across it");
        }
    }

    [TestMethod]
    public void TheGullies_StandOnSteepGroundOnly_DeeperAsItSteepens_WithinTheirDepth()
    {
        Assert.IsTrue(Sample(MathF.Tan(20f * MathF.PI / 180f), 0f, 5, 500).All(s => s.Height == 0f && s.Gx == 0f && s.Gz == 0f), "none below 25°");
        double Rms(float degrees) => Math.Sqrt(Sample(MathF.Tan(degrees * MathF.PI / 180f), 0f, 5).Average(s => (double)s.Height * s.Height));
        double at45 = Rms(45f), at70 = Rms(70f);
        Assert.IsTrue(at45 > 1.0, $"metres of relief at 45° ({at45:F2} m rms)");
        // The depth is across the face, so the height grows as the slope's secant: 2.07 times from 45° to 70°.
        Assert.AreEqual(Math.Sqrt(2.0) / Math.Cos(70.0 * Math.PI / 180.0) / 2.0, at70 / at45, 0.25, "deeper on steeper faces");
        double sum = 0.0;
        for (int k = 0; k < 5; k++)
            sum += Depth * Coarsest / (1 << k) * RockStructure.SecMax * (4.0 / 3.0);
        List<(float Height, float Gx, float Gz, float Floor)> steep = Sample(MathF.Tan(80f * MathF.PI / 180f), 0f, 5);
        Assert.IsTrue(steep.All(s => Math.Abs(s.Height) <= sum), "within the octaves' largest heights");
        double mean = steep.Average(s => s.Height), rms = Math.Sqrt(steep.Average(s => (double)s.Height * s.Height));
        // The ribs stand a little proud on average (a quarter of the relief, measured); the shading reads only the relief's slopes and
        // its heights against each other, so a level offset changes nothing.
        Assert.IsTrue(Math.Abs(mean) < 0.35 * rms, $"about level on average: mean {mean:F2} m against {rms:F2} m rms");
        Assert.IsTrue(steep.All(s => s.Floor is >= 0f and <= 1f) && steep.Any(s => s.Floor > 0.8f) && steep.Any(s => s.Floor < 0.2f), "ribs and floors");
    }

    [TestMethod]
    public void TheGullies_SlopeIsTheirHeightsOwn_WhereTheyHoldStill()
    {
        // One octave on a plane: the analytic slope leaves out how the stripes' contrast changes, so it matches the height's own slope
        // (central differences) where the octave is at its full depth, which is most places.
        float steep = 2f;
        var random = new Random(9);
        var errors = new List<double>();
        for (int i = 0; i < 3000; i++)
        {
            float x = (float)(random.NextDouble() * 2000.0 - 1000.0), z = (float)(random.NextDouble() * 2000.0 - 1000.0);
            (_, float gx, float gz, _) = RockStructure.Gullies(Seed, x, z, steep, 0f, Depth, Coarsest, 1);
            const float H = 0.05f;
            float dx = (RockStructure.Gullies(Seed, x + H, z, steep, 0f, Depth, Coarsest, 1).Height - RockStructure.Gullies(Seed, x - H, z, steep, 0f, Depth, Coarsest, 1).Height) / (2f * H);
            float dz = (RockStructure.Gullies(Seed, x, z + H, steep, 0f, Depth, Coarsest, 1).Height - RockStructure.Gullies(Seed, x, z - H, steep, 0f, Depth, Coarsest, 1).Height) / (2f * H);
            errors.Add(Math.Sqrt((gx - dx) * (gx - dx) + (gz - dz) * (gz - dz)) / (0.1 + Math.Sqrt(dx * dx + dz * dz)));
        }
        errors.Sort();
        Assert.IsTrue(errors[errors.Count / 2] < 0.05, $"median error {errors[errors.Count / 2]:F3}");
    }

    [TestMethod]
    public void TheProfile_IsARibAndARoundedFloor()
    {
        (float rib, float ribSlope, float ribT) = RockStructure.Profile(0f);
        (float floor, float floorSlope, _) = RockStructure.Profile(MathF.PI);
        Assert.AreEqual(4f / 3f, rib, 1e-5f);
        Assert.AreEqual(1f, ribT, 1e-6f);
        Assert.AreEqual(0f, ribSlope, 1e-6f, "rounded at the crest");
        Assert.AreEqual(-2f / 3f, floor, 1e-5f);
        Assert.AreEqual(0f, floorSlope, 1e-5f, "flat at the floor");
        Assert.AreEqual(RockStructure.Profile(-1f).Profile, RockStructure.Profile(1f).Profile, "symmetric");
        Assert.IsTrue(RockStructure.Profile(1f).Slope < 0f && RockStructure.Profile(-1f).Slope > 0f, "falling away from the rib on both sides");
    }

    [TestMethod]
    public void TheLedges_SteepenHardBeds_FlattenSoftOnes_AndNeverDrift()
    {
        // Beds 10 m thick (five entries of 2 m), hard and soft by turns, over the whole table.
        const int Entries = RockHardness.TableEntries;
        var beds = new float[4 * Entries];
        for (int e = 0; e < Entries; e++)
            beds[4 * e] = e / 5 % 2 == 0 ? 0.85f : 0.25f;
        float[] table = RockStructure.LedgeTable(beds, RockHardness.TableSpacingMetres, 2.5f, 0.7f, Seed);
        Assert.AreEqual(Entries, table.Length);
        double hardSlope = 0.0, softSlope = 0.0, highest = 0.0;
        int hard = 0, soft = 0;
        for (int e = 2; e < Entries - 3; e++)
        {
            float t = e + 0.5f;
            (float g, float slope) = RockStructure.LedgeAt(table, Entries, t);
            highest = Math.Max(highest, Math.Abs(g));
            if (beds[4 * e] > 0.5f && beds[4 * e + 4] > 0.5f)
            {
                hardSlope += slope;
                hard++;
            }
            else if (beds[4 * e] < 0.5f && beds[4 * e + 4] < 0.5f)
            {
                softSlope += slope;
                soft++;
            }
        }
        Assert.IsTrue(hardSlope / hard > 0.3, $"hard beds stand steeper: {hardSlope / hard:F2} m an entry");
        Assert.IsTrue(softSlope / soft < -0.3, $"soft beds recede: {softSlope / soft:F2} m an entry");
        Assert.IsTrue(highest < 6.0, $"the ledges' height stays within metres over {Entries} entries: {highest:F2} m");
    }

    [TestMethod]
    public void LedgeAt_IsASpline_WhoseSlopeIsItsHeightsDerivative()
    {
        var random = new Random(3);
        var beds = new float[4 * 4096];
        for (int e = 0; e < 4096; e++)
            beds[4 * e] = (float)random.NextDouble();
        float[] table = RockStructure.LedgeTable(beds, 2f, 2.5f, 0.7f, Seed);
        for (int i = 0; i < 2000; i++)
        {
            // Inside an entry's piece, where the spline is one cubic (its curvature jumps at the entries, by design).
            float t = 3f + random.Next(4000) + 0.1f + 0.8f * (float)random.NextDouble();
            (float g, float slope) = RockStructure.LedgeAt(table, 4096, t);
            const float H = 1e-2f;
            float derivative = (RockStructure.LedgeAt(table, 4096, t + H).Height - RockStructure.LedgeAt(table, 4096, t - H).Height) / (2f * H);
            Assert.AreEqual(derivative, slope, 2e-3f + 2e-3f * Math.Abs(slope), $"t = {t}");
            Assert.IsTrue(float.IsFinite(g));
        }
        Assert.AreEqual(table[100], RockStructure.LedgeAt(table, 4096, 100f).Height, 1e-5f, "through the entries");
    }

    [TestMethod]
    public void TheSwitch_TurnsTheStructureOff_AndTheRulesCheckItsSettings()
    {
        string[] args = ["--preview", "--recipe", "r.json", "--seed", "1", "--out", "p.png"];
        Assert.IsTrue(PreviewCommandLine.TryParse([.. args, "--rock-structure", "off"], out PreviewRequest? off, out IReadOnlyList<string> errors), string.Join(" ", errors));
        Assert.IsFalse(off!.RockStructure);
        Assert.IsTrue(PreviewCommandLine.TryParse(args, out PreviewRequest? on, out errors), string.Join(" ", errors));
        Assert.IsTrue(on!.RockStructure, "on by default");
        Assert.IsFalse(PreviewCommandLine.TryParse([.. args, "--rock-structure", "maybe"], out _, out errors));
        StringAssert.Contains(string.Join(" ", errors), "--rock-structure");
        Assert.AreEqual(0, MaterialRules.Default.Validate().Count);
        Assert.IsTrue((MaterialRules.Default with { StructureDepth = 0.6f }).Validate().Count > 0);
        Assert.IsTrue((MaterialRules.Default with { LedgeMax = 1.2f }).Validate().Count > 0);
    }
}
