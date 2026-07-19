using System;

namespace Pinball.Physics;

/// <summary>
/// Measured SI constants for the simulation (pinball-plan §6.1.1 / §6.1.9). These are physical material
/// properties sourced from literature — a real 27&#160;mm carbon-steel ball on a ~6.5° inclined clearcoat
/// playfield — <b>not</b> feel knobs. Expected values in the §6.1.8 tests are computed from these, so the
/// tests assert against physics rather than hand-tuned magic numbers.
/// </summary>
public static class PhysicsConstants
{
    /// <summary>Ball radius r (m). Diameter 27.0&#160;mm (1-1/16″).</summary>
    public const double BallRadius = 0.0135;
    /// <summary>Ball mass m (kg). 80.6&#160;g carbon steel.</summary>
    public const double BallMass = 0.0806;

    /// <summary>Solid-sphere moment of inertia <c>I = ⅖ m r²</c> (kg·m²), isotropic so <c>I⁻¹ = 1/I</c>.</summary>
    public static readonly double BallInertia = 0.4 * BallMass * BallRadius * BallRadius;

    /// <summary>Standard gravity g (m/s²).</summary>
    public const double Gravity = 9.80665;

    /// <summary>Default playfield incline α (radians); 6.5° nominal (§6.1.1, range 6.0–7.0°).</summary>
    public static readonly double DefaultInclineRadians = 6.5 * Math.PI / 180.0;

    /// <summary>Air density ρ at 20&#160;°C (kg/m³).</summary>
    public const double AirDensity = 1.204;

    /// <summary>Ball cross-sectional area <c>A = π r²</c> (m²), for drag/Magnus.</summary>
    public static readonly double CrossSection = Math.PI * BallRadius * BallRadius;

    /// <summary>Drag coefficient of a smooth sphere at Re ≈ 10⁴ (sub-crisis, §6.1.4).</summary>
    public const double DragCoefficient = 0.47;

    /// <summary>Magnus lift slope <c>k_L</c> in <c>C_L(S) = min(C_L,max, k_L·S)</c> (§6.1.3).</summary>
    public const double MagnusLiftSlope = 0.25;
    /// <summary>Magnus lift ceiling <c>C_L,max</c> (§6.1.3).</summary>
    public const double MagnusLiftMax = 0.35;

    /// <summary>Fixed physics substep h (s). 1000&#160;Hz base rate (§6.1.5).</summary>
    public const double SubstepSeconds = 0.001;

    /// <summary>Down-slope gravity drive <c>g·sinα</c> (m/s²) for incline <paramref name="inclineRadians"/>.</summary>
    public static double DownSlopeAcceleration(double inclineRadians) => Gravity * Math.Sin(inclineRadians);

    /// <summary>Normal-load gravity component <c>g·cosα</c> (m/s²) for incline <paramref name="inclineRadians"/>.</summary>
    public static double NormalLoadAcceleration(double inclineRadians) => Gravity * Math.Cos(inclineRadians);
}
