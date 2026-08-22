using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Destruction;
using IronFlag.Objective;
using IronFlag.Supply;
using IronFlag.Vehicles;

namespace IronFlag.Tests.PlayMode
{
    /// <summary>
    /// Winning a match: finding a flag, taking it in the one vehicle allowed to, losing it
    /// again, and getting it home.
    /// </summary>
    /// <remarks>
    /// The numbers and the rulebook are settled in edit mode. What is left for here is
    /// everything that only exists once vehicles are driving around: a tower that reveals
    /// itself because something came close, a flag that rides a hull it is not parented to,
    /// a drop that happens because the carrier stopped existing, and a capture that happens
    /// because a jeep arrived somewhere. All of it is the design document's core loop, which
    /// is the one thing in this project that cannot be checked by reading a table.
    /// </remarks>
    public sealed class FlagTests
    {
        /// <summary>Metres from the middle of the map to each side's bunker in these tests.</summary>
        private const float BunkerLine = 40.0f;

        private readonly List<GameObject> spawned = new List<GameObject>();

        /// <summary>
        /// Clears the map between tests, immediately rather than at the end of the frame.
        /// </summary>
        /// <remarks>
        /// Deferred destruction would leave a won <see cref="Match"/> and both flags
        /// registered while the next test starts, and <see cref="Match.IsFinished"/> is a
        /// static that every flag in the scene reads. A test that inherited a finished match
        /// would sit there doing nothing and blame the flag.
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
        /// Rule one: an intact tower looks the same whether it holds the flag or not, from
        /// anywhere, for as long as you like.
        /// </summary>
        /// <remarks>
        /// The whole decoy rests on this. Parking on top of a pristine tower is the hardest
        /// possible test of it - there is no closer anybody can get - and it still has to
        /// answer nothing.
        /// </remarks>
        [UnityTest]
        public IEnumerator AnIntactTowerGivesNothingAwayHoweverCloseYouGet()
        {
            FlagTower tower = CreateTower(Team.Green, true, Vector3.zero);
            Flag flag = CreateFlag(Team.Green, tower);
            CreateVehicle(VehicleKind.Tank, Team.Brown, Away(1.0f));
            yield return null;
            yield return null;

            Assert.That(tower.IsOpen, Is.False, "an intact tower reported itself open");
            Assert.That(flag.IsVisible, Is.False, "driving up to a pristine tower showed the flag");
        }

        /// <summary>
        /// Rule two: breaking a tower open shows what it was holding.
        /// </summary>
        [UnityTest]
        public IEnumerator BreakingATowerOpenShowsWhatItHolds()
        {
            FlagTower tower = CreateTower(Team.Green, true, Vector3.zero);
            Flag flag = CreateFlag(Team.Green, tower);
            yield return null;

            Assert.That(flag.IsVisible, Is.False, "the flag was on show before anybody shot it");
            Assert.That(tower.Open(), Is.True, "the tower would not break open");
            yield return null;

            Assert.That(tower.IsOpen, Is.True, "a shot tower is still sealed");
            Assert.That(flag.IsVisible, Is.True, "breaking the tower open revealed nothing");
        }

        /// <summary>
        /// Rule three: a jeep cannot take a flag out of a tower nobody has opened, which is
        /// what makes the tank sortie part of the raid rather than a nicety.
        /// </summary>
        [UnityTest]
        public IEnumerator AJeepCannotTakeAFlagFromAnIntactTower()
        {
            FlagTower tower = CreateTower(Team.Green, true, Vector3.zero);
            Flag flag = CreateFlag(Team.Green, tower);
            VehicleController jeep = CreateVehicle(VehicleKind.Jeep, Team.Brown, Away(1.0f));
            yield return null;
            yield return null;

            Assert.That(flag.State, Is.EqualTo(FlagState.AtTower), "a sealed tower was robbed");
            Assert.That(
                flag.GiveTo(jeep),
                Is.False,
                "the flag was handed over by a tower nobody had opened");

            Assert.That(tower.Open(), Is.True);
            yield return null;

            Assert.That(
                flag.State,
                Is.EqualTo(FlagState.Carried),
                "the jeep was standing on an opened tower and did not take the flag");
            Assert.That(flag.Carrier, Is.EqualTo(jeep));
        }

