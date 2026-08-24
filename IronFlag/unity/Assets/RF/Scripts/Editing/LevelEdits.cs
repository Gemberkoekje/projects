using System;
using System.Collections.Generic;
using UnityEngine;
using IronFlag.Core;
using IronFlag.Destruction;
using IronFlag.Levels;

namespace IronFlag.Editing
{
    /// <summary>
    /// Every change the editor can make to a level, as plain functions on a
    /// <see cref="LevelDefinition"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No scene, no camera, no components: this is arithmetic on the level file, which means
    /// the whole of what an editor <em>does</em> can be tested without one. The parts that
    /// need Unity - hit-testing a mouse, drawing a panel, rebuilding the map - are everywhere
    /// else, and none of them decide anything.
    /// </para>
    /// <para>
    /// It also owns the handful of facts a person hand-editing JSON has to remember and the
    /// editor should not make them: that a bridge is placed sunk to its deck, that a depot
    /// with no rate is scenery, and that a tower belongs to a side. Every one of those is a
    /// mistake a level file cannot catch, which is exactly the kind of thing an editor exists
    /// to stop happening.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// int placed = LevelEdits.AddStructure(level, StructureKind.Bridge, at);
    /// LevelEdits.SetYaw(level, new EditSelection(EditTarget.Structure, placed), 90.0f);
    /// </code>
    /// </example>
    public static class LevelEdits
    {
        /// <summary>
        /// Metres a bridge is placed below the ground plane.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A fact about the model rather than about the map. <c>RF_Prop_Bridge</c> stands on
        /// its piers with the deck 1.2 m above its own origin, so a bridge placed at ground
        /// level puts a metre-high step at each end - something that looks like a perfectly
        /// good bridge and is a wall.
        /// </para>
        /// <para>
        /// Nothing in a level file can catch that, and nobody placing a bridge in an editor
        /// should have to know it, so every bridge this class places is placed sunk.
        /// </para>
        /// </remarks>
        public const float BridgeSink = -1.2f;

        /// <summary>Fraction of a pool a depot hands out per second when one is placed.</summary>
        /// <remarks>
        /// A depot placed with a rate of zero is a model of a depot: it looks exactly like the
        /// real thing on the map and refuels nobody. So placing one turns its own rate on,
        /// and turning it off again is a deliberate act in the inspector rather than the
        /// default nobody noticed.
        /// </remarks>
        public const float DepotRate = 0.12f;

        /// <summary>Metres a placed structure resupplies within, by default.</summary>
        public const float DepotRadius = 7.0f;

        /// <summary>Smallest rectangle of land the editor will draw, in metres.</summary>
        /// <remarks>
        /// Below this a drag is a click that moved, and a level full of quarter-metre slivers
        /// is one nobody can select anything on.
        /// </remarks>
        public const float MinimumLandSide = 2.0f;

        /// <summary>How far a new map's coastline is from the origin, in metres.</summary>
        private const float StarterShore = 60.0f;

        /// <summary>How far a new map's bunkers stand from the origin, in metres.</summary>
        private const float StarterBunker = 44.0f;

        /// <summary>How far a new map's towers stand from the origin, in metres.</summary>
        private const float StarterTower = 30.0f;

        /// <summary>How far apart a new map's two towers a side stand, in metres.</summary>
        private const float StarterTowerSpread = 18.0f;

        /// <summary>How far out a new map's depots stand, in metres.</summary>
        private const float StarterDepot = 34.0f;

