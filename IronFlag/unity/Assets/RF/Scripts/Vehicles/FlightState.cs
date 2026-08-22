using System;
using UnityEngine;

namespace IronFlag.Vehicles
{
    /// <summary>
    /// How high an aircraft is and how fast that is changing.
    /// </summary>
    /// <remarks>
    /// The horizontal half of a helicopter's motion is an ordinary
    /// <see cref="GroundMotionState"/>; this is everything the third axis adds.
    /// </remarks>
    [Serializable]
    public readonly struct FlightState : IEquatable<FlightState>
    {
        /// <summary>
        /// Creates a flight state.
        /// </summary>
        /// <param name="altitude">Height above the ground plane, in metres.</param>
        /// <param name="verticalSpeed">Rate of climb in m/s; negative is descending.</param>
        public FlightState(float altitude, float verticalSpeed)
        {
            Altitude = altitude;
            VerticalSpeed = verticalSpeed;
        }

        /// <summary>Sitting on the ground, not moving.</summary>
        public static FlightState Landed => default;

        /// <summary>Height above the ground plane, in metres.</summary>
        public float Altitude { get; }

        /// <summary>Rate of climb in m/s; negative is descending.</summary>
        public float VerticalSpeed { get; }

        /// <summary>
        /// Returns a state hovering at a given altitude.
        /// </summary>
        /// <param name="altitude">Altitude to hold.</param>
        /// <returns>A state at that altitude with no vertical movement.</returns>
        public static FlightState Hovering(float altitude) => new FlightState(altitude, 0.0f);

        /// <inheritdoc/>
        public bool Equals(FlightState other)
            => Mathf.Approximately(Altitude, other.Altitude)
                && Mathf.Approximately(VerticalSpeed, other.VerticalSpeed);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is FlightState other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => (Altitude, VerticalSpeed).GetHashCode();

        /// <inheritdoc/>
        public override string ToString() => $"{Altitude:0.##} m, {VerticalSpeed:0.##} m/s";
    }
}
