using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Players;
using IronFlag.Vehicles;

namespace IronFlag.Tests.PlayMode
{
    /// <summary>
    /// Plays the generated sandbox with two gamepads that do not exist, and checks that two
    /// people can share one screen without sharing each other's controls.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the part of M2 that has to be right, and it is the part that fails quietly:
    /// every way of getting two-player input wrong - one shared actions asset, a missing
    /// binding mask, the same pad paired twice - produces a game that runs and looks fine
    /// until both vehicles move together.
    /// </para>
    /// <para>
    /// The scene is the real <c>Sandbox.unity</c> rather than one assembled here, because
    /// half of what is being checked is what the generator produced: two cameras with a half
    /// of the screen each, two rosters, and one session wired to both.
    /// <see cref="InputTestFixture"/> swaps the whole input system out for an empty one, so
    /// whatever is plugged into the machine running the tests makes no difference and every
    /// device here is virtual.
    /// </para>
    /// </remarks>
    public sealed class SplitScreenTests : InputTestFixture
    {
        /// <summary>Seconds of driving a test gives the players.</summary>
        private const float DrivingTime = 1.5f;

        /// <summary>Name of the scene these tests play, as the scene manager knows it.</summary>
        private const string SandboxSceneName = "Sandbox";

        private Gamepad first;
        private Gamepad second;
        private List<PlayerVehicleDriver> players;

        /// <summary>
        /// Empties the input system and plugs two pads into it.
        /// </summary>
        /// <remarks>
        /// With no keyboard or mouse in the fixture's input system, two pads is the
        /// dual-gamepad case - one player per pad - which is what makes "did the
        /// <em>other</em> vehicle move" a question worth asking.
        /// </remarks>
        public override void Setup()
        {
            base.Setup();
            first = InputSystem.AddDevice<Gamepad>();
            second = InputSystem.AddDevice<Gamepad>();
        }

        /// <summary>
        /// Takes the sandbox back out of the world when a test has finished with it.
        /// </summary>
        /// <returns>The coroutine the framework steps through.</returns>
        /// <remarks>
        /// <para>
        /// These are the only tests in the project that load a scene, and loading one
        /// <c>Single</c> replaces the world for everybody who comes after: without this the
        /// last test here leaves the whole sandbox - its bunkers, its depots, its flags and
        /// eight vehicles - running underneath every later test class. That is not
        /// hypothetical. A tank parked at thirty metres by
        /// <see cref="SupplyTests"/> lands six metres from the sandbox fuel depot, is
        /// refuelled as fast as it burns, and the test that says driving costs more than
        /// idling fails somewhere else entirely.
        /// </para>
        /// <para>
        /// An empty scene has to be made and made active first, because Unity will not
        /// unload the last scene standing.
        /// </para>
        /// </remarks>
        [UnityTearDown]
        public IEnumerator LeaveNoSandboxBehind()
        {
            Scene sandbox = SceneManager.GetSceneByName(SandboxSceneName);
            if (!sandbox.IsValid() || !sandbox.isLoaded)
            {
                yield break;
            }

            SceneManager.SetActiveScene(SceneManager.CreateScene("AfterSplitScreen"));
            yield return SceneManager.UnloadSceneAsync(sandbox);
        }

        /// <summary>
        /// Loads the sandbox, lets it settle, and finds the two players in it.
        /// </summary>        /// <summary>
        /// Loads the sandbox, lets it settle, and finds the two players in it.
        /// </summary>
        /// <returns>The coroutine the test steps through.</returns>
        /// <remarks>
        /// Every test starts by yielding on this rather than by a <c>[UnitySetUp]</c>,
        /// because the test framework runs <c>[UnitySetUp]</c> <em>before</em>
        /// <c>[SetUp]</c> - so a scene loaded there would wake up and deal its devices out
        /// before <see cref="InputTestFixture"/> has plugged any in, and every player would
        /// come out with nothing to hold.
        /// </remarks>
        private IEnumerator StartTheGame()
        {
            yield return OpenTheBunkers();

            foreach (PlayerVehicleDriver player in players)
            {
                Assert.That(player.TakeTheField(0), Is.True, $"{player.name} could not deploy");
            }

            // Long enough for a vehicle that has just been set down on the lift to have
            // finished dropping the last few centimetres onto the ground. A test that
            // measures how far something moved has to start after the settle, or the settle
            // is what it measures.
            yield return Wait(0.5f);

            foreach (PlayerVehicleDriver player in players)
            {
                Assert.That(player.ActiveVehicle, Is.Not.Null, $"{player.name} is not in a vehicle");
            }
        }

