using Vista.Atmosphere;
using Vista.Scene;
using Vista.World;

namespace Vista.Tests;

/// <summary>
/// The frame the mesh is built and traced in since M1.9 (vista-plan §10 M1.9): the world bent onto the Earth around the camera. The
/// shader's <c>HeightAbove</c> and <c>LocalUp</c> follow these.
/// </summary>
[TestClass]
public sealed class EarthFrameTests
{
    private static double Radius => AtmosphereLutsTests.Frame.GroundRadius;

    [TestMethod]
    public void TheCamera_StandsOnItsOwnTangentPoint()
    {
        var frame = new EarthFrame(Radius, 100.0, -200.0);
        Assert.AreEqual(new Vector3D(0.0, 1700.0, 0.0), frame.ToFrame(100.0, 1700.0, -200.0));
        Assert.AreEqual(new Vector3D(0.0, 1.0, 0.0), frame.LocalUp(frame.ToFrame(100.0, 1700.0, -200.0)));
        Assert.AreEqual(1700.0, frame.HeightAbove(new Vector3D(0.0, 1700.0, 0.0)), 1e-9);
    }

    [TestMethod]
    public void ToFrame_KeepsHeights_SinksByTheDrop_AndTiltsTheVertical()
    {
        var frame = new EarthFrame(Radius, 0.0, 0.0);
        Vector3D far = frame.ToFrame(30_000.0, 500.0, 40_000.0);
        Vector3D centre = new(0.0, -Radius, 0.0);
        Assert.AreEqual(Radius + 500.0, (far - centre).Length(), 1e-6, "the point keeps its height above the ground sphere");
        Assert.AreEqual(500.0, frame.HeightAbove(far), 1e-6);
        Vector3D up = frame.LocalUp(far);
        Assert.AreEqual(50_000.0 / Radius, Math.Acos(up.Y), 1e-9, "its vertical tilts by s/R");
        Assert.AreEqual(0.6, up.X / Math.Sqrt(up.X * up.X + up.Z * up.Z), 1e-12, "toward the point");
        Assert.AreEqual(500.0 * Math.Cos(50_000.0 / Radius) - AerialPerspective.CurvatureDrop(Radius, 50_000.0), far.Y, 1e-6, "it sinks by the drop");
        Assert.AreEqual(50_000.0, Radius * Math.Atan2(Math.Sqrt(far.X * far.X + far.Z * far.Z), far.Y + Radius), 1e-6, "s along the ground");
    }

    [TestMethod]
    public void HeightAbove_KeepsItsMetres_PastTheHorizon()
    {
        // At 400 km the point lies 12.5 km below the tangent plane; the stable form still gives its height to the millimetre, also
        // when the position is rounded to float, as the GPU holds it.
        var frame = new EarthFrame(Radius, 0.0, 0.0);
        foreach (double h in new[] { 0.0, 1.0, 1234.5, 2400.0, -150.0 })
        {
            Vector3D p = frame.ToFrame(240_000.0, h, -320_000.0);
            Assert.AreEqual(h, frame.HeightAbove(p), 1e-6, $"{h} m");
            var rounded = new Vector3D((float)p.X, (float)p.Y, (float)p.Z);
            Assert.AreEqual(h, frame.HeightAbove(rounded), 0.05, $"{h} m from floats");
        }
    }

    [TestMethod]
    public void HorizonReach_AddsTheTwoHorizons()
    {
        var frame = new EarthFrame(Radius, 0.0, 0.0);
        double camera = Math.Sqrt(4200.0 * (2.0 * Radius + 4200.0));
        double terrain = Math.Sqrt(2400.0 * (2.0 * Radius + 2400.0));
        Assert.AreEqual(camera + terrain, frame.HorizonReach(4200.0, 2400.0), 1e-6);
        Assert.AreEqual(231_200.0, camera, 400.0, "4.2 km up sees 231 km");
        Assert.AreEqual(terrain, frame.HorizonReach(0.0, 2400.0), 1e-9);
    }
}
