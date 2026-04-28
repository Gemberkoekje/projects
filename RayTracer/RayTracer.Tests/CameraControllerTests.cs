using System.Numerics;
using RayTracer;

namespace RayTracer.Tests;

[TestClass]
public class CameraControllerTests
{
    // ── HeadingToQuaternion ────────────────────────────────────────

    [TestMethod]
    public void HeadingToQuaternion_South_IsIdentity()
    {
        var q = CameraController.HeadingToQuaternion(Direction.South);
        Assert.AreEqual(Quaternion.Identity, q);
    }

    [TestMethod]
    public void HeadingToQuaternion_ForwardDirectionMatchesExpected()
    {
        // The camera default forward is +Z.
        var forward = Vector3.UnitZ;

        var south = Vector3.Transform(forward, CameraController.HeadingToQuaternion(Direction.South));
        AssertVec3Near(new Vector3(0, 0, 1), south, 1e-5f);

        var north = Vector3.Transform(forward, CameraController.HeadingToQuaternion(Direction.North));
        AssertVec3Near(new Vector3(0, 0, -1), north, 1e-5f);

        var east = Vector3.Transform(forward, CameraController.HeadingToQuaternion(Direction.East));
        AssertVec3Near(new Vector3(1, 0, 0), east, 1e-5f);

        var west = Vector3.Transform(forward, CameraController.HeadingToQuaternion(Direction.West));
        AssertVec3Near(new Vector3(-1, 0, 0), west, 1e-5f);
    }

    // ── Update integration ─────────────────────────────────────────

    [TestMethod]
    public void Update_DoesNotSetDirty_DuringStill()
    {
        var maze = new Maze(1, 2, seed: 0);
        var nav = new MazeNavigator(maze, 0, 0, Direction.South);
        var ctrl = new CameraController(nav, 2f, 1f);
        var cam = new Camera
        {
            Position = new Vector3(1, 1, 1),
            Rotation = Quaternion.Identity,
            Fov = MathF.PI / 3f,
            Aspect = 4f / 3f,
            ImgPlaneZ = 1f
        };

        Assert.IsFalse(ctrl.Dirty, "Dirty should be false after construction while still.");
        ctrl.Update(0.01f, cam);
        Assert.IsFalse(ctrl.Dirty, "Still phase should not set Dirty.");
    }

    [TestMethod]
    public void Update_StartsTurnImmediately_WhenStillAndFacingWall()
    {
        var maze = new Maze(1, 2, seed: 0);
        var nav = new MazeNavigator(maze, 0, 0, Direction.North);
        var ctrl = new CameraController(nav, 2f, 1f)
        {
            StillTime = 10f,
            TurnTime = 1f
        };
        var cam = new Camera
        {
            Position = new Vector3(1, 1, 1),
            Rotation = CameraController.HeadingToQuaternion(Direction.North),
            Fov = MathF.PI / 3f,
            Aspect = 4f / 3f,
            ImgPlaneZ = 1f
        };

        ctrl.Update(0.001f, cam);

        Assert.IsTrue(ctrl.IsTurning, "Controller should begin turning immediately instead of waiting out still time.");
        Assert.IsTrue(ctrl.Dirty, "Starting a turn should mark camera as dirty.");
    }

    [TestMethod]
    public void Update_TurnCompletes_AndHeadingUpdates()
    {
        var maze = new Maze(1, 2, seed: 0);
        var nav = new MazeNavigator(maze, 0, 0, Direction.North);
        var ctrl = new CameraController(nav, 2f, 1f)
        {
            StillTime = 10f,
            TurnTime = 1f
        };
        var cam = new Camera
        {
            Position = new Vector3(1, 1, 1),
            Rotation = CameraController.HeadingToQuaternion(Direction.North),
            Fov = MathF.PI / 3f,
            Aspect = 4f / 3f,
            ImgPlaneZ = 1f
        };

        ctrl.Update(0.001f, cam);
        ctrl.Dirty = false;
        ctrl.Update(1f, cam);

        Assert.AreEqual(Direction.South, nav.Heading);
        Assert.IsFalse(ctrl.IsTurning, "After full turn duration, controller should leave turning state.");
    }

    [TestMethod]
    public void Update_LargeTime_SnapsToTarget()
    {
        var maze = new Maze(4, 4, seed: 0);
        var nav = new MazeNavigator(maze, 0, 0, Direction.South);
        var ctrl = new CameraController(nav, 2f, 1f)
        {
            StillTime = 0f,
            MoveTime = 1f
        };
        var cam = new Camera
        {
            Position = new Vector3(1, 1, 1),
            Rotation = Quaternion.Identity,
            Fov = MathF.PI / 3f,
            Aspect = 4f / 3f,
            ImgPlaneZ = 1f
        };

        // Advance well past one action duration.
        ctrl.Update(100f, cam);

        // Camera should have snapped to a cell center at eye height.
        Assert.AreEqual(1f, cam.Position.Y, 0.001f, "Eye height should stay constant.");
    }

