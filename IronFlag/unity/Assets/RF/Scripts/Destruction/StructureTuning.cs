using System;
using UnityEngine;

namespace IronFlag.Destruction
{
    /// <summary>
    /// The numbers that make one destructible different from the next: how much it takes to
    /// knock down, when it starts to look like it, and how big a mess it makes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A table in one file, for the same reason as
    /// <see cref="IronFlag.Vehicles.VehicleTuning.For"/> and
    /// <see cref="IronFlag.Combat.WeaponTuning.For"/>: these six rows are balanced by being
    /// read against each other and against the four guns, which is something a diff can show
    /// and six separate assets cannot.
    /// </para>
    /// <para>
    /// Hit points are in the same unit as a vehicle's, so the roster table is the scale to
    /// read this one against: the tank's shell does 34 and its full load is worth 680, so a
    /// building at 220 is two thirds of a jeep's entire load or seven cannon shells - real
    /// cover, and a decision about ammunition rather than a wall that evaporates.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class StructureTuning
    {
        /// <summary>Damage the structure absorbs before it is rubble.</summary>
        [Tooltip("Damage the structure absorbs before it is rubble, in hit points.")]
        public float HitPoints = 100.0f;

        /// <summary>Fraction of the pool at which the damaged mesh comes in.</summary>
        /// <remarks>
        /// Half is the honest default: the model changes exactly when the structure has
        /// taken half of what it can, so a player reading the map can tell how much more it
        /// wants without a health bar over every wall. A structure with no damaged mesh
        /// ignores this and goes straight to rubble.
        /// </remarks>
        [Range(0.0f, 1.0f)]
        [Tooltip("Fraction of the pool remaining at which the damaged mesh comes in.")]
        public float DamagedAt = 0.5f;

        /// <summary>Radius of the debris burst thrown on each transition, in metres.</summary>
        [Tooltip("Radius of the debris burst thrown on each transition, in metres.")]
        public float DebrisRadius = 3.0f;

        /// <summary>
        /// Returns the numbers for one kind of structure.
        /// </summary>
        /// <param name="kind">Structure to look up.</param>
        /// <returns>A fresh copy, so callers can stamp and edit it.</returns>
        /// <example>
        /// <code>
        /// StructureTuning tuning = StructureTuning.For(StructureKind.BuildingA);
        /// destructible.Configure(StructureKind.BuildingA, tuning, roots, debris);
        /// </code>
        /// </example>
        public static StructureTuning For(StructureKind kind)
        {
            switch (kind)
            {
                // Forty, which is one grenade and a half, or a two-second burst of chaingun.
                // A tree is the one piece of cover in the game that anybody can remove in
                // passing, which is what makes driving into a copse a temporary idea.
                case StructureKind.Tree:
                    return new StructureTuning
                    {
                        HitPoints = 40.0f,
                        DamagedAt = 0.5f,
                        DebrisRadius = 1.6f,
                    };

                // Seven cannon shells, or ten grenades - most of a jeep's load. Buildings
                // are the map's line-of-sight furniture, and one that came down to a single
                // burst would make the sightlines change faster than either player could
                // plan around.
                case StructureKind.BuildingA:
                    return new StructureTuning
                    {
                        HitPoints = 220.0f,
                        DamagedAt = 0.5f,
                        DebrisRadius = 4.0f,
                    };

                case StructureKind.BuildingB:
                    return new StructureTuning
                    {
                        HitPoints = 260.0f,
                        DamagedAt = 0.5f,
                        DebrisRadius = 4.5f,
                    };

                // The toughest thing on the map, and the only one that is a route rather
                // than cover: dropping it is meant to be a decision somebody commits a
                // sortie to, because it takes a crossing away from both sides at once.
                // It has no damaged mesh - a bridge is either crossable or it is not.
                case StructureKind.Bridge:
                    return new StructureTuning
                    {
                        HitPoints = 320.0f,
                        DamagedAt = 0.0f,
                        DebrisRadius = 5.0f,
                    };

                // Softer than a building, because a depot is a target worth going out of
                // your way for and the reward has to be reachable in one sortie. Both hold
                // the same, so neither side's fuel is safer than their ammunition.
                case StructureKind.DepotFuel:
                case StructureKind.DepotAmmo:
                    return new StructureTuning
                    {
                        HitPoints = 130.0f,
                        DamagedAt = 0.5f,
                        DebrisRadius = 4.0f,
                    };

                // Three hundred and forty, a shade above the bridge, which makes the
                // tower the toughest thing on the map. It is priced as the cost of asking
                // a question: five cannon shells crack one open and show whether the flag
                // is on it, so checking both of a side's towers is half a tank's load and
                // one sortie always answers. A jeep can do it alone with eight grenades,
                // but that is eight seconds parked fourteen metres from the tower in the
                // thinnest armour in the game - possible, and a gamble rather than a plan.
                // That gap is the design document's first pillar made literal: everything
                // else exists to clear the jeep's path.
                case StructureKind.FlagTower:
                    return new StructureTuning
                    {
                        HitPoints = 340.0f,
                        DamagedAt = 0.5f,
                        DebrisRadius = 4.5f,
                    };

                default:
                    return new StructureTuning();
            }
        }

        /// <summary>
        /// The destructibles a level file may scatter as scenery, in asset-spec order.
        /// </summary>
        /// <returns>
        /// Every <see cref="StructureKind"/> except <see cref="StructureKind.None"/> and
        /// <see cref="StructureKind.FlagTower"/>. The tower is a destructible with the same
        /// numbers as everything here, but a level places it as an objective - it needs a
        /// side and a real-or-decoy flag - so it is not something a structure list may hold.
        /// </returns>
        public static StructureKind[] Roster()
            => new[]
            {
                StructureKind.Tree,
                StructureKind.BuildingA,
                StructureKind.BuildingB,
                StructureKind.Bridge,
                StructureKind.DepotFuel,
                StructureKind.DepotAmmo,
            };

        /// <summary>
        /// Returns the state a structure is in at a given share of its pool.
        /// </summary>
        /// <param name="fraction">Hit points remaining as a fraction of full, in 0..1.</param>
        /// <param name="hasDamagedMesh">Whether the structure has a middle state to show.</param>
        /// <returns>The state that share of the pool means.</returns>
        /// <remarks>
        /// Static and side-effect free, like the camera placement and the explosion's shape,
        /// so what a hit does to a wall can be checked without shooting one.
        /// </remarks>
        public DestructionState StateAt(float fraction, bool hasDamagedMesh)
        {
            if (fraction <= 0.0f)
            {
                return DestructionState.Destroyed;
            }

            return hasDamagedMesh && fraction <= DamagedAt
                ? DestructionState.Damaged
                : DestructionState.Intact;
        }
    }
}
