using System;

namespace IronFlag.Core
{
    /// <summary>
    /// The two sides of a match.
    /// </summary>
    /// <remarks>
    /// Team identity is carried by one material on one child renderer per model - the
    /// <c>TeamTrim</c> object the Blender pipeline exports - so adding a third side is a
    /// material and an enum entry, never an art change.
    /// </remarks>
    [Serializable]
    public enum Team
    {
        /// <summary>No side: map furniture, or a vehicle that has not been assigned yet.</summary>
        None = 0,

        /// <summary>The green side.</summary>
        Green = 1,

        /// <summary>The brown side.</summary>
        Brown = 2,
    }
}
