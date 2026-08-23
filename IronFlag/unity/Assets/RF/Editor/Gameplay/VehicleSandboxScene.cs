using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Destruction;
using IronFlag.Editing;
using IronFlag.Editor.ArtPipeline;
using IronFlag.Levels;
using IronFlag.Objective;
using IronFlag.Players;
using IronFlag.Supply;
using IronFlag.UI;
using IronFlag.Vehicles;

namespace IronFlag.Editor.Gameplay
{
    /// <summary>
    /// Builds the scene the game is played in: two players, a roster of four vehicles each,
    /// the session that seats them, and a <see cref="LevelLoader"/> pointed at the map.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The map is not in here any more.</strong> Every coordinate this class used to
    /// carry - the coastline, the bunkers, the towers, the depots, the trees - moved into a
    /// level file, and what is left is the shell a map is played in. That split is the
    /// milestone: this builds the things that belong to a <em>session</em>, and
    /// <see cref="LevelBuilder"/> builds the things that belong to a <em>map</em>.
    /// </para>
    /// <para>
    /// The level is still <em>baked</em> into the saved scene, because a scene that opened
    /// empty would be a scene nobody could look at and the command-line still would have
    /// nothing to photograph. The bake is a copy, not the truth: <see cref="LevelLoader"/>
    /// throws it away on the first frame of play and rebuilds from the file. Editing the
    /// JSON and pressing Play is therefore the whole iteration loop - no menu item, no
    /// recompile.
    /// </para>
    /// <para>
    /// Generated rather than hand-authored, like every other scene in this project, so it
    /// picks up a rebuilt prefab or a retuned vehicle by being regenerated. It writes over
    /// <c>Sandbox.unity</c>, which is the scene the build settings already point at, so
    /// pressing Play lands here.
    /// </para>
    /// </remarks>
    public static class VehicleSandboxScene
    {
        /// <summary>Where the generated scene is saved.</summary>
        /// <remarks>
        /// Read off <see cref="LevelScenes"/> rather than written here, because the level
        /// editor loads this scene <em>by name</em> to play a map in it - and a scene saved
        /// under one name and loaded under another is a Play button that does nothing.
        /// </remarks>
        public static readonly string ScenePath = LevelScenes.GamePath;

        /// <summary>Input actions the players are wired up to.</summary>
        public const string ActionsPath = "Assets/RF/Input/IronFlagControls.inputactions";

        /// <summary>How many local players the sandbox seats.</summary>
        public const int PlayerCount = 2;

        /// <summary>The map the scene is built around.</summary>
        /// <remarks>
        /// The one thing about the map this class still knows, and it is a file name rather
        /// than a place. Point it at another level file and the scene is another map.
        /// </remarks>
        public const string LevelName = LevelLibrary.DefaultLevel;

        /// <summary>Name of the object the loaded map hangs off.</summary>
        private const string LoaderName = "Level Loader";

        /// <summary>Command-line switch naming the file <see cref="RenderToFile"/> writes.</summary>
        private const string OutputArgument = "-sandboxOutput";

        /// <summary>File <see cref="RenderToFile"/> writes when the switch is absent.</summary>
        private const string DefaultOutputFile = "vehicle-sandbox.png";

        /// <summary>Roster slot the still sends out, which is the jeep.</summary>
        /// <remarks>
        /// The tank until M6. The still is a picture of the thing the milestone added, and
        /// what M6 added is the only vehicle that can be carrying a flag.
        /// </remarks>
        private const int StagedVehicle = 0;

        /// <summary>Metres back down the road home the still puts the raider.</summary>
        /// <remarks>
        /// Clear of the tower it has just robbed, so the still is a picture of a jeep
        /// getting away rather than of one parked against a wall. Well outside
        /// <see cref="FlagRules.PickupRadius"/>, which is the point: the flag is on the mast
        /// because it was taken, not because the jeep is standing near it.
        /// </remarks>
        private const float StagedGetaway = 9.0f;

        /// <summary>Metres around the staged fight the still shoots the scenery up in.</summary>
        /// <remarks>
        /// Roughly what one camera can see, so everything the still changes is something the
        /// still shows.
        /// </remarks>
        private const float StagedRubbleRange = 24.0f;

        /// <summary>Metres between neighbouring vehicles parked outside a bunker.</summary>
        private const float ParkingSpacing = 7.0f;

