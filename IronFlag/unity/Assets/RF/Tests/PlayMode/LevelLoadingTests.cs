using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Destruction;
using IronFlag.Levels;
using IronFlag.Objective;
using IronFlag.Vehicles;

namespace IronFlag.Tests.PlayMode
{
    /// <summary>
    /// The map, loaded from its file and then driven on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two claims are being checked here and they are the milestone. The first is that the
    /// file is the map: what the scene contains on the first frame of play is what the JSON
    /// says, rebuilt, rather than whatever was baked into the scene the last time somebody
    /// pressed a menu item. The second is that the map <em>works</em> - that the causeway
    /// carries a tank, that a bridge deck comes out level with the banks it joins, and that
    /// the water between them does what the design document says water does.
    /// </para>
    /// <para>
    /// The second one cannot be checked by reading the level file, because it is a claim
    /// about an asset's dimensions and a collider's height. It is exactly the kind of thing
    /// that looks perfect in the editor and turns out to be a fifteen-centimetre step
    /// nothing can drive up.
    /// </para>
    /// </remarks>
    public sealed class LevelLoadingTests
    {
        /// <summary>Name of the scene these tests play, as the scene manager knows it.</summary>
        private const string SandboxSceneName = "Sandbox";

        private readonly List<GameObject> spawned = new List<GameObject>();

        /// <summary>
        /// Takes the sandbox back out of the world when a test has finished with it.
        /// </summary>
        /// <returns>The coroutine the framework steps through.</returns>
        /// <remarks>
        /// The same debt <see cref="SplitScreenTests"/> pays, for the same reason: loading a
        /// scene <c>Single</c> replaces the world for everybody who comes after, and this map
        /// carries depots that would quietly refuel a tank another class parked.
        /// </remarks>
        [UnityTearDown]
        public IEnumerator LeaveNoSandboxBehind()
        {
            foreach (GameObject item in spawned)
            {
                if (item != null)
                {
                    Object.DestroyImmediate(item);
                }
            }

            spawned.Clear();

            Scene sandbox = SceneManager.GetSceneByName(SandboxSceneName);
            if (!sandbox.IsValid() || !sandbox.isLoaded)
            {
                yield break;
            }

            SceneManager.SetActiveScene(SceneManager.CreateScene("AfterLevelLoading"));
            yield return SceneManager.UnloadSceneAsync(sandbox);
        }

        /// <summary>
        /// The file is the map: pressing Play rebuilds it, and what comes up is what the
        /// level file says.
        /// </summary>
        [UnityTest]
        public IEnumerator TheMapThatComesUpIsTheOneInTheFile()
        {
            yield return LoadTheSandbox();

            LevelDefinition level = LevelLoader.Current;
            Assert.That(level, Is.Not.Null, "no level was loaded");
            Assert.That(level.Name, Is.Not.Empty);

            Assert.That(
                Object.FindObjectsByType<TeamBunker>().Length,
                Is.EqualTo(level.Bunkers.Length),
                "the bunkers in the world do not match the bunkers in the file");
            Assert.That(
                Object.FindObjectsByType<FlagTower>().Length,
                Is.EqualTo(level.Towers.Length),
                "the towers in the world do not match the towers in the file");
            // Towers are destructibles as well, and they are counted by the line above.
            int scenery = 0;
            foreach (Destructible structure in Object.FindObjectsByType<Destructible>())
            {
                if (structure.GetComponent<FlagTower>() == null)
                {
                    scenery++;
                }
            }

            Assert.That(
                scenery,
                Is.EqualTo(level.Structures.Length),
                "the scenery in the world does not match the scenery in the file");

            Assert.That(
                Object.FindObjectsByType<Flag>().Length,
                Is.EqualTo(2),
                "there is not one flag a side");
            Assert.That(WaterLine.Active, Is.Not.Null, "the map has no sea");
            Assert.That(
                WaterLine.Active.Surface,
                Is.EqualTo(level.Bounds.WaterLevel).Within(0.001f),
                "the sea is not where the level file puts it");
        }

        /// <summary>
        /// Loading a level replaces the one that was up rather than laying another on top
        /// of it - which is the whole of what a level editor will need from the loader.
        /// </summary>
        /// <remarks>
        /// The failure this guards against is not a doubled map, which anybody would see. It
        /// is a doubled <em>bunker</em>: two on the same spot, both on
        /// <see cref="TeamBunker.For"/>'s roll-call, one of them already destroyed and about
        /// to disappear with a player's whole roster stowed inside it.
        /// </remarks>
        [UnityTest]
        public IEnumerator LoadingALevelReplacesTheOneThatWasUp()
        {
            yield return LoadTheSandbox();

            var loader = Object.FindAnyObjectByType<LevelLoader>();
            Assert.That(loader, Is.Not.Null, "the scene has no level loader");

            Assert.That(loader.Load(loader.LevelName), Is.True, "the level would not load again");
            yield return null;
            yield return null;

            Assert.That(
                Object.FindObjectsByType<TeamBunker>().Length,
                Is.EqualTo(2),
                "loading the map again left a second set of bunkers");
            Assert.That(
                Object.FindObjectsByType<FlagTower>().Length,
                Is.EqualTo(LevelLoader.Current.Towers.Length),
                "loading the map again left a second set of towers");

            foreach (Team side in new[] { Team.Green, Team.Brown })
            {
                TeamBunker home = TeamBunker.For(side);
                Assert.That(home, Is.Not.Null, $"{side} lost its bunker to the reload");
                Assert.That(
                    home.gameObject.activeInHierarchy,
                    Is.True,
                    $"{side}'s bunker is one that has already been taken down");
            }
        }

