using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using IronFlag.Core;
using IronFlag.Destruction;
using IronFlag.Vehicles;

namespace IronFlag.Tests.PlayMode
{
    /// <summary>
    /// The one destructible that gets out of the way: who it opens for, who it does not,
    /// and what it does when somebody is standing in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The arithmetic of a gate - how far it notices from, how long the leaf takes, how
    /// much it costs to breach - is settled in edit mode against the built prefab and the
    /// weapon table. What is left for here is the part that only exists once there are
    /// vehicles on the field and time is passing: a gate is a component walking a roll-call
    /// every fixed step and deciding where a collider should be, and only real steps can
    /// say whether it decides right.
    /// </para>
    /// <para>
    /// Built out of cubes rather than the real prefab, like <see cref="TurretTests"/> and
    /// for the same reason: what is under test is the rule, not the art pipeline. That the
    /// shipped prefab has a leaf in the right states, with colliders and a kinematic body,
    /// is <c>StructureRosterTests</c>' business.
    /// </para>
    /// </remarks>
    public sealed class DoorTests
    {
        /// <summary>Metres a gate in these tests notices its own side from.</summary>
        /// <remarks>
        /// <see cref="IronFlag.Editor.Gameplay.DestructiblePrefabBuilder.DoorReach"/>'s
        /// value, written again because that lives in the editor assembly and this does
        /// not. The prefab is checked against the real one in edit mode.
        /// </remarks>
        private const float Reach = 16.0f;

        /// <summary>Metres per second the leaf travels in these tests.</summary>
        private const float LeafSpeed = 3.5f;

        /// <summary>Long enough for a leaf to finish travelling, with room to spare.</summary>
        private const float FullStroke = 1.5f;

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
        /// The whole feature: a vehicle of the gate's own side turns up and the leaf goes
        /// into the floor.
        /// </summary>
        [UnityTest]
        public IEnumerator AGateOpensForItsOwnSide()
        {
            AutoDoor door = CreateDoor(Team.Green, Vector3.zero);
            CreateVehicle(Team.Green, new Vector3(0.0f, 0.0f, 10.0f));
            yield return Wait(FullStroke);

            Assert.That(door.IsOpen, Is.True, "the gate did not open for its own side");
            Assert.That(
                door.Leaf.localPosition.y,
                Is.EqualTo(-door.Travel).Within(0.01f),
                "the gate says it is open but the leaf is still standing there");
        }

        /// <summary>
        /// And the other half of it: to anybody else a gate is a wall, and it does not so
        /// much as flinch.
        /// </summary>
        [UnityTest]
        public IEnumerator AGateIsAWallToTheEnemy()
        {
            AutoDoor door = CreateDoor(Team.Green, Vector3.zero);
            CreateVehicle(Team.Brown, new Vector3(0.0f, 0.0f, 6.0f));
            yield return Wait(FullStroke);

            Assert.That(door.IsOpen, Is.False, "the gate opened for the side it is built against");
            Assert.That(
                door.Openness,
                Is.EqualTo(0.0f),
                "the gate came off its seat for an enemy, so it is a wall with a gap in it");
        }

        /// <summary>
        /// A gate notices its own side at a distance and not across the map, so an owner
        /// halfway to the other bunker is not holding it open behind them.
        /// </summary>
        [UnityTest]
        public IEnumerator AGateIgnoresItsOwnSideOutOfReach()
        {
            AutoDoor door = CreateDoor(Team.Green, Vector3.zero);
            CreateVehicle(Team.Green, new Vector3(0.0f, 0.0f, Reach + 8.0f));
            yield return Wait(FullStroke);

            Assert.That(door.Openness, Is.EqualTo(0.0f), "the gate opens from anywhere at all");
        }

