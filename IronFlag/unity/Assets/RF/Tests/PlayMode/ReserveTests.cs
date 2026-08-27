using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Objective;
using IronFlag.Players;
using IronFlag.Supply;
using IronFlag.Vehicles;

namespace IronFlag.Tests.PlayMode
{
    /// <summary>
    /// The finite roster: what a wreck costs, what it costs to park instead, what happens to
    /// a vehicle a side has run out of, and the one loss it cannot come back from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// None of this can be checked by reading a table, because every rule in it is about an
    /// event reaching something that was not looking at the vehicle when it happened - a
    /// reserve built with the map counting a wreck, and a match built with the session
    /// hearing that a side has nothing left to win with.
    /// </para>
    /// <para>
    /// The rig is <see cref="BunkerTests"/>'s, assembled rather than loaded from prefabs for
    /// the same reason: a failure here should mean the code is wrong, not the asset. What is
    /// new is that these scenes contain a <see cref="TeamReserve"/> at all - most do not,
    /// and a scene without one is deliberately a scene with no limit.
    /// </para>
    /// </remarks>
    public sealed class ReserveTests
    {
        /// <summary>Seconds of repairs the tests use, so they do not sit through four.</summary>
        private const float Repairs = 0.4f;

        private readonly List<GameObject> spawned = new List<GameObject>();

        /// <summary>
        /// Clears the scene between tests, immediately rather than at the end of the frame.
        /// </summary>
        /// <remarks>
        /// Both of the things these tests build are found statically - a reserve by its side
        /// and the match by there being one - so a deferred destroy would leave the next test
        /// counting a previous test's wrecks into a match somebody had already won.
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
        /// The rule in one line: a vehicle destroyed is a vehicle that side no longer has.
        /// </summary>
        [UnityTest]
        public IEnumerator AWreckCostsItsOwnSideOneOfThatVehicle()
        {
            TeamReserve green = CreateReserve(Team.Green, 2, 1);
            TeamReserve brown = CreateReserve(Team.Brown, 2, 1);
            VehicleController jeep = CreateVehicle(VehicleKind.Jeep, Team.Green);
            yield return null;

            Assert.That(green.Remaining(VehicleKind.Jeep), Is.EqualTo(2), "the jeeps were never stocked");

            jeep.GetComponent<VehicleHealth>().TakeDamage(999.0f, Team.Brown);
            yield return null;

            Assert.That(green.Remaining(VehicleKind.Jeep), Is.EqualTo(1), "the wreck cost nothing");
            Assert.That(
                green.Remaining(VehicleKind.Tank),
                Is.EqualTo(1),
                "losing a jeep took a tank with it");
            Assert.That(
                brown.Remaining(VehicleKind.Jeep),
                Is.EqualTo(2),
                "the side that did the shooting paid for it");
        }

        /// <summary>
        /// Driving home and swapping costs nothing, which is what makes the drive worth
        /// making at all.
        /// </summary>
        /// <remarks>
        /// The pair to it is below: scuttling is a destroyed vehicle and is charged as one.
        /// Those two together are the whole economy of deciding whether to drive home.
        /// </remarks>
        [UnityTest]
        public IEnumerator ParkingAVehicleInItsOwnBunkerCostsNothing()
        {
            TeamReserve green = CreateReserve(Team.Green, 2, 1);
            VehicleController jeep = CreateVehicle(VehicleKind.Jeep, Team.Green);
            yield return null;

            jeep.GetComponent<VehicleBay>().Stow();
            yield return null;

            Assert.That(green.Remaining(VehicleKind.Jeep), Is.EqualTo(2), "parking cost a jeep");
        }

        /// <summary>
        /// Scuttling a stranded vehicle costs exactly what dying in it costs.
        /// </summary>
        [UnityTest]
        public IEnumerator ScuttlingAVehicleCostsOneJustLikeBeingShot()
        {
            TeamReserve green = CreateReserve(Team.Green, 2, 1);
            VehicleController tank = CreateVehicle(VehicleKind.Tank, Team.Green);
            yield return null;

            Assert.That(tank.GetComponent<VehicleHealth>().SelfDestruct(), Is.True);
            yield return null;

            Assert.That(green.Remaining(VehicleKind.Tank), Is.Zero, "blowing it up was free");
        }

        /// <summary>
        /// A vehicle a side has run out of stays on the roster panel and can never leave the
        /// bunker again - while the rest of the roster carries on as normal.
        /// </summary>
        /// <remarks>
        /// The wait is the point of the second half: a wreck is unavailable for four seconds
        /// whether or not it was the last one, so a test that asked immediately would pass
        /// on the repairs and prove nothing about the reserve.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheLastOfAVehicleLeavesItsRowStuckInTheBunker()
        {
            CreateReserve(Team.Green, 2, 1);
            CreateBunker(Team.Green);
            PlayerVehicleDriver player = CreatePlayer(Team.Green);
            yield return null;

            Assert.That(player.TakeTheField(1), Is.True, "the only tank would not come out");
            player.ActiveVehicle.GetComponent<VehicleHealth>().TakeDamage(999.0f, Team.Brown);
            yield return Wait(Repairs + 0.3f);

            player.Select(1);
            Assert.That(player.RemainingOf(1), Is.Zero, "the tank was not counted");
            Assert.That(player.HasOneLeft(1), Is.False);
            Assert.That(
                player.CanDeploy,
                Is.False,
                "a side with no tanks left was offered another one");
            Assert.That(player.DeploySelected(), Is.False, "a tank came out of an empty bay");
            Assert.That(player.TakeTheField(1), Is.False, "a tank came out the other way");

            player.Select(0);
            Assert.That(player.RemainingOf(0), Is.EqualTo(2), "the jeeps were spent too");
            Assert.That(player.CanDeploy, Is.True, "running out of tanks stopped the jeeps");
        }

