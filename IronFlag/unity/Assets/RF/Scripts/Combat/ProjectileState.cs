using System;
using UnityEngine;

namespace IronFlag.Combat
{
    /// <summary>
    /// Where a round is and how fast it is going, which is all a round ever is.
    /// </summary>
    /// <remarks>
    /// The counterpart of <see cref="IronFlag.Vehicles.GroundMotionState"/> for things that
    /// have left a barrel: a value advanced by a pure function
    /// (<see cref="ProjectileMotion.Step"/>) rather than by a rigidbody. Rounds are not
    /// simulated by physics for the same reason vehicles are not - the flight path has to
    /// be predictable enough to aim at, and a projectile that can be nudged by a collision
    /// is neither predictable nor cheap.
    /// </remarks>
    [Serializable]
    public readonly struct ProjectileState : IEquatable<ProjectileState>
    {
        /// <summary>
        /// Creates one instant of a round's flight.
        /// </summary>
        /// <param name="position">Where the round is, in world space.</param>
        /// <param name="velocity">How fast it is going, in metres per second.</param>
        public ProjectileState(Vector3 position, Vector3 velocity)
        {
            Position = position;
            Velocity = velocity;
        }

        /// <summary>Where the round is, in world space.</summary>
        public Vector3 Position { get; }

        /// <summary>How fast the round is going, in metres per second.</summary>
        public Vector3 Velocity { get; }

        /// <summary>Speed along the flight path, in metres per second.</summary>
        public float Speed => Velocity.magnitude;

        /// <inheritdoc/>
        public bool Equals(ProjectileState other)
            => Position == other.Position && Velocity == other.Velocity;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ProjectileState other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => (Position, Velocity).GetHashCode();

        /// <inheritdoc/>
        public override string ToString() => $"at {Position}, going {Velocity}";
    }
}
