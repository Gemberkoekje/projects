using Vista.Recipes;
using Vista.Scene;
using Vista.World;

namespace Vista.Tests;

/// <summary>
/// The far field as height-field tiles (vista-plan §10 M1.10) on a small world: the grids hold the surface the mesh refines, the
/// tiles cover the far field once out to the reach, each box holds its tile bent onto the Earth, each local frame is orthonormal and
/// its grid close to the bent samples, the mesh beside them is the world grid's alone, and the same bits come out every time.
/// </summary>
[TestClass]
public sealed class FarHeightFieldTests
{
    private const double Radius = 6_361_000.0;

    /// <summary>The small world's inner squares are 24 and 28 cells from the edge, so its tiles are 4² (8 does not divide 28).</summary>
    private const int Tile = 4;

    private static readonly Lazy<(HeightField Field, TerrainHierarchy Hierarchy)> World = new(() =>
    {
        HeightField field = TerrainGenerator.Generate(new Recipe { SetPieces = [SetPiece.AlpineRange] }, 9, TestWorlds.Small);
        return (field, TerrainHierarchy.Build(field, TerrainProfile.Mountains, 9, new TerrainDetail(9, field.CellSize)));
    });

    private static CameraSpec Camera(double x, double above, double z, double yaw, double pitch)
    {
        HeightField field = World.Value.Field;
        return new CameraSpec { Position = new WorldPoint(x, field.HeightAt(x, z) + above, z), YawDegrees = yaw, PitchDegrees = pitch, FocalLengthMm = 28.0 };
    }

    private static IEnumerable<CameraSpec> Cameras()
    {
        yield return Camera(0.0, 5.0, 0.0, 0.0, -5.0);
        yield return Camera(-1000.0, 1500.0, -1500.0, 30.0, -20.0);
        yield return Camera(1950.0, 10.0, 100.0, 90.0, 0.0);
    }

    private static MeshSettings Settings => new() { HeightField = true, HeightFieldTileCells = Tile };

    private static TerrainMesh Build(CameraSpec camera, MeshSettings settings) =>
        ViewMesher.Build(World.Value.Hierarchy, MeshView.For(camera, 640, 360, Radius), settings);

    [TestMethod]
    public void Grids_HoldTheSurfaceTheMeshRefines()
    {
        TerrainHierarchy h = World.Value.Hierarchy;
        HeightFieldGrids grids = h.FarGrids;
        int step1 = TerrainHierarchy.Ratio1 * TerrainHierarchy.Sub, step2 = TerrainHierarchy.Ratio2 * TerrainHierarchy.Sub;
        for (int j = 0; j < grids.Size; j++)
        {
            for (int i = 0; i < grids.Size; i++)
            {
                Assert.AreEqual(h.BaseHeight(h.Grid1Low + i * step1, h.Grid1Low + j * step1), grids.Level1[j * grids.Size + i], $"g1 ({i}, {j})");
                Assert.AreEqual(h.BaseHeight(i * step2, j * step2), grids.Level2[j * grids.Size + i], $"g2 ({i}, {j})");
            }
        }
        Assert.AreEqual(h.WorldOf(h.Grid1Low), grids.Origin1, 1e-9);
        Assert.AreEqual(h.WorldOf(0), grids.Origin2, 1e-9);
        Assert.AreEqual(TerrainHierarchy.Ratio2 * h.Surface.CellSize, grids.Cell2, 1e-12);
    }