        /// <summary>
        /// A tower stays open. Nothing repairs one, so the second raid down the same lane
        /// costs a drive rather than a sortie.
        /// </summary>
        [UnityTest]
        public IEnumerator ATowerStaysOpenOnceItIsBroken()
        {
            FlagTower tower = CreateTower(Team.Green, true, Vector3.zero);
            Flag flag = CreateFlag(Team.Green, tower);

            int announcements = 0;
            tower.Opened += _ => announcements++;

            Assert.That(tower.Open(), Is.True);
            yield return null;
            yield return null;

            Assert.That(tower.Open(), Is.False, "an open tower opened a second time");
            Assert.That(tower.IsOpen, Is.True, "the tower sealed itself back up");
            Assert.That(flag.IsVisible, Is.True, "the flag went back into hiding");
            Assert.That(announcements, Is.EqualTo(1), "the tower announced itself twice");
        }

        /// <summary>
        /// The design document's first pillar, with a real vehicle standing on a real flag.
        /// </summary>
        [UnityTest]
        public IEnumerator AJeepTakesTheEnemyFlagAndCarriesItOnItsMast()
        {
            FlagTower tower = CreateOpenTower(Team.Green, true, Vector3.zero);
            Flag flag = CreateFlag(Team.Green, tower);
            VehicleController jeep = CreateVehicle(VehicleKind.Jeep, Team.Brown, Away(2.0f));
            yield return null;
            yield return null;

            Assert.That(flag.State, Is.EqualTo(FlagState.Carried), "the jeep drove over it");
            Assert.That(flag.Carrier, Is.EqualTo(jeep));
            Assert.That(flag.CarriedBy, Is.EqualTo(Team.Brown));
            Assert.That(flag.IsVisible, Is.True, "a carried flag is invisible");

            jeep.transform.position = new Vector3(14.0f, 0.0f, -21.0f);
            yield return null;

            Assert.That(
                CombatPlane.DistanceOnMap(flag.transform.position, jeep.transform.position),
                Is.LessThan(0.01f),
                "the flag did not follow the jeep");
            Assert.That(
                flag.transform.position.y,
                Is.EqualTo(jeep.transform.position.y + FlagRules.MastHeight).Within(0.01f),
                "the flag is not on the mast");
            Assert.That(
                flag.transform.parent,
                Is.Not.EqualTo(jeep.transform),
                "the flag was parented to the jeep it is riding");
        }

        /// <summary>
        /// The other three quarters of the roster, driving straight over a flag and leaving
        /// it exactly where it was.
        /// </summary>
        [UnityTest]
        public IEnumerator NothingButAJeepCanPickTheFlagUp()
        {
            FlagTower tower = CreateOpenTower(Team.Green, true, Vector3.zero);
            Flag flag = CreateFlag(Team.Green, tower);

            foreach (VehicleKind kind in new[]
            {
                VehicleKind.Tank, VehicleKind.Asv, VehicleKind.Helicopter,
            })
            {
                VehicleController vehicle = CreateVehicle(kind, Team.Brown, Away(1.0f));
                yield return null;
                yield return null;

                Assert.That(
                    flag.State,
                    Is.EqualTo(FlagState.AtTower),
                    $"the {kind} drove off with the flag");
                Assert.That(flag.GiveTo(vehicle), Is.False, $"the {kind} was handed the flag");

                Object.Destroy(vehicle.gameObject);
                yield return null;
            }
        }

        /// <summary>
        /// You cannot steal your own flag. Picking it up would be a way to carry the
        /// objective out of reach of the side that is meant to be coming for it.
        /// </summary>
        [UnityTest]
        public IEnumerator AJeepCannotPickUpItsOwnSidesFlag()
        {
            FlagTower tower = CreateOpenTower(Team.Green, true, Vector3.zero);
            Flag flag = CreateFlag(Team.Green, tower);
            VehicleController jeep = CreateVehicle(VehicleKind.Jeep, Team.Green, Away(1.0f));
            yield return null;
            yield return null;

            Assert.That(flag.State, Is.EqualTo(FlagState.AtTower), "a side stole its own flag");
            Assert.That(flag.GiveTo(jeep), Is.False, "the flag was handed to its own side");
        }

