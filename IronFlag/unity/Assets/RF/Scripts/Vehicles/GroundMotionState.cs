using System;
using UnityEngine;

namespace IronFlag.Vehicles
{
    /// <summary>
    /// Where a vehicle is pointing and how fast it is going along that heading.
    /// </summary>
    /// <remarks>
    /// Motion is deliberately one-dimensional: a vehicle always travels along its own
    /// forward axis, so a heading and a signed speed say everything about it. There is no
    /// lateral slide, which is what keeps these vehicles feeling like the ones in the game
    /// this is a homage to rather than like a driving simulator.
    /// </remarks>
    [Serializable]
    public readonly struct GroundMotionState : IEquatable<GroundMotionState>
    {
        /// <summary>
        /// Creates a motion state.
        /// </summary>
        /// <param name="speed">Speed along the heading; negative is reversing.</param>
        /// <param name="yawDegrees">Heading, clockwise from world +Z.</param>
        public GroundMotionState(float speed, float yawDegrees)
        {
            Speed = speed;
            YawDegrees = yawDegrees;
        }

        /// <summary>Stationary, facing world +Z.</summary>
        public static GroundMotionState Still => default;

        /// <summary>Speed along <see cref="Forward"/> in m/s; negative is reversing.</summary>
        public float Speed { get; }

        /// <summary>Heading in degrees, clockwise from world +Z, as a Unity yaw.</summary>
        public float YawDegrees { get; }

        /// <summary>Unit vector the vehicle is facing.</summary>
        public Vector3 Forward => Quaternion.Euler(0.0f, YawDegrees, 0.0f) * Vector3.forward;

        /// <summary>Horizontal velocity implied by the heading and speed.</summary>
        public Vector3 Velocity => Forward * Speed;

        /// <summary>Rotation matching <see cref="YawDegrees"/>.</summary>
        public Quaternion Rotation => Quaternion.Euler(0.0f, YawDegrees, 0.0f);

        /// <summary>
        /// Returns this state with its speed replaced.
        /// </summary>
        /// <param name="speed">Speed to carry instead.</param>
        /// <returns>A state with the same heading and the given speed.</returns>
        public GroundMotionState WithSpeed(float speed) => new GroundMotionState(speed, YawDegrees);

        /// <summary>
        /// Returns this state with its heading replaced.
        /// </summary>
        /// <param name="yawDegrees">Heading to carry instead.</param>
        /// <returns>A state with the same speed and the given heading.</returns>
        public GroundMotionState WithYaw(float yawDegrees) => new GroundMotionState(Speed, yawDegrees);

        /// <inheritdoc/>
        public bool Equals(GroundMotionState other)
            => Mathf.Approximately(Speed, other.Speed)
                && Mathf.Approximately(YawDegrees, other.YawDegrees);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is GroundMotionState other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => (Speed, YawDegrees).GetHashCode();

        /// <inheritdoc/>
        public override string ToString() => $"{Speed:0.##} m/s at {YawDegrees:0.#} deg";
    }
}
