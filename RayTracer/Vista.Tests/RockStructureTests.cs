using Vista.World;

namespace Vista.Tests;

/// <summary>
/// The rock's structure (vista-plan §10 M1a.4): the gullies and ribs down the fall line and the ledges of the beds, and how the mesh
/// carries and bounds them.
/// </summary>
[TestClass]
public sealed class RockStructureTests
{
    [TestMethod]
    [TestCategory("Measure")]
    public void MeasureTheGullyEnvelope()
    {
        double[] lengths = [0.00390625, 0.0078125, 0.015625, 0.03125, 0.0625, 0.125, 0.25, 0.5, 1.0, 2.0, 4.0];
        foreach (double t in lengths)
        {
            var random = new Random(77);
            var off = new double[200_000];
            for (int i = 0; i < off.Length; i++)
            {
                ulong seed = (ulong)random.NextInt64();
                double x = random.NextDouble() * 1000.0 - 500.0, y = random.NextDouble() * 1000.0 - 500.0, a = random.NextDouble() * 2.0 * Math.PI;
                double bx = x + t * Math.Cos(a), by = y + t * Math.Sin(a);
                off[i] = Math.Abs(RockStructure.Unit(seed, 0.5 * (x + bx), 0.5 * (y + by)) - 0.5 * (RockStructure.Unit(seed, x, y) + RockStructure.Unit(seed, bx, by)));
            }
            Array.Sort(off);
            Console.WriteLine(FormattableString.Invariant(
                $"envelope {t}, p99 {off[(int)(0.99 * off.Length)]:F5}, p999 {off[(int)(0.999 * off.Length)]:F5}, max {off[^1]:F5}"));
        }
    }
}
