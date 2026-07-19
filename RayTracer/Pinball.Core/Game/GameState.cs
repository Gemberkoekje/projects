namespace Pinball.Game;

/// <summary>
/// The minimal ball-and-score state machine for a game (pinball-plan §7.3 <c>Game/*</c>): balls remaining,
/// the current ball, the running score, and the drain/tilt/game-over transitions. The event-driven layer on
/// top — which target/bumper was hit, mission and rank progression, replay, the full tilt penalty (§4/§6.4) —
/// is wired by the P6 game host from the physics contact stream; this core owns only the game lifecycle so it
/// stays pure and unit-testable.
/// </summary>
public sealed class GameState
{
    /// <summary>Balls in a full game (classic 3).</summary>
    public int BallsPerGame { get; }
    /// <summary>Current ball number, 1-based.</summary>
    public int BallNumber { get; private set; } = 1;
    /// <summary>Running score.</summary>
    public long Score { get; private set; }
    /// <summary>True once the last ball has drained.</summary>
    public bool GameOver { get; private set; }
    /// <summary>True while a ball is live (between <see cref="StartBall"/> and a drain/tilt).</summary>
    public bool BallInPlay { get; private set; }

    /// <summary>Creates a new game.</summary>
    public GameState(int ballsPerGame = 3) => BallsPerGame = ballsPerGame;

    /// <summary>Puts the current ball into play (no-op after game over).</summary>
    public void StartBall()
    {
        if (!GameOver)
            BallInPlay = true;
    }

    /// <summary>Adds <paramref name="points"/> to the score while a ball is live.</summary>
    public void AddScore(long points)
    {
        if (BallInPlay && !GameOver)
            Score += points;
    }

    /// <summary>Ends the live ball (drained past the flippers): advances to the next ball or ends the game.</summary>
    public void Drain()
    {
        if (!BallInPlay)
            return;
        BallInPlay = false;
        if (BallNumber >= BallsPerGame)
            GameOver = true;
        else
            BallNumber++;
    }

    /// <summary>Tilt: the live ball is lost with no end-of-ball bonus — same lifecycle effect as a drain.</summary>
    public void Tilt() => Drain();

    /// <summary>Resets to a fresh game (ball 1, score 0).</summary>
    public void Reset()
    {
        BallNumber = 1;
        Score = 0;
        GameOver = false;
        BallInPlay = false;
    }
}