        /// <summary>
        /// Opening the decoy buys one thing: the knowledge that it was the decoy. The shells
        /// are spent and the real tower is still sealed.
        /// </summary>
        [UnityTest]
        public IEnumerator OpeningTheDecoyBuysNothingButTheAnswer()
        {
            FlagTower decoy = CreateTower(Team.Green, false, Vector3.zero);
            FlagTower real = CreateTower(Team.Green, true, new Vector3(60.0f, 0.0f, 0.0f));
            Flag flag = CreateFlag(Team.Green, real);
            VehicleController jeep = CreateVehicle(VehicleKind.Jeep, Team.Brown, Away(1.0f));

            Assert.That(decoy.Open(), Is.True, "the decoy would not break open");
            yield return null;
            yield return null;

            Assert.That(decoy.HoldsTheFlag, Is.False, "the decoy is real");
            Assert.That(real.IsOpen, Is.False, "breaking the decoy opened the real tower too");
            Assert.That(flag.State, Is.EqualTo(FlagState.AtTower), "the decoy handed over a flag");
            Assert.That(flag.IsVisible, Is.False, "breaking the decoy showed the real flag");
            Assert.That(flag.GiveTo(jeep), Is.False, "a jeep at the decoy was given the flag");
            Assert.That(FlagTower.RealFor(Team.Green), Is.EqualTo(real));
        }

        /// <summary>
        /// The flag comes off a wreck where the wreck was, not where the wreck went.
        /// </summary>
        /// <remarks>
        /// The interesting half is the position. A vehicle that is killed is hidden and
        /// moved back inside its own bunker on the same frame, so a flag that read its
        /// carrier's position after the fact would plant itself in the raider's base - which
        /// is one metre from a capture.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheFlagDropsWhereItsCarrierDiedAndNotWhereTheWreckWent()
        {
            FlagTower tower = CreateOpenTower(Team.Green, true, Vector3.zero);
            Flag flag = CreateFlag(Team.Green, tower);
            VehicleController jeep = CreateVehicle(VehicleKind.Jeep, Team.Brown, Away(2.0f));
            yield return null;
            yield return null;

            Assert.That(flag.State, Is.EqualTo(FlagState.Carried));

            var killedAt = new Vector3(21.0f, 0.0f, -13.0f);
            jeep.transform.position = killedAt;
            yield return null;

            jeep.GetComponent<VehicleHealth>().TakeDamage(1000.0f, Team.Green);
            jeep.transform.position = new Vector3(0.0f, 0.0f, BunkerLine);
            jeep.enabled = false;
            yield return null;

            Assert.That(flag.State, Is.EqualTo(FlagState.Dropped), "the flag went home with the wreck");
            Assert.That(flag.Carrier, Is.Null);
            Assert.That(
                CombatPlane.DistanceOnMap(flag.transform.position, killedAt),
                Is.LessThan(0.01f),
                "the flag was dropped somewhere the jeep never was");
            Assert.That(flag.transform.position.y, Is.EqualTo(0.0f).Within(0.01f), "the flag is airborne");
            Assert.That(flag.ReturnCountdown, Is.GreaterThan(0.0f), "the flag is not counting down");
        }

        /// <summary>
        /// A dropped flag goes home on its own, which is the defender's whole reward for
        /// killing the carrier and holding the ground.
        /// </summary>
        [UnityTest]
        public IEnumerator ADroppedFlagGoesBackToItsTowerOnItsOwn()
        {
            FlagTower tower = CreateOpenTower(Team.Green, true, Vector3.zero);
            Flag flag = CreateFlag(Team.Green, tower);
            VehicleController jeep = CreateVehicle(VehicleKind.Jeep, Team.Brown, Away(2.0f));
            yield return null;
            yield return null;

            jeep.transform.position = new Vector3(30.0f, 0.0f, 0.0f);
            yield return null;

            Object.Destroy(jeep.gameObject);
            yield return null;

            Assert.That(flag.State, Is.EqualTo(FlagState.Dropped));

            yield return Wait(FlagRules.ReturnSeconds + 0.4f);

            Assert.That(flag.State, Is.EqualTo(FlagState.AtTower), "the flag never came home");
            Assert.That(flag.CarriedBy, Is.EqualTo(Team.None), "the flag still belongs to a raid");
            Assert.That(
                (flag.transform.position - tower.FlagPoint).magnitude,
                Is.LessThan(0.01f),
                "the flag came home to the wrong place");
        }

