using System;

namespace IronFlag.Editing
{
    /// <summary>
    /// Which of a level's four lists a selection points into.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="IronFlag.Levels.LevelDefinition"/> is four arrays of unrelated types, so
    /// "the thing the player has picked" cannot be one reference - it is a list and an index.
    /// This is the list half; <see cref="EditSelection"/> carries both.
    /// </para>
    /// <para>
    /// The order matters and is the order things are picked in: a prop standing on a shore
    /// should be selected before the shore under it, so the point-like targets come before
    /// <see cref="Land"/>. See <see cref="LevelPick.At"/>.
    /// </para>
    /// </remarks>
    [Serializable]
    public enum EditTarget
    {
        /// <summary>Nothing is selected.</summary>
        None = 0,

        /// <summary>One side's bunker.</summary>
        Bunker = 1,

        /// <summary>A flag tower, real or decoy.</summary>
        Tower = 2,

        /// <summary>A placed destructible: cover, a crossing, or a depot.</summary>
        Structure = 3,

        /// <summary>One rectangle of dry ground.</summary>
        Land = 4,
    }
}