        /// <summary>
        /// It shuts again behind whoever opened it, which is what makes it a defence rather
        /// than a hole somebody unlocked once.
        /// </summary>
        [UnityTest]
        public IEnumerator AGateShutsOnceItsOwnerHasGone()
        {
            AutoDoor door = CreateDoor(Team.Green, Vector3.zero);
            VehicleController owner = CreateVehicle(Team.Green, new Vector3(0.0f, 0.0f, 10.0f));
            yield return Wait(FullStroke);
            Assert.That(door.IsOpen, Is.True, "it never opened, so shutting proves nothing");

            owner.transform.position = new Vector3(0.0f, 0.0f, Reach + 20.0f);
            yield return Wait(FullStroke);

            Assert.That(door.Openness, Is.EqualTo(0.0f), "the gate stayed open after its owner left");
            Assert.That(
                door.Leaf.localPosition.y,
                Is.EqualTo(0.0f).Within(0.01f),
                "the gate says it is shut but the leaf is still down");
        }

        /// <summary>
        /// A gate may not close on a vehicle - and because it may not, an enemy who gets
        /// into the gateway holds it open.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Both halves of one rule, and the first is the reason for it: the leaf is a
        /// collider that moves, and driving it up through a vehicle standing on it would
        /// throw that vehicle into the air. The tactic is what falls out - at the price of
        /// parking in a doorway, in the open, doing nothing else.
        /// </para>
        /// <para>
        /// The intruder has to arrive <em>after</em> the gate is off its seat, which is the
        /// other half of the rule and is checked by
        /// <see cref="AnEnemyLeaningOnAShutGateDoesNotPinItOpen"/>.
        /// </para>
        /// </remarks>
        [UnityTest]
        public IEnumerator AGateWillNotShutOnAVehicleStandingInIt()
        {
            AutoDoor door = CreateDoor(Team.Green, Vector3.zero);
            VehicleController owner = CreateVehicle(Team.Green, new Vector3(0.0f, 0.0f, 10.0f));
            yield return Wait(FullStroke);
            Assert.That(door.IsOpen, Is.True, "it never opened, so nothing can drive into it");

            CreateVehicle(Team.Brown, Vector3.zero);
            owner.transform.position = new Vector3(0.0f, 0.0f, Reach + 20.0f);
            yield return Wait(FullStroke);

            Assert.That(door.IsBlocked(), Is.True, "the intruder is not being counted as in the way");
            Assert.That(
                door.IsOpen,
                Is.True,
                "the gate shut on a vehicle that was standing in it");
        }

        /// <summary>
        /// A gate that is already shut is not held open by an enemy leaning on it, which is
        /// what stops the rule above being a way to jam somebody's door from outside.
        /// </summary>
        [UnityTest]
        public IEnumerator AnEnemyLeaningOnAShutGateDoesNotPinItOpen()
        {
            AutoDoor door = CreateDoor(Team.Green, Vector3.zero);
            CreateVehicle(Team.Brown, new Vector3(0.0f, 0.0f, 1.5f));
            yield return Wait(FullStroke);

            Assert.That(
                door.IsBlocked(),
                Is.True,
                "the intruder is not up against the gate, so this tests nothing");
            Assert.That(
                door.Openness,
                Is.EqualTo(0.0f),
                "an enemy parked against a shut gate opened it");
        }

        /// <summary>
        /// A gate hit while it is open stays open, which is the one place a door
        /// deliberately does the opposite of what a turret does.
        /// </summary>
        /// <remarks>
        /// A turret re-stows its barrel when it swaps model, because a gun's rest position
        /// is a fact about the gun. A gate's position is a fact about the traffic, and the
        /// leaf is solid: a gate that snapped shut the instant it was hit would put a
        /// two-metre barrier through whoever was driving through it. Asserted in the same
        /// frame as the damage, with no step in between, because one step of a full-height
        /// leaf appearing under a vehicle is already the bug.
        /// </remarks>
        [UnityTest]
        public IEnumerator AGateHitWhileOpenDoesNotSlamShut()
        {
            AutoDoor door = CreateDoor(Team.Green, Vector3.zero);
            CreateVehicle(Team.Green, new Vector3(0.0f, 0.0f, 10.0f));
            yield return Wait(FullStroke);
            Assert.That(door.IsOpen, Is.True, "it never opened, so being hit open proves nothing");

            var shell = door.GetComponent<Destructible>();
            shell.TakeDamage(shell.Tuning.HitPoints * 0.6f, Team.Brown);

            Assert.That(
                shell.State,
                Is.EqualTo(DestructionState.Damaged),
                "the hit did not swap the model, so no leaf was exchanged");
            Assert.That(door.Openness, Is.EqualTo(1.0f), "the gate forgot it was open");
            Assert.That(
                door.Leaf.localPosition.y,
                Is.EqualTo(-door.Travel).Within(0.01f),
                "the damaged gate's leaf came up under whoever was driving through");
        }

