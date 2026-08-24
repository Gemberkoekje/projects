using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using IronFlag.Levels;
using IronFlag.Supply;
using IronFlag.Vehicles;

namespace IronFlag.Tests.PlayMode
{
    /// <summary>
    /// What the ground does to a vehicle, driven for real over three surfaces laid side by
    /// side.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The arithmetic is covered without a scene in <c>GroundVehicleMotionTests</c>. What
    /// these add is the wiring between it and the world: that a vehicle actually asks the
    /// map what it is standing on, that the answer reaches the rigidbody as a distance
    /// covered, that the fuel pool is billed for it, and that the helicopter is left out of
    /// all three without anybody having to leave it out.
    /// </para>
    /// <para>
    /// The map is three long strips - a road, open country and a beach - rather than the
    /// shipped one, for the same reason the colour of a surface is measured off a throwaway
    /// level of parallel strips: a comparison wants two runs that differ in one thing, and
    /// the shipped map has no ninety-metre straight of sand on it. It is built with no
    /// catalog, so it has no geometry at all and warns about it: what a vehicle is standing
    /// on comes out of <see cref="SurfaceField"/>, which is a rasterised level file rather
    /// than anything you could see, and the vehicles here stand on a plain slab like the
    /// ones in <see cref="VehicleDrivingTests"/>.
    /// </para>
    /// </remarks>
    public sealed class SurfaceDrivingTests
    {
        /// <summary>Where along each strip a run starts.</summary>
        private const float StartX = -80.0f;

        /// <summary>How long each run is held at full throttle.</summary>
        private const float RunSeconds = 4.0f;

        /// <summary>Middle of the asphalt strip.</summary>
        private const float RoadZ = 24.0f;

        /// <summary>Middle of the open country between the two.</summary>
        private const float CountryZ = 0.0f;

        /// <summary>Middle of the sand strip.</summary>
        private const float BeachZ = -24.0f;

        private readonly List<GameObject> spawned = new List<GameObject>();

        private LevelDefinition inherited;
        private LevelLoader loader;
        private float measured;

        /// <summary>What the last run measured, because a coroutine cannot return one.</summary>
        private float Measured => measured;

        /// <summary>
        /// Remembers the map that was up before this class replaced it.
        /// </summary>
        [SetUp]
        public void NoteTheMapThatWasUp() => inherited = LevelLoader.Current;

        /// <summary>
        /// Takes the strips back down and puts back whatever map was up before them.
        /// </summary>
        /// <returns>The coroutine the framework steps through.</returns>
        /// <remarks>
        /// <see cref="LevelLoader.Current"/> is a static that outlives a scene and a test, so
        /// a class that puts a map up owes the next one the map it inherited - which may well
        /// be none, and putting none back is showing none. Everything else here is a
        /// <see cref="GameObject"/> and goes the ordinary way.
        /// </remarks>
        [UnityTearDown]
        public IEnumerator PutTheMapBack()
        {
            if (loader != null)
            {
                loader.Show(inherited, false);
            }

            loader = null;

            foreach (GameObject item in spawned)
            {
                if (item != null)
                {
                    Object.DestroyImmediate(item);
                }
            }

            spawned.Clear();
            yield return null;
        }

        /// <summary>
        /// The headline of the phase: the road is the fastest way across the map and the
        /// beach is the slowest, measured in metres actually covered.
        /// </summary>
        [UnityTest]
        public IEnumerator AJeepIsQuickestOnTheRoadAndSlowestOnTheSand()
        {
            LayTheStrips();

            yield return Run(VehicleKind.Jeep, RoadZ);
            float road = Measured;
            yield return Run(VehicleKind.Jeep, CountryZ);
            float country = Measured;
            yield return Run(VehicleKind.Jeep, BeachZ);
            float beach = Measured;

            Assert.That(road, Is.GreaterThan(country), $"road {road:0.0} m, country {country:0.0} m");
            Assert.That(country, Is.GreaterThan(beach), $"country {country:0.0} m, beach {beach:0.0} m");
            Assert.That(
                beach / road,
                Is.LessThan(0.85f),
                $"a jeep crosses {beach:0.0} m of sand while it covers {road:0.0} m of road, which "
                + "is not a difference anybody would change route for");
        }

