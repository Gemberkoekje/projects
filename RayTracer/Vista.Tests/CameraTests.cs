using System.Numerics;
using RayTracer;
using Vista.Recipes;
using Vista.Scene;
using Vista.World;

namespace Vista.Tests;

/// <summary>The camera model and its mapping onto the engine's conventions (vista-plan §3.1 / §5.6).</summary>
[TestClass]
public sealed class CameraTests
{
    private static CameraSpec At(double yaw, double pitch, double roll = 0) =>
        new() { Position = new WorldPoint(0, 0, 0), YawDegrees = yaw, PitchDegrees = pitch, RollDegrees = roll };

    [TestMethod]
    public void Heading_FollowsTheCompass()
    {
        AssertClose(new Vector3D(0, 0, 1), At(0, 0).Forward());   // north = +Z
        AssertClose(new Vector3D(1, 0, 0), At(90, 0).Forward());  // east = +X
        AssertClose(new Vector3D(0, 0, -1), At(180, 0).Forward());
        AssertClose(new Vector3D(1, 0, 0), At(0, 0).Right());     // facing north, east is on the right
        AssertClose(new Vector3D(0, 1, 0), At(0, 0).Up());
    }

    [TestMethod]
    public void PositivePitch_LooksUp_PositiveRoll_BanksLeft()
    {
        Assert.IsTrue(At(0, 20).Forward().Y > 0.3);
        Assert.IsTrue(At(0, 0, 15).Right().Y > 0.2, "banking left raises the right side");
    }

    [TestMethod]
    [DataRow(0.0, 0.0, 0.0)]
    [DataRow(37.0, 12.0, -5.0)]
    [DataRow(-120.0, -30.0, 20.0)]
    [DataRow(250.0, 60.0, 90.0)]
    public void Basis_IsOrthonormal(double yaw, double pitch, double roll)
    {
        CameraSpec c = At(yaw, pitch, roll);
        Assert.AreEqual(1.0, c.Forward().Length(), 1e-12);
        Assert.AreEqual(1.0, c.Right().Length(), 1e-12);
        Assert.AreEqual(1.0, c.Up().Length(), 1e-12);
        Assert.AreEqual(0.0, Vector3D.Dot(c.Forward(), c.Right()), 1e-12);
        Assert.AreEqual(0.0, Vector3D.Dot(c.Forward(), c.Up()), 1e-12);
        Assert.AreEqual(0.0, Vector3D.Dot(c.Right(), c.Up()), 1e-12);
    }

    [TestMethod]
    [DataRow(0.0, 0.0, 0.0)]
    [DataRow(37.0, 12.0, -5.0)]
    [DataRow(-120.0, -30.0, 20.0)]
    [DataRow(250.0, 60.0, 90.0)]
    public void EngineRotation_MapsCameraSpaceToTheBasis(double yaw, double pitch, double roll)
    {
        // The engine generates primary rays as (px, py, +z) in camera space, rotated by Camera.Rotation.
        CameraSpec c = At(yaw, pitch, roll);
        Quaternion q = c.ToEngineRotation();
        AssertClose(c.Forward(), Vector3.Transform(Vector3.UnitZ, q), 1e-5);
        AssertClose(c.Right(), Vector3.Transform(Vector3.UnitX, q), 1e-5);
        AssertClose(c.Up(), Vector3.Transform(Vector3.UnitY, q), 1e-5);
    }

    [TestMethod]
    public void FieldOfView_FollowsFocalLengthAndSensor()
    {
        CameraSpec c = At(0, 0) with { FocalLengthMm = 24, SensorHeightMm = 24 };
        Assert.AreEqual(2.0 * Math.Atan(0.5), c.VerticalFovRadians(), 1e-12);
        // Same convention as the engine's lens model: f = h / (2·tan(fov/2)).
        var lens = new LensOptions(Model: LensModel.Cine, SensorHeightMm: 24f);
        Assert.AreEqual(24f, lens.EffectiveFocalLengthMm((float)c.VerticalFovRadians()), 1e-3f);
    }

    [TestMethod]
    public void Json_RoundTrips()
    {
        CameraSpec c = At(12.5, -3, 1) with { Position = new WorldPoint(10.25, 1234.5, -2000), FocalLengthMm = 50, ExposureEv = -0.5 };
        CameraSpec back = VistaJson.ParseCamera(VistaJson.Serialize(c));
        Assert.AreEqual(c, back);
    }

    [TestMethod]
    [DataRow("""{ "position": { "x": 0, "y": 0, "z": 0 }, "pitchDegrees": 90 }""", "Pitch")]
    [DataRow("""{ "position": { "x": 0, "y": 0, "z": 0 }, "focalLengthMm": 0 }""", "Focal length")]
    [DataRow("""{ "position": { "x": 0, "y": 0, "z": 0 }, "fStop": -1 }""", "F-stop")]
    [DataRow("""{ "position": { "x": 0, "y": 0, "z": 0 }, "schema": 9 }""", "schema")]
    public void Json_RejectsInvalidCameras(string json, string expected)
    {
        var e = Assert.ThrowsExactly<InvalidDataException>(() => VistaJson.ParseCamera(json));
        StringAssert.Contains(e.Message, expected);
    }

    [TestMethod]
    public void Json_HoldsOnlyTheEditableFields()
    {
        // Derived vectors are not written, and editing one by hand is an error rather than silently ignored.
        string json = VistaJson.Serialize(At(10, 5));
        Assert.IsFalse(json.Contains("forward", StringComparison.Ordinal) || json.Contains("verticalFov", StringComparison.Ordinal), json);
        Assert.ThrowsExactly<System.Text.Json.JsonException>(() =>
            VistaJson.ParseCamera("""{ "position": { "x": 0, "y": 0, "z": 0 }, "forward": { "x": 1, "y": 0, "z": 0 } }"""));
    }

    [TestMethod]
    public void Json_RequiresAPosition() =>
        Assert.ThrowsExactly<System.Text.Json.JsonException>(() => VistaJson.ParseCamera("""{ "yawDegrees": 3 }"""));

    [TestMethod]
    public void DefaultCamera_StandsAtEyeHeightLookingNorth()
    {
        HeightField field = TerrainGenerator.Generate(new Recipe { Terrain = [TerrainBlock.AlpineRange] }, 4, new WorldGrid(65, 32.0));
        CameraSpec c = DefaultCamera.For(field);
        Assert.AreEqual(0, c.Validate().Count);
        Assert.AreEqual(field.HeightAt(c.Position.X, c.Position.Z) + DefaultCamera.EyeHeightMetres, c.Position.Y, 1e-9);
        Assert.AreEqual(0.0, c.YawDegrees);
        Assert.IsNull(DefaultCamera.GroundWarning(c, field));
        StringAssert.Contains(DefaultCamera.GroundWarning(c with { Position = c.Position with { Y = -50 } }, field), "below the terrain");
    }

    private static void AssertClose(Vector3D expected, Vector3D actual, double tolerance = 1e-12)
    {
        Assert.AreEqual(expected.X, actual.X, tolerance, "x");
        Assert.AreEqual(expected.Y, actual.Y, tolerance, "y");
        Assert.AreEqual(expected.Z, actual.Z, tolerance, "z");
    }

    private static void AssertClose(Vector3D expected, Vector3 actual, double tolerance) =>
        AssertClose(expected, new Vector3D(actual.X, actual.Y, actual.Z), tolerance);
}
