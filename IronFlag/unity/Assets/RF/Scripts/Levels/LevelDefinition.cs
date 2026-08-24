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
        /// <para>
        /// Bumped when a change would make an older file mean something different. A file
        /// from the future is refused rather than half-read, because a level that loads with
        /// its water missing is worse than one that does not load.
        /// </para>
        /// <para>
        /// Version 2 is coastlines that move. Surfaces alone did not earn a bump - a build
        /// that had never heard of them lost two colours and still built the same map - but
        /// a map authored against a displaced coast and cut to a shape does not survive
        /// being read by a build that draws every rectangle as a box: the coastline is in
        /// the wrong place, an ellipse comes out square, and a bridgehead measured against
        /// one of those could stand in the sea. That is exactly the sentence
        /// <see cref="LevelFile"/> refuses a file with, and the whole reason for having a
        /// version.
        /// </para>
        /// </remarks>
        public const int Schema = 2;

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

        /// <summary>Which coastline this map gets out of the infinity of them.</summary>
        /// <remarks>
        /// <para>
        /// A level file draws its land as boxes and ellipses; where the water's edge
        /// actually runs is those shapes with a wobble added, and this is the number that
        /// picks the wobble. Two maps with two seeds get two coastlines. One map keeps its
        /// coastline forever, which is not a nicety: the map baked into a scene and the map
        /// the loader builds from the file are compared prop for prop, and anything drawn
        /// from <see cref="UnityEngine.Random"/> would make those two different maps on
        /// alternate Tuesdays. See <see cref="SurfaceNoise"/>.
        /// </para>
        /// <para>
        /// Zero is a seed like any other rather than "no wobble". A map that wants a coast
        /// with no wobble in it says so by drawing it out of surfaces whose
        /// <see cref="SurfaceTuning.NaturalEdge"/> is false, which is a statement about what
        /// the ground is rather than a switch.
        /// </para>
        /// </remarks>
        [Tooltip("Which coastline this map gets. Two seeds are two coastlines; one seed is one, forever.")]
        public int Seed;

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

        /// <summary>The map rasterised: what is actually at each square metre of it.</summary>
        [NonSerialized]
        private SurfaceField field;

        /// <summary>
        /// The map as it is really made, rather than as it was drawn.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A level file lists the rectangles somebody drew. This is what those come out as
        /// once the derived parts of the map - the beach, the shelf - have been worked out,
        /// and it is what everything that asks a question <em>about</em> the map should ask.
        /// See <see cref="SurfaceField"/> for why there is one of these rather than three
        /// different answers.
        /// </para>
        /// <para>
        /// Built when it is first wanted and rebuilt whenever the land has changed under it,
        /// which is checked rather than announced: the level editor moves a coastline by
        /// writing to <see cref="Land"/> and has no way to tell anybody it has, and a field
        /// that went stale would put a bunker on a shore that is no longer there. Not part
        /// of the file - it is derived from it, and writing it out would be writing the same
        /// map twice.
        /// </para>
        /// </remarks>
        public SurfaceField Field
        {
            get
            {
                if (field == null || !field.Describes(this))
                {
                    field = SurfaceField.Build(this);
                }

                return field;
            }
        }

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
        /// <returns><c>true</c> when the point is that far inside the coastline.</returns>
        /// <remarks>
        /// <para>
        /// Measured against <see cref="Field"/> rather than against the rectangles, and the
        /// difference is not cosmetic. Rectangles meet and overlap on purpose - a headland
        /// is a second rectangle laid over the shore, a bridgehead is a strip of road built
        /// onto the end of one - and asking each of them separately means a point a metre
        /// inside a seam is within a margin of an edge of <em>both</em> and is refused by
        /// both, though it is thirty metres from any water. The realised coast has no seams
        /// in it, so there is nothing there to refuse.
        /// </para>
        /// <para>
        /// The margin is not optional in practice. Something whose centre is exactly on a
        /// coastline is technically on land and is half in the sea.
        /// </para>
        /// </remarks>
        public bool IsOnLand(Vector3 at, float margin) => Field.IsLand(at, margin);

        /// <summary>
        /// Measures the box every piece of land fits inside.
        /// </summary>
        /// <returns>
        /// The land's bounds on the ground plane, or the whole world when a level has no
        /// land at all - so a caller framing a camera on it always has something to frame.
        /// </returns>
        /// <remarks>
        /// What both views of a map are framed on: the overhead still and the editor's own
        /// camera. Framed on the land rather than on <see cref="Bounds"/>, because a level
        /// with a small island in a big sea should still be a picture of the island.
        /// </remarks>
        public Bounds LandBounds()
        {
            var box = new Bounds(Vector3.zero, Vector3.zero);
            bool started = false;

            foreach (LevelLand piece in Land)
            {
                if (piece == null || !piece.IsDrawn)
                {
                    continue;
                }

                var corner = new Bounds(piece.Centre, new Vector3(piece.Width, 1.0f, piece.Depth));

                if (started)
                {
                    box.Encapsulate(corner);
                }
                else
                {
                    box = corner;
                    started = true;
                }
            }

            if (!started)
            {
                float extent = Bounds == null ? 100.0f : Mathf.Abs(Bounds.HalfExtent);
                box = new Bounds(Vector3.zero, new Vector3(extent * 2.0f, 1.0f, extent * 2.0f));
            }

            return box;
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
