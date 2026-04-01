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
    public void Update_SetsDirtyFlag_OnActionTransition()
    {
        var maze = new Maze(4, 4, seed: 0);
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

        // Dirty is set at the start of each action (constructor calls BeginNextAction).
        Assert.IsTrue(ctrl.Dirty, "Dirty should be true after construction.");
        ctrl.Dirty = false;

        // A small tick during interpolation SHOULD set Dirty (camera is moving).
        ctrl.Update(0.01f, cam);
        Assert.IsTrue(ctrl.Dirty, "Mid-interpolation should set Dirty.");

        // Advancing past the action duration triggers BeginNextAction → Dirty.
        ctrl.Update(100f, cam);
        Assert.IsTrue(ctrl.Dirty, "Action transition should set Dirty.");
    }

    [TestMethod]
    public void Update_LargeTime_SnapsToTarget()
    {
        var maze = new Maze(4, 4, seed: 0);
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
        var ctrl = new CameraController(nav, cs, eyeHeight);
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

    // ── Helpers ────────────────────────────────────────────────────

    static void AssertVec3Near(Vector3 expected, Vector3 actual, float eps)
    {
        Assert.AreEqual(expected.X, actual.X, eps, $"X mismatch: expected {expected}, got {actual}");
        Assert.AreEqual(expected.Y, actual.Y, eps, $"Y mismatch: expected {expected}, got {actual}");
        Assert.AreEqual(expected.Z, actual.Z, eps, $"Z mismatch: expected {expected}, got {actual}");
    }
}
