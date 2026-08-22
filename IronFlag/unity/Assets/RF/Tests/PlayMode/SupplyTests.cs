using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Supply;
using IronFlag.Vehicles;

namespace IronFlag.Tests.PlayMode
{
    /// <summary>
    /// The resource economy with a clock running: fuel going down, ammunition running out,
    /// and both of them coming back at a depot or a bunker.
    /// </summary>
    /// <remarks>
    /// The arithmetic - the draw curve, the size of the pools, what a depot is worth - is
    /// settled in edit mode. What is left for here is everything that only exists once time
    /// passes: whether a vehicle that runs dry actually stops, whether it can still shoot
    /// after it has, and whether the helicopter really is turned away by a field depot.
    /// </remarks>
    public sealed class SupplyTests
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

        [UnityTest]
        public IEnumerator DrivingCostsMoreFuelThanSittingStill()
        {
            VehicleSupply idle = CreateVehicle(VehicleKind.Tank, Team.Green, Vector3.zero).Supply;
            Rig driven = CreateVehicle(VehicleKind.Tank, Team.Green, new Vector3(30.0f, 0.0f, 0.0f));

            driven.Controller.SetInput(new VehicleInput(new Vector2(0.0f, 1.0f), Vector2.zero, 0.0f));
            yield return Wait(0.5f);

            Assert.That(idle.Fuel, Is.LessThan(idle.FuelCapacity), "an idling engine costs nothing");
            Assert.That(
                driven.Supply.Fuel,
                Is.LessThan(idle.Fuel),
                "driving flat out costs no more than parking");
        }

        /// <summary>
        /// The design document is explicit: an empty tank strands the vehicle, and it can
        /// still fight where it stands.
        /// </summary>
        [UnityTest]
        public IEnumerator AVehicleThatRunsDryStopsWhereItIsAndKeepsItsGun()
        {
            Rig tank = CreateVehicle(VehicleKind.Tank, Team.Green, Vector3.zero);
            int carried = tank.Supply.Rounds;
            tank.Supply.RunDry();

            tank.Controller.SetInput(new VehicleInput(new Vector2(0.0f, 1.0f), Vector2.zero, 0.0f, true));
            yield return Wait(0.6f);

            Assert.That(tank.Supply.IsStranded, Is.True);
            Assert.That(
                tank.Controller.transform.position.z,
                Is.LessThan(1.0f),
                "it drove off on an empty tank");
            Assert.That(tank.Controller.CurrentInput.Fire, Is.True, "it lost its trigger as well");
            Assert.That(
                tank.Supply.Rounds,
                Is.LessThan(carried),
                "a stranded vehicle cannot fight in place");
        }

        /// <summary>
        /// A helicopter that runs dry has to come down. Left hovering it would be permanent
        /// cover that nothing on the ground could ever remove.
        /// </summary>
        [UnityTest]
        public IEnumerator AHelicopterThatRunsDrySinksToTheGround()
        {
            Rig flyer = CreateHelicopter(Team.Green, new Vector3(0.0f, 12.0f, 0.0f));
            yield return Wait(0.2f);

            float started = ((Helicopter)flyer.Controller).Altitude;
            flyer.Supply.RunDry();
            flyer.Controller.SetInput(new VehicleInput(Vector2.zero, Vector2.zero, 1.0f));
            yield return Wait(1.0f);

            Assert.That(
                ((Helicopter)flyer.Controller).Altitude,
                Is.LessThan(started - 1.0f),
                "it is holding a hover on an empty tank");
        }

        [UnityTest]
        public IEnumerator FiringSpendsRoundsAndAnEmptyGunWillNotFire()
        {
            Rig tank = CreateVehicle(VehicleKind.Tank, Team.Green, Vector3.zero);
            yield return Wait(0.1f);

            int carried = tank.Supply.Rounds;
            Assert.That(tank.Gun.TryFire(), Is.True, "it would not fire at all");
            Assert.That(tank.Supply.Rounds, Is.EqualTo(carried - 1), "the shot cost nothing");

            tank.Supply.FireOff();

            Assert.That(tank.Gun.IsEmpty, Is.True, "the gun does not know it is empty");
            Assert.That(tank.Gun.IsLoaded, Is.False);
            Assert.That(tank.Gun.TryFire(), Is.False, "it fired a round it did not have");
        }