    [TestMethod]
    public void Tiles_CoverTheFarFieldOnce_OutToTheReach()
    {
        foreach (CameraSpec camera in Cameras())
        {
            TerrainMesh mesh = Build(camera, Settings);
            FarHeightField field = mesh.HeightField;
            HeightFieldGrids grids = field.Grids;
            int perSide = (grids.Size - 1) / Tile;
            var seen = new HashSet<(uint Level, uint I, uint J)>();
            for (int k = 0; k < field.TileCount; k++)
            {
                uint packed = BitConverter.SingleToUInt32Bits(field.Tiles[k * FarHeightField.TileStride + 3]);
                (uint level, uint i, uint j) = (packed >> 26, packed & 0x1FFF, (packed >> 13) & 0x1FFF);
                Assert.IsTrue(seen.Add((level, i, j)), $"tile {k} twice");
                Assert.AreEqual(level == 0, k < field.Level1Tiles, "g1's tiles come first");
            }
            for (uint level = 0; level < 2; level++)
            {
                int cells = grids.Size - 1;
                int innerLow = (level == 0 ? cells * 3 / 8 : cells * 7 / 16) / Tile, innerHigh = (level == 0 ? cells * 5 / 8 : cells * 9 / 16) / Tile;
                double cell = grids.Cell((int)level), width = Tile * cell;
                for (int tj = 0; tj < perSide; tj++)
                {
                    for (int ti = 0; ti < perSide; ti++)
                    {
                        bool inner = ti >= innerLow && ti < innerHigh && tj >= innerLow && tj < innerHigh;
                        double x0 = grids.Origin((int)level) + ti * width, z0 = grids.Origin((int)level) + tj * width;
                        double dx = Math.Max(Math.Max(x0 - camera.Position.X, camera.Position.X - (x0 + width)), 0.0);
                        double dz = Math.Max(Math.Max(z0 - camera.Position.Z, camera.Position.Z - (z0 + width)), 0.0);
                        bool within = Math.Sqrt(dx * dx + dz * dz) <= mesh.ReachMetres;
                        Assert.AreEqual(!inner && within, seen.Contains((level, (uint)(ti * Tile), (uint)(tj * Tile))), $"level {level} tile ({ti}, {tj})");
                    }
                }
            }
        }
    }

    [TestMethod]
    public void EveryBox_HoldsItsTileBentOntoTheEarth()
    {
        foreach (CameraSpec camera in Cameras())
        {
            TerrainMesh mesh = Build(camera, Settings);
            FarHeightField field = mesh.HeightField;
            HeightFieldGrids grids = field.Grids;
            for (int k = 0; k < field.TileCount; k++)
            {
                (int level, int i0, int j0) = Unpack(field, k);
                float[] heights = grids.Heights(level);
                for (int b = 0; b <= Tile; b++)
                {
                    for (int a = 0; a <= Tile; a++)
                    {
                        double x = grids.Origin(level) + (i0 + a) * grids.Cell(level), z = grids.Origin(level) + (j0 + b) * grids.Cell(level);
                        Vector3D p = mesh.Frame.ToFrame(x, heights[(j0 + b) * grids.Size + i0 + a], z);
                        int box = k * FarHeightField.AabbStride;
                        Assert.IsTrue(p.X >= field.Aabbs[box] && p.Y >= field.Aabbs[box + 1] && p.Z >= field.Aabbs[box + 2]
                            && p.X <= field.Aabbs[box + 3] && p.Y <= field.Aabbs[box + 4] && p.Z <= field.Aabbs[box + 5], $"tile {k}, sample ({a}, {b})");
                    }
                }
            }
        }
    }

