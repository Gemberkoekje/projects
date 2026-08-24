using System;
using UnityEngine;

namespace IronFlag.Levels
{
    /// <summary>
    /// One row of <see cref="LevelCatalog"/>: a surface and the material it is painted with.
    /// </summary>
    /// <remarks>
    /// A list of pairs rather than a field per surface, for the same reason as
    /// <see cref="LevelStructurePrefab"/>: <see cref="SurfaceKind"/> is a list that grows,
    /// and a catalog with a field per member is a catalog somebody has to remember to widen.
    /// A missing row is a warning naming the surface; a missing field would be a null nobody
    /// notices until half the map is untextured white.
    /// </remarks>
    [Serializable]
    public sealed class LevelSurfaceMaterial
    {
        /// <summary>Which surface this row is for.</summary>
        [Tooltip("Which surface this row is for.")]
        public SurfaceKind Kind = SurfaceKind.None;

        /// <summary>The material that surface is painted with.</summary>
        [Tooltip("The material that surface is painted with.")]
        public Material Material;

        /// <summary>The material the bank below that surface is painted with.</summary>
        /// <remarks>
        /// The same colour taken down a step - see <see cref="SurfaceTuning.BankShade"/> -
        /// and it lives beside the surface it belongs to rather than in a list of its own,
        /// because the two are always generated together and a bank without its surface is
        /// not a thing a map can have.
        /// </remarks>
        [Tooltip("The material the bank below that surface is painted with.")]
        public Material Bank;
    }
}
