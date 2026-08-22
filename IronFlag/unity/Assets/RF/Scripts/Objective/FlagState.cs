using System;

namespace IronFlag.Objective
{
    /// <summary>
    /// Where one side's flag is in the round trip between its tower and the enemy bunker.
    /// </summary>
    /// <remarks>
    /// The order is the order the design document's core loop walks through them: a flag
    /// waits on its tower, a jeep grabs it, it either goes down with that jeep or reaches
    /// the bunker. Only <see cref="Captured"/> is an end state - a dropped flag goes back to
    /// its tower on its own, so <see cref="Dropped"/> is a countdown rather than a place.
    /// </remarks>
    [Serializable]
    public enum FlagState
    {
        /// <summary>Not placed on the map yet, which is what an unconfigured flag reads.</summary>
        None = 0,

        /// <summary>On its tower, waiting to be found.</summary>
        AtTower = 1,

        /// <summary>Riding the mast of an enemy jeep.</summary>
        Carried = 2,

        /// <summary>Lying where its carrier was killed, counting down to going home.</summary>
        Dropped = 3,

        /// <summary>Delivered to the enemy bunker. The match is over.</summary>
        Captured = 4,
    }
}
