using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using IronFlag.Editor.ArtPipeline;
using IronFlag.Levels;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// The surface table read against itself: what the map is made of, what each surface
    /// costs, and what a level file is allowed to call one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing here presses Play and nothing here builds a map. A surface is a row of
    /// numbers and a name a file may write, and both are checkable without a scene - which
    /// is the same bet <see cref="StructureRosterTests"/> and the vehicle roster make.
    /// </para>
    /// <para>
    /// The load-bearing test is <see cref="TheMapIsARampFromTheOpenSeaToTheBeach"/>. M7
    /// established that a player reads value rather than hue at thirty-four metres, and it
    /// established it by shipping a sea that failed to: five surfaces is five chances to
    /// make that mistake again, and this is the one assertion that would catch it.
    /// </para>
    /// </remarks>
    public sealed class SurfaceTests
    {
        /// <summary>
        /// The roster is the enum, minus the empty member. Everything downstream - the
        /// materials, the catalog rows, a palette an editor offers - walks this list, so a
        /// surface missing from it is a surface that exists in the file format and nowhere
        /// else.
        /// </summary>
        [Test]
        public void EverySurfaceIsOnTheRoster()
        {
            var roster = new List<SurfaceKind>(SurfaceTuning.Roster());

            foreach (SurfaceKind kind in System.Enum.GetValues(typeof(SurfaceKind)))
            {
                if (kind == SurfaceKind.None)
                {
                    continue;
                }

                Assert.That(roster, Has.Member(kind), $"{kind} is on no roster");
            }

            Assert.That(roster, Has.No.Member(SurfaceKind.None), "the empty member is not a surface");
            Assert.That(roster, Is.Unique);
        }

        /// <summary>
        /// Two surfaces the same colour are one surface as far as a player is concerned.
        /// </summary>
        [Test]
        public void NoTwoSurfacesArePaintedTheSame()
        {
            var seen = new Dictionary<Color, SurfaceKind>();

            foreach (SurfaceKind kind in SurfaceTuning.Roster())
            {
                Color colour = SurfaceTuning.For(kind).Colour;
                Assert.That(
                    seen.ContainsKey(colour),
                    Is.False,
                    $"{kind} is painted exactly like {(seen.ContainsKey(colour) ? seen[colour] : kind)}");
                seen.Add(colour, kind);
            }
        }

        /// <summary>
        /// The ordering the whole coastline rests on: open sea darkest, then the shelf, then
        /// the interior, then the road, then the beach lightest of all.
        /// </summary>
        /// <remarks>
        /// The shelf is the risky one. A pale band between the darkest thing on the map and
        /// the lightest is exactly how the first sea went wrong - a mid value in the gap
        /// where the contrast is supposed to be - so it is asserted to sit below the ground
        /// rather than merely above the sea.
        /// </remarks>
        [Test]
        public void TheMapIsARampFromTheOpenSeaToTheBeach()
        {
            float deep = Value(SurfaceKind.DeepWater);
            float shelf = Value(SurfaceKind.ShallowWater);
            float grass = Value(SurfaceKind.Grass);
            float asphalt = Value(SurfaceKind.Asphalt);
            float sand = Value(SurfaceKind.Sand);

            // A fifth is about the smallest step that survives a sunlit ground seen in half
            // a screen; anything closer is a difference of hue, which is the thing M7 proved
            // a player does not read at speed. Every step of the ramp is a boundary somebody
            // is crossing at 22 m/s, so every step is held to it rather than only the
            // coastline - the shelf was the one that failed this first, at a fifth.
            Assert.That(shelf / deep, Is.GreaterThan(1.2f), "the shelf does not read against the open sea");
            Assert.That(grass / shelf, Is.GreaterThan(1.2f), "the waterline is too subtle to read");
            Assert.That(asphalt / grass, Is.GreaterThan(1.2f), "the road is too subtle to read");
            Assert.That(sand / asphalt, Is.GreaterThan(1.2f), "the beach does not read against a road");
        }

        /// <summary>
        /// The two waters drown you and the three grounds do not, which is also the answer
        /// to what a level may paint a rectangle of land with.
        /// </summary>
        [Test]
        public void ExactlyTheTwoWatersDrownYou()
        {
            var drowning = new List<SurfaceKind>();
            foreach (SurfaceKind kind in SurfaceTuning.Roster())
            {
                if (SurfaceTuning.For(kind).Drowns)
                {
                    drowning.Add(kind);
                }
            }

            Assert.That(
                drowning,
                Is.EquivalentTo(new[] { SurfaceKind.ShallowWater, SurfaceKind.DeepWater }));
        }

        /// <summary>
        /// The road is the fastest ground and the sand is the slowest, and the road is
        /// faster than open country rather than merely equal to it.
        /// </summary>
        /// <remarks>
        /// Asphalt above 1.0 is the whole point of having roads: the fastest line across the
        /// map should be a line somebody drew, so that both players know where it is. This
        /// checks the argument; the effect on a vehicle is
        /// <see cref="IronFlag.Vehicles.GroundVehicleMotion.Traction"/> and is measured in
        /// <c>GroundVehicleMotionTests</c>.
        /// </remarks>
        [Test]
        public void TheRoadIsFasterThanTheCountryAndTheSandIsSlower()
        {
            float grass = SurfaceTuning.For(SurfaceKind.Grass).Grip;
            float asphalt = SurfaceTuning.For(SurfaceKind.Asphalt).Grip;
            float sand = SurfaceTuning.For(SurfaceKind.Sand).Grip;

            Assert.That(grass, Is.EqualTo(1.0f), "grass is the surface everything else is read against");
            Assert.That(asphalt, Is.GreaterThan(grass), "a road nobody gains anything from is scenery");
            Assert.That(sand, Is.LessThan(grass), "sand costs nothing, so the road buys nothing");

            // Thirst runs the other way, or the fast route is also the cheap route and there
            // is no decision left in taking the slow one.
            Assert.That(SurfaceTuning.For(SurfaceKind.Asphalt).FuelDraw, Is.LessThan(1.0f));
            Assert.That(SurfaceTuning.For(SurfaceKind.Sand).FuelDraw, Is.GreaterThan(1.0f));
        }

        /// <summary>
        /// Nothing a vehicle can be standing on has an opinion the model cannot use: no
        /// negative grip, no free fuel, and nothing that would run a tank backwards.
        /// </summary>
        /// <remarks>
        /// The two waters are checked as well, and deliberately. They drown you, so their
        /// handling numbers should never reach anybody - but a vehicle is over water for the
        /// fraction of a second between leaving a bank and being taken off the field, and it
        /// is asking this table what it is standing on the whole time. A row with a zero grip
        /// in it would stop a jeep dead on the waterline instead of letting it sail off.
        /// </remarks>
        [Test]
        public void NoSurfaceHasHandlingNumbersAVehicleCannotUse()
        {
            foreach (SurfaceKind kind in SurfaceTuning.Roster())
            {
                SurfaceTuning surface = SurfaceTuning.For(kind);

                Assert.That(surface.Grip, Is.GreaterThan(0.0f), $"{kind} stops a vehicle dead");
                Assert.That(surface.Grip, Is.LessThan(2.0f), $"{kind} doubles what a vehicle can do");
                Assert.That(surface.FuelDraw, Is.GreaterThan(0.0f), $"{kind} is free to drive on");
                Assert.That(surface.FuelDraw, Is.LessThan(2.0f), $"{kind} halves every vehicle's range");
            }
        }

        /// <summary>
        /// Neither water is an opinion about handling: the sea is a thing you drown in, not
        /// a thing you drive slowly across.
        /// </summary>
        /// <remarks>
        /// If either water ever wants a grip figure it will be because
        /// <see cref="SurfaceTuning.Drowns"/> stopped being true of it, which is the
        /// amphibious jeep and a different pass. Until then a number here would be a rule
        /// nobody could ever see the effect of, which is the worst kind to leave in a table.
        /// </remarks>
        [Test]
        public void NeitherWaterHasAnOpinionAboutHandling()
        {
            foreach (SurfaceKind kind in SurfaceTuning.Roster())
            {
                SurfaceTuning surface = SurfaceTuning.For(kind);
                if (!surface.Drowns)
                {
                    continue;
                }

                Assert.That(surface.Grip, Is.EqualTo(1.0f), $"{kind} handles unlike open country");
                Assert.That(surface.FuelDraw, Is.EqualTo(1.0f), $"{kind} costs unlike open country");
            }
        }

        /// <summary>
        /// Asphalt is the only surface whose edges are kept exactly as written, and that is
        /// load-bearing rather than decorative.
        /// </summary>
        /// <remarks>
        /// It is what will keep a 16 m causeway 16 m wide and the 13 m channel at each
        /// bridgehead exact once Phase C starts pushing coastlines around with noise. A
        /// bridgehead that became a natural edge would move the narrows, and the narrows are
        /// measured by a test that computes the jeep's ballistic jump from its real top speed.
        /// </remarks>
        [Test]
        public void OnlyWhatSomebodyBuiltKeepsItsExactEdges()
        {
            foreach (SurfaceKind kind in SurfaceTuning.Roster())
            {
                Assert.That(
                    SurfaceTuning.For(kind).NaturalEdge,
                    Is.EqualTo(kind != SurfaceKind.Asphalt),
                    $"{kind} disagrees with the rule that built surfaces keep their edges");
            }
        }

        /// <summary>
        /// Exactly two surfaces are something else at their edges: open country is a beach
        /// where it meets the water, and the open sea is a shelf where it meets the land.
        /// </summary>
        /// <remarks>
        /// One column rather than a beach rule and a shelf rule, which is what lets
        /// <see cref="SurfaceField"/> derive both in one pass. The rest of the roster has to
        /// say so by rimming itself with nothing: a road that grew a beach would lose the
        /// hard edges that are the whole reason it reads as built.
        /// </remarks>
        [Test]
        public void OnlyTheInteriorAndTheOpenSeaAreSomethingElseAtTheirEdges()
        {
            var rimmed = new Dictionary<SurfaceKind, SurfaceKind>();

            foreach (SurfaceKind kind in SurfaceTuning.Roster())
            {
                SurfaceTuning surface = SurfaceTuning.For(kind);

                if (surface.RimSurface == SurfaceKind.None)
                {
                    Assert.That(
                        surface.RimWidth,
                        Is.EqualTo(0.0f),
                        $"{kind} gives up {surface.RimWidth} m of its coast to nothing at all");
                    continue;
                }

                Assert.That(surface.RimWidth, Is.GreaterThan(0.0f), $"{kind} names a rim it never draws");
                rimmed.Add(kind, surface.RimSurface);
            }

            Assert.That(rimmed.Keys, Is.EquivalentTo(new[] { SurfaceKind.Grass, SurfaceKind.DeepWater }));
            Assert.That(rimmed[SurfaceKind.Grass], Is.EqualTo(SurfaceKind.Sand), "the interior has no beach");
            Assert.That(
                rimmed[SurfaceKind.DeepWater],
                Is.EqualTo(SurfaceKind.ShallowWater),
                "the sea has no shelf, so an island would sit on the water rather than in it");
        }

        /// <summary>
        /// Neither rim rims itself, and the two are the right sizes read against each other.
        /// </summary>
        /// <remarks>
        /// A rim that rimmed itself would eat an island a few metres at a time - the field
        /// only runs the rule once, so this is the table saying the same thing rather than
        /// leaning on that. The widths are the two numbers in the table most likely to be
        /// wrong, and they are held apart rather than pinned: what matters is that the beach
        /// stays too narrow to be a lane and the shelf stays wide enough to be a band.
        /// </remarks>
        [Test]
        public void NeitherRimRimsItself()
        {
            SurfaceKind[] rims = SurfaceTuning.Rims();
            Assert.That(rims, Is.EquivalentTo(new[] { SurfaceKind.Sand, SurfaceKind.ShallowWater }));

            foreach (SurfaceKind rim in rims)
            {
                Assert.That(
                    SurfaceTuning.For(rim).RimSurface,
                    Is.EqualTo(SurfaceKind.None),
                    $"{rim} rims itself, which would put a beach around a beach");
            }

            float beach = SurfaceTuning.For(SurfaceKind.Grass).RimWidth;
            float shelf = SurfaceTuning.For(SurfaceKind.DeepWater).RimWidth;

            Assert.That(beach, Is.LessThan(8.0f), "a beach that wide is a road made of sand");
            Assert.That(
                shelf,
                Is.GreaterThan(beach),
                "the shelf is the band that does the work and it is the narrower of the two");
        }

        /// <summary>
        /// Every surface has its own place in the stack, and the stack reads from the bottom
        /// of the sea to the top of the road.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The map is drawn as a stack of sheets rather than as one sheet cut into pieces,
        /// and two surfaces sharing a layer would be two sheets at the same height fighting
        /// over which of them the depth buffer likes today. It is also the order a reader
        /// should be able to take the column in: sea, shelf, beach, country, road - out from
        /// the deep water and up onto the land, which is the same order the palette is
        /// argued in.
        /// </para>
        /// <para>
        /// The two ends are the ones that carry a rule rather than a preference. The bottom
        /// is the open sea because it is the one sheet that is not cut to a shape, so no gap
        /// anywhere on the map can show anything that is not water. The top is whatever was
        /// built, because a road is the last thing laid down on a map - which is both how it
        /// is drawn and how it got there.
        /// </para>
        /// </remarks>
        [Test]
        public void EverySurfaceHasItsOwnPlaceInTheStack()
        {
            var taken = new HashSet<int>();
            int deepest = int.MaxValue;
            int highest = int.MinValue;
            int highestWater = int.MinValue;
            int lowestGround = int.MaxValue;
            SurfaceKind bottom = SurfaceKind.None;
            SurfaceKind top = SurfaceKind.None;

            foreach (SurfaceKind kind in SurfaceTuning.Roster())
            {
                SurfaceTuning surface = SurfaceTuning.For(kind);
                Assert.That(taken.Add(surface.Layer), Is.True, $"{kind} shares a layer with another surface");

                if (surface.Layer < deepest)
                {
                    deepest = surface.Layer;
                    bottom = kind;
                }

                if (surface.Layer > highest)
                {
                    highest = surface.Layer;
                    top = kind;
                }

                if (surface.Drowns)
                {
                    highestWater = Mathf.Max(highestWater, surface.Layer);
                }
                else
                {
                    lowestGround = Mathf.Min(lowestGround, surface.Layer);
                }
            }

            Assert.That(
                bottom,
                Is.EqualTo(SurfaceKind.DeepWater),
                "something other than the open sea is at the bottom of the stack, so a gap "
                + "between two sheets could show it");
            Assert.That(
                SurfaceTuning.For(top).NaturalEdge,
                Is.False,
                "the top of the stack is not something somebody built");
            Assert.That(
                highestWater,
                Is.LessThan(lowestGround),
                "the water and the ground are shuffled together, so the column cannot be "
                + "read as one list from the sea bed upward");
        }

        /// <summary>
        /// Every bank is its own surface taken down a step, and no bank is a colour of its
        /// own.
        /// </summary>
        /// <remarks>
        /// The coastline is a wall now rather than the side of a box, so it needs a colour -
        /// and deriving it is what stops the map growing one the palette has never been read
        /// against. Only the ground has a bank: the coast is the land's edge, and the water
        /// has none of its own to hang one from.
        /// </remarks>
        [Test]
        public void EveryBankIsTheSurfaceAboveItTakenDownAStep()
        {
            Assert.That(SurfaceTuning.BankShade, Is.GreaterThan(0.0f).And.LessThan(1.0f));

            foreach (SurfaceKind kind in SurfaceTuning.Stack(false))
            {
                SurfaceTuning surface = SurfaceTuning.For(kind);

                Assert.That(
                    Value(surface.Bank),
                    Is.LessThan(Value(surface.Colour)),
                    $"{kind}'s bank is no darker than the ground standing on it");
                Assert.That(surface.Bank.r, Is.EqualTo(surface.Colour.r * SurfaceTuning.BankShade).Within(0.001f));
                Assert.That(surface.Bank.g, Is.EqualTo(surface.Colour.g * SurfaceTuning.BankShade).Within(0.001f));
                Assert.That(surface.Bank.b, Is.EqualTo(surface.Colour.b * SurfaceTuning.BankShade).Within(0.001f));
            }
        }

        /// <summary>
        /// Every surface a map is walked on has a bank material painted the colour the table
        /// says.
        /// </summary>
        /// <remarks>
        /// The same argument as <see cref="EverySurfaceHasAMaterialPaintedTheColourTheTableSays"/>,
        /// and the same trap: the catalog does not refresh itself, so a colour changed in
        /// the table and never rebuilt is a coastline still wearing last week's. Running
        /// this fixes it as well as reporting it.
        /// </remarks>
        [Test]
        public void EveryGroundSurfaceHasABankPaintedTheColourTheTableSays()
        {
            GeneratedMaterials.EnsureAssets();

            foreach (SurfaceKind kind in SurfaceTuning.Stack(false))
            {
                string name = GeneratedMaterials.BankMaterial(kind);
                Material material = GeneratedMaterials.Load(name);
                Assert.That(material, Is.Not.Null, $"{kind} has no bank at {GeneratedMaterials.PathOf(name)}");

                Color painted = material.GetColor("_BaseColor");
                Color wanted = SurfaceTuning.For(kind).Bank;
                string stale = $"{kind}'s bank was not rebuilt after the table changed";
                Assert.That(painted.r, Is.EqualTo(wanted.r).Within(0.002f), stale);
                Assert.That(painted.g, Is.EqualTo(wanted.g).Within(0.002f), stale);
                Assert.That(painted.b, Is.EqualTo(wanted.b).Within(0.002f), stale);
            }

            // And the waters have none, which is a row that is right rather than short.
            // Asked of the folder rather than of the loader, which says so out loud when it
            // is handed the name of something nobody generated.
            foreach (SurfaceKind kind in SurfaceTuning.Stack(true))
            {
                Assert.That(
                    File.Exists(GeneratedMaterials.PathOf(GeneratedMaterials.BankMaterial(kind))),
                    Is.False,
                    $"{kind} has a bank, and the sea has no coastline of its own");
            }
        }

        /// <summary>
        /// A shape survives being written to a file and read back, and it is written as a
        /// name.
        /// </summary>
        /// <remarks>
        /// The same rule as every other word in the format, for the same reason: a file that
        /// said <c>"Shape": 2</c> could not be reviewed in a diff and would mean something
        /// else the day a row is inserted into <see cref="LandShape"/>.
        /// </remarks>
        [Test]
        public void AShapeSurvivesTheRoundTrip()
        {
            var level = new LevelDefinition
            {
                Name = "Rounded",
                Seed = 17,
                Land = new[]
                {
                    new LevelLand
                    {
                        Name = "Island",
                        Surface = nameof(SurfaceKind.Grass),
                        Shape = nameof(LandShape.Ellipse),
                        MinX = -30,
                        MaxX = 30,
                        MinZ = -20,
                        MaxZ = 20,
                    },
                },
            };

            string json = LevelFile.ToJson(level);

            StringAssert.Contains("\"Ellipse\"", json);
            Assert.That(
                json.Contains($"\"Shape\": {(int)LandShape.Ellipse}"),
                Is.False,
                "a shape was written as the number behind it");

            Assert.That(
                LevelFile.TryParse(json, "round trip", out LevelDefinition copy, out string problem),
                Is.True,
                problem);
            Assert.That(copy.Land[0].Form, Is.EqualTo(LandShape.Ellipse));
            Assert.That(copy.Seed, Is.EqualTo(17), "the seed did not survive, so neither did the coastline");
        }

        /// <summary>
        /// A level file naming a shape nobody has heard of gets a rectangle rather than a
        /// hole in the world.
        /// </summary>
        /// <remarks>
        /// Exactly the arrangement <see cref="AnUnknownSurfaceIsGrassRatherThanNothing"/>
        /// describes, one field along. <see cref="LevelNames"/> keeps its one rule and
        /// answers with the empty member; the piece of land, which has to be cut to
        /// something, falls back to the shape every map before this one meant.
        /// </remarks>
        [Test]
        public void AnUnknownShapeIsARectangleRatherThanNothing()
        {
            Assert.That(LevelNames.ToShape("Trapezium"), Is.EqualTo(LandShape.None));
            Assert.That(LevelNames.ToShape(string.Empty), Is.EqualTo(LandShape.None));
            Assert.That(LevelNames.ToShape("2"), Is.EqualTo(LandShape.None), "a number was read as a shape");
            Assert.That(LevelNames.ToShape("ellipse"), Is.EqualTo(LandShape.Ellipse), "names are case-sensitive");

            var piece = new LevelLand { Name = "Odd", Shape = "Trapezium", MaxX = 10, MaxZ = 10 };
            Assert.That(piece.Form, Is.EqualTo(LandShape.Rectangle));

            var blank = new LevelLand { Name = "Old", Shape = string.Empty, MaxX = 10, MaxZ = 10 };
            Assert.That(blank.Form, Is.EqualTo(LandShape.Rectangle), "a map written before shapes existed");
        }

        /// <summary>
        /// A misspelled surface costs a colour, not a hole in the map - and every rectangle
        /// written before surfaces existed keeps working untouched.
        /// </summary>
        [Test]
        public void AnUnknownSurfaceIsGrassRatherThanNothing()
        {
            Assert.That(LevelNames.ToSurface("Gravel"), Is.EqualTo(SurfaceKind.None));
            Assert.That(LevelNames.ToSurface(string.Empty), Is.EqualTo(SurfaceKind.None));

            // Case is forgiven, because a person is typing this.
            Assert.That(LevelNames.ToSurface("asphalt"), Is.EqualTo(SurfaceKind.Asphalt));
            Assert.That(LevelNames.ToSurface(" Sand "), Is.EqualTo(SurfaceKind.Sand));

            // A number where a name belongs is refused, as it is everywhere else in the
            // format: Enum.TryParse would hand back whatever member sits at that value.
            Assert.That(LevelNames.ToSurface("3"), Is.EqualTo(SurfaceKind.None));

            Assert.That(new LevelLand().Ground, Is.EqualTo(SurfaceKind.Grass), "the default is grass");
            Assert.That(
                new LevelLand { Surface = "Gravel" }.Ground,
                Is.EqualTo(SurfaceKind.Grass),
                "a typo left a piece of land made of nothing");
            Assert.That(
                new LevelLand { Surface = string.Empty }.Ground,
                Is.EqualTo(SurfaceKind.Grass),
                "a rectangle from before surfaces existed stopped working");
        }

        /// <summary>
        /// A surface survives being written out and read back, which is the claim the whole
        /// format stands on and the one the in-game editor saves through.
        /// </summary>
        [Test]
        public void ASurfaceSurvivesTheRoundTrip()
        {
            var level = new LevelDefinition
            {
                Name = "Surfaced",
                Land = new[]
                {
                    new LevelLand
                    {
                        Name = "Road",
                        Surface = nameof(SurfaceKind.Asphalt),
                        MinX = -8,
                        MaxX = 8,
                        MinZ = -20,
                        MaxZ = 20,
                    },
                },
            };

            string json = LevelFile.ToJson(level);

            StringAssert.Contains("\"Asphalt\"", json);
            Assert.That(
                json.Contains($"\"Surface\": {(int)SurfaceKind.Asphalt}"),
                Is.False,
                "a surface was written as the number behind it");

            Assert.That(
                LevelFile.TryParse(json, "round trip", out LevelDefinition copy, out string problem),
                Is.True,
                problem);
            Assert.That(copy.Land[0].Ground, Is.EqualTo(SurfaceKind.Asphalt));
        }

        /// <summary>
        /// The map the game ships is no longer one colour, and none of its land is painted
        /// with something you would drown in.
        /// </summary>
        /// <remarks>
        /// Written against whatever level is shipped rather than against Iron Channel by
        /// name, the way <see cref="LevelDesignTests"/> is: point the game at a different
        /// default map and this checks the new one.
        /// </remarks>
        [Test]
        public void TheShippedMapIsMadeOfMoreThanOneThing()
        {
            string path = LevelLibrary.ShippedPathFor(LevelLibrary.DefaultLevel);
            Assert.That(File.Exists(path), Is.True, $"the game ships no map at {path}");
            Assert.That(LevelFile.TryRead(path, out LevelDefinition level, out string problem), Is.True, problem);

            var painted = new HashSet<SurfaceKind>();
            foreach (LevelLand piece in level.Land)
            {
                Assert.That(
                    LevelNames.ToSurface(piece.Surface),
                    Is.Not.EqualTo(SurfaceKind.None),
                    $"land '{piece.Name}' is made of '{piece.Surface}', which is not a surface");
                Assert.That(
                    SurfaceTuning.For(piece.Ground).Drowns,
                    Is.False,
                    $"land '{piece.Name}' is painted with water");

                painted.Add(piece.Ground);
            }

            Assert.That(
                painted.Count,
                Is.GreaterThan(1),
                "the whole map is one surface again, which is the harbour this pass exists to undo");
        }

        /// <summary>
        /// Every surface has a material of its own on disk, painted the colour the table
        /// says.
        /// </summary>
        /// <remarks>
        /// The gotcha this exists for: the generated materials are assets, so changing a
        /// number in the table changes nothing until they are regenerated, and the symptom
        /// is a map that renders in yesterday's colours with no error anywhere. Running
        /// <see cref="GeneratedMaterials.EnsureAssets"/> here means the next render is right
        /// even if somebody forgot the menu item.
        /// </remarks>
        [Test]
        public void EverySurfaceHasAMaterialPaintedTheColourTheTableSays()
        {
            GeneratedMaterials.EnsureAssets();

            var names = new HashSet<string>();
            foreach (SurfaceKind kind in SurfaceTuning.Roster())
            {
                string name = GeneratedMaterials.SurfaceMaterial(kind);
                Assert.That(names.Add(name), Is.True, $"{kind} shares a material with another surface");

                Material material = GeneratedMaterials.Load(name);
                Assert.That(material, Is.Not.Null, $"{kind} has no material at {GeneratedMaterials.PathOf(name)}");

                Color painted = material.GetColor("_BaseColor");
                Color wanted = SurfaceTuning.For(kind).Colour;
                string stale = $"{kind}'s material was not rebuilt after the table changed";
                Assert.That(painted.r, Is.EqualTo(wanted.r).Within(0.002f), stale);
                Assert.That(painted.g, Is.EqualTo(wanted.g).Within(0.002f), stale);
                Assert.That(painted.b, Is.EqualTo(wanted.b).Within(0.002f), stale);
            }

            // The sea already had a material and keeps it, so that the water a vehicle
            // drowns in and the water the map is painted with cannot become two colours.
            Assert.That(
                GeneratedMaterials.SurfaceMaterial(SurfaceKind.DeepWater),
                Is.EqualTo(GeneratedMaterials.Water));
        }

        /// <summary>
        /// How bright a surface is written to be, on the scale the eye compares.
        /// </summary>
        /// <param name="kind">Surface to weigh.</param>
        /// <returns>Its brightness, between nought and one.</returns>
        /// <remarks>
        /// Rec. 709 luminance of the colour as written, which is already gamma-encoded
        /// because that is what URP takes as a base colour. No sun and no exposure: this
        /// weighs the <em>palette</em>, which is the thing a test can hold still. What the
        /// map actually comes out at is measured off the still and recorded against each row
        /// of <see cref="SurfaceTuning"/> - it is close to this and not identical to it,
        /// because smoothness moves it too.
        /// </remarks>
        /// <summary>
        /// Measures how bright a colour is, on the scale a player reads.
        /// </summary>
        /// <param name="colour">The colour.</param>
        /// <returns>Its Rec. 709 luminance.</returns>
        private static float Value(Color colour)
            => (0.2126f * colour.r) + (0.7152f * colour.g) + (0.0722f * colour.b);

        private static float Value(SurfaceKind kind)
        {
            Color colour = SurfaceTuning.For(kind).Colour;
            return (0.2126f * colour.r) + (0.7152f * colour.g) + (0.0722f * colour.b);
        }
    }
}
