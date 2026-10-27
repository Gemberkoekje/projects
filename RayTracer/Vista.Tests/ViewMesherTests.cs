using Vista.Recipes;
using Vista.Scene;
using Vista.World;

namespace Vista.Tests;

/// <summary>
/// The view-dependent terrain mesh (vista-plan §10 M1.9) on a small world: free of cracks from any camera and under any settings,
/// every triangle facing up, every vertex on the world, the ground around the camera covered exactly once, the same bits every
/// time, and what a turn or a finer target changes.
/// </summary>
[TestClass]
public sealed class ViewMesherTests
{
    private static readonly Lazy<(HeightField Field, TerrainHierarchy Hierarchy, TerrainHierarchy Plain)> World = new(() =>
    {
        HeightField field = TerrainGenerator.Generate(new Recipe { SetPieces = [SetPiece.AlpineRange] }, 9, TestWorlds.Small);
        return (field,
            TerrainHierarchy.Build(field, TerrainProfile.Mountains, 9, new TerrainDetail(9, field.CellSize)),
            TerrainHierarchy.Build(field, TerrainProfile.Mountains, 9, TerrainDetail.None));
    });

    private const double Radius = 6_361_000.0;

    private static CameraSpec Camera(double x, double above, double z, double yaw, double pitch)
    {
        HeightField field = World.Value.Field;
        return new CameraSpec { Position = new WorldPoint(x, field.HeightAt(x, z) + above, z), YawDegrees = yaw, PitchDegrees = pitch, FocalLengthMm = 28.0 };
    }

    /// <summary>On the ground, from the air, at g0's edge looking out, out in the far field looking in, straight down, and up.</summary>
    private static IEnumerable<(string Name, CameraSpec Camera)> Cameras()
    {
        yield return ("ground", Camera(0.0, 5.0, 0.0, 0.0, -5.0));
        yield return ("aerial", Camera(-1000.0, 1500.0, -1500.0, 30.0, -20.0));
        yield return ("edge", Camera(1950.0, 10.0, 100.0, 90.0, 0.0));
        yield return ("far field", Camera(5000.0, 1000.0, 5000.0, 225.0, -10.0));
        yield return ("down", Camera(300.0, 50.0, 300.0, 0.0, -89.0));
        yield return ("up", Camera(-200.0, 3.0, 600.0, 120.0, 80.0));
    }

    private static MeshView View(CameraSpec camera, int width = 640, int height = 360) => MeshView.For(camera, width, height, Radius);

    private static TerrainMesh Build(CameraSpec camera, MeshSettings? settings = null, int width = 640, int height = 360, bool plain = false)
    {
        (_, TerrainHierarchy h, TerrainHierarchy p) = World.Value;
        return ViewMesher.Build(plain ? p : h, View(camera, width, height), settings ?? new MeshSettings());
    }

    [TestMethod]
    public void NoCracks_FromAnyCamera_UnderAnySettings()
    {
        (_, TerrainHierarchy h, TerrainHierarchy plain) = World.Value;
        var settings = new (string Name, MeshSettings Settings, TerrainHierarchy Hierarchy)[]
        {
            ("default", new MeshSettings(), h),
            ("a finer target", new MeshSettings { ErrorPixels = 0.3 }, h),
            ("small triangles", new MeshSettings { MaxTrianglePixels = 2.0 }, h),
            ("as fine off screen", new MeshSettings { OffScreenRadians = 0.0 }, h),
            ("no far field", new MeshSettings { FarField = false }, h),
            ("no detail", new MeshSettings { Detail = false }, plain),
            ("uniform", new MeshSettings { Kind = MeshKind.Uniform }, plain),
        };
        foreach ((string name, CameraSpec camera) in Cameras())
        {
            foreach ((string label, MeshSettings s, TerrainHierarchy hierarchy) in settings)
            {
                foreach ((int w, int ht) in new[] { (320, 180), (1920, 1080) })
                    Assert.AreEqual(0, ViewMesher.TJunctions(hierarchy, View(camera, w, ht), s), $"{name}, {label}, {w}×{ht}");
            }
        }
    }

    [TestMethod]
    public void CountTriangles_IsTheMeshsCount()
    {
        foreach ((string name, CameraSpec camera) in Cameras())
        {
            foreach ((int w, int ht) in new[] { (320, 180), (1920, 1080) })
                Assert.AreEqual(Build(camera, null, w, ht).TriangleCount, ViewMesher.CountTriangles(World.Value.Hierarchy, View(camera, w, ht), new MeshSettings()), $"{name}, {w}×{ht}");
        }
    }