    [TestMethod]
    public void LocalFrames_AreOrthonormal_AndTheirGridsWithinTheMargin()
    {
        foreach (CameraSpec camera in Cameras())
        {
            TerrainMesh mesh = Build(camera, Settings);
            FarHeightField field = mesh.HeightField;
            HeightFieldGrids grids = field.Grids;
            double worst = 0.0;
            for (int k = 0; k < field.TileCount; k++)
            {
                int t = k * FarHeightField.TileStride;
                var o = new Vector3D(field.Tiles[t], field.Tiles[t + 1], field.Tiles[t + 2]);
                var u = new Vector3D(field.Tiles[t + 4], field.Tiles[t + 5], field.Tiles[t + 6]);
                var w = new Vector3D(field.Tiles[t + 8], field.Tiles[t + 9], field.Tiles[t + 10]);
                Assert.AreEqual(1.0, u.Length(), 1e-6);
                Assert.AreEqual(1.0, w.Length(), 1e-6);
                Assert.AreEqual(0.0, Vector3D.Dot(u, w), 1e-6);
                Vector3D v = Vector3D.Cross(u, w);
                (int level, int i0, int j0) = Unpack(field, k);
                float[] heights = grids.Heights(level);
                double cell = grids.Cell(level);
                float low = field.Tiles[t + 7], high = field.Tiles[t + 11];
                for (int b = 0; b <= Tile; b++)
                {
                    for (int a = 0; a <= Tile; a++)
                    {
                        float h = heights[(j0 + b) * grids.Size + i0 + a];
                        Assert.IsTrue(h >= low && h <= high, $"tile {k}: its height range");
                        Vector3D r = mesh.Frame.ToFrame(grids.Origin(level) + (i0 + a) * cell, h, grids.Origin(level) + (j0 + b) * cell) - o;
                        double du = Vector3D.Dot(r, u) / cell - (a - Tile / 2), dv = Vector3D.Dot(r, v) / cell - (b - Tile / 2);
                        worst = Math.Max(worst, Math.Max(Math.Abs(du), Math.Abs(dv)));
                    }
                }
            }
            // The mismatch is measured on nine points a tile; every sample stays within the clip's margin.
            Assert.IsTrue(worst <= field.Margin, $"worst {worst} against a margin of {field.Margin}");
            Assert.IsTrue(field.GridMismatch <= worst + 1e-9);
        }
    }

    [TestMethod]
    public void TheMeshBeside_IsTheWorldGridsAlone_WithTheFarFieldsReachAndHeights()
    {
        foreach (CameraSpec camera in Cameras())
        {
            TerrainMesh tiled = Build(camera, Settings);
            TerrainMesh meshed = Build(camera, new MeshSettings());
            TerrainMesh near = Build(camera, new MeshSettings { FarField = false });
            CollectionAssert.AreEqual(near.Indices, tiled.Indices);
            CollectionAssert.AreEqual(near.Positions, tiled.Positions);
            Assert.AreEqual(meshed.ReachMetres, tiled.ReachMetres);
            Assert.AreEqual(meshed.MinHeight, tiled.MinHeight);
            Assert.AreEqual(meshed.MaxHeight, tiled.MaxHeight);
            Assert.IsTrue(tiled.Info.HeightField && tiled.Info.HeightFieldTiles == tiled.HeightField.TileCount && tiled.HeightField.Any);
            Assert.IsFalse(meshed.HeightField.Any);
            Assert.AreEqual(0L, meshed.Info.HeightFieldTiles);
        }
    }

    [TestMethod]
    public void TheSameTiles_EveryTime()
    {
        CameraSpec camera = Cameras().First();
        FarHeightField a = Build(camera, Settings).HeightField, b = Build(camera, Settings).HeightField;
        CollectionAssert.AreEqual(a.Tiles, b.Tiles);
        CollectionAssert.AreEqual(a.Aabbs, b.Aabbs);
        Assert.AreEqual(a.GridMismatch, b.GridMismatch);
    }

    [TestMethod]
    public void ATileThatDoesNotDivideTheInnerSquares_IsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Build(Cameras().First(), Settings with { HeightFieldTileCells = 8 }));
        Assert.AreEqual(1, new MeshSettings { HeightFieldTileCells = 6 }.Validate().Count);
        Assert.AreEqual(1, new MeshSettings { HeightFieldTileCells = 512 }.Validate().Count);
        Assert.AreEqual(0, new MeshSettings { HeightFieldTileCells = 32 }.Validate().Count);
    }

    private static (int Level, int I0, int J0) Unpack(FarHeightField field, int k)
    {
        uint packed = BitConverter.SingleToUInt32Bits(field.Tiles[k * FarHeightField.TileStride + 3]);
        return ((int)(packed >> 26), (int)(packed & 0x1FFF), (int)((packed >> 13) & 0x1FFF));
    }
}
