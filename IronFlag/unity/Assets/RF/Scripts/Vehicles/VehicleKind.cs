using System;

namespace IronFlag.Vehicles
{
    /// <summary>
    /// The four vehicles of the core roster.
    /// </summary>
    /// <remarks>
    /// The order is the roster order used everywhere the four are listed - fastest and
    /// lightest first, slowest and heaviest last, with the helicopter's separate
    /// airborne role at the end.
    /// </remarks>
    [Serializable]
    public enum VehicleKind
    {
        /// <summary>No vehicle: an unconfigured controller, or "on foot" in the bunker.</summary>
        None = 0,

        /// <summary>Fastest, weakest, and the only vehicle that can carry the flag.</summary>
        Jeep = 1,

        /// <summary>Medium all-rounder with a rotating turret.</summary>
        Tank = 2,

        /// <summary>Slowest and most armored; carries the rocket launcher.</summary>
        Asv = 3,

        /// <summary>Airborne, fast and fragile; ignores ground terrain entirely.</summary>
        Helicopter = 4,
    }
}