        /// <summary>
        /// A vehicle with no supply behind it never runs out of anything, which is what
        /// every rig in the combat tests is.
        /// </summary>
        [UnityTest]
        public IEnumerator AGunWithNoSupplyBehindItNeverRunsOut()
        {
            Rig tank = CreateVehicle(VehicleKind.Tank, Team.Green, Vector3.zero);
            Object.DestroyImmediate(tank.Supply);
            yield return Wait(0.1f);

            for (int shot = 0; shot < 3; shot++)
            {
                Assert.That(tank.Gun.TryFire(), Is.True, $"shot {shot} was refused");
                yield return Wait(WeaponTuning.For(VehicleKind.Tank).ShotInterval + 0.1f);
            }
        }

        /// <summary>
        /// A depot hands out one commodity to whoever is parked on it, which is what the
        /// asset spec means by giving depots no team colour.
        /// </summary>
        [UnityTest]
        public IEnumerator AFuelDepotRefuelsEitherSideAndRearmsNobody()
        {
            CreatePoint(Team.None, Vector3.zero, 8.0f, 0.5f, 0.0f, false);
            Rig green = CreateVehicle(VehicleKind.Tank, Team.Green, new Vector3(2.0f, 0.0f, 0.0f));
            Rig brown = CreateVehicle(VehicleKind.Tank, Team.Brown, new Vector3(-2.0f, 0.0f, 0.0f));

            green.Supply.RunDry();
            green.Supply.FireOff();
            brown.Supply.RunDry();
            yield return Wait(0.5f);

            Assert.That(green.Supply.Fuel, Is.GreaterThan(0.0f), "it refused to serve the green side");
            Assert.That(brown.Supply.Fuel, Is.GreaterThan(0.0f), "it refused to serve the brown side");
            Assert.That(green.Supply.Rounds, Is.EqualTo(0), "a fuel depot handed out ammunition");
            Assert.That(green.Supply.Serving, Is.Not.Null, "the vehicle does not know it is being served");
        }

        [UnityTest]
        public IEnumerator AnAmmoDepotRefillsTheGunAndNotTheTank()
        {
            CreatePoint(Team.None, Vector3.zero, 8.0f, 0.0f, 0.5f, false);
            Rig tank = CreateVehicle(VehicleKind.Tank, Team.Green, new Vector3(2.0f, 0.0f, 0.0f));

            tank.Supply.RunDry();
            tank.Supply.FireOff();
            yield return Wait(0.5f);

            Assert.That(tank.Supply.Rounds, Is.GreaterThan(0), "the gun was not rearmed");
            Assert.That(tank.Supply.Fuel, Is.EqualTo(0.0f), "an ammo depot handed out fuel");
        }

        /// <summary>
        /// The drawback the design document's roster table gives the helicopter: it has to
        /// go home. A depot it could use in the field would delete that entirely, and a
        /// depot it could use from the air would delete it twice.
        /// </summary>
        [UnityTest]
        public IEnumerator AHelicopterIsTurnedAwayByAFieldDepotAndServedByItsOwnBunker()
        {
            CreatePoint(Team.None, Vector3.zero, 10.0f, 0.5f, 0.5f, false);
            CreatePoint(Team.Green, new Vector3(60.0f, 0.0f, 0.0f), 12.0f, 0.5f, 0.5f, true);

            Rig flyer = CreateHelicopter(Team.Green, new Vector3(0.0f, 0.5f, 0.0f));
            flyer.Supply.RunDry();
            yield return Wait(0.3f);

            Assert.That(
                flyer.Supply.Fuel,
                Is.EqualTo(0.0f),
                "the helicopter refuelled at a field depot");

            flyer.Controller.Teleport(new Vector3(60.0f, 0.5f, 0.0f), 0.0f);
            yield return Wait(0.3f);

            Assert.That(flyer.Supply.Fuel, Is.GreaterThan(0.0f), "its own bunker turned it away");
        }

        /// <summary>
        /// A helicopter still in the air over its own bunker has not landed on the pad, and
        /// a hovering aircraft taking on fuel would be the same drawback deleted a third way.
        /// </summary>
        [UnityTest]
        public IEnumerator AHelicopterHasToComeDownOntoThePadToBeServed()
        {
            CreatePoint(Team.Green, Vector3.zero, 12.0f, 0.5f, 0.5f, true);
            Rig flyer = CreateHelicopter(Team.Green, new Vector3(0.0f, 14.0f, 0.0f));
            flyer.Supply.RunDry();
            yield return Wait(0.2f);

            Assert.That(flyer.Supply.Fuel, Is.EqualTo(0.0f), "it refuelled from up in the air");
        }

