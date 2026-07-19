using System;
using Pinball.Physics;

namespace Pinball.Tests;

/// <summary>
/// Narrowphase closest-feature tests for the finite cylinder and bounded quad colliders (pinball-plan
/// §6.1.2): posts / bumper bodies and wall panels / slingshot faces. Each analytic branch (lateral, cap
/// face, cap rim; quad face and clamped edge) is pinned so the impulse solver gets a correct normal.
/// </summary>
[TestClass]
public sealed class ColliderTests
{
    private const double R = PhysicsConstants.BallRadius;

    [TestMethod]
    public void Cylinder_LateralContact()
    {
        var cyl = new CylinderCollider(Vector3D.Zero, Vector3D.UnitY, 0.2, 0.05, PhysicsMaterial.Steel);
        ContactQuery q = cyl.Query(new Vector3D(0.10, 0.1, 0), R); // beside the tube at mid-height
        Assert.AreEqual(0.10 - 0.05 - R, q.Separation, 1e-12);
        Assert.AreEqual(1.0, Vector3D.Dot(q.Normal, Vector3D.UnitX), 1e-12);
    }

    [TestMethod]
    public void Cylinder_CapFaceContact()
    {
        var cyl = new CylinderCollider(Vector3D.Zero, Vector3D.UnitY, 0.2, 0.05, PhysicsMaterial.Steel);
        ContactQuery q = cyl.Query(new Vector3D(0.02, 0.30, 0), R); // above the top cap, within the radius
        Assert.AreEqual(0.10 - R, q.Separation, 1e-12); // 0.30 − 0.20 above the y=0.2 cap
        Assert.AreEqual(1.0, Vector3D.Dot(q.Normal, Vector3D.UnitY), 1e-12);
    }

    [TestMethod]
    public void Cylinder_CapRimContact()
    {
        var cyl = new CylinderCollider(Vector3D.Zero, Vector3D.UnitY, 0.2, 0.05, PhysicsMaterial.Steel);
        ContactQuery q = cyl.Query(new Vector3D(0.10, 0.30, 0), R); // above and beyond the radius ⇒ nearest the rim
        var rim = new Vector3D(0.05, 0.20, 0);
        double dist = (new Vector3D(0.10, 0.30, 0) - rim).Length();
        Assert.AreEqual(dist - R, q.Separation, 1e-12);
        Assert.IsTrue(q.Normal.X > 0 && q.Normal.Y > 0, "rim normal points up and out");
    }

    [TestMethod]
    public void Quad_FaceAndClampedEdge()
    {
        // A 1×1 patch in the XZ plane at the origin.
        var quad = new QuadCollider(Vector3D.Zero, new Vector3D(1, 0, 0), new Vector3D(0, 0, 1), PhysicsMaterial.Playfield);

        ContactQuery face = quad.Query(new Vector3D(0.5, 0.1, 0.5), R); // above the middle
        Assert.AreEqual(0.1 - R, face.Separation, 1e-12);
        Assert.AreEqual(1.0, Vector3D.Dot(face.Normal, Vector3D.UnitY), 1e-12);

        ContactQuery edge = quad.Query(new Vector3D(1.5, 0.1, 0.5), R); // beyond the +X edge ⇒ clamps to it
        double dist = Math.Sqrt(0.5 * 0.5 + 0.1 * 0.1);
        Assert.AreEqual(dist - R, edge.Separation, 1e-12);
        Assert.IsTrue(edge.Normal.X > 0, "edge normal leans out past the clamped edge");
    }
}
