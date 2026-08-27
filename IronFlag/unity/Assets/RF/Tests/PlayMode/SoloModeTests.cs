using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using IronFlag.Core;
using IronFlag.Levels;
using IronFlag.Objective;
using IronFlag.Players;
using IronFlag.Vehicles;

namespace IronFlag.Tests.PlayMode
{
    /// <summary>
    /// Plays the shipped one-player map and checks that the second seat is empty and the
    /// objective is still the one a match is won on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scene is the real <c>Sandbox.unity</c>, which is built with two seats in it,
    /// because that is the whole thing worth testing: one-player mode is not a second scene
    /// or a second game loop, it is <see cref="SessionSeating"/> emptying the seat the map
    /// has no side for on the first frame. A test that assembled its own single-seat session
    /// would be testing an arrangement the game never produces.
    /// </para>
    /// <para>
    /// The map is asked for through <see cref="LevelHandoff"/>, exactly as the menu asks for
    /// it, rather than by rebuilding the scene around it.
    /// </para>
    /// </remarks>
    public sealed class SoloModeTests
    {
        /// <summary>Name of the scene these tests play, as the scene manager knows it.</summary>
        private const string SandboxSceneName = "Sandbox";

        /// <summary>The shipped one-player map.</summary>
        private const string SoloLevel = "iron-watch";

        /// <summary>
        /// Takes the sandbox back out of the world, and forgets the map that was asked for.
        /// </summary>
        /// <returns>The coroutine the framework steps through.</returns>
        /// <remarks>
        /// The same unload <see cref="SplitScreenTests"/> does, and for the same reason - a
        /// scene loaded <c>Single</c> and left standing is the world every later test class
        /// runs in. The handoff is cleared as well, because it is static and would otherwise
        /// send the next test that loads a scene to this map.
        /// </remarks>
        [UnityTearDown]
        public IEnumerator LeaveNoSandboxBehind()
        {
            LevelHandoff.Clear();

            Scene sandbox = SceneManager.GetSceneByName(SandboxSceneName);
            if (!sandbox.IsValid() || !sandbox.isLoaded)
            {
                yield break;
            }

            SceneManager.SetActiveScene(SceneManager.CreateScene("AfterSoloMode"));
            yield return SceneManager.UnloadSceneAsync(sandbox);
        }

        /// <summary>
        /// Loads the sandbox on the shipped one-player map and lets it settle.
        /// </summary>
        /// <returns>The coroutine the test steps through.</returns>
        private IEnumerator StartTheSoloMap()
        {
            LevelHandoff.Play(SoloLevel);
            SceneManager.LoadScene(SandboxSceneName, LoadSceneMode.Single);
            yield return null;
            yield return new WaitForFixedUpdate();
        }

        /// <summary>
        /// Returns the players still seated, in seat order.
        /// </summary>
        private static IReadOnlyList<PlayerVehicleDriver> Seated()
        {
            LocalMultiplayer session = Object.FindAnyObjectByType<LocalMultiplayer>();
            Assert.That(session, Is.Not.Null, "the sandbox has no session in it");
            return session.Players;
        }

        /// <summary>
        /// The shipped map is a one-player map, which every other test here assumes.
        /// </summary>
        [UnityTest]
        public IEnumerator TheShippedSoloMapIsPlayableAndSolo()
        {
            yield return StartTheSoloMap();

            LevelDefinition level = LevelLoader.Current;
            Assert.That(level, Is.Not.Null, $"{SoloLevel} did not load");
            Assert.That(level.IsSolo, Is.True, $"{SoloLevel} is not a one-player map");

            var problems = LevelValidation.Problems(level);
            Assert.That(problems, Is.Empty, string.Join("; ", problems));
        }

        /// <summary>
        /// One seat, and it belongs to the side the map plays.
        /// </summary>
        [UnityTest]
        public IEnumerator OnlyThePlayedSideIsSeated()
        {
            yield return StartTheSoloMap();

            IReadOnlyList<PlayerVehicleDriver> seated = Seated();
            Assert.That(seated.Count, Is.EqualTo(1), "the empty side was seated anyway");
            Assert.That(seated[0].Team, Is.EqualTo(Team.Green));
        }

