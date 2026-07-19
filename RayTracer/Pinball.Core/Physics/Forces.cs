using System;

namespace Pinball.Physics;

/// <summary>
/// The continuous external forces on the ball, as accelerations (per unit mass): true-3-D gravity, quadratic
/// aerodynamic drag (§6.1.4), and Magnus lift of a spinning ball (§6.1.3). Contact impulses are handled
/// separately by the solver. Every term is physically modelled from the SI constants; drag and Magnus are
/// honestly small at steel-ball pinball speeds but included because they are correct and cheap, and because
/// the terminal-velocity / lateral-curve tests assert their predicted (small) magnitudes.
/// </summary>
public static class Forces
{
    /// <summary>Total external acceleration (m/s²) on the ball in state <paramref name="ball"/> under <paramref name="settings"/>.</summary>
    public static Vector3D ExternalAcceleration(in BallState ball, in PhysicsSettings settings)
    {
        Vector3D a = Vector3D.Zero;

        if (settings.EnableGravity)
            a += settings.GravityVector;

        Vector3D v = ball.LinearVelocity;
        double speed = v.Length();

        if (settings.EnableDrag && speed > 1e-9)
        {
            // F_drag = −½ ρ C_D A |v| v  ⇒  a = F/m.
            double dragMag = 0.5 * PhysicsConstants.AirDensity * PhysicsConstants.DragCoefficient
                * PhysicsConstants.CrossSection * speed;
            a += v * (-dragMag / PhysicsConstants.BallMass);
        }

        if (settings.EnableMagnus && speed > 1e-6)
        {
            Vector3D omega = ball.AngularVelocity;
            Vector3D liftDir = Vector3D.Cross(omega, v);
            double liftDirLen = liftDir.Length();
            if (liftDirLen > 1e-9)
            {
                // Spin parameter S = |ω| r / |v|; lift coefficient C_L(S) = min(C_L,max, k_L·S).
                double spinParam = omega.Length() * PhysicsConstants.BallRadius / speed;
                double cl = Math.Min(PhysicsConstants.MagnusLiftMax, PhysicsConstants.MagnusLiftSlope * spinParam);
                double magnusMag = 0.5 * PhysicsConstants.AirDensity * PhysicsConstants.CrossSection * cl * speed * speed;
                a += (liftDir / liftDirLen) * (magnusMag / PhysicsConstants.BallMass);
            }
        }

        return a;
    }
}
