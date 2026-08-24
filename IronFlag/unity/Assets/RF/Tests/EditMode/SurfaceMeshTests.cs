using System.IO;
using NUnit.Framework;
using UnityEngine;
using IronFlag.Levels;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// The geometry a map is actually drawn as: the sheets cut out of the field, and the
    /// bank that hangs off the coastline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="SurfaceFieldTests"/> checks what the map is made of, a square metre at a
    /// time. This checks what comes out of that when it is turned into something you can
    /// look at - which is a different question with a different way of being wrong: a field
    /// that is right and a mesh that is a staircase is still a map with a staircase coast,
    /// and a mesh that stops half a metre short of a road is still a road with sand on the
    /// end of it.
    /// </para>
    /// <para>
    /// The claims here are the three the phase rests on. A built edge comes out exactly
    /// where the file wrote it, even between two cells. A natural one comes out as a line
    /// rather than as a staircase. And the sheets cover each other, so nothing anywhere on
    /// the map shows a colour that does not belong at the place it shows through.
    /// </para>
    /// </remarks>
    public sealed class SurfaceMeshTests
    {
        /// <summary>Half-width of the worlds these tests build, in metres.</summary>
        private const float Extent = 60.0f;

        /// <summary>
        /// Half-width of the islands these tests build, deliberately off the metre.
        /// </summary>
        /// <remarks>
        /// Twenty and a half metres, so that the edge of the island lands exactly on the
        /// middle of a cell - which is the one place the map used to be able to disagree
        /// with the level file, because a cell whose middle is on the edge belongs to the
        /// land and the drawn edge is half a metre inside it. It is not a corner case
        /// somebody invented: the shipped map's four bridgeheads are at z = plus or minus
        /// 6.5 for exactly this reason, and the half metre showed up as a dark line the sea
        /// came through.
        /// </remarks>
        private const float Shore = 20.5f;

        /// <summary>How far the bank these tests build drops, in metres.</summary>
        private const float Drop = 1.2f;

        /// <summary>
        /// A built edge comes out exactly where the level file wrote it, to the millimetre,
        /// even when that is between two cells.
        /// </summary>
        /// <remarks>
        /// The reason the whole map is cut from a measured shape rather than from the cells
        /// it was rasterised into. A causeway is 12 m wide because somebody typed 12; if the
        /// geometry rounded that to the grid it would be 12 m give or take a cell, the
        /// narrows at each bridgehead would be 13 m give or take a cell, and the test that
        /// works out how far a jeep can jump would be measuring something nobody drew.
        /// </remarks>
        [Test]
        public void ABuiltEdgeComesOutWhereTheFileWroteIt()
        {
            SurfaceField field = Island(SurfaceKind.Asphalt).Field;
            Mesh sheet = SurfaceMesh.Build(field, SurfaceKind.Asphalt, "Asphalt");

            Assert.That(sheet, Is.Not.Null, "the island was not drawn at all");
            Assert.That(sheet.bounds.min.x, Is.EqualTo(-Shore).Within(0.001f), "the west edge moved");
            Assert.That(sheet.bounds.max.x, Is.EqualTo(Shore).Within(0.001f), "the east edge moved");
            Assert.That(sheet.bounds.min.z, Is.EqualTo(-Shore).Within(0.001f), "the south edge moved");
            Assert.That(sheet.bounds.max.z, Is.EqualTo(Shore).Within(0.001f), "the north edge moved");
        }

        /// <summary>
        /// A natural coast comes out as a line through the cells rather than as a staircase
        /// around them.
        /// </summary>
        /// <remarks>
        /// What the last phase left behind and this one is for. A band whose edge steps
        /// around whole cells reads as stairs wherever the coast turns, which was visible at
        /// every bridgehead in <c>surfaces-b-coast.png</c>. Cutting the boundary through the
        /// cells at whatever angle the numbers say is what dissolves it, and a coastline
        /// every one of whose corners sits on the grid is a coastline that did not get cut.
        /// </remarks>
        [Test]
        public void ANaturalCoastIsCutThroughTheCellsRatherThanAroundThem()
        {
            SurfaceField field = Island(SurfaceKind.Grass).Field;
            Mesh sheet = SurfaceMesh.Build(field, SurfaceKind.Sand, "Sand");

            Assert.That(sheet, Is.Not.Null, "the island was not drawn at all");

            int offTheGrid = 0;
            foreach (Vector3 corner in sheet.vertices)
            {
                if (!OnTheGrid(field, corner.x) || !OnTheGrid(field, corner.z))
                {
                    offTheGrid++;
                }
            }

            Assert.That(
                offTheGrid,
                Is.GreaterThan(0),
                "every corner of the coastline is on a cell boundary, so the coast is a "
                + "staircase rather than a line");
        }

        /// <summary>
        /// Every sheet of a map covers every sheet drawn above it, so no gap between two of
        /// them can show anything that does not belong there.
        /// </summary>
        /// <remarks>
        /// The arrangement that makes the boundaries between surfaces free. Cut as five
        /// separate pieces that had to meet exactly, three of them would meet at a point
        /// somewhere on the map and leave a hole a quarter of a cell across showing whatever
        /// is under the whole island. Cut as a stack, the sheet below is always a superset
        /// of the one above and a hole is impossible rather than unlikely.
        /// </remarks>
        [Test]
        public void EverySheetCoversTheOneAboveIt()
        {
            SurfaceField field = TheShippedMap().Field;
            float under = float.MaxValue;
            int drawn = 0;

            foreach (SurfaceKind kind in SurfaceTuning.Stack(false))
            {
                if (!field.Covers(kind))
                {
                    continue;
                }

                Mesh sheet = SurfaceMesh.Build(field, kind, kind.ToString());
                Assert.That(sheet, Is.Not.Null, $"the map has {kind} on it and did not draw it");

                float area = AreaOf(sheet);
                Assert.That(
                    area,
                    Is.LessThanOrEqualTo(under),
                    $"the {kind} sheet is bigger than the one it is drawn on top of");

                under = area;
                drawn++;
            }

            Assert.That(drawn, Is.GreaterThan(1), "the map is drawn as one sheet, so nothing is stacked");
        }

        /// <summary>
        /// The bank hangs from the coastline down to below the water, and it hangs from all
        /// of it.
        /// </summary>
        /// <remarks>
        /// The only relief the map has. Land is at <c>y = 0</c> everywhere because every
        /// round in the game is resolved on that plane, so the drop to the water is the
        /// whole of the third dimension - and it is what makes a coastline read from
        /// directly overhead, which a flat colour boundary would not.
        /// </remarks>
        [Test]
        public void TheBankHangsFromEveryCoastTheIslandHas()
        {
            SurfaceField field = Island(SurfaceKind.Asphalt).Field;
            Mesh sheet = SurfaceMesh.Build(field, SurfaceKind.Asphalt, "Asphalt");
            Mesh bank = SurfaceMesh.Bank(field, SurfaceKind.Asphalt, Drop, "Asphalt bank");

            Assert.That(bank, Is.Not.Null, "the island has no bank, so it is a sticker on the sea");
            Assert.That(bank.bounds.max.y, Is.EqualTo(0.0f).Within(0.001f), "the bank starts below the land");
            Assert.That(bank.bounds.min.y, Is.EqualTo(-Drop).Within(0.001f), "the bank stops short of the water");

            // It hangs from the coastline itself rather than from a second line that has to
            // agree with it, so the two are the same shape seen from above.
            Assert.That(bank.bounds.min.x, Is.EqualTo(sheet.bounds.min.x).Within(0.001f));
            Assert.That(bank.bounds.max.x, Is.EqualTo(sheet.bounds.max.x).Within(0.001f));
            Assert.That(bank.bounds.min.z, Is.EqualTo(sheet.bounds.min.z).Within(0.001f));
            Assert.That(bank.bounds.max.z, Is.EqualTo(sheet.bounds.max.z).Within(0.001f));
        }

        /// <summary>
        /// Nothing is drawn upside down: every sheet faces the camera and every bank faces
        /// the water.
        /// </summary>
        /// <remarks>
        /// Winding is a very quiet bug in a world with a sea in it. A sheet wound the wrong
        /// way is simply not there when you look down at it, and what you see instead is
        /// whatever is underneath - which on this map is more map. The bank is checked from
        /// the other side: its faces have to point away from the island, or the island is a
        /// hole you can see the inside of.
        /// </remarks>
        [Test]
        public void NothingIsDrawnInsideOut()
        {
            SurfaceField field = Island(SurfaceKind.Asphalt).Field;
            Mesh sheet = SurfaceMesh.Build(field, SurfaceKind.Asphalt, "Asphalt");
            Mesh bank = SurfaceMesh.Bank(field, SurfaceKind.Asphalt, Drop, "Asphalt bank");

            foreach (Vector3 facing in sheet.normals)
            {
                Assert.That(facing.y, Is.GreaterThan(0.5f), "a sheet of the map faces the ground");
            }

            Vector3[] corners = bank.vertices;
            Vector3[] outward = bank.normals;
            for (int at = 0; at < corners.Length; at++)
            {
                Assert.That(
                    Mathf.Abs(outward[at].y),
                    Is.LessThan(0.001f),
                    "a stretch of bank is not vertical");

                var away = new Vector2(corners[at].x, corners[at].z);
                Assert.That(
                    Vector2.Dot(new Vector2(outward[at].x, outward[at].z), away.normalized),
                    Is.GreaterThan(0.0f),
                    "a stretch of bank faces the middle of the island rather than the water");
            }
        }

        /// <summary>
        /// A map with none of a surface on it is not drawn in that surface.
        /// </summary>
        /// <remarks>
        /// The lowest sheet of a stack covers the whole of its host, so a map with no sand
        /// would otherwise be an island painted sand with an island painted grass laid
        /// exactly over the top of it - invisible, twice the geometry, and one rounding
        /// error away from showing.
        /// </remarks>
        [Test]
        public void ASurfaceNoMapHasIsNotDrawn()
        {
            SurfaceField field = Island(SurfaceKind.Asphalt).Field;

            Assert.That(field.Covers(SurfaceKind.Grass), Is.False, "the road grew a lawn");
            Assert.That(field.Covers(SurfaceKind.Sand), Is.False, "the road grew a beach");
            Assert.That(
                SurfaceMesh.Build(field, SurfaceKind.None, "Nothing"),
                Is.Null,
                "a surface that is not a surface was drawn");
        }

        /// <summary>
        /// Reports whether a number is on a cell boundary of a field.
        /// </summary>
        /// <param name="field">The field.</param>
        /// <param name="metres">The coordinate.</param>
        /// <returns><c>true</c> when it sits on the grid rather than between two of it.</returns>
        /// <remarks>
        /// Half a cell either way, because a boundary cut between two cell middles is on the
        /// grid when it falls exactly halfway - a straight coast along a cell edge is on it
        /// everywhere, which is right, and is why this counts corners that are off it rather
        /// than demanding that all of them are.
        /// </remarks>
        private static bool OnTheGrid(SurfaceField field, float metres)
        {
            float steps = (metres + field.Extent) / (field.Cell * 0.5f);
            return Mathf.Abs(steps - Mathf.Round(steps)) < 0.001f;
        }

        /// <summary>
        /// Measures how much ground a flat sheet covers.
        /// </summary>
        /// <param name="sheet">The mesh.</param>
        /// <returns>Its area seen from above, in square metres.</returns>
        private static float AreaOf(Mesh sheet)
        {
            Vector3[] corners = sheet.vertices;
            int[] triangles = sheet.triangles;
            float area = 0.0f;

            for (int at = 0; at + 2 < triangles.Length; at += 3)
            {
                Vector3 first = corners[triangles[at]];
                Vector3 second = corners[triangles[at + 1]];
                Vector3 third = corners[triangles[at + 2]];

                area += Mathf.Abs(
                    ((second.x - first.x) * (third.z - first.z))
                    - ((third.x - first.x) * (second.z - first.z))) * 0.5f;
            }

            return area;
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
                Seed = 4,
                Bounds = new LevelBounds { HalfExtent = Extent },
                Land = new[]
                {
                    new LevelLand
                    {
                        Name = "Island",
                        Surface = surface.ToString(),
                        MinX = -Shore,
                        MaxX = Shore,
                        MinZ = -Shore,
                        MaxZ = Shore,
                    },
                },
            };

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
