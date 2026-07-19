using Pinball.Game;

namespace Pinball.Tests;

/// <summary>Tests the ball/score lifecycle (pinball-plan §7.3 Game/*): scoring, drain progression, tilt,
/// and game over.</summary>
[TestClass]
public sealed class GameStateTests
{
    [TestMethod]
    public void FreshGame_StartsAtBallOneZeroScore()
    {
        var g = new GameState();
        Assert.AreEqual(3, g.BallsPerGame);
        Assert.AreEqual(1, g.BallNumber);
        Assert.AreEqual(0, g.Score);
        Assert.IsFalse(g.GameOver);
    }

    [TestMethod]
    public void Draining_AdvancesBallsThenEndsGame()
    {
        var g = new GameState(ballsPerGame: 3);

        g.StartBall();
        g.AddScore(15_000);
        Assert.AreEqual(15_000, g.Score);
        g.Drain();
        Assert.AreEqual(2, g.BallNumber);
        Assert.IsFalse(g.GameOver);

        g.StartBall(); g.Drain();
        Assert.AreEqual(3, g.BallNumber);

        g.StartBall(); g.Drain();
        Assert.IsTrue(g.GameOver, "game over after the third ball drains");

        // Scoring is inert once the game is over.
        g.StartBall();
        g.AddScore(999);
        Assert.AreEqual(15_000, g.Score);
    }

    [TestMethod]
    public void Score_OnlyCountsWhileBallIsLive()
    {
        var g = new GameState();
        g.AddScore(500); // no ball in play yet
        Assert.AreEqual(0, g.Score);
        g.StartBall();
        g.AddScore(500);
        Assert.AreEqual(500, g.Score);
    }

    [TestMethod]
    public void Tilt_EndsTheLiveBall()
    {
        var g = new GameState();
        g.StartBall();
        g.Tilt();
        Assert.IsFalse(g.BallInPlay, "tilt kills the live ball");
        Assert.AreEqual(2, g.BallNumber);
    }
}
