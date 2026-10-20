using Vista.Recipes;
using Vista.Scene;
using Vista.World;

namespace Vista.Tests;

/// <summary>
/// The grids' normal maps (vista-plan §10 M1.9): the shading a coarse triangle gets back — the plane's own normal on a plane, central
/// differences at the samples, bilinear between them and the edge's beyond, and on every vertex of a mesh the map's normal there.
/// </summary>
[TestClass]
public sealed class NormalMapTests
{
    [TestMethod]
    public void OnAPlane_EverySampleIsThePlanesNormal()
    {
        const int size = 9;
        const double a = 0.3, b = -0.45;
        var heights = new float[size * size];
        for (int j = 0; j < size; j++)
        {
            for (int i = 0; i < size; i++)
                heights[j * size + i] = (float)(a * i * 10.0 + b * j * 10.0 + 500.0);
        }
        var map = new NormalMap(heights, size, 10.0, -40.0, -40.0);
        Vector3D expected = new Vector3D(-a, 1.0, -b).Normalized();
        foreach ((double x, double z) in new[] { (-40.0, -40.0), (0.0, 0.0), (13.7, -22.1), (40.0, 40.0), (500.0, -900.0) })
        {
            (float nx, float nz) = map.Sample(x, z);
            Assert.AreEqual(expected.X, nx, 1e-3, $"({x}, {z})");
            Assert.AreEqual(expected.Z, nz, 1e-3, $"({x}, {z})");
        }
        Assert.IsTrue(map.Contains(40.0, -40.0));
        Assert.IsFalse(map.Contains(40.1, 0.0));
        Assert.AreEqual(size * size * 4L, map.ByteSize);
    }

    [TestMethod]
    public void BetweenSamples_TheMapIsBilinear()
    {
        var heights = new float[4 * 4];
        heights[1 * 4 + 1] = 10f; // one bump: its neighbours lean away from it
        var map = new NormalMap(heights, 4, 1.0, 0.0, 0.0);
        (float x0, _) = map.Sample(0.0, 1.0);
        (float x1, _) = map.Sample(1.0, 1.0);
        (float half, _) = map.Sample(0.5, 1.0);
        Assert.AreEqual(0.5f * (x0 + x1), half, 1e-6f);
        Assert.IsTrue(x0 < -0.5f, "west of the bump the ground rises to the east, so its normal leans west");
    }

    [TestMethod]
    public void Packing_KeepsHalfPrecision()
    {
        foreach ((double x, double z) in new[] { (0.0, 0.0), (0.123456, -0.654321), (-0.999, 0.5) })
        {
            (float px, float pz) = NormalMap.Unpack(NormalMap.Pack(x, z));
            Assert.AreEqual(x, px, 1e-3);
            Assert.AreEqual(z, pz, 1e-3);
        }
    }

    [TestMethod]
    public void EveryVertex_CarriesTheMapsNormalAtItsPlace()
    {
        HeightField field = TerrainGenerator.Generate(new Recipe { SetPieces = [SetPiece.AlpineRange] }, 9, TestWorlds.Small);
        TerrainHierarchy h = TerrainHierarchy.Build(field, TerrainProfile.Mountains, 9, new TerrainDetail(9, field.CellSize));
        Assert.AreEqual(2, h.NormalMaps.Count, "the world grid's and the 32 m far grid's");
        var camera = new CameraSpec { Position = new WorldPoint(0.0, field.HeightAt(0.0, 0.0) + 20.0, 0.0), PitchDegrees = -10.0 };
        TerrainMesh mesh = ViewMesher.Build(h, MeshView.For(camera, 640, 360, 6_361_000.0), new MeshSettings());
        Assert.AreEqual(mesh.VertexCount, mesh.MapNormals.Length);
        Assert.AreSame(h.NormalMaps, mesh.NormalMaps, "every mesh of the world shares the maps");
        for (int v = 0; v < mesh.VertexCount; v += 7)
        {
            var p = new Vector3D(mesh.Positions[v * 3], mesh.Positions[v * 3 + 1], mesh.Positions[v * 3 + 2]);
            double horizontal = Math.Sqrt(p.X * p.X + p.Z * p.Z);
            double s = horizontal > 0.0 ? mesh.Frame.GroundRadius * Math.Atan2(horizontal, p.Y + mesh.Frame.GroundRadius) / horizontal : 0.0;
            double x = p.X * s, z = p.Z * s, edge = 0.5 * field.Extent;
            if (Math.Abs(Math.Abs(x) - edge) < 1.0 || Math.Abs(Math.Abs(z) - edge) < 1.0)
                continue; // on the world grid's edge, where the maps change, the float position may land on either side
            (float ex, float ez) = h.MapNormal(x, z);
            (float nx, float nz) = NormalMap.Unpack(mesh.MapNormals[v]);
            Assert.AreEqual(ex, nx, 2e-3, $"vertex {v}");
            Assert.AreEqual(ez, nz, 2e-3, $"vertex {v}");
        }
        TerrainHierarchy plain = TerrainHierarchy.Build(field, TerrainProfile.Mountains, 9, TerrainDetail.None);
        TerrainMesh uniform = ViewMesher.Build(plain, MeshView.For(camera, 640, 360, 6_361_000.0), new MeshSettings { Kind = MeshKind.Uniform });
        Assert.AreEqual(0, uniform.MapNormals.Length, "the uniform mesh has a vertex at every sample: nothing to add");
        Assert.AreEqual(0, uniform.NormalMaps.Count);
    }
}