        /// <summary>
        /// A wrecked gate has nothing left to close, which is what makes shooting one a
        /// permanent hole rather than a door somebody has to remember not to shut.
        /// </summary>
        [UnityTest]
        public IEnumerator AWreckedGateHasNothingLeftToClose()
        {
            AutoDoor door = CreateDoor(Team.Green, Vector3.zero);
            var shell = door.GetComponent<Destructible>();
            shell.TakeDamage(shell.Tuning.HitPoints, Team.Brown);
            CreateVehicle(Team.Green, new Vector3(0.0f, 0.0f, 10.0f));
            yield return Wait(FullStroke);

            Assert.That(shell.State, Is.EqualTo(DestructionState.Destroyed), "it did not come down");
            Assert.That(door.Leaf, Is.Null, "the rubble still has a gate in it");
            Assert.That(door.IsOpen, Is.False, "rubble is a hole, not an open gate");
            Assert.That(
                door.IsBlocked(),
                Is.False,
                "a gate with no leaf is asking about a doorway that no longer exists");
        }

        /// <summary>
        /// A gate on no side opens for nobody, which is the safe way round for an authoring
        /// slip.
        /// </summary>
        /// <remarks>
        /// Reading the roll-call the other way - opening for anybody not hostile - would
        /// make an unconfigured gate stand open for both players at once, because
        /// <see cref="Teams.IsHostile"/> counts everybody as hostile to
        /// <see cref="Team.None"/>. A hole in a wall looks exactly like a gate that works.
        /// </remarks>
        [UnityTest]
        public IEnumerator AGateOnNoSideOpensForNobody()
        {
            AutoDoor door = CreateDoor(Team.None, Vector3.zero);
            CreateVehicle(Team.Green, new Vector3(0.0f, 0.0f, 6.0f));
            CreateVehicle(Team.Brown, new Vector3(0.0f, 0.0f, -6.0f));
            yield return Wait(FullStroke);

            Assert.That(door.Openness, Is.EqualTo(0.0f), "a gate belonging to nobody let somebody in");
        }

        /// <summary>
        /// A side cannot demolish its own gate, which is the same rule the turret gets and
        /// out of the same field.
        /// </summary>
        /// <remarks>
        /// Free behaviour rather than new code - <see cref="Destructible.TakeDamage"/> asks
        /// <see cref="Teams.IsHostile"/> - and pinned here because "free" and "checked" are
        /// different things. A gate its owner could accidentally shell open would be worse
        /// than no gate: the hole would be in their own wall.
        /// </remarks>
        [UnityTest]
        public IEnumerator AGateCannotBeShotDownByItsOwners()
        {
            AutoDoor door = CreateDoor(Team.Green, Vector3.zero);
            var shell = door.GetComponent<Destructible>();
            yield return null;

            Assert.That(
                shell.TakeDamage(shell.Tuning.HitPoints, Team.Green),
                Is.EqualTo(0.0f),
                "a side can blow a hole in its own wall");
            Assert.That(shell.State, Is.EqualTo(DestructionState.Intact));

            Assert.That(
                shell.TakeDamage(shell.Tuning.HitPoints, Team.Brown),
                Is.GreaterThan(0.0f),
                "the enemy cannot open it either, which makes it a permanent wall");
        }

