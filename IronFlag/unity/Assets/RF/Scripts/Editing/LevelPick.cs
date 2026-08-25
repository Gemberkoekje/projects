using UnityEngine;
using IronFlag.Destruction;
using IronFlag.Levels;

namespace IronFlag.Editing
{
    /// <summary>
    /// What is under a point on the map, answered against the level file rather than against
    /// the geometry built from it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The obvious way to do this is a physics raycast onto the map the builder just made,
    /// and it is the wrong way. A ray comes back with a collider somewhere inside an
    /// instantiated prefab, and turning that into "the fourteenth entry of the structure
    /// list" means walking up a hierarchy looking for something that was never labelled with
    /// its index. Worse, it can only ever find things that were <em>built</em>: a prop whose
    /// kind the catalog has no prefab for is on the map, is in the file, and has no collider
    /// to hit - so it could be neither selected nor deleted, which is the exact situation
    /// somebody opening the editor would most want to fix.
    /// </para>
    /// <para>
    /// Asking the definition instead makes the answer exact, testable without a scene, and
    /// true of things that failed to build.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// EditSelection under = LevelPick.At(level, LevelPick.GroundUnder(camera, mouse));
    /// </code>
    /// </example>
    public static class LevelPick
    {
        /// <summary>Metres around a bunker a click counts as being on it.</summary>
        /// <remarks>
        /// These are click targets rather than footprints, and they are deliberately a little
        /// generous: the map is looked at from a long way up, so a prop is a few pixels
        /// across at the zoom a whole level is judged at, and an editor that demands a hit on
        /// the mesh is one that is used zoomed in. They are ordered the way the models are -
        /// the bunker is the biggest thing on the map, a tree is the smallest - so overlapping
        /// targets still resolve the way the picture reads.
        /// </remarks>
        public const float BunkerReach = 9.0f;

        /// <summary>Metres around a flag tower a click counts as being on it.</summary>
        public const float TowerReach = 4.5f;

        /// <summary>Metres around a corner of a rectangle of land a drag takes hold of it.</summary>
        /// <remarks>
        /// Smaller than <see cref="LevelEdits.MinimumLandSide"/> is halved, so the four
        /// corners of even the smallest legal rectangle cannot all be the same point.
        /// </remarks>
        public const float GripReach = 3.0f;

        /// <summary>
        /// Returns what is under a point on the ground plane.
        /// </summary>
        /// <param name="level">The level to look in.</param>
        /// <param name="at">The point; its height is ignored.</param>
        /// <returns>What was hit, or <see cref="EditSelection.Nothing"/>.</returns>
        /// <remarks>
        /// Everything that stands on the map is tried before the ground it stands on, so a
        /// depot on a shore selects the depot. Among the things that stand on it, the best
        /// hit is the one that is furthest inside its <em>own</em> reach rather than the
        /// nearest in metres: a tree two metres from the cursor beats a bunker four metres
        /// from it, which is what the picture on screen says as well.
        /// </remarks>
        public static EditSelection At(LevelDefinition level, Vector3 at)
        {
            if (level == null)
            {
                return EditSelection.Nothing;
            }

            EditSelection best = EditSelection.Nothing;
            float closest = float.MaxValue;

            for (int index = 0; index < level.Bunkers.Length; index++)
            {
                LevelBunker bunker = level.Bunkers[index];
                if (bunker != null)
                {
                    Consider(
                        at, bunker.Position, BunkerReach, EditTarget.Bunker, index,
                        ref best, ref closest);
                }
            }

            for (int index = 0; index < level.Towers.Length; index++)
            {
                LevelTower tower = level.Towers[index];
                if (tower != null)
                {
                    Consider(
                        at, tower.Position, TowerReach, EditTarget.Tower, index,
                        ref best, ref closest);
                }
            }

            for (int index = 0; index < level.Structures.Length; index++)
            {
                LevelStructure structure = level.Structures[index];
                if (structure != null)
                {
                    Consider(
                        at, structure.Position, ReachOf(structure.Structure), EditTarget.Structure,
                        index, ref best, ref closest);
                }
            }

            return best.IsSomething ? best : LandAt(level, at);
        }

        /// <summary>
        /// Returns the smallest rectangle of land covering a point.
        /// </summary>
        /// <param name="level">The level to look in.</param>
        /// <param name="at">The point; its height is ignored.</param>
        /// <returns>The land, or <see cref="EditSelection.Nothing"/> when the point is sea.</returns>
        /// <remarks>
        /// Smallest rather than first, because rectangles overlap on purpose - a bridgehead
        /// is a small rectangle laid over a long shore - and the small one on top is the one
        /// somebody clicking it is pointing at. Picking the first would make a headland
        /// unselectable the moment it was drawn over its own coast.
        /// </remarks>
        public static EditSelection LandAt(LevelDefinition level, Vector3 at)
        {
            if (level == null)
            {
                return EditSelection.Nothing;
            }

            EditSelection best = EditSelection.Nothing;
            float smallest = float.MaxValue;

            for (int index = 0; index < level.Land.Length; index++)
            {
                LevelLand piece = level.Land[index];
                if (piece == null || !piece.IsDrawn || !piece.Contains(at))
                {
                    continue;
                }

                float area = piece.Width * piece.Depth;
                if (area < smallest)
                {
                    smallest = area;
                    best = new EditSelection(EditTarget.Land, index);
                }
            }

            return best;
        }