        /// <summary>
        /// Loads the sandbox and stops where a match really starts: both players inside
        /// their bunkers, choosing.
        /// </summary>
        /// <returns>The coroutine the test steps through.</returns>
        private IEnumerator OpenTheBunkers()
        {
            SceneManager.LoadScene(SandboxSceneName, LoadSceneMode.Single);
            yield return null;
            yield return new WaitForFixedUpdate();
            players = Players();
        }

        [UnityTest]
        public IEnumerator TheTwoPlayersHoldDifferentControls()
        {
            yield return StartTheGame();
            yield return null;

            Assert.That(
                players[0].Controls,
                Is.Not.SameAs(players[1].Controls),
                "both players are reading one set of controls");
            Assert.That(players[0].Controls.Scheme, Is.EqualTo(ControlScheme.Gamepad));
            Assert.That(players[1].Controls.Scheme, Is.EqualTo(ControlScheme.Gamepad));
        }

        [UnityTest]
        public IEnumerator EachPadDrivesOnlyItsOwnPlayersVehicle()
        {
            yield return StartTheGame();
            VehicleController mine = players[0].ActiveVehicle;
            VehicleController theirs = players[1].ActiveVehicle;
            Vector3 myStart = mine.transform.position;
            Vector3 theirStart = theirs.transform.position;

            Hold(first, Driving(Vector2.up));
            yield return Wait(DrivingTime);

            Assert.That(Travelled(mine, myStart), Is.GreaterThan(10.0f), "player one's vehicle sat still");
            Assert.That(
                Travelled(theirs, theirStart),
                Is.LessThan(0.5f),
                "player two's vehicle drove off on somebody else's stick");
        }

        [UnityTest]
        public IEnumerator BothPlayersCanDriveAtTheSameTime()
        {
            yield return StartTheGame();
            VehicleController mine = players[0].ActiveVehicle;
            VehicleController theirs = players[1].ActiveVehicle;
            Vector3 myStart = mine.transform.position;
            Vector3 theirStart = theirs.transform.position;

            Hold(first, Driving(Vector2.up));
            Hold(second, Driving(Vector2.up));
            yield return Wait(DrivingTime);

            Assert.That(Travelled(mine, myStart), Is.GreaterThan(10.0f), "player one stayed put");
            Assert.That(Travelled(theirs, theirStart), Is.GreaterThan(10.0f), "player two stayed put");

            // The two sides start facing each other, so driving forwards closes the gap.
            Assert.That(
                Vector3.Distance(mine.transform.position, theirs.transform.position),
                Is.LessThan(Vector3.Distance(myStart, theirStart)),
                "one of them is driving backwards");
        }

        /// <summary>
        /// The bunker screen goes through the same routing the field does, so it can be
        /// wrong in the same way: one shoulder button moving both players down their
        /// rosters is exactly what a shared actions asset produces.
        /// </summary>
        [UnityTest]
        public IEnumerator TheShoulderButtonMovesOnlyThatPlayersSelection()
        {
            yield return OpenTheBunkers();
            int theirs = players[1].Selected;

            Hold(first, Neutral.WithButton(GamepadButton.RightShoulder));
            yield return null;
            Hold(first, Neutral);
            yield return null;

            Assert.That(players[0].Selected, Is.EqualTo(1), "player one did not move down the roster");
            Assert.That(players[1].Selected, Is.EqualTo(theirs), "player two moved as well");
        }

