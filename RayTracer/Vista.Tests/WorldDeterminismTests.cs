using Vista.Recipes;
using Vista.World;

namespace Vista.Tests;

/// <summary>
/// "The world is a function of (recipe, seed)" (vista-plan pillar 4, §4.1). The hashes, noise and terrain
/// are pinned so any change to world generation is a deliberate, visible re-baseline — the same role the
/// maze goldens play for the renderer.
/// </summary>
[TestClass]
public sealed class WorldDeterminismTests
{
    private static readonly WorldGrid SmallGrid = new(65, 32.0);

    private static Recipe Mountains(TimeOfDay time = TimeOfDay.Day, Season season = Season.Summer) =>
        new() { SetPieces = [SetPiece.AlpineRange], Time = time, Season = season };

    [TestMethod]
    public void StableHash_MatchesReferenceVectors()
    {
        // splitmix64's first output from state 0, and the published FNV-1a 64 test vectors.
        Assert.AreEqual(0xE220A8397B1DCDAFul, StableHash.Mix(0));
        Assert.AreEqual(0xcbf29ce484222325ul, StableHash.Of(""));
        Assert.AreEqual(0xaf63dc4c8601ec8cul, StableHash.Of("a"));
        Assert.AreEqual(0x85944171f73967e8ul, StableHash.Of("foobar"));
    }

    [TestMethod]
    public void SeedDerivation_SeparatesStagesAndSeeds()
    {
        ulong terrain = SeedDerivation.Derive(42, SeedDerivation.TerrainBase);
        Assert.AreEqual(terrain, SeedDerivation.Derive(42, SeedDerivation.TerrainBase));
        Assert.AreNotEqual(terrain, SeedDerivation.Derive(43, SeedDerivation.TerrainBase));
        Assert.AreNotEqual(terrain, SeedDerivation.Derive(42, "terrain.erosion"));
    }

    [TestMethod]
    public void GradientNoise_IsZeroOnTheLatticeAndBounded()
    {
        for (int i = -5; i <= 5; i++)
            Assert.AreEqual(0.0, GradientNoise.Sample(7, i, -3 * i));

        double min = double.MaxValue, max = double.MinValue;
        for (int k = 0; k < 20000; k++)
        {
            double n = GradientNoise.Sample(7, k * 0.0371, k * 0.0193 - 40.0);
            min = Math.Min(min, n);
            max = Math.Max(max, n);
        }
        Assert.IsTrue(min >= -1.0 && max <= 1.0, $"range [{min}, {max}]");
        Assert.IsTrue(min < -0.4 && max > 0.4, $"range [{min}, {max}] should use most of [-1, 1]");
    }

    [TestMethod]
    public void GradientNoise_IsPinned()
    {
        // Change-detector: if this moves, every world moves. Re-baseline only on purpose.
        Assert.AreEqual(PinnedNoise, GradientNoise.Sample(12345, 3.25, -7.75));
    }

    private const double PinnedNoise = 0.2647657356251159;

    [TestMethod]
    public void Terrain_SameRecipeAndSeed_IsBitIdentical()
    {
        HeightField a = TerrainGenerator.Generate(Mountains(), 42, SmallGrid);
        HeightField b = TerrainGenerator.Generate(Mountains(), 42, SmallGrid);
        Assert.AreEqual(a.ContentHash(), b.ContentHash());
        CollectionAssert.AreEqual(a.Heights.ToArray(), b.Heights.ToArray());
    }

    [TestMethod]
    public void Terrain_IsPinned()
    {
        Assert.AreEqual(PinnedMountainHash, TerrainGenerator.Generate(Mountains(), 42, SmallGrid).ContentHash());
        Assert.AreEqual(PinnedHillsHash, TerrainGenerator.Generate(new Recipe { SetPieces = [SetPiece.RollingHills] }, 42, SmallGrid).ContentHash());
    }

    private const ulong PinnedMountainHash = 0x2e210d0a143d0cb6ul;
    private const ulong PinnedHillsHash = 0xcd6ee97d0e940547ul;

    [TestMethod]
    public void Terrain_DoesNotMoveWithTimeSeasonOrName()
    {
        // vista-plan §3.4: changing the light or the season must never reshuffle the mountains.
        ulong reference = TerrainGenerator.Generate(Mountains(), 42, SmallGrid).ContentHash();
        Assert.AreEqual(reference, TerrainGenerator.Generate(Mountains(TimeOfDay.Dusk), 42, SmallGrid).ContentHash());
        Assert.AreEqual(reference, TerrainGenerator.Generate(Mountains(season: Season.Winter), 42, SmallGrid).ContentHash());
        Assert.AreEqual(reference, TerrainGenerator.Generate(Mountains() with { Name = "renamed" }, 42, SmallGrid).ContentHash());
    }

