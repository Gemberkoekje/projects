namespace Pinball.Physics;

/// <summary>
/// World-level physics settings (pinball-plan §6.1). The force toggles exist because several §6.1.8
/// validation tests isolate one term ("energy conservation with drag/friction/rolling off"); realistic
/// play leaves them all on. <see cref="Seed"/> feeds the deterministic PRNG for any stochastic term
/// (bounce scatter), keeping a given (seed, input) reproducible (§6.1.5).
/// </summary>
/// <param name="InclineRadians">Playfield incline α used by helpers/tests (the plane collider carries the actual tilt).</param>
/// <param name="EnableGravity">Apply true-3-D gravity <c>(0,−g,0)</c>.</param>
/// <param name="EnableDrag">Apply quadratic aerodynamic drag (§6.1.4).</param>
/// <param name="EnableMagnus">Apply the Magnus lift of a spinning ball (§6.1.3).</param>
/// <param name="EnableRollingResistance">Apply rolling resistance in rolling contact (§6.1.2).</param>
/// <param name="Seed">Seed for the deterministic scatter PRNG.</param>
/// <param name="GravityOverride">
/// Explicit gravity vector (m/s²). Null ⇒ true vertical <c>(0,−g,0)</c> against a tilted plane collider (the
/// §6.1.1 default). A flat playfield that matches the flat P0 render instead sets this to the incline-rotated
/// gravity <c>(0,−g·cosα,−g·sinα)</c>: identical 3-D dynamics in the render's untilted frame — the ball still
/// goes airborne, still gets a computed normal reaction — just expressed with a tilted g rather than a tilted
/// plane, so it is <b>not</b> the 2-D pre-projected slope the plan warns against.
/// </param>
public readonly record struct PhysicsSettings(
    double InclineRadians,
    bool EnableGravity = true,
    bool EnableDrag = true,
    bool EnableMagnus = true,
    bool EnableRollingResistance = true,
    ulong Seed = 0x5D_EE_CE_5Eul,
    Vector3D? GravityOverride = null,
    double BallRadius = PhysicsConstants.BallRadius)
{
    /// <summary>Realistic defaults at the nominal 6.5° incline with every force term on.</summary>
    public static PhysicsSettings Default => new(PhysicsConstants.DefaultInclineRadians);

    /// <summary>The gravity vector actually applied: <see cref="GravityOverride"/> if set, else true vertical.</summary>
    public Vector3D GravityVector => GravityOverride ?? new Vector3D(0, -PhysicsConstants.Gravity, 0);

    /// <summary>All non-gravity, non-contact force terms off — for the closed-form energy/incline tests.</summary>
    public PhysicsSettings WithForcesOff() => this with
    {
        EnableDrag = false,
        EnableMagnus = false,
        EnableRollingResistance = false,
    };
}
