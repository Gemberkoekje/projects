using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Players;
using IronFlag.Supply;
using IronFlag.Vehicles;

namespace IronFlag.Tests.PlayMode
{
    /// <summary>
    /// The design document's core loop, walked end to end: choose a vehicle, ride out of the
    /// bunker, drive it, lose it, and be back in front of the roster choosing again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All of it is a state machine spread across three components - the player, the bay a
    /// vehicle waits in, and the bunker it waits inside - and the ways it goes wrong are the
    /// ways state machines go wrong: two vehicles out at once, a pilot driving something
    /// that is still on the lift, a wreck that can be picked before it has been repaired, a
    /// player stuck at a menu because the event that should have taken them out of it went
    /// to the wrong bay.
    /// </para>
    /// <para>
    /// Vehicles are assembled here rather than loaded from the prefabs, so a failure means
    /// the code is wrong rather than the asset. The bunker is built with the two markers the
    /// real model carries, because where a vehicle appears is half of what this tests.
    /// </para>
    /// </remarks>
    public sealed class BunkerTests
    {
        /// <summary>Seconds of repairs the tests use, so they do not sit through four.</summary>
        private const float Repairs = 0.4f;

        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void CleanUp()
        {
            foreach (GameObject item in spawned)
            {
                if (item != null)
                {
                    Object.Destroy(item);
                }
            }

            spawned.Clear();
        }

        [UnityTest]
        public IEnumerator AMatchStartsWithEverybodyInsideTheBunkerChoosing()
        {
            CreateBunker(Team.Green);
            PlayerVehicleDriver player = CreatePlayer(Team.Green);
            yield return null;

            Assert.That(player.AtTheBunker, Is.True, "somebody started the match already driving");
            Assert.That(player.ActiveVehicle, Is.Null);

            foreach (VehicleController vehicle in player.Roster)
            {
                Assert.That(
                    vehicle.GetComponent<VehicleBay>().IsReady,
                    Is.True,
                    $"{vehicle.Kind} is not available to be picked");
                Assert.That(
                    vehicle.GetComponentInChildren<Renderer>(true).enabled,
                    Is.False,
                    $"{vehicle.Kind} is standing out on the field");
            }
        }

        [UnityTest]
        public IEnumerator ChoosingAVehicleRidesItOutOfTheBunkerAndHandsItOver()
        {
            TeamBunker bunker = CreateBunker(Team.Green);
            PlayerVehicleDriver player = CreatePlayer(Team.Green);
            yield return null;

            player.Select(1);
            Assert.That(player.DeploySelected(), Is.True, "the tank refused to come out");

            VehicleBay bay = player.BayFor(1);
            Assert.That(bay.IsDeploying, Is.True, "there was no ride out at all");
            Assert.That(player.AtTheBunker, Is.True, "the pilot is driving something still on the lift");
            Assert.That(
                player.Roster[1].transform.position.y,
                Is.LessThan(bunker.LiftPoint.y),
                "the lift starts at the top");

            yield return Wait(bay.DeploySeconds + 0.3f);

            Assert.That(player.AtTheBunker, Is.False, "the ride never finished");
            Assert.That(player.ActiveVehicle, Is.EqualTo(player.Roster[1]));
            // Out of the bay and clear of the door: a vehicle set down on the middle of the
            // platform would have its back end inside the wall, so it finishes half its own
            // length further out.
            Vector3 stood = player.ActiveVehicle.transform.position;

            Assert.That(
                Vector3.Distance(Flat(stood), Flat(bunker.LiftPoint)),
                Is.LessThan(3.5f),
                "the tank did not come out of the lift bay");
            Assert.That(
                stood.z,
                Is.GreaterThan(bunker.LiftPoint.z),
                "the tank is standing in its own doorway");
        }

