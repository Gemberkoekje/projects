using System;
using UnityEngine;
using IronFlag.Destruction;

namespace IronFlag.Levels
{
    /// <summary>
    /// One row of <see cref="LevelCatalog"/>: a kind of destructible and the prefab it is
    /// built from.
    /// </summary>
    /// <remarks>
    /// A list of pairs rather than a field per kind, because <see cref="StructureKind"/> is
    /// a list that grows and a catalog with a field per member is a catalog somebody has to
    /// remember to widen. A missing row is a warning naming the kind; a missing field would
    /// be a null nobody notices until the map is short a building.
    /// </remarks>
    [Serializable]
    public sealed class LevelStructurePrefab
    {
        /// <summary>Which destructible this row is for.</summary>
        [Tooltip("Which destructible this row is for.")]
        public StructureKind Kind = StructureKind.None;

        /// <summary>The prefab that kind is built from.</summary>
        [Tooltip("The prefab that kind is built from.")]
        public GameObject Prefab;
    }
}
