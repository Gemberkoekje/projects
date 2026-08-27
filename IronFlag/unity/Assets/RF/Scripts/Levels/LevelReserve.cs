using System;
using UnityEngine;
using IronFlag.Vehicles;

namespace IronFlag.Levels
{
    /// <summary>
    /// How many of each vehicle a side gets for the whole match, and never gets again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A vehicle destroyed is a vehicle gone. This is the number it comes off, and it is the
    /// only finite resource in the game that a bunker does not put back - fuel, ammunition
    /// and hit points are all refilled the moment a vehicle is inside, and the reason those
    /// can be is that this one cannot.
    /// </para>
    /// <para>
    /// <strong>Why this is in the level file at all.</strong>
    /// <see cref="LevelDefinition"/> says a level carries no balance: what a building takes
    /// is <see cref="IronFlag.Destruction.StructureTuning"/> and how far a flag can be seen
    /// is <see cref="IronFlag.Objective.FlagRules"/>, because two levels that disagreed
    /// about either would be two games wearing one name. This is the exception, and it is
    /// one for the same reason the towers are: it is a <em>quantity of things placed on this
    /// map</em> rather than a rule about what one of them does. A map with one crossing and
    /// four jeeps is the same game as a map with three crossings and twelve, played at a
    /// different pitch - which is exactly what the original's missions varied.
    /// </para>
    /// <para>
    /// One block for the whole level rather than one per bunker, so a level cannot quietly
    /// give one side more than the other. Every other pairing on a map is placed twice and
    /// checked for symmetry; this is placed once and cannot be asymmetric at all.
    /// </para>
    /// <para>
    /// A file that says nothing here gets the standard allotment, which is what these
    /// defaults are for: an older map is a map played on the numbers the game shipped with,
    /// not a map with no vehicles on it.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class LevelReserve
    {
        /// <summary>Jeeps a side gets on a level that does not say.</summary>
        /// <remarks>
        /// Nearly three times any other vehicle, because the jeep is the only one that can
        /// win and the only one that dies to two hits. Running out of these is losing - see
        /// <see cref="IronFlag.Objective.TeamReserve"/> - so this number is the length of
        /// the match as much as it is a stock of vehicles.
        /// </remarks>
        public const int DefaultJeeps = 8;

        /// <summary>Tanks a side gets on a level that does not say.</summary>
        public const int DefaultTanks = 3;

        /// <summary>ASVs a side gets on a level that does not say.</summary>
        public const int DefaultAsvs = 3;

        /// <summary>Helicopters a side gets on a level that does not say.</summary>
        public const int DefaultHelicopters = 3;

        /// <summary>Jeeps each side gets.</summary>
        [Tooltip("Jeeps each side gets for the whole match. Lose the last one and you lose.")]
        public int Jeeps = DefaultJeeps;

        /// <summary>Tanks each side gets.</summary>
        [Tooltip("Tanks each side gets for the whole match.")]
        public int Tanks = DefaultTanks;

        /// <summary>ASVs each side gets.</summary>
        [Tooltip("ASVs each side gets for the whole match.")]
        public int Asvs = DefaultAsvs;

        /// <summary>Helicopters each side gets.</summary>
        [Tooltip("Helicopters each side gets for the whole match.")]
        public int Helicopters = DefaultHelicopters;

        /// <summary>
        /// Returns how many of one vehicle a side gets.
        /// </summary>
        /// <param name="kind">Vehicle to look up.</param>
        /// <returns>The count, or zero for a vehicle this format does not carry.</returns>
        public int For(VehicleKind kind)
        {
            switch (kind)
            {
                case VehicleKind.Jeep:
                    return Jeeps;
                case VehicleKind.Tank:
                    return Tanks;
                case VehicleKind.Asv:
                    return Asvs;
                case VehicleKind.Helicopter:
                    return Helicopters;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Sets how many of one vehicle a side gets.
        /// </summary>
        /// <param name="kind">Vehicle to set.</param>
        /// <param name="count">How many; negative is read as none.</param>
        /// <remarks>
        /// What the level editor's panel writes through, so that the panel is a loop over
        /// <see cref="VehicleRoster.Kinds"/> rather than four hand-written rows that would
        /// have to be found again the day the roster grows.
        /// </remarks>
        public void Set(VehicleKind kind, int count)
        {
            int held = Mathf.Max(0, count);

            switch (kind)
            {
                case VehicleKind.Jeep:
                    Jeeps = held;
                    break;
                case VehicleKind.Tank:
                    Tanks = held;
                    break;
                case VehicleKind.Asv:
                    Asvs = held;
                    break;
                case VehicleKind.Helicopter:
                    Helicopters = held;
                    break;
                default:
                    break;
            }
        }
    }
}
