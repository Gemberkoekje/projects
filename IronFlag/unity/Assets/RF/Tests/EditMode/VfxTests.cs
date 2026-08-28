using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using IronFlag.Combat;
using IronFlag.Destruction;
using IronFlag.Editor.ArtPipeline;
using IronFlag.Editor.Gameplay;
using IronFlag.Levels;
using IronFlag.Vehicles;
using IronFlag.Vfx;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// The rules behind the particle effects, and the wiring that connects them to the
    /// things they come off.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A particle system cannot be asserted on frame by frame, and this does not try. What it
    /// checks is the half that is not particles at all: <em>when</em> smoke starts, <em>how
    /// much</em> dust a surface gives up, <em>which</em> impacts throw spray, and whether
    /// every prefab that should carry an effect actually does. Those are the four places a
    /// mistake would be invisible in a still and obvious in play.
    /// </para>
    /// <para>
    /// The three tuning functions are all static and side-effect free, which is the same
    /// property <c>Explosion.Scale</c> and <c>DebrisBurst.Offset</c> have and for the same
    /// reason: the decision is checkable without the effect.
    /// </para>
    /// </remarks>
    public sealed class VfxTests
    {
        [Test]
        public void NothingSmokesUntilItHasLostHalfOfItself()
        {
            Assert.That(DamageSmoke.ShouldSmoke(1.0f, false), Is.False, "a fresh hull smokes");
            Assert.That(DamageSmoke.ShouldSmoke(0.75f, false), Is.False, "a scratch smokes");
            Assert.That(DamageSmoke.ShouldSmoke(DamageSmoke.SmokesBelow, false), Is.True);
            Assert.That(DamageSmoke.ShouldSmoke(0.1f, false), Is.True);
        }

        /// <summary>
        /// A wreck is at zero, which is below the threshold - so without the second question
        /// every rubble pile on the map would smoke for the rest of the match. The column is
        /// what marks a death, and it is a burst rather than a plume for exactly this reason.
        /// </summary>
        [Test]
        public void AWreckStopsSmokingRatherThanSmokingForEver()
        {
            Assert.That(DamageSmoke.ShouldSmoke(0.0f, true), Is.False);
            Assert.That(DamageSmoke.ShouldSmoke(0.4f, true), Is.False);
        }

        /// <summary>
        /// The whole claim of the dust row: the amount falls out of the surface table's grip
        /// column rather than out of a number somebody wrote for dust. Soft ground is loose
        /// ground, so the surface that costs the jeep a fifth of its speed is the surface
        /// that ends up behind it.
        /// </summary>
        [Test]
        public void SoftGroundGivesUpMoreDustThanARoad()
        {
            float sand = DustTrail.Dustiness(SurfaceTuning.For(SurfaceKind.Sand));
            float grass = DustTrail.Dustiness(SurfaceTuning.For(SurfaceKind.Grass));
            float road = DustTrail.Dustiness(SurfaceTuning.For(SurfaceKind.Asphalt));

            Assert.That(sand, Is.GreaterThan(grass), $"sand {sand:0.00}, grass {grass:0.00}");
            Assert.That(grass, Is.GreaterThan(road), $"grass {grass:0.00}, road {road:0.00}");
            Assert.That(sand, Is.LessThanOrEqualTo(1.0f), "sand throws more than a full cloud");
            Assert.That(road, Is.GreaterThan(0.0f), "a road throws nothing at all");
        }

        [Test]
        public void WaterGivesUpNoDustAtAll()
        {
            foreach (SurfaceKind kind in SurfaceTuning.Roster())
            {
                SurfaceTuning surface = SurfaceTuning.For(kind);
                if (!surface.Drowns)
                {
                    continue;
                }

                Assert.That(DustTrail.Dustiness(surface), Is.Zero, kind.ToString());
                Assert.That(
                    DustTrail.RateFor(20.0f, 10.0f, surface, 30.0f), Is.Zero, kind.ToString());
            }
        }

        /// <summary>
        /// Dust is the ground in the air, so it has to be the ground's own colour and it has
        /// to be lighter than it - a trail that matched the grass exactly would be invisible
        /// over the thing it came off.
        /// </summary>
        [Test]
        public void DustIsThePaleVersionOfWhateverItCameOff()
        {
            foreach (SurfaceKind kind in new[] { SurfaceKind.Sand, SurfaceKind.Grass, SurfaceKind.Asphalt })
            {
                SurfaceTuning surface = SurfaceTuning.For(kind);
                Color dust = DustTrail.TintFor(surface);

                Assert.That(
                    dust.grayscale,
                    Is.GreaterThan(surface.Colour.grayscale),
                    $"{kind} dust is no lighter than {kind}");

                // Still recognisably that ground: the channel that led on the surface still
                // leads in its dust, which is what keeps sand sandy rather than merely pale.
                Assert.That(
                    dust.r - dust.b,
                    Is.EqualTo(surface.Colour.r - surface.Colour.b).Within(0.3f),
                    $"{kind} dust is a different colour from {kind}");
            }
        }

        [Test]
        public void NothingIsKickedUpByAVehicleThatIsBarelyMoving()
        {
            SurfaceTuning sand = SurfaceTuning.For(SurfaceKind.Sand);

            Assert.That(DustTrail.RateFor(0.0f, 10.0f, sand, 30.0f), Is.Zero, "parked");
            Assert.That(
                DustTrail.RateFor(DustTrail.MovingAbove, 10.0f, sand, 30.0f),
                Is.Zero,
                "creeping");
            Assert.That(
                DustTrail.RateFor(10.0f, 10.0f, sand, 30.0f),
                Is.GreaterThan(0.0f),
                "flat out");
        }

        /// <summary>
        /// Reversing kicks up exactly as much as driving forward, which falls out of taking
        /// the speed as a magnitude rather than out of a rule about reverse.
        /// </summary>
        [Test]
        public void ReversingRaisesAsMuchDustAsDrivingForwards()
        {
            SurfaceTuning grass = SurfaceTuning.For(SurfaceKind.Grass);

            Assert.That(
                DustTrail.RateFor(-6.0f, 10.0f, grass, 30.0f),
                Is.EqualTo(DustTrail.RateFor(6.0f, 10.0f, grass, 30.0f)).Within(0.0001f));
        }

        [Test]
        public void DustNeverExceedsThePeakRateItWasGiven()
        {
            SurfaceTuning sand = SurfaceTuning.For(SurfaceKind.Sand);

            Assert.That(
                DustTrail.RateFor(200.0f, 10.0f, sand, 30.0f),
                Is.LessThanOrEqualTo(30.0f),
                "a vehicle doing twenty times its top speed still only kicks up so much");
        }

        /// <summary>
        /// A shell in the sea throws spray instead of fire. All three conditions matter, and
        /// the one that is easy to forget is the middle one: a helicopter shot down over the
        /// bay was hit, so the round found a hull rather than the water.
        /// </summary>
        [Test]
        public void AShellInTheSeaThrowsSprayAndOneOnTheBeachDoesNot()
        {
            const float sea = -0.7f;

            Assert.That(Projectile.DrawsSpray(true, false, sea, sea), Is.True, "in the water");
            Assert.That(Projectile.DrawsSpray(false, false, sea, sea), Is.False, "on the sand");
            Assert.That(
                Projectile.DrawsSpray(true, true, sea, sea),
                Is.False,
                "it hit something floating over the water rather than the water");
            Assert.That(
                Projectile.DrawsSpray(true, false, sea + Projectile.SprayReach + 1.0f, sea),
                Is.False,
                "it went off in the air over the water");
        }

        [Test]
        public void EveryVehicleCarriesSomethingToSmokeWith()
        {
            foreach (VehicleKind kind in VehiclePrefabBuilder.Roster())
            {
                var smoke = LoadVehicle(kind).GetComponent<DamageSmoke>();

                Assert.That(smoke, Is.Not.Null, $"{kind} cannot smoke");
                Assert.That(smoke.Plume, Is.Not.Null, $"{kind} has no plume");
                Assert.That(smoke.Column, Is.Not.Null, $"{kind} leaves no column where it dies");
                Assert.That(smoke.ColumnSize, Is.GreaterThan(0.0f), kind.ToString());
            }
        }

        /// <summary>
        /// The helicopter is excluded by being the one vehicle that is not a
        /// <see cref="GroundVehicle"/>, rather than by anything naming it - so this is really
        /// a test that nothing has started naming it.
        /// </summary>
        [Test]
        public void EveryVehicleThatTouchesTheGroundKicksDustOffItAndTheOneThatDoesNotDoesNot()
        {
            foreach (VehicleKind kind in VehiclePrefabBuilder.Roster())
            {
                GameObject prefab = LoadVehicle(kind);
                var trail = prefab.GetComponent<DustTrail>();

                if (prefab.GetComponent<GroundVehicle>() == null)
                {
                    Assert.That(trail, Is.Null, $"{kind} flies and still raises dust");
                    continue;
                }

                Assert.That(trail, Is.Not.Null, $"{kind} drives and raises no dust");
                Assert.That(trail.Dust, Is.Not.Null, $"{kind}'s trail has no particles");
                Assert.That(trail.PeakRate, Is.GreaterThan(0.0f), kind.ToString());
            }
        }

        [Test]
        public void EveryDestructibleCarriesSomethingToSmokeWith()
        {
            DestructiblePrefabBuilder.EnsureAssets();

            foreach (StructureKind kind in StructureTuning.Roster())
            {
                GameObject prefab = DestructiblePrefabBuilder.Load(kind);
                Assert.That(prefab, Is.Not.Null, $"{kind} has no prefab");

                var smoke = prefab.GetComponent<DamageSmoke>();
                Assert.That(smoke, Is.Not.Null, $"{kind} cannot smoke");
                Assert.That(smoke.Plume, Is.Not.Null, $"{kind} has no plume");
                Assert.That(smoke.Column, Is.Not.Null, $"{kind} leaves no column where it falls");
            }
        }

        [Test]
        public void EveryRoundPutsUpSprayWhenItGoesIntoTheWater()
        {
            CombatPrefabBuilder.EnsureAssets();

            foreach (WeaponKind kind in CombatPrefabBuilder.Arsenal())
            {
                Projectile round = CombatPrefabBuilder.LoadProjectile(kind);

                Assert.That(round, Is.Not.Null, $"{kind} has no round");
                Assert.That(round.Splash, Is.Not.Null, $"a {kind} round in the sea makes no spray");
            }
        }

        /// <summary>
        /// One material for smoke, dust and spray alike, and the whole reason one is enough
        /// is that it is transparent and the shader multiplies by the particle's own colour.
        /// A material that came out opaque would render every effect as a cloud of solid
        /// boxes, which is the failure this catches.
        /// </summary>
        [Test]
        public void TheParticleMaterialIsTransparent()
        {
            GeneratedMaterials.EnsureAssets();
            Material particle = AssetDatabase.LoadAssetAtPath<Material>(
                GeneratedMaterials.PathOf(GeneratedMaterials.Particle));

            Assert.That(particle, Is.Not.Null, "the particle material was never created");
            Assert.That(
                particle.renderQueue,
                Is.GreaterThanOrEqualTo((int)UnityEngine.Rendering.RenderQueue.Transparent),
                "the particle material draws in the opaque queue");
            Assert.That(
                particle.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"),
                Is.True,
                "the particle material was not switched to a transparent surface");
        }

        [Test]
        public void BothStandaloneEffectsExistAndCountThemselvesOut()
        {
            VfxPrefabBuilder.EnsureAssets();

            foreach (ParticleBurst burst in new[]
            {
                VfxPrefabBuilder.LoadSmokeColumn(), VfxPrefabBuilder.LoadSplash(),
            })
            {
                Assert.That(burst, Is.Not.Null, "an effect prefab is missing");
                Assert.That(burst.Duration, Is.GreaterThan(0.0f), burst.name);
                Assert.That(burst.AuthoredSize, Is.GreaterThan(0.0f), burst.name);
                Assert.That(
                    burst.GetComponentsInChildren<ParticleSystem>(true).Length,
                    Is.GreaterThan(0),
                    $"{burst.name} has no particles in it");
            }
        }

        /// <summary>
        /// A wheel track and a dust cloud are the same fact about loose ground, so they come
        /// out of the same column and cannot disagree about which surface is soft.
        /// </summary>
        [Test]
        public void TheGroundThatThrowsDustIsTheGroundThatTakesARut()
        {
            float sand = TyreTracks.Darkness(SurfaceTuning.For(SurfaceKind.Sand));
            float grass = TyreTracks.Darkness(SurfaceTuning.For(SurfaceKind.Grass));
            float road = TyreTracks.Darkness(SurfaceTuning.For(SurfaceKind.Asphalt));

            Assert.That(sand, Is.GreaterThan(grass), "a beach takes no deeper a rut than a field");
            Assert.That(grass, Is.GreaterThan(road), "a road takes as deep a rut as a field");
            Assert.That(sand, Is.LessThanOrEqualTo(TyreTracks.Darkest), "a rut took more than it may");
        }

        /// <summary>
        /// Nothing marks water. A vehicle over the sea is a vehicle about to drown, and two
        /// lines drawn on the waves behind it would be cheerful about it.
        /// </summary>
        [Test]
        public void NothingLeavesTracksOnWater()
        {
            foreach (SurfaceKind kind in SurfaceTuning.Roster())
            {
                if (!SurfaceTuning.For(kind).Drowns)
                {
                    continue;
                }

                Assert.That(TyreTracks.Darkness(SurfaceTuning.For(kind)), Is.EqualTo(0.0f), kind.ToString());
            }
        }

        /// <summary>
        /// The small guns leave nothing and the big ones leave a scorch.
        /// </summary>
        /// <remarks>
        /// Without a floor on this a firefight would carpet the ground inside ten seconds:
        /// the chaingun and the autocannon fire several rounds a second each and neither has
        /// a splash radius at all.
        /// </remarks>
        [Test]
        public void OnlyTheShotsThatAreEventsLeaveAScorch()
        {
            Assert.That(
                Projectile.BlastRadius(WeaponTuning.For(VehicleKind.Helicopter)),
                Is.LessThan(GroundMark.SmallestBlast),
                "the helicopter's chaingun scorches the ground");
            Assert.That(
                Projectile.BlastRadius(WeaponTuning.For(VehicleKind.Tank)),
                Is.GreaterThan(GroundMark.SmallestBlast),
                "the tank's gun leaves no mark");
            Assert.That(
                Projectile.BlastRadius(WeaponTuning.For(VehicleKind.Asv)),
                Is.GreaterThan(GroundMark.SmallestBlast),
                "the ASV's rockets leave no mark");
        }

        /// <summary>
        /// A mark spawned in the editor is a mark nothing will ever step, so nothing spawns
        /// one there.
        /// </summary>
        /// <remarks>
        /// The doors pass lost an afternoon to a <c>DebrisBurst</c> frozen at the origin of
        /// every structure a preview had knocked down, which read in the still as the rubble
        /// being a metre tall. A scorch would fail the same way and be harder to spot,
        /// because a dark patch on the ground looks like a dark patch on the ground.
        /// </remarks>
        [Test]
        public void NothingScorchesTheGroundOutsidePlayMode()
        {
            VfxPrefabBuilder.EnsureAssets();
            GroundMark scorch = VfxPrefabBuilder.LoadScorch();

            Assert.That(scorch, Is.Not.Null, "there is no scorch prefab");
            Assert.That(
                GroundMark.Spawn(scorch, Vector3.zero, 4.0f, GroundMark.Fade),
                Is.Null,
                "a scorch was left in a scene nothing is playing");
        }

        /// <summary>
        /// The scorch prefab is a square metre of geometry with the vertex colours the mark
        /// shader reads, and it is on that shader.
        /// </summary>
        /// <remarks>
        /// The colours are the part worth checking. <c>RF_Mark.shader</c> reads vertex alpha
        /// as how much of the mark is left, and a mesh with no colour channel hands it
        /// whatever happens to be in that register - which is a scorch that is there or not
        /// depending on the machine.
        /// </remarks>
        [Test]
        public void TheScorchIsAFlatQuadOnTheMarkShader()
        {
            VfxPrefabBuilder.EnsureAssets();
            GroundMark scorch = VfxPrefabBuilder.LoadScorch();
            Assert.That(scorch, Is.Not.Null, "there is no scorch prefab");

            Mesh quad = scorch.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(quad, Is.Not.Null, "the scorch has no geometry");
            Assert.That(quad.vertexCount, Is.EqualTo(4), "a quad has four corners");
            Assert.That(quad.colors.Length, Is.EqualTo(4), "the shader has no fade to read");
            Assert.That(quad.bounds.size.y, Is.EqualTo(0.0f).Within(0.0001f), "the scorch stands up");

            Assert.That(
                scorch.GetComponent<Renderer>().sharedMaterial.shader.name,
                Is.EqualTo(GeneratedMaterials.MarkShaderName));
        }

        /// <summary>
        /// A mark is scaled to the thing that made it, so one prefab serves a grenade and a
        /// wreck.
        /// </summary>
        [Test]
        public void AMarkIsTheSizeItIsToldToBe()
        {
            var host = new GameObject("Mark");
            try
            {
                GroundMark mark = host.AddComponent<GroundMark>();
                mark.Wire(null, 1.0f);
                mark.Configure(6.0f, GroundMark.Fade);

                Assert.That(host.transform.localScale.x, Is.EqualTo(6.0f).Within(0.001f));
                Assert.That(mark.Life, Is.EqualTo(GroundMark.Fade).Within(0.001f));

                mark.Configure(6.0f, 0.0f);
                Assert.That(mark.Life, Is.EqualTo(0.0f), "a collapse's burn is supposed to stay");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// Everything that drives leaves two lines behind it, and the one thing that flies
        /// leaves none.
        /// </summary>
        [Test]
        public void EveryGroundVehicleLeavesTracksAndTheHelicopterLeavesNone()
        {
            foreach (VehicleKind kind in VehiclePrefabBuilder.Roster())
            {
                GameObject prefab = LoadVehicle(kind);
                var tracks = prefab.GetComponent<TyreTracks>();

                if (prefab.GetComponent<GroundVehicle>() == null)
                {
                    Assert.That(tracks, Is.Null, $"{kind} flies and still leaves wheel tracks");
                    continue;
                }

                Assert.That(tracks, Is.Not.Null, $"{kind} drives and leaves no tracks");
                Assert.That(tracks.Left, Is.Not.Null, $"{kind} has no left track");
                Assert.That(tracks.Right, Is.Not.Null, $"{kind} has no right track");
                Assert.That(
                    tracks.Left.transform.localPosition.x,
                    Is.LessThan(tracks.Right.transform.localPosition.x),
                    $"{kind}'s tracks are the wrong way round");
                Assert.That(
                    tracks.Left.emitting, Is.False, $"{kind} is marking the ground in its box");
            }
        }

        /// <summary>
        /// Every blast in the game comes through one factory, so binding the scorch to that
        /// one prefab is what puts a mark under all of them.
        /// </summary>
        [Test]
        public void TheExplosionCarriesTheScorchForEverything()
        {
            CombatPrefabBuilder.EnsureAssets();

            Explosion blast = CombatPrefabBuilder.LoadExplosion();
            Assert.That(blast, Is.Not.Null, "there is no explosion prefab");
            Assert.That(blast.Scorch, Is.Not.Null, "no blast in the game marks the ground");
        }

        private static GameObject LoadVehicle(VehicleKind kind)
            => AssetDatabase.LoadAssetAtPath<GameObject>(VehiclePrefabBuilder.PrefabPathFor(kind));
    }
}
