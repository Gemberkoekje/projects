using System;
using UnityEngine;

namespace IronFlag.Vehicles
{
    /// <summary>
    /// The vertical half of a helicopter's handling: everything a ground vehicle has no
    /// use for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept out of <see cref="VehicleTuning"/> so three quarters of the roster does not
    /// carry inspector fields that do nothing. Altitudes are metres above the ground
    /// plane; the helicopter model sits on its skids at zero.
    /// </para>
    /// <para>
    /// There is one altitude rather than a band, because the pilot no longer has a
    /// collective: a helicopter flies at <see cref="CruiseAltitude"/> and nothing it does
    /// changes that. The ceiling and floor this used to carry were the two ends of a
    /// choice that has been taken away, so they are gone rather than left as numbers
    /// nothing reads. <see cref="ClimbRate"/> and <see cref="ClimbAcceleration"/> survive
    /// because the aircraft still has to <em>get</em> to that altitude - off the pad on
    /// deploy, and back up after something shoves it - and doing that instantly looks
    /// like a teleport.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class FlightTuning
    {
        /// <summary>The one altitude a running helicopter flies at.</summary>
        /// <remarks>
        /// Ten metres clears everything on the map - the flag tower is the tallest thing
        /// at six and a half - and sits well under <see cref="IronFlag.Combat.CombatPlane.Ceiling"/>,
        /// so a helicopter is inside every column ever swept at it. It is also the
        /// altitude the aircraft used to deploy at, so locking the collective changed
        /// where a helicopter sits by nothing at all.
        /// </remarks>
        [Tooltip("The one altitude a running helicopter flies at, in metres above the ground.")]
        public float CruiseAltitude = 10.0f;

        /// <summary>Altitude a helicopter with a dead engine settles onto.</summary>
        /// <remarks>
        /// Zero is skids-down. A stranded aircraft comes down rather than hanging where it
        /// ran dry - see <see cref="IronFlag.Supply.VehicleSupply"/> - which puts it on the
        /// ground where it can be shot at instead of parked over the map for ever.
        /// </remarks>
        [Tooltip("Altitude a helicopter with a dead engine settles onto. Zero is skids-down.")]
        public float GroundedAltitude = 0.0f;

        /// <summary>Fastest the aircraft closes on the altitude it is holding.</summary>
        [Tooltip("Fastest the aircraft climbs or settles towards its altitude, in m/s.")]
        public float ClimbRate = 7.0f;

        /// <summary>How quickly vertical speed responds.</summary>
        [Tooltip("How quickly vertical speed changes, in m/s^2.")]
        public float ClimbAcceleration = 14.0f;

        /// <summary>Nose-down angle at full forward speed. Cosmetic.</summary>
        [Tooltip("Cosmetic nose-down angle at top speed, in degrees.")]
        public float MaxPitch = 12.0f;

        /// <summary>Bank angle at full steering. Cosmetic.</summary>
        [Tooltip("Cosmetic bank angle at full steering, in degrees.")]
        public float MaxRoll = 18.0f;

        /// <summary>How quickly the cosmetic attitude follows the controls.</summary>
        [Tooltip("Seconds for the cosmetic tilt to catch up with the controls.")]
        public float AttitudeDamping = 0.25f;

        /// <summary>
        /// Returns the altitude a helicopter holds in one of its two states.
        /// </summary>
        /// <param name="powered">Whether the engine is still running.</param>
        /// <returns>The altitude to fly towards, in metres.</returns>
        /// <remarks>
        /// The whole of the vertical decision, and it is not the pilot's: a helicopter is
        /// either flying or it has run out of fuel.
        /// </remarks>
        public float AltitudeHeld(bool powered) => powered ? CruiseAltitude : GroundedAltitude;
    }
}
