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
    /// <see cref="Land"/>. On a real map that ordering is not enough on its own - the ground
    /// is under everything, so the loser of it is unreachable everywhere the winner is - which
    /// is why an <see cref="EditorMode"/> decides which of these are candidates at all before
    /// the order gets a say - see <see
    /// cref="LevelPick.At(IronFlag.Levels.LevelDefinition, UnityEngine.Vector3, EditorMode)"/>.
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
