using System;
using System.Collections.Generic;
using Pinball.Physics;

namespace Pinball.Tests;

/// <summary>
/// The deterministic, closed-form physics validation suite (pinball-plan §6.1.8) — how realism is proven,
/// in the repo's fixed-seed style. Each test asserts the simulation against an analytic result computed from
/// the SI constants, within a tolerance scaled to the fixed step <c>h</c>. Velocity-vs-time assertions are
/// tight (semi-implicit Euler is exact in velocity for constant acceleration); distance/position tolerances
/// carry the bounded <c>O(a·h·t)</c> offset.
/// </summary>
[TestClass]
public sealed class PhysicsValidationTests
{
    private static readonly double Alpha = PhysicsConstants.DefaultInclineRadians;
    private const double G = PhysicsConstants.Gravity;
    private const double R = PhysicsConstants.BallRadius;

    // Down-slope unit direction for the InclinedPlayfield (normal (0,cosα,sinα)).
    private static Vector3D DownSlope => new Vector3D(0, -Math.Sin(Alpha), Math.Cos(Alpha)).Normalized();
    private static Vector3D InclineNormal => new(0, Math.Cos(Alpha), Math.Sin(Alpha));

    private static void Run(PhysicsWorld world, int steps)
    {
        for (int i = 0; i < steps; i++)
            world.Substep();
    }

    // ── #1 Frictionless incline reaches the analytic speed g·sinα·t. ──
    [TestMethod]
    public void FrictionlessIncline_ReachesAnalyticSpeed()
    {
        var frictionless = new PhysicsMaterial(0.0, 0.0, 0.0, 0.0);
        var world = new PhysicsWorld(
            PhysicsSettings.Default.WithForcesOff() with { InclineRadians = Alpha },
            new[] { PlaneCollider.InclinedPlayfield(Alpha, frictionless) });
        world.Ball = BallState.AtRest(InclineNormal * R); // resting on the plane at the origin

        const int steps = 500; // 0.5 s
        Run(world, steps);

        double t = steps * PhysicsConstants.SubstepSeconds;
        double expectedSpeed = G * Math.Sin(Alpha) * t; // = 1.110 m/s² · t
        double speed = world.Ball.LinearVelocity.Length();
        Assert.AreEqual(expectedSpeed, speed, expectedSpeed * 1e-3, "|v| should equal g·sinα·t (tight)");

        // Motion is purely down-slope, and a frictionless slide induces no spin.
        Assert.IsTrue(Vector3D.Dot(world.Ball.LinearVelocity.Normalized(), DownSlope) > 0.9999, "direction is down-slope");
        Assert.AreEqual(0.0, world.Ball.AngularVelocity.Length(), 1e-9, "frictionless ⇒ no spin");
    }

    // ── #2 Energy conservation (frictionless, no drag): a ballistic arc holds mechanical energy. ──
    [TestMethod]
    public void BallisticArc_ConservesMechanicalEnergy()
    {
        var settings = PhysicsSettings.Default.WithForcesOff();
        var world = new PhysicsWorld(settings, Array.Empty<ICollider>());
        world.Ball = new BallState
        {
            Position = new Vector3D(0, 0, 0),
            Orientation = QuaternionD.Identity,
            LinearVelocity = new Vector3D(1.0, 5.0, 0.0), // launched up and along
            AngularVelocity = Vector3D.Zero,
        };

        double e0 = world.Ball.MechanicalEnergy(Vector3D.UnitY, 0.0);
        double maxDev = 0, eStart = e0, eMid = e0, eEnd = e0;
        const int steps = 1000; // 1 s arc (no colliders to hit)
        for (int i = 0; i < steps; i++)
        {
            world.Substep();
            double e = world.Ball.MechanicalEnergy(Vector3D.UnitY, 0.0);
            maxDev = Math.Max(maxDev, Math.Abs(e - e0) / e0);
            if (i == steps / 2) eMid = e;
            if (i == steps - 1) eEnd = e;
        }

        Assert.IsTrue(maxDev < 0.01, $"energy drift {maxDev:P3} should stay bounded (symplectic Euler)");
        // No secular growth: end and mid energies match the start within the same bounded band.
        Assert.IsTrue(Math.Abs(eEnd - eStart) / e0 < 0.005, "no net energy drift over the arc");
        Assert.IsTrue(Math.Abs(eMid - eStart) / e0 < 0.01, "no secular growth mid-arc");
    }

