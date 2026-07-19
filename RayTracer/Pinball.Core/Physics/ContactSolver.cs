using System;

namespace Pinball.Physics;

/// <summary>
/// The core restitution + Coulomb-friction impulse solver for a single sphere-vs-surface contact
/// (pinball-plan §6.1.2). For a sphere the normal passes through the centre, so the normal impulse is
/// torque-free (effective mass <c>1/m</c>) and <b>never creates spin</b>; all spin comes from the
/// tangential friction impulse, whose reduced mass is the constant <c>2m/7</c>. Grip vs slide (and thus the
/// skid-to-roll transition) emerges from the friction cone — it is not scripted.
/// </summary>
public static class ContactSolver
{
    private const double SlipEpsilon = 1e-9;

    /// <summary>
    /// Applies the contact impulse for <paramref name="contact"/> to the ball's linear velocity
    /// <paramref name="v"/> and angular velocity <paramref name="omega"/> (ball centre <paramref name="ballCenter"/>).
    /// A no-op when the contact point is separating, so the same call is safe for resting contacts.
    /// </summary>
    public static void Resolve(ref Vector3D v, ref Vector3D omega, Vector3D ballCenter, in Contact contact)
    {
        double m = PhysicsConstants.BallMass;
        double invI = 1.0 / PhysicsConstants.BallInertia;

        Vector3D n = contact.Normal;
        Vector3D rc = contact.Point - ballCenter;                       // arm centre→contact (= −r·n)
        Vector3D vc = v + Vector3D.Cross(omega, rc) - contact.SurfaceVelocity; // contact-point velocity vs surface
        double vn = Vector3D.Dot(vc, n);                                // <0 ⇒ approaching

        if (vn >= 0.0)
            return; // separating (or purely resting) — no normal impulse this step, hence no friction either

        // Normal impulse with velocity-thresholded restitution (resting contacts get e→0, §6.1.9).
        double approachSpeed = -vn;
        double e = contact.Material.EffectiveRestitution(approachSpeed);
        double jn = -(1.0 + e) * m * vn;                                // ≥ 0
        Vector3D impulse = n * jn;

        // Active zone (bumper/kicker/slingshot, §6.1.6): above the trigger speed the coil adds an outward
        // impulse, so the ball leaves with more energy than it arrived — the one place energy is injected,
        // physically justified (the coil does work). Self-limiting: the ball then separates and won't re-fire.
        if (contact.ActiveImpulse > 0.0 && approachSpeed >= contact.ActiveTriggerSpeed)
            impulse += n * contact.ActiveImpulse;

        // Friction impulse, Coulomb-capped. Tangential reduced mass 2m/7 for a solid sphere (K_t = 7/2m).
        Vector3D vtVec = vc - n * vn;
        double vt = vtVec.Length();
        if (vt > SlipEpsilon)
        {
            Vector3D tHat = vtVec / vt;
            double jtStar = (2.0 * m / 7.0) * vt;                       // impulse that exactly arrests slip
            double jtMaxStatic = contact.Material.StaticFriction * jn;
            double jt = jtStar <= jtMaxStatic ? jtStar : contact.Material.KineticFriction * jn;
            Vector3D frictionImpulse = tHat * (-jt);
            impulse += frictionImpulse;
            omega += Vector3D.Cross(rc, frictionImpulse) * invI;        // only friction torques a sphere
        }

        v += impulse / m;
    }
}