        /// <summary>
        /// The raiding side gets another go at a flag still lying on the ground, which is
        /// what makes the countdown a contest rather than a formality.
        /// </summary>
        [UnityTest]
        public IEnumerator ASecondJeepCanPickUpADroppedFlagBeforeItReturns()
        {
            FlagTower tower = CreateOpenTower(Team.Green, true, Vector3.zero);
            Flag flag = CreateFlag(Team.Green, tower);
            VehicleController first = CreateVehicle(VehicleKind.Jeep, Team.Brown, Away(2.0f));
            yield return null;
            yield return null;

            var lostAt = new Vector3(26.0f, 0.0f, 4.0f);
            first.transform.position = lostAt;
            yield return null;

            Object.Destroy(first.gameObject);
            yield return null;

            Assert.That(flag.State, Is.EqualTo(FlagState.Dropped));

            VehicleController second = CreateVehicle(VehicleKind.Jeep, Team.Brown, lostAt);
            yield return null;
            yield return null;

            Assert.That(flag.State, Is.EqualTo(FlagState.Carried), "the flag refused a second jeep");
            Assert.That(flag.Carrier, Is.EqualTo(second));
            Assert.That(flag.ReturnCountdown, Is.EqualTo(0.0f), "the flag is still counting down");
        }

        /// <summary>
        /// The design document's primary win condition, from the tower to the bunker.
        /// </summary>
        [UnityTest]
        public IEnumerator AJeepThatDrivesTheEnemyFlagHomeWinsTheMatch()
        {
            Match match = CreateMatch();
            CreateBunker(Team.Brown);
            FlagTower tower = CreateOpenTower(Team.Green, true, Vector3.zero);
            Flag flag = CreateFlag(Team.Green, tower);
            VehicleController jeep = CreateVehicle(VehicleKind.Jeep, Team.Brown, Away(2.0f));
            yield return null;
            yield return null;

            Assert.That(flag.State, Is.EqualTo(FlagState.Carried));
            Assert.That(match.IsOver, Is.False, "the match was won at the tower");

            jeep.transform.position = new Vector3(0.0f, 0.0f, BunkerLine);
            yield return null;

            Assert.That(flag.State, Is.EqualTo(FlagState.Captured), "the flag was not delivered");
            Assert.That(match.IsOver, Is.True, "nobody won");
            Assert.That(match.Winner, Is.EqualTo(Team.Brown), "the wrong side won");
            Assert.That(match.FlagTaken, Is.EqualTo(Team.Green));
            Assert.That(Match.IsFinished, Is.True);
        }

        /// <summary>
        /// Arriving at somebody else's bunker with their flag is not a capture, which is the
        /// one way the win condition could be read backwards.
        /// </summary>
        [UnityTest]
        public IEnumerator AFlagIsNotCapturedAtTheBunkerItWasStolenFrom()
        {
            Match match = CreateMatch();
            CreateBunker(Team.Green);
            FlagTower tower = CreateOpenTower(Team.Green, true, Vector3.zero);
            Flag flag = CreateFlag(Team.Green, tower);
            VehicleController jeep = CreateVehicle(VehicleKind.Jeep, Team.Brown, Away(2.0f));
            yield return null;
            yield return null;

            jeep.transform.position = new Vector3(0.0f, 0.0f, -BunkerLine);
            yield return null;
            yield return null;

            Assert.That(flag.State, Is.EqualTo(FlagState.Carried), "the raider captured it at home");
            Assert.That(match.IsOver, Is.False, "the match was won at the wrong bunker");
        }