        /// <summary>
        /// The one branch in the whole bunker: everything that flies leaves from the roof.
        /// </summary>
        [UnityTest]
        public IEnumerator TheHelicopterLeavesFromTheRoofAndClimbsAway()
        {
            TeamBunker bunker = CreateBunker(Team.Green);
            PlayerVehicleDriver player = CreatePlayer(Team.Green);
            yield return null;

            Assert.That(player.TakeTheField(3), Is.True, "the helicopter refused to come out");
            yield return Wait(0.2f);

            var flyer = (Helicopter)player.ActiveVehicle;

            Assert.That(
                Vector3.Distance(Flat(flyer.transform.position), Flat(bunker.HelipadPoint)),
                Is.LessThan(1.5f),
                "it did not take off from the pad");
            Assert.That(
                flyer.Altitude,
                Is.EqualTo(flyer.Flight.CruiseAltitude).Within(1.0f),
                "it was handed over at roof height, and with no collective it would stay there");
        }

        /// <summary>
        /// One vehicle at a time is the rule the whole milestone turns on. M3 let a pilot
        /// step between four vehicles all parked on the start line; M4 keeps three of them
        /// inside the bunker where nobody can shoot them and nobody can drive them.
        /// </summary>
        [UnityTest]
        public IEnumerator OnlyOneVehicleIsEverOutOfTheBunker()
        {
            CreateBunker(Team.Green);
            PlayerVehicleDriver player = CreatePlayer(Team.Green);
            yield return null;

            player.TakeTheField(0);
            yield return Wait(0.2f);

            int onField = 0;
            foreach (VehicleController vehicle in player.Roster)
            {
                if (vehicle.GetComponent<VehicleBay>().IsOnField)
                {
                    onField++;
                }
            }

            Assert.That(onField, Is.EqualTo(1), "the rest of the roster is out on the map");
        }

        [UnityTest]
        public IEnumerator AWreckedVehicleCannotBePickedUntilItHasBeenRepaired()
        {
            CreateBunker(Team.Green);
            PlayerVehicleDriver player = CreatePlayer(Team.Green);
            yield return null;

            player.TakeTheField(0);
            yield return Wait(0.2f);

            player.ActiveVehicle.GetComponent<VehicleHealth>().TakeDamage(999.0f, Team.Brown);
            yield return null;

            VehicleBay bay = player.BayFor(0);

            Assert.That(player.AtTheBunker, Is.True, "the pilot is still driving a wreck");
            Assert.That(bay.IsRepairing, Is.True, "nobody started the repairs");
            Assert.That(bay.RepairCountdown, Is.GreaterThan(0.0f), "there is no countdown to show");
            Assert.That(player.CanDeploy, Is.False, "the wreck can be driven straight back out");
            Assert.That(player.DeploySelected(), Is.False, "the wreck came back out anyway");

            yield return Wait(Repairs + 0.2f);

            Assert.That(bay.IsReady, Is.True, "it never finished being repaired");
            Assert.That(player.CanDeploy, Is.True, "it is repaired but still cannot be picked");
        }

        /// <summary>
        /// Anything can be highlighted, including a wreck. Skipping over the vehicle that is
        /// being repaired would hide the countdown the player most wants to read.
        /// </summary>
        [UnityTest]
        public IEnumerator TheSelectionCanRestOnAVehicleThatIsNotReadyYet()
        {
            CreateBunker(Team.Green);
            PlayerVehicleDriver player = CreatePlayer(Team.Green);
            yield return null;

            player.TakeTheField(2);
            yield return Wait(0.2f);
            player.ActiveVehicle.GetComponent<VehicleHealth>().TakeDamage(999.0f, Team.Brown);
            yield return null;

            player.Select(2);

            Assert.That(player.Selected, Is.EqualTo(2), "the highlight refused to sit on the wreck");
            Assert.That(player.BayFor(2).IsRepairing, Is.True);
        }

        [UnityTest]
        public IEnumerator TheSelectionWrapsAroundTheRosterBothWays()
        {
            CreateBunker(Team.Green);
            PlayerVehicleDriver player = CreatePlayer(Team.Green);
            yield return null;

            player.Select(0);
            player.Select(player.Selected - 1);
            Assert.That(player.Selected, Is.EqualTo(player.Roster.Count - 1), "it did not wrap backwards");

            player.Select(player.Selected + 1);
            Assert.That(player.Selected, Is.EqualTo(0), "it did not wrap forwards");
        }

