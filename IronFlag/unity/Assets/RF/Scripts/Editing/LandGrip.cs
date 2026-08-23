using System;

namespace IronFlag.Editing
{
    /// <summary>
    /// Which part of a rectangle of land a drag has hold of.
    /// </summary>
    /// <remarks>
    /// Corners rather than edges, and that is a deliberate reduction: a corner changes one x
    /// edge and one z edge at once, so four handles reach all four edges, and a rectangle
    /// small enough to be fiddly has four things to grab rather than eight overlapping ones.
    /// The exact numbers are always available in the inspector, which is where a coastline
    /// that has to line up with another one gets set.
    /// </remarks>
    [Serializable]
    public enum LandGrip
    {
        /// <summary>Nothing: the point is not on this rectangle at all.</summary>
        None = 0,

        /// <summary>The middle of it, which moves the whole rectangle.</summary>
        Body = 1,

        /// <summary>The low-x, low-z corner.</summary>
        SouthWest = 2,

        /// <summary>The high-x, low-z corner.</summary>
        SouthEast = 3,

        /// <summary>The low-x, high-z corner.</summary>
        NorthWest = 4,

        /// <summary>The high-x, high-z corner.</summary>
        NorthEast = 5,
    }
}
