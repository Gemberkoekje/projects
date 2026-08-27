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
        /// The turret and the door are the only structures that belong to a side, and the
        /// level format, the validator, the builder and the mirror tool all turn on that
        /// one answer.
        /// </summary>
        /// <remarks>
        /// Written as an explicit set rather than as a repeat of
        /// <see cref="StructureTuning.BelongsToASide"/>'s own expression, so that widening
        /// the rule is a deliberate edit here as well as there. A test that said
        /// <c>BelongsToASide(kind) == BelongsToASide(kind)</c> would pass whatever the game
        /// believed.
        /// </remarks>
        [Test]
        public void OnlyTheTurretAndTheDoorBelongToASide()
        {
            var owned = new[] { StructureKind.Turret, StructureKind.Door };

            foreach (StructureKind kind in Enum.GetValues(typeof(StructureKind)))
            {
                Assert.That(
                    StructureTuning.BelongsToASide(kind),
                    Is.EqualTo(Array.IndexOf(owned, kind) >= 0),
                    $"{kind} disagrees with the rest of the game about whose it is");
            }
        }

        /// <summary>
        /// A gate is built with a leaf in the states that can still open and none in the
        /// rubble, and the part that slides keeps its colliders and gets a kinematic body.
        /// </summary>
        /// <remarks>
        /// The exact inverse of what
        /// <see cref="TheTurretPrefabCarriesAGunOnEveryStateThatStillHasABarrel"/> demands,
        /// and both are asserted because the reasoning is opposite rather than absent. A
        /// turret's head is stripped of colliders because it moves and the base underneath
        /// is what a vehicle bumps into; a gate's leaf has no stand-in, so being solid is
        /// the whole of what it is for and the moving-collider cost is paid with a
        /// kinematic rigidbody instead. Losing either half is silent: colliders gone is a
        /// gate that looks shut and is not, and the body gone is a gate that works and
        /// quietly dirties the static physics scene every step it moves.
        /// </remarks>
        [Test]
        public void TheDoorPrefabCarriesASolidLeafOnEveryStateThatStillOpens()
        {
            DestructiblePrefabBuilder.EnsureAssets();
            GameObject prefab = DestructiblePrefabBuilder.Load(StructureKind.Door);
            Assert.That(prefab, Is.Not.Null, "the door has no prefab");

            var door = prefab.GetComponent<AutoDoor>();
            Assert.That(door, Is.Not.Null, "the door has nobody opening it");
            Assert.That(door.Reach, Is.GreaterThan(0.0f), "the gate never notices anybody");
            Assert.That(door.Speed, Is.GreaterThan(0.0f), "the leaf never moves");

            foreach (DestructionState state in new[]
            {
                DestructionState.Intact, DestructionState.Damaged,
            })
            {
                Transform leaf = Sliding(prefab, state);
                Assert.That(leaf, Is.Not.Null, $"the {state} gate has nothing that opens");
                Assert.That(
                    leaf.GetComponentsInChildren<Collider>(true).Length,
                    Is.GreaterThan(0),
                    $"the {state} gate's leaf can be driven through while it is shut");

                var body = leaf.GetComponent<Rigidbody>();
                Assert.That(
                    body,
                    Is.Not.Null,
                    $"the {state} gate's leaf is a static collider that moves");
                Assert.That(
                    body.isKinematic,
                    Is.True,
                    $"the {state} gate's leaf can be pushed, so a tank could shove it open");
            }

            Assert.That(
                Sliding(prefab, DestructionState.Destroyed),
                Is.Null,
                "the rubble still has a gate in it, so a wrecked door could close again");
        }

        /// <summary>
        /// The fastest vehicle in the game must not have to slow down at its own gate.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the one requirement <see cref="DestructiblePrefabBuilder.DoorReach"/>
        /// and <see cref="DestructiblePrefabBuilder.DoorLeafSpeed"/> were picked from, so
        /// it is checked rather than described: a gate its owner had to brake for is a gate
        /// its owner drives round, and the jeep - the only vehicle that can carry the flag
        /// - is the one that would be doing the braking.
        /// </para>
        /// <para>
        /// The drop is measured off the built prefab rather than written down again, so
        /// re-exporting a taller gate is caught here instead of in a match.
        /// </para>
        /// </remarks>
        [Test]
        public void AJeepNeverHasToWaitAtItsOwnGate()
        {
            DestructiblePrefabBuilder.EnsureAssets();
            GameObject prefab = DestructiblePrefabBuilder.Load(StructureKind.Door);
            Assert.That(prefab, Is.Not.Null, "the door has no prefab");

            Transform leaf = Sliding(prefab, DestructionState.Intact);
            float drop = AutoDoor.TravelFor(prefab.transform, leaf);
            Assert.That(drop, Is.GreaterThan(0.0f), "the leaf has nowhere to go");

            var door = prefab.GetComponent<AutoDoor>();
            float toOpen = drop / door.Speed;
            float toArrive = door.Reach / VehicleTuning.For(VehicleKind.Jeep).MaxSpeed;

            Assert.That(
                toArrive,
                Is.GreaterThan(toOpen),
                $"a jeep covers the gate's {door.Reach:0.#} m reach in {toArrive:0.00} s and the "
                + $"leaf takes {toOpen:0.00} s to drop, so its own side drives into it");
        }

        /// <summary>
        /// A gate is the softest part of any run it stands in.
        /// </summary>
        /// <remarks>
        /// The oldest rule in fortification, and the only arrangement that keeps both
        /// pieces worth placing. A gate tougher than the wall would mean building the whole
        /// run out of gates; a gate equal to it would be a wall that happens to open. It
        /// still has to survive one round of everything, which is what keeps a breach a
        /// decision rather than a reflex - see
        /// <see cref="EveryStructureFallsToOneFullLoadAndNoneToASingleRound"/>, which
        /// checks the tank's shell, and the ASV's rocket below, which is the biggest single
        /// round in the game and the one that comes closest.
        /// </remarks>
        [Test]
        public void AGateIsTheWeakPointOfTheWallItStandsIn()
        {
            float gate = StructureTuning.For(StructureKind.Door).HitPoints;
            float wall = StructureTuning.For(StructureKind.Wall).HitPoints;

            Assert.That(
                gate,
                Is.LessThan(wall),
                "the gate is no cheaper to breach than the wall, so nothing is gained by aiming at it");

            float heaviest = 0.0f;
            foreach (VehicleKind kind in Enum.GetValues(typeof(VehicleKind)))
            {
                heaviest = Mathf.Max(heaviest, WeaponTuning.For(kind).Damage);
            }

            Assert.That(
                gate,
                Is.GreaterThan(heaviest),
                $"the biggest single round in the game does {heaviest:0.#}, so one shot opens a gate");
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
            Assert.That(gun.Flash, Is.Not.Null, "the turret fires without a muzzle flash");

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
        /// The rate of fire is what decides whether an emplacement can be taken on at all,
        /// so it is read against the two vehicles that would try: the tank wins the
        /// exchange and pays most of its armour for it, and the jeep loses.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The relationship rather than the two numbers, because the numbers are taste and
        /// this is not. A turret that beat the tank sent to remove it would leave exactly
        /// one decision on the map - bring the gun that outranges it - and a turret the jeep
        /// could shoot its way past would break the design document's first pillar, which is
        /// that everything else exists to clear the jeep's path. The gun is tuned to sit
        /// between those, and this is where that is written down.
        /// </para>
        /// <para>
        /// A straight trade, standing still, which is the worst way either vehicle could do
        /// it and the only one that can be read off the table at all. Anything a player
        /// actually does - reversing out, circling, using the sixteen metres of standoff -
        /// is better than this for the vehicle and worse for the emplacement.
        /// </para>
        /// </remarks>
        [Test]
        public void TheEmplacementLosesToTheTankAndBeatsTheJeep()
        {
            WeaponTuning emplacement = WeaponTuning.Emplacement();
            float pool = StructureTuning.For(StructureKind.Turret).HitPoints;

            foreach (VehicleKind kind in new[] { VehicleKind.Tank, VehicleKind.Jeep })
            {
                WeaponTuning carried = WeaponTuning.For(kind);
                VehicleTuning hull = VehicleTuning.For(kind);

                float seconds = pool / carried.DamagePerSecond;
                float taken = seconds * emplacement.DamagePerSecond;

                Assert.That(
                    taken < hull.HitPoints,
                    Is.EqualTo(kind == VehicleKind.Tank),
                    $"a {kind} trading with an emplacement needs {seconds:0.#} s and takes "
                        + $"{taken:0.#} of its {hull.HitPoints:0.#} hit points doing it");
            }
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
        /// Returns the sliding part of one of the door's states, or null.
        /// </summary>
        /// <param name="prefab">The door prefab.</param>
        /// <param name="state">State to look inside.</param>
        /// <returns>The leaf node, or <c>null</c> when that state has none.</returns>
        private static Transform Sliding(GameObject prefab, DestructionState state)
        {
            Transform model = prefab.transform.Find(Destructible.NodeNameFor(state));
            return model == null ? null : Find(model, AutoDoor.LeafNodeName);
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
        /// The pitch a run is laid at is the length the segments were actually modelled to.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="IronFlag.Editing.LevelEdits.SegmentLength"/> is the one number in the
        /// game that has to agree with two different files it cannot see: <c>prop_wall.py</c>
        /// and <c>structure_door.py</c>. Everything that lays a run - the generator's
        /// <c>Rampart</c>, and a person clicking along the editor's 5 m grid - trusts it, and
        /// a wall re-exported at 4.8 m would come out as a row of boxes with gaps in it that
        /// nothing else in the project would notice.
        /// </para>
        /// <para>
        /// Measured off the built prefabs rather than compared against a second copy of the
        /// number, which is the same discipline the walls pass arrived at the hard way: ask
        /// what was built, not what the tuple said.
        /// </para>
        /// </remarks>
        [Test]
        public void TheSegmentLengthIsWhatTheModelsWereBuiltTo()
        {
            DestructiblePrefabBuilder.EnsureAssets();

            foreach (StructureKind kind in new[] { StructureKind.Wall, StructureKind.Door })
            {
                GameObject prefab = DestructiblePrefabBuilder.Load(kind);
                Assert.That(prefab, Is.Not.Null, $"{kind} has no prefab");

                Transform model = prefab.transform.Find(
                    Destructible.NodeNameFor(DestructionState.Intact));
                Assert.That(model, Is.Not.Null, $"{kind} has no intact model");

                Renderer[] parts = model.GetComponentsInChildren<Renderer>(true);
                Assert.That(parts.Length, Is.GreaterThan(0), $"{kind} draws nothing");

                Bounds box = parts[0].bounds;
                for (int part = 1; part < parts.Length; part++)
                {
                    box.Encapsulate(parts[part].bounds);
                }

                Assert.That(
                    box.size.x,
                    Is.EqualTo(IronFlag.Editing.LevelEdits.SegmentLength).Within(0.01f),
                    $"a {kind} is {box.size.x:0.###} m long, so a run of them laid at "
                    + $"{IronFlag.Editing.LevelEdits.SegmentLength} m does not butt up");
            }
        }

        /// <summary>
        /// The three built structures read as a height sequence: a wall, a gun tower at twice
        /// it, and a flag tower half again as tall as that.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The gun tower spent its first life at 1.68 m, below the 2.0 m wall that arrived
        /// later, and it read as a bollard rather than as a defence. Nothing caught that,
        /// because nothing in the game reads a structure's height: a round sweeps a
        /// <see cref="CombatPlane"/> column from 0.5 m to 30 m whatever is in the way, so
        /// height here is silhouette and only silhouette — and silhouette is exactly the kind
        /// of thing that regresses silently on a re-export.
        /// </para>
        /// <para>
        /// So the rule is asserted rather than described. Measured off the built prefabs,
        /// like the segment length, because the numbers live in three separate Blender files
        /// that cannot see each other.
        /// </para>
        /// </remarks>
        [Test]
        public void TheBuiltStructuresReadAsAHeightSequence()
        {
            DestructiblePrefabBuilder.EnsureAssets();
            ObjectivePrefabBuilder.EnsureAssets();

            float wall = StandingHeight(DestructiblePrefabBuilder.Load(StructureKind.Wall));
            float gate = StandingHeight(DestructiblePrefabBuilder.Load(StructureKind.Door));
            float gun = StandingHeight(DestructiblePrefabBuilder.Load(StructureKind.Turret));
            float flag = StandingHeight(ObjectivePrefabBuilder.LoadTower());

            Assert.That(
                gate, Is.EqualTo(wall).Within(0.01f),
                $"a gate is {gate:0.##} m and a wall is {wall:0.##} m, so a run of the two has "
                + "two cap lines instead of one");

            Assert.That(
                gun, Is.GreaterThan(wall * 1.5f),
                $"the gun tower is {gun:0.##} m against a {wall:0.##} m wall, so it does not "
                + "tower over anything and reads as a bollard behind its own fence");

            Assert.That(
                gun, Is.LessThan(flag),
                $"the gun tower is {gun:0.##} m and the flag tower is {flag:0.##} m; the thing "
                + "the whole map is about has to be the tallest thing on it");
        }

        /// <summary>
        /// Returns how tall a destructible's intact model stands, in metres.
        /// </summary>
        /// <param name="prefab">The assembled prefab.</param>
        /// <returns>The height of its intact state, measured off the renderers.</returns>
        private static float StandingHeight(GameObject prefab)
        {
            Assert.That(prefab, Is.Not.Null, "a prefab this test measures has not been built");

            Transform model = prefab.transform.Find(
                Destructible.NodeNameFor(DestructionState.Intact));
            Assert.That(model, Is.Not.Null, $"{prefab.name} has no intact model");

            Renderer[] parts = model.GetComponentsInChildren<Renderer>(true);
            Assert.That(parts.Length, Is.GreaterThan(0), $"{prefab.name} draws nothing");

            Bounds box = parts[0].bounds;
            for (int part = 1; part < parts.Length; part++)
            {
                box.Encapsulate(parts[part].bounds);
            }

            return box.max.y - prefab.transform.position.y;
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
