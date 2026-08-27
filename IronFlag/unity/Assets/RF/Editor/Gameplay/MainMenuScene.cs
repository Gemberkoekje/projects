using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using IronFlag.Core;
using IronFlag.Editing;
using IronFlag.Editor.ArtPipeline;
using IronFlag.Levels;
using IronFlag.Menu;
using IronFlag.UI;

namespace IronFlag.Editor.Gameplay
{
    /// <summary>
    /// Builds the scene the game starts in: a map turning slowly behind a column of choices.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The smallest of the three generated scenes, and the only one with no gameplay in it at
    /// all - no players, no match, no vehicles, nothing that can be shot. What it does have is
    /// a real map, built by <see cref="LevelLoader"/> out of the same file the game plays and
    /// lit by the same table, because the alternative was a dark screen with four buttons on it
    /// and the game already knew how to draw an island.
    /// </para>
    /// <para>
    /// Generated rather than authored like the other two, and it bakes a copy of the map for
    /// the same reason they do: a scene that opened empty is a scene nobody can look at without
    /// pressing Play, and the command-line still would have nothing to photograph.
    /// </para>
    /// </remarks>
    public static class MainMenuScene
    {
        /// <summary>Name of the object the backdrop map hangs off.</summary>
        private const string LoaderName = "Level Loader";

        /// <summary>Command-line switch naming the file <see cref="RenderToFile"/> writes.</summary>
        private const string OutputArgument = "-menuOutput";

        /// <summary>File <see cref="RenderToFile"/> writes when the switch is absent.</summary>
        private const string DefaultOutputFile = "main-menu.png";

        /// <summary>Command-line switch naming which screen <see cref="RenderToFile"/> shows.</summary>
        private const string PanelArgument = "-menuPanel";

        /// <summary>The map the menu turns around.</summary>
        /// <remarks>
        /// The same one the other two scenes are built on. A menu that opened on whatever map
        /// happened to be first alphabetically would be a boot screen that changed the day
        /// somebody saved a map called "aaa".
        /// </remarks>
        public const string LevelName = LevelLibrary.DefaultLevel;

        /// <summary>
        /// Rebuilds the main menu scene and saves it.
        /// </summary>
        [MenuItem("Tools/IronFlag/Build Main Menu Scene", false, 150)]
        public static void BuildAndSave()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Build();

