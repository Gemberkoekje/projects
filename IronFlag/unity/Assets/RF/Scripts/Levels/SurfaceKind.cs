using System;

namespace IronFlag.Levels
{
    /// <summary>
    /// What a piece of the map is made of: the ground a vehicle drives on, and the two
    /// waters it does not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The map used to be one material and one colour, which is why <c>m7-map.png</c> reads
    /// as a harbour rather than as an island: the causeway - the single most important piece
    /// of ground on the map - was the same grey as the open country either side of it. A
    /// surface is what lets the map say that a road is a road.
    /// </para>
    /// <para>
    /// The roster is deliberately short and deliberately an enum. Adding a surface is one
    /// member here, one case in <see cref="SurfaceTuning.For"/> and one rebuild of the level
    /// catalog; everything downstream - the material, the name a level file may write, the
    /// palette an editor offers, the validation - falls out of those. See
    /// <see cref="SurfaceTuning"/> for why that is a code table rather than an asset.
    /// </para>
    /// <para>
    /// The two waters are members rather than a separate idea because the shelf that rims
    /// every coastline is a surface with a colour like any other, and because a vehicle
    /// asking what it is standing on wants one answer rather than two questions.
    /// </para>
    /// </remarks>
    [Serializable]
    public enum SurfaceKind
    {
        /// <summary>Not a surface, which is what an unrecognised name in a level file reads.</summary>
        None = 0,

        /// <summary>Open country: the default, and what most of a map is.</summary>
        Grass = 1,

        /// <summary>Beach and scuff, which every coastline is rimmed with.</summary>
        Sand = 2,

        /// <summary>Road, causeway and bridgehead - the surfaces somebody built.</summary>
        Asphalt = 3,

        /// <summary>The pale shelf a couple of vehicle lengths wide that hugs every coast.</summary>
        ShallowWater = 4,

        /// <summary>The open sea beyond the shelf.</summary>
        DeepWater = 5,
    }
}
