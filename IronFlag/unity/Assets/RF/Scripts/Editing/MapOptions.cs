using System;
using UnityEngine;

namespace IronFlag.Editing
{
    /// <summary>
    /// What to generate: the handful of choices a person makes before pressing the button.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately small. Everything else about a generated map - which coastlines wander
    /// where, how many trees, where the causeway crosses - comes out of
    /// <see cref="Seed"/> through <see cref="Dice"/>, so the dialogue asks four questions
    /// rather than forty and the answer to "I want a different one" is to press it again.
    /// </para>
    /// <para>
    /// Every field has a meaning for its empty value, and <see cref="Settled"/> is where they
    /// are filled in: an unset enum is "roll one" or "the usual", and a seed of zero is a
    /// seed like any other rather than "no seed". That is what lets a caller write
    /// <c>new MapOptions { Seed = 12 }</c> and get a sensible map.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// LevelDefinition map = LevelGenerator.Generate(new MapOptions
    /// {
    ///     Seed = 1995,
    ///     Difficulty = MapDifficulty.Hard,
    ///     Symmetry = MapSymmetry.Mirrored,
    /// });
    /// </code>
    /// </example>
    [Serializable]
    public sealed class MapOptions
    {
        /// <summary>The most sides a generated map can have.</summary>
        public const int MostPlayers = 2;

        /// <summary>Which map out of the four billion of them.</summary>
        /// <remarks>
        /// The whole of what makes one generated map different from another with the same
        /// options, and it is written into
        /// <see cref="IronFlag.Levels.LevelDefinition.Seed"/> so the map can be drawn again
        /// from it. See <see cref="Dice"/> for why this is a seed rather than a call to
        /// <see cref="UnityEngine.Random"/>.
        /// </remarks>
        [Tooltip("Which map. The same seed and the same options always draw the same map.")]
        public int Seed;

        /// <summary>How big and how heavily defended, or unset for the middle setting.</summary>
        [Tooltip("How big and how heavily defended the map is.")]
        public MapDifficulty Difficulty = MapDifficulty.Medium;

        /// <summary>What shape of ground, or unset to roll one.</summary>
        [Tooltip("What shape of ground the map is built on. Unset rolls one.")]
        public MapLayout Layout = MapLayout.None;

        /// <summary>Whether the halves match, or unset for matching.</summary>
        [Tooltip("Whether the two halves of the map are the same shape.")]
        public MapSymmetry Symmetry = MapSymmetry.Mirrored;

        /// <summary>How many sides get a bunker: one or two.</summary>
        /// <remarks>
        /// <para>
        /// One is the solo shape: a green bunker, no green towers and no green flag, and a
        /// brown side that is a field of flag towers guarded by emplacements rather than an
        /// opponent. Playing one seats a single player - see
        /// <see cref="IronFlag.Players.SessionSeating"/> - and it is judged by its own rules
        /// rather than a match's, see <see cref="IronFlag.Levels.LevelDefinition.IsSolo"/>.
        /// </para>
        /// <para>
        /// An <see cref="int"/> rather than an enum because it is a count, it is spelled 1
        /// and 2 on the buttons, and <see cref="Settled"/> clamps it.
        /// </para>
        /// </remarks>
        [Tooltip("How many sides get a bunker: one for a solo map, two for a match.")]
        [Range(1, MostPlayers)]
        public int Players = MostPlayers;

        /// <summary>What to call the map, or empty to name it after what was rolled.</summary>
        [Tooltip("What to call the map. Empty names it after what was rolled.")]
        public string Name = string.Empty;

        /// <summary>Whether this asks for a map with one side on it.</summary>
        public bool IsSolo => Players <= 1;

        /// <summary>
        /// Returns these options with every empty value filled in.
        /// </summary>
        /// <returns>
        /// A copy that a generator can read without asking what an unset field means.
        /// </returns>
        /// <remarks>
        /// A copy rather than a tidy-up in place, because the caller's options are very
        /// likely the dialogue's own and are still on screen: filling in a rolled layout
        /// there would turn "ANY" into "LAGOON" under the player's hand the first time they
        /// pressed the button.
        /// </remarks>
        public MapOptions Settled()
            => new MapOptions
            {
                Seed = Seed,
                Difficulty = Difficulty == MapDifficulty.None ? MapDifficulty.Medium : Difficulty,
                Layout = Layout,
                Symmetry = Symmetry == MapSymmetry.None ? MapSymmetry.Mirrored : Symmetry,
                Players = Mathf.Clamp(Players, 1, MostPlayers),
                Name = Name == null ? string.Empty : Name.Trim(),
            };
    }
}
