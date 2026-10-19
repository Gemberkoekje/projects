using Vista.Recipes;
using Vista.World;

namespace Vista.Tests;

/// <summary>The one-world cache a contact sheet's cells share (M1.1).</summary>
[TestClass]
public sealed class WorldCacheTests
{
    private static readonly WorldGrid Small = new(Size: 65, CellSize: 64.0);

    [TestMethod]
    public void SameProfileAndSeed_ReusesTheWorldAndItsMesh()
    {
        var cache = new WorldCache();
        var day = new Recipe { SetPieces = [SetPiece.AlpineRange], Time = TimeOfDay.Day };
        HeightField a = cache.Terrain(day, 7);
        TerrainMesh mesh = cache.Mesh(a);

        // Time, season and the other mountain piece share the mountains profile, so the world stays.
        HeightField b = cache.Terrain(day with { Time = TimeOfDay.Dusk, Season = Season.Winter, SetPieces = [SetPiece.SnowCappedMountains] }, 7);
        Assert.AreSame(a, b);
        Assert.AreSame(mesh, cache.Mesh(b));
        Assert.AreEqual(TerrainGenerator.Generate(day, 7).ContentHash(), a.ContentHash(), "the cached world is the generated one");
    }

    [TestMethod]
    public void AnotherSeedOrProfile_GeneratesANewWorld()
    {
        var cache = new WorldCache();
        var mountains = new Recipe { SetPieces = [SetPiece.AlpineRange] };
        HeightField a = cache.Terrain(mountains, 7);
        TerrainMesh mesh = cache.Mesh(a);
        HeightField b = cache.Terrain(mountains, 8);
        Assert.AreNotSame(a, b);
        Assert.AreNotSame(mesh, cache.Mesh(b));
        Assert.AreNotSame(b, cache.Terrain(new Recipe { SetPieces = [SetPiece.RollingHills] }, 8));
    }

    [TestMethod]
    public void Mesh_OfAFieldItDidNotMake_IsBuiltFresh()
    {
        var cache = new WorldCache();
        HeightField own = cache.Terrain(new Recipe(), 1);
        HeightField other = TerrainGenerator.Generate(new Recipe(), 1, Small);
        TerrainMesh mesh = cache.Mesh(other);
        Assert.AreEqual(Small.Size, other.Size);
        Assert.AreNotSame(mesh, cache.Mesh(own));
        Assert.AreNotSame(mesh, cache.Mesh(other), "a foreign field's mesh is not cached");
    }
}
