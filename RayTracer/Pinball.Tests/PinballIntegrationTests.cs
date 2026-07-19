using System;
using Pinball.Physics;

namespace Pinball.Tests;

/// <summary>
/// The P5 capstone (pinball-plan §8 P5): a <b>playable ball on the P0 table</b>, proven deterministically on
/// the CPU physics before the P6 game host wires it to the renderer via P4's pose interface. The whole
/// system — inclined playfield, perimeter, slingshot posts and the two flippers — is exercised together: a
/// ball rolls down under the incline, stays contained (never tunnels), drains through the centre when not
/// caught, and is knocked back up-table by a flip. Everything is fixed-seed reproducible.
/// </summary>
[TestClass]
public sealed class PinballIntegrationTests
{
    private const double R = PhysicsConstants.BallRadius;

    private static bool Finite(Vector3D v) =>
        double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);

    [TestMethod]
    public void Ball_RollsDownAndDrainsThroughTheCentre_StayingContained()
    {
        var table = new PinballTable();
        var world = new PhysicsWorld(table.Settings, table.Colliders);
        world.Ball = table.NewBall(); // centre, above the flippers

        double halfWidth = 5.5 * PinballTable.RenderScale + R;
        bool drained = false;
        for (int i = 0; i < 4000 && !drained; i++)
        {
            world.Substep();
            BallState b = world.Ball;
            Assert.IsTrue(Finite(b.Position) && Finite(b.LinearVelocity), "no NaN/inf");
            Assert.IsTrue(Math.Abs(b.Position.X) < halfWidth, "stays between the side walls (no tunnelling)");
            Assert.IsTrue(b.Position.Y is > -0.005 and < 0.06, "stays on the playfield (never falls through / flies off)");
            drained = table.IsDrained(b);
        }
        Assert.IsTrue(drained, "the incline drains an un-caught centre ball toward the flippers");
    }

    [TestMethod]
    public void Flip_KnocksADescendingBallBackUpTable()
    {
        var table = new PinballTable();
        var world = new PhysicsWorld(table.Settings, table.Colliders);
        world.Ball = BallState.AtRest(PinballTable.PlayfieldPoint(-2.1, 4.4)); // up-table of the left flipper

        // Let it descend and settle against the resting flipper.
        for (int i = 0; i < 400; i++)
            world.Substep();
        double zBeforeFlip = world.Ball.Position.Z;
        Assert.IsFalse(table.IsDrained(world.Ball), "the resting flipper catches the ball");

        // Flip: energise the left flipper and let it sweep.
        table.LeftFlipper.Energized = true;
        double maxUpVelocity = double.NegativeInfinity, maxZ = zBeforeFlip;
        for (int i = 0; i < 200; i++)
        {
            world.Substep();
            maxUpVelocity = Math.Max(maxUpVelocity, world.Ball.LinearVelocity.Z);
            maxZ = Math.Max(maxZ, world.Ball.Position.Z);
        }

        Assert.IsTrue(maxUpVelocity > 0.2, $"the flip drives the ball up-table (+z), got {maxUpVelocity:0.00} m/s");
        Assert.IsTrue(maxZ > zBeforeFlip + 0.01, "the ball is propelled back up the table");
    }

    [TestMethod]
    public void TableSimulation_IsDeterministic()
    {
        static BallState RunScripted()
        {
            var table = new PinballTable();
            var world = new PhysicsWorld(table.Settings, table.Colliders);
            world.Ball = BallState.AtRest(PinballTable.PlayfieldPoint(-2.1, 5.0));
            for (int i = 0; i < 1500; i++)
            {
                // A fixed input schedule: flip both flippers in a repeating pattern.
                table.LeftFlipper.Energized = (i / 100) % 2 == 0;
                table.RightFlipper.Energized = (i / 130) % 2 == 1;
                world.Substep();
            }
            return world.Ball;
        }

        BallState a = RunScripted();
        BallState b = RunScripted();
        Assert.AreEqual(a.Position, b.Position, "same seed + same inputs ⇒ identical position");
        Assert.AreEqual(a.LinearVelocity, b.LinearVelocity, "…and identical velocity");
        Assert.AreEqual(a.AngularVelocity, b.AngularVelocity, "…and identical spin");
    }
}
