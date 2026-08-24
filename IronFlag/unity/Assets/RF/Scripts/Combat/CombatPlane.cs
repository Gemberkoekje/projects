using UnityEngine;

namespace IronFlag.Combat
{
    /// <summary>
    /// The rule that combat happens on the map, not in the air above it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This game is played through a fixed camera looking down at a plane. Height exists -
    /// the helicopter flies, the tank's barrel is higher than a jeep's roof - but none of it
    /// is something a player can see, aim at, or reason about. Resolving hits in three
    /// dimensions anyway produced a game where the tank's shells sailed over the jeep, the
    /// helicopter could not hit anything at all from ten metres up, and neither failure gave
    /// the player any clue why. The original this is a homage to looked three-dimensional
    /// and was not, and that is the right answer here too.
    /// </para>
    /// <para>
    /// So a round is not a point. It is a <em>column</em>, standing from just above the
    /// ground to above the helicopter's ceiling, swept horizontally: what it hits is decided
    /// by where things are on the map and never by how tall they are. Two consequences worth
    /// knowing, both deliberate - a ground vehicle can shoot down a helicopter overhead, and
    /// a helicopter cannot shoot past one it is hovering over.
    /// </para>
    /// <para>
    /// Rounds are still <em>drawn</em> in three dimensions, because a tracer that leaves the
    /// barrel it came out of looks right and one that does not looks broken.
    /// <see cref="DepressionFrom"/> is what keeps the two stories close together: a round
    /// leaves the muzzle aimed to arrive at <see cref="ShootingHeight"/> at the far end of
    /// its reach, so a strafing helicopter visibly fires down at the ground it is hitting.
    /// </para>
    /// </remarks>
    public static class CombatPlane
    {
        /// <summary>
        /// Bottom of the column a round sweeps, in metres above the ground.
        /// </summary>
        /// <remarks>
        /// Above the ground plane rather than on it. A column that reached the floor would
        /// hit the map itself on the first step of every shot ever fired.
        /// </remarks>
        public const float Floor = 0.5f;

        /// <summary>
        /// Top of the column a round sweeps, in metres above the ground.
        /// </summary>
        /// <remarks>
        /// Well clear of <see cref="IronFlag.Vehicles.FlightTuning.CruiseAltitude"/> plus a
        /// rotor, so a helicopter is always inside the column being swept at it. Nothing in
        /// the game is allowed above this, which is why it is a constant rather than a
        /// measurement - and since the aircraft lost its collective there is nothing that
        /// could try.
        /// </remarks>
        public const float Ceiling = 30.0f;

        /// <summary>
        /// The height rounds converge on, in metres: roughly where a hull is.
        /// </summary>
        public const float ShootingHeight = 1.0f;

        /// <summary>
        /// Returns the ends of the column a round at one place sweeps with.
        /// </summary>
        /// <param name="at">Where the round is; only its horizontal position is used.</param>
        /// <param name="bottom">Bottom of the column.</param>
        /// <param name="top">Top of the column.</param>
        public static void Column(Vector3 at, out Vector3 bottom, out Vector3 top)
        {
            bottom = new Vector3(at.x, Floor, at.z);
            top = new Vector3(at.x, Ceiling, at.z);
        }

        /// <summary>
        /// Drops the height out of a vector, leaving what the map sees of it.
        /// </summary>
        /// <param name="value">Any vector.</param>
        /// <returns>The same vector with no vertical component.</returns>
        public static Vector3 Flatten(Vector3 value) => new Vector3(value.x, 0.0f, value.z);

        /// <summary>
        /// Returns the distance between two points as the map measures it.
        /// </summary>
        /// <param name="from">One point.</param>
        /// <param name="to">The other.</param>
        /// <returns>The horizontal distance in metres.</returns>
        /// <remarks>
        /// Used for blast falloff, so a rocket landing under a helicopter is as dangerous to
        /// it as one landing beside a tank. Measuring through the air instead would make the
        /// same weapon behave differently against a target whose only difference is that it
        /// flies.
        /// </remarks>
        public static float DistanceOnMap(Vector3 from, Vector3 to) => Flatten(to - from).magnitude;

        /// <summary>
        /// Returns the launch angle that brings a round down onto the combat plane.
        /// </summary>
        /// <param name="muzzleHeight">Height the round leaves at, in metres.</param>
        /// <param name="range">How far the round reaches, in metres.</param>
        /// <returns>
        /// A negative angle in degrees, or zero when the muzzle is already low enough or the
        /// weapon has no reach to converge over.
        /// </returns>
        /// <remarks>
        /// Purely so the picture matches the mechanic. The column is what decides hits; this
        /// is what stops a helicopter's tracers streaking level over the top of the vehicle
        /// they are demonstrably killing.
        /// </remarks>
        public static float DepressionFrom(float muzzleHeight, float range)
        {
            float fall = muzzleHeight - ShootingHeight;
            if (range <= 0.0f || fall <= 0.0f)
            {
                return 0.0f;
            }

            return -Mathf.Atan2(fall, range) * Mathf.Rad2Deg;
        }
    }
}
