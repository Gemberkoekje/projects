using System;

namespace IronFlag.Combat
{
    /// <summary>
    /// The four weapons of the core roster, one per vehicle.
    /// </summary>
    /// <remarks>
    /// A weapon is a property of the vehicle carrying it rather than something a pilot
    /// picks, so this enum is parallel to <see cref="IronFlag.Vehicles.VehicleKind"/> and
    /// <see cref="WeaponTuning.For"/> is the mapping between them. The order matches the
    /// roster order: the jeep's lobbed grenades first, the helicopter's chaingun last.
    /// </remarks>
    [Serializable]
    public enum WeaponKind
    {
        /// <summary>No weapon: an unconfigured mount, or a vehicle that cannot shoot.</summary>
        None = 0,

        /// <summary>The jeep's lobbed grenades: short, arcing and fixed forward.</summary>
        Grenade = 1,

        /// <summary>The tank's turret cannon: flat, fast and the longest reach in the game.</summary>
        Cannon = 2,

        /// <summary>The ASV's rockets: the heaviest single hit, and the easiest to see coming.</summary>
        Rocket = 3,

        /// <summary>The helicopter's chaingun: small rounds, fired faster than anything else.</summary>
        Chaingun = 4,
    }
}