        /// <summary>
        /// A neutral depot standing where a bunker is must not be able to swallow a capture.
        /// </summary>
        /// <remarks>
        /// The reason <see cref="SupplyPoint.HomeFor"/> exists rather than the win condition
        /// reusing the first point that serves a vehicle: the two are the same answer until
        /// two points overlap, and then the difference is a match that cannot be won.
        /// </remarks>
        [UnityTest]
        public IEnumerator ADepotParkedOnABunkerDoesNotStopACapture()
        {
            Match match = CreateMatch();
            CreateDepot(new Vector3(0.0f, 0.0f, BunkerLine));
            CreateBunker(Team.Brown);
            FlagTower tower = CreateOpenTower(Team.Green, true, Vector3.zero);
            Flag flag = CreateFlag(Team.Green, tower);
            VehicleController jeep = CreateVehicle(VehicleKind.Jeep, Team.Brown, Away(2.0f));
            yield return null;
            yield return null;

            jeep.transform.position = new Vector3(0.0f, 0.0f, BunkerLine);
            yield return null;

            Assert.That(flag.State, Is.EqualTo(FlagState.Captured), "a fuel drum stopped the capture");
            Assert.That(match.Winner, Is.EqualTo(Team.Brown));
        }

        /// <summary>
        /// Nothing moves once somebody has won, including the other flag.
        /// </summary>
        [UnityTest]
        public IEnumerator TheOtherFlagStopsMatteringOnceTheMatchIsOver()
        {
            Match match = CreateMatch();
            CreateBunker(Team.Brown);
            FlagTower theirs = CreateOpenTower(Team.Green, true, Vector3.zero);
            Flag stolen = CreateFlag(Team.Green, theirs);

            FlagTower ours = CreateOpenTower(Team.Brown, true, new Vector3(0.0f, 0.0f, -60.0f));
            Flag defended = CreateFlag(Team.Brown, ours);

            VehicleController jeep = CreateVehicle(VehicleKind.Jeep, Team.Brown, Away(2.0f));
            yield return null;
            yield return null;

            jeep.transform.position = new Vector3(0.0f, 0.0f, BunkerLine);
            yield return null;

            Assert.That(match.IsOver, Is.True);
            Assert.That(stolen.State, Is.EqualTo(FlagState.Captured));

            VehicleController raider = CreateVehicle(
                VehicleKind.Jeep, Team.Green, new Vector3(0.0f, 0.0f, -60.0f));
            yield return null;
            yield return null;

            Assert.That(
                CombatPlane.DistanceOnMap(defended.transform.position, raider.transform.position),
                Is.LessThan(FlagRules.PickupRadius),
                "the raider never got within reach, so this proves nothing");
            Assert.That(
                defended.State,
                Is.EqualTo(FlagState.AtTower),
                "a flag was taken after the match was decided");
        }

        /// <summary>
        /// Builds a flag tower that has already been broken open.
        /// </summary>
        /// <remarks>
        /// What most of these tests want. Only the handful that are about the tower rule
        /// itself start from a sealed one - everything else is about what happens to a flag
        /// once it is in play, and having every one of them shell a pyramid first would bury
        /// the thing being tested.
        /// </remarks>
        /// <param name="side">Side whose base it stands in.</param>
        /// <param name="real">Whether it flies that side's flag.</param>
        /// <param name="at">Where it stands.</param>
        /// <returns>The tower.</returns>
        private FlagTower CreateOpenTower(Team side, bool real, Vector3 at)
        {
            FlagTower tower = CreateTower(side, real, at);
            Assert.That(tower.Open(), Is.True, "the tower would not break open");
            return tower;
        }

