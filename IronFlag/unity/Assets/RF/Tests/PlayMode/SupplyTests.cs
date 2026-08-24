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

            driven.Controller.SetInput(new VehicleInput(new Vector2(0.0f, 1.0f), Vector2.zero));
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

            tank.Controller.SetInput(new VehicleInput(new Vector2(0.0f, 1.0f), Vector2.zero, true));
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
        /// <remarks>
        /// The one thing that can move a helicopter off its cruising altitude, now that the
        /// pilot cannot. Nothing is asked of the controls here because there is nothing to
        /// ask: the aircraft flies itself, and the empty tank is what changes that.
        /// </remarks>
        [UnityTest]
        public IEnumerator AHelicopterThatRunsDrySinksToTheGround()
        {
            Rig flyer = CreateHelicopter(Team.Green, new Vector3(0.0f, 12.0f, 0.0f));
            yield return Wait(0.2f);

            var aircraft = (Helicopter)flyer.Controller;
            float started = aircraft.Altitude;
            Assert.That(aircraft.IsPowered, Is.True, "it was never flying");

            flyer.Supply.RunDry();
            yield return Wait(1.0f);

            Assert.That(aircraft.IsPowered, Is.False, "nobody told it the engine had stopped");
            Assert.That(
                aircraft.Altitude,
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
        /// go home. A depot it could use in the field would delete that entirely.
        /// </summary>
        /// <remarks>
        /// Both points are visited at the aircraft's cruising altitude, because that is the
        /// only height it can be at. What refuses it at the depot is
        /// <see cref="SupplyPoint.ServesAircraft"/> and nothing else - the height gate that
        /// used to be the second half of this rule is gone, and had to go: with one altitude
        /// it would have refused the bunker too.
        /// </remarks>
        [UnityTest]
        public IEnumerator AHelicopterIsTurnedAwayByAFieldDepotAndServedByItsOwnBunker()
        {
            CreatePoint(Team.None, Vector3.zero, 10.0f, 0.5f, 0.5f, false);
            CreatePoint(Team.Green, new Vector3(60.0f, 0.0f, 0.0f), 12.0f, 0.5f, 0.5f, true);

            Rig flyer = CreateHelicopter(Team.Green, Vector3.zero);
            float cruise = ((Helicopter)flyer.Controller).Flight.CruiseAltitude;
            flyer.Controller.Teleport(new Vector3(0.0f, cruise, 0.0f), 0.0f);
            flyer.Supply.RunDry();
            yield return Wait(0.3f);

            Assert.That(
                flyer.Supply.Fuel,
                Is.EqualTo(0.0f),
                "the helicopter refuelled at a field depot");

            flyer.Controller.Teleport(new Vector3(60.0f, cruise, 0.0f), 0.0f);
            yield return Wait(0.3f);

            Assert.That(flyer.Supply.Fuel, Is.GreaterThan(0.0f), "its own bunker turned it away");
        }

        /// <summary>
        /// A helicopter is served hovering over its own bunker, because hovering is the only
        /// thing it can do.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This test used to say the opposite - that an aircraft had to come down onto the
        /// pad before anything would serve it - and it was right until the collective was
        /// taken away. A helicopter now flies at
        /// <see cref="FlightTuning.CruiseAltitude"/> and cannot descend to anything, so a
        /// height gate on a supply point does not mean "land first", it means "never". That
        /// would have cost the aircraft refuelling, rearming and swapping vehicles at once,
        /// with nothing in the game to say why.
        /// </para>
        /// <para>
        /// The drawback the roster table is protecting survives intact one door along, and
        /// <see cref="AHelicopterIsTurnedAwayByAFieldDepotAndServedByItsOwnBunker"/> is where
        /// it is checked: only a bunker serves aircraft at all, so the helicopter is still
        /// the one vehicle that has to fly all the way home.
        /// </para>
        /// </remarks>
        [UnityTest]
        public IEnumerator AHelicopterIsServedHoveringOverItsOwnBunker()
        {
            CreatePoint(Team.Green, Vector3.zero, 12.0f, 0.5f, 0.5f, true);
            Rig flyer = CreateHelicopter(Team.Green, Vector3.zero);
            var aircraft = (Helicopter)flyer.Controller;
            flyer.Controller.Teleport(new Vector3(0.0f, aircraft.Flight.CruiseAltitude, 0.0f), 0.0f);

            flyer.Supply.RunDry();
            yield return Wait(0.4f);

            Assert.That(
                flyer.Supply.Fuel,
                Is.GreaterThan(0.0f),
                "its own bunker refused an aircraft that has no way of landing on the pad");
            Assert.That(
                flyer.Supply.Serving,
                Is.Not.Null,
                "it took on fuel from nowhere");
        }

        /// <summary>
        /// And height is not what a bunker is checking. An aircraft that has run dry and
        /// settled onto the ground over its own pad is served on exactly the same terms as
        /// one hovering above it - which is what stops the two states of a helicopter being
        /// two different rules.
        /// </summary>
        [UnityTest]
        public IEnumerator ABunkerServesAnAircraftAtEitherOfTheHeightsItCanBeAt()
        {
            CreatePoint(Team.Green, Vector3.zero, 12.0f, 0.5f, 0.5f, true);
            Rig flyer = CreateHelicopter(Team.Green, Vector3.zero);
            FlightTuning flight = ((Helicopter)flyer.Controller).Flight;

            foreach (float height in new[] { flight.GroundedAltitude, flight.CruiseAltitude })
            {
                flyer.Controller.Teleport(new Vector3(0.0f, height, 0.0f), 0.0f);
                yield return null;

                Assert.That(
                    SupplyPoint.HomeFor(new Vector3(0.0f, height, 0.0f), Team.Green, true),
                    Is.Not.Null,
                    $"a helicopter at {height} m is not home");
            }
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