        /// <summary>
        /// Makes a map that can be played the moment it is made.
        /// </summary>
        /// <param name="name">What to call it.</param>
        /// <returns>A new level: one island, two bunkers, four towers and two depots.</returns>
        /// <remarks>
        /// <para>
        /// Deliberately not empty. An empty level is a square of sea that fails eleven of
        /// <see cref="LevelValidation"/>'s rules at once, and an editor that opens on eleven
        /// red lines has taught the player nothing except that they have done something
        /// wrong before touching anything. This opens on a map that validates clean and is
        /// dull, which is a much better thing to start editing.
        /// </para>
        /// <para>
        /// Everything in it is placed in pairs rotated half a turn about the origin, which is
        /// the symmetry rule the shipped map is built on: neither side gets a shorter run.
        /// </para>
        /// </remarks>
        public static LevelDefinition Starter(string name)
        {
            var level = new LevelDefinition
            {
                SchemaVersion = LevelDefinition.Schema,
                Name = string.IsNullOrWhiteSpace(name) ? "New Map" : name.Trim(),
                Description = "A new map. Say here what it is trying to be: a level file has "
                    + "no other room for comments.",
                Bounds = new LevelBounds(),
                Land = new[]
                {
                    new LevelLand
                    {
                        Name = "The island",
                        MinX = -StarterShore,
                        MaxX = StarterShore,
                        MinZ = -StarterShore,
                        MaxZ = StarterShore,
                    },
                },
                Bunkers = new[]
                {
                    NewBunker(Team.Green, new Vector3(0.0f, 0.0f, -StarterBunker), 0.0f),
                    NewBunker(Team.Brown, new Vector3(0.0f, 0.0f, StarterBunker), 180.0f),
                },
                Towers = new[]
                {
                    NewTower(Team.Green, new Vector3(-StarterTowerSpread, 0.0f, -StarterTower), true),
                    NewTower(Team.Green, new Vector3(StarterTowerSpread, 0.0f, -StarterTower), false),
                    NewTower(Team.Brown, new Vector3(StarterTowerSpread, 0.0f, StarterTower), true),
                    NewTower(Team.Brown, new Vector3(-StarterTowerSpread, 0.0f, StarterTower), false),
                },
                Structures = new[]
                {
                    NewStructure(
                        StructureKind.DepotFuel, new Vector3(-StarterDepot, 0.0f, 0.0f), Team.None),
                    NewStructure(
                        StructureKind.DepotAmmo, new Vector3(StarterDepot, 0.0f, 0.0f), Team.None),
                },
            };

            return level;
        }

        /// <summary>
        /// Makes an independent copy of a level.
        /// </summary>
        /// <param name="level">The level to copy.</param>
        /// <returns>A copy sharing nothing with the original, or <c>null</c> for <c>null</c>.</returns>
        /// <remarks>
        /// Through the file format rather than by hand, because a copy made field by field is
        /// one that silently stops copying the day somebody adds a field - and the thing this
        /// backs is undo, where a half-copied level is a level that half comes back.
        /// </remarks>
        public static LevelDefinition Copy(LevelDefinition level)
            => level == null ? null : JsonUtility.FromJson<LevelDefinition>(LevelFile.ToJson(level));

        /// <summary>
        /// Rounds a number onto a grid.
        /// </summary>
        /// <param name="value">The number.</param>
        /// <param name="step">Grid spacing in metres; zero or less snaps to nothing.</param>
        /// <returns>The nearest multiple of the step.</returns>
        public static float Snap(float value, float step)
            => step <= 0.0f ? value : Mathf.Round(value / step) * step;

        /// <summary>
        /// Rounds a point onto a grid, leaving its height alone.
        /// </summary>
        /// <param name="at">The point.</param>
        /// <param name="step">Grid spacing in metres; zero or less snaps to nothing.</param>
        /// <returns>The snapped point.</returns>
        /// <remarks>
        /// Height is not snapped because it is never authored: land is at <c>y = 0</c> and
        /// the one exception, a sunk bridge, is a number this class supplies.
        /// </remarks>
        public static Vector3 Snap(Vector3 at, float step)
            => new Vector3(Snap(at.x, step), at.y, Snap(at.z, step));

        /// <summary>
        /// Returns the height one kind of structure stands at.
        /// </summary>
        /// <param name="kind">The kind being placed.</param>
        /// <returns>Its resting height in metres, which is the ground plane for all but one.</returns>
        /// <seealso cref="BridgeSink"/>
        public static float RestingHeight(StructureKind kind)
            => kind == StructureKind.Bridge ? BridgeSink : 0.0f;