        /// <summary>
        /// The whole of the bunker path, walked once with a pad that does not exist: choose
        /// with a shoulder button, send it out with the deploy button, and end up driving
        /// the vehicle that was highlighted. Every other bunker test calls the driver.
        /// </summary>
        [UnityTest]
        public IEnumerator TheDeployButtonSendsOutTheHighlightedVehicleForItsOwnPlayerOnly()
        {
            yield return OpenTheBunkers();

            Hold(first, Neutral.WithButton(GamepadButton.RightShoulder));
            yield return null;
            Hold(first, Neutral.WithButton(GamepadButton.West));
            yield return null;
            Hold(first, Neutral);

            yield return Wait(2.0f);

            Assert.That(players[0].AtTheBunker, Is.False, "player one never left the bunker");
            Assert.That(
                players[0].ActiveVehicle.Kind,
                Is.EqualTo(VehicleKind.Tank),
                "player one deployed something they had not chosen");
            Assert.That(
                players[1].AtTheBunker, Is.True, "player two was deployed on somebody else's button");
        }

        /// <summary>
        /// Held rather than tapped, and only for the player holding it. A button that took
        /// somebody off the field on a single press would eventually do it by accident, and
        /// one that took both players off would do it every time.
        /// </summary>
        [UnityTest]
        public IEnumerator HoldingDeployTakesOnlyThatPlayerOffTheField()
        {
            yield return StartTheGame();
            VehicleController theirs = players[1].ActiveVehicle;

            Hold(first, Neutral.WithButton(GamepadButton.West));
            yield return null;

            Assert.That(players[0].AtTheBunker, Is.False, "one frame of the button was enough");

            yield return Wait(players[0].RecallHoldSeconds + 0.4f);

            Assert.That(players[0].AtTheBunker, Is.True, "holding it did nothing");
            Assert.That(players[1].ActiveVehicle, Is.SameAs(theirs), "player two left the field too");
        }

        [UnityTest]
        public IEnumerator ClimbingAndDescendingAreOneAxis()
        {
            yield return StartTheGame();
            PlayerControls controls = players[0].Controls;

            Assert.That(controls.Lift, Is.EqualTo(0.0f), "it is climbing on its own");

            Hold(first, Neutral.WithButton(GamepadButton.South));
            yield return null;
            Assert.That(controls.Lift, Is.GreaterThan(0.9f), "it did not climb");

            Hold(first, Neutral.WithButton(GamepadButton.East));
            yield return null;
            Assert.That(controls.Lift, Is.LessThan(-0.9f), "it did not descend");
        }

        [UnityTest]
        public IEnumerator AimingOnAGamepadIsAStickAndNotAPointer()
        {
            yield return StartTheGame();
            PlayerControls controls = players[0].Controls;

            Hold(first, new GamepadState { rightStick = Vector2.up });
            yield return null;

            Assert.That(controls.Aim.y, Is.GreaterThan(0.9f), "the stick did not aim");
            Assert.That(controls.Aim.magnitude, Is.LessThanOrEqualTo(1.0f), "a stick is not in pixels");
        }

        /// <summary>
        /// One button both sends a vehicle out and takes one off the field, and the ride out
        /// of the bunker lasts longer than the hold does. A pilot who keeps their thumb down
        /// through the ride must not be handed a vehicle that immediately blows itself up.
        /// </summary>
        [UnityTest]
        public IEnumerator HoldingDeployThroughTheRideOutDoesNotScuttleWhatItChose()
        {
            yield return OpenTheBunkers();

            Hold(first, Neutral.WithButton(GamepadButton.West));
            yield return Wait(players[0].RecallHoldSeconds + 2.0f);

            Assert.That(
                players[0].AtTheBunker,
                Is.False,
                "the vehicle it deployed was scuttled by the same press that deployed it");
            Assert.That(players[0].CanLeaveTheField, Is.False, "the button counts as let go of");

            Hold(first, Neutral);
            yield return null;
            Hold(first, Neutral.WithButton(GamepadButton.West));
            yield return Wait(players[0].RecallHoldSeconds + 0.4f);

            Assert.That(
                players[0].AtTheBunker, Is.True, "letting go and holding again did nothing");
        }