            string path = LevelScenes.MainMenuPath;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), path);

            // After the save, so the scene asset exists by the time the build list looks for
            // it - BuildScenes leaves out anything that is not on disk yet.
            BuildScenes.Register();
            AssetDatabase.Refresh();
            Debug.Log($"IronFlag: main menu saved to {path}");
        }

        /// <summary>
        /// Builds the menu and writes a render of it to a PNG file.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Intended for <c>-executeMethod</c>. Pass <c>-menuOutput &lt;path&gt;</c> to choose
        /// the file and <c>-menuPanel root|levels|settings</c> to choose which of the three
        /// screens is up.
        /// </para>
        /// <para>
        /// The menu is generated at runtime, so nothing exists to photograph until this asks
        /// for it by hand - the same arrangement the level editor's still uses, and for the
        /// same reason: a saved scene carrying a frozen copy of a map list would be a scene
        /// carrying the maps that existed when somebody pressed a menu item.
        /// </para>
        /// </remarks>
        public static void RenderToFile()
        {
            const int width = 1920;
            const int height = 1080;

            MainMenuController menu = Build();
            MenuBackdrop backdrop = Object.FindAnyObjectByType<MenuBackdrop>();

            // Pointed at a texture of the right shape first. The menu scales with the screen,
            // so in batch mode - where the game view is not the shape of the still - the column
            // would be laid out for one window and photographed in another.
            var frame = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            backdrop.View.targetTexture = frame;

            try
            {
                menu.Show(PanelFromCommandLine());

                Canvas.ForceUpdateCanvases();
                menu.Refresh();
                Canvas.ForceUpdateCanvases();

                CameraCapture.RenderToPng(
                    backdrop.View,
                    width,
                    height,
                    CameraCapture.OutputPathFromCommandLine(OutputArgument, DefaultOutputFile));
            }
            finally
            {
                backdrop.View.targetTexture = null;
                frame.Release();
                Object.DestroyImmediate(frame);
            }
        }

        /// <summary>
        /// Creates the scene contents: lighting, the map, the view and the menu.
        /// </summary>
        /// <returns>The menu.</returns>
        public static MainMenuController Build()
        {
            GeneratedMaterials.EnsureAssets();

            // The level editor's own check, called rather than repeated: it writes the UI
            // action map into the project-wide actions asset, so once either scene has been
            // generated the other finds it already there.
            InputActionAsset controls = LevelEditorScene.EnsureUiActions();
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            LevelDefinition level = BakeLevel();
            MenuBackdrop backdrop = CreateView(level);
            LightScene(backdrop.Distance);
            CreateEventSystem(controls);

            Camera panelView = ViewStack.InterfaceView(backdrop.View);
            return CreateMenu(panelView);
        }

        /// <summary>
        /// Reads which screen the still should be of.
        /// </summary>
        /// <returns>The screen named on the command line, or the map list.</returns>
        /// <remarks>
        /// The map list by default, because a menu photographed on its title screen is a
        /// picture of four buttons, and the list is the half of this feature that had to be
        /// built. A name nobody recognises answers with the default rather than with nothing,
        /// the same way <see cref="LevelNames"/> handles a word it does not know.
        /// </remarks>
        private static MenuPanel PanelFromCommandLine()
        {
            string named = CameraCapture.ValueFromCommandLine(PanelArgument, "levels");

            switch (named.Trim().ToLowerInvariant())
            {
                case "root":
                    return MenuPanel.Root;
                case "settings":
                    return MenuPanel.Settings;
                default:
                    return MenuPanel.Levels;
            }
        }

        /// <summary>
        /// Lights the scene the way a match is lit, with the haze pushed out past the island.
        /// </summary>
        /// <param name="distance">How far the camera stands back, in metres.</param>
        /// <remarks>
        /// The two overhead views drop the fog entirely, because from 200 metres straight up it
        /// is a grey sheet over the whole map. This one keeps it, scaled: the game's own numbers
        /// - 55 to 200 metres - were measured for a chase camera 34 metres off a jeep, and at
        /// this camera's range they put the far half of the frame in weather. Started at the
        /// point the camera is aimed at and ended well past the back of the frame, the haze does
        /// what it is actually for here, which is to sit the far shore back from the near one.
        /// </remarks>
        private static void LightScene(float distance)
        {
            LightingTuning lighting = LightingTuning.For(LightingMood.Daylight);
            lighting.FogStart = distance;
            lighting.FogEnd = distance * 2.2f;
            SceneLighting.Apply(LightingMood.Daylight, lighting);
        }

        /// <summary>
        /// Reads the level file and builds it into the scene, behind the menu.
        /// </summary>
        /// <returns>The map that was baked, or an empty one when the file could not be read.</returns>
        /// <remarks>
        /// A loader rather than loose geometry, so the map is rebuilt on the first frame of
        /// play out of whatever the file says now - which is what makes the menu show a map the
        /// player has since edited rather than the one that was baked in.
        /// </remarks>
        private static LevelDefinition BakeLevel()
        {
            LevelCatalog catalog = LevelCatalogBuilder.Load();

            string path = LevelLibrary.PathFor(LevelName);
            if (!LevelFile.TryRead(path, out LevelDefinition level, out string problem))
            {
                Debug.LogWarning($"{problem} The menu will stand on an empty map.");
                level = LevelEdits.Starter("Menu");
            }

            var host = new GameObject(LoaderName);
            LevelLoader loader = host.AddComponent<LevelLoader>();
            loader.Configure(LevelName, catalog);

            GameObject map = LevelBuilder.Build(level, catalog, InstantiatePrefab);
            map.transform.SetParent(host.transform, false);

            return level;
        }

        /// <summary>
        /// Turns the scene's camera into the slow orbit the menu stands in front of.
        /// </summary>
        /// <param name="level">The map, so the camera stands back far enough to hold it.</param>
        /// <returns>The backdrop driving the camera.</returns>
        private static MenuBackdrop CreateView(LevelDefinition level)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                var made = new GameObject("Camera", typeof(Camera), typeof(AudioListener));
                made.tag = "MainCamera";
                camera = made.GetComponent<Camera>();
            }

            camera.name = "Menu Camera";
            camera.cullingMask = InterfaceLayers.WorldMask();
            camera.farClipPlane = Mathf.Max(camera.farClipPlane, 1200.0f);

            MenuBackdrop backdrop = camera.gameObject.AddComponent<MenuBackdrop>();
            backdrop.Configure(level);

            ViewStack.MakeWorldView(camera);
            ViewStack.AttachInterfaceView(camera, InterfaceLayers.EditorLayer());

            return backdrop;
        }

        /// <summary>
        /// Creates the thing that turns a mouse into a button press.
        /// </summary>
        /// <param name="controls">The actions asset carrying the UI map.</param>
        private static void CreateEventSystem(InputActionAsset controls)
        {
            var host = new GameObject("Event System", typeof(EventSystem));
            var module = host.AddComponent<InputSystemUIInputModule>();

            if (controls != null && controls.FindActionMap(LevelEditorScene.UiActionMap, false) != null)
            {
                module.actionsAsset = controls;
            }
            else
            {
                Debug.LogWarning(
                    $"IronFlag: {VehicleSandboxScene.ActionsPath} has no "
                    + $"'{LevelEditorScene.UiActionMap}' action map, so the menu will not "
                    + "respond to the mouse.");
            }
        }

        /// <summary>
        /// Creates the canvas the menu is drawn on.
        /// </summary>
        /// <remarks>
        /// The menu itself is not built here. It is generated at runtime out of
        /// <see cref="LevelLibrary.Names"/> and the machine's own display modes, so the saved
        /// scene carries an empty canvas rather than a snapshot of the maps and resolutions
        /// that existed when somebody pressed a menu item.
        /// </remarks>
        private static MainMenuController CreateMenu(Camera view)
        {
            var host = new GameObject(
                "Main Menu", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster),
                typeof(MainMenuController));

            var menu = host.GetComponent<MainMenuController>();
            menu.Configure(view);
            return menu;
        }

        private static GameObject InstantiatePrefab(GameObject prefab)
            => (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    }
}
