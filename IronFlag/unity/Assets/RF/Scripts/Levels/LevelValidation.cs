using System;
using System.Collections.Generic;
using UnityEngine;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Destruction;
using IronFlag.Objective;
using IronFlag.Vehicles;

namespace IronFlag.Levels
{
    /// <summary>
    /// The rules a level file has to obey to be a playable map, and the reasons it does not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A level is now data, which means the ways it can be wrong are the ways a text file can
    /// be wrong: a tower nobody can win from, a bunker in the sea, a decoy close enough to its
    /// twin that one shell opens both, a coastline that cuts the map in half. None of those
    /// look wrong. All of them are arithmetic, and this is where the arithmetic lives -
    /// once, so the tests, the scene builder and the level editor that comes later all agree
    /// about what a broken map is.
    /// </para>
    /// <para>
    /// These are hard failures rather than taste. Whether a map is <em>good</em> is not
    /// checkable; whether it can be finished is.
    /// </para>
    /// <para>
    /// <strong>There are two kinds of map and one set of rules.</strong> A match is played
    /// between two sides that each own a bunker and a flag; a one-player map has a single
    /// bunker and an enemy that is a field of flag towers behind emplacements, with nobody
    /// sitting behind it. The checks below branch on <see cref="LevelDefinition.IsSolo"/>
    /// rather than being duplicated per mode, because most of them - the shore margins, the
    /// tower spacing, the reserve, the land - are the same question either way, and the
    /// three that differ (who owes a bunker, who owes towers, what has to be drivable to)
    /// differ in a sentence each.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// List&lt;string&gt; problems = LevelValidation.Problems(level);
    /// Assert.That(problems, Is.Empty);
    /// </code>
    /// </example>
    public static class LevelValidation
    {
        /// <summary>Metres of dry land a placed structure needs around its centre.</summary>
        /// <remarks>
        /// Read against the widest vehicle in the game, the tank at 3.19 m: something whose
        /// centre is this far inland cannot be half in the water from any angle a vehicle
        /// could reach it from.
        /// </remarks>
        public const float ShoreMargin = 2.5f;

        /// <summary>Metres a bunker needs of dry land around it.</summary>
        /// <remarks>
        /// A bunker is the biggest structure on the map and has to be drivable around on
        /// every side, because its lift bay, its helipad and its supply radius are all
        /// approached from the field.
        /// </remarks>
        public const float BunkerShoreMargin = 10.0f;

        /// <summary>Size of the cells the land is walked over in, in metres.</summary>
        /// <remarks>
        /// Half the width of the narrowest thing a level should ask a tank to drive through.
        /// Coarser and a real isthmus disappears; finer and the walk costs more than the
        /// answer is worth.
        /// </remarks>
        public const float StepSize = 2.0f;