    // ── #3a Rolling steady state on the incline: 5/7·g·sinα, not g·sinα (the solid-sphere factor). ──
    [TestMethod]
    public void RollingOnIncline_SteadyAccelerationIsFiveSevenths()
    {
        var world = new PhysicsWorld(
            PhysicsSettings.Default.WithForcesOff() with { InclineRadians = Alpha },
            new[] { PlaneCollider.InclinedPlayfield(Alpha, PhysicsMaterial.Playfield) });
        world.Ball = BallState.AtRest(InclineNormal * R);

        Run(world, 100); // 0.1 s
        double v1 = Vector3D.Dot(world.Ball.LinearVelocity, DownSlope);
        Run(world, 100); // to 0.2 s
        double v2 = Vector3D.Dot(world.Ball.LinearVelocity, DownSlope);

        double measuredAccel = (v2 - v1) / (100 * PhysicsConstants.SubstepSeconds);
        double expected = 5.0 / 7.0 * G * Math.Sin(Alpha); // 0.793 m/s²
        Assert.AreEqual(expected, measuredAccel, expected * 0.02, "rolling accel is 5/7·g·sinα");

        // Rolling without slipping: |v| = |ω|·r and the contact-point velocity vanishes.
        BallState b = world.Ball;
        Assert.AreEqual(b.LinearVelocity.Length(), b.AngularVelocity.Length() * R, b.LinearVelocity.Length() * 0.02, "|v| = |ω|r");
        Vector3D rc = -InclineNormal * R;
        Vector3D contactVel = b.LinearVelocity + Vector3D.Cross(b.AngularVelocity, rc);
        Assert.IsTrue(contactVel.Length() < 0.01, "contact-point velocity ≈ 0 (rolling)");
    }

    // ── #3b Skid-to-roll on the flat: a struck-flat ball catches into rolling at v = 5/7·v₀. ──
    [TestMethod]
    public void SkidToRoll_CatchesAtFiveSeventhsOfInitialSpeed()
    {
        var plane = new PlaneCollider(Vector3D.Zero, Vector3D.UnitY, PhysicsMaterial.Playfield);
        var world = new PhysicsWorld(PhysicsSettings.Default.WithForcesOff(), new[] { plane });
        world.Ball = new BallState
        {
            Position = new Vector3D(0, R, 0),
            Orientation = QuaternionD.Identity,
            LinearVelocity = new Vector3D(1.0, 0, 0), // skidding: translating with no spin
            AngularVelocity = Vector3D.Zero,
        };

        Run(world, 2000); // 2 s to settle
        BallState b = world.Ball;
        // Angular momentum about the contact conserves ⇒ v_f = (5/7)·v₀ once rolling.
        Assert.AreEqual(5.0 / 7.0, b.LinearVelocity.Length(), 0.02, "catches into rolling at 5/7·v₀");
        Assert.AreEqual(b.LinearVelocity.Length(), b.AngularVelocity.Length() * R, 0.02, "|v| = |ω|r once rolling");
    }

