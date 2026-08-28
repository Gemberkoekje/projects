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
    /// The underground half of a bunker with the clock running: vehicles waiting where a
    /// camera can see them, a lift car that follows the highlight, and a ride out that goes
    /// through the hall rather than through the ground.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="BunkerTests"/> covers the same loop from a bunker with nothing underneath
    /// it, which is still a supported shape - the hall is scenery, and a map built without it
    /// plays identically. This file is the other half: everything that only happens when
    /// there <em>is</em> a hall, and the guarantee that the two shapes end in the same place.
    /// </para>
    /// <para>
    /// The hall here is four empty markers rather than the model, for the same reason the
    /// bunker in <see cref="BunkerTests"/> is two: a failure should mean the code is wrong,
    /// not that the <c>.glb</c> is missing. What the real model is, and where it puts those
    /// markers, is asserted in the edit-mode wiring tests against the generated scene.
    /// </para>
    /// </remarks>
    public sealed class BunkerBaseTests
    {
        /// <summary>Seconds of repairs the tests use, so they do not sit through four.</summary>
        private const float Repairs = 0.4f;

        /// <summary>Seconds the ride out takes here, short enough to wait through.</summary>
        private const float Ride = 0.4f;

        /// <summary>Where the four bays sit in the bunker's own frame, in roster order.</summary>
        /// <remarks>
        /// The real hall's arrangement in miniature: two columns, the heavy pair upstairs,
        /// and roster order running from the left of the picture - which is the bunker's +x,
        /// because the select camera looks back along its heading.
        /// </remarks>
        private static readonly Vector3[] Bays =
        {
            new Vector3(6.0f, -13.0f, 4.6f),
            new Vector3(6.0f, -9.0f, 4.6f),
            new Vector3(-6.0f, -13.0f, 4.6f),
            new Vector3(-6.0f, -9.0f, 4.6f),
        };

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

        /// <summary>
        /// Everything waiting is in its own bay, drawn, and impossible to touch.
        /// </summary>
        /// <remarks>
        /// The three together are the whole change: a stowed vehicle used to be hidden, and
        /// what made it unreachable was having no renderers. Now it is something to look at,
        /// and what makes it unreachable is being twelve metres underground with its
        /// colliders off.
        /// </remarks>
        [UnityTest]
        public IEnumerator EveryVehicleWaitsInItsOwnBayWhereItCanBeSeen()
        {
            TeamBunker bunker = CreateBunker(Team.Green, withHall: true);
            PlayerVehicleDriver player = CreatePlayer(Team.Green);
            yield return null;

            for (int slot = 0; slot < player.Roster.Count; slot++)
            {
                VehicleController vehicle = player.Roster[slot];

                Assert.That(
                    Vector3.Distance(vehicle.transform.position, bunker.BayFor(slot)),
                    Is.LessThan(0.01f),
                    $"the {vehicle.Kind} is not waiting in bay {slot}");
                Assert.That(
                    vehicle.GetComponentInChildren<Renderer>(true).enabled,
                    Is.True,
                    $"the {vehicle.Kind} is invisible in a room built to show it");
                Assert.That(
                    vehicle.GetComponentInChildren<Collider>(true).enabled,
                    Is.False,
                    $"the {vehicle.Kind} can be hit while it waits underground");
                Assert.That(
                    vehicle.transform.position.y,
                    Is.LessThan(-4.0f),
                    $"the {vehicle.Kind} is waiting above ground");
            }
        }

        /// <summary>
        /// Every vehicle waits with its flank to the camera and its nose towards the shaft.
        /// </summary>
        /// <remarks>
        /// Two things at once, and the second is why the first is free: a tank seen nose-on
        /// in a dark room is a box, and a vehicle facing the shaft drives forwards onto the
        /// lift rather than reversing onto it.
        /// </remarks>
        [UnityTest]
        public IEnumerator EveryVehicleWaitsFacingTheShaft()
        {
            TeamBunker bunker = CreateBunker(Team.Green, withHall: true);
            PlayerVehicleDriver player = CreatePlayer(Team.Green);
            yield return null;

            for (int slot = 0; slot < player.Roster.Count; slot++)
            {
                VehicleController vehicle = player.Roster[slot];
                Vector3 bay = bunker.transform.InverseTransformPoint(vehicle.transform.position);
                Vector3 facing = bunker.transform.InverseTransformDirection(vehicle.transform.forward);

                Assert.That(
                    Mathf.Abs(facing.x),
                    Is.GreaterThan(0.99f),
                    $"the {vehicle.Kind} is not presenting its flank to the select camera");
                Assert.That(
                    Mathf.Sign(facing.x),
                    Is.Not.EqualTo(Mathf.Sign(bay.x)),
                    $"the {vehicle.Kind} would have to reverse onto the lift");
            }
        }

        /// <summary>
        /// A wreck is not drawn in its bay, and it is back the moment it is repaired.
        /// </summary>
        /// <remarks>
        /// An empty bay is the honest picture of a vehicle in the shop, because the only
        /// model this game has of one is the intact model - an undamaged tank sitting under
        /// the word REPAIRING would be saying the opposite of what the console says.
        /// </remarks>
        [UnityTest]
        public IEnumerator AWreckLeavesItsBayEmptyUntilItIsRepaired()
        {
            CreateBunker(Team.Green, withHall: true);
            PlayerVehicleDriver player = CreatePlayer(Team.Green);
            yield return null;

            player.TakeTheField(1);
            yield return null;
            player.Roster[1].GetComponent<VehicleHealth>().SelfDestruct();
            yield return null;

            VehicleBay bay = player.BayFor(1);
            Assert.That(bay.IsRepairing, Is.True, "the tank was not wrecked");
            Assert.That(
                player.Roster[1].GetComponentInChildren<Renderer>(true).enabled,
                Is.False,
                "a wreck is standing in its bay looking brand new");

            yield return Wait(Repairs + 0.3f);

            Assert.That(bay.IsReady, Is.True, "the tank never came back");
            Assert.That(
                player.Roster[1].GetComponentInChildren<Renderer>(true).enabled,
                Is.True,
                "the repaired tank is still invisible");
        }

        /// <summary>
        /// The lift car goes to whichever bay is highlighted, and back to the surface when
        /// the player leaves.
        /// </summary>
        /// <remarks>
        /// This is the only part of the select screen that answers "what did I just press"
        /// while the player is looking at the picture rather than at the words under it - and
        /// it means the ride out starts with the lift already where it needs to be.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheLiftWaitsAtWhicheverBayIsHighlighted()
        {
            TeamBunker bunker = CreateBunker(Team.Green, withHall: true);
            PlayerVehicleDriver player = CreatePlayer(Team.Green);
            yield return null;

            player.Select(0);
            yield return Wait(1.5f);
            Assert.That(
                bunker.Car.Height,
                Is.EqualTo(bunker.BayFor(0).y).Within(0.05f),
                "the lift did not come down to the jeep");

            player.Select(1);
            yield return Wait(1.5f);
            Assert.That(
                bunker.Car.Height,
                Is.EqualTo(bunker.BayFor(1).y).Within(0.05f),
                "the lift did not move to the tank's deck");

            player.TakeTheField(1);
            yield return Wait(2.5f);
            Assert.That(
                bunker.Car.Height,
                Is.EqualTo(bunker.LiftPoint.y).Within(0.05f),
                "the lift stayed underground with nobody at home");
        }

        /// <summary>
        /// The ride out goes bay, shaft, surface - and ends exactly where a bunker with no
        /// hall would have put the same vehicle.
        /// </summary>
        /// <remarks>
        /// The last clause is the one that matters. Everything M4 settled about where a
        /// vehicle stands when it arrives - half its own length past the lift, the helicopter
        /// at its cruising altitude - is untouched by there now being a route to get there.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheRideOutClimbsTheShaftAndFinishesWhereItAlwaysDid()
        {
            TeamBunker bunker = CreateBunker(Team.Green, withHall: true);
            PlayerVehicleDriver player = CreatePlayer(Team.Green);
            yield return null;

            player.Select(1);
            Assert.That(player.DeploySelected(), Is.True, "the tank refused to come out");

            VehicleController tank = player.Roster[1];
            Assert.That(
                Vector3.Distance(tank.transform.position, bunker.BayFor(1)),
                Is.LessThan(0.2f),
                "the ride out did not start in the bay");

            // Halfway through it is in the shaft rather than in either end of the journey.
            yield return Wait(Ride * 0.55f);
            Vector3 shaft = bunker.transform.InverseTransformPoint(tank.transform.position);
            Assert.That(
                Mathf.Abs(shaft.x),
                Is.LessThan(2.5f),
                "the tank went up through the ceiling of its own bay");
            Assert.That(
                tank.transform.position.y,
                Is.LessThan(bunker.LiftPoint.y),
                "the tank was already at the surface halfway through the ride");

            // Caught on the frame it arrives rather than after a wait: nothing in this scene
            // is holding a vehicle up, so a tank left alone for half a second afterwards has
            // fallen a metre and a half and the height would be a measurement of gravity.
            yield return UntilDeployed(player, Ride + 0.6f);

            Assert.That(player.ActiveVehicle, Is.EqualTo(tank), "the ride never finished");
            Assert.That(
                tank.transform.position.y,
                Is.EqualTo(bunker.LiftPoint.y).Within(0.2f),
                "the tank was handed over somewhere other than the shaft mouth");

            Vector3 forward = Quaternion.Euler(0.0f, bunker.FacingYawDegrees, 0.0f) * Vector3.forward;
            float past = Vector3.Dot(tank.transform.position - bunker.LiftPoint, forward);
            Assert.That(past, Is.GreaterThan(0.5f), "the tank is standing in the shaft mouth");
        }

        /// <summary>
        /// The hall is only drawn for the player who is looking into it.
        /// </summary>
        /// <remarks>
        /// The ground is opaque from above and hides the hall on its own; the shaft is the
        /// hole in that argument, and a player driving over their own bunker would otherwise
        /// be looking down a lit stairwell. It stays drawn for the whole ride out, because
        /// the ride out is the thing being watched.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheHallIsOnlyDrawnWhileItsOwnPlayerIsChoosing()
        {
            TeamBunker bunker = CreateBunker(Team.Green, withHall: true);
            PlayerVehicleDriver player = CreatePlayer(Team.Green);
            yield return null;

            Assert.That(bunker.Hall.activeSelf, Is.True, "the player is choosing in the dark");

            player.Select(1);
            player.DeploySelected();
            yield return null;
            Assert.That(
                bunker.Hall.activeSelf, Is.True, "the hall went out during the ride out of it");

            yield return Wait(Ride + 0.4f);
            Assert.That(
                bunker.Hall.activeSelf,
                Is.False,
                "the base is still drawn with nobody in it");

            player.Recall();
            yield return Wait(2.0f);
            Assert.That(bunker.Hall.activeSelf, Is.True, "the player came home to an unlit base");
        }

        /// <summary>
        /// The highlighted bay is the lit one, and only it.
        /// </summary>
        [UnityTest]
        public IEnumerator OnlyTheHighlightedBayIsLit()
        {
            TeamBunker bunker = CreateBunker(Team.Green, withHall: true);
            PlayerVehicleDriver player = CreatePlayer(Team.Green);
            yield return null;

            player.Select(2);
            yield return null;

            Assert.That(bunker.ChosenBay, Is.EqualTo(2), "the lit bay is not the chosen one");

            player.TakeTheField(2);
            yield return null;

            Assert.That(
                bunker.ChosenBay,
                Is.EqualTo(-1),
                "a bay is still lit for a player who is out on the field");
        }

        private static IEnumerator Wait(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        /// <summary>
        /// Waits until the player has a vehicle, and no longer.
        /// </summary>
        /// <param name="player">The player deploying one.</param>
        /// <param name="patience">Seconds to give up after.</param>
        /// <returns>An enumerator to yield on.</returns>
        private static IEnumerator UntilDeployed(PlayerVehicleDriver player, float patience)
        {
            float until = Time.time + patience;
            while (player.ActiveVehicle == null && Time.time < until)
            {
                yield return null;
            }
        }

        /// <summary>
        /// Builds a bunker, optionally with a hall of four bay markers under it.
        /// </summary>
        /// <param name="side">Side it belongs to.</param>
        /// <param name="withHall">Whether to give it an underground base.</param>
        /// <returns>The bunker.</returns>
        private TeamBunker CreateBunker(Team side, bool withHall)
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

            if (!withHall)
            {
                return bunker;
            }

            var hall = new GameObject("Hall");
            hall.transform.SetParent(host.transform, false);

            var decks = new Transform[Bays.Length];
            for (int slot = 0; slot < Bays.Length; slot++)
            {
                decks[slot] = Marker(
                    hall.transform, $"{TeamBunker.BayNodePrefix}{slot}", Bays[slot]);
            }

            var car = new GameObject("Lift");
            car.transform.SetParent(host.transform, false);
            car.transform.position = bunker.LiftPoint;
            BunkerLift lift = car.AddComponent<BunkerLift>();
            lift.Configure(40.0f);

            bunker.ConfigureBase(
                hall,
                Marker(hall.transform, TeamBunker.SkylineNodeName, new Vector3(0.0f, -4.2f, 6.9f)),
                decks,
                new Renderer[Bays.Length],
                new Light[Bays.Length],
                lift);

            return bunker;
        }

        private static Transform Marker(Transform parent, string name, Vector3 at)
        {
            var host = new GameObject(name);
            host.transform.SetParent(parent, false);
            host.transform.localPosition = at;
            return host.transform;
        }

        /// <summary>
        /// Builds one player with a full roster of four, all of them in the bunker.
        /// </summary>
        /// <param name="side">Side they play.</param>
        /// <returns>The driver, already awake.</returns>
        /// <remarks>
        /// Built inactive and switched on at the end, so the driver wakes with a roster
        /// rather than with an empty list - the same order the scene builder produces, and
        /// the only order in which the bays get watched.
        /// </remarks>
        private PlayerVehicleDriver CreatePlayer(Team side)
        {
            var roster = new List<VehicleController>();
            foreach (VehicleKind kind in VehicleRoster.Kinds)
            {
                roster.Add(CreateVehicle(kind, side));
            }

            var host = new GameObject($"Player ({side})");
            host.SetActive(false);
            spawned.Add(host);

            PlayerVehicleDriver driver = host.AddComponent<PlayerVehicleDriver>();
            driver.Configure(null, roster);
            host.SetActive(true);
            return driver;
        }

        /// <summary>
        /// Assembles one vehicle with everything the bunker flow reaches for.
        /// </summary>
        /// <param name="kind">Which vehicle to build.</param>
        /// <param name="side">Side to paint it.</param>
        /// <returns>Its controller.</returns>
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
            host.AddComponent<VehicleBay>().Configure(Repairs, Ride);

            host.SetActive(true);
            return controller;
        }
    }
}
