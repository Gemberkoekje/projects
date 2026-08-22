using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Levels;
using IronFlag.Vehicles;

namespace IronFlag.Tests.PlayMode
{
    /// <summary>
    /// The drowning rule, on its own: who the sea takes and who it does not.
    /// </summary>
    /// <remarks>
    /// The map's own water is checked where it belongs, in
    /// <see cref="LevelLoadingTests"/>, driving on the real thing. What is here is the rule
    /// itself, because it is a rule that can only be wrong in ways nobody would see until it
    /// happened to them mid-match: a helicopter that dies for descending, a vehicle waiting
    /// in its own bunker that drowns because the bunker floor is below the water line, or a
    /// wreck that goes up twice.
    /// </remarks>
    public sealed class WaterTests
    {
        /// <summary>Height of the water surface in these tests, in metres.</summary>
        private const float Surface = -0.7f;

        /// <summary>Height a vehicle drowns below in these tests, in metres.</summary>
        private const float Drowns = -0.35f;

        private readonly List<GameObject> spawned = new List<GameObject>();

        /// <summary>
        /// Clears the world between tests, immediately rather than at the end of the frame.
        /// </summary>
        /// <remarks>
        /// <see cref="WaterLine.Active"/> is a static. A sea left registered by a deferred
        /// destroy would still be walking the roll-call while the next test sets up, and the
        /// next test would blame the vehicle.
        /// </remarks>
        [TearDown]
        public void CleanUp()
        {
            foreach (GameObject item in spawned)
            {
                if (item != null)
                {
                    Object.DestroyImmediate(item);
                }
            }

            spawned.Clear();
        }

        /// <summary>
        /// The design document's water: driving into it costs the vehicle, exactly as being
        /// shot does.
        /// </summary>
        [UnityTest]
        public IEnumerator AGroundVehicleThatGoesIntoTheWaterIsLost()
        {
            CreateSea();
            VehicleController tank = CreateVehicle(
                VehicleKind.Tank, Team.Green, new Vector3(0.0f, Drowns - 0.2f, 0.0f));
            yield return null;

            Assert.That(
                tank.GetComponent<VehicleHealth>().IsDestroyed,
                Is.True,
                "the tank drove into the sea and carried on");
        }

        /// <summary>
        /// Land is anything at or above the plane the game is played on.
        /// </summary>
        [UnityTest]
        public IEnumerator AVehicleOnTheGroundIsNotInTheSea()
        {
            CreateSea();
            VehicleController tank = CreateVehicle(VehicleKind.Tank, Team.Green, Vector3.zero);
            yield return null;
            yield return null;

            Assert.That(
                tank.GetComponent<VehicleHealth>().IsDestroyed,
                Is.False,
                "a tank standing on the ground drowned");
        }

        /// <summary>
        /// The helicopter's "ignores ground terrain" falls out of a rule about height rather
        /// than out of a check on what kind of vehicle it is.
        /// </summary>
        /// <remarks>
        /// <see cref="FlightTuning.MinAltitude"/> holds it at or above the land, so it is
        /// never below the line - even hovering over open water at the bottom of its range.
        /// If that floor were ever lowered, this is where it would be noticed.
        /// </remarks>
        [UnityTest]
        public IEnumerator AHelicopterCanHoverOverTheSea()
        {
            CreateSea();
            VehicleController helicopter = CreateVehicle(
                VehicleKind.Helicopter, Team.Brown, Vector3.zero);

            Assert.That(
                ((Helicopter)helicopter).Flight.MinAltitude,
                Is.GreaterThanOrEqualTo(Drowns),
                "a helicopter can descend below the water line");

            yield return null;
            yield return null;

            Assert.That(
                helicopter.GetComponent<VehicleHealth>().IsDestroyed,
                Is.False,
                "the helicopter drowned in mid-air");
        }

        /// <summary>
        /// A vehicle waiting in its bunker cannot drown, and nobody had to write that rule.
        /// </summary>
        /// <remarks>
        /// <see cref="VehicleBay"/> stows a vehicle by disabling its controller, which takes
        /// it off <see cref="VehicleController.OnTheField"/> - the same roll-call the flag
        /// uses, and the same one the sea walks. Membership is exactly "drivable", so the
        /// question of how deep a bunker's floor is never comes up.
        /// </remarks>
        [UnityTest]
        public IEnumerator AVehicleThatIsNotOnTheFieldCannotDrown()
        {
            CreateSea();
            VehicleController stowed = CreateVehicle(
                VehicleKind.Jeep, Team.Green, new Vector3(0.0f, -5.0f, 0.0f));
            stowed.enabled = false;
            yield return null;
            yield return null;

            Assert.That(
                stowed.GetComponent<VehicleHealth>().IsDestroyed,
                Is.False,
                "a stowed vehicle drowned inside its own bunker");
        }

        /// <summary>
        /// The sea does not keep killing something it has already killed.
        /// </summary>
        /// <remarks>
        /// A wreck under the water line is still under the water line the next frame. Every
        /// one of those would be another explosion and another trip home for a pilot who is
        /// already on their way, which is why <see cref="VehicleHealth.SelfDestruct"/>
        /// refuses to fire twice - checked here because this is the one caller that asks
        /// every frame.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheSeaOnlyTakesAVehicleOnce()
        {
            CreateSea();
            VehicleController tank = CreateVehicle(
                VehicleKind.Tank, Team.Green, new Vector3(0.0f, Drowns - 1.0f, 0.0f));

            var health = tank.GetComponent<VehicleHealth>();
            int wrecks = 0;
            health.Destroyed += _ => wrecks++;

            yield return null;
            yield return null;
            yield return null;

            Assert.That(wrecks, Is.EqualTo(1), $"the sea destroyed the same tank {wrecks} times");
        }

        /// <summary>
        /// Builds a sea with nothing in it.
        /// </summary>
        /// <returns>The water line.</returns>
        private WaterLine CreateSea()
        {
            var host = new GameObject("Sea");
            host.SetActive(false);
            spawned.Add(host);

            WaterLine sea = host.AddComponent<WaterLine>();
            sea.Configure(Surface, Drowns);

            host.SetActive(true);
            return sea;
        }

        /// <summary>
        /// Builds one vehicle that can be hurt and can be found on the roll-call.
        /// </summary>
        /// <param name="kind">Which vehicle.</param>
        /// <param name="side">Which team it is on.</param>
        /// <param name="at">Where it starts.</param>
        /// <returns>Its controller.</returns>
        /// <remarks>
        /// The rigidbody is kinematic, so a test that puts a vehicle at a height is testing
        /// the rule about that height rather than how long gravity takes to get there.
        /// </remarks>
        private VehicleController CreateVehicle(VehicleKind kind, Team side, Vector3 at)
        {
            var host = new GameObject($"{kind} ({side})");
            host.SetActive(false);
            host.transform.position = at;
            spawned.Add(host);

            host.AddComponent<Rigidbody>().isKinematic = true;
            host.AddComponent<VehicleTeamPaint>().Team = side;

            VehicleTuning tuning = VehicleTuning.For(kind);
            VehicleController controller = kind == VehicleKind.Helicopter
                ? host.AddComponent<Helicopter>()
                : host.AddComponent<GroundVehicle>();
            controller.Configure(kind, tuning);
            host.AddComponent<VehicleHealth>().Configure(tuning.HitPoints, null, 2.5f);

            host.SetActive(true);
            return controller;
        }
    }
}
