using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Destruction;
using IronFlag.Supply;
using IronFlag.Vehicles;

namespace IronFlag.Tests.PlayMode
{
    /// <summary>
    /// Knocking things down: a round that hurts a building the same way it hurts a tank, the
    /// three models swapping over as the pool empties, a depot that stops handing anything
    /// out once it is rubble, and a firing lane opened by removing the thing in it.
    /// </summary>
    /// <remarks>
    /// The arithmetic - how much a building takes, when the damaged mesh comes in, where the
    /// debris flies - is settled in edit mode. What is left for here is everything that only
    /// exists once a round is actually in the air, which is most of the reason M5 is worth
    /// testing at all: the interesting claim is that a wall and a hull are the same kind of
    /// target, and only a real shot can say whether they are.
    /// </remarks>
    public sealed class DestructionTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();
        private Projectile round;

        [TearDown]
        public void CleanUp()
        {
            foreach (Projectile stray in Object.FindObjectsByType<Projectile>(FindObjectsInactive.Include))
            {
                if (stray != null)
                {
                    Object.Destroy(stray.gameObject);
                }
            }

            foreach (GameObject item in spawned)
            {
                if (item != null)
                {
                    Object.Destroy(item);
                }
            }

            spawned.Clear();
            round = null;
        }

        /// <summary>
        /// The design document's state swap, end to end: intact while it is healthy, the
        /// damaged mesh once it has taken half of what it can, rubble when it runs out.
        /// </summary>
        [UnityTest]
        public IEnumerator ABuildingPassesThroughAllThreeModelsAsItIsShotUp()
        {
            Destructible building = CreateStructure(StructureKind.BuildingA, Vector3.zero);
            yield return null;

            Assert.That(building.State, Is.EqualTo(DestructionState.Intact));
            Assert.That(Showing(building), Is.EqualTo(Destructible.IntactNodeName));

            building.TakeDamage(building.Tuning.HitPoints * 0.6f, Team.Green);

            Assert.That(building.State, Is.EqualTo(DestructionState.Damaged), "it still looks new");
            Assert.That(Showing(building), Is.EqualTo(Destructible.DamagedNodeName));

            building.TakeDamage(building.Tuning.HitPoints, Team.Green);

            Assert.That(building.State, Is.EqualTo(DestructionState.Destroyed));
            Assert.That(Showing(building), Is.EqualTo(Destructible.DestroyedNodeName));
            Assert.That(building.IsDestroyed, Is.True);
        }

        /// <summary>
        /// The whole point of the interface: a shell fired at a wall has to do to it what a
        /// shell fired at a tank does to that.
        /// </summary>
        [UnityTest]
        public IEnumerator ARealShellFiredAtABuildingHurtsIt()
        {
            VehicleWeapon gun = CreateTank(Team.Green, Vector3.zero);
            Destructible building = CreateStructure(StructureKind.BuildingA, Ahead(20.0f));
            yield return Settle();

            float before = building.HitPoints;
            Assert.That(gun.TryFire(), Is.True, "the gun refused to fire");
            yield return Wait(1.0f);

            Assert.That(building.HitPoints, Is.LessThan(before), "the shell went through the wall");
            Assert.That(
                before - building.HitPoints,
                Is.EqualTo(gun.Tuning.Damage).Within(0.01f),
                "a wall takes a different amount from a shell than the table says");
        }

        /// <summary>
        /// Nobody owns a building, so both sides can shoot it. A structure that could only
        /// be hurt by one player would be that player's wall.
        /// </summary>
        [UnityTest]
        public IEnumerator EitherSideCanShootTheSameBuilding()
        {
            Destructible building = CreateStructure(StructureKind.BuildingB, Vector3.zero);
            yield return null;

            Assert.That(building.Team, Is.EqualTo(Team.None));
            Assert.That(building.TakeDamage(10.0f, Team.Green), Is.GreaterThan(0.0f));
            Assert.That(building.TakeDamage(10.0f, Team.Brown), Is.GreaterThan(0.0f));
        }

        /// <summary>
        /// Rubble absorbs nothing, for the same reason a wreck does: the shot that flattens
        /// a building must not be able to flatten it twice.
        /// </summary>
        [UnityTest]
        public IEnumerator RubbleTakesNoMoreDamageAndCollapsesOnlyOnce()
        {
            Destructible building = CreateStructure(StructureKind.BuildingA, Vector3.zero);
            int collapses = 0;
            building.Collapsed += _ => collapses++;
            yield return null;

            building.TakeDamage(building.Tuning.HitPoints * 2.0f, Team.Green);
            Assert.That(collapses, Is.EqualTo(1));

            Assert.That(building.TakeDamage(50.0f, Team.Green), Is.EqualTo(0.0f), "rubble lost hit points");
            Assert.That(collapses, Is.EqualTo(1), "it collapsed a second time");
        }