        /// <summary>
        /// And the other half of it: the same three runs in a tank barely differ, which is
        /// what the surface-sensitivity column is for.
        /// </summary>
        [UnityTest]
        public IEnumerator ATankBarelyNoticesGroundTheJeepIsAtTheMercyOf()
        {
            LayTheStrips();

            yield return Run(VehicleKind.Jeep, RoadZ);
            float jeepOnRoad = Measured;
            yield return Run(VehicleKind.Jeep, BeachZ);
            float jeepOnSand = Measured;
            yield return Run(VehicleKind.Tank, RoadZ);
            float tankOnRoad = Measured;
            yield return Run(VehicleKind.Tank, BeachZ);
            float tankOnSand = Measured;

            float jeepCost = 1.0f - (jeepOnSand / jeepOnRoad);
            float tankCost = 1.0f - (tankOnSand / tankOnRoad);

            Assert.That(tankCost, Is.GreaterThan(0.0f), "the tank is not on the map at all");
            Assert.That(
                tankCost,
                Is.LessThan(jeepCost * 0.5f),
                $"the beach costs the tank {tankCost:P0} and the jeep {jeepCost:P0}, which is not "
                + "the difference between wheels and tracks");
        }

        /// <summary>
        /// A vehicle knows what it is standing on, and the answer comes from the map rather
        /// than from anything underneath it in the scene.
        /// </summary>
        /// <remarks>
        /// The slab these are standing on is one undifferentiated cube, so an answer that
        /// varied with position can only have come out of the level file.
        /// </remarks>
        [UnityTest]
        public IEnumerator AVehicleKnowsWhatItIsStandingOn()
        {
            LayTheStrips();

            GroundVehicle jeep = Park(VehicleKind.Jeep, RoadZ);
            yield return new WaitForFixedUpdate();
            Assert.That(jeep.Standing, Is.EqualTo(SurfaceKind.Asphalt));
            Assert.That(
                jeep.Underfoot.Grip,
                Is.EqualTo(SurfaceTuning.For(SurfaceKind.Asphalt).Grip).Within(0.0001f));

            jeep.Teleport(new Vector3(StartX, 0.05f, BeachZ), 90.0f);
            yield return new WaitForFixedUpdate();
            Assert.That(jeep.Standing, Is.EqualTo(SurfaceKind.Sand), "it is still on the road it left");
            Assert.That(
                jeep.Underfoot.FuelDraw,
                Is.EqualTo(SurfaceTuning.For(SurfaceKind.Sand).FuelDraw).Within(0.0001f));
        }

        /// <summary>
        /// A beach costs range as well as time, and a road hands a little of both back.
        /// </summary>
        /// <remarks>
        /// The tank is used deliberately. It shrugs off nearly all of the sand's grip, so if
        /// the surface-sensitivity column had been allowed to weigh thirst as well this would
        /// be the test that no longer measured anything.
        /// </remarks>
        [UnityTest]
        public IEnumerator ABeachCostsRangeAndARoadSavesIt()
        {
            LayTheStrips();

            yield return Burn(VehicleKind.Tank, RoadZ);
            float onRoad = Measured;
            yield return Burn(VehicleKind.Tank, CountryZ);
            float onCountry = Measured;
            yield return Burn(VehicleKind.Tank, BeachZ);
            float onBeach = Measured;

            Assert.That(onBeach, Is.GreaterThan(onCountry), $"beach {onBeach:0.00}, country {onCountry:0.00}");
            Assert.That(onCountry, Is.GreaterThan(onRoad), $"country {onCountry:0.00}, road {onRoad:0.00}");
        }

        /// <summary>
        /// Standing still costs the same wherever you are standing: an engine turning over
        /// is doing no more work on a beach than on a road.
        /// </summary>
        [UnityTest]
        public IEnumerator AParkedVehiclePaysNothingForTheGroundUnderIt()
        {
            LayTheStrips();

            yield return Idle(VehicleKind.Tank, RoadZ);
            float onRoad = Measured;
            yield return Idle(VehicleKind.Tank, BeachZ);
            float onBeach = Measured;

            Assert.That(onBeach, Is.GreaterThan(0.0f), "a parked engine is burning nothing at all");
            Assert.That(onBeach, Is.EqualTo(onRoad).Within(onRoad * 0.05f));
        }

