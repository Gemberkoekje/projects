using System;
using Pinball.Physics;

namespace Pinball.Tests;

/// <summary>
/// Robustness tests targeting the numerical hard cases of pinball-plan §6.1.9 and the exact configuration the
/// shipped table runs — the gaps a P5 adversarial review surfaced. These complement the §6.1.8 closed-form
/// suite: the flat-playfield tilted-gravity path the P0 table actually uses, no-energy-injection at real
/// contacts (restitution + friction + rolling), resting-ball stability, and fast multi-contact in a tight lane.
/// </summary>
[TestClass]
public sealed class PhysicsRobustnessTests
{
    private const double R = PhysicsConstants.BallRadius;
    private const double G = PhysicsConstants.Gravity;
    private static readonly double Alpha = PhysicsConstants.DefaultInclineRadians;

    private static void Run(PhysicsWorld w, int n) { for (int i = 0; i < n; i++) w.Substep(); }

    // The SHIPPED table's incline model (flat playfield + tilted GravityOverride) must also produce the
    // rolling 5/7·g·sinα — the closed-form §6.1.8 tests only exercised the tilted-plane path, so a sign/factor
    // error in the z-gravity component would otherwise be validated by nothing.
    [TestMethod]
    public void FlatPlayfield_TiltedGravity_RollsAtFiveSevenths()
    {
        var settings = new PhysicsSettings(Alpha,
            EnableDrag: false, EnableMagnus: false, EnableRollingResistance: false,
            GravityOverride: new Vector3D(0, -G * Math.Cos(Alpha), -G * Math.Sin(Alpha)));
        var world = new PhysicsWorld(settings, new[] { new PlaneCollider(Vector3D.Zero, Vector3D.UnitY, PhysicsMaterial.Playfield) });
        world.Ball = BallState.AtRest(new Vector3D(0, R, 0));

        Run(world, 100);
        double vz1 = world.Ball.LinearVelocity.Z; // down-slope is −z here
        Run(world, 100);
        double vz2 = world.Ball.LinearVelocity.Z;

        double accel = -(vz2 - vz1) / (100 * PhysicsConstants.SubstepSeconds);
        double expected = 5.0 / 7.0 * G * Math.Sin(Alpha);
        Assert.AreEqual(expected, accel, expected * 0.02, "the flat-playfield GravityOverride path also gives 5/7·g·sinα");
        Assert.IsTrue(world.Ball.LinearVelocity.Z < 0, "drains toward −z (the flippers)");
    }

    // Real contacts (restitution + friction + rolling resistance) must DISSIPATE, never inject — the bounded
    // ballistic energy test can't see collision-energy bugs. Also guards the rolling-resistance fix.
    [TestMethod]
    public void ContactEnergy_NeverIncreasesAcrossBouncesAndRolling()
    {
        var plane = new PlaneCollider(Vector3D.Zero, Vector3D.UnitY, new PhysicsMaterial(0.6, 0.18, 0.15, 0.002));
        var settings = PhysicsSettings.Default with { EnableDrag = false, EnableMagnus = false, EnableRollingResistance = true };
        var world = new PhysicsWorld(settings, new[] { plane });
        world.Ball = new BallState
        {
            Position = new Vector3D(0, 0.3, 0),
            Orientation = QuaternionD.Identity,
            LinearVelocity = new Vector3D(1.2, 0, 0.5),
            AngularVelocity = new Vector3D(2, 0, -3),
        };

        double e0 = world.Ball.MechanicalEnergy(Vector3D.UnitY, 0.0);
        double maxE = e0;
        for (int i = 0; i < 6000; i++) // 6 s of bouncing → rolling → settling
        {
            world.Substep();
            maxE = Math.Max(maxE, world.Ball.MechanicalEnergy(Vector3D.UnitY, 0.0));
        }
        double eFinal = world.Ball.MechanicalEnergy(Vector3D.UnitY, 0.0);

        Assert.IsTrue(maxE <= e0 * 1.02, $"no energy injection at contacts (max {maxE / e0:P1} of start)");
        Assert.IsTrue(eFinal < e0 * 0.90, "restitution + friction + rolling resistance dissipate energy");
    }

    // §6.1.9 resting stability: a ball on the flat playfield under vertical gravity must sit still — no jitter,
    // no slow sink, no phantom spin — over a long run.
    [TestMethod]
    public void RestingBall_DoesNotDriftOrCreep()
    {
        var settings = PhysicsSettings.Default.WithForcesOff(); // gravity vertical (0,−g,0), no drag/magnus/rolling
        var world = new PhysicsWorld(settings, new[] { new PlaneCollider(Vector3D.Zero, Vector3D.UnitY, PhysicsMaterial.Playfield) });
        var start = new Vector3D(0, R, 0);
        world.Ball = BallState.AtRest(start);

        Run(world, 5000); // 5 s
        BallState b = world.Ball;
        Assert.IsTrue((b.Position - start).Length() < 0.001, "no resting drift (< 1 mm over 5 s)");
        Assert.IsTrue(b.LinearVelocity.Length() < 0.01, "stays at rest");
        Assert.IsTrue(b.AngularVelocity.Length() < 0.01, "no phantom spin");
    }

    // §6.1.9 "the single biggest source of impossible bugs": a fast ball pinging down a narrow channel touches
    // both walls in quick succession — it must stay contained (no tunnel) and never gain energy.
    [TestMethod]
    public void TightLane_FastBallStaysContainedAndGainsNoEnergy()
    {
        double d = R + 0.004; // wall offset ⇒ ~4 mm clearance each side
        var elastic = new PhysicsMaterial(1.0, 0, 0, 0);
        var settings = new PhysicsSettings(0, EnableGravity: false, EnableDrag: false, EnableMagnus: false, EnableRollingResistance: false);
        var world = new PhysicsWorld(settings, new[]
        {
            new PlaneCollider(new Vector3D(-d, 0, 0), Vector3D.UnitX, elastic),  // left wall
            new PlaneCollider(new Vector3D(d, 0, 0), -Vector3D.UnitX, elastic),  // right wall
        });
        world.Ball = new BallState
        {
            Orientation = QuaternionD.Identity,
            LinearVelocity = new Vector3D(3.0, 0, 8.0), // 3 m/s across a 4 mm gap ⇒ a bounce roughly every step
        };
        double v0 = world.Ball.LinearVelocity.Length();

        double maxX = 0, maxV = 0;
        for (int i = 0; i < 500; i++)
        {
            world.Substep();
            Assert.IsTrue(double.IsFinite(world.Ball.Position.X) && double.IsFinite(world.Ball.LinearVelocity.X), "no NaN");
            maxX = Math.Max(maxX, Math.Abs(world.Ball.Position.X));
            maxV = Math.Max(maxV, world.Ball.LinearVelocity.Length());
        }

        Assert.IsTrue(maxX < d, $"ball centre never crosses a wall (max |x| {maxX:0.0000} < {d:0.0000})");
        Assert.IsTrue(maxV <= v0 * 1.01, $"no energy gain from the multi-contact channel ({maxV:0.00} vs {v0:0.00} m/s)");
    }
}