        /// <summary>
        /// Why anybody bothers: a building in the way stops a shell, and the same building
        /// as rubble does not. Opening a firing lane is most of what destruction is for.
        /// </summary>
        [UnityTest]
        public IEnumerator FlatteningABuildingOpensTheFiringLaneBehindIt()
        {
            VehicleWeapon gun = CreateTank(Team.Green, Vector3.zero);
            Destructible building = CreateStructure(StructureKind.BuildingA, Ahead(12.0f));
            VehicleHealth target = CreateTarget(Team.Brown, Ahead(24.0f));
            yield return Settle();

            gun.TryFire();
            yield return Wait(1.0f);

            Assert.That(
                target.HitPoints,
                Is.EqualTo(target.MaxHitPoints),
                "the shell went through a building that was still standing");

            building.TakeDamage(building.Tuning.HitPoints, Team.Green);
            yield return Wait(gun.Tuning.ShotInterval + 0.1f);

            gun.TryFire();
            yield return Wait(1.0f);

            Assert.That(
                target.HitPoints,
                Is.LessThan(target.MaxHitPoints),
                "the rubble is still stopping shells");
        }

        /// <summary>
        /// Rubble is not solid. A destroyed structure keeps its model and loses its
        /// colliders, so a vehicle drives over where it stood - which is the other half of
        /// opening a firing lane, and the half M5 deliberately left out.
        /// </summary>
        [UnityTest]
        public IEnumerator RubbleHasNoHitboxLeft()
        {
            Destructible building = CreateStructure(StructureKind.BuildingA, Vector3.zero);
            yield return null;

            Assert.That(
                SolidColliders(building),
                Is.GreaterThan(0),
                "a standing building is not something a vehicle can bump into");

            building.TakeDamage(building.Tuning.HitPoints, Team.Green);
            yield return null;

            Assert.That(
                building.State,
                Is.EqualTo(DestructionState.Destroyed),
                "it did not come down at all");
            Assert.That(
                Showing(building),
                Is.EqualTo(Destructible.DestroyedNodeName),
                "the rubble is not being drawn");
            Assert.That(
                SolidColliders(building),
                Is.Zero,
                "the rubble still blocks the road it was knocked down to open");
        }

        /// <summary>
        /// A structure put back up is solid again. Nothing in the game rebuilds one, but a
        /// building that came back intangible would be a wall nothing could ever hit again -
        /// and that is a bug found by shooting the same building twice, which no test would
        /// think to do.
        /// </summary>
        [UnityTest]
        public IEnumerator AStructurePutBackUpIsSolidAgain()
        {
            Destructible building = CreateStructure(StructureKind.BuildingA, Vector3.zero);
            yield return null;

            int standing = SolidColliders(building);
            building.TakeDamage(building.Tuning.HitPoints, Team.Green);
            yield return null;

            building.Restore();
            yield return null;

            Assert.That(building.State, Is.EqualTo(DestructionState.Intact));
            Assert.That(
                SolidColliders(building),
                Is.EqualTo(standing),
                "the rebuilt building is scenery nothing can touch");
        }

        /// <summary>
        /// The design document's "destroyable/contestable" depots, in one test: a depot that
        /// has been knocked down stops being somewhere to refuel.
        /// </summary>
        [UnityTest]
        public IEnumerator ADestroyedDepotStopsHandingAnythingOut()
        {
            Destructible depot = CreateStructure(StructureKind.DepotFuel, Vector3.zero);
            depot.gameObject.AddComponent<SupplyPoint>().Configure(
                Team.None, servedRadius: 7.0f, fuelPerSecond: 0.2f, ammoPerSecond: 0.0f, aircraft: false);
            yield return null;

            Assert.That(
                SupplyPoint.ServingAt(Vector3.zero, Team.Green, false),
                Is.Not.Null,
                "the depot was never serving anybody");

            depot.TakeDamage(depot.Tuning.HitPoints, Team.Green);
            yield return null;

            Assert.That(
                SupplyPoint.ServingAt(Vector3.zero, Team.Green, false),
                Is.Null,
                "a flattened depot is still refuelling people");
        }

        /// <summary>
        /// A structure put back up is a depot put back: the point is switched off rather
        /// than removed, so nothing has to re-configure it.
        /// </summary>
        [UnityTest]
        public IEnumerator RebuildingADepotBringsItsSupplyBack()
        {
            Destructible depot = CreateStructure(StructureKind.DepotAmmo, Vector3.zero);
            depot.gameObject.AddComponent<SupplyPoint>().Configure(
                Team.None, servedRadius: 7.0f, fuelPerSecond: 0.0f, ammoPerSecond: 0.2f, aircraft: false);

            depot.TakeDamage(depot.Tuning.HitPoints, Team.Green);
            yield return null;

            depot.Restore();
            yield return null;

            Assert.That(depot.State, Is.EqualTo(DestructionState.Intact));
            Assert.That(
                SupplyPoint.ServingAt(Vector3.zero, Team.Green, false),
                Is.Not.Null,
                "the rebuilt depot hands out nothing");
        }

