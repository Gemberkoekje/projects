using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using IronFlag.Destruction;
using IronFlag.Levels;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// The map as it is really made rather than as it was drawn: the beach and the shelf
    /// nobody authored, and the coastline everything else measures itself against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="SurfaceTests"/> on purpose. That file argues the table - what
    /// a surface is worth and what it is painted - and this one checks what happens when a
    /// map is put through it. The two fail for completely different reasons: a broken row is
    /// a balance mistake, and a broken field is a bunker in the sea.
    /// </para>
    /// <para>
    /// Most of these are built on a plain square island rather than on the shipped map,
    /// because a rim four metres wide is only checkable against a coastline somebody can do
    /// the arithmetic for in their head. The shipped map is then checked for the two things
    /// the arithmetic cannot say: that it has a beach and a shelf at all, and that neither
    /// of them was drawn by hand.
    /// </para>
    /// </remarks>
    public sealed class SurfaceFieldTests
    {
        /// <summary>Half-width of the worlds these tests build, in metres.</summary>
        private const float Extent = 60.0f;

        /// <summary>Half-width of the island these tests build, in metres.</summary>
        private const float Shore = 20.0f;

        /// <summary>
        /// A rectangle of land comes out as a rectangle of land, and everything the level
        /// file does not cover comes out as sea.
        /// </summary>
        [Test]
        public void TheFieldIsTheRectanglesAndTheSeaIsEverythingElse()
        {
            SurfaceField field = Island(SurfaceKind.Grass).Field;

            Assert.That(field.At(Vector3.zero), Is.EqualTo(SurfaceKind.Grass), "the middle of the island");
            Assert.That(
                field.At(new Vector3(0.0f, 0.0f, Extent - 1.0f)),
                Is.EqualTo(SurfaceKind.DeepWater),
                "the open sea");
            Assert.That(
                field.At(new Vector3(0.0f, 0.0f, Extent + 40.0f)),
                Is.EqualTo(SurfaceKind.DeepWater),
                "off the edge of the world is open sea, not a hole");

            Assert.That(field.IsLand(Vector3.zero), Is.True);
            Assert.That(field.IsLand(new Vector3(0.0f, 0.0f, Extent - 1.0f)), Is.False);
        }

        /// <summary>
        /// Every coastline gets a beach, and it is exactly as wide as the table says.
        /// </summary>
        /// <remarks>
        /// The single biggest difference between the reference shot and this map, and the
        /// one a hand-authored level would get wrong first - which is the whole argument for
        /// deriving it. Nothing in the level this is built from mentions sand.
        /// </remarks>
        [Test]
        public void EveryCoastlineGetsABeachNobodyDrew()
        {
            LevelDefinition level = Island(SurfaceKind.Grass);
            SurfaceField field = level.Field;
            float beach = SurfaceTuning.For(SurfaceKind.Grass).RimWidth;

            foreach (LevelLand piece in level.Land)
            {
                Assert.That(piece.Surface, Is.Not.EqualTo(nameof(SurfaceKind.Sand)));
            }

            Assert.That(
                BandEast(field, SurfaceKind.Sand),
                Is.EqualTo(beach).Within(field.Cell),
                "the beach is not the width the table gives it");
            Assert.That(
                field.At(Along(field, Vector3.right, LandEnds(field, Vector3.right))),
                Is.EqualTo(SurfaceKind.Sand),
                "the waterline is not sand");
            Assert.That(
                field.At(Along(
                    field, Vector3.right, LandEnds(field, Vector3.right) - beach - field.Cell)),
                Is.EqualTo(SurfaceKind.Grass),
                "the beach reaches further inland than it is wide");

            // A beach on every side of it, or it is a strip rather than a rim.
            foreach (Vector3 towards in Compass)
            {
                Assert.That(
                    Ashore(field, towards),
                    Is.EqualTo(SurfaceKind.Sand),
                    $"the last metre of land {towards} is not beach");
            }
        }

        /// <summary>
        /// Every coastline gets a shelf, and it is wider than the beach and still finite.
        /// </summary>
        /// <remarks>
        /// The band that makes an island sit <em>in</em> the water rather than on top of it.
        /// It is checked against the open sea beyond it as well as against its own width,
        /// because a shelf that reached everywhere would not be a shelf, it would be a new
        /// colour for the sea.
        /// </remarks>
        [Test]
        public void EveryCoastlineGetsAShelfNobodyDrew()
        {
            SurfaceField field = Island(SurfaceKind.Grass).Field;
            float shelf = SurfaceTuning.For(SurfaceKind.DeepWater).RimWidth;

            float coast = LandEnds(field, Vector3.right);

            Assert.That(
                BandEast(field, SurfaceKind.ShallowWater),
                Is.EqualTo(shelf).Within(field.Cell),
                "the shelf is not the width the table gives it");
            Assert.That(
                field.At(Along(field, Vector3.right, coast + field.Cell)),
                Is.EqualTo(SurfaceKind.ShallowWater),
                "the water at the waterline is not the shelf");
            Assert.That(
                field.At(Along(field, Vector3.right, coast + shelf + (2.0f * field.Cell))),
                Is.EqualTo(SurfaceKind.DeepWater),
                "the shelf runs out into the open sea");
        }

        /// <summary>
        /// The shelf is water. It is drawn as appearance and it is not something anybody can
        /// stand on.
        /// </summary>
        /// <remarks>
        /// The one thing that would have made this pass a balance change rather than a
        /// picture. The shelf drowns you exactly as the open sea does, which is the decision
        /// the table records and this is the check that nothing quietly softened it.
        /// </remarks>
        [Test]
        public void NothingCanStandOnTheShelf()
        {
            SurfaceField field = Island(SurfaceKind.Grass).Field;
            Vector3 justOffshore = Along(
                field, Vector3.right, LandEnds(field, Vector3.right) + field.Cell);

            Assert.That(field.At(justOffshore), Is.EqualTo(SurfaceKind.ShallowWater));
            Assert.That(field.IsLand(justOffshore), Is.False, "the shelf became dry land");
            Assert.That(field.ToTheCoast(justOffshore), Is.LessThan(0.0f), "the shelf is inside the coast");
        }

        /// <summary>
        /// What somebody built keeps its surface right up to the water, and that is what
        /// makes it read as built.
        /// </summary>
        /// <remarks>
        /// A road that ran out onto a beach would be a road that stopped being a road at the
        /// one place it matters - the crossing - and the beach would be a colour saying
        /// something untrue about how the ground got there. The sea still rims itself,
        /// because the shelf is the sea's business rather than the shore's.
        /// </remarks>
        [Test]
        public void WhatSomebodyBuiltKeepsItsSurfaceAtTheWaterline()
        {
            SurfaceField field = Island(SurfaceKind.Asphalt).Field;

            Assert.That(
                BandEast(field, SurfaceKind.Sand),
                Is.EqualTo(0.0f),
                "a road grew a beach, so the one thing on the map with hard edges has soft ones");
            Assert.That(
                field.At(new Vector3(Shore - 0.5f, 0.0f, 0.0f)),
                Is.EqualTo(SurfaceKind.Asphalt),
                "the road stops before the water");
            Assert.That(
                field.At(new Vector3(Shore + 0.5f, 0.0f, 0.0f)),
                Is.EqualTo(SurfaceKind.ShallowWater),
                "a built coast wandered: it is where the file put it or it is not built");
            Assert.That(
                BandEast(field, SurfaceKind.ShallowWater),
                Is.GreaterThan(0.0f),
                "a built coast lost its shelf, which is the sea's and not the shore's");
        }

        /// <summary>
        /// Two rectangles that meet are one landmass, and a point next to the seam is as far
        /// inland as it really is.
        /// </summary>
        /// <remarks>
        /// The bug this phase fixes rather than a hypothetical. Every margin used to be
        /// measured against each rectangle on its own, so a point a metre from where a
        /// bridgehead is built onto a shore was within a margin of an edge of both and was
        /// refused by both - though it is thirty metres from any water. Nothing on the
        /// shipped map happened to stand there. Something would have.
        /// </remarks>
        [Test]
        public void RectanglesThatMeetAreOneLandmassRatherThanASeam()
        {
            var level = new LevelDefinition
            {
                Name = "Two halves",
                Bounds = new LevelBounds { HalfExtent = Extent },
                Land = new[]
                {
                    Rectangle("West", SurfaceKind.Grass, -Shore, 0.0f, -Shore, Shore),
                    Rectangle("East", SurfaceKind.Grass, 0.0f, Shore, -Shore, Shore),
                },
            };

            Assert.That(
                level.IsOnLand(Vector3.zero, LevelValidation.BunkerShoreMargin),
                Is.True,
                "the middle of a landmass was refused for standing on the seam between the two "
                + "rectangles it is drawn with");
            Assert.That(
                level.IsOnLand(
                    Along(level.Field, Vector3.right, LandEnds(level.Field, Vector3.right)),
                    LevelValidation.ShoreMargin),
                Is.False,
                "a point a metre from the coast passed a two-and-a-half metre margin");
        }

        /// <summary>
        /// The same map rasterises the same way every time, which the baked scene and the
        /// map the loader builds from the file both depend on.
        /// </summary>
        [Test]
        public void TheFieldSaysTheSameThingEveryTimeItIsBuilt()
        {
            LevelDefinition level = Island(SurfaceKind.Grass);
            SurfaceField first = SurfaceField.Build(level);
            SurfaceField again = SurfaceField.Build(level);

            Assert.That(again.Side, Is.EqualTo(first.Side));

            // Counted rather than asserted cell by cell: fourteen thousand assertions to say
            // one thing is a test that takes a second to run and a paragraph to read.
            int differed = 0;
            for (int z = 0; z < first.Side; z++)
            {
                for (int x = 0; x < first.Side; x++)
                {
                    if (again.At(x, z) != first.At(x, z))
                    {
                        differed++;
                    }
                }
            }

            Assert.That(
                differed,
                Is.EqualTo(0),
                $"{differed} cells came out differently the second time, so the map baked into "
                + "a scene and the map the loader builds from the file are two maps");
        }

        /// <summary>
        /// Moving a coastline moves the field under it, without anybody having to say so.
        /// </summary>
        /// <remarks>
        /// The level editor writes straight into the land array and rebuilds the world; it
        /// has no way to tell a cached field that it has done so, and a field that went stale
        /// would answer questions about the map somebody used to have. So the field checks
        /// the map rather than being told about it, and this is the test that it does.
        /// </remarks>
        [Test]
        public void TheFieldIsRebuiltWhenTheLandMoves()
        {
            LevelDefinition level = Island(SurfaceKind.Grass);
            var offTheEastEnd = new Vector3(Shore - 5.0f, 0.0f, 0.0f);

            Assert.That(level.Field.IsLand(offTheEastEnd), Is.True);

            SurfaceField before = level.Field;
            level.Land[0].MaxX = Shore - 10.0f;

            Assert.That(level.Field, Is.Not.SameAs(before), "the field outlived the map it describes");
            Assert.That(
                level.Field.IsLand(offTheEastEnd),
                Is.False,
                "the coast moved and the field went on saying the old one was there");
        }

        /// <summary>
        /// A world nobody could rasterise at a metre a cell gets bigger cells rather than an
        /// allocation that takes the process with it.
        /// </summary>
        /// <remarks>
        /// A half-extent is a number typed into an editor, which means it is a number
        /// somebody can get wrong by three digits.
        /// </remarks>
        [Test]
        public void AWorldTooBigForMetreCellsGetsBiggerCells()
        {
            var level = new LevelDefinition
            {
                Name = "Enormous",
                Bounds = new LevelBounds { HalfExtent = 4000.0f },
                Land = new[] { Rectangle("Island", SurfaceKind.Grass, -500.0f, 500.0f, -500.0f, 500.0f) },
            };

            SurfaceField field = level.Field;

            Assert.That(field.Side, Is.LessThanOrEqualTo(SurfaceField.MostCellsAcross));
            Assert.That(field.Cell, Is.GreaterThan(SurfaceField.CellSize), "the cells did not grow with the world");
            Assert.That(field.At(Vector3.zero), Is.EqualTo(SurfaceKind.Grass), "the island went missing");
            Assert.That(field.At(new Vector3(3000.0f, 0.0f, 0.0f)), Is.EqualTo(SurfaceKind.DeepWater));
        }

        /// <summary>
        /// The map the game ships has a beach and a shelf, and its level file mentions
        /// neither.
        /// </summary>
        /// <remarks>
        /// The claim this phase is judged on, stated so that it holds for whatever map is
        /// shipped rather than for Iron Channel by name.
        /// </remarks>
        [Test]
        public void TheShippedMapHasACoastNobodyDrew()
        {
            LevelDefinition level = TheShippedMap();
            Dictionary<SurfaceKind, int> counted = Census(level.Field);

            foreach (SurfaceKind rim in SurfaceTuning.Rims())
            {
                Assert.That(
                    counted.ContainsKey(rim) ? counted[rim] : 0,
                    Is.GreaterThan(0),
                    $"the shipped map has no {rim} on it at all");
            }

            // The shipped map paints sand by hand now - an apron wide enough to slow a jeep
            // down is a design decision rather than a derived one - so "nobody drew it" is
            // checked by taking the painting away rather than by forbidding it. Repaint
            // every natural shape with the surface a level falls back to and leave every
            // built one alone, so the outline does not move, and the coast has to come back
            // on its own.
            foreach (LevelLand piece in level.Land)
            {
                if (SurfaceTuning.For(piece.Ground).NaturalEdge)
                {
                    piece.Surface = nameof(SurfaceKind.Grass);
                }
            }

            SurfaceField bare = level.Field;
            Dictionary<SurfaceKind, int> derived = Census(bare);
            foreach (SurfaceKind rim in SurfaceTuning.Rims())
            {
                Assert.That(
                    derived.ContainsKey(rim) ? derived[rim] : 0,
                    Is.GreaterThan(0),
                    $"a map painted one colour has no {rim} on it, so {rim} is authored rather "
                    + "than derived");
            }

            float beach = SurfaceTuning.For(SurfaceKind.Grass).RimWidth;
            for (int z = 0; z < bare.Side; z++)
            {
                for (int x = 0; x < bare.Side; x++)
                {
                    var at = new Vector3(bare.Middle(x), 0.0f, bare.Middle(z));
                    float inland = bare.ToTheCoast(at);
                    if (inland <= 0.0f || inland > beach)
                    {
                        continue;
                    }

                    // A rim only reaches its own surface, so a crossing that runs out to the
                    // waterline keeps its tarmac right to the edge - which is the whole point
                    // of a built surface and not a hole in this rule.
                    Assert.That(
                        bare.At(x, z),
                        Is.EqualTo(SurfaceKind.Asphalt).Or.EqualTo(SurfaceKind.Sand),
                        $"the coast at {at} is neither a beach nor something somebody built");
                }
            }
        }

        /// <summary>
        /// The shelf never closes over a crossing: the water a bridge spans is still open
        /// sea in the middle.
        /// </summary>
        /// <remarks>
        /// What holds the shelf's width down. A crossing whose water is pale from bank to
        /// bank reads as a ford - shallow, drivable, a place to cut the corner - and it is
        /// nothing of the kind: it drowns you exactly as the open sea does. The narrowest
        /// water on this map is thirteen metres, so a shelf of more than six and a half
        /// would start telling that lie, and this is where it would be caught.
        /// </remarks>
        [Test]
        public void TheShelfLeavesOpenSeaInTheNarrows()
        {
            LevelDefinition level = TheShippedMap();
            SurfaceField field = level.Field;
            int bridges = 0;

            foreach (LevelStructure structure in level.Structures)
            {
                if (structure.Structure != StructureKind.Bridge)
                {
                    continue;
                }

                bridges++;
                var middle = new Vector3(structure.Position.x, 0.0f, structure.Position.z);
                Assert.That(
                    field.At(middle),
                    Is.EqualTo(SurfaceKind.DeepWater),
                    $"the shelf has closed over the narrows at {middle}, so a crossing that "
                    + "drowns you reads as a ford");
            }

            Assert.That(bridges, Is.GreaterThan(0), "the map has no crossing to check");
        }

        /// <summary>
        /// A natural coast wanders, and a built one is exactly where the file put it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The whole of this phase in one test. Both halves matter and they matter against
        /// each other: a map where nothing wanders is the basin the last phase shipped, and
        /// a map where everything wanders has no crossings, only approximate ones. The
        /// shipped map draws both from the same rectangles and tells them apart by nothing
        /// but <see cref="SurfaceTuning.NaturalEdge"/>.
        /// </para>
        /// <para>
        /// The causeway is measured rather than the bridgeheads because its edges are on
        /// whole metres, so a cell either is causeway or is not and the count is the width.
        /// A crossing that had lost even one cell to the noise would be a crossing whose
        /// width was a suggestion.
        /// </para>
        /// </remarks>
        [Test]
        public void ANaturalCoastWandersAndABuiltOneDoesNot()
        {
            LevelDefinition level = TheShippedMap();
            SurfaceField field = level.Field;

            var waterlines = new HashSet<float>();
            for (float x = -20.0f; x <= 20.0f; x += 2.0f)
            {
                waterlines.Add(NorthOf(field, x));
            }

            Assert.That(
                waterlines.Count,
                Is.GreaterThan(1),
                "the whole southern shore is one straight line, so no coast on this map wanders");

            LevelLand causeway = Built(level);
            int measured = 0;
            for (float z = causeway.MinZ + 1.0f; z <= causeway.MaxZ - 1.0f; z += 1.0f)
            {
                // Only across the water. A crossing is built a few metres into the banks it
                // joins - otherwise it would be a jetty with a gap at each end - and the
                // land counted there is the headland's rather than the causeway's.
                if (!OverWater(field, causeway, z))
                {
                    continue;
                }

                measured++;
                Assert.That(
                    AcrossAt(field, causeway, z),
                    Is.EqualTo(causeway.Width).Within(0.001f),
                    $"'{causeway.Name}' is not the width it was written at z = {z}");
            }

            Assert.That(
                measured,
                Is.GreaterThan(4),
                $"'{causeway.Name}' has almost no water either side of it, so nothing here "
                + "measured whether a built edge is held still");
        }

        /// <summary>
        /// Reports whether a piece of land has open water on both sides at one place along it.
        /// </summary>
        /// <param name="field">The realised map.</param>
        /// <param name="piece">The piece to look either side of.</param>
        /// <param name="z">Where along it to look, in metres.</param>
        /// <returns><c>true</c> when the four metres outside each edge are all water.</returns>
        /// <remarks>
        /// Four metres because that is the window <see cref="AcrossAt"/> counts across, so
        /// this answers exactly the question "is there anything in that window that is not
        /// this piece".
        /// </remarks>
        private static bool OverWater(SurfaceField field, LevelLand piece, float z)
        {
            for (float away = field.Cell; away <= 4.0f; away += field.Cell)
            {
                if (field.IsLand(new Vector3(piece.MinX - away, 0.0f, z))
                    || field.IsLand(new Vector3(piece.MaxX + away, 0.0f, z)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The seed is the coastline: change it and every natural coast moves, and not one
        /// built edge does.
        /// </summary>
        /// <remarks>
        /// Which is what makes a seed worth carrying in the file at all. A map whose seed
        /// changed nothing would be a map with one coastline pretending to have a choice of
        /// them, and a map whose seed moved a bridgehead would be a map you could not
        /// balance.
        /// </remarks>
        [Test]
        public void TheSeedIsTheCoastlineAndNothingBuiltMovesWithIt()
        {
            LevelDefinition level = TheShippedMap();
            level.Seed = 1;
            Dictionary<SurfaceKind, int> first = Census(level.Field);
            HashSet<int> built = Cells(level.Field, SurfaceKind.Asphalt);

            level.Seed = 2;
            Dictionary<SurfaceKind, int> again = Census(level.Field);

            Assert.That(
                again[SurfaceKind.DeepWater],
                Is.Not.EqualTo(first[SurfaceKind.DeepWater]),
                "two seeds gave the same coastline, so the seed is not the coastline");
            Assert.That(
                Cells(level.Field, SurfaceKind.Asphalt),
                Is.EquivalentTo(built),
                "changing the seed moved something somebody built");
        }

        /// <summary>
        /// Changing the seed rebuilds the field, without anybody having to say so.
        /// </summary>
        /// <remarks>
        /// The same argument as <see cref="TheFieldIsRebuiltWhenTheLandMoves"/> and the
        /// other half of it: the seed shapes every coast on the map, so a field that
        /// survived a change to it would be a picture of a map nobody has.
        /// </remarks>
        [Test]
        public void TheFieldIsRebuiltWhenTheSeedChanges()
        {
            LevelDefinition level = Island(SurfaceKind.Grass);
            SurfaceField before = level.Field;

            level.Seed = before.Side + 7;

            Assert.That(level.Field, Is.Not.SameAs(before), "the field outlived the seed it was cut with");
        }

        /// <summary>
        /// An ellipse is cut out of the box a level file gives it, corners and all.
        /// </summary>
        /// <remarks>
        /// The honest half of making a coastline natural. Noise moves an edge by a metre or
        /// two, which is the metre scale; at the hundred-metre scale a rectangle is still a
        /// rectangle, and no amount of wobble rescues that. This is the shape that does.
        /// </remarks>
        [Test]
        public void AnEllipseIsCutOutOfTheBoxItIsGiven()
        {
            LevelLand round = Rectangle("Island", SurfaceKind.Grass, -Shore, Shore, -Shore, Shore);
            round.Shape = nameof(LandShape.Ellipse);

            var level = new LevelDefinition
            {
                Name = "Round island",
                Bounds = new LevelBounds { HalfExtent = Extent },
                Land = new[] { round },
            };

            SurfaceField field = level.Field;

            Assert.That(field.IsLand(Vector3.zero), Is.True, "the middle of the island is not land");
            Assert.That(
                field.IsLand(new Vector3(Shore - 4.0f, 0.0f, 0.0f)),
                Is.True,
                "the ellipse does not reach the middle of the side it is inscribed in");
            Assert.That(
                field.IsLand(new Vector3(Shore - 1.0f, 0.0f, Shore - 1.0f)),
                Is.False,
                "the corner of the box is land, so the ellipse came out square");

            // Pi over four of the box it is inscribed in, give or take the beach's wobble
            // and a metre of cells: enough to tell an ellipse from a rectangle and from a
            // circle drawn on the wrong axis.
            Dictionary<SurfaceKind, int> counted = Census(field);
            float boxed = Shore * 2.0f * Shore * 2.0f / (field.Cell * field.Cell);
            float land = counted[SurfaceKind.Grass] + counted[SurfaceKind.Sand];

            Assert.That(land / boxed, Is.EqualTo(Mathf.PI / 4.0f).Within(0.05f));
        }

        /// <summary>The four directions a rim has to exist in to be a rim.</summary>
        private static readonly Vector3[] Compass =
        {
            Vector3.right, Vector3.left, Vector3.forward, Vector3.back,
        };

        /// <summary>
        /// Measures how far the land reaches from the middle of the map one way.
        /// </summary>
        /// <param name="field">The field to walk.</param>
        /// <param name="towards">Which way to walk.</param>
        /// <returns>Metres from the origin to the last cell of land that way.</returns>
        /// <remarks>
        /// Every band on a coast is measured from the coast, and the coast is not where the
        /// level file drew it any more - it is that line with up to
        /// <see cref="SurfaceNoise.Amplitude"/> of wobble in it. So a test that probes a
        /// fixed number of metres from a rectangle's edge is a test about a map that is not
        /// being built. These walk out and find the water first.
        /// </remarks>
        private static float LandEnds(SurfaceField field, Vector3 towards)
        {
            float reached = 0.0f;
            for (float away = 0.0f; away < field.Extent; away += field.Cell)
            {
                if (!field.IsLand(towards * away))
                {
                    return reached;
                }

                reached = away;
            }

            return reached;
        }

        /// <summary>
        /// Returns a point a given distance from the middle of the map one way.
        /// </summary>
        /// <param name="field">The field being walked.</param>
        /// <param name="towards">Which way.</param>
        /// <param name="away">How far, in metres.</param>
        /// <returns>The point.</returns>
        private static Vector3 Along(SurfaceField field, Vector3 towards, float away)
            => towards * Mathf.Clamp(away, -field.Extent, field.Extent);

        /// <summary>
        /// Returns what the last metre of land is made of one way from the middle.
        /// </summary>
        /// <param name="field">The field to walk.</param>
        /// <param name="towards">Which way to walk.</param>
        /// <returns>The surface at the waterline.</returns>
        private static SurfaceKind Ashore(SurfaceField field, Vector3 towards)
            => field.At(Along(field, towards, LandEnds(field, towards)));

        /// <summary>
        /// Measures how far north the land reaches on the southern half of a map.
        /// </summary>
        /// <param name="field">The field to walk.</param>
        /// <param name="x">Where across the map to measure, in metres.</param>
        /// <returns>Where the waterline is, in metres.</returns>
        private static float NorthOf(SurfaceField field, float x)
        {
            float reached = -field.Extent;
            for (float z = -field.Extent; z < 0.0f; z += field.Cell)
            {
                if (field.IsLand(new Vector3(x, 0.0f, z)))
                {
                    reached = z;
                }
            }

            return reached;
        }

        /// <summary>
        /// Measures how wide a piece of land really is at one place across it.
        /// </summary>
        /// <param name="field">The field to walk.</param>
        /// <param name="piece">The piece, for where to start and stop looking.</param>
        /// <param name="z">Where up the map to measure, in metres.</param>
        /// <returns>Metres of land, counted a cell at a time.</returns>
        private static float AcrossAt(SurfaceField field, LevelLand piece, float z)
        {
            float found = 0.0f;
            for (float x = piece.MinX - 4.0f; x < piece.MaxX + 4.0f; x += field.Cell)
            {
                if (field.IsLand(new Vector3(x, 0.0f, z)))
                {
                    found += field.Cell;
                }
            }

            return found;
        }

        /// <summary>
        /// Returns the widest piece of a map that nobody is allowed to move.
        /// </summary>
        /// <param name="level">The map.</param>
        /// <returns>A piece of land whose surface keeps its exact edges.</returns>
        /// <summary>
        /// Returns the widest crossing on a map: the widest built shape with water either
        /// side of it.
        /// </summary>
        /// <param name="level">The map to search.</param>
        /// <returns>That piece of land.</returns>
        /// <remarks>
        /// A crossing rather than merely the widest built shape, and the difference is the
        /// whole reason this exists. What holds a built edge still is worth checking where
        /// somebody could drown for it - a causeway is 12 m wide because a test measured a
        /// jeep against it - and a crossing is the one built shape whose width can be
        /// counted at all, because the cells either side of it are water. A road laid across
        /// a field has land on both sides and no width to measure; its exactness is held to
        /// a millimetre in the geometry instead, by
        /// <c>SurfaceMeshTests.ABuiltEdgeComesOutWhereTheFileWroteIt</c>.
        /// <para>
        /// "Crosses the channel" is the same question <c>LevelLoadingTests</c> asks of the
        /// same map, asked the same way: a shape that covers the centre line at its own
        /// middle.
        /// </para>
        /// </remarks>
        private static LevelLand Built(LevelDefinition level)
        {
            LevelLand widest = null;
            foreach (LevelLand piece in level.Land)
            {
                if (SurfaceTuning.For(piece.Ground).NaturalEdge
                    || !piece.Contains(new Vector3(piece.Centre.x, 0.0f, 0.0f)))
                {
                    continue;
                }

                if (widest == null || piece.Width > widest.Width)
                {
                    widest = piece;
                }
            }

            Assert.That(widest, Is.Not.Null, "the map has no crossing on it to hold still");
            return widest;
        }

        /// <summary>
        /// Returns which cells of a map are made of one surface.
        /// </summary>
        /// <param name="field">The field to read.</param>
        /// <param name="kind">Surface to find.</param>
        /// <returns>Their indices.</returns>
        private static HashSet<int> Cells(SurfaceField field, SurfaceKind kind)
        {
            var found = new HashSet<int>();
            for (int z = 0; z < field.Side; z++)
            {
                for (int x = 0; x < field.Side; x++)
                {
                    if (field.At(x, z) == kind)
                    {
                        found.Add((z * field.Side) + x);
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// Builds a square island in the middle of a small sea.
        /// </summary>
        /// <param name="surface">What the island is made of.</param>
        /// <returns>The level.</returns>
        private static LevelDefinition Island(SurfaceKind surface)
            => new LevelDefinition
            {
                Name = "Island",
                Bounds = new LevelBounds { HalfExtent = Extent },
                Land = new[] { Rectangle("Island", surface, -Shore, Shore, -Shore, Shore) },
            };

        /// <summary>
        /// Builds one rectangle of land.
        /// </summary>
        /// <param name="name">What to call it.</param>
        /// <param name="surface">What it is made of.</param>
        /// <param name="west">West edge, in metres.</param>
        /// <param name="east">East edge, in metres.</param>
        /// <param name="south">South edge, in metres.</param>
        /// <param name="north">North edge, in metres.</param>
        /// <returns>The rectangle.</returns>
        private static LevelLand Rectangle(
            string name, SurfaceKind surface, float west, float east, float south, float north)
            => new LevelLand
            {
                Name = name,
                Surface = surface.ToString(),
                MinX = west,
                MaxX = east,
                MinZ = south,
                MaxZ = north,
            };

        /// <summary>
        /// Measures how much of one surface is crossed walking east from the middle of the
        /// map.
        /// </summary>
        /// <param name="field">The field to walk.</param>
        /// <param name="kind">Surface to measure.</param>
        /// <returns>Metres of it.</returns>
        private static float BandEast(SurfaceField field, SurfaceKind kind)
        {
            float metres = 0.0f;
            for (float x = 0.0f; x < field.Extent; x += field.Cell)
            {
                if (field.At(new Vector3(x, 0.0f, 0.0f)) == kind)
                {
                    metres += field.Cell;
                }
            }

            return metres;
        }

        /// <summary>
        /// Counts the cells of every surface on a map.
        /// </summary>
        /// <param name="field">The field to count.</param>
        /// <returns>How many cells each surface covers.</returns>
        private static Dictionary<SurfaceKind, int> Census(SurfaceField field)
        {
            var counted = new Dictionary<SurfaceKind, int>();
            for (int z = 0; z < field.Side; z++)
            {
                for (int x = 0; x < field.Side; x++)
                {
                    SurfaceKind kind = field.At(x, z);
                    counted[kind] = (counted.ContainsKey(kind) ? counted[kind] : 0) + 1;
                }
            }

            return counted;
        }

        /// <summary>
        /// Reads the map the game ships.
        /// </summary>
        /// <returns>The level.</returns>
        private static LevelDefinition TheShippedMap()
        {
            string path = LevelLibrary.ShippedPathFor(LevelLibrary.DefaultLevel);
            Assert.That(File.Exists(path), Is.True, $"the game ships no map at {path}");
            Assert.That(LevelFile.TryRead(path, out LevelDefinition level, out string problem), Is.True, problem);
            return level;
        }
    }
}
