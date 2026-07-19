using Pinball.Physics;

namespace Pinball.Game;

/// <summary>
/// The game orchestrator (pinball-plan §6.2–§6.4): the fixed-timestep loop that turns real-time and input
/// into simulation and game state. It owns the deterministic physics (<see cref="PinballTable"/> +
/// <see cref="PhysicsWorld"/>) and the <see cref="GameState"/> lifecycle (serve → play → drain → next ball →
/// game over), decoupled from rendering — the host samples input, calls <see cref="Tick"/> with the frame
/// delta, and reads <see cref="Ball"/> / the flipper angles to drive the renderer (§6.2 handoff). Pure and
/// headless, so a scripted input timeline replays identically (the basis for the game tests and regressions).
/// The event-driven scoring layer (which target/bumper was hit → missions/rank) sits on top in P7.
/// </summary>
public sealed class PinballGame
{
    private readonly PhysicsWorld _world;

    /// <summary>Creates a new game with the given ball count and physics seed.</summary>
    public PinballGame(int ballsPerGame = 3, ulong seed = 0x5D_EE_CE_5Eul)
    {
        Table = new PinballTable(seed);
        _world = new PhysicsWorld(Table.Settings, Table.Colliders);
        State = new GameState(ballsPerGame);
        _world.Ball = BallState.AtRest(Table.ShooterLaneStart);
    }

    /// <summary>The physics table (colliders, flippers, drain).</summary>
    public PinballTable Table { get; }
    /// <summary>The ball/score lifecycle state.</summary>
    public GameState State { get; }
    /// <summary>The live ball's physics state (its centre drives the renderer's ball via UpdateSpheres).</summary>
    public BallState Ball => _world.Ball;
    /// <summary>True while TILT is latched — flippers and nudge are dead until the ball is served again.</summary>
    public bool Tilted => _world.Tilt.Tripped;

    /// <summary>Starts a fresh game: reset score/balls/tilt and rack the first ball in the shooter lane.</summary>
    public void NewGame()
    {
        State.Reset();
        _world.Tilt.Reset();
        _world.Ball = BallState.AtRest(Table.ShooterLaneStart);
    }

    /// <summary>
    /// Auto-plunges the racked ball into play (no-op when a ball is already live or the game is over): places
    /// it at the plunger in the shooter lane and fires it up the lane, where the top feed deflects it into the
    /// playfield. (A charge-and-release plunger — hold to vary power — is a later refinement.)
    /// </summary>
    public void Launch()
    {
        if (State.GameOver || State.BallInPlay)
            return;
        _world.Tilt.Reset(); // a fresh tilt budget for each ball
        _world.Ball = new BallState
        {
            Position = Table.ShooterLaneStart,
            Orientation = QuaternionD.Identity,
            LinearVelocity = new Vector3D(0, 0, Table.LaunchSpeed), // +z, up the shooter lane
        };
        State.StartBall();
    }

    /// <summary>
    /// Advances the game by real-time <paramref name="dt"/> seconds under <paramref name="input"/>: applies the
    /// input to the actuators, runs the fixed-<c>h</c> physics accumulator, and handles the drain/tilt → lose-ball
    /// transition. When no ball is live, <see cref="PinballInput.Launch"/> serves the next one.
    /// </summary>
    public void Tick(double dt, in PinballInput input)
    {
        if (State.GameOver)
            return;

        if (!State.BallInPlay)
        {
            if (input.Launch)
                Launch();
            return;
        }

        bool tilted = _world.Tilt.Tripped;
        // Tilt kills ball control (flippers dead, nudge ignored) — the physical penalty of over-nudging.
        Table.LeftFlipper.Energized = input.LeftFlipper && !tilted;
        Table.RightFlipper.Energized = input.RightFlipper && !tilted;
        if (!tilted && input.Nudge.LengthSquared() > 0)
            _world.Nudge(input.Nudge);

        _world.Advance(dt);

        if (tilted || Table.IsDrained(_world.Ball))
        {
            Table.LeftFlipper.Energized = false;
            Table.RightFlipper.Energized = false;
            State.Drain(); // advances the ball number, or ends the game after the last ball
        }
    }
}