    // ── #4 Backspin Magnus lateral curve — small, and matching a FROZEN prediction. ──
    // Goldens are hand-computed ½·a·t² deflections (m) from the SI constants, frozen as literals so a change
    // to the Magnus model (constant, power, clamp) breaks the test — unlike recomputing them from the same
    // constants each run, which cannot fail. Two rows: high spin hits the C_L clamp; low spin stays under it,
    // so it is the only row that actually exercises MagnusLiftSlope (k_L). Gravity/drag off ⇒ Magnus only.
    [TestMethod]
    [DataRow(2000.0, 0.004311)] // S=3.375 ⇒ C_L clamped at MagnusLiftMax (0.35)
    [DataRow(300.0, 0.001559)]  // S=0.506 ⇒ C_L = k_L·S unclamped ⇒ exercises MagnusLiftSlope
    public void Magnus_LateralCurveMatchesFrozenPrediction(double spinRate, double expectedDeflection)
    {
        var settings = PhysicsSettings.Default with { EnableGravity = false, EnableDrag = false, EnableMagnus = true };
        var world = new PhysicsWorld(settings, Array.Empty<ICollider>());
        world.Ball = new BallState
        {
            Orientation = QuaternionD.Identity,
            LinearVelocity = new Vector3D(8.0, 0, 0),      // +X flight
            AngularVelocity = new Vector3D(0, spinRate, 0), // spin about +Y curves it in −Z
        };

        const double hop = 0.3;
        Run(world, (int)(hop / PhysicsConstants.SubstepSeconds));

        double measured = -world.Ball.Position.Z;
        Assert.IsTrue(measured > 0, "curves in the predicted −Z direction");
        Assert.IsTrue(expectedDeflection is > 0.001 and < 0.010, "the curve is a few mm — correct and tiny");
        Assert.AreEqual(expectedDeflection, measured, expectedDeflection * 0.12, "matches the frozen Magnus prediction");
    }

    // ── #5 Drag terminal velocity ≈ 70 m/s, and negligible (≈0.05 m/s²) at pinball speeds. ──
    [TestMethod]
    public void Drag_TerminalVelocityAndNegligibleAtPinballSpeeds()
    {
        double terminal = Math.Sqrt(2 * PhysicsConstants.BallMass * G /
            (PhysicsConstants.AirDensity * PhysicsConstants.DragCoefficient * PhysicsConstants.CrossSection));
        Assert.AreEqual(70.0, terminal, 3.0, "terminal velocity ≈ 70 m/s");

        var settings = PhysicsSettings.Default with { EnableMagnus = false, EnableRollingResistance = false };
        var world = new PhysicsWorld(settings, Array.Empty<ICollider>());
        world.Ball = BallState.AtRest(Vector3D.Zero); // free fall
        Run(world, 30_000); // 30 s — asymptotes to terminal
        double vFall = world.Ball.LinearVelocity.Length();
        Assert.AreEqual(terminal, vFall, terminal * 0.02, "|v| asymptotes to terminal");

        // The honest "negligible at pinball speeds" claim, tested: drag deceleration at 5 m/s ≈ 0.05 m/s².
        var slow = new BallState { LinearVelocity = new Vector3D(5, 0, 0), Orientation = QuaternionD.Identity };
        var dragOnly = new PhysicsSettings(Alpha, EnableGravity: false, EnableDrag: true, EnableMagnus: false, EnableRollingResistance: false);
        double dragDecel = Forces.ExternalAcceleration(slow, dragOnly).Length();
        Assert.AreEqual(0.05, dragDecel, 0.005, "drag decel at 5 m/s ≈ 0.05 m/s²");
    }

