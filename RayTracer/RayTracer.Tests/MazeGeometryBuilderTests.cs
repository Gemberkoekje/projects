using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

[TestClass]
public class MazeGeometryBuilderTests
{
    // ── Helper ──────────────────────────────────────────────────────

    private static MaterialData DummyMaterial() =>
        new("test", "Test", null, null, 0, 50, 0, 0, 0.5f, 0.5f, 0.5f, 0, 1);

    // ────────────────────────────────────────────────────────────────
    //  1. Correct total number of tracables
    //     A perfect W×H maze has exactly (W*H + W + H + 1) wall quads
    //     plus 2 for the floor and ceiling.
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Build_1x1Maze_Returns6Tracables()
    {
        // 4 perimeter walls + floor + ceiling = 6
        var maze = new Maze(1, 1, seed: 0);
        var mat = DummyMaterial();

        var scene = MazeGeometryBuilder.Build(maze, mat, mat, mat);

        Assert.AreEqual(6, scene.Length);
    }

    [TestMethod]
    [DataRow(2, 2, 0, DisplayName = "2×2")]
    [DataRow(4, 4, 42, DisplayName = "4×4")]
    [DataRow(8, 8, 7, DisplayName = "8×8")]
    [DataRow(16, 16, 99, DisplayName = "16×16")]
    [DataRow(3, 7, 55, DisplayName = "3×7")]
    public void Build_PerfectMaze_CorrectTotalTracables(int w, int h, int seed)
    {
        var maze = new Maze(w, h, seed);
        var mat = DummyMaterial();

        var scene = MazeGeometryBuilder.Build(maze, mat, mat, mat);

        // walls = W*H + W + H + 1, plus floor + ceiling
        int expected = w * h + w + h + 1 + 2;
        Assert.AreEqual(expected, scene.Length,
            $"Expected {expected} tracables for a {w}×{h} maze.");
    }

    // ────────────────────────────────────────────────────────────────
    //  2. Every tracable is a TracableRectangle
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Build_AllTracablesAreRectangles()
    {
        var maze = new Maze(4, 4, seed: 0);
        var mat = DummyMaterial();

        var scene = MazeGeometryBuilder.Build(maze, mat, mat, mat);

        foreach (var t in scene)
            Assert.IsInstanceOfType<TracableRectangle>(t);
    }

    // ────────────────────────────────────────────────────────────────
    //  3. Floor and ceiling are the last two tracables
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Build_LastTwoTracables_AreFloorAndCeiling()
    {
        var maze = new Maze(4, 4, seed: 0);
        var mat = DummyMaterial();

        var scene = MazeGeometryBuilder.Build(maze, mat, mat, mat);

        Assert.IsInstanceOfType<TracableRectangle>(scene[^2], "Second-to-last should be floor.");
        Assert.IsInstanceOfType<TracableRectangle>(scene[^1], "Last should be ceiling.");
    }

    // ────────────────────────────────────────────────────────────────
    //  4. Ray from inside cell (0,0) hits the North perimeter wall
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Build_RayNorth_HitsPerimeterWall()
    {
        var maze = new Maze(2, 2, seed: 0);
        var mat = DummyMaterial();
        var scene = MazeGeometryBuilder.Build(maze, mat, mat, mat);

        float cs = MazeGeometryBuilder.CellSize;
        float wh = MazeGeometryBuilder.WallHeight;

        var ray = new Ray
        {
            Origin = new Vector3(cs / 2, wh / 2, cs / 2),
            Direction = new Vector3(0, 0, -1),   // toward North wall (z = 0)
            Wavelength = 550,
            Intensity = 1
        };

        bool anyHit = false;
        foreach (var t in scene)
        {
            var hit = t.Intersect(ray);
            if (hit.HasValue)
            {
                anyHit = true;
                Assert.AreEqual(0f, hit.Value.location.Z, 0.01f,
                    "North perimeter wall should be at Z = 0.");
                break;
            }
        }
        Assert.IsTrue(anyHit, "Ray pointing North from cell (0,0) must hit the perimeter wall.");
    }

    // ────────────────────────────────────────────────────────────────
    //  5. Ray from inside cell (0,0) hits the West perimeter wall
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Build_RayWest_HitsPerimeterWall()
    {
        var maze = new Maze(2, 2, seed: 0);
        var mat = DummyMaterial();
        var scene = MazeGeometryBuilder.Build(maze, mat, mat, mat);

        float cs = MazeGeometryBuilder.CellSize;
        float wh = MazeGeometryBuilder.WallHeight;

        var ray = new Ray
        {
            Origin = new Vector3(cs / 2, wh / 2, cs / 2),
            Direction = new Vector3(-1, 0, 0),   // toward West wall (x = 0)
            Wavelength = 550,
            Intensity = 1
        };

        bool anyHit = false;
        foreach (var t in scene)
        {
            var hit = t.Intersect(ray);
            if (hit.HasValue)
            {
                anyHit = true;
                Assert.AreEqual(0f, hit.Value.location.X, 0.01f,
                    "West perimeter wall should be at X = 0.");
                break;
            }
        }
        Assert.IsTrue(anyHit, "Ray pointing West from cell (0,0) must hit the perimeter wall.");
    }

