using System;
using Pinball.Physics;

namespace Pinball.Tests;

/// <summary>
/// Per-responder tests (pinball-plan §6.1.6): the flipper (push + spin from the sweeping surface), the
/// plunger (spring launch), the bumper/slingshot active-impulse zone (adds energy above a trigger), and
/// nudge/tilt. Each asserts the mechanism physically, deterministically, with no gravity so the responder
/// is isolated.
/// </summary>
[TestClass]
public sealed class ActuatorTests
{
    private const double R = PhysicsConstants.BallRadius;
    private const double H = PhysicsConstants.SubstepSeconds;
    private static PhysicsSettings NoGravity => new(0, EnableGravity: false, EnableDrag: false, EnableMagnus: false, EnableRollingResistance: false);

    // ── Flipper: surface velocity is ω×r, and an energised sweep launches the ball with push AND spin. ──
    [TestMethod]
    public void Flipper_SurfaceVelocityIsOmegaCrossR()
    {
        var flip = new Flipper(Vector3D.Zero, Vector3D.UnitY, Vector3D.UnitX, 0.12, 0.01,
            restAngle: 0, endAngle: 1.0, material: PhysicsMaterial.Rubber);
        flip.Energized = true;
        flip.Advance(H); // build a little angular velocity

        double omega = flip.AngularVelocity;
        Assert.IsTrue(omega > 0, "energising drives the bat");
        double tipSpeed = flip.SurfaceVelocity(flip.PointB).Length();
        Assert.AreEqual(omega * flip.Length, tipSpeed, omega * flip.Length * 1e-9, "|u_tip| = ω·L");
    }

    [TestMethod]
    public void Flipper_EnergisedSweepLaunchesBallWithSpin()
    {
        // Bat along +X at rest; the ball just touches its −Z face. Energising sweeps +θ (surface moves −Z),
        // so the ball is driven in −Z and, from tangential friction against the sweep, spun up.
        var flip = new Flipper(Vector3D.Zero, Vector3D.UnitY, Vector3D.UnitX, 0.15, 0.02,
            restAngle: 0, endAngle: 1.2, material: PhysicsMaterial.Rubber);
        var world = new PhysicsWorld(NoGravity, new ICollider[] { flip });
        world.Ball = BallState.AtRest(new Vector3D(0.10, 0, -(0.02 + R))); // touching the bat's −Z side

        flip.Energized = true;
        for (int i = 0; i < 40; i++)
            world.Substep();

        BallState b = world.Ball;
        Assert.IsTrue(b.LinearVelocity.Length() > 0.5, $"ball launched ({b.LinearVelocity.Length():0.00} m/s)");
        Assert.IsTrue(b.LinearVelocity.Z < 0, "driven in the sweep (−Z) direction");
        Assert.IsTrue(b.AngularVelocity.Length() > 1.0, "acquired spin from the sweeping surface (skill mechanic)");
    }

    // ── Plunger: spring launch scales with pull, roughly matching the ideal spring speed. ──
    [TestMethod]
    public void Plunger_SpringLaunchScalesWithPull()
    {
        static double LaunchSpeed(double pull)
        {
            var plunger = new Plunger(Vector3D.UnitZ, Vector3D.Zero, springConstant: 81000, plungerMass: 10.0, material: PhysicsMaterial.Steel);
            var world = new PhysicsWorld(NoGravity, new ICollider[] { plunger });
            world.Ball = BallState.AtRest(new Vector3D(0, 0, -pull + R)); // resting against the charged tip face
            plunger.Charge(pull);
            plunger.Release();
            for (int i = 0; i < 200; i++)
                world.Substep();
            return world.Ball.LinearVelocity.Z;
        }

        double ideal = 0.05 * Math.Sqrt(81000 / 10.0); // 4.5 m/s
        double fast = LaunchSpeed(0.05);
        double slow = LaunchSpeed(0.025);

        Assert.IsTrue(fast > 0, "launches forward (+Z)");
        Assert.AreEqual(ideal, fast, ideal * 0.20, "≈ ideal spring launch speed for a heavy plunger");
        Assert.IsTrue(slow < fast * 0.75, "half the pull launches markedly slower");
    }

    // ── Bumper (active impulse): above the trigger the ball leaves faster than it arrived; below, it's passive. ──
    [TestMethod]
    public void Bumper_AddsEnergyAboveTriggerAndIsPassiveBelow()
    {
        static double OutgoingSpeed(double incoming)
        {
            var post = new CylinderCollider(Vector3D.Zero, Vector3D.UnitY, 0.1, 0.03, PhysicsMaterial.Rubber);
            var bumper = new ActiveZone(post, impulseStrength: 0.2, triggerSpeed: 1.0);
            var world = new PhysicsWorld(NoGravity, new ICollider[] { bumper });
            world.Ball = new BallState
            {
                Position = new Vector3D(-0.10, 0.05, 0),
                Orientation = QuaternionD.Identity,
                LinearVelocity = new Vector3D(incoming, 0, 0), // fired at the post
            };
            for (int i = 0; i < 300; i++) // enough for the slow ball to reach the post and rebound
                world.Substep();
            return world.Ball.LinearVelocity.Length();
        }

        Assert.IsTrue(OutgoingSpeed(3.0) > 3.0, "coil fires above the trigger ⇒ leaves with more energy");
        Assert.IsTrue(OutgoingSpeed(0.5) < 0.5, "below the trigger ⇒ passive restitution loses energy");
    }

    // ── Nudge / tilt: a nudge kicks the ball (Δv = J/m) and charges the tilt bob. ──
    [TestMethod]
    public void Nudge_KicksBallAndChargesTilt()
    {
        var world = new PhysicsWorld(NoGravity, Array.Empty<ICollider>());
        world.Ball = BallState.AtRest(Vector3D.Zero);
        var impulse = new Vector3D(0.05, 0, 0);
        world.Nudge(impulse);

        Assert.AreEqual(0.05 / PhysicsConstants.BallMass, world.Ball.LinearVelocity.X, 1e-9, "Δv = J/m");
        Assert.AreEqual(0.05, world.Tilt.Level, 1e-9, "nudge charges the tilt bob");
        Assert.IsFalse(world.Tilt.Tripped, "one modest nudge does not tilt");
    }

    [TestMethod]
    public void TiltSensor_TripsOnAccumulationAndLeaksOtherwise()
    {
        var sensor = new TiltSensor(threshold: 0.10, leakPerSecond: 0.05);
        sensor.Bump(0.03); sensor.Bump(0.03); sensor.Bump(0.03);
        Assert.IsFalse(sensor.Tripped, "0.09 < 0.10 — not yet");
        sensor.Bump(0.03);
        Assert.IsTrue(sensor.Tripped, "0.12 ≥ 0.10 — TILT");

        // A single nudge that fully leaks away before the next never accumulates.
        var relaxed = new TiltSensor(threshold: 0.10, leakPerSecond: 0.05);
        relaxed.Bump(0.03);
        relaxed.Decay(1.0); // 0.03 − 0.05 → clamped to 0
        Assert.AreEqual(0.0, relaxed.Level, 1e-12);
        relaxed.Bump(0.03);
        Assert.IsFalse(relaxed.Tripped, "spaced-out nudges are safe");
    }
}
