using System;
using Pinball.Game;
using Pinball.Physics;

namespace Pinball.Tests;

/// <summary>
/// Tests the game orchestrator / fixed-timestep loop (pinball-plan §6.2–§6.4): serve → play → drain → next
/// ball → game over, plus flipper-input wiring and the tilt penalty. All headless and deterministic.
/// </summary>
[TestClass]
public sealed class PinballGameTests
{
    private const double Dt = 1.0 / 60.0; // a 60 fps frame

    // Ticks until the live ball drains (or a timeout), returning the tick count.
    private static int PlayUntilBallLost(PinballGame game, PinballInput input, int maxTicks = 1200)
    {
        int t = 0;
        while (game.State.BallInPlay && t < maxTicks)
        {
            game.Tick(Dt, input);
            t++;
        }
        return t;
    }

    [TestMethod]
    public void NewGame_StartsAtBallOneNotYetInPlay()
    {
        var game = new PinballGame();
        game.NewGame();
        Assert.AreEqual(1, game.State.BallNumber);
        Assert.AreEqual(0, game.State.Score);
        Assert.IsFalse(game.State.GameOver);
        Assert.IsFalse(game.State.BallInPlay, "waits for launch");
    }

    [TestMethod]
    public void Launch_PutsTheBallInPlay_ThenItDrainsToTheNextBall()
    {
        var game = new PinballGame();
        game.NewGame();
        game.Tick(Dt, new PinballInput(Launch: true));
        Assert.IsTrue(game.State.BallInPlay, "launch serves the ball");

        int ticks = PlayUntilBallLost(game, default);
        Assert.IsTrue(ticks < 1200, "the centre-served ball drains");
        Assert.IsFalse(game.State.BallInPlay);
        Assert.AreEqual(2, game.State.BallNumber, "drain advances to the next ball");
        Assert.IsFalse(game.State.GameOver);
    }

    [TestMethod]
    public void ThreeDrains_EndTheGame()
    {
        var game = new PinballGame(ballsPerGame: 3);
        game.NewGame();
        for (int ball = 0; ball < 3; ball++)
        {
            game.Tick(Dt, new PinballInput(Launch: true));
            Assert.IsTrue(game.State.BallInPlay);
            PlayUntilBallLost(game, default);
        }
        Assert.IsTrue(game.State.GameOver, "game over after the third ball drains");

        // Launch is inert after game over.
        game.Tick(Dt, new PinballInput(Launch: true));
        Assert.IsFalse(game.State.BallInPlay);
    }

    [TestMethod]
    public void FlipperInput_EnergisesTheHeldFlipperAndReleasesTheOther()
    {
        var game = new PinballGame();
        game.NewGame();
        game.Tick(Dt, new PinballInput(Launch: true));

        game.Tick(Dt, new PinballInput(LeftFlipper: true));
        Assert.IsTrue(game.Table.LeftFlipper.Energized, "input drives the flipper");

        game.Tick(Dt, new PinballInput(RightFlipper: true)); // left released
        Assert.IsFalse(game.Table.LeftFlipper.Energized);
        Assert.IsTrue(game.Table.RightFlipper.Energized);
    }

    [TestMethod]
    public void OverNudging_TripsTiltAndLosesTheBall()
    {
        var game = new PinballGame();
        game.NewGame();
        game.Tick(Dt, new PinballInput(Launch: true));

        var hardNudge = new Vector3D(0.04, 0, 0);
        int guard = 0;
        while (!game.Tilted && guard++ < 100)
            game.Tick(Dt, new PinballInput(Nudge: hardNudge));
        Assert.IsTrue(game.Tilted, "sustained hard nudging trips TILT");

        // The tick after the trip: HOLD the left flipper — tilt must keep it dead and lose the ball.
        game.Tick(Dt, new PinballInput(LeftFlipper: true, Nudge: hardNudge));
        Assert.IsFalse(game.State.BallInPlay, "tilt loses the live ball");
        Assert.IsFalse(game.Table.LeftFlipper.Energized, "tilt disables the flippers even with the button held");
    }

    [TestMethod]
    public void ScriptedGame_IsDeterministic()
    {
        static (Vector3D pos, int ball, long score, bool over) Run()
        {
            var game = new PinballGame(seed: 0xABCDEF);
            game.NewGame();
            for (int i = 0; i < 2000; i++)
            {
                // A fixed input timeline: relaunch when idle, flip on a repeating pattern.
                var input = new PinballInput(
                    LeftFlipper: (i / 20) % 2 == 0,
                    RightFlipper: (i / 25) % 2 == 1,
                    Launch: !game.State.BallInPlay);
                game.Tick(Dt, input);
            }
            return (game.Ball.Position, game.State.BallNumber, game.State.Score, game.State.GameOver);
        }

        var a = Run();
        var b = Run();
        Assert.AreEqual(a.pos, b.pos, "ball position bit-identical");
        Assert.AreEqual(a.ball, b.ball);
        Assert.AreEqual(a.score, b.score);
        Assert.AreEqual(a.over, b.over);
    }

    // The ball racks at the plunger in the shooter lane and the auto-plunge launches it up the lane, where the
    // top feed deflects it into the playfield (rather than falling back down the lane to drain).
    [TestMethod]
    public void Launch_RacksAtPlungerAndFeedsIntoPlayfield()
    {
        double s = Pinball.Physics.PinballTable.RenderScale;
        var game = new PinballGame();
        game.NewGame();
        Assert.IsTrue(game.Ball.Position.X > 4.6 * s, "racked in the shooter lane (right of the divider)");
        Assert.IsTrue(game.Ball.Position.Z < 2.0 * s, "racked at the bottom of the lane, at the plunger");

        game.Launch();
        Assert.IsTrue(game.Ball.LinearVelocity.Z > 0, "auto-plunge fires the ball up the lane (+z)");

        double maxZ = double.NegativeInfinity;
        bool enteredPlayfield = false;
        for (int i = 0; i < 900 && game.State.BallInPlay; i++)
        {
            game.Tick(1.0 / 60.0, default);
            Pinball.Physics.Vector3D p = game.Ball.Position;
            maxZ = System.Math.Max(maxZ, p.Z);
            // Crossed left of the divider while still up-table (above the drain, below the feed): fed into play.
            if (p.X < 4.6 * s && p.Z > game.Table.DrainZ && p.Z < 15.0 * s) enteredPlayfield = true;
        }
        Assert.IsTrue(maxZ > 13.0 * s, "the launch climbs the lane to the feed (z > 13 render)");
        Assert.IsTrue(enteredPlayfield, "the top feed deflects the ball left into the playfield");
    }
}
