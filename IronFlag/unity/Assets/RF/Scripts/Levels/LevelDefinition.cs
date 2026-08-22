using System;
using System.Collections.Generic;
using UnityEngine;
using IronFlag.Core;
using IronFlag.Destruction;

namespace IronFlag.Levels
{
    /// <summary>
    /// A map, as a file: where the land is, where the water is, and everything standing on
    /// either.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This is the whole of a level.</strong> Nothing in code knows where anything
    /// on the map is any more - not the bunkers, not the towers, not the depots, not the
    /// coastline. <see cref="LevelBuilder"/> turns one of these into a scene and knows no
    /// coordinates of its own, which is the test of whether the format is really the map or
    /// merely a description of it.
    /// </para>
    /// <para>
    /// What it deliberately does <em>not</em> carry is balance. A level says a building
    /// stands here; how much a building takes is <see cref="StructureTuning.For"/>, and how
    /// far you can see a flag from is <see cref="IronFlag.Objective.FlagRules"/>. Two levels
    /// that disagreed about either would be two games wearing one name.
    /// </para>
    /// <para>
    /// It is read and written by <see cref="LevelFile"/> as JSON, through Unity's
    /// <see cref="JsonUtility"/>, which is why every field is a public field of a
    /// <see cref="SerializableAttribute"/> class and why enums are carried as names - see
    /// <see cref="LevelNames"/>. The shape is deliberately flat and dull: it has to survive
    /// being written by hand today and by an in-game editor later.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// LevelDefinition level = LevelFile.Read(LevelLibrary.PathFor("iron-channel"));
    /// LevelBuilder.Build(level, catalog, root.transform);
    /// </code>
    /// </example>
    [Serializable]
    public sealed class LevelDefinition
    {
        /// <summary>The format version this code writes and understands.</summary>
        /// <remarks>
        /// Bumped when a change would make an older file mean something different. A file
        /// from the future is refused rather than half-read, because a level that loads with
        /// its water missing is worse than one that does not load.
        /// </remarks>
        public const int Schema = 1;

        /// <summary>Format version of the file this came from.</summary>
        [Tooltip("Format version of the level file.")]
        public int SchemaVersion = Schema;

        /// <summary>What the map is called.</summary>
        [Tooltip("What the map is called.")]
        public string Name = string.Empty;

        /// <summary>What the map is trying to be, in a sentence or two.</summary>
        /// <remarks>
        /// The only place a level file can explain itself: JSON has no comments, and the
        /// reason a channel is thirteen metres wide at the bridgeheads is worth writing
        /// down where the person moving it will read it.
        /// </remarks>
        [Tooltip("What the map is trying to be. The level file has no other room for comments.")]
        public string Description = string.Empty;

        /// <summary>How far the world goes, and where the sea sits in it.</summary>
        public LevelBounds Bounds = new LevelBounds();

        /// <summary>Every rectangle of dry ground. Everything else is sea.</summary>
        public LevelLand[] Land = Array.Empty<LevelLand>();

        /// <summary>One bunker per side.</summary>
        public LevelBunker[] Bunkers = Array.Empty<LevelBunker>();

        /// <summary>The flag towers, real and decoy.</summary>
        public LevelTower[] Towers = Array.Empty<LevelTower>();

        /// <summary>Everything that can be shot down.</summary>
        public LevelStructure[] Structures = Array.Empty<LevelStructure>();

        /// <summary>
        /// Returns one side's bunker.
        /// </summary>
        /// <param name="side">Side to look up.</param>
        /// <returns>The bunker, or <c>null</c> when the level does not give that side one.</returns>
        public LevelBunker BunkerFor(Team side)
        {
            foreach (LevelBunker bunker in Bunkers)
            {
                if (bunker != null && bunker.Side == side)
                {
                    return bunker;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns where one side's bunker stands.
        /// </summary>
        /// <param name="side">Side to look up.</param>
        /// <returns>The bunker position, or the origin when that side has no bunker.</returns>
        /// <remarks>
        /// What the scene builder parks a roster against, so it survives a level that is
        /// still being written: four vehicles in the middle of the map is a visible mistake,
        /// and an exception during a scene build is a blank scene.
        /// </remarks>
        public Vector3 BunkerPosition(Team side)
        {
            LevelBunker bunker = BunkerFor(side);
            return bunker == null ? Vector3.zero : bunker.Position;
        }

        /// <summary>
        /// Returns the towers belonging to one side.
        /// </summary>
        /// <param name="side">Side to look up.</param>
        /// <returns>Its towers, real and decoy, in file order.</returns>
        public List<LevelTower> TowersFor(Team side)
        {
            var found = new List<LevelTower>();
            foreach (LevelTower tower in Towers)
            {
                if (tower != null && tower.Side == side)
                {
                    found.Add(tower);
                }
            }

            return found;
        }

        /// <summary>
        /// Reports whether a point on the map stands on dry land.
        /// </summary>
        /// <param name="at">Point to test; its height is ignored.</param>
        /// <param name="margin">Metres of clearance demanded from every coastline.</param>
        /// <returns><c>true</c> when some land rectangle covers it with room to spare.</returns>
        /// <remarks>
        /// The margin is not optional in practice. Land rectangles overlap on purpose - a
        /// headland is a second rectangle laid over the shore - so a point can be well
        /// inland and still sit exactly on the edge of the rectangle it happens to be tested
        /// against. Asking each rectangle for clearance and taking the best answer is what
        /// makes an overlap read as one landmass rather than as a seam.
        /// </remarks>
        public bool IsOnLand(Vector3 at, float margin)
        {
            foreach (LevelLand piece in Land)
            {
                if (piece != null && piece.IsDrawn && piece.Contains(at, margin))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Counts the structures of one kind.
        /// </summary>
        /// <param name="kind">Kind to count.</param>
        /// <returns>How many of them the level places.</returns>
        public int CountOf(StructureKind kind)
        {
            int found = 0;
            foreach (LevelStructure structure in Structures)
            {
                if (structure != null && structure.Structure == kind)
                {
                    found++;
                }
            }

            return found;
        }
    }
}