        /// <summary>
        /// Losing the last jeep loses the match, because there is no longer anything on that
        /// side that could carry a flag home.
        /// </summary>
        [UnityTest]
        public IEnumerator LosingTheLastJeepHandsTheMatchToTheOtherSide()
        {
            Match match = CreateMatch();
            CreateReserve(Team.Green, 1, 1);
            CreateReserve(Team.Brown, 1, 1);
            VehicleController jeep = CreateVehicle(VehicleKind.Jeep, Team.Green);
            yield return null;

            Assert.That(match.IsOver, Is.False, "the match was over before anybody lost anything");

            jeep.GetComponent<VehicleHealth>().TakeDamage(999.0f, Team.Brown);
            yield return null;

            Assert.That(match.IsOver, Is.True, "the last jeep was lost and nothing happened");
            Assert.That(match.Winner, Is.EqualTo(Team.Brown), "the wrong side won");
            Assert.That(match.Beaten, Is.EqualTo(Team.Green));
            Assert.That(match.Outcome, Is.EqualTo(MatchOutcome.OutOfJeeps));
        }

        /// <summary>
        /// Running out of anything else is a bad afternoon, not a defeat.
        /// </summary>
        [UnityTest]
        public IEnumerator LosingEveryTankIsNotLosingTheMatch()
        {
            Match match = CreateMatch();
            CreateReserve(Team.Green, 3, 1);
            VehicleController tank = CreateVehicle(VehicleKind.Tank, Team.Green);
            yield return null;

            tank.GetComponent<VehicleHealth>().TakeDamage(999.0f, Team.Brown);
            yield return null;

            Assert.That(
                match.IsOver,
                Is.False,
                "a side with three jeeps left was told it had lost");
        }

        /// <summary>
        /// A scene with no reserve in it has no limit, which is what every other test in this
        /// project is standing on.
        /// </summary>
        /// <remarks>
        /// It reads as a large number rather than as zero deliberately: everything that asks
        /// how many are left compares, and a missing reserve that answered none would empty
        /// every bunker in the game the moment a scene was built without one.
        /// </remarks>
        [UnityTest]
        public IEnumerator ASceneWithNoReserveDoesNotCountAnything()
        {
            CreateBunker(Team.Green);
            PlayerVehicleDriver player = CreatePlayer(Team.Green);
            yield return null;

            // Also the tripwire for the whole class: a reserve here means some earlier test
            // left a map loaded, and every count below would be that map's rather than this
            // scene's - see MainMenuTests.LeaveNothingBehind.
            Assert.That(
                TeamReserve.For(Team.Green),
                Is.Null,
                "a scene left over from an earlier test still has a reserve in it");
            Assert.That(player.RemainingOf(0), Is.EqualTo(int.MaxValue));
            Assert.That(player.HasOneLeft(0), Is.True);

            Assert.That(player.TakeTheField(0), Is.True, "the jeep would not come out");
            player.ActiveVehicle.GetComponent<VehicleHealth>().TakeDamage(999.0f, Team.Brown);
            yield return Wait(Repairs + 0.3f);

            player.Select(0);
            Assert.That(player.CanDeploy, Is.True, "an unlimited jeep ran out");
        }

        private static IEnumerator Wait(float seconds)
        {
            float left = seconds;
            while (left > 0.0f)
            {
                left -= Time.deltaTime;
                yield return null;
            }
        }

        /// <summary>
        /// Builds one side's reserve, stocked with jeeps and tanks and nothing else.
        /// </summary>
        /// <param name="side">Side it belongs to.</param>
        /// <param name="jeeps">How many jeeps that side gets.</param>
        /// <param name="tanks">How many tanks that side gets.</param>
        /// <returns>The reserve, already awake and registered.</returns>
        /// <remarks>
        /// Two vehicles rather than four, because every rule being checked here is either
        /// about the one that can carry a flag or about one of the three that cannot, and a
        /// rig that stocked all four would say which is which four times over.
        /// </remarks>
        private TeamReserve CreateReserve(Team side, int jeeps, int tanks)
        {
            var host = new GameObject($"Reserve ({side})");
            host.SetActive(false);
            spawned.Add(host);

            TeamReserve reserve = host.AddComponent<TeamReserve>();
            reserve.Configure(side);
            reserve.Give(VehicleKind.Jeep, jeeps);
            reserve.Give(VehicleKind.Tank, tanks);

            host.SetActive(true);
            return reserve;
        }

        private Match CreateMatch()
        {
            var host = new GameObject("Match");
            spawned.Add(host);
            return host.AddComponent<Match>();
        }

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

        /// <summary>
        /// Builds one player with a full roster of four, all of them in the bunker.
        /// </summary>
        /// <param name="side">Side they play.</param>
        /// <returns>The driver, already awake.</returns>
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
    }
}