    [TestMethod]
    public void MultipleUpdates_CameraMovesAwayFromStart()
    {
        var maze = new Maze(8, 8, seed: 42);
        var nav = new MazeNavigator(maze, 0, 0, Direction.South);
        float cs = 2f;
        float eyeHeight = 1f;
        var ctrl = new CameraController(nav, cs, eyeHeight)
        {
            StillTime = 0.01f
        };
        var cam = new Camera
        {
            Position = new Vector3(cs * 0.5f, eyeHeight, cs * 0.5f),
            Rotation = Quaternion.Identity,
            Fov = MathF.PI / 3f,
            Aspect = 4f / 3f,
            ImgPlaneZ = 1f
        };

        Vector3 start = cam.Position;

        // Simulate several seconds of walking.
        for (int i = 0; i < 500; i++)
            ctrl.Update(0.016f, cam);

        float dist = Vector3.Distance(start, cam.Position);
        Assert.IsTrue(dist > 0.1f, $"Camera should have moved from start; distance = {dist}.");
    }

    [TestMethod]
    public void Update_ZeroDeltaTime_DoesNotMoveCameraPosition()
    {
        var maze = new Maze(4, 4, seed: 11);
        var nav = new MazeNavigator(maze, 0, 0, Direction.South);
        var ctrl = new CameraController(nav, 2f, 1f);
        var cam = new Camera
        {
            Position = new Vector3(1, 1, 1),
            Rotation = Quaternion.Identity,
            Fov = MathF.PI / 3f,
            Aspect = 4f / 3f,
            ImgPlaneZ = 1f
        };

        var startPos = cam.Position;
        ctrl.Update(0f, cam);

        AssertVec3Near(startPos, cam.Position, 1e-6f);
        Assert.IsFalse(ctrl.Dirty, "Controller should not mark the camera dirty during a still phase.");
    }

    [TestMethod]
    public void RapidAlternatingUpdates_KeepNavigatorWithinMazeBounds()
    {
        const int mazeSize = 8;
        var maze = new Maze(mazeSize, mazeSize, seed: 123);
        var nav = new MazeNavigator(maze, 0, 0, Direction.South);
        var ctrl = new CameraController(nav, 2f, 1f)
        {
            StillTime = 0.01f
        };
        var cam = new Camera
        {
            Position = new Vector3(1, 1, 1),
            Rotation = Quaternion.Identity,
            Fov = MathF.PI / 3f,
            Aspect = 16f / 9f,
            ImgPlaneZ = 1f
        };

        for (int i = 0; i < 200; i++)
        {
            float dt = (i % 2 == 0) ? 0.001f : 2.5f;
            ctrl.Update(dt, cam);

            Assert.IsTrue(nav.CellX >= 0 && nav.CellX < mazeSize, $"CellX out of bounds: {nav.CellX}");
            Assert.IsTrue(nav.CellY >= 0 && nav.CellY < mazeSize, $"CellY out of bounds: {nav.CellY}");
            Assert.AreEqual(1f, cam.Position.Y, 0.001f, "Eye height should remain stable under rapid update cadence.");
            Assert.IsTrue(float.IsFinite(cam.Position.X) && float.IsFinite(cam.Position.Y) && float.IsFinite(cam.Position.Z),
                "Camera position should stay finite.");
        }
    }

    [TestMethod]
    public void Update_DuringStill_WhenFacingWall_TurnsImmediatelyWithoutWaiting()
    {
        var maze = new Maze(1, 2, seed: 0);
        var nav = new MazeNavigator(maze, 0, 0, Direction.North);
        var ctrl = new CameraController(nav, 2f, 1f)
        {
            StillTime = 10f
        };
        var cam = new Camera
        {
            Position = new Vector3(1, 1, 1),
            Rotation = CameraController.HeadingToQuaternion(Direction.North),
            Fov = MathF.PI / 3f,
            Aspect = 4f / 3f,
            ImgPlaneZ = 1f
        };

        ctrl.Update(0.001f, cam);

        Assert.IsTrue(ctrl.IsTurning, "Controller should start turning immediately instead of waiting out still time.");
        Assert.IsTrue(ctrl.Dirty, "Turn start should mark camera as dirty.");
    }

    // ── Helpers ────────────────────────────────────────────────────

    static void AssertVec3Near(Vector3 expected, Vector3 actual, float eps)
    {
        Assert.AreEqual(expected.X, actual.X, eps, $"X mismatch: expected {expected}, got {actual}");
        Assert.AreEqual(expected.Y, actual.Y, eps, $"Y mismatch: expected {expected}, got {actual}");
        Assert.AreEqual(expected.Z, actual.Z, eps, $"Z mismatch: expected {expected}, got {actual}");
    }
}