        /// <summary>
        /// The design document asks for a debris burst on each transition, and for it to be
        /// over quickly - it is a punctuation mark, not a prop.
        /// </summary>
        [UnityTest]
        public IEnumerator EachTransitionThrowsDebrisThatTidiesItselfUp()
        {
            Destructible building = CreateStructure(StructureKind.BuildingA, Vector3.zero, WithDebris());
            yield return null;

            Assert.That(LiveDebris(), Is.EqualTo(0), "something threw debris before anything happened");

            building.TakeDamage(building.Tuning.HitPoints * 0.6f, Team.Green);
            Assert.That(LiveDebris(), Is.EqualTo(1), "cracking a building open made no mess");

            building.TakeDamage(building.Tuning.HitPoints, Team.Green);
            Assert.That(LiveDebris(), Is.EqualTo(2), "knocking a building down made no mess");

            yield return Wait(1.2f);

            Assert.That(LiveDebris(), Is.EqualTo(0), "the debris is still lying about");
        }

        /// <summary>
        /// A hit that does not change the state must not throw debris, or a chaingun would
        /// bury the map in cubes.
        /// </summary>
        [UnityTest]
        public IEnumerator AHitThatChangesNothingThrowsNoDebris()
        {
            Destructible building = CreateStructure(StructureKind.BuildingB, Vector3.zero, WithDebris());
            yield return null;

            for (int shot = 0; shot < 3; shot++)
            {
                building.TakeDamage(1.0f, Team.Green);
            }

            Assert.That(building.State, Is.EqualTo(DestructionState.Intact));
            Assert.That(LiveDebris(), Is.EqualTo(0), "scratching a wall threw pieces of it around");
        }

        /// <summary>
        /// Exactly one model is ever showing, which is what keeps what a player sees and
        /// what a vehicle bumps into from ever disagreeing.
        /// </summary>
        [UnityTest]
        public IEnumerator OnlyOneModelIsEverShowing()
        {
            Destructible building = CreateStructure(StructureKind.BuildingA, Vector3.zero);
            yield return null;

            foreach (float share in new[] { 1.0f, 0.4f, 0.0f })
            {
                building.Restore();
                building.TakeDamage(building.Tuning.HitPoints * (1.0f - share), Team.Green);

                int showing = 0;
                foreach (Transform state in building.transform)
                {
                    if (state.gameObject.activeSelf)
                    {
                        showing++;
                    }
                }

                Assert.That(showing, Is.EqualTo(1), $"at {share:0.0} of its pool it is showing {showing} models");
            }
        }

        /// <summary>
        /// Builds a destructible out of three cubes, one per state.
        /// </summary>
        /// <param name="kind">Which structure it is, for the numbers.</param>
        /// <param name="at">Where to put it.</param>
        /// <param name="debris">Debris prefab to throw, or null for none.</param>
        /// <returns>The structure.</returns>
        /// <remarks>
        /// Cubes rather than the real models on purpose: what is being tested is the swap
        /// and the pool, and a rig built from the imported <c>.glb</c> files would be
        /// testing the art pipeline as well. The prefabs themselves are checked in edit
        /// mode, where the models are what is under test.
        /// </remarks>
        private Destructible CreateStructure(StructureKind kind, Vector3 at, DebrisBurst debris = null)
        {
            var host = new GameObject($"{kind}");
            host.SetActive(false);
            host.transform.position = at;
            spawned.Add(host);

            GameObject intact = CreateState(host, Destructible.IntactNodeName, 4.0f);
            GameObject damaged = CreateState(host, Destructible.DamagedNodeName, 3.6f);
            GameObject destroyed = CreateState(host, Destructible.DestroyedNodeName, 3.0f);

            Destructible structure = host.AddComponent<Destructible>();
            structure.Configure(kind, StructureTuning.For(kind), intact, damaged, destroyed, debris);

            host.SetActive(true);
            return structure;
        }

        private static GameObject CreateState(GameObject host, string name, float size)
        {
            var state = new GameObject(name);
            state.transform.SetParent(host.transform, false);

            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Mesh";
            box.transform.SetParent(state.transform, false);
            box.transform.localScale = new Vector3(size, size, size);
            box.transform.localPosition = new Vector3(0.0f, size * 0.5f, 0.0f);

            return state;
        }

