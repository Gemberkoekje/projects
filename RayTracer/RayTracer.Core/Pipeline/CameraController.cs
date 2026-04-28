using System.Numerics;

namespace RayTracer;

/// <summary>
/// Drives a first-person <see cref="Camera"/> through a maze using a
/// <see cref="MazeNavigator"/>. Holds still between moves and handles
/// smooth cell-to-cell movement and smooth turns.
/// </summary>
public class CameraController
{
    private readonly MazeNavigator _nav;
    private readonly float _cellSize;
    private readonly float _eyeHeight;

    /// <summary>Seconds to move from one cell center to the next.</summary>
    public float MoveTime { get; set; } = 1.0f;

    /// <summary>Seconds to hold the camera still between actions.</summary>
    public float StillTime { get; set; } = 6.0f;

    /// <summary>Seconds to perform a 90° turn.</summary>
    public float TurnTime { get; set; } = 1.0f;

    /// <summary>
    /// Set to true by the controller whenever the camera moves or
    /// rotates so the caller can reset the accumulation buffer.
    /// Cleared by the caller after handling.
    /// </summary>
    public bool Dirty { get; set; }

    // Expose whether the controller is currently performing a turn.
    public bool IsTurning => _state == State.Turning;

    private enum State { Still, Moving, Turning }
    private State _state;

    // Interpolation progress [0..1].
    private float _t;

    // Endpoints for the current interpolation.
    private Vector3 _startPos;
    private Vector3 _endPos;
    private Quaternion _startRot;
    private Quaternion _endRot;

    // When a turn is needed before a move, the pending move target is
    // stored here so we don't re-peek after turning.
    private (int x, int y, Direction heading)? _pendingMove;

    public CameraController(MazeNavigator navigator, float cellSize, float eyeHeight)
    {
        _nav = navigator;
        _cellSize = cellSize;
        _eyeHeight = eyeHeight;

        BeginStill();
    }

    /// <summary>
    /// Call once per frame with the elapsed seconds since the last
    /// frame. Updates the supplied <paramref name="camera"/> in place.
    /// </summary>
    public void Update(float deltaTime, Camera camera)
    {
        if (_state == State.Still)
        {
            if (TryBeginImmediateTurn())
                return;

            if (StillTime <= 0f)
            {
                BeginNextAction();
                return;
            }

            _t += deltaTime / StillTime;
            if (_t >= 1f)
                BeginNextAction();

            return;
        }

        float duration = _state == State.Moving ? MoveTime : TurnTime;
        if (duration <= 0f)
        {
            Dirty = true;
            camera.Position = _endPos;
            camera.Rotation = _endRot;
            if (_state == State.Turning && _pendingMove is { } instantTurnTarget)
                _nav.Heading = instantTurnTarget.heading;
            BeginStill();
            return;
        }

        _t += deltaTime / duration;

        if (_t >= 1f)
        {
            Dirty = true;
            _t = 1f;
            camera.Position = _endPos;
            camera.Rotation = _endRot;
            if (_state == State.Turning && _pendingMove is { } turnTarget)
                _nav.Heading = turnTarget.heading;
            BeginStill();
            return;
        }

        Dirty = true;
        float smooth = SmoothStep(_t);

        if (_state == State.Moving)
            camera.Position = Vector3.Lerp(_startPos, _endPos, smooth);

        camera.Rotation = Quaternion.Slerp(_startRot, _endRot, smooth);
    }

    // ── Private helpers ────────────────────────────────────────────

    private bool TryBeginImmediateTurn()
    {
        if (_pendingMove is not null)
            return false;

        var (nextX, nextY, nextHeading) = _nav.PeekNext();
        if (nextHeading == _nav.Heading)
            return false;

        BeginTurn(nextX, nextY, nextHeading);
        return true;
    }

    private void BeginNextAction()
    {
        // If we just finished a turn and have a pending move, execute it.
        if (_pendingMove is { } pm)
        {
            _pendingMove = null;
            Dirty = true;
            _state = State.Moving;
            _startPos = CellCenter(_nav.CellX, _nav.CellY);
            _endPos = CellCenter(pm.x, pm.y);
            _startRot = HeadingToQuaternion(pm.heading);
            _endRot = _startRot;
            // Advance the navigator to the target cell.
            _nav.CellX = pm.x;
            _nav.CellY = pm.y;
            _nav.Heading = pm.heading;
            _t = 0f;
            return;
        }

        var (nextX, nextY, nextHeading) = _nav.PeekNext();

        _startPos = CellCenter(_nav.CellX, _nav.CellY);
        _startRot = HeadingToQuaternion(_nav.Heading);

        if (nextHeading != _nav.Heading)
        {
            // Turn immediately when needed; don't wait out still time.
            BeginTurn(nextX, nextY, nextHeading);
        }
        else
        {
            // Same heading — move straight into the next cell.
            Dirty = true;
            _state = State.Moving;
            _endPos = CellCenter(nextX, nextY);
            _endRot = _startRot;
            _nav.Advance();
            _t = 0f;
        }
    }

    private void BeginTurn(int nextX, int nextY, Direction nextHeading)
    {
        Dirty = true;
        _state = State.Turning;
        _startPos = CellCenter(_nav.CellX, _nav.CellY);
        _endPos = _startPos;
        _startRot = HeadingToQuaternion(_nav.Heading);
        _endRot = HeadingToQuaternion(nextHeading);
        _pendingMove = (nextX, nextY, nextHeading);
        _t = 0f;
    }

    private void BeginStill()
    {
        _state = State.Still;
        _t = 0f;
    }

    private Vector3 CellCenter(int cx, int cy)
    {
        return new Vector3(
            (cx + 0.5f) * _cellSize,
            _eyeHeight,
            (cy + 0.5f) * _cellSize);
    }

    /// <summary>
    /// Maps a maze <see cref="Direction"/> to a camera quaternion.
    /// The camera's default forward is +Z, so:
    ///   South (+Z) = 0°,  West (-X) = +90°,
    ///   North (-Z) = 180°, East (+X) = -90°.
    /// </summary>
    public static Quaternion HeadingToQuaternion(Direction d) => d switch
    {
        Direction.South => Quaternion.Identity,
        Direction.East  => Quaternion.CreateFromAxisAngle(Vector3.UnitY,  MathF.PI * 0.5f),
        Direction.North => Quaternion.CreateFromAxisAngle(Vector3.UnitY,  MathF.PI),
        Direction.West  => Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI * 0.5f),
        _ => Quaternion.Identity,
    };

    private static float SmoothStep(float t)
    {
        // Hermite smoothstep for ease-in / ease-out.
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
