using System;
using Pinball.Physics;

namespace Pinball.Tests;

/// <summary>
/// Unit tests for the physics value types and collider narrowphase (pinball-plan §6.1.1–§6.1.2): the
/// double-precision vector/quaternion algebra and each collider's closest-feature query, which the impulse
/// solver and CCD depend on being exact.
/// </summary>
[TestClass]
public sealed class PhysicsPrimitivesTests
{
    private const double R = PhysicsConstants.BallRadius;

    [TestMethod]
    public void Vector3D_DotCrossNormalize()
    {
        var a = new Vector3D(1, 2, 3);
        var b = new Vector3D(4, 5, 6);
        Assert.AreEqual(32.0, Vector3D.Dot(a, b), 1e-12);
        Vector3D c = Vector3D.Cross(a, b); // (-3, 6, -3)
        Assert.AreEqual(-3.0, c.X, 1e-12);
        Assert.AreEqual(6.0, c.Y, 1e-12);
        Assert.AreEqual(-3.0, c.Z, 1e-12);
        Assert.AreEqual(0.0, Vector3D.Dot(c, a), 1e-12); // cross ⟂ both
        Assert.AreEqual(1.0, new Vector3D(0, 3, 0).Normalized().Length(), 1e-12);
        Assert.AreEqual(Vector3D.Zero, Vector3D.Zero.Normalized()); // degenerate is safe
    }

    [TestMethod]
    public void QuaternionD_IntegrationStaysUnitAndSpinsCorrectly()
    {
        // Spin about +Y at 1 rad/s for 1 s ⇒ ~1 rad rotation, quaternion stays unit.
        var q = QuaternionD.Identity;
        var omega = new Vector3D(0, 1, 0);
        for (int i = 0; i < 1000; i++)
            q = q.IntegrateAngularVelocity(omega, 0.001);
        Assert.AreEqual(1.0, q.Length(), 1e-9, "orientation renormalises to unit");

        Vector3D x = q.Rotate(Vector3D.UnitX); // X rotated ~1 rad about Y → ~(cos1, 0, -sin1)
        Assert.AreEqual(Math.Cos(1.0), x.X, 1e-3);
        Assert.AreEqual(-Math.Sin(1.0), x.Z, 1e-3);
    }

    [TestMethod]
    public void PlaneCollider_SignedContact()
    {
        var plane = new PlaneCollider(Vector3D.Zero, Vector3D.UnitY, PhysicsMaterial.Playfield);
        ContactQuery q = plane.Query(new Vector3D(3, R + 0.01, -2), R); // 1 cm above resting height
        Assert.AreEqual(0.01, q.Separation, 1e-12);
        Assert.AreEqual(Vector3D.UnitY, q.Normal);
        // The contact point is the lowest point of the ball's surface (centre − r·n), 1 cm above the plane.
        Assert.AreEqual(0.01, q.ContactPoint.Y, 1e-12);
    }

    [TestMethod]
    public void SphereCollider_CentreDistanceContact()
    {
        var sphere = new SphereCollider(new Vector3D(0, 0, 0), 0.05, PhysicsMaterial.Steel);
        ContactQuery q = sphere.Query(new Vector3D(0.10, 0, 0), R); // centres 0.10 apart
        Assert.AreEqual(0.10 - (0.05 + R), q.Separation, 1e-12);
        Assert.AreEqual(Vector3D.UnitX, q.Normal);
    }

    [TestMethod]
    public void CapsuleCollider_ClosestOnSegmentAndCaps()
    {
        // Segment along X from (0,0,0) to (1,0,0), tube radius 0.02.
        var capsule = new CapsuleCollider(Vector3D.Zero, new Vector3D(1, 0, 0), 0.02, PhysicsMaterial.Rubber);

        // Ball beside the middle of the segment: normal is the perpendicular (+Y), separation is the radial gap.
        ContactQuery mid = capsule.Query(new Vector3D(0.5, 0.10, 0), R);
        Assert.AreEqual(0.10 - (0.02 + R), mid.Separation, 1e-12);
        Assert.AreEqual(Vector3D.UnitY, mid.Normal);

        // Ball beyond the far cap: closest feature is the endpoint (spherical cap).
        ContactQuery beyond = capsule.Query(new Vector3D(1.10, 0, 0), R);
        Assert.AreEqual(0.10 - (0.02 + R), beyond.Separation, 1e-12);
        Assert.AreEqual(Vector3D.UnitX, beyond.Normal);
    }

    [TestMethod]
    public void DeterministicRandom_SameSeedSameSequence()
    {
        var a = new DeterministicRandom(12345);
        var b = new DeterministicRandom(12345);
        var c = new DeterministicRandom(99999);
        for (int i = 0; i < 100; i++)
            Assert.AreEqual(a.NextUInt64(), b.NextUInt64(), "same seed ⇒ same stream");
        Assert.AreNotEqual(a.NextUInt64(), c.NextUInt64(), "different seed ⇒ different stream");
        double d = new DeterministicRandom(7).NextDouble();
        Assert.IsTrue(d is >= 0.0 and < 1.0, "NextDouble in [0,1)");
    }
}
