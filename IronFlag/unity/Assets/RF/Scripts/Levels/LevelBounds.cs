using System;
using UnityEngine;

namespace IronFlag.Levels
{
    /// <summary>
    /// How far the world goes, and where the sea sits in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three numbers, and every one of them is a rule the rest of the game already
    /// assumes. Land is always at <c>y = 0</c>: <see cref="IronFlag.Combat.CombatPlane"/>
    /// resolves every round on one plane, the camera looks down at that plane from a fixed
    /// height, and a map with hills would quietly break both. So elevation in this game is
    /// not terrain - it is the difference between the bank and the water, and that is what
    /// these numbers describe.
    /// </para>
    /// <para>
    /// Terrain variety, in the design document's sense, is therefore about <em>route</em>:
    /// where the water is, where you may cross it, and what stands in the way. Not about
    /// height.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class LevelBounds
    {
        /// <summary>Half-width of the sea, in metres: the world is this square.</summary>
        [Tooltip("Half-width of the sea, in metres. The world is a square this far out.")]
        public float HalfExtent = 120.0f;

        /// <summary>How far the water surface sits below the land, in metres.</summary>
        /// <remarks>
        /// The whole of the map's vertical relief. Deep enough that a shoreline reads as a
        /// bank from above and that a vehicle which drives off one visibly drops; shallow
        /// enough that the fall is a wrong turn rather than a plummet. Anything below
        /// <see cref="DrownDepth"/> is in the water.
        /// </remarks>
        [Tooltip("Metres the water surface sits below the land.")]
        public float WaterDepth = 0.7f;

        /// <summary>How thick the sea slab is drawn, in metres.</summary>
        /// <remarks>
        /// Purely so the sea has a solid top to land on and a visible edge at the horizon.
        /// Nothing is ever underneath it.
        /// </remarks>
        [Tooltip("Thickness of the sea slab, in metres.")]
        public float SeaThickness = 3.0f;

        /// <summary>Height of the water surface, in metres. Always at or below zero.</summary>
        public float WaterLevel => -Mathf.Abs(WaterDepth);

        /// <summary>
        /// Height below which a vehicle counts as being in the water, in metres.
        /// </summary>
        /// <remarks>
        /// Halfway down the bank rather than at the waterline. A vehicle balanced exactly on
        /// the lip of a bank jitters by a few centimetres against the collider it is resting
        /// on, and a drowning rule measured at the waterline would fire on that jitter.
        /// </remarks>
        public float DrownDepth => WaterLevel * 0.5f;

        /// <summary>How thick a land slab is drawn, in metres.</summary>
        /// <remarks>
        /// Enough to stand proud of the sea and still have a base buried in it, so a
        /// shoreline is a solid wall rather than a paper edge a vehicle can see under.
        /// </remarks>
        public float LandThickness => Mathf.Abs(WaterDepth) + 0.5f;
    }
}
