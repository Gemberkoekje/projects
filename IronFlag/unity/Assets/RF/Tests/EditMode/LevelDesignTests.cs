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
        /// And neither is the ground: every piece of land is one of a pair rotated half a
        /// turn about the middle of the map.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The props have been held to this since M7 and the land never has, which was fine
        /// while the map was two rectangles and a causeway and a glance settled it. It is
        /// forty shapes now, kept in step by hand, and "neither side has a shorter run to the
        /// other's flag" is a claim about the ground a vehicle crosses at least as much as
        /// about where the towers stand. One side's approach to a bridge being open country
        /// where the other's is sand is a fifth of a jeep's speed, and nothing else on this
        /// map would notice.
        /// </para>
        /// <para>
        /// Compared as <em>written</em> rather than as realised, and the difference is
        /// deliberate: the coastline wobble is a function of position, so the two shores
        /// wander differently on purpose. That is a metre or two of coast, not a route.
        /// </para>
        /// </remarks>
        [Test]
        public void TheGroundIsTheSameMapTurnedHalfATurn()
        {
            foreach (LevelLand piece in level.Land)
            {
                Assert.That(
                    HasMirror(piece),
                    Is.True,
                    $"the land '{piece.Name}' has no opposite number, so one side is driving on "
                    + "ground the other has not got");
            }
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
        /// Every way over the channel is something somebody built, and at least one of them
        /// is land: take the built ground away and the two halves fall apart.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The first half of this is <see cref="LevelValidation"/>'s rule and would fail
        /// loudly on its own - a map whose every crossing can be shot down becomes
        /// unwinnable the moment somebody drops the last one, because only the jeep carries
        /// the flag and the jeep cannot fly. The second half is what makes the first half
        /// mean something: without it, a map whose every rectangle joined up would pass while
        /// having no water on it at all.
        /// </para>
        /// <para>
        /// "Built" is <see cref="SurfaceTuning.NaturalEdge"/> rather than a count or a
        /// position, which is what lets this survive the map being redrawn. It used to say
        /// "exactly one rectangle contains the origin", and that was a description of a
        /// causeway down the middle rather than a rule: the middle is open water now, the
        /// permanent crossings are two causeways out on the flanks, and the property worth
        /// keeping was never where they are.
        /// </para>
        /// </remarks>
        [Test]
        public void EveryWayOverIsSomethingSomebodyBuilt()
        {
            Vector3 green = level.BunkerPosition(Team.Green);
            Vector3 brown = level.BunkerPosition(Team.Brown);

            Assert.That(
                LevelValidation.IsConnected(level, green, brown),
                Is.True,
                "the two halves are not joined by land");

            var natural = new List<LevelLand>();
            int built = 0;
            foreach (LevelLand piece in level.Land)
            {
                if (SurfaceTuning.For(piece.Ground).NaturalEdge)
                {
                    natural.Add(piece);
                    continue;
                }

                built++;
            }

            Assert.That(built, Is.GreaterThan(0), "nothing on this map was built: there are no crossings");

            level.Land = natural.ToArray();
            Assert.That(
                LevelValidation.IsConnected(level, green, brown),
                Is.False,
                "the flanks are dry land: the crossings cross nothing and cost nothing");
        }

        /// <summary>
        /// Neither bunker has a straight run at the other: the shortest line between them
        /// goes through water.
        /// </summary>
        /// <remarks>
        /// The reason the middle of the channel is open. A map with a crossing on the centre
        /// line is a map where the fastest route home is also the most obvious one and there
        /// is no flank to choose - both sides drive the same corridor at each other and the
        /// rest of the island is scenery. Making the shortest line wet is what turns getting
        /// home into a decision about which crossing to commit to.
        /// </remarks>
        [Test]
        public void NeitherBunkerHasAStraightRunAtTheOther()
        {
            Vector3 green = level.BunkerPosition(Team.Green);
            Vector3 brown = level.BunkerPosition(Team.Brown);

            bool wet = false;
            const int samples = 400;
            for (int step = 0; step <= samples; step++)
            {
                Vector3 along = Vector3.Lerp(green, brown, step / (float)samples);
                if (!level.IsOnLand(along, 0.0f))
                {
                    wet = true;
                    break;
                }
            }

            Assert.That(
                wet,
                Is.True,
                "a vehicle can drive from one bunker to the other in a straight line without "
                + "getting its wheels wet, so the map has one corridor rather than a choice");
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
        /// <para>
        /// A jeep leaving a bank is a projectile until it is under the water line, and if it
        /// covers the channel in that time the bridges are decoration and the causeway is a
        /// suggestion. Measured against the real numbers - the jeep's top speed, the ground
        /// it can reach that speed on, the depth of the bank, Unity's gravity - so retuning
        /// any of them fails here rather than in a playtest six months later.
        /// </para>
        /// <para>
        /// The launch is off the best ground on the map rather than off open country, and
        /// that is not pedantry: every crossing on this map is asphalt right up to the
        /// waterline, asphalt is the one surface with a grip figure above one, and so the
        /// fastest a jeep can possibly leave a bank is faster than its own table says. A
        /// road that got quick enough to turn the narrows into a ramp would be a balance
        /// change nobody asked for, arriving through the surface table rather than through
        /// the map.
        /// </para>
        /// </remarks>
        [Test]
        public void NoJeepCanJumpTheChannel()
        {
            VehicleTuning jeep = VehicleTuning.For(VehicleKind.Jeep);
            float fall = Mathf.Abs(level.Bounds.DrownDepth);
            float airborne = Mathf.Sqrt(2.0f * fall / Mathf.Abs(Physics.gravity.y));
            float reach = jeep.MaxSpeed * FastestGround(jeep) * airborne;

            Assert.That(
                NarrowestChannel(),
                Is.GreaterThan(reach * 1.5f),
                $"a jeep clears {reach:0.0} m of water and the channel is narrower than that "
                + "with room to spare");
        }

        /// <summary>
        /// Returns the most traction any surface on this map offers one vehicle.
        /// </summary>
        /// <param name="tuning">Vehicle to ask on behalf of.</param>
        /// <returns>What to multiply its top speed by to get the fastest it can ever go here.</returns>
        /// <remarks>
        /// Walked rather than named, so that a map that grows a new surface, or a table that
        /// grows a new row, is measured rather than assumed. The waters are skipped for the
        /// obvious reason: nothing launches off a bank it is already under.
        /// </remarks>
        private float FastestGround(VehicleTuning tuning)
        {
            float best = 1.0f;
            foreach (LevelLand piece in level.Land)
            {
                SurfaceTuning surface = SurfaceTuning.For(piece.Ground);
                if (surface.Drowns)
                {
                    continue;
                }

                best = Mathf.Max(best, GroundVehicleMotion.Traction(tuning, surface));
            }

            return best;
        }

        /// <summary>
        /// There is no straight run at a crossing that does not cross soft ground, which is
        /// what the roads to the crossings are for.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The claim the map was repainted to make. A grip figure is worth nothing on a map
        /// with nowhere to feel it, and until this repaint the only sand on this one was the
        /// four metres of beach every coastline derives - ground a vehicle crosses rather
        /// than ground it drives along - so the road's 1.06 was a number no route ever chose
        /// between.
        /// </para>
        /// <para>
        /// A floor on distance rather than on time, because the arithmetic is what a level
        /// file can be held to: <em>where</em> the soft ground is and how much of it there is
        /// are the map's business, and that a straight run at a crossing meets some of it is
        /// this test's. Ten metres is about half a second of a jeep at speed - enough that
        /// the road is a question rather than scenery, and low enough that a map is free to
        /// answer the question differently.
        /// </para>
        /// </remarks>
        [Test]
        public void EveryStraightRunAtACrossingCrossesSoftGround()
        {
            const float least = 10.0f;

            foreach (Vector3 crossing in Crossings())
            {
                foreach (LevelBunker bunker in level.Bunkers)
                {
                    Assert.That(
                        SoftGroundBetween(bunker.Position, crossing),
                        Is.GreaterThan(least),
                        $"the run from the {bunker.Side} bunker to the crossing at {crossing} is "
                        + "firm the whole way, so nothing on this map makes the road to it worth "
                        + "taking");
                }
            }
        }

        /// <summary>
        /// Returns the middle of every way over the channel.
        /// </summary>
        /// <returns>One point on the centre line per crossing.</returns>
        /// <remarks>
        /// Both kinds: the built land that spans the water, which is what a causeway is, and
        /// the bridges laid across it, which are structures rather than land. Found rather
        /// than named, so a map that grows a third crossing is measured instead of assumed.
        /// </remarks>
        private List<Vector3> Crossings()
        {
            var found = new List<Vector3>();

            foreach (LevelLand piece in level.Land)
            {
                if (piece != null
                    && piece.IsDrawn
                    && !SurfaceTuning.For(piece.Ground).NaturalEdge
                    && piece.Contains(new Vector3(piece.Centre.x, 0.0f, 0.0f)))
                {
                    found.Add(new Vector3(piece.Centre.x, 0.0f, 0.0f));
                }
            }

            foreach (LevelStructure structure in level.Structures)
            {
                if (structure.Structure == StructureKind.Bridge)
                {
                    found.Add(new Vector3(structure.Position.x, 0.0f, structure.Position.z));
                }
            }

            Assert.That(found, Is.Not.Empty, "the map has no way over the channel at all");
            return found;
        }

        /// <summary>
        /// Measures how much of a straight line is over ground softer than open country.
        /// </summary>
        /// <param name="from">Where the run starts.</param>
        /// <param name="to">Where it ends.</param>
        /// <returns>Metres of it spent on ground with less grip than grass.</returns>
        /// <remarks>
        /// Water is not soft ground, it is the end of the vehicle, so it is not counted -
        /// a run that ends over the channel would otherwise pass this by drowning.
        /// </remarks>
        private float SoftGroundBetween(Vector3 from, Vector3 to)
        {
            const int samples = 400;
            float open = SurfaceTuning.For(SurfaceKind.Grass).Grip;
            float step = Vector3.Distance(from, to) / samples;
            SurfaceField field = level.Field;
            float soft = 0.0f;

            for (int at = 0; at < samples; at++)
            {
                Vector3 along = Vector3.Lerp(from, to, (at + 0.5f) / samples);
                SurfaceTuning under = SurfaceTuning.For(field.At(along));
                if (!under.Drowns && under.Grip < open)
                {
                    soft += step;
                }
            }

            return soft;
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

        /// <summary>
        /// Reports whether a piece of land has an opposite number.
        /// </summary>
        /// <param name="piece">The piece to look for a twin of.</param>
        /// <returns><c>true</c> when the map carries the same shape turned half a turn.</returns>
        /// <remarks>
        /// A piece is allowed to be its own twin, unlike a prop: a shape drawn symmetrically
        /// about the origin already is the same on both halves, and demanding a second copy
        /// of it would be demanding the map draw it twice.
        /// </remarks>
        private bool HasMirror(LevelLand piece)
        {
            foreach (LevelLand other in level.Land)
            {
                if (other.Ground == piece.Ground
                    && other.Form == piece.Form
                    && Mathf.Abs(other.MinX + piece.MaxX) < MirrorTolerance
                    && Mathf.Abs(other.MaxX + piece.MinX) < MirrorTolerance
                    && Mathf.Abs(other.MinZ + piece.MaxZ) < MirrorTolerance
                    && Mathf.Abs(other.MaxZ + piece.MinZ) < MirrorTolerance)
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