        /// <summary>
        /// Counts one of a level's lists.
        /// </summary>
        /// <param name="level">The level.</param>
        /// <param name="target">Which list.</param>
        /// <returns>How long it is, or zero.</returns>
        public static int Count(LevelDefinition level, EditTarget target)
        {
            if (level == null)
            {
                return 0;
            }

            switch (target)
            {
                case EditTarget.Land:
                    return level.Land.Length;
                case EditTarget.Bunker:
                    return level.Bunkers.Length;
                case EditTarget.Tower:
                    return level.Towers.Length;
                case EditTarget.Structure:
                    return level.Structures.Length;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Reports whether a selection still points at something.
        /// </summary>
        /// <param name="level">The level it points into.</param>
        /// <param name="selection">The selection.</param>
        /// <returns><c>true</c> when that list is that long.</returns>
        /// <remarks>
        /// Asked after every undo and every delete. A selection is an index, so the thing it
        /// named can stop existing without the selection changing.
        /// </remarks>
        public static bool Holds(LevelDefinition level, EditSelection selection)
            => selection.IsSomething && selection.Index < Count(level, selection.Target);

        /// <summary>
        /// Adds a rectangle of dry ground.
        /// </summary>
        /// <param name="level">The level to add to.</param>
        /// <param name="from">One corner, on the ground plane.</param>
        /// <param name="to">The opposite corner.</param>
        /// <param name="name">What to call it.</param>
        /// <returns>Its index, or -1 when the two corners were too close together.</returns>
        /// <remarks>
        /// The corners are sorted here rather than at the call site, because a rectangle
        /// dragged up and to the left is the same rectangle as one dragged down and to the
        /// right, and a level file with <c>MaxX</c> below <c>MinX</c> describes no land at
        /// all - it would simply not be built, with a warning about a piece of land that has
        /// no area.
        /// </remarks>
        public static int AddLand(LevelDefinition level, Vector3 from, Vector3 to, string name)
        {
            if (level == null)
            {
                return -1;
            }

            float minX = Mathf.Min(from.x, to.x);
            float maxX = Mathf.Max(from.x, to.x);
            float minZ = Mathf.Min(from.z, to.z);
            float maxZ = Mathf.Max(from.z, to.z);

            if (maxX - minX < MinimumLandSide || maxZ - minZ < MinimumLandSide)
            {
                return -1;
            }

            var piece = new LevelLand
            {
                Name = string.IsNullOrWhiteSpace(name) ? "Land" : name,
                MinX = minX,
                MaxX = maxX,
                MinZ = minZ,
                MaxZ = maxZ,
            };

            level.Land = Append(level.Land, piece);
            return level.Land.Length - 1;
        }

        /// <summary>
        /// Puts a destructible on the map.
        /// </summary>
        /// <param name="level">The level to add to.</param>
        /// <param name="kind">What to place.</param>
        /// <param name="at">Where, on the ground plane; its height is replaced.</param>
        /// <returns>Its index, or -1 when there is no such kind.</returns>
        /// <remarks>
        /// Places it on no side, which is right for everything except a turret. The overload
        /// taking a side is what the editor's palette calls, because the palette already has
        /// one selected for the towers and bunkers.
        /// </remarks>
        public static int AddStructure(LevelDefinition level, StructureKind kind, Vector3 at)
            => AddStructure(level, kind, at, Team.None);

        /// <summary>
        /// Puts a destructible on the map, on a side.
        /// </summary>
        /// <param name="level">The level to add to.</param>
        /// <param name="kind">What to place.</param>
        /// <param name="at">Where, on the ground plane; its height is replaced.</param>
        /// <param name="side">Side it belongs to; ignored by everything but a turret.</param>
        /// <returns>Its index, or -1 when there is no such kind.</returns>
        /// <remarks>
        /// The side is dropped rather than refused for a kind that cannot have one, so that
        /// a palette with a side always selected can place a tree without the editor having
        /// to know which kinds care. What a level file may actually say is
        /// <see cref="LevelValidation"/>'s business, and it refuses both mistakes.
        /// </remarks>
        public static int AddStructure(
            LevelDefinition level, StructureKind kind, Vector3 at, Team side)
        {
            if (level == null || kind == StructureKind.None || kind == StructureKind.FlagTower)
            {
                return -1;
            }

            level.Structures = Append(level.Structures, NewStructure(kind, at, side));
            return level.Structures.Length - 1;
        }

        /// <summary>
        /// Puts a flag tower on the map.
        /// </summary>
        /// <param name="level">The level to add to.</param>
        /// <param name="side">Side whose flag it is for.</param>
        /// <param name="at">Where, on the ground plane.</param>
        /// <returns>Its index, or -1 when the side was nobody.</returns>
        /// <remarks>
        /// The first tower a side gets is the real one and every one after it is a decoy,
        /// which is the only rule that makes a half-built map legal at every step: a side
        /// with one tower has a findable flag, and a side with two has a decoy. Making them
        /// all decoys instead would mean every new map is broken until somebody remembers to
        /// promote one.
        /// </remarks>
        public static int AddTower(LevelDefinition level, Team side, Vector3 at)
        {
            if (level == null || side == Team.None)
            {
                return -1;
            }

            bool holdsTheFlag = level.TowersFor(side).Count == 0;
            level.Towers = Append(level.Towers, NewTower(side, at, holdsTheFlag));
            return level.Towers.Length - 1;
        }

        /// <summary>
        /// Puts a side's bunker on the map, or moves the one it has.
        /// </summary>
        /// <param name="level">The level to add to.</param>
        /// <param name="side">Side the bunker belongs to.</param>
        /// <param name="at">Where, on the ground plane.</param>
        /// <returns>Its index, or -1 when the side was nobody.</returns>
        /// <remarks>
        /// A side has one bunker: it is where that side deploys, repairs, refuels and wins,
        /// and two of them would be two of all four. So placing a second one moves the first
        /// instead, which is also what somebody clicking the bunker tool on the far side of
        /// the map almost certainly meant.
        /// </remarks>
        public static int AddBunker(LevelDefinition level, Team side, Vector3 at)
        {
            if (level == null || side == Team.None)
            {
                return -1;
            }

            for (int index = 0; index < level.Bunkers.Length; index++)
            {
                if (level.Bunkers[index] != null && level.Bunkers[index].Side == side)
                {
                    level.Bunkers[index].Position = OnTheGround(at);
                    return index;
                }
            }

            level.Bunkers = Append(level.Bunkers, NewBunker(side, at, FacingTheField(at)));
            return level.Bunkers.Length - 1;
        }

        /// <summary>
        /// Takes something off the map.
        /// </summary>
        /// <param name="level">The level to remove from.</param>
        /// <param name="selection">What to remove.</param>
        /// <returns><c>true</c> when something was removed.</returns>
        public static bool Remove(LevelDefinition level, EditSelection selection)
        {
            if (!Holds(level, selection))
            {
                return false;
            }

            switch (selection.Target)
            {
                case EditTarget.Land:
                    level.Land = RemoveAt(level.Land, selection.Index);
                    return true;
                case EditTarget.Bunker:
                    level.Bunkers = RemoveAt(level.Bunkers, selection.Index);
                    return true;
                case EditTarget.Tower:
                    level.Towers = RemoveAt(level.Towers, selection.Index);
                    return true;
                case EditTarget.Structure:
                    level.Structures = RemoveAt(level.Structures, selection.Index);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns where something stands.
        /// </summary>
        /// <param name="level">The level it is on.</param>
        /// <param name="selection">What to look up.</param>
        /// <returns>Its position, or the origin when the selection points at nothing.</returns>
        /// <remarks>A rectangle of land answers with its middle, which is what a drag moves.</remarks>
        public static Vector3 PositionOf(LevelDefinition level, EditSelection selection)
        {
            if (!Holds(level, selection))
            {
                return Vector3.zero;
            }

            switch (selection.Target)
            {
                case EditTarget.Land:
                    return level.Land[selection.Index].Centre;
                case EditTarget.Bunker:
                    return level.Bunkers[selection.Index].Position;
                case EditTarget.Tower:
                    return level.Towers[selection.Index].Position;
                case EditTarget.Structure:
                    return level.Structures[selection.Index].Position;
                default:
                    return Vector3.zero;
            }
        }

        /// <summary>
        /// Moves something to a place.
        /// </summary>
        /// <param name="level">The level it is on.</param>
        /// <param name="selection">What to move.</param>
        /// <param name="to">Where to put it; its height is ignored.</param>
        /// <returns><c>true</c> when something moved.</returns>
        /// <remarks>
        /// Heights are never taken from the caller. A structure goes back to the height its
        /// kind rests at, so dragging a bridge cannot lift it out of the water it spans, and
        /// everything else goes to the ground plane the whole game is resolved on.
        /// </remarks>
        public static bool MoveTo(LevelDefinition level, EditSelection selection, Vector3 to)
        {
            if (!Holds(level, selection))
            {
                return false;
            }

            switch (selection.Target)
            {
                case EditTarget.Land:
                    LevelLand piece = level.Land[selection.Index];
                    float halfWidth = piece.Width * 0.5f;
                    float halfDepth = piece.Depth * 0.5f;
                    piece.MinX = to.x - halfWidth;
                    piece.MaxX = to.x + halfWidth;
                    piece.MinZ = to.z - halfDepth;
                    piece.MaxZ = to.z + halfDepth;
                    return true;

                case EditTarget.Bunker:
                    level.Bunkers[selection.Index].Position = OnTheGround(to);
                    return true;

                case EditTarget.Tower:
                    level.Towers[selection.Index].Position = OnTheGround(to);
                    return true;

                case EditTarget.Structure:
                    LevelStructure structure = level.Structures[selection.Index];
                    structure.Position = new Vector3(to.x, RestingHeight(structure.Structure), to.z);
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Sets the edges of a rectangle of land, sorting them and refusing a sliver.
        /// </summary>
        /// <param name="level">The level it is on.</param>
        /// <param name="index">Which rectangle.</param>
        /// <param name="minX">One x edge.</param>
        /// <param name="maxX">The other x edge.</param>
        /// <param name="minZ">One z edge.</param>
        /// <param name="maxZ">The other z edge.</param>
        /// <returns><c>true</c> when the rectangle was changed.</returns>
        public static bool SetLandEdges(
            LevelDefinition level, int index, float minX, float maxX, float minZ, float maxZ)
        {
            if (!Holds(level, new EditSelection(EditTarget.Land, index)))
            {
                return false;
            }

            float lowX = Mathf.Min(minX, maxX);
            float highX = Mathf.Max(minX, maxX);
            float lowZ = Mathf.Min(minZ, maxZ);
            float highZ = Mathf.Max(minZ, maxZ);

            if (highX - lowX < MinimumLandSide || highZ - lowZ < MinimumLandSide)
            {
                return false;
            }

            LevelLand piece = level.Land[index];
            piece.MinX = lowX;
            piece.MaxX = highX;
            piece.MinZ = lowZ;
            piece.MaxZ = highZ;
            return true;
        }

        /// <summary>
        /// Returns which way something faces.
        /// </summary>
        /// <param name="level">The level it is on.</param>
        /// <param name="selection">What to look up.</param>
        /// <returns>Its heading in degrees; land has none and answers zero.</returns>
        public static float YawOf(LevelDefinition level, EditSelection selection)
        {
            if (!Holds(level, selection))
            {
                return 0.0f;
            }

            switch (selection.Target)
            {
                case EditTarget.Bunker:
                    return level.Bunkers[selection.Index].YawDegrees;
                case EditTarget.Tower:
                    return level.Towers[selection.Index].YawDegrees;
                case EditTarget.Structure:
                    return level.Structures[selection.Index].YawDegrees;
                default:
                    return 0.0f;
            }
        }

        /// <summary>
        /// Turns something to face a new heading.
        /// </summary>
        /// <param name="level">The level it is on.</param>
        /// <param name="selection">What to turn.</param>
        /// <param name="degrees">Heading, clockwise from world +Z. Wrapped into 0..360.</param>
        /// <returns><c>true</c> when something turned.</returns>
        public static bool SetYaw(LevelDefinition level, EditSelection selection, float degrees)
        {
            if (!Holds(level, selection))
            {
                return false;
            }

            float wrapped = Mathf.Repeat(degrees, 360.0f);

            switch (selection.Target)
            {
                case EditTarget.Bunker:
                    level.Bunkers[selection.Index].YawDegrees = wrapped;
                    return true;
                case EditTarget.Tower:
                    level.Towers[selection.Index].YawDegrees = wrapped;
                    return true;
                case EditTarget.Structure:
                    level.Structures[selection.Index].YawDegrees = wrapped;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns what something is called, in the words the editor shows.
        /// </summary>
        /// <param name="level">The level it is on.</param>
        /// <param name="selection">What to describe.</param>
        /// <returns>A short label; never empty while the selection holds.</returns>
        public static string Describe(LevelDefinition level, EditSelection selection)
        {
            if (!Holds(level, selection))
            {
                return "Nothing";
            }

            switch (selection.Target)
            {
                case EditTarget.Land:
                    LevelLand piece = level.Land[selection.Index];
                    return string.IsNullOrWhiteSpace(piece.Name) ? "Land" : piece.Name;

                case EditTarget.Bunker:
                    return $"{level.Bunkers[selection.Index].Side} bunker";

                case EditTarget.Tower:
                    LevelTower tower = level.Towers[selection.Index];
                    return $"{tower.Side} tower";

                case EditTarget.Structure:
                    LevelStructure structure = level.Structures[selection.Index];
                    return string.IsNullOrWhiteSpace(structure.Name)
                        ? structure.Kind
                        : structure.Name;

                default:
                    return "Nothing";
            }
        }

        /// <summary>
        /// Makes one side's tower the real one and every other tower of that side a decoy.
        /// </summary>
        /// <param name="level">The level it is on.</param>
        /// <param name="index">Which tower becomes the real one.</param>
        /// <returns><c>true</c> when a tower was promoted.</returns>
        /// <remarks>
        /// Promotion rather than a toggle, and that is the point. A side must have exactly
        /// one real tower, so a checkbox on each of them lets a player make a map with two or
        /// with none - both of which are broken, and neither of which looks any different on
        /// the map. Choosing which one is real can only ever demote the other.
        /// </remarks>
        public static bool MakeRealTower(LevelDefinition level, int index)
        {
            if (!Holds(level, new EditSelection(EditTarget.Tower, index)))
            {
                return false;
            }

            Team side = level.Towers[index].Side;

            for (int other = 0; other < level.Towers.Length; other++)
            {
                if (level.Towers[other] != null && level.Towers[other].Side == side)
                {
                    level.Towers[other].HoldsTheFlag = other == index;
                }
            }

            return true;
        }

        /// <summary>
        /// Returns the side opposite one side.
        /// </summary>
        /// <param name="side">A side.</param>
        /// <returns>The other one, or <see cref="Team.None"/> for nobody.</returns>
        public static Team Opposite(Team side)
        {
            switch (side)
            {
                case Team.Green:
                    return Team.Brown;
                case Team.Brown:
                    return Team.Green;
                default:
                    return Team.None;
            }
        }

        /// <summary>
        /// Adds the opposite number of something: the same thing, rotated half a turn about
        /// the origin, belonging to the other side.
        /// </summary>
        /// <param name="level">The level it is on.</param>
        /// <param name="selection">What to mirror.</param>
        /// <returns>What was added, or <see cref="EditSelection.Nothing"/>.</returns>
        /// <remarks>
        /// <para>
        /// <strong>Rotated, not reflected.</strong> Every map this game has is built in pairs
        /// turned half a turn about the origin rather than mirrored across a line, and the
        /// difference is the whole point: a reflection gives one side a left-handed base and
        /// the other a right-handed one, and the two runs to the enemy flag are then not the
        /// same shape. A rotation makes them identical, which is what
        /// <c>LevelDesignTests</c> walks the shipped map checking for.
        /// </para>
        /// <para>
        /// A tower or a bunker comes back belonging to the <em>other</em> side, because the
        /// thing at the opposite end of a symmetrical map is the other side's version of it.
        /// Mirroring the green side's real tower therefore produces the brown side's real
        /// tower, which is exactly the pairing the shipped map has.
        /// </para>
        /// </remarks>
        public static EditSelection Mirror(LevelDefinition level, EditSelection selection)
        {
            if (!Holds(level, selection))
            {
                return EditSelection.Nothing;
            }

            switch (selection.Target)
            {
                case EditTarget.Land:
                    LevelLand piece = level.Land[selection.Index];
                    int drawn = AddLand(
                        level,
                        new Vector3(-piece.MinX, 0.0f, -piece.MinZ),
                        new Vector3(-piece.MaxX, 0.0f, -piece.MaxZ),
                        piece.Name);
                    if (drawn < 0)
                    {
                        return EditSelection.Nothing;
                    }

                    level.Land[drawn].Surface = piece.Surface;
                    level.Land[drawn].Shape = piece.Shape;
                    return new EditSelection(EditTarget.Land, drawn);

                case EditTarget.Structure:
                    LevelStructure structure = level.Structures[selection.Index];

                    // A mirrored turret is the *other* side's turret, exactly as a mirrored
                    // tower is the other side's tower. Copying the side across instead would
                    // give one player both emplacements on a map that still looked
                    // symmetrical, which is the worst kind of asymmetry: invisible.
                    int placed = AddStructure(
                        level, structure.Structure, Turned(structure.Position),
                        Opposite(structure.Team));
                    if (placed < 0)
                    {
                        return EditSelection.Nothing;
                    }

                    LevelStructure copy = level.Structures[placed];
                    copy.Name = structure.Name;
                    copy.YawDegrees = Mathf.Repeat(structure.YawDegrees + 180.0f, 360.0f);
                    copy.FuelRate = structure.FuelRate;
                    copy.AmmoRate = structure.AmmoRate;
                    copy.SupplyRadius = structure.SupplyRadius;
                    return new EditSelection(EditTarget.Structure, placed);

                case EditTarget.Tower:
                    LevelTower tower = level.Towers[selection.Index];
                    int raised = AddTower(level, Opposite(tower.Side), Turned(tower.Position));
                    if (raised < 0)
                    {
                        return EditSelection.Nothing;
                    }

                    level.Towers[raised].YawDegrees =
                        Mathf.Repeat(tower.YawDegrees + 180.0f, 360.0f);

                    // AddTower auto-promotes a side's very first tower to real, which is
                    // right when mirroring the real one onto a side with none yet and wrong
                    // when mirroring a decoy onto the same empty side - so the mirrored
                    // tower's flag status always matches the source's explicitly, rather
                    // than being left to that heuristic. Left to the heuristic, mirroring
                    // only a side's decoys produced a side with one real tower and zero
                    // problems reported, and the flag sitting on the wrong pyramid.
                    if (tower.HoldsTheFlag)
                    {
                        MakeRealTower(level, raised);
                    }
                    else
                    {
                        level.Towers[raised].HoldsTheFlag = false;
                    }

                    return new EditSelection(EditTarget.Tower, raised);

                case EditTarget.Bunker:
                    LevelBunker bunker = level.Bunkers[selection.Index];
                    int built = AddBunker(level, Opposite(bunker.Side), Turned(bunker.Position));
                    if (built < 0)
                    {
                        return EditSelection.Nothing;
                    }

                    LevelBunker twin = level.Bunkers[built];
                    twin.YawDegrees = Mathf.Repeat(bunker.YawDegrees + 180.0f, 360.0f);
                    twin.SupplyRadius = bunker.SupplyRadius;
                    twin.SupplyRate = bunker.SupplyRate;
                    return new EditSelection(EditTarget.Bunker, built);

                default:
                    return EditSelection.Nothing;
            }
        }

        /// <summary>
        /// Returns a point rotated half a turn about the origin, at the same height.
        /// </summary>
        /// <param name="at">The point.</param>
        /// <returns>Its opposite number.</returns>
        private static Vector3 Turned(Vector3 at) => new Vector3(-at.x, at.y, -at.z);

        /// <summary>
        /// Returns the kinds of structure a palette can offer.
        /// </summary>
        /// <returns>Everything a level may place as scenery, in roster order.</returns>
        /// <remarks>
        /// Read off <see cref="StructureTuning.Roster"/> rather than written down again, so a
        /// seventh prop appears in the editor by existing. The flag tower is deliberately not
        /// in it: it is placed as an objective, where it can be given a side.
        /// </remarks>
        public static IReadOnlyList<StructureKind> Palette() => StructureTuning.Roster();

        /// <summary>
        /// Makes one structure, at the height and rates its kind wants.
        /// </summary>
        /// <param name="kind">What it is.</param>
        /// <param name="at">Where it goes; its height is replaced.</param>
        /// <param name="side">Side it belongs to; kept only by the kinds that have one.</param>
        /// <returns>The structure.</returns>
        private static LevelStructure NewStructure(StructureKind kind, Vector3 at, Team side)
            => new LevelStructure
            {
                Kind = kind.ToString(),
                Side = (StructureTuning.BelongsToASide(kind) ? side : Team.None).ToString(),
                Name = string.Empty,
                Position = new Vector3(at.x, RestingHeight(kind), at.z),
                YawDegrees = 0.0f,
                FuelRate = kind == StructureKind.DepotFuel ? DepotRate : 0.0f,
                AmmoRate = kind == StructureKind.DepotAmmo ? DepotRate : 0.0f,
                SupplyRadius = DepotRadius,
            };

        private static LevelTower NewTower(Team side, Vector3 at, bool holdsTheFlag)
            => new LevelTower
            {
                Team = side.ToString(),
                HoldsTheFlag = holdsTheFlag,
                Position = OnTheGround(at),
                YawDegrees = FacingTheField(at),
            };

        private static LevelBunker NewBunker(Team side, Vector3 at, float yawDegrees)
            => new LevelBunker
            {
                Team = side.ToString(),
                Position = OnTheGround(at),
                YawDegrees = Mathf.Repeat(yawDegrees, 360.0f),
            };

        /// <summary>
        /// Returns the heading something at a place should be given when it is first placed.
        /// </summary>
        /// <param name="at">Where it is going.</param>
        /// <returns>A heading in degrees, pointing back at the middle of the map.</returns>
        /// <remarks>
        /// A bunker's yaw is which way its lift bay points, so one facing away from the field
        /// deploys its vehicles into its own back wall - the single most expensive default to
        /// get wrong, and the one a person placing a bunker is least likely to think about.
        /// Facing the origin is right on every map whose fight is in the middle, which is
        /// every map this game can describe.
        /// </remarks>
        private static float FacingTheField(Vector3 at)
        {
            var away = new Vector3(at.x, 0.0f, at.z);
            return away.sqrMagnitude < 0.01f
                ? 0.0f
                : Mathf.Repeat(Mathf.Atan2(-away.x, -away.z) * Mathf.Rad2Deg, 360.0f);
        }

        private static Vector3 OnTheGround(Vector3 at) => new Vector3(at.x, 0.0f, at.z);

        private static T[] Append<T>(T[] existing, T item)
        {
            T[] grown = existing == null ? new T[1] : new T[existing.Length + 1];
            if (existing != null)
            {
                Array.Copy(existing, grown, existing.Length);
            }

            grown[grown.Length - 1] = item;
            return grown;
        }

        private static T[] RemoveAt<T>(T[] existing, int index)
        {
            var kept = new T[existing.Length - 1];
            Array.Copy(existing, 0, kept, 0, index);
            Array.Copy(existing, index + 1, kept, index, existing.Length - index - 1);
            return kept;
        }
    }
}