        /// <summary>
        /// Driving home and parking is the cheap way to change vehicles: no explosion, no
        /// repairs, and the vehicle is available again immediately. It is the reason to make
        /// the drive rather than press the self-destruct.
        /// </summary>
        [UnityTest]
        public IEnumerator ParkingAtTheBunkerCostsNothingButTheDriveHome()
        {
            TeamBunker bunker = CreateBunker(Team.Green);
            PlayerVehicleDriver player = CreatePlayer(Team.Green);
            yield return null;

            player.TakeTheField(0);
            yield return Wait(0.2f);

            VehicleController jeep = player.ActiveVehicle;
            jeep.Teleport(bunker.transform.position + new Vector3(0.0f, 0.0f, 3.0f), 0.0f);
            yield return null;

            Assert.That(player.IsHome, Is.True, "standing on the bunker does not count as home");
            Assert.That(player.Recall(), Is.True, "it would not park");

            Assert.That(player.AtTheBunker, Is.True, "the pilot is still out there");
            Assert.That(player.BayFor(0).IsReady, Is.True, "parking cost it a repair");
            Assert.That(
                jeep.GetComponent<VehicleHealth>().IsDestroyed, Is.False, "parking blew it up");
        }

        /// <summary>
        /// The other half of parking: picking the same vehicle again afterwards.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A stowed vehicle is kinematic, and <see cref="VehicleBay.Deploy"/> freezes it
        /// again on the way out - so this is the one path through the loop that freezes
        /// something already frozen. Unity answers a velocity written onto a kinematic body
        /// with a warning and a stack trace, which is harmless and is noise, and this project
        /// keeps its log clean enough that the noise is worth a test of its own. So the log
        /// is asserted on here and not only the state.
        /// </para>
        /// <para>
        /// It is also the path the command-line still walks: <c>VehicleSandboxScene</c> stows
        /// every bay to put both rosters back inside and then sends one vehicle out again.
        /// </para>
        /// </remarks>
        [UnityTest]
        public IEnumerator SendingAStowedVehicleBackOutSaysNothingToTheLog()
        {
            TeamBunker bunker = CreateBunker(Team.Green);
            PlayerVehicleDriver player = CreatePlayer(Team.Green);
            yield return null;

            Assert.That(player.TakeTheField(0), Is.True, "the jeep refused to come out");
            yield return Wait(0.2f);

            VehicleController jeep = player.ActiveVehicle;
            jeep.Teleport(bunker.transform.position + new Vector3(0.0f, 0.0f, 3.0f), 0.0f);
            yield return null;

            Assert.That(player.Recall(), Is.True, "it would not park");
            Assert.That(player.BayFor(0).IsReady, Is.True, "parking cost it a repair");

            var complaints = new List<string>();
            Application.LogCallback listen = (message, trace, kind) =>
            {
                if (kind != LogType.Log)
                {
                    complaints.Add($"{kind}: {message}");
                }
            };

            Application.logMessageReceived += listen;
            try
            {
                player.Select(0);
                Assert.That(
                    player.DeploySelected(), Is.True, "the parked jeep refused to come back out");
                yield return Wait(player.BayFor(0).DeploySeconds + 0.3f);
            }
            finally
            {
                Application.logMessageReceived -= listen;
            }

            Assert.That(
                complaints,
                Is.Empty,
                "riding back out of the bunker wrote to the log: "
                    + string.Join(" | ", complaints));

            Assert.That(player.AtTheBunker, Is.False, "the second ride out never finished");
            Assert.That(player.ActiveVehicle, Is.EqualTo(jeep), "something else came out instead");
            Assert.That(
                jeep.GetComponent<Rigidbody>().isKinematic,
                Is.False,
                "it is out on the field and still frozen");
        }