        /// <summary>
        /// Builds a tower nobody has shot yet.
        /// </summary>
        /// <param name="side">Side whose base it stands in.</param>
        /// <param name="real">Whether it flies that side's flag.</param>
        /// <param name="at">Where it stands.</param>
        /// <returns>The tower.</returns>
        private FlagTower CreateTower(Team side, bool real, Vector3 at)
        {
            var host = new GameObject($"Tower ({side})");
            host.SetActive(false);
            host.transform.position = at;
            spawned.Add(host);

            // The three state models the real prefab carries, as empty objects: without a
            // damaged one the tower would go straight from standing to rubble, and the
            // damaged state is half of what these tests are about.
            GameObject intact = State(host, Destructible.IntactNodeName);
            GameObject damaged = State(host, Destructible.DamagedNodeName);
            GameObject destroyed = State(host, Destructible.DestroyedNodeName);

            host.AddComponent<Destructible>().Configure(
                StructureKind.FlagTower,
                StructureTuning.For(StructureKind.FlagTower),
                intact,
                damaged,
                destroyed,
                null);

            FlagTower tower = host.AddComponent<FlagTower>();
            tower.Configure(side, real);

            host.SetActive(true);
            return tower;
        }

        /// <summary>
        /// Adds one empty destruction-state node to a tower under construction.
        /// </summary>
        /// <param name="host">The tower.</param>
        /// <param name="name">State node name, from <see cref="Destructible"/>.</param>
        /// <returns>The node.</returns>
        private static GameObject State(GameObject host, string name)
        {
            var node = new GameObject(name);
            node.transform.SetParent(host.transform, false);
            return node;
        }

        /// <summary>
        /// Builds a flag with something to look at, so its visibility can be read.
        /// </summary>
        /// <param name="side">Side the flag belongs to.</param>
        /// <param name="tower">Tower it flies from.</param>
        /// <returns>The flag.</returns>
        private Flag CreateFlag(Team side, FlagTower tower)
        {
            var host = new GameObject($"Flag ({side})");
            host.SetActive(false);
            spawned.Add(host);

            var banner = new GameObject("Banner", typeof(MeshRenderer));
            banner.transform.SetParent(host.transform, false);

            Flag flag = host.AddComponent<Flag>();
            flag.Configure(side, tower);

            host.SetActive(true);
            return flag;
        }

        /// <summary>
        /// Builds a vehicle that can drive, be shot and be seen by a tower.
        /// </summary>
        /// <param name="kind">Which vehicle to build.</param>
        /// <param name="side">Side to paint it.</param>
        /// <param name="at">Where to put it.</param>
        /// <returns>Its controller.</returns>
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

        /// <summary>
        /// Builds a side's bunker: the place a flag has to reach to end a match.
        /// </summary>
        /// <param name="side">Side it belongs to.</param>
        /// <returns>Its supply point, which is what "home" means.</returns>
        private SupplyPoint CreateBunker(Team side)
        {
            var host = new GameObject($"Bunker ({side})");
            host.SetActive(false);
            host.transform.position =
                new Vector3(0.0f, 0.0f, side == Team.Green ? -BunkerLine : BunkerLine);
            spawned.Add(host);

            SupplyPoint point = host.AddComponent<SupplyPoint>();
            point.Configure(side, 12.0f, 0.25f, 0.25f, true);

            host.SetActive(true);
            return point;
        }

        /// <summary>
        /// Builds a neutral depot, which serves everybody and is nobody's home.
        /// </summary>
        /// <param name="at">Where it stands.</param>
        /// <returns>Its supply point.</returns>
        private SupplyPoint CreateDepot(Vector3 at)
        {
            var host = new GameObject("Depot");
            host.SetActive(false);
            host.transform.position = at;
            spawned.Add(host);

            SupplyPoint point = host.AddComponent<SupplyPoint>();
            point.Configure(Team.None, 9.0f, 0.12f, 0.0f, false);

            host.SetActive(true);
            return point;
        }

        /// <summary>
        /// Builds the match these tests are won and lost in.
        /// </summary>
        /// <returns>The match.</returns>
        private Match CreateMatch()
        {
            var host = new GameObject("Match");
            spawned.Add(host);
            return host.AddComponent<Match>();
        }

        /// <summary>Returns a point a given distance up the map from the origin.</summary>
        private static Vector3 Away(float metres) => new Vector3(0.0f, 0.0f, metres);

        private static IEnumerator Wait(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
            {
                yield return null;
            }
        }
    }
}
