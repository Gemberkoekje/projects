using System;
using Pinball.Physics;

namespace Pinball.Tests;

/// <summary>
/// Narrowphase + emergent tests for <see cref="BezierWallCollider"/> and <see cref="CubicBezierD"/>
/// (pinball-plan §6.1.2): the closest-feature contract on a known-geometry wall, near-endpoint clamping (no
/// tunnelling to the far end), and a ball guided by a curved wall staying contained and gaining no energy.
/// </summary>
[TestClass]
public sealed class BezierColliderTests
{
    private const double R = PhysicsConstants.BallRadius;

    private static void Run(PhysicsWorld w, int n) { for (int i = 0; i < n; i++) w.Substep(); }

    // A straight, evenly-parametrised curve along +x at z=0 — a curve whose geometry we know exactly.
    private static CubicBezierD StraightAlongX(double length) => new(
        new Vector3D(0, 0, 0), new Vector3D(length / 3, 0, 0),
        new Vector3D(2 * length / 3, 0, 0), new Vector3D(length, 0, 0));

    [TestMethod]
    public void CubicBezier_Evaluate_HitsEndpointsAndMidpoint()
    {
        var c = StraightAlongX(1.0);
        Assert.AreEqual(0.0, (c.Evaluate(0) - c.P0).Length(), 1e-12, "t=0 is P0");
        Assert.AreEqual(0.0, (c.Evaluate(1) - c.P3).Length(), 1e-12, "t=1 is P3");
        // Evenly-spaced collinear control points ⇒ uniform straight line: Evaluate(t) = (t,0,0).
        Assert.AreEqual(0.0, (c.Evaluate(0.5) - new Vector3D(0.5, 0, 0)).Length(), 1e-12);
    }

    [TestMethod]
    public void BezierWall_ClosestFeature_SeparationNormalAndContactPoint()
    {
        double tube = 0.02, height = 0.5;
        var col = new BezierWallCollider(StraightAlongX(1.0), 0, height, 8, tube, PhysicsMaterial.Steel);

        // Ball offset +z from the wall face, at a height inside [0,height] so the closest point is on the face.
        Vector3D surfacePoint = new(0.5, 0.1, 0.0);   // on the wall
        Vector3D outward = Vector3D.UnitZ;             // unit direction away from the wall
        double gap = 0.3;
        Vector3D ballCenter = surfacePoint + outward * gap;
        ContactQuery q = col.Query(ballCenter, R);

        Assert.AreEqual(gap - tube - R, q.Separation, 1e-9, "separation = |centre-surface| - halfThickness - r");
        Assert.AreEqual(1.0, Vector3D.Dot(q.Normal, outward), 1e-9, "normal points surface -> ball centre");
        Assert.AreEqual(0.0, (q.ContactPoint - (ballCenter - outward * R)).Length(), 1e-9, "contact on the ball surface");
    }

    [TestMethod]
    public void BezierWall_TwoSided_NormalFlipsToTheBallSide()
    {
        var col = new BezierWallCollider(StraightAlongX(1.0), 0, 0.5, 8, 0.0, PhysicsMaterial.Steel);
        ContactQuery front = col.Query(new Vector3D(0.5, 0.1, 0.3), R);  // +z side
        ContactQuery back = col.Query(new Vector3D(0.5, 0.1, -0.3), R);  // -z side
        Assert.IsTrue(Vector3D.Dot(front.Normal, Vector3D.UnitZ) > 0.99, "front normal points +z");
        Assert.IsTrue(Vector3D.Dot(back.Normal, Vector3D.UnitZ) < -0.99, "back normal points -z");
    }

    [TestMethod]
    public void BezierWall_JustPastEnd_ClampsToNearEndpointNotFarOne()
    {
        double tube = 0.01;
        var col = new BezierWallCollider(StraightAlongX(1.0), 0, 0.5, 8, tube, PhysicsMaterial.Steel);
        // Ball 0.1 past the +x (t=1) end at (1,0,0). The near endpoint is (1,·,0); the far one is (0,·,0).
        Vector3D nearTip = new(1.0, 0.1, 0.0);
        Vector3D ball = nearTip + new Vector3D(0.1, 0, 0);
        ContactQuery q = col.Query(ball, R);
        Assert.AreEqual(0.1 - tube - R, q.Separation, 1e-9, "resolves to the NEAR endpoint, not the far one 1.1 away");
    }

