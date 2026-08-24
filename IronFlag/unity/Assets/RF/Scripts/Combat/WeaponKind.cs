using System;

namespace IronFlag.Combat
{
    /// <summary>
    /// The guns in the game: one per vehicle, plus the one nobody drives.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A weapon is a property of the mount carrying it rather than something a pilot picks.
    /// The first four are parallel to <see cref="IronFlag.Vehicles.VehicleKind"/> and
    /// <see cref="WeaponTuning.For"/> is the mapping between them; they are in roster order,
    /// the jeep's lobbed grenades first and the helicopter's chaingun last.
    /// </para>
    /// <para>
    /// <see cref="Autocannon"/> is the exception and is deliberately last: it is bolted to
    /// a building rather than to a vehicle, so it has no roster slot to be parallel to and
    /// is looked up by <see cref="WeaponTuning.Emplacement"/> instead.
    /// </para>
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

        /// <summary>The automated turret's gun: the only one with nobody behind it.</summary>
        Autocannon = 5,
    }
}
