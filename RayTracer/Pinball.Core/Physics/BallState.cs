namespace Pinball.Physics;

/// <summary>
/// The 6-DOF state of the dynamic ball (pinball-plan §6.1.1), integrated in double precision. A mutable
/// value type: the integrator advances a <see cref="PhysicsWorld"/>'s copy in place each substep, and the
/// renderer samples <see cref="Position"/>/<see cref="Orientation"/> per frame for the mover handoff (§6.2).
/// </summary>
public struct BallState
{
    /// <summary>Ball-centre position (m), world/playfield frame.</summary>
    public Vector3D Position;
    /// <summary>Ball orientation (unit quaternion, body frame).</summary>
    public QuaternionD Orientation;
    /// <summary>Linear velocity (m/s).</summary>
    public Vector3D LinearVelocity;
    /// <summary>Angular velocity ω (rad/s, world axes).</summary>
    public Vector3D AngularVelocity;

    /// <summary>Creates a ball at rest at <paramref name="position"/> with identity orientation.</summary>
    public static BallState AtRest(Vector3D position) => new()
    {
        Position = position,
        Orientation = QuaternionD.Identity,
        LinearVelocity = Vector3D.Zero,
        AngularVelocity = Vector3D.Zero,
    };

    /// <summary>Translational kinetic energy <c>½m|v|²</c> (J).</summary>
    public readonly double TranslationalKineticEnergy => 0.5 * PhysicsConstants.BallMass * LinearVelocity.LengthSquared();

    /// <summary>Rotational kinetic energy <c>½I|ω|²</c> (J).</summary>
    public readonly double RotationalKineticEnergy => 0.5 * PhysicsConstants.BallInertia * AngularVelocity.LengthSquared();

    /// <summary>
    /// Total mechanical energy (J) relative to <paramref name="referenceHeight"/> along <paramref name="up"/>:
    /// <c>½m|v|² + ½I|ω|² + m g (height − ref)</c>. Used by the energy-conservation test (§6.1.8 #2).
    /// </summary>
    public readonly double MechanicalEnergy(Vector3D up, double referenceHeight)
    {
        double height = Vector3D.Dot(Position, up) - referenceHeight;
        return TranslationalKineticEnergy + RotationalKineticEnergy
            + PhysicsConstants.BallMass * PhysicsConstants.Gravity * height;
    }
}
