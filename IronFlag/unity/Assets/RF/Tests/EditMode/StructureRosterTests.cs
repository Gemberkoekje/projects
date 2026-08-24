using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using IronFlag.Combat;
using IronFlag.Destruction;
using IronFlag.Editor.Gameplay;
using IronFlag.Vehicles;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// The destructibles read against each other and against the guns that have to knock
    /// them down, plus the arithmetic of the swap and of the mess it makes.
    /// </summary>
    /// <remarks>
    /// Nothing here presses Play. What a hit does to a wall is a pure function of the
    /// numbers, and how far a piece of that wall has flown after half a second is a pure
    /// function of time - both are deliberately static so that they can be read here rather
    /// than watched. What only exists once time passes is in the play-mode suite.
    /// </remarks>
    public sealed class StructureRosterTests
    {
        /// <summary>
        /// The scale everything else is read against: a structure's hit points are in the
        /// same unit as a vehicle's, so a building has to be worth more than a vehicle or
        /// cover is softer than the things hiding behind it.
        /// </summary>
        [Test]
        public void CoverIsTougherThanTheVehiclesHidingBehindIt()
        {
            float toughestVehicle = 0.0f;
            foreach (VehicleKind kind in VehiclePrefabBuilder.Roster())
            {
                toughestVehicle = Mathf.Max(toughestVehicle, VehicleTuning.For(kind).HitPoints);
            }

            foreach (StructureKind kind in new[]
            {
                StructureKind.BuildingA, StructureKind.BuildingB, StructureKind.Bridge,
            })
            {
                Assert.That(
                    StructureTuning.For(kind).HitPoints,
                    Is.GreaterThan(toughestVehicle),
                    $"{kind} is softer than the toughest vehicle, so shooting it is never a choice");
            }
        }

        /// <summary>
        /// A tree is the one piece of cover anybody can remove in passing, so it has to be
        /// worth a couple of rounds from the weakest gun in the game rather than a sortie.
        /// </summary>
        [Test]
        public void ATreeComesDownToAHandfulOfGrenades()
        {
            float grenade = WeaponTuning.For(VehicleKind.Jeep).Damage;
            float rounds = StructureTuning.For(StructureKind.Tree).HitPoints / grenade;

            Assert.That(rounds, Is.GreaterThan(1.0f), "a tree falls to a single grenade");
            Assert.That(rounds, Is.LessThan(4.0f), "clearing a tree costs a jeep most of its load");
        }

        /// <summary>
        /// Every destructible has to be worth going out of your way for: one full load from
        /// the vehicle that carries the most damage should not be enough to flatten the map,
        /// and nothing should need more than one load to knock down on its own.
        /// </summary>
        [Test]
        public void EveryStructureFallsToOneFullLoadAndNoneToASingleRound()
        {
            WeaponTuning chaingun = WeaponTuning.For(VehicleKind.Helicopter);
            float load = chaingun.Damage * chaingun.Rounds;

            foreach (StructureKind kind in StructureTuning.Roster())
            {
                StructureTuning tuning = StructureTuning.For(kind);

                Assert.That(
                    tuning.HitPoints,
                    Is.LessThan(load),
                    $"{kind} survives the biggest load in the game, so it can never be removed");
                Assert.That(
                    tuning.HitPoints,
                    Is.GreaterThan(WeaponTuning.For(VehicleKind.Tank).Damage),
                    $"{kind} comes down to one cannon shell");
            }
        }

        /// <summary>
        /// A depot is a target worth a detour, so it has to be softer than the buildings
        /// that are only in the way - and both hold the same, so neither side's fuel is
        /// safer than their ammunition.
        /// </summary>
        [Test]
        public void ADepotIsSofterThanABuildingAndTheTwoDepotsMatch()
        {
            float fuel = StructureTuning.For(StructureKind.DepotFuel).HitPoints;
            float ammo = StructureTuning.For(StructureKind.DepotAmmo).HitPoints;

            Assert.That(fuel, Is.EqualTo(ammo), "one depot is harder to take away than the other");
            Assert.That(
                fuel,
                Is.LessThan(StructureTuning.For(StructureKind.BuildingA).HitPoints),
                "a depot is tougher than the building next to it");
        }

        [Test]
        public void TheDamagedMeshComesInHalfwayThroughThePool()
        {
            StructureTuning tuning = StructureTuning.For(StructureKind.BuildingA);

            Assert.That(tuning.StateAt(1.0f, true), Is.EqualTo(DestructionState.Intact));
            Assert.That(tuning.StateAt(tuning.DamagedAt + 0.01f, true), Is.EqualTo(DestructionState.Intact));
            Assert.That(tuning.StateAt(tuning.DamagedAt, true), Is.EqualTo(DestructionState.Damaged));
            Assert.That(tuning.StateAt(0.01f, true), Is.EqualTo(DestructionState.Damaged));
            Assert.That(tuning.StateAt(0.0f, true), Is.EqualTo(DestructionState.Destroyed));
        }

        /// <summary>
        /// The bridge has two states rather than three, and a structure with no middle mesh
        /// has to stay whole until it is rubble rather than showing nothing at all.
        /// </summary>
        [Test]
        public void AStructureWithNoDamagedMeshGoesStraightToRubble()
        {
            StructureTuning tuning = StructureTuning.For(StructureKind.Bridge);

            Assert.That(tuning.StateAt(0.5f, false), Is.EqualTo(DestructionState.Intact));
            Assert.That(tuning.StateAt(0.01f, false), Is.EqualTo(DestructionState.Intact));
            Assert.That(tuning.StateAt(0.0f, false), Is.EqualTo(DestructionState.Destroyed));
        }

        [Test]
        public void DebrisIsThrownOutwardsAndUpwardsAndThenFalls()
        {
            const int count = 10;
            const float spread = 4.0f;

            Vector3 early = DebrisBurst.Offset(0, count, spread, 0.05f);
            Assert.That(early.y, Is.GreaterThan(0.0f), "debris starts by going into the ground");

            float apex = 0.0f;
            float peak = 0.0f;
            for (float t = 0.0f; t < 1.0f; t += 0.02f)
            {
                float height = DebrisBurst.Offset(0, count, spread, t).y;
                if (height > apex)
                {
                    apex = height;
                    peak = t;
                }
            }

            Assert.That(apex, Is.GreaterThan(0.2f), "debris barely leaves the floor");
            Assert.That(peak, Is.LessThan(0.5f), "debris hangs in the air on the way up");
            Assert.That(
                DebrisBurst.Offset(0, count, spread, 1.0f).y,
                Is.LessThan(apex),
                "debris never comes back down");
        }

        /// <summary>
        /// Two chunks in the same burst have to end up somewhere different, or the fan is a
        /// single lump of cubes.
        /// </summary>
        [Test]
        public void NoTwoChunksFollowTheSamePath()
        {
            const int count = 10;

            for (int index = 1; index < count; index++)
            {
                Assert.That(
                    Vector3.Distance(
                        DebrisBurst.Offset(index, count, 4.0f, 0.3f),
                        DebrisBurst.Offset(index - 1, count, 4.0f, 0.3f)),
                    Is.GreaterThan(0.3f),
                    $"chunks {index - 1} and {index} fly together");
            }
        }

        [Test]
        public void DebrisHoldsItsSizeAndThenShrinksAway()
        {
            Assert.That(DebrisBurst.Scale(0.0f), Is.EqualTo(1.0f).Within(0.001f));
            Assert.That(DebrisBurst.Scale(0.5f), Is.EqualTo(1.0f).Within(0.001f));
            Assert.That(DebrisBurst.Scale(0.85f), Is.LessThan(1.0f));
            Assert.That(DebrisBurst.Scale(1.0f), Is.EqualTo(0.0f).Within(0.001f));
        }

        /// <summary>
        /// The prefabs are assembled from the exported models, so a destructible that lost
        /// one of its meshes is a building that cannot be knocked down.
        /// </summary>
        [Test]
        public void EveryDestructibleHasAPrefabWithItsStatesAndItsNumbers()
        {
            DestructiblePrefabBuilder.EnsureAssets();

            foreach (StructureKind kind in StructureTuning.Roster())
            {
                GameObject prefab = DestructiblePrefabBuilder.Load(kind);
                Assert.That(prefab, Is.Not.Null, $"{kind} has no prefab");

                var structure = prefab.GetComponent<Destructible>();
                Assert.That(structure, Is.Not.Null, $"{kind} cannot be shot");
                Assert.That(structure.Kind, Is.EqualTo(kind), $"{kind} is stamped as something else");
                Assert.That(
                    structure.Tuning.HitPoints,
                    Is.EqualTo(StructureTuning.For(kind).HitPoints),
                    $"{kind} carries numbers the table does not");

                Assert.That(
                    prefab.transform.Find(Destructible.IntactNodeName),
                    Is.Not.Null,
                    $"{kind} has no intact model");
                Assert.That(
                    prefab.transform.Find(Destructible.DestroyedNodeName),
                    Is.Not.Null,
                    $"{kind} has no rubble");
                Assert.That(
                    structure.HasDamagedState,
                    Is.EqualTo(kind != StructureKind.Bridge),
                    $"{kind} does not have the states the asset spec gives it");
            }
        }

        /// <summary>
        /// Each state carries its own colliders, which is what makes a rubble pile a
        /// different shape from the building that stood there.
        /// </summary>
        /// <remarks>
        /// Every kind, not just a building. This is the end-to-end half of "is it really
        /// destructible": a state model that arrived without geometry is a prop a vehicle
        /// drives through while it is still standing, and nothing else in the suite would
        /// notice. Whether the <em>rubble</em> is solid is a separate question with a
        /// separate answer - it is not, deliberately - and that is
        /// <c>DestructionTests.RubbleHasNoHitboxLeft</c>'s business, because it is a
        /// decision made at runtime rather than something missing from the asset.
        /// </remarks>
        [Test]
        public void EveryStateHasSomethingToBumpInto()
        {
            DestructiblePrefabBuilder.EnsureAssets();

            foreach (StructureKind kind in StructureTuning.Roster())
            {
                GameObject prefab = DestructiblePrefabBuilder.Load(kind);
                Assert.That(prefab, Is.Not.Null, $"{kind} has no prefab");

                foreach (string node in new[]
                {
                    Destructible.IntactNodeName,
                    Destructible.DamagedNodeName,
                    Destructible.DestroyedNodeName,
                })
                {
                    Transform state = prefab.transform.Find(node);
                    if (state == null)
                    {
                        // Only the bridge is allowed to be missing one, and which one is
                        // checked by EveryDestructibleHasAPrefabWithItsStatesAndItsNumbers.
                        continue;
                    }

                    Assert.That(
                        state.GetComponentsInChildren<MeshCollider>(true).Length,
                        Is.GreaterThan(0),
                        $"the {kind}'s {node} state can be driven through");
                }
            }
        }

        /// <summary>
        /// Nothing on the map is destructible in name only. Every kind the enum has is
        /// either something a level scatters or the objective, both have a full set of
        /// numbers, and neither can be shrugged off.
        /// </summary>
        /// <remarks>
        /// The audit the design document's backlog asks for, as a test rather than as a
        /// reading: the enum, the roster, the tuning table and the model inventory each grow
        /// separately, and a kind that made it into one of the four and not the others is a
        /// building somewhere that cannot be shot down.
        /// </remarks>
        [Test]
        public void EveryStructureKindIsDestructibleEndToEnd()
        {
            foreach (StructureKind kind in Enum.GetValues(typeof(StructureKind)))
            {
                if (kind == StructureKind.None)
                {
                    continue;
                }

                bool scattered = Array.IndexOf(StructureTuning.Roster(), kind) >= 0;
                Assert.That(
                    scattered || kind == StructureKind.FlagTower,
                    Is.True,
                    $"{kind} is a destructible no level can place and no objective builds");

                StructureTuning tuning = StructureTuning.For(kind);
                Assert.That(
                    tuning.HitPoints,
                    Is.GreaterThan(0.0f),
                    $"{kind} has no pool, so shooting it does nothing for ever");
                Assert.That(
                    tuning.DebrisRadius,
                    Is.GreaterThan(0.0f),
                    $"{kind} comes apart without making a mess, so nothing reads as a hit");
                Assert.That(
                    tuning.StateAt(0.0f, false),
                    Is.EqualTo(DestructionState.Destroyed),
                    $"an empty {kind} is not rubble");
            }
        }

        /// <summary>
        /// The turret is the only structure that belongs to a side, and the level format,
        /// the validator and the mirror tool all turn on that one answer.
        /// </summary>
        [Test]
        public void OnlyTheTurretBelongsToASide()
        {
            foreach (StructureKind kind in Enum.GetValues(typeof(StructureKind)))
            {
                Assert.That(
                    StructureTuning.BelongsToASide(kind),
                    Is.EqualTo(kind == StructureKind.Turret),
                    $"{kind} disagrees with the rest of the game about whose it is");
            }
        }

        /// <summary>
        /// The emplacement is built with a gun in the states that still have a barrel and
        /// none in the rubble, and the part that traverses carries no collider.
        /// </summary>
        /// <remarks>
        /// The rubble having no turret node is what makes a wrecked emplacement silent by
        /// construction rather than by a check; the traversing part having no collider is
        /// what stops a mesh collider being rebuilt by physics every frame it swings.
        /// </remarks>
        [Test]
        public void TheTurretPrefabCarriesAGunOnEveryStateThatStillHasABarrel()
        {
            DestructiblePrefabBuilder.EnsureAssets();
            GameObject prefab = DestructiblePrefabBuilder.Load(StructureKind.Turret);
            Assert.That(prefab, Is.Not.Null, "the turret has no prefab");

            var gun = prefab.GetComponent<VehicleWeapon>();
            Assert.That(gun, Is.Not.Null, "the turret cannot fire");
            Assert.That(
                gun.Tuning.Kind,
                Is.EqualTo(WeaponTuning.Emplacement().Kind),
                "the turret carries a gun the table does not give it");

            var turret = prefab.GetComponent<AutoTurret>();
            Assert.That(turret, Is.Not.Null, "the turret has nobody aiming it");
            Assert.That(turret.TurnRate, Is.GreaterThan(0.0f), "the gun cannot traverse");

            foreach (DestructionState state in new[]
            {
                DestructionState.Intact, DestructionState.Damaged,
            })
            {
                Transform head = Traverse(prefab, state);
                Assert.That(head, Is.Not.Null, $"the {state} turret has nothing that traverses");
                Assert.That(
                    Find(head, AutoTurret.MuzzlePointName),
                    Is.Not.Null,
                    $"the {state} turret has no firing point; re-run the Blender art pipeline");
                Assert.That(
                    head.GetComponentsInChildren<Collider>(true).Length,
                    Is.Zero,
                    $"the {state} turret's head carries a collider that moves every frame");
            }

            Assert.That(
                Traverse(prefab, DestructionState.Destroyed),
                Is.Null,
                "the rubble still has a gun on it");
        }

        /// <summary>
        /// The emplacement's gun read against the four the roster carries: shorter than the
        /// tank, so an emplacement can be fought from outside its own reach by the vehicle
        /// built to; and no blast, so it cannot clear the ground around itself.
        /// </summary>
        [Test]
        public void TheEmplacementIsOutrangedByTheTankAndHasNoBlast()
        {
            WeaponTuning gun = WeaponTuning.Emplacement();

            Assert.That(gun.Exists, Is.True, "the emplacement carries nothing");
            Assert.That(
                gun.Range,
                Is.LessThan(WeaponTuning.For(VehicleKind.Tank).Range),
                "a turret outranges the one vehicle sent to remove it");
            Assert.That(
                gun.SplashRadius,
                Is.EqualTo(0.0f),
                "a turret that splashes clears the cover a raider needs to reach it");
            Assert.That(
                gun.ArmingDistance,
                Is.EqualTo(0.0f),
                "a turret cannot defend the ground it is standing on");
            Assert.That(
                StructureTuning.For(StructureKind.Turret).HitPoints,
                Is.LessThan(StructureTuning.For(StructureKind.BuildingA).HitPoints),
                "an emplacement is harder to remove than the building beside it");
        }

        /// <summary>
        /// Returns the traversing part of one of the turret's states, or null.
        /// </summary>
        /// <param name="prefab">The turret prefab.</param>
        /// <param name="state">State to look inside.</param>
        /// <returns>The turret node, or <c>null</c> when that state has none.</returns>
        private static Transform Traverse(GameObject prefab, DestructionState state)
        {
            Transform model = prefab.transform.Find(Destructible.NodeNameFor(state));
            return model == null ? null : Find(model, AutoTurret.TurretNodeName);
        }

        /// <summary>
        /// Finds a named object anywhere under a root, including switched-off ones.
        /// </summary>
        /// <param name="root">Object to search under.</param>
        /// <param name="name">Name to look for.</param>
        /// <returns>The transform, or <c>null</c>.</returns>
        private static Transform Find(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }

        /// <summary>
        /// The debris is generated rather than modelled, like the tracer and the flash, so
        /// the thing that says how many pieces a building comes apart into is the prefab.
        /// </summary>
        [Test]
        public void TheDebrisPrefabCarriesChunksAndNoColliders()
        {
            CombatPrefabBuilder.EnsureAssets();
            DebrisBurst debris = CombatPrefabBuilder.LoadDebris();

            Assert.That(debris, Is.Not.Null, "there is no debris prefab");
            Assert.That(debris.ChunkCount, Is.GreaterThan(4), "a building comes apart into too little");
            Assert.That(
                debris.GetComponentsInChildren<Collider>(true).Length,
                Is.EqualTo(0),
                "debris would leave an invisible wall where the building stood");
            Assert.That(debris.Duration, Is.LessThan(2.0f), "debris outstays the explosion that threw it");
        }

        /// <summary>
        /// The models are the asset spec's, and this is the only place the two naming
        /// schemes meet.
        /// </summary>
        [Test]
        public void EveryStateModelTheSpecPromisesIsOnDisk()
        {
            foreach (StructureKind kind in StructureTuning.Roster())
            {
                foreach (DestructionState state in new[]
                {
                    DestructionState.Intact, DestructionState.Destroyed,
                })
                {
                    string path =
                        $"{DestructiblePrefabBuilder.ModelFolder}/"
                        + $"{DestructiblePrefabBuilder.ModelNameFor(kind, state)}.glb";

                    Assert.That(
                        AssetDatabase.LoadAssetAtPath<GameObject>(path),
                        Is.Not.Null,
                        $"{path} is missing; re-run the Blender art pipeline");
                }
            }
        }
    }
}