        /// <summary>
        /// The helicopter flies the same over every kind of ground, and nothing anywhere has
        /// to check that it is a helicopter for that to be true.
        /// </summary>
        [UnityTest]
        public IEnumerator TheHelicopterFliesTheSameOverEveryKindOfGround()
        {
            LayTheStrips();

            Helicopter overRoad = Fly(RoadZ);
            Helicopter overSand = Fly(BeachZ);

            yield return Drive(new[] { overRoad, overSand }, Ahead, RunSeconds);

            float road = overRoad.transform.position.x - StartX;
            float sand = overSand.transform.position.x - StartX;

            Assert.That(road, Is.GreaterThan(20.0f), "neither aircraft went anywhere");
            Assert.That(sand, Is.EqualTo(road).Within(0.05f));
        }

        /// <summary>Full throttle, straight ahead.</summary>
        private static VehicleInput Ahead => new VehicleInput(Vector2.up, Vector2.zero, 0.0f);

        /// <summary>
        /// Builds the three-strip map and puts it up, so the vehicles have something to read.
        /// </summary>
        /// <remarks>
        /// The strips run east-west and are sixteen metres wide, which is wide enough that a
        /// vehicle holding a straight line down the middle of one never leaves it. The
        /// country is drawn first and the two others over it, which is the level format's
        /// own rule: rectangles overlap on purpose and the last one in the file wins.
        /// </remarks>
        private void LayTheStrips()
        {
            var strips = new LevelDefinition
            {
                Name = "Three strips",
                Description = "A road, open country and a beach, laid side by side to be driven along.",
                Seed = 7,
                Bounds = new LevelBounds { HalfExtent = 120.0f, WaterDepth = 0.7f, SeaThickness = 3.0f },
                Land = new[]
                {
                    Strip("Open country", SurfaceKind.Grass, -100.0f, 100.0f, -50.0f, 50.0f),
                    Strip("The road", SurfaceKind.Asphalt, -95.0f, 95.0f, RoadZ - 8.0f, RoadZ + 8.0f),
                    Strip("The beach", SurfaceKind.Sand, -95.0f, 95.0f, BeachZ - 8.0f, BeachZ + 8.0f),
                },
            };

            // Left inactive on purpose, so LevelLoader.Awake never runs and never loads the
            // shipped map over the top of this one. Nothing needs the loader to be alive: it
            // is here to be the one thing that can set LevelLoader.Current, which is where a
            // vehicle goes to find out what it is standing on.
            var host = new GameObject("Test Level Loader");
            host.SetActive(false);
            spawned.Add(host);

            // No catalog, so this builds no geometry and warns that it has not. What is being
            // driven on is the field the level file rasterises to; the slab below is only
            // what holds the vehicles up.
            loader = host.AddComponent<LevelLoader>();
            loader.Show(strips, false);

            GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "Test Ground";
            slab.transform.localScale = new Vector3(400.0f, 1.0f, 400.0f);
            slab.transform.position = new Vector3(0.0f, -0.5f, 0.0f);
            spawned.Add(slab);
        }

        private static LevelLand Strip(
            string name, SurfaceKind ground, float minX, float maxX, float minZ, float maxZ)
            => new LevelLand
            {
                Name = name,
                Surface = ground.ToString(),
                MinX = minX,
                MaxX = maxX,
                MinZ = minZ,
                MaxZ = maxZ,
            };

        /// <summary>
        /// Drives one vehicle flat out along one strip and reports how far east it got.
        /// </summary>
        /// <param name="kind">Vehicle to drive.</param>
        /// <param name="z">Strip to drive down.</param>
        /// <returns>The coroutine the framework steps through.</returns>
        /// <remarks>Leaves the distance covered, in metres, in <see cref="Measured"/>.</remarks>
        private IEnumerator Run(VehicleKind kind, float z)
        {
            GroundVehicle vehicle = Park(kind, z);
            yield return Drive(new[] { vehicle }, Ahead, RunSeconds);
            measured = vehicle.transform.position.x - StartX;
        }