        /// <summary>
        /// Builds a debris prefab with one chunk, which is enough to count.
        /// </summary>
        /// <returns>The prefab, switched off so it does not animate where it stands.</returns>
        private DebrisBurst WithDebris()
        {
            var host = new GameObject("Debris");
            host.SetActive(false);
            spawned.Add(host);

            var chunk = new GameObject("Chunk0");
            chunk.transform.SetParent(host.transform, false);

            DebrisBurst burst = host.AddComponent<DebrisBurst>();
            burst.Configure(new[] { chunk.transform }, 0.6f, 3.0f);
            return burst;
        }

        /// <summary>
        /// Counts the debris bursts currently in the world, ignoring the prefab itself.
        /// </summary>
        /// <returns>How many bursts are live.</returns>
        private static int LiveDebris()
        {
            int live = 0;
            foreach (DebrisBurst burst in Object.FindObjectsByType<DebrisBurst>(FindObjectsInactive.Exclude))
            {
                if (burst != null)
                {
                    live++;
                }
            }

            return live;
        }

        /// <summary>
        /// Counts the colliders a structure currently presents to the world.
        /// </summary>
        /// <param name="structure">Structure to measure.</param>
        /// <returns>
        /// How many enabled colliders sit on objects that are switched on, which is exactly
        /// the set a vehicle can drive into.
        /// </returns>
        private static int SolidColliders(Destructible structure)
        {
            int solid = 0;
            foreach (Collider part in structure.GetComponentsInChildren<Collider>(true))
            {
                if (part.enabled && part.gameObject.activeInHierarchy)
                {
                    solid++;
                }
            }

            return solid;
        }

        /// <summary>
        /// Returns the name of the one state object that is showing.
        /// </summary>
        /// <param name="structure">Structure to look at.</param>
        /// <returns>The active child's name, or an empty string when none is showing.</returns>
        private static string Showing(Destructible structure)
        {
            foreach (Transform state in structure.transform)
            {
                if (state.gameObject.activeSelf)
                {
                    return state.name;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Assembles a tank with a gun and nothing else it does not need.
        /// </summary>
        /// <param name="side">Side to paint it.</param>
        /// <param name="at">Where to put it.</param>
        /// <returns>Its gun, which is all these tests fire.</returns>
        private VehicleWeapon CreateTank(Team side, Vector3 at)
        {
            var host = new GameObject($"Tank ({side})");
            host.SetActive(false);
            host.transform.position = at;
            spawned.Add(host);

            host.AddComponent<BoxCollider>().size = new Vector3(2.0f, 2.0f, 4.0f);
            host.AddComponent<Rigidbody>().isKinematic = true;
            host.AddComponent<VehicleTeamPaint>().Team = side;

            VehicleTuning tuning = VehicleTuning.For(VehicleKind.Tank);
            GroundVehicle controller = host.AddComponent<GroundVehicle>();
            controller.Configure(VehicleKind.Tank, tuning);
            host.AddComponent<VehicleHealth>().Configure(tuning.HitPoints, null, 3.0f);

            var muzzle = new GameObject("MuzzlePoint");
            muzzle.transform.SetParent(host.transform, false);
            muzzle.transform.localPosition = new Vector3(0.0f, 1.9f, 2.4f);

            VehicleWeapon gun = host.AddComponent<VehicleWeapon>();
            gun.Configure(
                controller, muzzle.transform, WeaponTuning.For(VehicleKind.Tank), Round(), null);

            host.SetActive(true);
            return gun;
        }

        /// <summary>
        /// Puts a hull on the map for a shell to arrive at.
        /// </summary>
        /// <param name="side">Side to paint it.</param>
        /// <param name="at">Where to put it.</param>
        /// <returns>Its health, which is what these tests read.</returns>
        private VehicleHealth CreateTarget(Team side, Vector3 at)
        {
            var host = new GameObject($"Target ({side})");
            host.SetActive(false);
            host.transform.position = at;
            spawned.Add(host);

            host.AddComponent<BoxCollider>().size = new Vector3(2.0f, 2.0f, 4.0f);
            host.AddComponent<VehicleTeamPaint>().Team = side;
            VehicleHealth hull = host.AddComponent<VehicleHealth>();
            hull.Configure(VehicleTuning.For(VehicleKind.Tank).HitPoints, null, 3.0f);

            host.SetActive(true);
            return hull;
        }

        private Projectile Round()
        {
            if (round != null)
            {
                return round;
            }

            var host = new GameObject("Round");
            host.SetActive(false);

            var body = new GameObject("Body");
            body.transform.SetParent(host.transform, false);

            round = host.AddComponent<Projectile>();
            round.Configure(body.transform, null, null, null);
            spawned.Add(host);
            return round;
        }

        private static Vector3 Ahead(float metres) => new Vector3(0.0f, 0.0f, metres);

        private static IEnumerator Settle()
        {
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
        }

        private static IEnumerator Wait(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
            {
                yield return new WaitForFixedUpdate();
            }
        }
    }
}
