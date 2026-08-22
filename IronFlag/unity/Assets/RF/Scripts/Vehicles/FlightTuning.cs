using System;
using UnityEngine;

namespace IronFlag.Vehicles
{
    /// <summary>
    /// The vertical half of a helicopter's handling: everything a ground vehicle has no
    /// use for.
    /// </summary>
    /// <remarks>
    /// Kept out of <see cref="VehicleTuning"/> so three quarters of the roster does not
    /// carry inspector fields that do nothing. Altitudes are metres above the ground
    /// plane; the helicopter model sits on its skids at zero.
    /// </remarks>
    [Serializable]
    public sealed class FlightTuning
    {
        /// <summary>Altitude the helicopter is placed at when it deploys.</summary>
        [Tooltip("Altitude the helicopter deploys at, in metres above the ground.")]
        public float DeployAltitude = 10.0f;

        /// <summary>Lowest altitude the pilot can descend to.</summary>
        [Tooltip("Lowest altitude the pilot can descend to. Zero is skids-down.")]
        public float MinAltitude = 0.0f;

        /// <summary>Ceiling the pilot cannot climb past.</summary>
        [Tooltip("Ceiling, in metres. Keeps the vehicle inside the camera's useful range.")]
        public float MaxAltitude = 26.0f;

        /// <summary>Climb and descent speed at full vertical input.</summary>
        [Tooltip("Climb and descent speed at full input, in m/s.")]
        public float ClimbRate = 7.0f;

        /// <summary>How quickly vertical speed responds to the collective.</summary>
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
    }
}