        /// <summary>Metres in front of a bunker its roster is parked, before a match starts.</summary>
        private const float ParkingDistance = 11.0f;

        /// <summary>The sides the two players are on, in seat order.</summary>
        private static readonly Team[] Sides = Teams.Playing;

        /// <summary>
        /// Rebuilds the sandbox scene and saves it.
        /// </summary>
        [MenuItem("Tools/IronFlag/Build Vehicle Sandbox Scene", false, 151)]
        public static void BuildAndSave()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Build();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ScenePath)));
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.Refresh();
            Debug.Log($"IronFlag: vehicle sandbox saved to {ScenePath}");
        }

        /// <summary>
        /// Builds the sandbox and writes a render of it to a PNG file.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Intended for <c>-executeMethod</c>. Pass <c>-sandboxOutput &lt;path&gt;</c> to
        /// choose the file. The camera rigs only run while playing, so each shot is framed
        /// by calling the same placement maths the rig uses - which is exactly why that
        /// maths is static and free of side effects. The result is the real split, both
        /// viewports rendered into one image.
        /// </para>
        /// <para>
        /// The match itself is <em>staged</em> into this shot by <see cref="StageMatch"/>
        /// and exists nowhere else: one player is left in their bunker looking at their
        /// roster and the other is put out on the field, shot at and half out of fuel, so
        /// that both halves of the HUD appear in one image. Nothing moves in a saved scene,
        /// so a still is the only way to look at any of this without pressing Play - and a
        /// HUD is exactly the kind of thing that fails silently. <see cref="BuildAndSave"/>
        /// stages nothing, so the scene on disk never contains any of it.
        /// </para>
        /// </remarks>
        public static void RenderToFile()
        {
            const int width = 1600;
            const int height = 1800;

            List<PlayerVehicleDriver> players = Build();
            StageMatch(players);
            ShowHuds();

            CameraCapture.RenderToPng(
                FrameOnPlayers(players),
                width,
                height,
                CameraCapture.OutputPathFromCommandLine(OutputArgument, DefaultOutputFile));
        }

        /// <summary>
        /// Freezes one moment of a match: one player choosing, the other in a fight.
        /// </summary>
        /// <param name="players">The local players, in seat order.</param>
        /// <remarks>
        /// <para>
        /// Seat one is left exactly as the scene builder made it, which is a player standing
        /// in their bunker in front of their roster - the state every match actually starts
        /// in. Seat two is sent out in the tank, driven up the map, shot at, and run down to
        /// half a tank of fuel and half a load of shells, because a HUD whose every bar is
        /// full is a HUD nobody can tell is working.
        /// </para>
        /// <para>
        /// The damage is real damage and the fuel is really burnt: everything here goes
        /// through the same public methods the game uses, so a still that looks right is
        /// evidence about the game rather than about the staging.
        /// </para>
        /// </remarks>
        private static void StageMatch(List<PlayerVehicleDriver> players)
        {
            if (players.Count < 2)
            {
                return;
            }

            // Everybody starts inside, which is what the first frame of a real match does
            // and what the roster panel is about to claim. Without this the still shows four
            // vehicles parked on the apron under a panel saying they are all in the field.
            foreach (PlayerVehicleDriver player in players)
            {
                for (int slot = 0; slot < player.Roster.Count; slot++)
                {
                    VehicleBay bay = player.BayFor(slot);
                    if (bay != null)
                    {
                        bay.Stow();
                    }
                }
            }

            PlayerVehicleDriver raider = players[1];
            if (!raider.TakeTheField(StagedVehicle))
            {
                Debug.LogWarning("IronFlag: nothing could be deployed for the still.");
                return;
            }

            VehicleController vehicle = raider.ActiveVehicle;
            StageRaid(raider.Team, vehicle);

            StageWear(vehicle);
            StageRound(vehicle);

            Explosion blast = CombatPrefabBuilder.LoadExplosion();
            if (blast != null)
            {
                StageBlast(
                    blast,
                    vehicle.transform.position + (vehicle.transform.forward * 13.0f)
                        + (vehicle.transform.right * 4.0f) + (Vector3.up * 1.5f));
            }

            StageRubble(vehicle.transform.position);
            ClearDebris();
        }

        /// <summary>
        /// Takes the debris bursts back out of a staged shot.
        /// </summary>
        /// <remarks>
        /// Everything staged here that takes damage throws a burst, and a burst clears
        /// itself up in <c>Update</c> - which never runs outside play mode. So every chunk
        /// sits at its spawn point instead of flying apart and fading, and a dozen chunks at
        /// one point is a solid dark block in the middle of whatever was just shot. In a
        /// still of a broken flag tower it lands exactly where the flag stands, which reads
        /// as the objective having gone black.
        /// </remarks>
        private static void ClearDebris()
        {
            foreach (DebrisBurst burst in
                Object.FindObjectsByType<DebrisBurst>(FindObjectsInactive.Include))
            {
                Object.DestroyImmediate(burst.gameObject);
            }
        }

        /// <summary>
        /// Puts the staged jeep on the way home with the other side's flag on its mast.
        /// </summary>
        /// <param name="side">Side the raider is on.</param>
        /// <param name="vehicle">The jeep that has been sent out.</param>
        /// <remarks>
        /// <para>
        /// The whole of M6 in one frozen moment: a scouted tower, a flag off it, and the one
        /// vehicle allowed to be holding it, pointed at its own bunker. Everything here goes
        /// through the same public methods a match does - <see cref="FlagTower.Scout"/> and
        /// <see cref="Flag.GiveTo"/>, which refuses anything but a hostile jeep - so a still
        /// that looks right is evidence about the game rather than about the staging. In
        /// particular, the flag on the mast is there because the rules allowed it.
        /// </para>
        /// <para>
        /// It also frames both viewports on the same event from opposite sides: the raider's
        /// camera follows the jeep, and the defender is standing in their bunker watching it
        /// leave with their flag.
        /// </para>
        /// </remarks>
        private static void StageRaid(Team side, VehicleController vehicle)
        {
            Flag flag = Flag.EnemyOf(side);
            FlagTower tower = flag == null ? null : flag.Home;
            if (tower == null)
            {
                Debug.LogWarning("IronFlag: there is no enemy flag to stage a raid on.");
                return;
            }

            TeamBunker bunker = TeamBunker.For(side);
            Vector3 toward = bunker == null ? Vector3.zero : bunker.transform.position;
            Vector3 home = CombatPlane.Flatten(toward - tower.transform.position);
            Vector3 away = home.sqrMagnitude < 0.01f ? Vector3.forward : home.normalized;

            vehicle.Teleport(
                tower.transform.position + (away * StagedGetaway),
                Quaternion.LookRotation(away, Vector3.up).eulerAngles.y);

            // Broken open, because that is now the only way a flag comes off a tower. It
            // goes through the same damage the game does, so the still shows a cracked
            // pyramid behind the getaway rather than an intact one with nothing on it.
            tower.Open();
            if (!flag.GiveTo(vehicle))
            {
                Debug.LogWarning(
                    $"IronFlag: the staged {vehicle.Kind} was refused the {flag.Team} flag.");
            }
        }

        /// <summary>
        /// Shoots up the scenery around the staged fight, so the still shows all three
        /// destruction states at once.
        /// </summary>
        /// <param name="near">Where the fight is, which is what the camera is looking at.</param>
        /// <remarks>
        /// The same argument as the half-empty fuel gauge: a map on which everything is
        /// intact is a map nobody can tell the destruction system is running on. Only the
        /// scenery inside the frame is touched, because a flattened building in the other
        /// player's half is a change to a saved-scene render that nobody can see. Trees are
        /// taken all the way down and buildings to their middle state, through
        /// <see cref="Destructible.TakeDamage"/> rather than by swapping meshes here, so a
        /// still that looks right is evidence about the game rather than about the staging.
        /// The debris is not staged - it lasts under a second and nothing in a saved scene
        /// animates it.
        /// </remarks>
        private static void StageRubble(Vector3 near)
        {
            foreach (Destructible structure in
                Object.FindObjectsByType<Destructible>(FindObjectsInactive.Include))
            {
                if (CombatPlane.DistanceOnMap(near, structure.transform.position) > StagedRubbleRange)
                {
                    continue;
                }

                // The tower the raid went through is left where StageRaid put it. Breaking
                // one open is the picture - the cap off and the pole leaning, which is how
                // a player tells a checked pyramid from an unchecked one - and shelling it
                // the rest of the way to rubble would replace that with a heap that looks
                // like every other flattened building in the frame.
                if (structure.GetComponent<FlagTower>() != null)
                {
                    continue;
                }

                float share = structure.Kind == StructureKind.Tree ? 1.0f : 0.6f;
                structure.TakeDamage(structure.Tuning.HitPoints * share, Team.Brown);
            }
        }

        /// <summary>
        /// Spends some of a vehicle's armour, fuel and ammunition, so its bars read.
        /// </summary>
        /// <param name="vehicle">Vehicle to wear down.</param>
        private static void StageWear(VehicleController vehicle)
        {
            var hull = vehicle.GetComponent<VehicleHealth>();
            if (hull != null)
            {
                hull.TakeDamage(hull.MaxHitPoints * 0.38f, Team.Green);
            }

            var supply = vehicle.GetComponent<VehicleSupply>();
            if (supply == null)
            {
                return;
            }

            supply.Burn(1.0f, supply.FuelCapacity * 0.45f);
            for (int spent = 0; spent < supply.RoundsCarried / 2; spent++)
            {
                supply.TryTakeRound();
            }
        }

        /// <summary>
        /// Generates both players' HUDs and brings them up to date, for the still.
        /// </summary>
        /// <remarks>
        /// The panels are runtime-generated, so nothing exists to photograph until this is
        /// called; the canvases then have to be laid out by hand, because the layout pass
        /// that normally happens at the end of a frame never runs in a scene nobody is
        /// playing.
        /// </remarks>
        private static void ShowHuds()
        {
            foreach (PlayerHud hud in Object.FindObjectsByType<PlayerHud>(FindObjectsInactive.Include))
            {
                hud.Build();
                hud.Refresh();
            }

            Canvas.ForceUpdateCanvases();
        }

        /// <summary>
        /// Places one round a few metres out of a vehicle's barrel.
        /// </summary>
        /// <param name="vehicle">Vehicle to stage a shot from.</param>
        private static void StageRound(VehicleController vehicle)
        {
            var gun = vehicle == null ? null : vehicle.GetComponent<VehicleWeapon>();
            if (gun == null || gun.Muzzle == null)
            {
                return;
            }

            Projectile round = CombatPrefabBuilder.LoadProjectile(gun.Tuning.Kind);
            if (round == null)
            {
                return;
            }

            var shot = (GameObject)PrefabUtility.InstantiatePrefab(round.gameObject);
            shot.name = $"{round.name} (staged)";
            shot.transform.SetPositionAndRotation(
                gun.Muzzle.position + (gun.Muzzle.forward * 4.0f), gun.Muzzle.rotation);
            shot.transform.localScale = Vector3.one * (gun.Tuning.Radius * 2.0f);
        }

        /// <summary>
        /// Places one explosion frozen at the peak of its flash.
        /// </summary>
        /// <param name="blast">The explosion prefab.</param>
        /// <param name="at">Where it goes off.</param>
        /// <remarks>
        /// The prefab ships with its ball at zero scale, because an explosion that has not
        /// started yet has no size. Nothing runs in a saved scene to grow it, so the still
        /// has to do by hand what <see cref="Explosion.Scale"/> does over half a second.
        /// </remarks>
        private static void StageBlast(Explosion blast, Vector3 at)
        {
            var burst = (GameObject)PrefabUtility.InstantiatePrefab(blast.gameObject);
            burst.name = $"{blast.name} (staged)";
            burst.transform.position = at;

            Transform ball = burst.transform.Find(CombatPrefabBuilder.BallNodeName);
            if (ball != null)
            {
                ball.localScale = Vector3.one * 5.0f;
            }

            var flash = burst.GetComponent<Light>();
            if (flash != null)
            {
                flash.intensity = 40.0f;
                flash.range = 10.0f;
            }
        }

        /// <summary>
        /// Creates the scene contents: lighting, the map, vehicles, cameras, players.
        /// </summary>
        /// <returns>The local players, in seat order.</returns>
        public static List<PlayerVehicleDriver> Build()
        {
            GeneratedMaterials.EnsureAssets();
            EnsureHudLayers();
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            ConfigureLighting();
            LevelDefinition level = BakeLevel();

            var players = new List<PlayerVehicleDriver>();
            var parent = new GameObject("Vehicles");

            for (int slot = 0; slot < PlayerCount; slot++)
            {
                Team side = Sides[slot % Sides.Length];
                List<VehicleController> vehicles = PlaceVehicles(side, parent.transform, level);
                players.Add(CreatePlayer(slot, side, vehicles));
            }

            CreateSession(players);
            return players;
        }

        /// <summary>
        /// Reads the level file, builds it into the scene, and leaves a loader pointed at it.
        /// </summary>
        /// <returns>
        /// The level that was baked, or an empty one when the file could not be read - so
        /// the rest of the scene still builds and the log says which file was the problem.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The bake is what makes the saved scene worth opening and the still worth
        /// rendering. It is deliberately the <em>same</em> call the game makes at load, with
        /// one difference: prefabs are instantiated as prefabs, so the scene on disk keeps
        /// its links and a rebuilt depot shows up in it. A second builder for the editor
        /// would be a second answer to the question of what a level file means.
        /// </para>
        /// <para>
        /// Anything wrong with the level is logged here as well as at load, because this is
        /// the moment somebody is looking - a menu item they just pressed - and a warning
        /// during a headless play-mode test is a warning nobody reads.
        /// </para>
        /// </remarks>
        private static LevelDefinition BakeLevel()
        {
            LevelCatalog catalog = LevelCatalogBuilder.Load();

            string path = LevelLibrary.PathFor(LevelName);
            if (!LevelFile.TryRead(path, out LevelDefinition level, out string problem))
            {
                Debug.LogWarning($"{problem} The scene will have no map in it.");
                level = new LevelDefinition();
            }

            foreach (string trouble in LevelValidation.Problems(level))
            {
                Debug.LogWarning($"IronFlag: {LevelName} - {trouble}");
            }

            var host = new GameObject(LoaderName);
            LevelLoader loader = host.AddComponent<LevelLoader>();
            loader.Configure(LevelName, catalog);

            GameObject map = LevelBuilder.Build(level, catalog, InstantiatePrefab);
            map.transform.SetParent(host.transform, false);

            return level;
        }

        /// <summary>
        /// Instantiates a prefab as a prefab, keeping the scene's link back to it.
        /// </summary>
        /// <param name="prefab">The prefab asset.</param>
        /// <returns>The instance.</returns>
        private static GameObject InstantiatePrefab(GameObject prefab)
            => (GameObject)PrefabUtility.InstantiatePrefab(prefab);

        /// <summary>
        /// Daylight, angled so the vehicles cast shadows that read from above.
        /// </summary>
        /// <remarks>
        /// Deliberately not shared with the art preview's lighting: that one is a product
        /// shot and this one is the game, and the two are expected to drift apart.
        /// </remarks>
        internal static void ConfigureLighting()
        {
            Light sun = Object.FindAnyObjectByType<Light>();
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(52.0f, -30.0f, 0.0f);
                sun.intensity = 1.5f;
                sun.shadows = LightShadows.Soft;
                sun.color = new Color(1.0f, 0.97f, 0.9f);
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.45f, 0.51f, 0.60f);
            RenderSettings.ambientEquatorColor = new Color(0.34f, 0.36f, 0.36f);
            RenderSettings.ambientGroundColor = new Color(0.19f, 0.18f, 0.16f);
            DynamicGI.UpdateEnvironment();
        }

        /// <summary>
        /// Parks one side's four vehicles outside their own bunker.
        /// </summary>
        /// <param name="side">Which team the vehicles belong to.</param>
        /// <param name="parent">Object to hang them off.</param>
        /// <param name="level">The map, which is where that side's bunker is.</param>
        /// <returns>The vehicles, in roster order.</returns>
        /// <remarks>
        /// This is where they stand in the <em>saved</em> scene, and it lasts exactly one
        /// frame: <see cref="PlayerVehicleDriver"/> stows the whole roster inside the
        /// bunker on its first update, because the design document's core loop starts with
        /// the player choosing one. Parking them on the apron rather than on a start line
        /// is what makes the scene on disk say the same thing the game does - these four
        /// are yours, and they are at home.
        /// </remarks>
        private static List<VehicleController> PlaceVehicles(
            Team side, Transform parent, LevelDefinition level)
        {
            var placed = new List<VehicleController>();
            LevelBunker home = level.BunkerFor(side);
            Vector3 bunker = home == null ? Vector3.zero : home.Position;

            // Along the bunker's own forward, rather than along the map's, so a level that
            // faces a bunker any way it likes still parks its roster on the apron in front
            // of the lift bay instead of through the back wall.
            Quaternion facing = Quaternion.Euler(
                0.0f, home == null ? 0.0f : home.YawDegrees, 0.0f);
            int slot = 0;

            foreach (VehicleKind kind in VehiclePrefabBuilder.Roster())
            {
                string path = VehiclePrefabBuilder.PrefabPathFor(kind);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    Debug.LogWarning($"IronFlag: {path} is missing; run Build Vehicle Prefabs first.");
                    continue;
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.name = $"{prefab.name} ({side})";
                instance.transform.SetParent(parent, true);

                Vector3 start = bunker + (facing * new Vector3(
                    (slot - 1.5f) * ParkingSpacing, 0.0f, ParkingDistance));

                instance.transform.SetPositionAndRotation(start, facing);

                var paint = instance.GetComponent<VehicleTeamPaint>();
                if (paint != null)
                {
                    paint.Team = side;
                }

                placed.Add(instance.GetComponent<VehicleController>());
                slot++;
            }

            return placed;
        }

        /// <summary>
        /// Creates one player: their camera, their viewport and their roster.
        /// </summary>
        /// <param name="slot">Which seat, zero-based. The first one is at the top.</param>
        /// <param name="side">Which team they are playing.</param>
        /// <param name="vehicles">The vehicles they can step through.</param>
        /// <returns>The driver.</returns>
        private static PlayerVehicleDriver CreatePlayer(
            int slot, Team side, List<VehicleController> vehicles)
        {
            TopDownCameraRig rig = CreateCameraRig(slot, side);
            var player = new GameObject($"Player {slot + 1} ({side})");
            PlayerVehicleDriver driver = player.AddComponent<PlayerVehicleDriver>();
            driver.Configure(rig, vehicles);
            CreateHud(slot, side, driver, rig.View);
            return driver;
        }

        /// <summary>
        /// Creates one player's HUD, on its own canvas in front of its own camera.
        /// </summary>
        /// <param name="slot">Which seat, zero-based.</param>
        /// <param name="side">Which team it belongs to, for the name.</param>
        /// <param name="driver">The player it reports on.</param>
        /// <param name="view">That player's camera.</param>
        /// <returns>The HUD.</returns>
        /// <remarks>
        /// The panels themselves are not built here. They are generated at runtime from the
        /// player's roster, so the saved scene carries an empty canvas rather than a
        /// snapshot of four rows and three bars that were true when somebody pressed a menu
        /// item.
        /// </remarks>
        private static PlayerHud CreateHud(
            int slot, Team side, PlayerVehicleDriver driver, Camera view)
        {
            var host = new GameObject(
                $"HUD {slot + 1} ({side})", typeof(Canvas), typeof(CanvasScaler), typeof(PlayerHud));

            var hud = host.GetComponent<PlayerHud>();
            hud.Configure(driver, view, slot);
            return hud;
        }

        /// <summary>
        /// Makes sure the project has a HUD layer for every seat.
        /// </summary>
        /// <remarks>
        /// A screen-space canvas is geometry in the world, so without a layer per player one
        /// player's instruments turn up in the other player's view the moment their cameras
        /// get close - see <see cref="HudLayers"/>. Layers cannot be created from a runtime
        /// script, so the scene builder does it, once, from the same names the runtime looks
        /// them up by.
        /// </remarks>
        private static void EnsureHudLayers()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets.Length == 0)
            {
                Debug.LogWarning("IronFlag: the tag manager is missing; both HUDs will share a layer.");
                return;
            }

            var manager = new SerializedObject(assets[0]);
            SerializedProperty layers = manager.FindProperty("layers");
            bool changed = false;

            for (int slot = 0; slot < HudLayers.Count; slot++)
            {
                string wanted = HudLayers.NameFor(slot);
                if (IndexOfLayer(layers, wanted) >= 0)
                {
                    continue;
                }

                int free = FirstFreeLayer(layers);
                if (free < 0)
                {
                    Debug.LogWarning($"IronFlag: no free layer left for {wanted}.");
                    break;
                }

                layers.GetArrayElementAtIndex(free).stringValue = wanted;
                changed = true;
            }

            if (changed)
            {
                manager.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
            }
        }

        private static int IndexOfLayer(SerializedProperty layers, string name)
        {
            for (int index = 0; index < layers.arraySize; index++)
            {
                if (layers.GetArrayElementAtIndex(index).stringValue == name)
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>
        /// Returns the first layer slot nothing is using.
        /// </summary>
        /// <param name="layers">The tag manager's layer array.</param>
        /// <returns>An index, or -1 when the project has run out.</returns>
        /// <remarks>
        /// The first eight are Unity's own and are left alone even when they read as empty:
        /// three of them are blank in a fresh project and all three are still reserved.
        /// </remarks>
        private static int FirstFreeLayer(SerializedProperty layers)
        {
            for (int index = 8; index < layers.arraySize; index++)
            {
                if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(index).stringValue))
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>
        /// Creates one player's camera, already in its half of the screen.
        /// </summary>
        /// <param name="slot">Which seat, zero-based.</param>
        /// <param name="side">Which team the camera belongs to, for the name.</param>
        /// <returns>The rig driving that camera.</returns>
        /// <remarks>
        /// The first seat reuses the scene's own main camera - it is the one that is tagged,
        /// keeps its audio listener, and is what <c>Camera.main</c> answers with. Every seat
        /// after it gets a plain camera with no listener: two listeners in a scene is a
        /// warning and doubled sound, and there is only ever one pair of speakers.
        /// </remarks>
        private static TopDownCameraRig CreateCameraRig(int slot, Team side)
        {
            Camera camera = slot == 0 ? Camera.main : null;
            if (camera == null)
            {
                var host = new GameObject("Camera", typeof(Camera));
                camera = host.GetComponent<Camera>();
                if (slot == 0)
                {
                    host.tag = "MainCamera";
                    host.AddComponent<AudioListener>();
                }
            }

            camera.name = $"Camera {slot + 1} ({side})";

            if (slot != 0)
            {
                AudioListener spare = camera.GetComponent<AudioListener>();
                if (spare != null)
                {
                    Object.DestroyImmediate(spare);
                }
            }

            camera.orthographic = false;
            camera.fieldOfView = 50.0f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 500.0f;
            camera.depth = slot;
            camera.rect = SplitScreenLayout.ViewportFor(slot, PlayerCount);
            camera.cullingMask = HudLayers.CullingMaskFor(slot);

            return camera.gameObject.AddComponent<TopDownCameraRig>();
        }

        /// <summary>
        /// Creates the object that deals the devices out and owns the split.
        /// </summary>
        /// <param name="players">The local players, in seat order.</param>
        private static void CreateSession(List<PlayerVehicleDriver> players)
        {
            var controls = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
            if (controls == null)
            {
                Debug.LogWarning($"IronFlag: {ActionsPath} is missing; the sandbox will not respond.");
            }

            var host = new GameObject("Local Multiplayer");
            LocalMultiplayer session = host.AddComponent<LocalMultiplayer>();
            session.Configure(controls, players);
            session.ApplyViewports();

            // The match is what is being played, not part of what it is played on: it goes
            // here rather than on the map so that loading a level does not silently restart
            // it, and so a level file never has to describe a rule.
            host.AddComponent<Match>();

            // The way back out of a playtest. It switches itself off unless the editor is
            // what loaded this scene, so a match started normally is exactly what it was.
            host.AddComponent<PlaytestReturn>();
        }

        /// <summary>
        /// Points each player's camera at their own start line, for the command-line render.
        /// </summary>
        /// <param name="players">The local players, in seat order.</param>
        /// <returns>The framed cameras, in seat order.</returns>
        private static Camera[] FrameOnPlayers(List<PlayerVehicleDriver> players)
        {
            var framed = new List<Camera>();

            foreach (PlayerVehicleDriver driver in players)
            {
                TopDownCameraRig rig = driver.CameraRig;
                if (rig == null || driver.Roster.Count == 0)
                {
                    Debug.LogWarning($"IronFlag: {driver.name} has no camera or no vehicles to frame.");
                    continue;
                }

                // Whatever that player is actually looking at: their vehicle if they have
                // one out, and their own bunker if they are still choosing.
                TeamBunker home = TeamBunker.For(driver.Team);
                Vector3 at = driver.ActiveVehicle != null
                    ? driver.ActiveVehicle.transform.position
                    : home == null ? driver.Roster[0].transform.position : home.transform.position;

                // The rig's own maths, minus the smoothing that only exists while playing.
                Vector3 focus = TopDownCameraRig.SolveFocus(at, Vector3.zero, 0.0f, 0.0f, 0.0f);
                rig.transform.SetPositionAndRotation(
                    TopDownCameraRig.SolveCameraPosition(
                        focus, rig.PitchDegrees, rig.YawDegrees, rig.Distance),
                    TopDownCameraRig.SolveRotation(rig.PitchDegrees, rig.YawDegrees));

                framed.Add(rig.View);
            }

            return framed.ToArray();
        }
    }
}
