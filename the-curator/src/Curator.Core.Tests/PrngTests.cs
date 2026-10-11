using Curator.Core.Game;

namespace Curator.Core.Tests;

public sealed class PrngTests
{
    [Fact]
    public void SameInputsGiveTheSameDraw()
    {
        Assert.Equal(Prng.Draw(42, 7, 1), Prng.Draw(42, 7, 1));
    }

    [Fact]
    public void SeedSequenceAndSaltEachChangeTheDraw()
    {
        var baseline = Prng.Draw(42, 7, 1);

        Assert.NotEqual(baseline, Prng.Draw(43, 7, 1));
        Assert.NotEqual(baseline, Prng.Draw(42, 8, 1));
        Assert.NotEqual(baseline, Prng.Draw(42, 7, 2));
    }

    [Fact]
    public void NextIntStaysInRangeAndCoversIt()
    {
        var seen = new HashSet<int>();
        for (var sequence = 0; sequence < 2000; sequence++)
        {
            var value = Prng.NextInt(1, sequence, 7);
            Assert.InRange(value, 0, 6);
            seen.Add(value);
        }

        Assert.Equal(7, seen.Count);
    }

    [Fact]
    public void NextIntIsRoughlyUniform()
    {
        var counts = new int[4];
        for (var sequence = 0; sequence < 40000; sequence++)
        {
            counts[Prng.NextInt(99, sequence, 4)]++;
        }

        Assert.All(counts, count => Assert.InRange(count, 9500, 10500));
    }

    [Fact]
    public void NextDoubleStaysInUnitInterval()
    {
        for (var sequence = 0; sequence < 2000; sequence++)
        {
            Assert.InRange(Prng.NextDouble(5, sequence), 0.0, 0.9999999999);
        }
    }

    [Fact]
    public void NextIntRejectsAnEmptyRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Prng.NextInt(1, 1, 0));
    }
}
