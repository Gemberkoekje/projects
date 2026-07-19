namespace Pinball.Physics;

/// <summary>
/// The pairwise contact coefficients between the steel ball and one collider surface (pinball-plan §6.1.9).
/// Every value is a measured material property (steel-on-X restitution/friction, rolling resistance),
/// starting from the literature values in the §6.1.9 table; a collider carries the material of <i>its</i>
/// surface. <paramref name="Restitution"/> is the low-speed value — see <see cref="EffectiveRestitution"/>
/// for the velocity-dependent falloff and the resting-contact threshold.
/// </summary>
/// <param name="Restitution">Coefficient of restitution e (0 = dead, 1 = elastic), at low approach speed.</param>
/// <param name="StaticFriction">Coulomb static friction μ_s (gripping).</param>
/// <param name="KineticFriction">Coulomb kinetic friction μ_k (sliding).</param>
/// <param name="RollingResistance">Rolling-resistance coefficient C_rr (hysteresis loss, §6.1.2).</param>
/// <param name="RestitutionFalloff">
/// Per-(m/s) linear falloff of e with approach speed (rubbers soften with speed, §6.1.9); 0 = constant e.
/// </param>
public readonly record struct PhysicsMaterial(
    double Restitution,
    double StaticFriction,
    double KineticFriction,
    double RollingResistance,
    double RestitutionFalloff = 0.0)
{
    /// <summary>
    /// The effective restitution at a given normal approach speed <paramref name="approachSpeed"/> (|v_n|,
    /// m/s, positive). Applies the velocity falloff and, crucially, collapses to 0 below
    /// <see cref="RestingSpeedThreshold"/> so per-step gravity-injected normal velocity on a resting/captured
    /// ball is absorbed rather than micro-bounced forever (§6.1.9).
    /// </summary>
    public double EffectiveRestitution(double approachSpeed)
    {
        if (approachSpeed < RestingSpeedThreshold)
            return 0.0;
        double e = Restitution - RestitutionFalloff * approachSpeed;
        return e < 0.0 ? 0.0 : e;
    }

    /// <summary>Approach speeds below this (m/s) are treated as resting contact (e→0), well under launch speeds.</summary>
    public const double RestingSpeedThreshold = 0.05;

    // ── Literature presets (§6.1.9). The ball is always steel; these describe steel-on-<surface>. ──

    /// <summary>Steel on steel — posts, metal guides (e ≈ 0.6, μ ≈ 0.2).</summary>
    public static PhysicsMaterial Steel => new(0.60, 0.20, 0.18, 0.0015);

    /// <summary>Steel on clearcoated wood — the playfield surface (e ≈ 0.25, low μ, VPX-like, §6.1.9).</summary>
    public static PhysicsMaterial Playfield => new(0.25, 0.18, 0.15, 0.002);

    /// <summary>Steel on rubber — flippers, slingshots, post rubbers (e ≈ 0.88 with speed falloff, high grip).</summary>
    public static PhysicsMaterial Rubber => new(0.88, 0.50, 0.42, 0.0, RestitutionFalloff: 0.03);
}