        [UnityTest]
        public IEnumerator DrivingDoesNotAlsoTakeThePlayerOffTheField()
        {
            yield return StartTheGame();
            VehicleController before = players[0].ActiveVehicle;

            Hold(first, Driving(new Vector2(1.0f, 1.0f)));
            yield return Wait(1.5f);

            Assert.That(players[0].ActiveVehicle, Is.SameAs(before), "driving changed vehicle");
            Assert.That(players[0].RecallProgress, Is.EqualTo(0.0f), "the stick is holding deploy down");
        }

        [UnityTest]
        public IEnumerator UnpluggingAPadPutsPlayerOneBackAtTheKeyboard()
        {
            yield return StartTheGame();

            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            InputSystem.RemoveDevice(second);
            yield return null;

            Assert.That(
                players[0].Controls.Scheme,
                Is.EqualTo(ControlScheme.KeyboardAndMouse),
                "player one was left holding a pad that is gone");
            Assert.That(
                players[1].Controls.Scheme,
                Is.EqualTo(ControlScheme.Gamepad),
                "player two lost their pad as well");
        }

        [UnityTest]
        public IEnumerator APlayerWithNothingToHoldStopsDriving()
        {
            yield return StartTheGame();
            VehicleController mine = players[0].ActiveVehicle;

            Hold(first, Driving(Vector2.up));
            yield return Wait(1.0f);
            Assert.That(mine.ForwardSpeed, Is.GreaterThan(5.0f), "it never got going");

            InputSystem.RemoveDevice(first);
            InputSystem.RemoveDevice(second);
            yield return Wait(2.0f);

            Assert.That(players[0].Controls, Is.Null, "player one is still holding something");
            Assert.That(mine.ForwardSpeed, Is.EqualTo(0.0f).Within(0.01f), "it drove on unattended");
        }

        [UnityTest]
        public IEnumerator EachPlayerHasTheirOwnHalfOfTheScreen()
        {
            yield return StartTheGame();
            yield return null;

            Assert.That(
                players[0].CameraRig.Viewport,
                Is.EqualTo(SplitScreenLayout.ViewportFor(0, players.Count)),
                "player one is not on top");
            Assert.That(
                players[1].CameraRig.Viewport,
                Is.EqualTo(SplitScreenLayout.ViewportFor(1, players.Count)),
                "player two is not below");
            Assert.That(
                players[0].CameraRig.Target,
                Is.SameAs(players[0].ActiveVehicle),
                "the top view is not following its own vehicle");
            Assert.That(
                players[1].CameraRig.Target,
                Is.SameAs(players[1].ActiveVehicle),
                "the bottom view is not following its own vehicle");
        }

        [UnityTest]
        public IEnumerator TheCamerasFollowTheirOwnPlayerOnly()
        {
            yield return StartTheGame();
            TopDownCameraRig top = players[0].CameraRig;
            TopDownCameraRig bottom = players[1].CameraRig;
            Vector3 bottomStart = bottom.transform.position;

            Hold(first, Driving(Vector2.up));
            yield return Wait(DrivingTime);

            Vector3 toVehicle = players[0].ActiveVehicle.transform.position - top.transform.position;
            Assert.That(
                Vector3.Dot(toVehicle.normalized, top.transform.forward),
                Is.GreaterThan(0.9f),
                "the top view is not looking at the vehicle that moved");
            Assert.That(
                Vector3.Distance(bottom.transform.position, bottomStart),
                Is.LessThan(0.5f),
                "the bottom view chased the other player");
        }

        /// <summary>
        /// Finds the sandbox's players, in seat order, and checks the scene is playable.
        /// </summary>
        /// <returns>The two local players.</returns>
        /// <summary>
        /// The one test that walks the whole firing path: a trigger on a pad that does not
        /// exist, through one player's own copy of the controls, into a vehicle's intent,
        /// out of the barrel of the gun the prefab builder bolted on. Every other combat
        /// test pulls the trigger by calling the gun directly.
        /// </summary>
        [UnityTest]
        public IEnumerator TheTriggerPutsARoundInTheAirForItsOwnPlayerOnly()
        {
            yield return StartTheGame();
            Team mine = Side(players[0]);

            Hold(first, Firing());
            yield return Wait(0.4f);

            List<Projectile> rounds = Rounds();

            Assert.That(rounds.Count, Is.GreaterThan(0), "the trigger fired nothing");
            foreach (Projectile round in rounds)
            {
                Assert.That(
                    round.Team,
                    Is.EqualTo(mine),
                    "somebody else's vehicle fired on this player's trigger");
            }
        }