        /// <summary>
        /// The design document's answer to running out of fuel in the middle of nowhere, and
        /// it deliberately costs exactly what dying costs.
        /// </summary>
        [UnityTest]
        public IEnumerator LeavingTheFieldAnywhereElseBlowsTheVehicleUp()
        {
            CreateBunker(Team.Green);
            PlayerVehicleDriver player = CreatePlayer(Team.Green);
            yield return null;

            player.TakeTheField(0);
            yield return Wait(0.2f);

            player.ActiveVehicle.Teleport(new Vector3(40.0f, 0.0f, 40.0f), 0.0f);
            yield return null;

            Assert.That(player.IsHome, Is.False, "the middle of the map counts as home");
            Assert.That(player.Recall(), Is.True, "it would not scuttle");
            yield return null;

            Assert.That(player.AtTheBunker, Is.True, "the pilot is still out there");
            Assert.That(player.BayFor(0).IsRepairing, Is.True, "scuttling was free");
        }

        /// <summary>
        /// A vehicle that comes home empty has to leave full, or a match would run down to
        /// four dry vehicles nobody can do anything with.
        /// </summary>
        [UnityTest]
        public IEnumerator AVehicleThatComesBackInIsRepairedAndRefuelled()
        {
            CreateBunker(Team.Green);
            PlayerVehicleDriver player = CreatePlayer(Team.Green);
            yield return null;

            player.TakeTheField(0);
            yield return Wait(0.2f);

            VehicleController jeep = player.ActiveVehicle;
            var supply = jeep.GetComponent<VehicleSupply>();
            supply.RunDry();
            supply.FireOff();
            jeep.GetComponent<VehicleHealth>().TakeDamage(999.0f, Team.Brown);

            yield return Wait(Repairs + 0.3f);
            player.TakeTheField(0);
            yield return Wait(0.2f);

            Assert.That(supply.FuelFraction, Is.EqualTo(1.0f).Within(0.01f), "it came back out dry");
            Assert.That(supply.RoundsFraction, Is.EqualTo(1.0f).Within(0.01f), "it came back out empty");
            Assert.That(
                jeep.GetComponent<VehicleHealth>().Fraction,
                Is.EqualTo(1.0f).Within(0.01f),
                "it came back out damaged");
        }

        /// <summary>
        /// The camera has to end up on whatever the player is looking at, and there are two
        /// of those: a vehicle when they have one out, and their own bunker when they do not.
        /// </summary>
        [UnityTest]
        public IEnumerator TheCameraFollowsTheVehicleOutAndTheBunkerWhenThereIsNone()
        {
            TeamBunker bunker = CreateBunker(Team.Green);
            TopDownCameraRig rig = CreateCameraRig();
            PlayerVehicleDriver player = CreatePlayer(Team.Green, rig);
            yield return null;

            Assert.That(
                Vector3.Distance(rig.transform.position, bunker.transform.position),
                Is.LessThan(rig.Distance + 10.0f),
                "the camera is not showing the player their own bunker");

            player.TakeTheField(0);
            yield return Wait(0.3f);
            player.ActiveVehicle.Teleport(new Vector3(60.0f, 0.0f, 20.0f), 0.0f);

            // The camera eases rather than cuts once it has a target, so this waits several
            // damping constants rather than a frame.
            yield return Wait(1.5f);

            Assert.That(rig.Target, Is.EqualTo(player.ActiveVehicle), "the camera stayed at home");
            Assert.That(
                Vector3.Distance(rig.transform.position, player.ActiveVehicle.transform.position),
                Is.LessThan(rig.Distance + 10.0f),
                "the camera never caught up with the vehicle");
        }

        private static Vector3 Flat(Vector3 at) => new Vector3(at.x, 0.0f, at.z);

