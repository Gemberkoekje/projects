using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Destruction;
using IronFlag.Levels;
using IronFlag.Objective;
using IronFlag.Vehicles;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// The shipped map, read as a design rather than as a file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every one of these is a mistake that would look completely normal in the editor. Two
    /// towers a metre too close and the decoy is a formality. A depot on the wrong flank and
    /// one side is playing a different game. A channel a metre too narrow and the jeep skips
    /// the bridge the whole map is arranged around. A causeway that is not actually the only
    /// indestructible crossing, and a match can be made unwinnable by shooting one prop.
    /// </para>
    /// <para>
    /// They are also the template for the next map. A second level that passes these is a
    /// level somebody can play.
    /// </para>
    /// </remarks>
    public sealed class LevelDesignTests
    {
        /// <summary>Metres a pair of props may differ by and still count as mirrored.</summary>
        private const float MirrorTolerance = 0.01f;

        private LevelDefinition level;

        /// <summary>
        /// Reads the shipped map before every test.
        /// </summary>
        [SetUp]
        public void ReadTheLevel()
        {
            string path = LevelLibrary.ShippedPathFor(LevelLibrary.DefaultLevel);
            Assert.That(File.Exists(path), Is.True, $"the game ships no map at {path}");
            Assert.That(
                LevelFile.TryRead(path, out level, out string problem), Is.True, problem);
        }

        /// <summary>
        /// The map is playable at all: everything <see cref="LevelValidation"/> insists on.
        /// </summary>
        [Test]
        public void TheShippedMapIsPlayable()
        {
            List<string> problems = LevelValidation.Problems(level);
            Assert.That(problems, Is.Empty, string.Join("; ", problems));
            Assert.That(level.Name, Is.Not.Empty);
            Assert.That(
                level.Description,
                Is.Not.Empty,
                "a level file has no comments, so the description is the only place it can "
                + "explain itself");
        }

        /// <summary>
        /// Neither side is looking at a different game: every prop is one of a pair rotated
        /// half a turn about the middle of the map.
        /// </summary>
        [Test]
        public void EveryPropOnTheMapHasAMirrorImage()
        {
            foreach (LevelStructure structure in level.Structures)
            {
                Assert.That(
                    HasMirror(structure),
                    Is.True,
                    $"the {structure.Structure} at {structure.Position} stands where the other "
                    + "side has nothing");
            }

            foreach (LevelTower tower in level.Towers)
            {
                Assert.That(
                    HasMirror(tower),
                    Is.True,
                    $"the {tower.Side} tower at {tower.Position} has no opposite number");
            }

            Assert.That(
                level.BunkerPosition(Team.Green),
                Is.EqualTo(-level.BunkerPosition(Team.Brown)),
                "the two bunkers are not opposite each other");
        }

        /// <summary>
        /// Both sides have exactly the same distance to run for the other's flag.
        /// </summary>
        [Test]
        public void NeitherSideHasAShorterRaid()
        {
            float green = Vector3.Distance(
                level.BunkerPosition(Team.Green), RealTower(Team.Brown).Position);
            float brown = Vector3.Distance(
                level.BunkerPosition(Team.Brown), RealTower(Team.Green).Position);

            Assert.That(green, Is.EqualTo(brown).Within(0.01f), "one side has a shorter raid");
            Assert.That(green, Is.GreaterThan(100.0f), "the flag is within a few seconds of home");
        }

        /// <summary>
        /// One round cannot open both of a side's towers, which is the only thing keeping
        /// the decoy a decision rather than scenery.
        /// </summary>
        [Test]
        public void ADecoyCannotBeOpenedAlongsideItsTwin()
        {
            foreach (Team side in new[] { Team.Green, Team.Brown })
            {
                List<LevelTower> towers = level.TowersFor(side);
                Assert.That(towers.Count, Is.EqualTo(2), $"{side} does not have a pair of towers");

                float apart = Vector3.Distance(towers[0].Position, towers[1].Position);
                Assert.That(
                    apart,
                    Is.GreaterThan(WeaponTuning.For(VehicleKind.Asv).SplashRadius * 2.0f),
                    $"{side}'s towers are close enough for one rocket to open both");
            }
        }

        /// <summary>
        /// Each half of the map has one fuel depot and one ammunition depot, as the design
        /// document's map section asks for.
        /// </summary>
        [Test]
        public void EachHalfHasSomewhereToRefuelAndSomewhereToRearm()
        {
            foreach (float towards in new[] { -1.0f, 1.0f })
            {
                Assert.That(
                    CountIn(StructureKind.DepotFuel, towards),
                    Is.EqualTo(1),
                    "a half of the map has the wrong number of fuel depots");
                Assert.That(
                    CountIn(StructureKind.DepotAmmo, towards),
                    Is.EqualTo(1),
                    "a half of the map has the wrong number of ammunition depots");
            }
        }

        /// <summary>
        /// The causeway is what stops the map from being able to deadlock: take it away and
        /// the only crossings left are ones a tank can shoot down.
        /// </summary>
        /// <remarks>
        /// The first half of this is <see cref="LevelValidation"/>'s rule and would fail
        /// loudly on its own. The second half is what makes the first half mean something:
        /// without it, a map whose every rectangle joined up would pass while having no water
        /// on it at all.
        /// </remarks>
        [Test]
        public void TheOnlyCrossingNobodyCanDestroyIsTheCauseway()
        {
            Vector3 green = level.BunkerPosition(Team.Green);
            Vector3 brown = level.BunkerPosition(Team.Brown);

            Assert.That(
                LevelValidation.IsConnected(level, green, brown),
                Is.True,
                "the two halves are not joined by land");

            var withoutCauseway = new List<LevelLand>();
            int removed = 0;
            foreach (LevelLand piece in level.Land)
            {
                if (piece.Contains(Vector3.zero))
                {
                    removed++;
                    continue;
                }

                withoutCauseway.Add(piece);
            }

            Assert.That(removed, Is.EqualTo(1), "the middle of the map is not one piece of land");

            level.Land = withoutCauseway.ToArray();
            Assert.That(
                LevelValidation.IsConnected(level, green, brown),
                Is.False,
                "the flanks are dry land: the bridges cross nothing and cost nothing");
        }

        /// <summary>
        /// The bridges span water rather than standing on the bank, and each one sits where
        /// the channel pinches in.
        /// </summary>
        [Test]
        public void BothBridgesStandInTheNarrowsTheySpan()
        {
            int bridges = 0;
            float widest = WidestChannel();

            foreach (LevelStructure structure in level.Structures)
            {
                if (structure.Structure != StructureKind.Bridge)
                {
                    continue;
                }

                bridges++;
                Assert.That(
                    level.IsOnLand(structure.Position, 0.0f),
                    Is.False,
                    "a bridge stands on dry land");
                Assert.That(
                    structure.Position.y,
                    Is.LessThan(0.0f),
                    "a bridge sits on the bank rather than sunk to its deck, so its deck is a "
                    + "step nothing can drive up");
                Assert.That(
                    ChannelAt(structure.Position.x),
                    Is.LessThan(widest),
                    "a bridge spans the channel at its full width rather than at a narrows");
            }

            Assert.That(bridges, Is.EqualTo(2), "the map does not have a bridge on each flank");
        }

        /// <summary>
        /// Nowhere on the map can a jeep at full speed clear the water, which is what makes
        /// the crossings the crossings.
        /// </summary>
        /// <remarks>
        /// A jeep leaving a bank is a projectile until it is under the water line, and if it
        /// covers the channel in that time the bridges are decoration and the causeway is a
        /// suggestion. Measured against the real numbers - the jeep's top speed, the depth
        /// of the bank, Unity's gravity - so retuning any of the three fails here rather
        /// than in a playtest six months later.
        /// </remarks>
        [Test]
        public void NoJeepCanJumpTheChannel()
        {
            float fall = Mathf.Abs(level.Bounds.DrownDepth);
            float airborne = Mathf.Sqrt(2.0f * fall / Mathf.Abs(Physics.gravity.y));
            float reach = VehicleTuning.For(VehicleKind.Jeep).MaxSpeed * airborne;

            Assert.That(
                NarrowestChannel(),
                Is.GreaterThan(reach * 1.5f),
                $"a jeep clears {reach:0.0} m of water and the channel is narrower than that "
                + "with room to spare");
        }

        /// <summary>
        /// Nothing is placed on top of anything else that matters.
        /// </summary>
        [Test]
        public void NothingIsParkedOnTheObjective()
        {
            foreach (LevelStructure structure in level.Structures)
            {
                foreach (LevelTower tower in level.Towers)
                {
                    Assert.That(
                        Vector3.Distance(structure.Position, tower.Position),
                        Is.GreaterThan(FlagRules.PickupRadius * 2.0f),
                        $"a {structure.Structure} is standing on a {tower.Side} tower");
                }

                foreach (LevelBunker bunker in level.Bunkers)
                {
                    Assert.That(
                        Vector3.Distance(structure.Position, bunker.Position),
                        Is.GreaterThan(bunker.SupplyRadius),
                        $"a {structure.Structure} is inside the {bunker.Side} bunker's apron");
                }
            }
        }

        private LevelTower RealTower(Team side)
        {
            foreach (LevelTower tower in level.TowersFor(side))
            {
                if (tower.HoldsTheFlag)
                {
                    return tower;
                }
            }

            Assert.Fail($"{side} has no real tower");
            return null;
        }

        private int CountIn(StructureKind kind, float towards)
        {
            int found = 0;
            foreach (LevelStructure structure in level.Structures)
            {
                if (structure.Structure == kind && Mathf.Sign(structure.Position.z) == towards)
                {
                    found++;
                }
            }

            return found;
        }

        private bool HasMirror(LevelStructure structure)
        {
            foreach (LevelStructure other in level.Structures)
            {
                if (other != structure
                    && other.Structure == structure.Structure
                    && Vector3.Distance(other.Position, Opposite(structure.Position))
                        < MirrorTolerance)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasMirror(LevelTower tower)
        {
            foreach (LevelTower other in level.Towers)
            {
                if (other != tower
                    && other.Side != tower.Side
                    && other.HoldsTheFlag == tower.HoldsTheFlag
                    && Vector3.Distance(other.Position, Opposite(tower.Position)) < MirrorTolerance)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Turns a point half a turn about the middle of the map.
        /// </summary>
        /// <param name="at">The point.</param>
        /// <returns>Where its opposite number stands.</returns>
        private static Vector3 Opposite(Vector3 at) => new Vector3(-at.x, at.y, -at.z);

        /// <summary>
        /// Measures the water between the two halves at one place along the map.
        /// </summary>
        /// <param name="x">Where across the map to measure, in metres.</param>
        /// <returns>Metres of water, or zero where the two halves are joined.</returns>
        /// <remarks>
        /// Walks out from the middle in both directions until it reaches land, which is the
        /// crossing a vehicle heading straight over would actually have to make.
        /// </remarks>
        private float ChannelAt(float x)
        {
            const float step = 0.25f;
            float extent = level.Bounds.HalfExtent;

            if (level.IsOnLand(new Vector3(x, 0.0f, 0.0f), 0.0f))
            {
                return 0.0f;
            }

            float south = 0.0f;
            while (south > -extent && !level.IsOnLand(new Vector3(x, 0.0f, south), 0.0f))
            {
                south -= step;
            }

            float north = 0.0f;
            while (north < extent && !level.IsOnLand(new Vector3(x, 0.0f, north), 0.0f))
            {
                north += step;
            }

            // Open sea rather than a channel: nothing stopped the walk on one side, so this
            // is off the end of the island and there is no crossing here to measure.
            if (south <= -extent || north >= extent)
            {
                return 0.0f;
            }

            return north - south;
        }

        private float NarrowestChannel() => AcrossTheMap(false);

        private float WidestChannel() => AcrossTheMap(true);

        private float AcrossTheMap(bool widest)
        {
            float found = widest ? 0.0f : float.MaxValue;

            for (float x = -level.Bounds.HalfExtent; x <= level.Bounds.HalfExtent; x += 0.5f)
            {
                float gap = ChannelAt(x);
                if (gap <= 0.0f)
                {
                    continue;
                }

                found = widest ? Mathf.Max(found, gap) : Mathf.Min(found, gap);
            }

            return found;
        }
    }
}
