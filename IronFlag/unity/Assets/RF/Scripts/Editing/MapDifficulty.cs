using System;

namespace IronFlag.Editing
{
    /// <summary>
    /// How hard a generated map is meant to be, which on this game's terms is how big it is
    /// and how much of it is defended.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>It cannot mean anything else yet, and that is worth saying out loud.</strong>
    /// A turret's health, its rate of fire and its reach are global constants -
    /// <see cref="IronFlag.Destruction.StructureTuning.For"/> and
    /// <see cref="IronFlag.Combat.WeaponTuning.Emplacement"/> - rather than per-instance
    /// fields of a level file, and a level deliberately carries placement rather than
    /// balance. So difficulty here scales the number of emplacements and the ground between
    /// them, never how tough any one of them is. Making it mean more would mean extending
    /// the level format, which is a different piece of work.
    /// </para>
    /// <para>
    /// A bigger map is genuinely harder in this game rather than merely longer: fuel and
    /// ammunition are finite, the depots are fixed, and a longer run to the enemy flag is a
    /// run that has to be planned around resupply.
    /// </para>
    /// </remarks>
    [Serializable]
    public enum MapDifficulty
    {
        /// <summary>Not a difficulty, which is what an unset option reads.</summary>
        None = 0,

        /// <summary>A small map with light defences.</summary>
        Easy = 1,

        /// <summary>The middle setting, and roughly the size of the shipped map.</summary>
        Medium = 2,

        /// <summary>A large map, heavily defended.</summary>
        Hard = 3,
    }
}