    [TestMethod]
    public void SetPieceOrder_DoesNotMatter()
    {
        // The set pieces are a set. (Adding or removing a piece starts a new scene — vista-plan §3.4 — so no
        // stability is asserted there; once lakes carve basins in M3 it would not hold anyway.)
        ulong reference = TerrainGenerator.Generate(
            new Recipe { SetPieces = [SetPiece.SnowCappedMountains, SetPiece.Forest, SetPiece.Lake] }, 42, SmallGrid).ContentHash();
        Assert.AreEqual(reference, TerrainGenerator.Generate(
            new Recipe { SetPieces = [SetPiece.Lake, SetPiece.Forest, SetPiece.SnowCappedMountains] }, 42, SmallGrid).ContentHash());
        Assert.AreEqual(reference, TerrainGenerator.Generate(
            new Recipe { SetPieces = [SetPiece.Forest, SetPiece.SnowCappedMountains, SetPiece.Lake] }, 42, SmallGrid).ContentHash());
    }

    [TestMethod]
    public void EmptySet_IsPlainBaseGround()
    {
        ulong hills = TerrainGenerator.Generate(new Recipe { SetPieces = [SetPiece.RollingHills] }, 42, SmallGrid).ContentHash();
        Assert.AreEqual(hills, TerrainGenerator.Generate(new Recipe(), 42, SmallGrid).ContentHash());
    }

    [TestMethod]
    public void Terrain_ChangesWithSeedAndTerrainShapingPieces()
    {
        ulong reference = TerrainGenerator.Generate(Mountains(), 42, SmallGrid).ContentHash();
        Assert.AreNotEqual(reference, TerrainGenerator.Generate(Mountains(), 43, SmallGrid).ContentHash());
        Assert.AreNotEqual(reference, TerrainGenerator.Generate(new Recipe { SetPieces = [SetPiece.RollingHills] }, 42, SmallGrid).ContentHash());
    }

    [TestMethod]
    public void Terrain_IsAPureFunctionOfPosition()
    {
        // The parallel grid fill equals point-wise evaluation, and a sample means the same place on any grid
        // that contains it — the camera can never change what is there (vista-plan §4.1).
        Recipe recipe = Mountains();
        HeightField field = TerrainGenerator.Generate(recipe, 9, SmallGrid);
        ulong seed = SeedDerivation.Derive(9, SeedDerivation.TerrainBase);
        foreach ((int i, int j) in new[] { (0, 0), (17, 40), (64, 64), (32, 32) })
            Assert.AreEqual((float)TerrainProfile.Mountains.Height(seed, field.XOf(i), field.ZOf(j)), field[i, j]);

        HeightField coarse = TerrainGenerator.Generate(recipe, 9, new WorldGrid(33, 64.0)); // same extent, every other sample
        for (int j = 0; j < 33; j++)
        {
            for (int i = 0; i < 33; i++)
                Assert.AreEqual(field[i * 2, j * 2], coarse[i, j]);
        }
    }

    [TestMethod]
    public void Terrain_StaysWithinTheProfileRelief()
    {
        HeightField field = TerrainGenerator.Generate(Mountains(), 1, SmallGrid);
        Assert.IsTrue(field.MinHeight >= 0f);
        Assert.IsTrue(field.MaxHeight <= TerrainProfile.Mountains.ReliefMetres);
        Assert.IsTrue(field.MaxHeight - field.MinHeight > 200f, "mountains should have real relief");
    }

    [TestMethod]
    public void Terrain_HasNoStraightCreaseAlongTheCoarseLattice()
    {
        // Regression: with axis-aligned gradients and no domain warp, ridged noise drew a ruler-straight
        // crest along the coarsest octave's lattice line z = 0 (the centre row of the default grid): its mean
        // curvature was ~60% above the neighbouring rows. It must now look like any other row.
        HeightField field = TerrainGenerator.Generate(Mountains(), 42);
        int centre = (field.Size - 1) / 2;
        Assert.AreEqual(0.0, field.ZOf(centre));
        double Curvature(int j)
        {
            double sum = 0.0;
            for (int i = 0; i < field.Size; i++)
                sum += Math.Abs(field[i, j + 1] - 2.0 * field[i, j] + field[i, j - 1]);
            return sum / field.Size;
        }
        double neighbours = Enumerable.Range(1, 8).Average(d => (Curvature(centre - d) + Curvature(centre + d)) / 2.0);
        Assert.IsTrue(Curvature(centre) < neighbours * 1.25, $"centre {Curvature(centre)} vs neighbours {neighbours}");
    }

    [TestMethod]
    public void TerrainProfile_MountainsWinOverLowerPieces()
    {
        Assert.AreSame(TerrainProfile.Mountains, TerrainProfile.For([SetPiece.Forest, SetPiece.SnowCappedMountains]));
        Assert.AreSame(TerrainProfile.Hills, TerrainProfile.For([SetPiece.Coast, SetPiece.Forest]));
        Assert.AreSame(TerrainProfile.Hills, TerrainProfile.For([]));
    }
}
