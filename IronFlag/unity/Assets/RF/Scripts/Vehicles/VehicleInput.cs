using System;
using UnityEngine;

namespace IronFlag.Vehicles
{
    /// <summary>
    /// One frame of pilot intent, in a form no vehicle has to know the input device for.
    /// </summary>
    /// <remarks>
    /// Everything that drives a vehicle - the local player, and later a replay or an
    /// AI - produces one of these per frame and hands it to
    /// <see cref="VehicleController.SetInput"/>. A vehicle that receives
    /// <see cref="Idle"/> coasts to a stop, which is exactly what an abandoned vehicle
    /// should do, so "nobody is driving" needs no separate state.
    /// </remarks>
    [Serializable]
    public readonly struct VehicleInput : IEquatable<VehicleInput>
    {
        /// <summary>
        /// Creates one frame of intent, with the trigger released.
        /// </summary>
        /// <param name="drive">Steer on X, throttle on Y, each in -1..1.</param>
        /// <param name="aim">World-space XZ direction the weapon should point, or zero.</param>
        /// <param name="lift">Climb on 1, descend on -1, hold altitude on 0.</param>
        public VehicleInput(Vector2 drive, Vector2 aim, float lift)
            : this(drive, aim, lift, false)
        {
        }

        /// <summary>
        /// Creates one frame of intent.
        /// </summary>
        /// <param name="drive">Steer on X, throttle on Y, each in -1..1.</param>
        /// <param name="aim">World-space XZ direction the weapon should point, or zero.</param>
        /// <param name="lift">Climb on 1, descend on -1, hold altitude on 0.</param>
        /// <param name="fire">Whether the trigger is held down this frame.</param>
        public VehicleInput(Vector2 drive, Vector2 aim, float lift, bool fire)
        {
            Drive = drive;
            Aim = aim;
            Lift = lift;
            Fire = fire;
        }

        /// <summary>Hands off the controls: the vehicle coasts and its turret recentres.</summary>
        public static VehicleInput Idle => default;

        /// <summary>Steer on X (right positive) and throttle on Y (forward positive), each -1..1.</summary>
        public Vector2 Drive { get; }

        /// <summary>
        /// World-space direction on the ground plane the weapon should point, X mapping to
        /// world X and Y to world Z. Zero means "no aim this frame", which recentres a turret.
        /// </summary>
        public Vector2 Aim { get; }

        /// <summary>Vertical intent for aircraft, -1..1. Ground vehicles ignore it.</summary>
        public float Lift { get; }

        /// <summary>
        /// Whether the trigger is held down this frame.
        /// </summary>
        /// <remarks>
        /// Held rather than pressed: what a gun does with a held trigger is the gun's
        /// business, and every weapon in the roster already has a rate of fire deciding it.
        /// Making the pilot's intent "pressed this frame" instead would put the difference
        /// between a cannon and a chaingun in the input layer, where no vehicle could reach
        /// it.
        /// </remarks>
        public bool Fire { get; }

        /// <summary>Steer component of <see cref="Drive"/>, right positive.</summary>
        public float Steer => Drive.x;

        /// <summary>Throttle component of <see cref="Drive"/>, forward positive.</summary>
        public float Throttle => Drive.y;

        /// <inheritdoc/>
        public bool Equals(VehicleInput other)
            => Drive == other.Drive && Aim == other.Aim
                && Mathf.Approximately(Lift, other.Lift) && Fire == other.Fire;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is VehicleInput other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
            => (Drive, Aim, Lift, Fire).GetHashCode();

        /// <inheritdoc/>
        public override string ToString()
            => $"drive {Drive}, aim {Aim}, lift {Lift:0.##}, fire {Fire}";
    }
}