        [UnityTest]
        public IEnumerator NobodyFiresWithTheTriggerReleased()
        {
            yield return StartTheGame();

            Hold(first, Neutral);
            yield return Wait(0.4f);

            Assert.That(Rounds().Count, Is.Zero, "something fired on its own");
        }

        private static List<Projectile> Rounds()
            => new List<Projectile>(Object.FindObjectsByType<Projectile>());

        private static Team Side(PlayerVehicleDriver player)
        {
            var paint = player.ActiveVehicle.GetComponent<VehicleTeamPaint>();
            return paint == null ? Team.None : paint.Team;
        }

        private static List<PlayerVehicleDriver> Players()
        {
            LocalMultiplayer session = Object.FindAnyObjectByType<LocalMultiplayer>();
            Assert.That(session, Is.Not.Null, "the sandbox has no local multiplayer session");

            var players = new List<PlayerVehicleDriver>(session.Players);
            Assert.That(players.Count, Is.EqualTo(2), "the sandbox does not seat two players");

            foreach (PlayerVehicleDriver player in players)
            {
                Assert.That(player.Controls, Is.Not.Null, $"{player.name} was never given a device");
                Assert.That(player.Roster.Count, Is.EqualTo(4), $"{player.name} is short of vehicles");
                Assert.That(player.CameraRig, Is.Not.Null, $"{player.name} has no camera");
            }

            return players;
        }

        /// <summary>A pad with nothing pressed and both sticks centred.</summary>
        private static GamepadState Neutral => default;

        /// <summary>
        /// Puts a pad's left stick where a player would be holding it.
        /// </summary>
        /// <param name="drive">Steer on X and throttle on Y, each in -1..1.</param>
        /// <returns>A pad state with that stick deflection and nothing else pressed.</returns>
        private static GamepadState Driving(Vector2 drive) => new GamepadState { leftStick = drive };

        /// <summary>
        /// Returns a pad state with the fire trigger held down.
        /// </summary>
        /// <returns>A neutral pad with its right trigger pulled.</returns>
        /// <remarks>
        /// The trigger is an axis rather than a button, so <c>WithButton</c> cannot reach it.
        /// </remarks>
        private static GamepadState Firing()
        {
            GamepadState state = Neutral;
            state.rightTrigger = 1.0f;
            return state;
        }

        /// <summary>
        /// Makes a pad look like this from the next frame on.
        /// </summary>
        /// <param name="pad">Pad to drive.</param>
        /// <param name="state">Everything the pad's controls read from now on.</param>
        /// <remarks>
        /// <para>
        /// A whole pad state at a time, queued for the player loop to pick up, rather than
        /// <see cref="InputTestFixture"/>'s <c>Set</c> and <c>Press</c>. Those build
        /// <em>delta</em> state events, which need the control's current state pointer - and
        /// under the batch-mode test runner that pointer is null, so every one of them throws
        /// before the test gets anywhere. A full state event does not need it.
        /// </para>
        /// <para>
        /// Queued rather than applied, so the player loop's own input update delivers it at
        /// the top of the next frame and the driver's <c>Update</c> sees a button as pressed
        /// <em>this</em> frame. Applying it here would spend the press before anyone looked.
        /// </para>
        /// </remarks>
        private static void Hold(Gamepad pad, GamepadState state)
            => InputSystem.QueueStateEvent(pad, state);

        private static float Travelled(VehicleController vehicle, Vector3 from)
        {
            Vector3 moved = vehicle.transform.position - from;
            return new Vector2(moved.x, moved.z).magnitude;
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