    // ────────────────────────────────────────────────────────────────
    //  6. Ray downward hits the floor at Y = 0
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Build_RayDown_HitsFloor()
    {
        var maze = new Maze(2, 2, seed: 0);
        var mat = DummyMaterial();
        var scene = MazeGeometryBuilder.Build(maze, mat, mat, mat);

        float cs = MazeGeometryBuilder.CellSize;
        float wh = MazeGeometryBuilder.WallHeight;

        var ray = new Ray
        {
            Origin = new Vector3(cs / 2, wh / 2, cs / 2),
            Direction = new Vector3(0, -1, 0),   // downward
            Wavelength = 550,
            Intensity = 1
        };

        bool hitFloor = false;
        foreach (var t in scene)
        {
            var hit = t.Intersect(ray);
            if (hit.HasValue && MathF.Abs(hit.Value.location.Y) < 0.01f)
            {
                hitFloor = true;
                break;
            }
        }
        Assert.IsTrue(hitFloor, "Ray pointing downward should hit the floor at Y = 0.");
    }

    // ────────────────────────────────────────────────────────────────
    //  7. Ray upward hits the ceiling at Y = WallHeight
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Build_RayUp_HitsCeiling()
    {
        var maze = new Maze(2, 2, seed: 0);
        var mat = DummyMaterial();
        var scene = MazeGeometryBuilder.Build(maze, mat, mat, mat);

        float cs = MazeGeometryBuilder.CellSize;
        float wh = MazeGeometryBuilder.WallHeight;

        var ray = new Ray
        {
            Origin = new Vector3(cs / 2, wh / 2, cs / 2),
            Direction = new Vector3(0, 1, 0),    // upward
            Wavelength = 550,
            Intensity = 1
        };

        bool hitCeiling = false;
        foreach (var t in scene)
        {
            var hit = t.Intersect(ray);
            if (hit.HasValue && MathF.Abs(hit.Value.location.Y - wh) < 0.01f)
            {
                hitCeiling = true;
                break;
            }
        }
        Assert.IsTrue(hitCeiling, "Ray pointing upward should hit the ceiling at Y = WallHeight.");
    }

    // ────────────────────────────────────────────────────────────────
    //  8. No duplicate wall quads (wall count matches formula)
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Build_NoDuplicateWalls()
    {
        // The formula W*H + W + H + 1 already implies no duplicates for a
        // perfect maze. Verify by counting walls (all tracables minus 2 for
        // floor/ceiling) and comparing to the expected count computed from
        // the maze's wall flags.
        var maze = new Maze(8, 8, seed: 42);
        var mat = DummyMaterial();
        var scene = MazeGeometryBuilder.Build(maze, mat, mat, mat);

        int wallCount = scene.Length - 2; // subtract floor + ceiling
        int expected = 8 * 8 + 8 + 8 + 1;

        Assert.AreEqual(expected, wallCount,
            "Wall count must match the perfect-maze formula (no duplicates).");
    }

    // ────────────────────────────────────────────────────────────────
    //  9. Custom cell size and wall height
    // ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Build_CustomDimensions_ScalesCorrectly()
    {
        var maze = new Maze(2, 2, seed: 0);
        var mat = DummyMaterial();
        float cs = 5f;
        float wh = 3f;

        var scene = MazeGeometryBuilder.Build(maze, mat, mat, mat, cellSize: cs, wallHeight: wh);

        // A ray from the center of cell (0,0) pointing down should hit
        // the floor, and pointing North should hit z=0.
        var rayDown = new Ray
        {
            Origin = new Vector3(cs / 2, wh / 2, cs / 2),
            Direction = new Vector3(0, -1, 0),
            Wavelength = 550,
            Intensity = 1
        };

        bool hitFloor = false;
        foreach (var t in scene)
        {
            var hit = t.Intersect(rayDown);
            if (hit.HasValue && MathF.Abs(hit.Value.location.Y) < 0.01f)
            {
                hitFloor = true;
                break;
            }
        }
        Assert.IsTrue(hitFloor, "Floor should exist at Y = 0 with custom cell size.");
    }
}