        /// <summary>
        /// The middle of the map is dry, which is what stops a match from being able to
        /// deadlock when both bridges are down.
        /// </summary>
        [UnityTest]
        public IEnumerator TheCausewayCarriesATank()
        {
            yield return LoadTheSandbox();

            VehicleHealth tank = DropATank(Vector3.zero);
            yield return Settle();

            Assert.That(tank.IsDestroyed, Is.False, "the causeway is under water");
            Assert.That(
                tank.transform.position.y,
                Is.GreaterThan(LevelLoader.Current.Bounds.DrownDepth),
                "the tank sank into the causeway");
        }

        /// <summary>
        /// A bridge deck comes out flush with the banks it joins, rather than as a step.
        /// </summary>
        /// <remarks>
        /// The bridge is modelled standing on a riverbed with its deck a little over a metre
        /// up, and the level file sinks it by exactly that much. Nothing in the level file
        /// can check that number - it is a fact about the asset - so this is the only place
        /// the two are ever compared. A bridge placed at ground level would leave its deck a
        /// metre in the air, which reads as a perfectly good bridge and is a wall.
        /// </remarks>
        [UnityTest]
        public IEnumerator ABridgeDeckIsLevelWithTheBanksItJoins()
        {
            yield return LoadTheSandbox();

            LevelStructure bridge = FirstBridge();
            Assert.That(bridge, Is.Not.Null, "the map has no bridge");

            VehicleHealth tank = DropATank(
                new Vector3(bridge.Position.x, 0.0f, bridge.Position.z));
            yield return Settle();

            Assert.That(tank.IsDestroyed, Is.False, "the tank fell through the bridge");
            Assert.That(
                tank.transform.position.y,
                Is.EqualTo(0.0f).Within(0.2f),
                "the bridge deck is not level with the ground either side of it");
        }

        /// <summary>
        /// The water between the crossings is water, and the design document's answer to
        /// driving into it is the vehicle.
        /// </summary>
        [UnityTest]
        public IEnumerator DrivingOffTheBankIntoTheChannelIsTheEndOfTheVehicle()
        {
            yield return LoadTheSandbox();

            VehicleHealth tank = DropATank(SomewhereInTheChannel());
            yield return Settle();

            Assert.That(tank.IsDestroyed, Is.True, "the channel held the tank up");
        }

        /// <summary>
        /// A jeep parked against an opened tower can take the flag, whichever side of the
        /// tower it drove up to.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The bug this exists for: the flag stands in the middle of four and a half metres
        /// of masonry, so a jeep nose-on to a wall has its origin 4.2 m from the flag - just
        /// past the 4 m pickup radius - while the same jeep side-on has it at 3.1 m and gets
        /// the flag. Which meant driving at a tower did nothing, backing off and clipping it
        /// sideways worked, and nothing on screen explained the difference.
        /// </para>
        /// <para>
        /// Measured against the tower's real collider rather than a written-down number, and
        /// from eight directions rather than one, because the failure was specifically about
        /// the direction you happened to arrive from.
        /// </para>
        /// </remarks>
        [UnityTest]
        public IEnumerator AnOpenedTowerGivesUpItsFlagFromEveryApproach()
        {
            yield return LoadTheSandbox();

            FlagTower tower = FlagTower.RealFor(Team.Green);
            Assert.That(tower, Is.Not.Null, "the map has no real green tower");
            Assert.That(tower.Open(), Is.True, "the tower would not break open");
            yield return null;

            Flag flag = Flag.Of(Team.Green);
            Assert.That(flag, Is.Not.Null, "the green flag is missing");
            Assert.That(flag.IsVisible, Is.True, "the opened tower is still hiding its flag");

            Bounds footprint = Footprint(tower);
            float reach = Mathf.Max(footprint.extents.x, footprint.extents.z) + JeepHalfLength;

            for (int step = 0; step < 8; step++)
            {
                float radians = step * Mathf.PI * 0.25f;
                Vector3 spot = tower.transform.position
                    + (new Vector3(Mathf.Sin(radians), 0.0f, Mathf.Cos(radians)) * reach);

                Assert.That(
                    flag.IsWithinReach(spot),
                    Is.True,
                    $"a jeep parked against the tower at {step * 45} degrees cannot reach the "
                    + "flag, so taking it depends on which way you drove up");
            }
        }

        /// <summary>Half a jeep, in metres: how far its origin sits behind its bumper.</summary>
        private const float JeepHalfLength = 2.0f;

