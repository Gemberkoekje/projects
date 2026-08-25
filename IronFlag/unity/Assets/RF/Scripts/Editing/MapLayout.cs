using System;

namespace IronFlag.Editing
{
    /// <summary>
    /// The shape of ground a generated map is built on: where the land is and, therefore,
    /// where the routes are.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This game's terrain variety is about <em>route</em> rather than height - land is
    /// always at <c>y = 0</c>, see <see cref="IronFlag.Levels.LevelBounds"/> - so a layout is
    /// the only lever a generator has over what a map plays like. Three of them, deliberately
    /// few: each is a different answer to "how do I get at the other side", and jittering one
    /// skeleton would have produced three hundred versions of the same answer.
    /// </para>
    /// <para>
    /// Every one of them owes the same debt to <see cref="IronFlag.Levels.LevelValidation"/>:
    /// the two bunkers have to be joined by land with no bridge in it. Each layout pays it
    /// differently, and each pays it <em>by construction</em> rather than by hoping - see
    /// <see cref="LevelGenerator"/>.
    /// </para>
    /// </remarks>
    [Serializable]
    public enum MapLayout
    {
        /// <summary>Not a layout: roll one. What an unset option reads.</summary>
        None = 0,

        /// <summary>
        /// One landmass, both bunkers on it. Open ground the whole way, so the fight is
        /// about cover rather than about crossings.
        /// </summary>
        Island = 1,

        /// <summary>
        /// Two shores facing each other across water, joined by causeways nobody can take
        /// away and bridges anybody can drop. The shipped map's shape.
        /// </summary>
        Channel = 2,

        /// <summary>
        /// A ring of land around open water, with a bunker at each end of it. Two ways
        /// round and no way through, so committing to a flank is the whole decision.
        /// </summary>
        Lagoon = 3,
    }
}