        [UnityTest]
        public IEnumerator ABunkerWillNotServeTheOtherSide()
        {
            CreatePoint(Team.Green, Vector3.zero, 12.0f, 0.5f, 0.5f, true);
            Rig enemy = CreateVehicle(VehicleKind.Tank, Team.Brown, new Vector3(2.0f, 0.0f, 0.0f));

            enemy.Supply.RunDry();
            yield return Wait(0.3f);

            Assert.That(enemy.Supply.Fuel, Is.EqualTo(0.0f), "it refuelled at the enemy bunker");
            Assert.That(enemy.Supply.Serving, Is.Null);
        }

        /// <summary>One assembled vehicle and the parts these tests reach into.</summary>
        private readonly struct Rig
        {
            public Rig(VehicleController controller, VehicleSupply supply, VehicleWeapon gun)
            {
                Controller = controller;
                Supply = supply;
                Gun = gun;
            }

            public VehicleController Controller { get; }

            public VehicleSupply Supply { get; }

            public VehicleWeapon Gun { get; }
        }

        private Rig CreateVehicle(VehicleKind kind, Team side, Vector3 at)
            => Assemble<GroundVehicle>(kind, side, at);

        private Rig CreateHelicopter(Team side, Vector3 at)
            => Assemble<Helicopter>(VehicleKind.Helicopter, side, at);

        /// <summary>
        /// Assembles a vehicle with a tank of fuel, a gun and nothing else it does not need.
        /// </summary>
        /// <typeparam name="T">Movement model to give it.</typeparam>
        /// <param name="kind">Which vehicle it is, for the two tuning tables.</param>
        /// <param name="side">Side to paint it.</param>
        /// <param name="at">Where to put it.</param>
        /// <returns>The rig.</returns>
        private Rig Assemble<T>(VehicleKind kind, Team side, Vector3 at)
            where T : VehicleController
        {
            var host = new GameObject($"{kind} ({side})");
            host.SetActive(false);
            host.transform.position = at;
            spawned.Add(host);

            host.AddComponent<BoxCollider>().size = new Vector3(2.0f, 2.0f, 4.0f);
            host.AddComponent<Rigidbody>();
            host.AddComponent<VehicleTeamPaint>().Team = side;

            VehicleTuning tuning = VehicleTuning.For(kind);
            T controller = host.AddComponent<T>();
            controller.Configure(kind, tuning);

            host.AddComponent<VehicleHealth>().Configure(tuning.HitPoints, null, 3.0f);
            VehicleSupply supply = host.AddComponent<VehicleSupply>();
            supply.Configure(tuning.FuelCapacity, tuning.IdleFuelDraw, WeaponTuning.For(kind).Rounds);

            var muzzle = new GameObject("MuzzlePoint");
            muzzle.transform.SetParent(host.transform, false);
            muzzle.transform.localPosition = new Vector3(0.0f, 1.5f, 2.4f);

            VehicleWeapon gun = host.AddComponent<VehicleWeapon>();
            gun.Configure(controller, muzzle.transform, WeaponTuning.For(kind), Round());

            host.SetActive(true);
            return new Rig(controller, supply, gun);
        }

        /// <summary>
        /// Builds a supply point somewhere.
        /// </summary>
        /// <param name="side">Side it serves, or none for a contestable depot.</param>
        /// <param name="at">Where it stands.</param>
        /// <param name="radius">How far it reaches.</param>
        /// <param name="fuel">Fraction of a tank per second.</param>
        /// <param name="ammo">Fraction of a load per second.</param>
        /// <param name="aircraft">Whether a landed helicopter can use it.</param>
        /// <returns>The point.</returns>
        private SupplyPoint CreatePoint(
            Team side, Vector3 at, float radius, float fuel, float ammo, bool aircraft)
        {
            var host = new GameObject($"Supply ({side})");
            host.transform.position = at;
            spawned.Add(host);

            SupplyPoint point = host.AddComponent<SupplyPoint>();
            point.Configure(side, radius, fuel, ammo, aircraft);
            return point;
        }

        /// <summary>
        /// Builds the round every gun in these tests fires.
        /// </summary>
        /// <returns>A projectile to be instantiated from.</returns>
        /// <remarks>
        /// Kept switched off, because a live round sitting in the scene with no velocity
        /// would count out its own reach and destroy the template.
        /// </remarks>
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
            round.Configure(body.transform, null);
            spawned.Add(host);
            return round;
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