        /// <summary>
        /// The player who is left gets the whole screen rather than the top half they were
        /// built with.
        /// </summary>
        [UnityTest]
        public IEnumerator TheOnePlayerHasTheWholeScreen()
        {
            yield return StartTheSoloMap();

            Camera view = Seated()[0].CameraRig.View;
            Assert.That(view.rect, Is.EqualTo(SplitScreenLayout.FullScreen));
        }

        /// <summary>
        /// Nothing of the empty seat is left running: no roster, no camera, no HUD.
        /// </summary>
        /// <remarks>
        /// Vehicles are what this is really about. Four enemy vehicles left parked on a map
        /// with no bunker under them would be four things the player can shoot, four things
        /// a turret can decide to defend, and a roster nobody is driving.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheEmptySeatLeavesNothingBehind()
        {
            yield return StartTheSoloMap();
            yield return null;

            foreach (VehicleController vehicle in Object.FindObjectsByType<VehicleController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var paint = vehicle.GetComponent<VehicleTeamPaint>();
                Assert.That(
                    paint == null ? Team.None : paint.Team,
                    Is.Not.EqualTo(Team.Brown),
                    $"{vehicle.name} belongs to a side nobody is playing");
            }

            Assert.That(
                Object.FindObjectsByType<PlayerVehicleDriver>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                Is.EqualTo(1),
                "the empty seat's driver is still in the scene");
        }

        /// <summary>
        /// The objective is the one a match already has: an enemy flag to find and take, and
        /// none of the player's own to lose.
        /// </summary>
        /// <remarks>
        /// This is the claim one-player mode rests on - that it is the existing capture loop
        /// with a human missing from one side rather than a new win condition - so it is
        /// worth asserting rather than assuming.
        /// </remarks>
        [UnityTest]
        public IEnumerator ThereIsAnEnemyFlagToTakeAndNoneToDefend()
        {
            yield return StartTheSoloMap();

            Assert.That(Flag.EnemyOf(Team.Green), Is.Not.Null, "there is no flag to go and get");
            Assert.That(Flag.Of(Team.Green), Is.Null, "the solo player has a flag to lose");
            Assert.That(Match.IsFinished, Is.False, "the match was over before it started");
        }

        /// <summary>
        /// The whole mode, end to end: break the towers open, take the flag that was behind
        /// one of them, drive it home, win.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every step of this is machinery a two-player match already had - the tower, the
        /// decoy, the pickup, the capture, the result - so what is being checked is not that
        /// they work but that they still work with one seat and half the map's sides
        /// missing. That is the claim one-player mode is built on, and it is the one thing
        /// that could not be checked before the mode was playable.
        /// </para>
        /// <para>
        /// The jeep is put where it needs to be rather than driven there, which is what
        /// <see cref="FlagTests"/> does for the same reason: the driving is somebody else's
        /// test, and a test that drove across a generated map would be measuring the route.
        /// </para>
        /// </remarks>
        [UnityTest]
        public IEnumerator BreakingTheRightTowerAndDrivingItHomeWinsTheMatch()
        {
            yield return StartTheSoloMap();

            foreach (FlagTower tower in Object.FindObjectsByType<FlagTower>(FindObjectsSortMode.None))
            {
                tower.Open();
            }

            yield return null;

            Flag flag = Flag.EnemyOf(Team.Green);
            Assert.That(flag.IsVisible, Is.True, "no broken tower gave up the flag");

            PlayerVehicleDriver player = Seated()[0];
            Assert.That(player.TakeTheField(0), Is.True, "the solo player could not deploy a jeep");
            yield return null;

            VehicleController jeep = player.ActiveVehicle;
            Assert.That(jeep, Is.Not.Null, "the solo player is not in a vehicle");

            jeep.transform.position = flag.transform.position;
            yield return null;
            yield return null;

            Assert.That(flag.State, Is.EqualTo(FlagState.Carried), "the jeep did not pick the flag up");
            Assert.That(Match.IsFinished, Is.False, "the match was won at the tower");

            jeep.transform.position = TeamBunker.For(Team.Green).transform.position;
            yield return null;
            yield return null;

            Assert.That(flag.State, Is.EqualTo(FlagState.Captured), "the flag was not delivered");
            Assert.That(Match.Current.Winner, Is.EqualTo(Team.Green), "the solo player did not win");
            Assert.That(Match.Current.Outcome, Is.EqualTo(MatchOutcome.FlagCaptured));
        }
    }
}
