namespace Pinball.Physics;

/// <summary>
/// The result of a collider's closest-feature query against the ball (pinball-plan §6.1.2). A single point,
/// because a sphere never forms an edge/face manifold.
/// </summary>
public readonly struct ContactQuery
{
    /// <summary>Unit contact normal, pointing from the collider surface toward the ball centre.</summary>
    public readonly Vector3D Normal;
    /// <summary>Signed gap between the ball surface and the collider surface: <c>|centre−closest| − r</c>. Negative ⇒ penetrating.</summary>
    public readonly double Separation;
    /// <summary>The point on the ball surface nearest the collider (<c>centre − r·Normal</c>).</summary>
    public readonly Vector3D ContactPoint;

    /// <summary>Constructs a query result.</summary>
    public ContactQuery(Vector3D normal, double separation, Vector3D contactPoint)
    {
        Normal = normal;
        Separation = separation;
        ContactPoint = contactPoint;
    }
}

/// <summary>
/// A static or kinematic analytic collider the ball can contact (pinball-plan §6.1.2). Each exposes a
/// double-precision closest-feature query and a broadphase box; kinematic actuators (flippers, plunger)
/// additionally report a non-zero surface velocity so the impulse law transfers both push and spin (§6.1.6).
/// </summary>
public interface ICollider
{
    /// <summary>The steel-on-this-surface contact coefficients.</summary>
    PhysicsMaterial Material { get; }

    /// <summary>A conservative broadphase bound (may be <see cref="AabbD.Infinite"/> for unbounded planes).</summary>
    AabbD Bounds { get; }

    /// <summary>Closest-feature query of a ball of radius <paramref name="ballRadius"/> centred at <paramref name="ballCenter"/>.</summary>
    ContactQuery Query(Vector3D ballCenter, double ballRadius);

    /// <summary>Surface velocity at world point <paramref name="worldPoint"/> (zero for static colliders).</summary>
    Vector3D SurfaceVelocity(Vector3D worldPoint);
}

/// <summary>
/// A collider that injects an outward coil impulse on a fast-enough contact (pinball-plan §6.1.6): pop
/// bumpers, kickers, slingshots. The impulse is a measured momentum magnitude, not a feel knob.
/// </summary>
public interface IActiveImpulse
{
    /// <summary>Outward impulse magnitude (kg·m/s) the coil delivers when triggered.</summary>
    double ImpulseStrength { get; }
    /// <summary>Minimum ball approach speed (m/s) that fires the coil.</summary>
    double TriggerSpeed { get; }
}

/// <summary>
/// A kinematic collider driven on the physics clock (pinball-plan §6.1.6): flippers, the plunger, moving
/// gates. The world advances every actuator once per substep before resolving the ball, so contact sees the
/// updated pose and a correct surface velocity.
/// </summary>
public interface IActuator
{
    /// <summary>Advances the actuator's own kinematics by one substep <paramref name="h"/> (seconds).</summary>
    void Advance(double h);
}

/// <summary>Base class for the static colliders — zero surface velocity, so subclasses only supply geometry.</summary>
public abstract class StaticCollider : ICollider
{
    /// <summary>Creates a static collider with the given surface material.</summary>
    protected StaticCollider(PhysicsMaterial material) => Material = material;

    /// <inheritdoc/>
    public PhysicsMaterial Material { get; }
    /// <inheritdoc/>
    public abstract AabbD Bounds { get; }
    /// <inheritdoc/>
    public abstract ContactQuery Query(Vector3D ballCenter, double ballRadius);
    /// <inheritdoc/>
    public Vector3D SurfaceVelocity(Vector3D worldPoint) => Vector3D.Zero;
}
