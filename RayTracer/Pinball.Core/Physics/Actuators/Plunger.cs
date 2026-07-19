using System;

namespace Pinball.Physics;

/// <summary>
/// The shooter-lane plunger (pinball-plan §6.1.6): a 1-DOF kinematic tip on the lane axis, driven by a Hooke
/// spring. Pull back to compress; release and the spring accelerates the tip forward through the same contact
/// solver (the tip carries a surface velocity), launching the ball, which separates when the tip reaches its
/// rest stop. Modelled as a moving plane cap facing up the lane; the lane geometry constrains the ball to the
/// axis. An auto-plunger is the same with a fixed charge.
/// </summary>
public sealed class Plunger : ICollider, IActuator
{
    private double _offset;    // tip position along the axis relative to rest (≤ 0 ⇒ pulled back)
    private double _velocity;  // tip speed along the axis (m/s)
    private bool _released;

    /// <summary>Unit launch direction (up the shooter lane).</summary>
    public Vector3D Axis { get; }
    /// <summary>Tip position at rest (spring uncompressed) — the forward mechanical stop.</summary>
    public Vector3D RestTip { get; }
    /// <summary>Spring constant k (N/m).</summary>
    public double SpringConstant { get; }
    /// <summary>
    /// Plunger mass M (kg) — sets how fast the spring accelerates the tip: peak tip speed is
    /// <c>pull·√(k/M)</c> (<see cref="IdealLaunchSpeed"/>). The tip is a kinematic pusher, so the ball rides
    /// it and separates at ≈ that peak (with a bouncy tip material it can leave marginally faster). M is not
    /// an energy-sharing collision mass; it only scales the tip's acceleration.
    /// </summary>
    public double PlungerMass { get; }
    /// <inheritdoc/>
    public PhysicsMaterial Material { get; }

    /// <summary>Current tip velocity along the axis (m/s); the contact surface velocity.</summary>
    public double TipVelocity => _velocity;
    /// <summary>Current tip position (world).</summary>
    public Vector3D TipPosition => RestTip + Axis * _offset;
    /// <summary>The ideal spring launch speed for a pull of <paramref name="pull"/> m: <c>pull·√(k/M)</c>.</summary>
    public double IdealLaunchSpeed(double pull) => pull * Math.Sqrt(SpringConstant / PlungerMass);

    /// <summary>Constructs a plunger on <paramref name="axis"/> with rest tip <paramref name="restTip"/>.</summary>
    public Plunger(Vector3D axis, Vector3D restTip, double springConstant, double plungerMass, PhysicsMaterial material)
    {
        Axis = axis.Normalized();
        RestTip = restTip;
        SpringConstant = springConstant;
        PlungerMass = plungerMass;
        Material = material;
    }

    /// <summary>Pulls the tip back by <paramref name="pull"/> m (compresses the spring); cancels any launch.</summary>
    public void Charge(double pull)
    {
        _offset = -Math.Abs(pull);
        _velocity = 0;
        _released = false;
    }

    /// <summary>Releases the charged spring — the tip launches forward on the next substeps.</summary>
    public void Release() => _released = true;

    /// <inheritdoc/>
    public void Advance(double h)
    {
        if (!_released)
        {
            if (_offset >= 0) _velocity = 0; // parked at rest
            return; // charged-but-held (or parked): tip static
        }

        double compression = -_offset; // ≥ 0 while behind rest
        double accel = SpringConstant * compression / PlungerMass;
        _velocity += accel * h;
        _offset += _velocity * h;

        if (_offset >= 0)
        {
            // Reached the rest stop: the ball has been driven to ≈ the tip speed and now separates. Keep
            // this step's velocity for the final surface-velocity read; the next Advance parks the tip.
            _offset = 0;
            _released = false;
        }
    }

    /// <inheritdoc/>
    public AabbD Bounds => AabbD.Infinite; // a lane-axis plane cap; the lane constrains the ball

    /// <inheritdoc/>
    public ContactQuery Query(Vector3D ballCenter, double ballRadius)
    {
        Vector3D tip = TipPosition;
        double signedDistance = Vector3D.Dot(ballCenter - tip, Axis);
        double separation = signedDistance - ballRadius;
        Vector3D contactPoint = ballCenter - Axis * ballRadius;
        return new ContactQuery(Axis, separation, contactPoint);
    }

    /// <inheritdoc/>
    public Vector3D SurfaceVelocity(Vector3D worldPoint) => Axis * _velocity;
}