        /// <summary>
        /// Assembles a gate out of cubes: two piers, and a leaf in every state but the
        /// rubble.
        /// </summary>
        /// <param name="side">Side it opens for.</param>
        /// <param name="at">Where to stand it.</param>
        /// <returns>The gate.</returns>
        private AutoDoor CreateDoor(Team side, Vector3 at)
        {
            var host = new GameObject($"Door ({side})");
            host.SetActive(false);
            host.transform.position = at;
            spawned.Add(host);

            GameObject intact = CreateState(host, Destructible.IntactNodeName, hinged: true);
            GameObject damaged = CreateState(host, Destructible.DamagedNodeName, hinged: true);
            GameObject destroyed = CreateState(host, Destructible.DestroyedNodeName, hinged: false);

            Destructible shell = host.AddComponent<Destructible>();
            shell.Configure(
                StructureKind.Door, StructureTuning.For(StructureKind.Door),
                intact, damaged, destroyed, null);
            shell.SetTeam(side);

            AutoDoor door = host.AddComponent<AutoDoor>();
            door.Configure(Reach, LeafSpeed);

            host.SetActive(true);
            return door;
        }

        /// <summary>
        /// Builds one destruction state, with or without a leaf in it.
        /// </summary>
        /// <param name="host">The gate being assembled.</param>
        /// <param name="name">State node name.</param>
        /// <param name="hinged">Whether this state carries a sliding leaf.</param>
        /// <returns>The state's child object.</returns>
        /// <remarks>
        /// The leaf node sits at the gate's own origin with the panel a metre up inside it,
        /// which is how <c>structure_door.py</c> builds it: the drop is measured off the
        /// panel's top, so a leaf whose origin was in the middle of itself would give half
        /// the travel and leave a metre of gate standing in a doorway that reads as open.
        /// </remarks>
        private static GameObject CreateState(GameObject host, string name, bool hinged)
        {
            var state = new GameObject(name);
            state.transform.SetParent(host.transform, false);

            Piece(state.transform, "PierWest", new Vector3(-2.25f, 1.0f, 0.0f), 0.5f);
            Piece(state.transform, "PierEast", new Vector3(2.25f, 1.0f, 0.0f), 0.5f);

            if (!hinged)
            {
                return state;
            }

            var leaf = new GameObject(AutoDoor.LeafNodeName);
            leaf.transform.SetParent(state.transform, false);
            Piece(leaf.transform, "LeafPanel", new Vector3(0.0f, 1.0f, 0.0f), 4.0f);

            return state;
        }

        /// <summary>
        /// Hangs one box of gate off something.
        /// </summary>
        /// <param name="parent">What to parent it to.</param>
        /// <param name="name">What to call it.</param>
        /// <param name="at">Where to put it, in the parent's space.</param>
        /// <param name="width">How wide it is; everything here is 2 m tall and 0.5 m thick.</param>
        private static void Piece(Transform parent, string name, Vector3 at, float width)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localScale = new Vector3(width, 2.0f, 0.5f);
            box.transform.localPosition = at;
        }

        /// <summary>
        /// Puts a vehicle on the field for a gate to notice.
        /// </summary>
        /// <param name="side">Side to paint it.</param>
        /// <param name="at">Where to put it.</param>
        /// <returns>The vehicle, so a test can drive it away again.</returns>
        /// <remarks>
        /// A real <see cref="VehicleController"/>, because that is what
        /// <see cref="VehicleController.OnTheField"/> holds and the roll-call is how a gate
        /// finds anything at all. Kinematic, so it stands where it is put - including in a
        /// doorway, which is the whole point of two of these tests.
        /// </remarks>
        private VehicleController CreateVehicle(Team side, Vector3 at)
        {
            var host = new GameObject($"Vehicle ({side})");
            host.SetActive(false);
            host.transform.position = at;
            spawned.Add(host);

            host.AddComponent<BoxCollider>().size = new Vector3(2.0f, 2.0f, 4.0f);
            host.AddComponent<Rigidbody>().isKinematic = true;
            host.AddComponent<VehicleTeamPaint>().Team = side;

            VehicleTuning tuning = VehicleTuning.For(VehicleKind.Tank);
            var vehicle = host.AddComponent<GroundVehicle>();
            vehicle.Configure(VehicleKind.Tank, tuning);

            host.SetActive(true);
            return vehicle;
        }

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