    [TestMethod]
    public void BezierWall_Bounds_ContainTheCurveAndHeight()
    {
        double tube = 0.03, height = 0.4;
        var col = new BezierWallCollider(StraightAlongX(1.0), 0, height, 12, tube, PhysicsMaterial.Steel);
        AabbD b = col.Bounds;
        // Curve spans x∈[0,1], z=0, y∈[0,height], inflated by tube on all sides.
        Assert.IsTrue(b.Min.X <= 0 - tube + 1e-12 && b.Max.X >= 1 + tube - 1e-12, "x range covers the curve + tube");
        Assert.IsTrue(b.Min.Y <= -tube + 1e-12 && b.Max.Y >= height + tube - 1e-12, "y range covers [0,height] + tube");
        Assert.IsTrue(b.Min.Z <= -tube + 1e-12 && b.Max.Z >= tube - 1e-12, "z range covers the curve + tube");
    }

    [TestMethod]
    public void BezierWall_GuidesABall_StaysContainedAndGainsNoEnergy()
    {
        // A quarter-circle guide (radius 0.5, centre origin) from (0.5,0) round to (0,0.5); its concave face
        // looks toward the origin, so a ball inside gets swept along it.
        const double rad = 0.5, k = 0.5523;
        var curve = new CubicBezierD(
            new Vector3D(rad, 0, 0), new Vector3D(rad, 0, rad * k),
            new Vector3D(rad * k, 0, rad), new Vector3D(0, 0, rad));
        var guide = new BezierWallCollider(curve, 0, 0.15, 16, 0.0, PhysicsMaterial.Steel);

        // Flat frame, vertical gravity only, so the wall is the only shaping force and the ball hugs the floor.
        var settings = PhysicsSettings.Default.WithForcesOff()
            with { GravityOverride = new Vector3D(0, -PhysicsConstants.Gravity, 0) };
        var world = new PhysicsWorld(settings, new ICollider[]
        {
            new PlaneCollider(Vector3D.Zero, Vector3D.UnitY, PhysicsMaterial.Playfield),
            guide,
        });

        // Fire the ball from inside the curve outward toward the wall (+x), on the floor.
        world.Ball = new BallState
        {
            Position = new Vector3D(0.2, R, 0.15),
            Orientation = QuaternionD.Identity,
            LinearVelocity = new Vector3D(1.2, 0, 0),
        };

        double ke0 = 0.5 * PhysicsConstants.BallMass * world.Ball.LinearVelocity.LengthSquared();
        double maxKe = ke0, maxPenetration = 0, maxReach = 0;
        for (int i = 0; i < 2000; i++)
        {
            world.Substep();
            BallState b = world.Ball;
            Assert.IsTrue(double.IsFinite(b.Position.X) && double.IsFinite(b.LinearVelocity.X), "no NaN");
            maxKe = Math.Max(maxKe, 0.5 * PhysicsConstants.BallMass * b.LinearVelocity.LengthSquared());
            maxPenetration = Math.Max(maxPenetration, -guide.Query(b.Position, R).Separation);
            // Distance from the arc centre (origin) in XZ: the wall should hold it near/inside rad + r.
            maxReach = Math.Max(maxReach, Math.Sqrt(b.Position.X * b.Position.X + b.Position.Z * b.Position.Z));
        }

        Assert.IsTrue(maxPenetration < 0.003, $"stays on the guide, no tunnel (max {maxPenetration * 1000:0.0} mm)");
        Assert.IsTrue(maxKe <= ke0 * 1.02, "a passive curved wall injects no energy");
        Assert.IsTrue(maxReach < rad + R + 0.02, $"contained by the guide (max reach {maxReach:0.000} m)");
    }
}