        private static IEnumerator Wait(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        /// <summary>
        /// Builds a bunker with the two markers the real model exports.
        /// </summary>
        /// <param name="side">Side it belongs to.</param>
        /// <returns>The bunker, with a supply point on it like the generated scene has.</returns>
        private TeamBunker CreateBunker(Team side)
        {
            var host = new GameObject($"Bunker ({side})");
            host.transform.SetPositionAndRotation(
                new Vector3(0.0f, 0.0f, side == Team.Green ? -50.0f : 50.0f),
                Quaternion.Euler(0.0f, side == Team.Green ? 0.0f : 180.0f, 0.0f));
            spawned.Add(host);

            TeamBunker bunker = host.AddComponent<TeamBunker>();
            bunker.Configure(
                side,
                Marker(host.transform, TeamBunker.LiftNodeName, new Vector3(0.0f, 0.25f, 5.2f)),
                Marker(host.transform, TeamBunker.HelipadNodeName, new Vector3(2.2f, 3.9f, -1.8f)));

            host.AddComponent<SupplyPoint>().Configure(side, 12.0f, 0.25f, 0.25f, true);
            return bunker;
        }

        private static Transform Marker(Transform parent, string name, Vector3 at)
        {
            var host = new GameObject(name);
            host.transform.SetParent(parent, false);
            host.transform.localPosition = at;
            return host.transform;
        }

        private PlayerVehicleDriver CreatePlayer(Team side) => CreatePlayer(side, null);

        /// <summary>
        /// Builds one player with a full roster of four, all of them in the bunker.
        /// </summary>
        /// <param name="side">Side they play.</param>
        /// <param name="rig">Camera rig to give them, or null for none.</param>
        /// <returns>The driver, already awake.</returns>
        /// <remarks>
        /// Built inactive and switched on at the end, so that the driver wakes up with a
        /// roster rather than with an empty list - which is the same order the scene builder
        /// produces and the only order in which the bays get watched.
        /// </remarks>
        private PlayerVehicleDriver CreatePlayer(Team side, TopDownCameraRig rig)
        {
            var roster = new List<VehicleController>();
            foreach (VehicleKind kind in new[]
            {
                VehicleKind.Jeep, VehicleKind.Tank, VehicleKind.Asv, VehicleKind.Helicopter,
            })
            {
                roster.Add(CreateVehicle(kind, side));
            }

            var host = new GameObject($"Player ({side})");
            host.SetActive(false);
            spawned.Add(host);

            PlayerVehicleDriver driver = host.AddComponent<PlayerVehicleDriver>();
            driver.Configure(rig, roster);
            host.SetActive(true);
            return driver;
        }

        /// <summary>
        /// Assembles one vehicle with everything the bunker flow reaches for.
        /// </summary>
        /// <param name="kind">Which vehicle to build.</param>
        /// <param name="side">Side to paint it.</param>
        /// <returns>The controller.</returns>
        private VehicleController CreateVehicle(VehicleKind kind, Team side)
        {
            var host = new GameObject($"{kind} ({side})");
            host.SetActive(false);
            spawned.Add(host);

            var skin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            skin.name = "Visual";
            Object.DestroyImmediate(skin.GetComponent<Collider>());
            skin.transform.SetParent(host.transform, false);

            host.AddComponent<BoxCollider>().size = new Vector3(2.0f, 2.0f, 4.0f);
            host.AddComponent<Rigidbody>();
            host.AddComponent<VehicleTeamPaint>().Team = side;

            VehicleTuning tuning = VehicleTuning.For(kind);
            VehicleController controller = kind == VehicleKind.Helicopter
                ? host.AddComponent<Helicopter>()
                : host.AddComponent<GroundVehicle>();
            controller.Configure(kind, tuning);

            host.AddComponent<VehicleHealth>().Configure(tuning.HitPoints, null, 3.0f);
            host.AddComponent<VehicleSupply>().Configure(
                tuning.FuelCapacity, tuning.IdleFuelDraw, WeaponTuning.For(kind).Rounds);
            host.AddComponent<VehicleBay>().Configure(Repairs, 0.25f);

            host.SetActive(true);
            return controller;
        }

        private TopDownCameraRig CreateCameraRig()
        {
            var host = new GameObject("Test Camera", typeof(Camera));
            spawned.Add(host);
            return host.AddComponent<TopDownCameraRig>();
        }
    }
}
