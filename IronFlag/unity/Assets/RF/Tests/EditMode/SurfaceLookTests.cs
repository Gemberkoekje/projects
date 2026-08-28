using NUnit.Framework;
using UnityEngine;
using IronFlag.Editor.ArtPipeline;
using IronFlag.Levels;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// The detail pass over the ground and the water: the look table, the coastline
    /// measurement the foam is drawn from, and which shader each surface ends up on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// None of this can be asserted by looking at a picture, which is the point of having it.
    /// A shader that draws foam a metre out to sea and a shader that draws it half a metre
    /// inland look about the same from thirty-four metres up; a sea that quietly carries the
    /// shelf's foam width looks like a sea until somebody renders the whole map. What is
    /// checkable is the numbers the shaders are handed and the numbers the mesh hands them.
    /// </para>
    /// <para>
    /// The load-bearing one is <see cref="TheOpenSeaHasNoFoamAndThatIsNotTidiness"/>. Every
    /// other assertion here would survive being wrong for a while.
    /// </para>
    /// </remarks>
    public sealed class SurfaceLookTests
    {
        /// <summary>Half-width of the worlds these tests build, in metres.</summary>
        private const float Extent = 60.0f;

        /// <summary>Half-width of the islands these tests build, in metres.</summary>
        private const float Shore = 20.0f;

        /// <summary>
        /// Every surface the game has is drawn with something, so a new row in the surfaces
        /// table cannot come out untextured white.
        /// </summary>
        [Test]
        public void EverySurfaceHasARowInTheLookTable()
        {
            foreach (SurfaceKind kind in SurfaceTuning.Roster())
            {
                SurfaceLook look = SurfaceLook.For(kind);
                Assert.That(look, Is.Not.Null, $"{kind} has no look");
                Assert.That(look.GrainScale, Is.GreaterThan(0.0f), $"{kind} has a grain of no size");
            }
        }

        /// <summary>
        /// The one number in this table that is load-bearing rather than decorative.
        /// </summary>
        /// <remarks>
        /// The open sea is a slab rather than one of <see cref="SurfaceMesh"/>'s sheets, so
        /// it carries no distance-to-coast at all and every vertex of it reads as being
        /// exactly on the coastline. A sea whose row said anything but zero here would be
        /// foam from one horizon to the other - which is not a subtle failure, but is one
        /// that only shows up in a render of a whole map.
        /// </remarks>
        [Test]
        public void TheOpenSeaHasNoFoamAndThatIsNotTidiness()
        {
            Assert.That(SurfaceLook.For(SurfaceKind.DeepWater).Foam, Is.EqualTo(0.0f));
            Assert.That(SurfaceLook.For(SurfaceKind.ShallowWater).Foam, Is.GreaterThan(0.0f));
        }

        /// <summary>
        /// Foam stops short of the outer edge of the shelf, so there is pale shallow water
        /// outside it rather than a straight line where the foam runs out.
        /// </summary>
        [Test]
        public void FoamIsNarrowerThanTheShelfItSitsIn()
        {
            float shelf = SurfaceTuning.For(SurfaceKind.DeepWater).RimWidth;
            Assert.That(SurfaceLook.For(SurfaceKind.ShallowWater).Foam, Is.LessThan(shelf));
        }

        /// <summary>
        /// Nothing that is ground pretends to be water, and nothing that is water pretends
        /// to have a grain.
        /// </summary>
        [Test]
        public void OnlyTheWatersMove()
        {
            foreach (SurfaceKind kind in SurfaceTuning.Roster())
            {
                SurfaceLook look = SurfaceLook.For(kind);
                if (SurfaceTuning.For(kind).Drowns)
                {
                    Assert.That(look.Swell, Is.GreaterThan(0.0f), $"{kind} is water and is dead still");
                    Assert.That(look.Grain, Is.EqualTo(0.0f), $"{kind} is water and has a grain");
                }
                else
                {
                    Assert.That(look.Grain, Is.GreaterThan(0.0f), $"{kind} is ground and is perfectly flat");
                    Assert.That(look.Swell, Is.EqualTo(0.0f), $"{kind} is ground and has a swell");
                }
            }
        }

        /// <summary>
        /// The grain is read against itself: a beach is the roughest thing on the map and a
        /// road is the smoothest, which is the same order the surfaces table already puts
        /// them in for grip.
        /// </summary>
        [Test]
        public void ABeachIsRougherThanOpenCountryAndARoadIsSmootherThanBoth()
        {
            float sand = SurfaceLook.For(SurfaceKind.Sand).Grain;
            float grass = SurfaceLook.For(SurfaceKind.Grass).Grain;
            float road = SurfaceLook.For(SurfaceKind.Asphalt).Grain;

            Assert.That(sand, Is.GreaterThan(grass), "a beach is no rougher than a field");
            Assert.That(grass, Is.GreaterThan(road), "a road is no smoother than a field");
        }

        /// <summary>
        /// The grain stays detail rather than becoming terrain the vehicles are visibly not
        /// driving on. The ground really is flat - every round in the game resolves on
        /// <c>CombatPlane</c> - so a normal leaning far off square is a lie the physics
        /// contradicts.
        /// </summary>
        [Test]
        public void NoSurfaceLeansFarOffSquare()
        {
            foreach (SurfaceKind kind in SurfaceTuning.Roster())
            {
                Assert.That(
                    SurfaceLook.For(kind).Grain,
                    Is.LessThanOrEqualTo(0.25f),
                    $"{kind}'s grain has stopped being detail");
            }
        }

        /// <summary>
        /// The waters get the water shader and the ground gets the ground one, decided by
        /// the same field that decides whether standing there drowns you.
        /// </summary>
        [Test]
        public void EachSurfaceWearsTheShaderItsRowImplies()
        {
            GeneratedMaterials.EnsureAssets();

            foreach (SurfaceKind kind in SurfaceTuning.Roster())
            {
                Material paint = GeneratedMaterials.Load(GeneratedMaterials.SurfaceMaterial(kind));
                Assert.That(paint, Is.Not.Null, $"{kind} has no material");

                string wanted = SurfaceTuning.For(kind).Drowns
                    ? GeneratedMaterials.WaterShaderName
                    : GeneratedMaterials.GroundShaderName;

                Assert.That(paint.shader.name, Is.EqualTo(wanted), $"{kind} is on the wrong shader");
            }
        }

        /// <summary>
        /// A bank is the same ground seen from the side, so it never gets the water shader
        /// however close to the sea it hangs.
        /// </summary>
        [Test]
        public void EveryBankIsGround()
        {
            GeneratedMaterials.EnsureAssets();

            foreach (SurfaceKind kind in SurfaceTuning.Stack(false))
            {
                Material paint = GeneratedMaterials.Load(GeneratedMaterials.BankMaterial(kind));
                Assert.That(paint, Is.Not.Null, $"{kind} has no bank material");
                Assert.That(
                    paint.shader.name,
                    Is.EqualTo(GeneratedMaterials.GroundShaderName),
                    $"{kind}'s bank is on the wrong shader");
            }
        }

        /// <summary>
        /// The two waters are still matte in the surfaces table, and this pass did not
        /// quietly change that to get a highlight.
        /// </summary>
        /// <remarks>
        /// The measured value ramp is argued on those numbers - a gloss on one enormous flat
        /// sheet is what made M7's first sea read lighter than the land it has to contrast
        /// with. The sun on the water comes from the shader's own glint instead, which is a
        /// few pixels wide and cannot lift the sea's value.
        /// </remarks>
        [Test]
        public void TheWatersAreStillMatte()
        {
            GeneratedMaterials.EnsureAssets();

            foreach (SurfaceKind kind in SurfaceTuning.Roster())
            {
                if (!SurfaceTuning.For(kind).Drowns)
                {
                    continue;
                }

                Assert.That(SurfaceTuning.For(kind).Smoothness, Is.EqualTo(0.0f), $"{kind} has grown a gloss");
                Assert.That(SurfaceLook.For(kind).Glint, Is.GreaterThan(0.0f), $"{kind} has no sun on it");

                Material paint = GeneratedMaterials.Load(GeneratedMaterials.SurfaceMaterial(kind));
                Assert.That(paint.GetFloat("_Smoothness"), Is.EqualTo(0.0f).Within(0.0001f));
            }
        }

        /// <summary>
        /// The shared water numbers actually reach the materials, rather than sitting in a
        /// constant nobody reads.
        /// </summary>
        /// <remarks>
        /// The trap this test exists for: Unity writes every property into a <c>.mat</c> on
        /// the day the material is created and never consults the shader's default again, so
        /// a "default" left in the shader is a number that can never be changed afterwards.
        /// That is why <see cref="SurfaceLook"/> holds the shared ones and
        /// <c>GeneratedMaterials</c> writes all of them.
        /// </remarks>
        [Test]
        public void TheSharedWaterNumbersReachTheMaterial()
        {
            GeneratedMaterials.EnsureAssets();

            Material shelf = GeneratedMaterials.Load(
                GeneratedMaterials.SurfaceMaterial(SurfaceKind.ShallowWater));

            Assert.That(shelf.GetFloat("_FoamEdge"), Is.EqualTo(SurfaceLook.FoamEdge).Within(0.0001f));
            Assert.That(shelf.GetFloat("_ChopScale"), Is.EqualTo(SurfaceLook.ChopScale).Within(0.0001f));
            Assert.That(
                shelf.GetFloat("_FresnelPower"), Is.EqualTo(SurfaceLook.FresnelPower).Within(0.0001f));
            Assert.That(
                shelf.GetFloat("_FoamWidth"),
                Is.EqualTo(SurfaceLook.For(SurfaceKind.ShallowWater).Foam).Within(0.0001f));
        }

        /// <summary>
        /// The smooth read of the coast field agrees with the cell-wise one at the middle of
        /// a cell, which is the only place the two can be compared.
        /// </summary>
        [Test]
        public void TheSmoothCoastAgreesWithTheCellsAtTheirMiddles()
        {
            SurfaceField field = Island().Field;

            for (int cell = 4; cell < field.Side - 4; cell += 7)
            {
                var at = new Vector3(field.Middle(cell), 0.0f, field.Middle(cell));
                Assert.That(
                    field.Shore(at),
                    Is.EqualTo(field.ToTheCoast(at)).Within(0.0005f),
                    $"the two readings disagree at cell {cell}");
            }
        }

        /// <summary>
        /// Between two cells the smooth read lands between their two values, which is the
        /// whole reason it exists: a foam line taken from the cell-wise one is a staircase on
        /// exactly the grid the rest of the map is cut across.
        /// </summary>
        [Test]
        public void TheSmoothCoastReadsBetweenTheCells()
        {
            SurfaceField field = Island().Field;

            int cell = field.Side / 3;
            float west = field.Middle(cell);
            float east = field.Middle(cell + 1);
            float z = field.Middle(field.Side / 2);

            float near = field.Shore(new Vector3(west, 0.0f, z));
            float far = field.Shore(new Vector3(east, 0.0f, z));
            float half = field.Shore(new Vector3((west + east) * 0.5f, 0.0f, z));

            Assert.That(half, Is.EqualTo((near + far) * 0.5f).Within(0.0005f));
        }

        /// <summary>
        /// Reading off the edge of the grid gives the edge's own value rather than the
        /// off-the-map sentinel, which would put a bright foam line around the outer rim of
        /// every sea slab.
        /// </summary>
        [Test]
        public void TheSmoothCoastDoesNotFallOffTheEdgeOfTheWorld()
        {
            SurfaceField field = Island().Field;

            float corner = field.Shore(new Vector3(-Extent, 0.0f, -Extent));
            float outside = field.Shore(new Vector3(-Extent * 4.0f, 0.0f, -Extent * 4.0f));

            Assert.That(corner, Is.LessThan(0.0f), "the corner of the world is not water");
            Assert.That(outside, Is.EqualTo(corner).Within(0.0001f), "reading off the map plunged");
        }

        /// <summary>
        /// The sheets drawn over water carry the coastline in their second texture channel,
        /// and the sheets of land do not.
        /// </summary>
        /// <remarks>
        /// Both halves matter. Without the first there is no foam; with the second, every
        /// sheet on the map would carry eight bytes a vertex into the committed scene file
        /// for a number nothing reads - and it would be wrong if anything did, because the
        /// interior of a land sheet is merged into rectangles tens of metres across.
        /// </remarks>
        [Test]
        public void OnlyTheWaterSheetsCarryTheCoastline()
        {
            SurfaceField field = Island().Field;

            Mesh shelf = SurfaceMesh.Build(
                field, SurfaceKind.ShallowWater, "Shelf", measureShore: true);
            Mesh land = SurfaceMesh.Build(field, SurfaceKind.Grass, "Grass");

            Assert.That(shelf, Is.Not.Null, "the island grew no shelf");
            Assert.That(land, Is.Not.Null, "the island was not drawn");

            Assert.That(
                shelf.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord1),
                Is.True,
                "the shelf carries no coastline, so there is nothing to draw foam from");
            Assert.That(
                land.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord1),
                Is.False,
                "a sheet of land is carrying a coastline nothing reads");
        }

        /// <summary>
        /// What the shelf carries is metres of water, running from nothing at the coastline
        /// out to about the width of the shelf.
        /// </summary>
        /// <remarks>
        /// The sign is the part worth pinning down: the shader turns it into "how far towards
        /// the land am I" by dividing by the foam width, and a shelf that measured positive
        /// would put the foam at the outer edge of the shelf instead of at the beach.
        /// </remarks>
        [Test]
        public void TheShelfMeasuresItselfInMetresOfWater()
        {
            SurfaceField field = Island().Field;
            Mesh shelf = SurfaceMesh.Build(
                field, SurfaceKind.ShallowWater, "Shelf", measureShore: true);

            var coast = new System.Collections.Generic.List<Vector2>();
            shelf.GetUVs(1, coast);

            Assert.That(coast.Count, Is.EqualTo(shelf.vertexCount), "one reading per corner");

            float nearest = float.NegativeInfinity;
            float furthest = float.PositiveInfinity;
            foreach (Vector2 measured in coast)
            {
                nearest = Mathf.Max(nearest, measured.x);
                furthest = Mathf.Min(furthest, measured.x);
            }

            float wide = SurfaceTuning.For(SurfaceKind.DeepWater).RimWidth;

            Assert.That(nearest, Is.LessThanOrEqualTo(1.0f), "part of the shelf is dry land");
            Assert.That(nearest, Is.GreaterThan(-1.0f), "no part of the shelf reaches the coast");
            Assert.That(
                furthest,
                Is.GreaterThan(-(wide + field.Cell * 3.0f)),
                "the shelf reaches further out to sea than the shelf is wide");
        }

        /// <summary>
        /// A square island in a sea, big enough to have a proper shelf all the way round.
        /// </summary>
        /// <returns>The level, already rasterised.</returns>
        private static LevelDefinition Island()
            => new LevelDefinition
            {
                Name = "Island",
                Seed = 7,
                Bounds = new LevelBounds { HalfExtent = Extent },
                Land = new[]
                {
                    new LevelLand
                    {
                        Name = "Island",
                        Surface = SurfaceKind.Grass.ToString(),
                        MinX = -Shore,
                        MaxX = Shore,
                        MinZ = -Shore,
                        MaxZ = Shore,
                    },
                },
            };
    }
}