    // ── #6 Restitution bounce-height decay: successive apexes scale by e². ──
    [TestMethod]
    public void Restitution_ApexHeightsDecayByESquared()
    {
        const double e = 0.6;
        var plane = new PlaneCollider(Vector3D.Zero, Vector3D.UnitY, new PhysicsMaterial(e, 0, 0, 0));
        var settings = PhysicsSettings.Default with { EnableDrag = false, EnableMagnus = false, EnableRollingResistance = false };
        var world = new PhysicsWorld(settings, new[] { plane });
        world.Ball = BallState.AtRest(new Vector3D(0, 0.5, 0)); // dropped from 0.5 m

        var apexes = new List<double>();
        double prevY = world.Ball.Position.Y;
        double prevVy = 0;
        for (int i = 0; i < 5000 && apexes.Count < 3; i++)
        {
            world.Substep();
            double vy = world.Ball.LinearVelocity.Y;
            if (prevVy > 0 && vy <= 0 && world.Ball.Position.Y > R + 0.01) // apex: vertical velocity crosses down
                apexes.Add(world.Ball.Position.Y - R); // height of the ball surface above the plane
            prevVy = vy;
            prevY = world.Ball.Position.Y;
        }

        Assert.IsTrue(apexes.Count >= 3, $"observed {apexes.Count} apexes");
        double e2 = e * e; // 0.36
        Assert.AreEqual(e2, apexes[1] / apexes[0], 0.04, "apex₁/apex₀ ≈ e²");
        Assert.AreEqual(e2, apexes[2] / apexes[1], 0.04, "apex₂/apex₁ ≈ e²");
    }

    // ── #7 No-tunnelling stress test (CCD): a fast ball fired at a thin wall never passes it. ──
    [TestMethod]
    [DataRow(10.0)]
    [DataRow(20.0)] // over-speed guard
    public void Ccd_FastBallDoesNotTunnelThroughWall(double speed)
    {
        // Wall plane at x=0 with the ball on the −X side; one 1 ms step at ≥10 m/s travels ≥10 mm ≫ a thin wall.
        var wall = new PlaneCollider(Vector3D.Zero, new Vector3D(-1, 0, 0), new PhysicsMaterial(1.0, 0, 0, 0));
        var settings = new PhysicsSettings(Alpha, EnableGravity: false, EnableDrag: false, EnableMagnus: false, EnableRollingResistance: false);
        var world = new PhysicsWorld(settings, new[] { wall });
        world.Ball = new BallState
        {
            Position = new Vector3D(-0.05, 0, 0),
            Orientation = QuaternionD.Identity,
            LinearVelocity = new Vector3D(speed, 0, 0),
            AngularVelocity = Vector3D.Zero,
        };

        double maxSurfaceX = double.NegativeInfinity;
        for (int i = 0; i < 40; i++)
        {
            world.Substep();
            maxSurfaceX = Math.Max(maxSurfaceX, world.Ball.Position.X + R); // leading surface
        }

        Assert.IsTrue(maxSurfaceX < 0.001, $"ball surface never crosses the wall (max {maxSurfaceX:0.0000} m)");
        Assert.IsTrue(world.Ball.LinearVelocity.X < 0, "velocity reflected back to the incoming side");
        Assert.AreEqual(speed, Math.Abs(world.Ball.LinearVelocity.X), speed * 0.05, "elastic reflection preserves speed");
    }

    // ── #8 Determinism: same seed + same inputs ⇒ bit-identical state stream (within a build). ──
    [TestMethod]
    public void Determinism_SameSeedReproducesBitIdentically()
    {
        static BallState RunScenario()
        {
            var world = new PhysicsWorld(
                PhysicsSettings.Default with { InclineRadians = Alpha },
                new ICollider[] { PlaneCollider.InclinedPlayfield(Alpha, PhysicsMaterial.Playfield) });
            world.Ball = new BallState
            {
                Position = new Vector3D(0.1, R, 0.2),
                Orientation = QuaternionD.Identity,
                LinearVelocity = new Vector3D(0.7, 0, -1.3),
                AngularVelocity = new Vector3D(3, -2, 5),
            };
            for (int i = 0; i < 3000; i++)
                world.Substep();
            return world.Ball;
        }

        BallState a = RunScenario();
        BallState b = RunScenario();
        Assert.AreEqual(a.Position, b.Position, "position bit-identical");
        Assert.AreEqual(a.LinearVelocity, b.LinearVelocity, "linear velocity bit-identical");
        Assert.AreEqual(a.AngularVelocity, b.AngularVelocity, "angular velocity bit-identical");
        Assert.AreEqual(a.Orientation, b.Orientation, "orientation bit-identical");
    }
}
