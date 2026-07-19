namespace Pinball.Physics;

/// <summary>
/// A resolved single-point contact between the ball and one collider (pinball-plan §6.1.2), carrying
/// everything the impulse solver needs. Contacts are solved in a deterministic order — sorted by
/// <see cref="ColliderIndex"/> then contact position — so a given (seed, input) reproduces bit-for-bit
/// within a build (§6.1.5).
/// </summary>
public readonly struct Contact
{
    /// <summary>Index of the collider in the world's collider list — the primary deterministic sort key.</summary>
    public readonly int ColliderIndex;
    /// <summary>Unit contact normal, from the collider surface toward the ball centre.</summary>
    public readonly Vector3D Normal;
    /// <summary>Contact point on the ball surface.</summary>
    public readonly Vector3D Point;
    /// <summary>Signed ball-surface-to-collider gap (negative ⇒ penetrating).</summary>
    public readonly double Separation;
    /// <summary>Surface velocity of the collider at the contact point (non-zero for kinematic actuators).</summary>
    public readonly Vector3D SurfaceVelocity;
    /// <summary>The steel-on-surface coefficients at this contact.</summary>
    public readonly PhysicsMaterial Material;
    /// <summary>Outward coil impulse magnitude (kg·m/s) for an active zone — bumper/kicker/slingshot (§6.1.6); 0 for a passive contact.</summary>
    public readonly double ActiveImpulse;
    /// <summary>Minimum approach speed (m/s) that arms <see cref="ActiveImpulse"/> — below it the zone is passive.</summary>
    public readonly double ActiveTriggerSpeed;

    /// <summary>Constructs a contact.</summary>
    public Contact(int colliderIndex, Vector3D normal, Vector3D point, double separation,
        Vector3D surfaceVelocity, PhysicsMaterial material, double activeImpulse = 0.0, double activeTriggerSpeed = 0.0)
    {
        ColliderIndex = colliderIndex;
        Normal = normal;
        Point = point;
        Separation = separation;
        SurfaceVelocity = surfaceVelocity;
        Material = material;
        ActiveImpulse = activeImpulse;
        ActiveTriggerSpeed = activeTriggerSpeed;
    }
}
