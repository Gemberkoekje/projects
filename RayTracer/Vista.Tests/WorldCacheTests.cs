using Vista.Recipes;
using Vista.Scene;
using Vista.World;

namespace Vista.Tests;

/// <summary>
/// The one-world cache a contact sheet's cells and a watch session share (M1.1, M1.8), keyed since M1.7 by the grid and the erosion
/// too; since M1.9 it keeps the world's hierarchy and its last view-dependent mesh, keyed by the view.
/// </summary>
[TestClass]
public sealed class WorldCacheTests
{
    private static readonly WorldGrid Small = new(Size: 65, CellSize: 64.0);

    private static MeshView View(double yaw = 0.0, double y = 3000.0, int width = 320, int height = 180) =>
        MeshView.For(new CameraSpec { Position = new WorldPoint(100.0, y, -300.0), YawDegrees = yaw, PitchDegrees = -10.0 }, width, height, 6_361_000.0);

    [TestMethod]
    public void SameProfileAndSeed_ReusesTheWorldAndItsMesh()
    {
        var cache = new WorldCache();
        var day = new Recipe { SetPieces = [SetPiece.AlpineRange], Time = TimeOfDay.Day };
        TerrainWorld a = cache.World(day, 7, Small, erode: false);
        TerrainMesh mesh = cache.Mesh(View(), new MeshSettings());

        // Time, season and the other mountain piece share the mountains profile, so the world stays.
        TerrainWorld b = cache.World(day with { Time = TimeOfDay.Dusk, Season = Season.Winter, SetPieces = [SetPiece.SnowCappedMountains] }, 7, Small, erode: false);
        Assert.AreSame(a, b);
        Assert.AreSame(mesh, cache.Mesh(View(), new MeshSettings()));
        Assert.AreEqual(TerrainGenerator.Generate(day, 7, Small).ContentHash(), a.Surface.ContentHash(), "the cached world is the generated one");
    }

    [TestMethod]
    public void AnotherSeedProfileGridOrErosion_BuildsANewWorld()
    {
        var cache = new WorldCache();
        var mountains = new Recipe { SetPieces = [SetPiece.AlpineRange] };
        TerrainWorld a = cache.World(mountains, 7, Small, erode: false);
        TerrainMesh mesh = cache.Mesh(View(), new MeshSettings());
        TerrainWorld b = cache.World(mountains, 8, Small, erode: false);
        Assert.AreNotSame(a, b);
        Assert.AreNotSame(mesh, cache.Mesh(View(), new MeshSettings()));
        Assert.AreNotSame(b, cache.World(new Recipe { SetPieces = [SetPiece.RollingHills] }, 8, Small, erode: false));
        Assert.AreNotSame(b, cache.World(mountains, 8, new WorldGrid(33, 128.0), erode: false));

        TerrainWorld uneroded = cache.World(mountains, 8, Small, erode: true);
        Assert.AreEqual(ErosionSource.Uneroded, uneroded.Info.Source, "nothing to erode with and no cache");
        TerrainWorld eroded = cache.World(mountains, 8, Small, new ReferenceEroder(), erode: true);
        Assert.AreNotSame(uneroded, eroded, "an eroder makes another world");
        Assert.AreEqual(ErosionSource.Eroded, eroded.Info.Source);
    }

    [TestMethod]
    public void TheMesh_FollowsTheView_AndTheHierarchyStaysWithTheWorld()
    {
        var cache = new WorldCache();
        cache.World(new Recipe { SetPieces = [SetPiece.AlpineRange] }, 7, Small, erode: false);
        TerrainMesh first = cache.Mesh(View(), new MeshSettings(), out bool reused);
        Assert.IsFalse(reused);
        Assert.IsTrue(first.Info.HierarchySeconds > 0.0, "the first mesh built the world's hierarchy");

        foreach ((string change, MeshView view, MeshSettings settings) in new[]
        {
            ("a turn", View(yaw: 40.0), new MeshSettings()),
            ("a move", View(y: 3500.0), new MeshSettings()),
            ("another frame", View(width: 640, height: 360), new MeshSettings()),
            ("another target", View(), new MeshSettings { ErrorPixels = 2.0 }),
            ("another ground sphere", View() with { GroundRadius = 6_362_000.0 }, new MeshSettings()),
        })
        {
            TerrainMesh mesh = cache.Mesh(view, settings, out reused);
            Assert.IsFalse(reused, change);
            Assert.AreEqual(0.0, mesh.Info.HierarchySeconds, $"{change}: the hierarchy is kept");
        }
        cache.Mesh(View(), new MeshSettings { Detail = false }, out _);
        TerrainMesh again = cache.Mesh(View(), new MeshSettings(), out reused);
        Assert.IsFalse(reused);
        Assert.IsTrue(again.Info.HierarchySeconds > 0.0, "turning the detail off and on again rebuilds the hierarchy");
        Assert.AreSame(again, cache.Mesh(View(), new MeshSettings(), out reused));
        Assert.IsTrue(reused);
    }

    [TestMethod]
    public void TheMesh_NeedsItsWorldFirst() =>
        Assert.ThrowsExactly<InvalidOperationException>(() => new WorldCache().Mesh(View(), new MeshSettings()));
}
