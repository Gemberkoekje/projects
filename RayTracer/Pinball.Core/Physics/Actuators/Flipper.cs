using System;

namespace Pinball.Physics;

/// <summary>
/// A flipper (pinball-plan §6.1.6): a kinematic, torque-limited rotating <see cref="CapsuleCollider"/> (the
/// rubber-clad bat) swinging about a pivot. It is not a free body — a coil drives it toward a target angle
/// and the end-of-stroke stop holds it, which is both physically reasonable (the solenoid dwarfs the ball)
/// and numerically stable. The bat carries a <b>surface velocity</b> <c>u = ω × r</c>, so feeding it through
/// the impulse law transfers <i>both</i> a push (advancing surface) and spin (tangential friction against the
/// sweep) — the whole skill mechanic, emergent, not scripted.
/// </summary>
public sealed class Flipper : CapsuleCollider, IActuator
{
    private readonly Vector3D _baseDirection; // bat direction at angle 0 (unit, ⟂ axis)

    /// <summary>Pivot point the bat rotates about.</summary>
    public Vector3D Pivot { get; }
    /// <summary>Unit rotation axis (the playfield normal for a flat flipper — +Y on a flat table).</summary>
    public Vector3D Axis { get; }
    /// <summary>Resting angle (rad, de-energised target).</summary>
    public double RestAngle { get; }
    /// <summary>Full-stroke angle (rad, energised target / EOS stop).</summary>
    public double EndAngle { get; }
    /// <summary>Bat length (pivot → tip).</summary>
    public double Length { get; }

    /// <summary>Coil angular acceleration toward the end stop (rad/s²).</summary>
    public double MaxAngularAccel { get; }
    /// <summary>Coil-saturation angular speed cap (rad/s).</summary>
    public double MaxAngularVelocity { get; }
    /// <summary>Return-spring strength as a fraction of the coil accel (VPX-typical ≈ 0.05–0.09).</summary>
    public double ReturnRatio { get; }

    /// <summary>Current bat angle (rad).</summary>
    public double Angle { get; private set; }
    /// <summary>Current bat angular speed about <see cref="Axis"/> (rad/s).</summary>
    public double AngularVelocity { get; private set; }
    /// <summary>Whether the coil is energised (button held) — drives toward <see cref="EndAngle"/>.</summary>
    public bool Energized { get; set; }

    /// <summary>Constructs a flipper. <paramref name="baseDirection"/> is the bat direction at angle 0 (⟂ <paramref name="axis"/>).</summary>
    public Flipper(Vector3D pivot, Vector3D axis, Vector3D baseDirection, double length, double radius,
        double restAngle, double endAngle, PhysicsMaterial material,
        double maxAngularAccel = 8000.0, double maxAngularVelocity = 55.0, double returnRatio = 0.07)
        : base(pivot, pivot + baseDirection.Normalized() * length, radius, material)
    {
        Pivot = pivot;
        Axis = axis.Normalized();
        _baseDirection = baseDirection.Normalized();
        Length = length;
        RestAngle = restAngle;
        EndAngle = endAngle;
        MaxAngularAccel = maxAngularAccel;
        MaxAngularVelocity = maxAngularVelocity;
        ReturnRatio = returnRatio;
        Angle = restAngle;
        UpdateGeometry();
    }

    /// <inheritdoc/>
    public void Advance(double h)
    {
        double target = Energized ? EndAngle : RestAngle;
        double accel = MaxAngularAccel * (Energized ? 1.0 : ReturnRatio);

        double dir = Math.Sign(target - Angle);
        if (dir != 0.0)
        {
            AngularVelocity += accel * dir * h;
            AngularVelocity = Math.Clamp(AngularVelocity, -MaxAngularVelocity, MaxAngularVelocity);
        }
        Angle += AngularVelocity * h;

        // End stops: the bat slams to the stop and is held there (ω → 0 at the stop it hit).
        double lo = Math.Min(RestAngle, EndAngle), hi = Math.Max(RestAngle, EndAngle);
        if (Angle <= lo) { Angle = lo; if (AngularVelocity < 0) AngularVelocity = 0; }
        if (Angle >= hi) { Angle = hi; if (AngularVelocity > 0) AngularVelocity = 0; }

        UpdateGeometry();
    }

    private void UpdateGeometry()
    {
        Vector3D dir = RotateAboutAxis(_baseDirection, Axis, Angle);
        PointA = Pivot;
        PointB = Pivot + dir * Length;
    }

    /// <inheritdoc/>
    public override Vector3D SurfaceVelocity(Vector3D worldPoint)
    {
        // u = ω × r, with ω = AngularVelocity · Axis and r the arm from the pivot to the contact.
        Vector3D omega = Axis * AngularVelocity;
        return Vector3D.Cross(omega, worldPoint - Pivot);
    }

    /// <summary>Rodrigues rotation of <paramref name="v"/> about unit <paramref name="axis"/> by <paramref name="angle"/> rad.</summary>
    public static Vector3D RotateAboutAxis(Vector3D v, Vector3D axis, double angle)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        return v * c + Vector3D.Cross(axis, v) * s + axis * (Vector3D.Dot(axis, v) * (1.0 - c));
    }
}
