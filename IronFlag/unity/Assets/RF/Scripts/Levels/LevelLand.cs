using System;
using UnityEngine;

namespace IronFlag.Levels
{
    /// <summary>
    /// One rectangle of dry ground. Everything the land rectangles do not cover is sea.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The map is described by what is <em>land</em> rather than by what is water, because
    /// the design document's map is an island: the water is the default and the land is the
    /// thing somebody drew. It also means a level cannot accidentally describe a hole in the
    /// sea, and that the shape of a coastline costs exactly as many rectangles as it is
    /// worth.
    /// </para>
    /// <para>
    /// Rectangles are axis-aligned and stated as their edges rather than as a centre and a
    /// size. Read out loud, "the green shore runs from z -92 to z -13" is a sentence about
    /// the map; a centre and a half-size is arithmetic about it. Overlap is fine and
    /// expected - a headland reaching into the channel is a second rectangle over the first.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class LevelLand
    {
        /// <summary>What this piece of land is called, for the hierarchy and the editor.</summary>
        [Tooltip("What this piece of land is called.")]
        public string Name = "Land";

        /// <summary>West edge, in metres.</summary>
        [Tooltip("West edge, in metres.")]
        public float MinX;

        /// <summary>East edge, in metres.</summary>
        [Tooltip("East edge, in metres.")]
        public float MaxX;

        /// <summary>South edge, in metres.</summary>
        [Tooltip("South edge, in metres.")]
        public float MinZ;

        /// <summary>North edge, in metres.</summary>
        [Tooltip("North edge, in metres.")]
        public float MaxZ;

        /// <summary>Width of the rectangle, in metres.</summary>
        public float Width => MaxX - MinX;

        /// <summary>Depth of the rectangle, in metres.</summary>
        public float Depth => MaxZ - MinZ;

        /// <summary>Middle of the rectangle, on the ground plane.</summary>
        public Vector3 Centre => new Vector3((MinX + MaxX) * 0.5f, 0.0f, (MinZ + MaxZ) * 0.5f);

        /// <summary>Whether the rectangle has any area at all.</summary>
        public bool IsDrawn => Width > 0.0f && Depth > 0.0f;

        /// <summary>
        /// Reports whether a point on the map stands on this piece of land.
        /// </summary>
        /// <param name="at">Point to test; its height is ignored.</param>
        /// <returns><c>true</c> when the point is inside the rectangle.</returns>
        public bool Contains(Vector3 at)
            => at.x >= MinX && at.x <= MaxX && at.z >= MinZ && at.z <= MaxZ;

        /// <summary>
        /// Reports whether a point is on this land with room to spare on every side.
        /// </summary>
        /// <param name="at">Point to test; its height is ignored.</param>
        /// <param name="margin">Metres of clearance demanded from every edge.</param>
        /// <returns><c>true</c> when the point is that far inside the rectangle.</returns>
        /// <remarks>
        /// What a placement check wants. A bunker whose centre is exactly on a coastline is
        /// technically on land and is half in the sea.
        /// </remarks>
        public bool Contains(Vector3 at, float margin)
            => at.x >= MinX + margin && at.x <= MaxX - margin
            && at.z >= MinZ + margin && at.z <= MaxZ - margin;
    }
}
