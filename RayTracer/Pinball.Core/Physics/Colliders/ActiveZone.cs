namespace Pinball.Physics;

/// <summary>
/// Wraps any collider as an <b>active impulse zone</b> (pinball-plan §6.1.6) — a pop bumper (over a
/// <see cref="CylinderCollider"/>), a slingshot (over a <see cref="QuadCollider"/>), or a kicker. On a
/// contact whose approach speed clears <see cref="TriggerSpeed"/>, the solver adds an outward coil impulse
/// on top of the passive restitution, so the ball leaves with more energy than it arrived (the coil does
/// work). Geometry is delegated to the wrapped shape.
/// </summary>
public sealed class ActiveZone : ICollider, IActiveImpulse
{
    private readonly ICollider _shape;

    /// <summary>Wraps <paramref name="shape"/> with a coil of impulse <paramref name="impulseStrength"/> armed above <paramref name="triggerSpeed"/>.</summary>
    public ActiveZone(ICollider shape, double impulseStrength, double triggerSpeed)
    {
        System.ArgumentNullException.ThrowIfNull(shape);
        _shape = shape;
        ImpulseStrength = impulseStrength;
        TriggerSpeed = triggerSpeed;
    }

    /// <inheritdoc/>
    public double ImpulseStrength { get; }
    /// <inheritdoc/>
    public double TriggerSpeed { get; }

    /// <inheritdoc/>
    public PhysicsMaterial Material => _shape.Material;
    /// <inheritdoc/>
    public AabbD Bounds => _shape.Bounds;
    /// <inheritdoc/>
    public ContactQuery Query(Vector3D ballCenter, double ballRadius) => _shape.Query(ballCenter, ballRadius);
    /// <inheritdoc/>
    public Vector3D SurfaceVelocity(Vector3D worldPoint) => _shape.SurfaceVelocity(worldPoint);
}