        /// <summary>The four steps a flood fill takes from a cell.</summary>
        private static readonly Vector2Int[] Neighbours =
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1),
        };

        /// <summary>
        /// Lists everything wrong with a level, in the order it was checked.
        /// </summary>
        /// <param name="level">The level to check.</param>
        /// <returns>One sentence per problem; empty when the level is playable.</returns>
        public static List<string> Problems(LevelDefinition level)
        {
            var problems = new List<string>();

            if (level == null)
            {
                problems.Add("There is no level.");
                return problems;
            }

            CheckBounds(level, problems);
            CheckReserve(level, problems);
            CheckLand(level, problems);
            CheckBunkers(level, problems);
            CheckTowers(level, problems);
            CheckStructures(level, problems);
            CheckCrossings(level, problems);

            return problems;
        }

        /// <summary>
        /// Reports whether two points are joined by dry land.
        /// </summary>
        /// <param name="level">The level to walk.</param>
        /// <param name="from">Where to start.</param>
        /// <param name="to">Where to get to.</param>
        /// <returns><c>true</c> when a ground vehicle could drive between them.</returns>
        /// <remarks>
        /// <para>
        /// Land only. Bridges are deliberately not counted, and that is the point of the
        /// check rather than a simplification of it: only the jeep carries the flag and the
        /// jeep cannot fly, so a map whose crossings are all destructible is a map that
        /// becomes unwinnable the moment somebody drops the last one. Every level owes at
        /// least one crossing nobody can take away.
        /// </para>
        /// <para>
        /// A flood fill over <see cref="StepSize"/> cells rather than anything cleverer: the
        /// map is a couple of hundred metres across and the answer is wanted once at load
        /// rather than once a frame. It has a grid of its own, coarser than
        /// <see cref="SurfaceField"/>'s, because the two are measuring different things -
        /// this one is asking what a tank can get through, and half the width of the
        /// narrowest thing worth driving through is the right step for that.
        /// </para>
        /// <para>
        /// What is under each step is the field's answer rather than the rectangles', so
        /// that a coastline the level file did not draw - one derived, or in a later phase
        /// displaced - cannot cut the map in two behind validation's back.
        /// </para>
        /// </remarks>
        public static bool IsConnected(LevelDefinition level, Vector3 from, Vector3 to)
        {
            if (level == null || level.Bounds == null)
            {
                return false;
            }

            SurfaceField field = level.Field;
            float extent = Mathf.Abs(level.Bounds.HalfExtent);
            int cells = Mathf.Max(1, Mathf.CeilToInt(extent * 2.0f / StepSize));

            Vector2Int start = ToCell(from, extent, cells);
            Vector2Int goal = ToCell(to, extent, cells);

            if (!IsWalkable(field, start, extent, cells) || !IsWalkable(field, goal, extent, cells))
            {
                return false;
            }

            var seen = new HashSet<int>();
            var queue = new Queue<Vector2Int>();
            seen.Add(Key(start, cells));
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                if (cell == goal)
                {
                    return true;
                }

                foreach (Vector2Int step in Neighbours)
                {
                    var next = new Vector2Int(cell.x + step.x, cell.y + step.y);
                    if (next.x < 0 || next.y < 0 || next.x >= cells || next.y >= cells)
                    {
                        continue;
                    }

                    if (!seen.Add(Key(next, cells)))
                    {
                        continue;
                    }

                    if (IsWalkable(field, next, extent, cells))
                    {
                        queue.Enqueue(next);
                    }
                }
            }

            return false;
        }

        private static void CheckBounds(LevelDefinition level, List<string> problems)
        {
            if (level.SchemaVersion < 1)
            {
                problems.Add($"The level claims schema version {level.SchemaVersion}.");
            }

            if (string.IsNullOrWhiteSpace(level.Name))
            {
                problems.Add("The level has no name.");
            }

            if (level.Bounds == null)
            {
                problems.Add("The level has no bounds.");
                return;
            }

            if (level.Bounds.HalfExtent <= 0.0f)
            {
                problems.Add("The level has no extent, so there is no world to drive in.");
            }

            if (level.Bounds.WaterDepth <= 0.0f)
            {
                problems.Add("The water is level with the land, so a shoreline is invisible.");
            }
        }

        /// <summary>
        /// Checks that both sides are given enough vehicles to play the match out.
        /// </summary>
        /// <param name="level">The level to check.</param>
        /// <param name="problems">List to add to.</param>
        /// <remarks>
        /// <para>
        /// A level that gives nobody a flag carrier is the arithmetic version of a map with
        /// no crossing on it: the game is unwinnable from the first frame, and it does not
        /// look wrong anywhere. Everything else about the reserve is a taste question - four
        /// jeeps is a short match and twelve is a long one, and neither is broken.
        /// </para>
        /// <para>
        /// A level with no reserve block at all is not a problem. That is every map written
        /// before version 4, and it means the standard allotment.
        /// </para>
        /// </remarks>
        private static void CheckReserve(LevelDefinition level, List<string> problems)
        {
            LevelReserve stock = level.Reserve;
            if (stock == null)
            {
                return;
            }

            bool carrier = false;

            foreach (VehicleKind kind in VehicleRoster.Kinds)
            {
                int held = stock.For(kind);

                if (held < 0)
                {
                    problems.Add(
                        $"The level gives each side {held} {VehicleNames.For(kind)}s, "
                        + "which is not a number of vehicles.");
                }

                carrier = carrier || (held > 0 && FlagRules.CanCarry(kind));
            }

            if (!carrier)
            {
                problems.Add(
                    "The level gives each side no jeeps, so neither side can carry a flag "
                    + "home and the match cannot be won.");
            }
        }

        private static void CheckLand(LevelDefinition level, List<string> problems)
        {
            if (level.Land.Length == 0)
            {
                problems.Add("The level is all sea: it has no land at all.");
                return;
            }

            float extent = level.Bounds == null ? 0.0f : Mathf.Abs(level.Bounds.HalfExtent);

            foreach (LevelLand piece in level.Land)
            {
                if (piece == null || !piece.IsDrawn)
                {
                    problems.Add("A piece of land has no area.");
                    continue;
                }

                // A natural coast is drawn where the file says and then wanders, so the
                // room it needs is its own edge plus everything the wobble can add to it.
                // A built one is exactly where it was put and needs none.
                float room = SurfaceTuning.For(piece.Ground).NaturalEdge ? SurfaceNoise.Amplitude : 0.0f;

                if (Mathf.Abs(piece.MinX) + room > extent || Mathf.Abs(piece.MaxX) + room > extent
                    || Mathf.Abs(piece.MinZ) + room > extent || Mathf.Abs(piece.MaxZ) + room > extent)
                {
                    problems.Add($"Land '{piece.Name}' runs off the edge of the world.");
                }

                CheckSurface(piece, problems);
                CheckShape(piece, problems);
            }
        }

        /// <summary>
        /// Checks that a piece of land is made of something the game has heard of.
        /// </summary>
        /// <param name="piece">The rectangle to check.</param>
        /// <param name="problems">List to add to.</param>
        /// <remarks>
        /// <para>
        /// <see cref="LevelLand.Ground"/> answers <see cref="SurfaceKind.Grass"/> for a name
        /// nobody recognises, because a piece of land has to be made of something. That is
        /// the right behaviour and it is also silent, so this is where a typo gets said out
        /// loud - naming the word that was not understood, which is the only clue worth
        /// having when a road came out green.
        /// </para>
        /// <para>
        /// An empty name is not a typo. It is a rectangle written before surfaces existed,
        /// or one an editor left alone, and it means grass.
        /// </para>
        /// <para>
        /// Painting a piece of <em>land</em> with one of the two waters is a different
        /// mistake and a worse one. It reads as a lake and it is not one: drowning is
        /// decided by how low a vehicle is rather than by what it is standing on, so the
        /// rectangle would be a stretch of sea you drive across without getting wet, while
        /// <see cref="SurfaceField"/> - which does go by the surface - counts it as a hole
        /// in the island and may cut the map in two through it. The palette an editor offers
        /// is the surfaces that do not drown you, which is the same question asked early.
        /// </para>
        /// </remarks>
        private static void CheckSurface(LevelLand piece, List<string> problems)
        {
            if (SurfaceTuning.For(piece.Ground).Drowns)
            {
                problems.Add(
                    $"Land '{piece.Name}' is made of '{piece.Surface}', which is water. "
                    + "Nothing can stand on it, and it leaves a hole in the island.");
                return;
            }

            if (string.IsNullOrWhiteSpace(piece.Surface)
                || LevelNames.ToSurface(piece.Surface) != SurfaceKind.None)
            {
                return;
            }

            problems.Add(
                $"Land '{piece.Name}' is made of '{piece.Surface}', which is not a surface "
                + "this game has. It will be grass.");
        }

        /// <summary>
        /// Checks that a piece of land is cut to a shape the game has heard of.
        /// </summary>
        /// <param name="piece">The piece to check.</param>
        /// <param name="problems">List to add to.</param>
        /// <remarks>
        /// The same arrangement as <see cref="CheckSurface"/> and for the same reason.
        /// <see cref="LevelLand.Form"/> answers <see cref="LandShape.Rectangle"/> for a word
        /// nobody knows, because a piece of land has to be some shape; this is where the
        /// word that was not understood gets said out loud, which is the only clue worth
        /// having when an island came out square. An empty name is not a typo - it is a
        /// rectangle written before shapes existed.
        /// </remarks>
        private static void CheckShape(LevelLand piece, List<string> problems)
        {
            if (string.IsNullOrWhiteSpace(piece.Shape)
                || LevelNames.ToShape(piece.Shape) != LandShape.None)
            {
                return;
            }

            problems.Add(
                $"Land '{piece.Name}' is cut to '{piece.Shape}', which is not a shape this "
                + "game has. It will be a rectangle.");
        }

        /// <summary>
        /// Checks the bunkers, against the rules of whichever kind of map this is.
        /// </summary>
        /// <param name="level">The level to check.</param>
        /// <param name="problems">List to add to.</param>
        /// <remarks>
        /// A match owes both sides a bunker. A one-player map owes exactly one, and the side
        /// that has none is not a fault but the mode - see
        /// <see cref="LevelDefinition.IsSolo"/>. A map with no bunker anywhere is neither,
        /// and is reported as a match missing both, because that is what an empty file most
        /// likely is on its way to being.
        /// </remarks>
        private static void CheckBunkers(LevelDefinition level, List<string> problems)
        {
            bool solo = level.IsSolo;

            foreach (Team side in Teams.Playing)
            {
                int owned = 0;
                foreach (LevelBunker candidate in level.Bunkers)
                {
                    if (candidate != null && candidate.Side == side)
                    {
                        owned++;
                    }
                }

                if (owned > 1)
                {
                    // Caught here rather than left to the total-count check below: two
                    // bunkers on the same side reads as one played side to IsSolo, which
                    // would otherwise wave the map through as a clean one-player map while
                    // silently orphaning the second bunker at build time.
                    problems.Add($"{side} has {owned} bunkers; only one is allowed.");
                }

                LevelBunker bunker = level.BunkerFor(side);
                if (bunker == null)
                {
                    if (!solo)
                    {
                        problems.Add($"{side} has no bunker, so that side cannot deploy or win.");
                    }

                    continue;
                }

                if (!level.IsOnLand(bunker.Position, BunkerShoreMargin))
                {
                    problems.Add($"The {side} bunker is not on dry land with room around it.");
                }

                if (bunker.SupplyRadius <= 0.0f || bunker.SupplyRate <= 0.0f)
                {
                    problems.Add($"The {side} bunker resupplies nothing, so nobody can refuel at home.");
                }
            }

            int extra = level.Bunkers.Length - Teams.Playing.Length;
            if (extra > 0)
            {
                problems.Add($"The level has {extra} bunker(s) more than it has sides.");
            }
        }

        /// <summary>
        /// Checks the flag towers, against the rules of whichever kind of map this is.
        /// </summary>
        /// <param name="level">The level to check.</param>
        /// <param name="problems">List to add to.</param>
        /// <remarks>
        /// <para>
        /// In a match every side owns a flag and hunts for the other one, so both sides owe
        /// two towers and exactly one real. A one-player map inverts that for the side
        /// nobody is sitting at: it owes the towers, because they are the objective, and the
        /// side that <em>is</em> played owes none at all.
        /// </para>
        /// <para>
        /// A flag on the solo player's own side is a fault rather than harmless clutter.
        /// Nothing on a one-player map ever attacks it, so it is an objective with no
        /// opponent - a line on the HUD that can never change, and a second real flag in a
        /// scene whose whole loop is finding the first one.
        /// </para>
        /// </remarks>
        private static void CheckTowers(LevelDefinition level, List<string> problems)
        {
            bool solo = level.IsSolo;

            foreach (Team side in Teams.Playing)
            {
                List<LevelTower> towers = level.TowersFor(side);

                if (solo && level.IsPlayed(side))
                {
                    if (towers.Count > 0)
                    {
                        problems.Add(
                            $"{side} is the only side playing and has {towers.Count} tower(s). "
                            + "On a one-player map nothing ever comes for them, so they are a "
                            + "flag that cannot be lost.");
                    }

                    continue;
                }

                if (towers.Count < 2)
                {
                    problems.Add($"{side} has {towers.Count} tower(s): a decoy needs two.");
                }

                int real = 0;
                foreach (LevelTower tower in towers)
                {
                    if (tower.HoldsTheFlag)
                    {
                        real++;
                    }

                    if (!level.IsOnLand(tower.Position, ShoreMargin))
                    {
                        problems.Add($"A {side} tower is not on dry land.");
                    }
                }

                if (real != 1)
                {
                    problems.Add($"{side} has {real} real towers; it needs exactly one.");
                }

                CheckTowerSpacing(side, towers, problems);
            }
        }

        /// <summary>
        /// Checks that no single round can crack open both of a side's towers.
        /// </summary>
        /// <param name="side">Side being checked, for the message.</param>
        /// <param name="towers">That side's towers.</param>
        /// <param name="problems">List to add to.</param>
        /// <remarks>
        /// <para>
        /// Finding a flag costs ammunition now rather than a drive, so the spacing rule is
        /// about blast rather than about distance: two towers close enough to sit inside one
        /// splash would both open to a single round, and the decoy would be free again.
        /// </para>
        /// <para>
        /// This is a much weaker constraint than the one it replaces, and deliberately so.
        /// Standing in one place and shelling both towers is fine - the tank can reach 36 m
        /// and repositioning was never the price. The price is the shells, and a level
        /// cannot change that.
        /// </para>
        /// </remarks>
        private static void CheckTowerSpacing(Team side, List<LevelTower> towers, List<string> problems)
        {
            float splash = WidestSplash();
            float needed = splash * 2.0f;

            for (int a = 0; a < towers.Count; a++)
            {
                for (int b = a + 1; b < towers.Count; b++)
                {
                    float apart = Vector3.Distance(towers[a].Position, towers[b].Position);
                    if (apart <= needed)
                    {
                        problems.Add(
                            $"Two {side} towers stand {apart:0.0} m apart, which is inside twice "
                            + $"the {splash:0.#} m widest blast in the game: one round would open "
                            + "both, and the decoy would cost nothing to see through.");
                    }
                }
            }
        }

        /// <summary>
        /// Returns the largest splash radius any weapon in the game throws.
        /// </summary>
        /// <returns>The radius, in metres.</returns>
        /// <remarks>
        /// Measured off the weapon table rather than written down here, so retuning a
        /// rocket moves this rule with it instead of leaving it quietly wrong.
        /// </remarks>
        private static float WidestSplash()
        {
            float widest = 0.0f;
            foreach (VehicleKind kind in Enum.GetValues(typeof(VehicleKind)))
            {
                widest = Mathf.Max(widest, WeaponTuning.For(kind).SplashRadius);
            }

            return widest;
        }

        private static void CheckStructures(LevelDefinition level, List<string> problems)
        {
            foreach (LevelStructure structure in level.Structures)
            {
                if (structure == null || structure.Structure == StructureKind.None)
                {
                    string named = structure == null ? "(nothing)" : structure.Kind;
                    problems.Add($"'{named}' is not a kind of structure this game has.");
                    continue;
                }

                // A tower is a destructible like everything else here, but it needs a side
                // and a real-or-decoy flag, so it is placed as an objective. One scattered
                // as scenery would be a fourth pyramid belonging to nobody, standing in the
                // way of the answer the other three exist to hide.
                if (structure.Structure == StructureKind.FlagTower)
                {
                    problems.Add(
                        $"A flag tower is listed as scenery at {structure.Position}. Towers go "
                        + "in the level's tower list, where they can be given a side.");
                    continue;
                }

                // Two destructibles belong to somebody, and both have to: an emplacement
                // with no side has no enemies and a gate with no side has nobody to open
                // for, and each of them stands there doing nothing in a way that looks
                // exactly like one that is working. The check runs both ways, because a
                // tree with a side is a level saying something the game has no meaning for
                // - see Destructible.Team. Which kinds care is StructureTuning's answer
                // rather than a comparison spelled out again here.
                if (structure.NeedsASide && structure.Team == Team.None)
                {
                    problems.Add(
                        $"The {structure.Kind} at {structure.Position} is on no side, so it "
                        + "does nothing: a turret with nobody to shoot at, or a gate with "
                        + "nobody to open for. Give it Green or Brown.");
                }
                else if (!structure.NeedsASide && structure.Team != Team.None)
                {
                    problems.Add(
                        $"The {structure.Kind} at {structure.Position} is given to "
                        + $"{structure.Team}, and only a turret or a door can belong to a side.");
                }

                // A bridge is the one prop meant to be over water. Anything else standing
                // there is an authoring slip, and would be in the sea on the first frame.
                bool onLand = level.IsOnLand(structure.Position, ShoreMargin);
                if (structure.Structure == StructureKind.Bridge)
                {
                    if (onLand)
                    {
                        problems.Add(
                            $"The bridge at {structure.Position} spans dry land, which is a ramp "
                            + "to nowhere.");
                    }
                }
                else if (!onLand)
                {
                    problems.Add($"A {structure.Kind} at {structure.Position} stands in the sea.");
                }
            }

            if (level.CountOf(StructureKind.DepotFuel) == 0)
            {
                problems.Add("There is nowhere on the map to refuel away from home.");
            }

            if (level.CountOf(StructureKind.DepotAmmo) == 0)
            {
                problems.Add("There is nowhere on the map to rearm away from home.");
            }
        }

        /// <summary>
        /// Checks that the objective can be driven to and driven home from.
        /// </summary>
        /// <param name="level">The level to check.</param>
        /// <param name="problems">List to add to.</param>
        /// <remarks>
        /// Two shapes of the same rule. In a match the run that has to exist is bunker to
        /// bunker, because that is the round trip a captured flag makes. On a one-player map
        /// there is no second bunker, so the run is from the one bunker out to every enemy
        /// tower: any tower a jeep cannot reach by land is a tower that might be holding the
        /// flag, and a map whose flag is on the wrong side of the water is unwinnable in
        /// exactly the way this check exists to catch.
        /// </remarks>
        private static void CheckCrossings(LevelDefinition level, List<string> problems)
        {
            if (level.IsSolo)
            {
                CheckSoloCrossings(level, problems);
                return;
            }

            LevelBunker green = level.BunkerFor(Team.Green);
            LevelBunker brown = level.BunkerFor(Team.Brown);
            if (green == null || brown == null)
            {
                return;
            }

            if (!IsConnected(level, green.Position, brown.Position))
            {
                problems.Add(
                    "The two bunkers are not joined by dry land. Every crossing on this map can "
                    + "be destroyed, so dropping the last one would leave the flag uncapturable: "
                    + "only the jeep carries it, and the jeep cannot fly.");
            }
        }

        /// <summary>
        /// Checks that every enemy tower on a one-player map can be driven to and back from.
        /// </summary>
        /// <param name="level">The level to check.</param>
        /// <param name="problems">List to add to.</param>
        /// <remarks>
        /// Every tower rather than only the real one, and that is the point: the player
        /// cannot tell which is which until they have broken one open, so a decoy across the
        /// water is a drive nobody can make against a tower nobody can rule out.
        /// </remarks>
        private static void CheckSoloCrossings(LevelDefinition level, List<string> problems)
        {
            foreach (Team side in Teams.Playing)
            {
                if (!level.IsPlayed(side))
                {
                    continue;
                }

                Vector3 home = level.BunkerPosition(side);

                foreach (Team enemy in Teams.Playing)
                {
                    if (enemy == side)
                    {
                        continue;
                    }

                    foreach (LevelTower tower in level.TowersFor(enemy))
                    {
                        if (IsConnected(level, home, tower.Position))
                        {
                            continue;
                        }

                        problems.Add(
                            $"A {enemy} tower cannot be reached from the {side} bunker by "
                            + "land. Only the jeep carries the flag and the jeep cannot fly, "
                            + "so a tower that might be holding it has to be drivable to.");
                    }
                }
            }
        }

        private static Vector2Int ToCell(Vector3 at, float extent, int cells)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt((at.x + extent) / StepSize), 0, cells - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt((at.z + extent) / StepSize), 0, cells - 1);
            return new Vector2Int(x, z);
        }

        private static bool IsWalkable(SurfaceField field, Vector2Int cell, float extent, int cells)
        {
            if (cell.x < 0 || cell.y < 0 || cell.x >= cells || cell.y >= cells)
            {
                return false;
            }

            var at = new Vector3(
                ((cell.x + 0.5f) * StepSize) - extent,
                0.0f,
                ((cell.y + 0.5f) * StepSize) - extent);

            return field.IsLand(at);
        }

        private static int Key(Vector2Int cell, int cells) => (cell.y * cells) + cell.x;
    }
}