        /// <summary>
        /// Measures the box a tower's standing masonry fills.
        /// </summary>
        /// <param name="tower">The tower.</param>
        /// <returns>Its world-space bounds.</returns>
        private static Bounds Footprint(FlagTower tower)
        {
            var box = new Bounds(tower.transform.position, Vector3.zero);
            foreach (Collider part in tower.GetComponentsInChildren<Collider>())
            {
                box.Encapsulate(part.bounds);
            }

            return box;
        }

        /// <summary>
        /// Loads the sandbox and lets its first frame run, which is where the map is built.
        /// </summary>
        /// <returns>The coroutine the test steps through.</returns>
        private IEnumerator LoadTheSandbox()
        {
            SceneManager.LoadScene(SandboxSceneName, LoadSceneMode.Single);
            yield return null;
            yield return new WaitForFixedUpdate();
        }

        /// <summary>
        /// Long enough for something dropped a half metre to have landed on whatever is
        /// under it, or to have gone through it.
        /// </summary>
        /// <returns>The coroutine the test steps through.</returns>
        private static IEnumerator Settle()
        {
            float until = Time.time + 0.75f;
            while (Time.time < until)
            {
                yield return null;
            }
        }

        /// <summary>
        /// Drops a tank-shaped thing onto a spot on the map and lets go of it.
        /// </summary>
        /// <param name="at">Where to drop it, on the ground plane.</param>
        /// <returns>Its hit points, which is what a test asks about afterwards.</returns>
        /// <remarks>
        /// Assembled rather than loaded from a prefab, like every other vehicle in these
        /// tests, so a failure means the map is wrong rather than the asset. It needs a real
        /// collider and real gravity, though: what is being measured is what it lands on.
        /// </remarks>
        private VehicleHealth DropATank(Vector3 at)
        {
            var host = new GameObject("Dropped Tank");
            host.SetActive(false);
            host.transform.position = at + (Vector3.up * 0.5f);
            spawned.Add(host);

            BoxCollider hull = host.AddComponent<BoxCollider>();
            hull.size = new Vector3(3.2f, 2.0f, 6.0f);
            hull.center = new Vector3(0.0f, 1.0f, 0.0f);

            host.AddComponent<Rigidbody>();
            host.AddComponent<VehicleTeamPaint>().Team = Team.Green;

            VehicleTuning tuning = VehicleTuning.For(VehicleKind.Tank);
            host.AddComponent<GroundVehicle>().Configure(VehicleKind.Tank, tuning);
            VehicleHealth health = host.AddComponent<VehicleHealth>();
            health.Configure(tuning.HitPoints, null, 2.5f);

            host.SetActive(true);
            return health;
        }

        private static LevelStructure FirstBridge()
        {
            foreach (LevelStructure structure in LevelLoader.Current.Structures)
            {
                if (structure.Structure == StructureKind.Bridge)
                {
                    return structure;
                }
            }

            return null;
        }

        /// <summary>Metres of open water a dropped tank needs on every side of it.</summary>
        /// <remarks>
        /// A tank is 3.2 m wide and 6 m long, and one dropped a metre off a coastline lands
        /// with a track on the bank and stays up - which reads as "the sea holds tanks" and
        /// is really "the test aimed badly".
        /// </remarks>
        private const float Clearance = 4.0f;

        /// <summary>
        /// Finds open water on the line between the two bases, well clear of any bridge.
        /// </summary>
        /// <returns>A spot in the channel with room around it for a tank.</returns>
        /// <remarks>
        /// Read off the level rather than written down here, so this still tests the map
        /// rather than a memory of it after somebody moves the coastline.
        /// </remarks>
        private static Vector3 SomewhereInTheChannel()
        {
            LevelDefinition level = LevelLoader.Current;

            for (float x = 0.0f; x < level.Bounds.HalfExtent; x += 1.0f)
            {
                var at = new Vector3(x, 0.0f, 0.0f);
                if (IsOpenWater(level, at) && !NearABridge(at))
                {
                    return at;
                }
            }

            Assert.Fail("the map has no water between its two halves");
            return Vector3.zero;
        }

        /// <summary>
        /// Reports whether a spot is water with a tank's worth of room around it.
        /// </summary>
        /// <param name="level">The map.</param>
        /// <param name="at">The spot.</param>
        /// <returns><c>true</c> when nothing within <see cref="Clearance"/> is dry.</returns>
        private static bool IsOpenWater(LevelDefinition level, Vector3 at)
        {
            foreach (Vector3 corner in new[]
            {
                Vector3.zero,
                new Vector3(Clearance, 0.0f, 0.0f),
                new Vector3(-Clearance, 0.0f, 0.0f),
                new Vector3(0.0f, 0.0f, Clearance),
                new Vector3(0.0f, 0.0f, -Clearance),
            })
            {
                if (level.IsOnLand(at + corner, 0.0f))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool NearABridge(Vector3 at)
        {
            foreach (LevelStructure structure in LevelLoader.Current.Structures)
            {
                if (structure.Structure == StructureKind.Bridge
                    && Mathf.Abs(structure.Position.x - at.x) < 12.0f)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
