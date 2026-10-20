using Vista.Recipes;
using Vista.World;

namespace Vista.Tests;

/// <summary>The one-world cache a contact sheet's cells share (M1.1), keyed since M1.7 by the grid and the erosion too.</summary>
[TestClass]
public sealed class WorldCacheTests
{
    private static readonly WorldGrid Small = new(Size: 65, CellSize: 64.0);

    [TestMethod]
    public void SameProfileAndSeed_ReusesTheWorldAndItsMesh()
    {
        var cache = new WorldCache();
        var day = new Recipe { SetPieces = [SetPiece.AlpineRange], Time = TimeOfDay.Day };
        TerrainWorld a = cache.World(day, 7, Small, erode: false);
        TerrainMesh mesh = cache.Mesh(a.Surface);

        // Time, season and the other mountain piece share the mountains profile, so the world stays.
        TerrainWorld b = cache.World(day with { Time = TimeOfDay.Dusk, Season = Season.Winter, SetPieces = [SetPiece.SnowCappedMountains] }, 7, Small, erode: false);
        Assert.AreSame(a, b);
        Assert.AreSame(mesh, cache.Mesh(b.Surface));
        Assert.AreEqual(TerrainGenerator.Generate(day, 7, Small).ContentHash(), a.Surface.ContentHash(), "the cached world is the generated one");
    }

    [TestMethod]
    public void AnotherSeedProfileGridOrErosion_BuildsANewWorld()
    {
        var cache = new WorldCache();
        var mountains = new Recipe { SetPieces = [SetPiece.AlpineRange] };
        TerrainWorld a = cache.World(mountains, 7, Small, erode: false);
        TerrainMesh mesh = cache.Mesh(a.Surface);
        TerrainWorld b = cache.World(mountains, 8, Small, erode: false);
        Assert.AreNotSame(a, b);
        Assert.AreNotSame(mesh, cache.Mesh(b.Surface));
        Assert.AreNotSame(b, cache.World(new Recipe { SetPieces = [SetPiece.RollingHills] }, 8, Small, erode: false));
        Assert.AreNotSame(b, cache.World(mountains, 8, new WorldGrid(33, 128.0), erode: false));

        TerrainWorld uneroded = cache.World(mountains, 8, Small, erode: true);
        Assert.AreEqual(ErosionSource.Uneroded, uneroded.Info.Source, "nothing to erode with and no cache");
        TerrainWorld eroded = cache.World(mountains, 8, Small, new ReferenceEroder(), erode: true);
        Assert.AreNotSame(uneroded, eroded, "an eroder makes another world");
        Assert.AreEqual(ErosionSource.Eroded, eroded.Info.Source);
    }

    [TestMethod]
    public void Mesh_OfAFieldItDidNotMake_IsBuiltFresh()
    {
        var cache = new WorldCache();
        HeightField own = cache.World(new Recipe(), 1, Small, erode: false).Surface;
        HeightField other = TerrainGenerator.Generate(new Recipe(), 1, Small);
        TerrainMesh mesh = cache.Mesh(other);
        Assert.AreEqual(Small.Size, other.Size);
        Assert.AreNotSame(mesh, cache.Mesh(own));
        Assert.AreNotSame(mesh, cache.Mesh(other), "a foreign field's mesh is not cached");
    }
}
