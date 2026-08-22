using UnityEngine;

namespace IronFlag.Combat
{
    /// <summary>
    /// The flight of a round, as pure arithmetic: launch it, step it, and know in advance
    /// where a lobbed one will land.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every round in the game is a point under constant acceleration, so the whole model
    /// is three functions and no state. That is deliberate: the jeep's grenade range is a
    /// balance number the design leans on - the jeep has to close to a fixed distance to
    /// fight - and a range that is <em>derived</em> from the launch numbers cannot drift
    /// away from the flight path the player actually sees.
    /// </para>
    /// <para>
    /// The integration is exact rather than approximate. Constant acceleration has a closed
    /// form, so a longer fixed step changes nothing about where a grenade lands; Euler's
    /// method would make the range a function of the frame rate.
    /// </para>
    /// </remarks>
    public static class ProjectileMotion
    {
        /// <summary>
        /// Advances a round by one step of constant acceleration.
        /// </summary>
        /// <param name="state">Where the round is and how fast it is going.</param>
        /// <param name="drop">Downward acceleration in m/s^2; zero flies flat.</param>
        /// <param name="deltaTime">Length of the step, in seconds.</param>
        /// <returns>The round's state at the end of the step.</returns>
        /// <example>
        /// <code>
        /// ProjectileState next = ProjectileMotion.Step(state, weapon.Drop, Time.fixedDeltaTime);
        /// </code>
        /// </example>
        public static ProjectileState Step(ProjectileState state, float drop, float deltaTime)
        {
            Vector3 acceleration = Vector3.down * Mathf.Max(0.0f, drop);
            Vector3 position = state.Position
                + (state.Velocity * deltaTime)
                + (0.5f * deltaTime * deltaTime * acceleration);
            return new ProjectileState(position, state.Velocity + (acceleration * deltaTime));
        }

        /// <summary>
        /// Puts a round in the air.
        /// </summary>
        /// <param name="origin">Muzzle position in world space.</param>
        /// <param name="direction">
        /// Where the gun is pointing. Only the horizontal component is used, so a barrel
        /// nodding with a vehicle's cosmetic tilt does not throw the shot.
        /// </param>
        /// <param name="speed">Muzzle speed in metres per second.</param>
        /// <param name="elevationDegrees">Angle above the horizon to launch at.</param>
        /// <returns>The round's state as it leaves the barrel.</returns>
        /// <remarks>
        /// A zero or vertical <paramref name="direction"/> yields a round with no velocity,
        /// which expires on its first step rather than flying somewhere arbitrary.
        /// </remarks>
        public static ProjectileState Launch(
            Vector3 origin, Vector3 direction, float speed, float elevationDegrees)
        {
            var flat = new Vector3(direction.x, 0.0f, direction.z);
            if (flat.sqrMagnitude < 0.000001f)
            {
                return new ProjectileState(origin, Vector3.zero);
            }

            flat.Normalize();
            float elevation = elevationDegrees * Mathf.Deg2Rad;
            Vector3 heading = (flat * Mathf.Cos(elevation)) + (Vector3.up * Mathf.Sin(elevation));
            return new ProjectileState(origin, heading * speed);
        }

        /// <summary>
        /// Returns the angle to lob a round at so that it comes down at a chosen distance.
        /// </summary>
        /// <param name="speed">Muzzle speed in metres per second.</param>
        /// <param name="range">Distance the round should carry, in metres.</param>
        /// <param name="drop">Downward acceleration in m/s^2.</param>
        /// <returns>
        /// The flatter of the two angles that reach that distance, in degrees, or zero when
        /// the round has no drop or cannot be thrown that far however it is angled.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The inverse of <see cref="BallisticRange"/>, and the direction the weapon table
        /// actually needs: reach is the number a designer balances the jeep on, and the
        /// angle is a consequence of wanting it. Solving it here means the arc a player
        /// watches and the distance the weapon claims cannot disagree.
        /// </para>
        /// <para>
        /// Every reachable distance has two solutions - a flat one and a steep one - and
        /// this returns the flat one. The steep one is a mortar shot, and a round that
        /// climbs out of the frame on its way to something twelve metres away is not a thing
        /// anyone can aim.
        /// </para>
        /// </remarks>
        public static float LaunchElevation(float speed, float range, float drop)
        {
            if (drop <= 0.0f || speed <= 0.0f || range <= 0.0f)
            {
                return 0.0f;
            }

            float sine = drop * range / (speed * speed);
            if (sine > 1.0f)
            {
                return 0.0f;
            }

            return Mathf.Asin(sine) * Mathf.Rad2Deg * 0.5f;
        }

        /// <summary>
        /// Returns how far a lobbed round travels before it comes back to the height it
        /// was fired from.
        /// </summary>
        /// <param name="speed">Muzzle speed in metres per second.</param>
        /// <param name="elevationDegrees">Angle above the horizon it was launched at.</param>
        /// <param name="drop">Downward acceleration in m/s^2.</param>
        /// <returns>
        /// The horizontal distance in metres, or zero for a round that does not fall - a
        /// flat-flying round has no ballistic range and is limited by its own lifetime
        /// instead.
        /// </returns>
        /// <remarks>
        /// This is what makes the jeep's reach a consequence of its launch numbers rather
        /// than a second number that has to be kept in step with them.
        /// </remarks>
        public static float BallisticRange(float speed, float elevationDegrees, float drop)
        {
            if (drop <= 0.0f || speed <= 0.0f)
            {
                return 0.0f;
            }

            float elevation = elevationDegrees * Mathf.Deg2Rad;
            return speed * speed * Mathf.Sin(2.0f * elevation) / drop;
        }
    }
}
