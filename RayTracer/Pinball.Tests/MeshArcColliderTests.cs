using System;
using Pinball.Physics;

namespace Pinball.Tests;

/// <summary>Narrowphase tests for the mesh and swept-arc colliders (pinball-plan §6.1.2): ramps / plastics
/// and curved lane guides.</summary>
[TestClass]
public sealed class MeshArcColliderTests
{
    private const double R = PhysicsConstants.BallRadius;

    [TestMethod]
    public void Mesh_ClosestOverTriangles()
    {
        // A unit quad in the XZ plane as two triangles.
        var tris = new (Vector3D, Vector3D, Vector3D)[]
        {
            (Vector3D.Zero, new Vector3D(1, 0, 0), new Vector3D(0, 0, 1)),
            (new Vector3D(1, 0, 0), new Vector3D(1, 0, 1), new Vector3D(0, 0, 1)),
        };
        var mesh = new MeshCollider(tris, PhysicsMaterial.Steel);

        ContactQuery above = mesh.Query(new Vector3D(0.5, 0.1, 0.5), R); // above the middle
        Assert.AreEqual(0.1 - R, above.Separation, 1e-9);
        Assert.AreEqual(1.0, Vector3D.Dot(above.Normal, Vector3D.UnitY), 1e-9);

        ContactQuery corner = mesh.Query(new Vector3D(-0.1, 0.0, -0.1), R); // off the (0,0,0) corner
        double dist = new Vector3D(-0.1, 0.0, -0.1).Length();
        Assert.AreEqual(dist - R, corner.Separation, 1e-9);
    }

    [TestMethod]
    public void Arc_ClosestOnQuarterArcWithAngleClamp()
    {
        // Quarter arc in the XZ plane, radius 0.5, tube 0.02, from angle 0 (+X) to π/2 (+Z).
        var arc = new ArcCollider(Vector3D.Zero, Vector3D.UnitX, Vector3D.UnitZ, 0.5, 0.02, 0, Math.PI / 2, PhysicsMaterial.Steel);

        ContactQuery onArc = arc.Query(new Vector3D(0.5, 0.1, 0), R); // straight above the angle-0 point
        Assert.AreEqual(0.1 - 0.02 - R, onArc.Separation, 1e-9);
        Assert.AreEqual(1.0, Vector3D.Dot(onArc.Normal, Vector3D.UnitY), 1e-9);

        // Past the π/2 end ⇒ the angle clamps to the arc tip at (0,0,0.5).
        ContactQuery pastEnd = arc.Query(new Vector3D(-0.1, 0.1, 0.55), R);
        Vector3D tip = new(0, 0, 0.5);
        double dist = (new Vector3D(-0.1, 0.1, 0.55) - tip).Length();
        Assert.AreEqual(dist - 0.02 - R, pastEnd.Separation, 1e-9);
    }

    [TestMethod]
    public void Arc_SpanningMoreThanPi_ResolvesToNearEndpointNotFarOne()
    {
        // A 270° arc in the XY plane. A ball just PAST the 270° end (at 280°) must contact the near (270°)
        // endpoint, not wrap-clamp to the far (0°) one — the bug that let a ball tunnel through a loop guide.
        var arc = new ArcCollider(Vector3D.Zero, Vector3D.UnitX, Vector3D.UnitY, 0.1, 0.01, 0, 4.712389, PhysicsMaterial.Steel);
        var ball = new Vector3D(0.1 * Math.Cos(280 * Math.PI / 180), 0.1 * Math.Sin(280 * Math.PI / 180), 0);
        ContactQuery q = arc.Query(ball, R);

        Vector3D nearEnd = new(0, -0.1, 0); // 270° (cos(3π/2) is ~−4e-9, not exactly 0, hence the 1e-6 band)
        double dist = (ball - nearEnd).Length();
        Assert.AreEqual(dist - 0.01 - R, q.Separation, 1e-6, "resolves to the 270° endpoint");
        Assert.IsTrue(q.Separation < 0, "the ball is in contact (would have been a missed contact / tunnel before)");
    }

    [TestMethod]
    public void Mesh_EmptyList_ReportsNoContactNotAPhantomAtTheCentre()
    {
        var empty = new MeshCollider(Array.Empty<(Vector3D, Vector3D, Vector3D)>(), PhysicsMaterial.Steel);
        ContactQuery q = empty.Query(new Vector3D(0, 0, 0), R); // a ball sitting on the world origin
        Assert.IsTrue(q.Separation > 0, "an empty mesh must not fabricate a penetrating contact at the ball centre");
    }
}