    [TestMethod]
    public void EveryTriangle_FacesUp()
    {
        foreach ((string name, CameraSpec camera) in Cameras())
        {
            TerrainMesh mesh = Build(camera);
            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                Vector3D p0 = Vertex(mesh, mesh.Indices[t * 3]), p1 = Vertex(mesh, mesh.Indices[t * 3 + 1]), p2 = Vertex(mesh, mesh.Indices[t * 3 + 2]);
                Vector3D up = mesh.Frame.LocalUp((p0 + p1 + p2) * (1.0 / 3.0));
                Assert.IsTrue(Vector3D.Dot(Vector3D.Cross(p1 - p0, p2 - p0), up) > 0.0, $"{name}: triangle {t} faces down");
            }
        }
    }

    [TestMethod]
    public void EveryVertex_SitsOnTheWorld_WithItsDetail()
    {
        (_, TerrainHierarchy h, _) = World.Value;
        foreach ((string name, CameraSpec camera) in Cameras())
        {
            TerrainMesh mesh = Build(camera, width: 1920, height: 1080);
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                Vector3D p = Vertex(mesh, (uint)v);
                (double x, double z) = WorldOf(mesh, camera, p);
                int i = (int)Math.Round((x - h.RootOrigin) / h.SubUnit), j = (int)Math.Round((z - h.RootOrigin) / h.SubUnit);
                double expected = h.BaseHeight(i, j) + h.Detail.Height(h.WorldOf(i), h.WorldOf(j));
                Assert.AreEqual(expected, mesh.Frame.HeightAbove(p), 0.05, $"{name}: vertex {v} at ({x:0.0}, {z:0.0})");
            }
        }
    }

    [TestMethod]
    public void TheGroundAroundTheCamera_IsCoveredExactlyOnce()
    {
        // Random points within 3 km of the camera, each in exactly one triangle (seen from above, in world coordinates).
        var random = new Random(10);
        foreach ((string name, CameraSpec camera) in Cameras().Take(3))
        {
            TerrainMesh mesh = Build(camera);
            var flat = new (double X, double Z)[mesh.VertexCount];
            for (int v = 0; v < mesh.VertexCount; v++)
                flat[v] = WorldOf(mesh, camera, Vertex(mesh, (uint)v));
            for (int n = 0; n < 150; n++)
            {
                double r = 3000.0 * Math.Sqrt(random.NextDouble()), a = random.NextDouble() * 2.0 * Math.PI;
                double x = camera.Position.X + r * Math.Cos(a), z = camera.Position.Z + r * Math.Sin(a);
                int inside = 0;
                for (int t = 0; t < mesh.TriangleCount; t++)
                {
                    if (Contains(flat[mesh.Indices[t * 3]], flat[mesh.Indices[t * 3 + 1]], flat[mesh.Indices[t * 3 + 2]], x, z))
                        inside++;
                }
                Assert.AreEqual(1, inside, $"{name}: ({x:0.0}, {z:0.0}) lies in {inside} triangles");
            }
        }
    }

    [TestMethod]
    public void Build_IsDeterministic()
    {
        CameraSpec camera = Cameras().First().Camera;
        TerrainMesh a = Build(camera, width: 1920, height: 1080), b = Build(camera, width: 1920, height: 1080);
        CollectionAssert.AreEqual(a.Positions, b.Positions);
        CollectionAssert.AreEqual(a.Normals, b.Normals);
        CollectionAssert.AreEqual(a.Indices, b.Indices);
    }

    [TestMethod]
    public void TheCamera_StandsAtTheFramesOrigin()
    {
        CameraSpec camera = Cameras().First().Camera;
        TerrainMesh mesh = Build(camera);
        Assert.AreEqual(new EarthFrame(Radius, camera.Position.X, camera.Position.Z), mesh.Frame);
        double nearest = double.MaxValue;
        for (int v = 0; v < mesh.VertexCount; v++)
            nearest = Math.Min(nearest, Math.Abs(mesh.Positions[v * 3]) + Math.Abs(mesh.Positions[v * 3 + 2]));
        Assert.IsTrue(nearest < 1.0, "the ground below the camera is near the frame's axis");
    }

    [TestMethod]
    public void AFinerTarget_MoreTriangles_AndTheDetailBelowTheGrid()
    {
        CameraSpec camera = Cameras().First().Camera;
        TerrainMesh coarse = Build(camera, new MeshSettings { ErrorPixels = 4.0 }, 1920, 1080);
        TerrainMesh fine = Build(camera, new MeshSettings { ErrorPixels = 0.5 }, 1920, 1080);
        Assert.IsTrue(fine.TriangleCount > coarse.TriangleCount, $"{fine.TriangleCount} against {coarse.TriangleCount}");
        Assert.IsTrue(fine.Info.DetailTriangles > 0, "near the ground camera the mesh goes below the grid");
        Assert.AreEqual(0, Build(camera, new MeshSettings { Detail = false }, 1920, 1080, plain: true).Info.DetailTriangles);
        Assert.AreEqual(0, Build(camera, new MeshSettings { FarField = false }).Info.FarTriangles);
        Assert.IsTrue(Build(camera).Info.FarTriangles > 0);
    }

    [TestMethod]
    public void Uniform_MeshesEverySampleOfTheWorldGrid_AndNothingElse()
    {
        TerrainMesh mesh = Build(Cameras().First().Camera, new MeshSettings { Kind = MeshKind.Uniform }, plain: true);
        Assert.AreEqual(2 * 64 * 64, mesh.TriangleCount);
        Assert.AreEqual(65 * 65, mesh.VertexCount);
        Assert.AreEqual(MeshKind.Uniform, mesh.Info.Kind);
        Assert.IsFalse(mesh.Info.Detail);
        Assert.IsFalse(mesh.Info.FarField);
    }

    [TestMethod]
    public void ATurn_RemeshesTheView_ButKeepsWhatTheAirReads()
    {
        CameraSpec camera = Cameras().First().Camera;
        TerrainMesh ahead = Build(camera, width: 1920, height: 1080);
        TerrainMesh turned = Build(camera with { YawDegrees = 120.0 }, width: 1920, height: 1080);
        CollectionAssert.AreNotEqual(ahead.Indices, turned.Indices, "the frustum's detail moved with it");
        Assert.AreEqual((ahead.MinHeight, ahead.MaxHeight, ahead.ReachMetres), (turned.MinHeight, turned.MaxHeight, turned.ReachMetres));
        TerrainMesh higher = Build(camera with { Position = camera.Position with { Y = camera.Position.Y + 2000.0 } }, width: 1920, height: 1080);
        Assert.IsTrue(higher.ReachMetres > ahead.ReachMetres, "a higher camera sees farther");
    }

    [TestMethod]
    public void OffScreen_TheGroundStays_AtTheSunsTolerance()
    {
        CameraSpec camera = Cameras().First().Camera;
        TerrainMesh mesh = Build(camera, width: 1920, height: 1080);
        Vector3D forward = camera.Forward();
        int behind = 0;
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            Vector3D p = Vertex(mesh, mesh.Indices[t * 3]);
            if (p.X * forward.X + p.Z * forward.Z < -500.0)
                behind++;
        }
        Assert.IsTrue(behind > 100, $"{behind} triangles behind the camera");
        TerrainMesh asOnScreen = Build(camera, new MeshSettings { OffScreenRadians = 0.0 }, 1920, 1080);
        Assert.IsTrue(asOnScreen.TriangleCount > mesh.TriangleCount, "the sun's tolerance is coarser than a pixel off screen");
    }

    [TestMethod]
    public void MeshView_ReadsTheCameraAndTheFrame()
    {
        var camera = new CameraSpec { Position = new WorldPoint(1.0, 2.0, 3.0), FocalLengthMm = 24.0, SensorHeightMm = 24.0 };
        MeshView view = MeshView.For(camera, 3840, 2160, Radius);
        double tanHalf = Math.Tan(0.5 * camera.VerticalFovRadians());
        Assert.AreEqual(0.5, tanHalf, 1e-12);
        Assert.AreEqual(2160.0, view.PixelsPerRadian, 1e-9, "2160 / (2 tan 26.6°)");
        Assert.AreEqual(tanHalf * 3840.0 / 2160.0, view.TanHalfWidth, 1e-12);
        Assert.AreEqual(new EarthFrame(Radius, 1.0, 3.0), view.Frame);
    }

    [TestMethod]
    public void Settings_AreValidated()
    {
        Assert.AreEqual(0, new MeshSettings().Validate().Count);
        Assert.AreEqual(1, new MeshSettings { ErrorPixels = 0.0 }.Validate().Count);
        Assert.AreEqual(1, new MeshSettings { OffScreenRadians = -1.0 }.Validate().Count);
        Assert.AreEqual(1, new MeshSettings { MaxTrianglePixels = 0.5 }.Validate().Count);
        Assert.AreEqual(1, new MeshSettings { Kind = MeshKind.None }.Validate().Count);
        (_, TerrainHierarchy h, _) = World.Value;
        Assert.ThrowsExactly<ArgumentException>(() => ViewMesher.Build(h, View(Cameras().First().Camera), new MeshSettings { ErrorPixels = double.NaN }));
    }

    private static Vector3D Vertex(TerrainMesh mesh, uint v) => new(mesh.Positions[v * 3], mesh.Positions[v * 3 + 1], mesh.Positions[v * 3 + 2]);

    /// <summary>The world (x, z) a frame point came from: its bearing from the camera and its distance along the ground.</summary>
    private static (double X, double Z) WorldOf(TerrainMesh mesh, CameraSpec camera, Vector3D p)
    {
        double horizontal = Math.Sqrt(p.X * p.X + p.Z * p.Z);
        if (horizontal == 0.0)
            return (camera.Position.X, camera.Position.Z);
        double s = mesh.Frame.GroundRadius * Math.Atan2(horizontal, p.Y + mesh.Frame.GroundRadius);
        return (camera.Position.X + p.X / horizontal * s, camera.Position.Z + p.Z / horizontal * s);
    }

    private static bool Contains((double X, double Z) a, (double X, double Z) b, (double X, double Z) c, double x, double z)
    {
        static double Side((double X, double Z) p, (double X, double Z) q, double x, double z) => (q.X - p.X) * (z - p.Z) - (q.Z - p.Z) * (x - p.X);
        double d0 = Side(a, b, x, z), d1 = Side(b, c, x, z), d2 = Side(c, a, x, z);
        return (d0 >= 0 && d1 >= 0 && d2 >= 0) || (d0 <= 0 && d1 <= 0 && d2 <= 0);
    }
}