        /// <summary>
        /// Returns which part of a rectangle of land a point has hold of.
        /// </summary>
        /// <param name="level">The level it is on.</param>
        /// <param name="index">Which rectangle.</param>
        /// <param name="at">The point; its height is ignored.</param>
        /// <param name="reach">Metres from a corner that counts as holding it.</param>
        /// <returns>The corner, <see cref="LandGrip.Body"/>, or <see cref="LandGrip.None"/>.</returns>
        /// <remarks>
        /// Corners are tested before the body and are reachable from <em>outside</em> the
        /// rectangle as well as inside it, so the last few metres of a shore can be dragged
        /// out over the water it is going to replace.
        /// </remarks>
        public static LandGrip GripOn(LevelDefinition level, int index, Vector3 at, float reach)
        {
            if (level == null || index < 0 || index >= level.Land.Length)
            {
                return LandGrip.None;
            }

            LevelLand piece = level.Land[index];
            if (piece == null || !piece.IsDrawn)
            {
                return LandGrip.None;
            }

            LandGrip nearest = LandGrip.None;
            float closest = reach;

            closest = TryCorner(at, piece.MinX, piece.MinZ, LandGrip.SouthWest, closest, ref nearest);
            closest = TryCorner(at, piece.MaxX, piece.MinZ, LandGrip.SouthEast, closest, ref nearest);
            closest = TryCorner(at, piece.MinX, piece.MaxZ, LandGrip.NorthWest, closest, ref nearest);
            TryCorner(at, piece.MaxX, piece.MaxZ, LandGrip.NorthEast, closest, ref nearest);

            if (nearest != LandGrip.None)
            {
                return nearest;
            }

            return piece.Contains(at) ? LandGrip.Body : LandGrip.None;
        }

        /// <summary>
        /// Returns metres around one kind of structure a click counts as being on it.
        /// </summary>
        /// <param name="kind">What it is.</param>
        /// <returns>Its click radius in metres.</returns>
        public static float ReachOf(StructureKind kind)
        {
            switch (kind)
            {
                case StructureKind.Tree:
                    return 1.8f;
                case StructureKind.BuildingA:
                    return 4.0f;
                case StructureKind.BuildingB:
                    return 5.0f;
                case StructureKind.Bridge:
                    return 6.0f;
                case StructureKind.DepotFuel:
                case StructureKind.DepotAmmo:
                    return 3.0f;

                // Half a segment, which is the one reach here that is a measurement rather
                // than a judgement: walls are placed touching, so anything wider would make
                // the two either side of a join fight over the same clicks, and this way the
                // boundary between them falls exactly where the join a player can see is.
                case StructureKind.Wall:
                    return 2.5f;
                default:
                    return 3.0f;
            }
        }

        /// <summary>
        /// Returns metres around one kind of target a click counts as being on it.
        /// </summary>
        /// <param name="level">The level it is on.</param>
        /// <param name="selection">What to measure.</param>
        /// <returns>Its click radius in metres, or zero for a rectangle of land.</returns>
        /// <remarks>What the overlay draws its selection ring at, so the two cannot drift.</remarks>
        public static float ReachOf(LevelDefinition level, EditSelection selection)
        {
            if (!LevelEdits.Holds(level, selection))
            {
                return 0.0f;
            }

            switch (selection.Target)
            {
                case EditTarget.Bunker:
                    return BunkerReach;
                case EditTarget.Tower:
                    return TowerReach;
                case EditTarget.Structure:
                    return ReachOf(level.Structures[selection.Index].Structure);
                default:
                    return 0.0f;
            }
        }

        /// <summary>
        /// Turns a point on the screen into the point on the ground plane under it.
        /// </summary>
        /// <param name="view">The camera looking at the map.</param>
        /// <param name="screen">A position in pixels, as the pointer reports it.</param>
        /// <returns>
        /// The point on <c>y = 0</c>, or the origin when the camera is missing or is looking
        /// along the plane rather than at it.
        /// </returns>
        /// <remarks>
        /// A plane intersection rather than a raycast, because the ground the editor works on
        /// is the plane the whole game is resolved on - not whatever collider happens to be
        /// up. Clicking a spot with no land under it has to land somewhere, or a rectangle
        /// could never be drawn out over open sea.
        /// </remarks>
        public static Vector3 GroundUnder(Camera view, Vector2 screen)
        {
            if (view == null)
            {
                return Vector3.zero;
            }

            Ray ray = view.ScreenPointToRay(screen);
            var ground = new Plane(Vector3.up, Vector3.zero);

            return ground.Raycast(ray, out float distance) ? ray.GetPoint(distance) : Vector3.zero;
        }

        private static void Consider(
            Vector3 at,
            Vector3 candidate,
            float reach,
            EditTarget target,
            int index,
            ref EditSelection best,
            ref float closest)
        {
            if (reach <= 0.0f)
            {
                return;
            }

            float across = Distance(at, candidate) / reach;
            if (across <= 1.0f && across < closest)
            {
                closest = across;
                best = new EditSelection(target, index);
            }
        }

        private static float TryCorner(
            Vector3 at, float x, float z, LandGrip grip, float closest, ref LandGrip nearest)
        {
            float away = Distance(at, new Vector3(x, 0.0f, z));
            if (away > closest)
            {
                return closest;
            }

            nearest = grip;
            return away;
        }

        private static float Distance(Vector3 from, Vector3 to)
            => new Vector2(from.x - to.x, from.z - to.z).magnitude;
    }
}
