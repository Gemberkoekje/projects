using System;

namespace IronFlag.Editing
{
    /// <summary>
    /// One of the four edges a rectangle of land is stated as.
    /// </summary>
    /// <remarks>
    /// A level file describes land by its edges rather than by a centre and a size, because
    /// "the green shore runs from z -92 to z -13" is a sentence about the map and a centre
    /// with a half-size is arithmetic about it. The inspector shows the same four numbers,
    /// and this is how a typed one knows which of them it is.
    /// </remarks>
    [Serializable]
    public enum LandEdge
    {
        /// <summary>No edge.</summary>
        None = 0,

        /// <summary>The low-x edge.</summary>
        West = 1,

        /// <summary>The high-x edge.</summary>
        East = 2,

        /// <summary>The low-z edge.</summary>
        South = 3,

        /// <summary>The high-z edge.</summary>
        North = 4,
    }
}