        /// <summary>
        /// Drives one vehicle flat out along one strip and reports what it cost in fuel.
        /// </summary>
        /// <param name="kind">Vehicle to drive.</param>
        /// <param name="z">Strip to drive down.</param>
        /// <returns>The coroutine the framework steps through.</returns>
        /// <remarks>Leaves the fuel spent, in seconds of running, in <see cref="Measured"/>.</remarks>
        private IEnumerator Burn(VehicleKind kind, float z)
        {
            GroundVehicle vehicle = Park(kind, z);
            VehicleSupply tank = Fuelled(vehicle, kind);
            float full = tank.Fuel;

            yield return Drive(new[] { vehicle }, Ahead, RunSeconds);
            measured = full - tank.Fuel;
        }

        /// <summary>
        /// Parks one vehicle on one strip with the engine running and reports what that cost.
        /// </summary>
        /// <param name="kind">Vehicle to park.</param>
        /// <param name="z">Strip to park it on.</param>
        /// <returns>The coroutine the framework steps through.</returns>
        /// <remarks>Leaves the fuel spent, in seconds of running, in <see cref="Measured"/>.</remarks>
        private IEnumerator Idle(VehicleKind kind, float z)
        {
            GroundVehicle vehicle = Park(kind, z);
            VehicleSupply tank = Fuelled(vehicle, kind);
            float full = tank.Fuel;

            yield return Drive(new[] { vehicle }, VehicleInput.Idle, RunSeconds);
            measured = full - tank.Fuel;
        }

        private GroundVehicle Park(VehicleKind kind, float z)
            => Create<GroundVehicle>(kind, new Vector3(StartX, 0.05f, z), 90.0f);

        private Helicopter Fly(float z)
        {
            Helicopter aircraft = Create<Helicopter>(
                VehicleKind.Helicopter, new Vector3(StartX, 10.0f, z), 90.0f);

            var visual = new GameObject("Visual");
            visual.transform.SetParent(aircraft.transform, false);
            aircraft.ConfigureFlight(new FlightTuning(), visual.transform);
            return aircraft;
        }

        /// <summary>
        /// Bolts a full tank onto a vehicle that is about to be driven.
        /// </summary>
        /// <param name="vehicle">Vehicle to fuel.</param>
        /// <param name="kind">Which row of the table to fill it from.</param>
        /// <returns>The supply component.</returns>
        /// <remarks>
        /// Added after the vehicle is awake, so it looks its own controller up here rather
        /// than in <c>Awake</c>. The component is added to the same object either way, which
        /// is all it needs to find the thing that knows what it is standing on.
        /// </remarks>
        private static VehicleSupply Fuelled(GroundVehicle vehicle, VehicleKind kind)
        {
            VehicleTuning tuning = VehicleTuning.For(kind);
            VehicleSupply tank = vehicle.gameObject.AddComponent<VehicleSupply>();
            tank.Configure(tuning.FuelCapacity, tuning.IdleFuelDraw, 0);
            return tank;
        }

        /// <summary>
        /// Assembles the smallest thing that counts as a vehicle, facing a given way.
        /// </summary>
        /// <typeparam name="T">Controller to attach.</typeparam>
        /// <param name="kind">Vehicle whose tuning to drive with.</param>
        /// <param name="position">Where to place it.</param>
        /// <param name="yawDegrees">Heading, clockwise from world +Z.</param>
        /// <returns>The controller, already awake.</returns>
        private T Create<T>(VehicleKind kind, Vector3 position, float yawDegrees)
            where T : VehicleController
        {
            var host = new GameObject($"{kind} at z {position.z:0}");
            host.SetActive(false);
            host.transform.SetPositionAndRotation(position, Quaternion.Euler(0.0f, yawDegrees, 0.0f));

            var box = host.AddComponent<BoxCollider>();
            box.size = new Vector3(1.8f, 1.6f, 4.0f);
            box.center = new Vector3(0.0f, 0.8f, 0.0f);

            host.AddComponent<Rigidbody>();
            T controller = host.AddComponent<T>();
            controller.Configure(kind, VehicleTuning.For(kind));

            host.SetActive(true);
            spawned.Add(host);
            return controller;
        }

        private static IEnumerator Drive(
            IReadOnlyList<VehicleController> vehicles, VehicleInput input, float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
            {
                foreach (VehicleController vehicle in vehicles)
                {
                    vehicle.SetInput(input);
                }

                yield return new WaitForFixedUpdate();
            }
        }
    }
}
