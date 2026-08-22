using System;
using UnityEngine;
using IronFlag.Destruction;

namespace IronFlag.Levels
{
    /// <summary>
    /// One placed destructible: cover, a crossing, or a depot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything on the map that can be shot down is one of these, and what it <em>is</em>
    /// comes from <see cref="StructureKind"/> - so its hit points, its damaged mesh and the
    /// size of the mess it makes are all still read out of
    /// <see cref="StructureTuning.For"/> rather than out of the level file. A level places
    /// props; it does not rebalance them. Two levels that disagreed about how tough a
    /// building is would be two games.
    /// </para>
    /// <para>
    /// The supply fields are the exception, and they are placement rather than balance: a
    /// fuel drum is a fuel drum because of where it stands on this map, and the same model
    /// with both rates at zero is scenery.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class LevelStructure
    {
        /// <summary>What it is, by name - a member of <see cref="StructureKind"/>.</summary>
        [Tooltip("What it is: Tree, BuildingA, BuildingB, Bridge, DepotFuel or DepotAmmo.")]
        public string Kind = nameof(StructureKind.Tree);

        /// <summary>What to call it in the hierarchy. Optional.</summary>
        /// <remarks>
        /// Worth filling in for the handful of props a level's design turns on - the two
        /// bridges, the depots - and worth leaving empty for the fourteenth tree.
        /// </remarks>
        [Tooltip("What to call it in the hierarchy. Optional.")]
        public string Name = string.Empty;

        /// <summary>Where it stands.</summary>
        /// <remarks>
        /// Almost always on the ground plane. The bridge is the exception: it is modelled
        /// standing on a riverbed with its deck a little over a metre up, so it is placed
        /// sunk by that much and its deck comes out flush with the banks.
        /// </remarks>
        [Tooltip("Where it stands. Usually on the ground plane; a bridge is sunk to its deck.")]
        public Vector3 Position = Vector3.zero;

        /// <summary>Which way it faces, in degrees.</summary>
        [Tooltip("Which way it faces, in degrees.")]
        public float YawDegrees;

        /// <summary>Fraction of a full fuel pool it hands out per second, or zero.</summary>
        [Tooltip("Fraction of a full fuel pool it hands out per second. Zero for scenery.")]
        public float FuelRate;

        /// <summary>Fraction of a full ammunition pool it hands out per second, or zero.</summary>
        [Tooltip("Fraction of a full ammunition pool it hands out per second. Zero for scenery.")]
        public float AmmoRate;

        /// <summary>Metres from it a vehicle may be and still be resupplied.</summary>
        [Tooltip("Metres from it a vehicle may be and still be resupplied.")]
        public float SupplyRadius = 7.0f;

        /// <summary>What it is.</summary>
        public StructureKind Structure => LevelNames.ToStructure(Kind);

        /// <summary>Whether it hands anything out.</summary>
        public bool Supplies => FuelRate > 0.0f || AmmoRate > 0.0f;
    }
}
