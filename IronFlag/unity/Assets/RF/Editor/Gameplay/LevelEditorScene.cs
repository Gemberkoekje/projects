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
using IronFlag.UI;

namespace IronFlag.Editor.Gameplay
{
    /// <summary>
    /// Builds the scene a map is made in: an overhead view of a level, the panels that edit
    /// it, and a button that plays it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A scene of its own rather than a mode inside the game, which is the decision the rest
    /// of this shape follows from. The game scene is two players, two cameras, two HUDs and a
    /// match; none of that is any use while drawing a coastline, and all of it would have to
    /// be turned off and back on again. A separate scene starts with nothing to turn off.
    /// </para>
    /// <para>
    /// The cost of that choice is that playtesting is a scene load, so the map has to get
    /// across the gap - and it already can, because a map is a file. The editor saves into the
    /// player's own level folder, hands the name over through
    /// <see cref="LevelHandoff"/>, and the game reads it the way it reads any other map. That
    /// is M7's two-folder rule doing exactly the job it was built for.
    /// </para>
    /// <para>
    /// Generated rather than authored, like every other scene here, and it bakes a copy of the
    /// map for the same reason the game scene does: a scene that opened empty would be a scene
    /// nobody could look at, and the command-line would have nothing to photograph.
    /// </para>
    /// </remarks>
    public static class LevelEditorScene
    {
        /// <summary>Name of the object the map hangs off.</summary>
        private const string LoaderName = "Level Loader";

        /// <summary>Command-line switch naming the file <see cref="RenderToFile"/> writes.</summary>
        private const string OutputArgument = "-editorOutput";

        /// <summary>File <see cref="RenderToFile"/> writes when the switch is absent.</summary>
        private const string DefaultOutputFile = "level-editor.png";

        /// <summary>Name of the action map the editor's panels are clicked with.</summary>
        /// <remarks>
        /// The name Unity's own UI module looks for, and the reason the module can be wired up
        /// by handing it the whole asset. The controls asset is the project-wide one, so the
        /// Input System keeps a map of this name in it whether anything uses it or not - which
        /// is why nothing in this project has ever needed one until now, and why
        /// <see cref="EnsureUiActions"/> is a check rather than a step.
        /// </remarks>
        public const string UiActionMap = "UI";

        /// <summary>
        /// Rebuilds the level editor scene and saves it.
        /// </summary>
        [MenuItem("Tools/IronFlag/Build Level Editor Scene", false, 152)]
        public static void BuildAndSave()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Build();

            string path = LevelScenes.EditorPath;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), path);
            BuildScenes.Register();
            AssetDatabase.Refresh();
            Debug.Log($"IronFlag: level editor saved to {path}");
        }

        /// <summary>
        /// Builds the editor and writes a render of it to a PNG file.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Intended for <c>-executeMethod</c>. Pass <c>-editorOutput &lt;path&gt;</c> to choose
        /// the file and <c>-level &lt;name&gt;</c> to choose the map.
        /// </para>
        /// <para>
        /// The panels are generated at runtime, so nothing exists to photograph until this
        /// calls <c>Build</c> on them by hand - the same arrangement the split-screen still
        /// uses for the HUD, and for the same reason: a saved scene carrying a frozen copy of
        /// a panel would be a scene carrying numbers that were true once.
        /// </para>
        /// </remarks>
        public static void RenderToFile()
        {
            const int width = 1920;
            const int height = 1080;

            LevelEditorSession session = Build();
            string name = CameraCapture.ValueFromCommandLine("-level", LevelLibrary.DefaultLevel);

            if (LevelFile.TryRead(LevelLibrary.PathFor(name), out LevelDefinition level, out _))
            {
                // Named explicitly: Adopt does not infer a file name from the level it is
                // handed, so without this the toolbar's FILE field - and the still - would
                // go on showing the scene's baked default map's name even when -level asked
                // for a different one.
                session.Adopt(level, false, name);
            }

            // Everything the editor draws is sized against the view it is drawn in: the
            // panels lay themselves out in the canvas the camera gives them, the camera frames
            // the map in whatever those panels leave free, and the overlay puts its markers
            // where the camera says the world falls. In batch mode the game view is not the
            // shape of the still, so the camera is pointed at a texture of the right size
            // first and all three then agree. Without this the map is framed for one window
            // and photographed in another.
            var frame = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            session.View.View.targetTexture = frame;

            try
            {
                ShowPanels();

                // After the panels, because framing a map is done against the part of the view
                // they leave free - and they only know how much that is once they exist.
                session.View.Frame(session.Level);

                // Something selected, because an inspector with nothing in it is a picture of
                // a panel rather than of an editor. The first tower is the most interesting
                // thing a level has: it is the one placement in the format that carries a rule.
                if (level != null && level.Towers.Length > 0)
                {
                    session.Select(new EditSelection(EditTarget.Tower, 0));
                }

                RefreshPanels();

                CameraCapture.RenderToPng(
                    session.View.View,
                    width,
                    height,
                    CameraCapture.OutputPathFromCommandLine(OutputArgument, DefaultOutputFile));
            }
            finally
            {
                session.View.View.targetTexture = null;
                frame.Release();
                Object.DestroyImmediate(frame);
            }
        }

        /// <summary>
        /// Creates the scene contents: lighting, the map, the view, the panels and the editor.
        /// </summary>
        /// <returns>The editor session.</returns>
        public static LevelEditorSession Build()
        {
            GeneratedMaterials.EnsureAssets();
            InputActionAsset controls = EnsureUiActions();
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // The game's lighting, less the haze - the editor's camera looks straight down
            // from 200 metres, which is the range at which a chase camera's fog is a grey
            // sheet over the whole map. Everything else about the look is shared on purpose,
            // so a map is authored under the light it is played under.
            LightingTuning lighting = LightingTuning.For(LightingMood.Daylight);
            lighting.Fog = false;
            SceneLighting.Apply(LightingMood.Daylight, lighting);

            LevelDefinition level = BakeLevel(out LevelLoader loader);
            EditorCameraRig view = CreateView(level);
            CreateEventSystem(controls);

            var host = new GameObject("Level Editor");
            LevelEditorSession session = host.AddComponent<LevelEditorSession>();
            session.Configure(loader, view, VehicleSandboxScene.LevelName);

            // Both canvases draw through the stacked camera rather than the world one, so the
            // panels stay the colours EditorTheme picked instead of being tone-mapped along
            // with the map behind them - see IronFlag.Core.ViewStack.
            Camera panelView = ViewStack.InterfaceView(view.View);
            CreateOverlay(session, panelView);
            CreatePanels(session, panelView);

            return session;
        }

        /// <summary>
        /// Reads the level file, builds it into the scene, and leaves a loader pointed at it.
        /// </summary>
        /// <param name="loader">The loader that was created.</param>
        /// <returns>The level that was baked, or an empty one when the file could not be read.</returns>
        /// <remarks>
        /// The same call the game makes, with prefabs instantiated as prefabs so the saved
        /// scene keeps its links. Problems are logged here because this is the moment somebody
        /// is looking - they pressed a menu item - and are not logged again at runtime, where
        /// the editor shows them in a panel instead.
        /// </remarks>
        private static LevelDefinition BakeLevel(out LevelLoader loader)
        {
            LevelCatalog catalog = LevelCatalogBuilder.Load();
            string name = VehicleSandboxScene.LevelName;

            string path = LevelLibrary.PathFor(name);
            if (!LevelFile.TryRead(path, out LevelDefinition level, out string problem))
            {
                Debug.LogWarning($"{problem} The editor will open on an empty map.");
                level = LevelEdits.Starter("New Map");
            }

            foreach (string trouble in LevelValidation.Problems(level))
            {
                Debug.LogWarning($"IronFlag: {name} - {trouble}");
            }

            var host = new GameObject(LoaderName);
            loader = host.AddComponent<LevelLoader>();
            loader.Configure(name, catalog);

            GameObject map = LevelBuilder.Build(level, catalog, InstantiatePrefab);
            map.transform.SetParent(host.transform, false);

            return level;
        }

        /// <summary>
        /// Turns the scene's camera into the overhead view a map is drawn through.
        /// </summary>
        /// <param name="level">The map, so the view opens framed on it.</param>
        /// <returns>The rig driving the camera.</returns>
        /// <remarks>
        /// The scene's own main camera, so it keeps its tag and its audio listener - the same
        /// reason the game scene reuses it for the first seat.
        /// </remarks>
        private static EditorCameraRig CreateView(LevelDefinition level)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                var made = new GameObject("Camera", typeof(Camera), typeof(AudioListener));
                made.tag = "MainCamera";
                camera = made.GetComponent<Camera>();
            }

            camera.name = "Editor Camera";
            camera.cullingMask = InterfaceLayers.WorldMask();

            EditorCameraRig rig = camera.gameObject.AddComponent<EditorCameraRig>();
            rig.Configure(level.Bounds == null ? 120.0f : level.Bounds.HalfExtent);
            rig.Frame(level);

            // After Frame, so the camera drawing the panels copies a projection that has
            // already been sized to the map. It keeps that projection while this one zooms,
            // which is what stops the interface growing and shrinking with the map under it.
            ViewStack.MakeWorldView(camera);
            ViewStack.AttachInterfaceView(camera, InterfaceLayers.EditorLayer());

            return rig;
        }

        /// <summary>
        /// Creates the thing that turns a mouse into a button press.
        /// </summary>
        /// <param name="controls">The actions asset carrying the UI map.</param>
        /// <remarks>
        /// The first event system in this project. Nothing in the game is clicked - a HUD is
        /// read - so there has never been one, and its absence is exactly the kind of thing
        /// that presents as "the buttons do nothing" with nothing in the console.
        /// </remarks>
        private static void CreateEventSystem(InputActionAsset controls)
        {
            var host = new GameObject("Event System", typeof(EventSystem));
            var module = host.AddComponent<InputSystemUIInputModule>();

            if (controls != null && controls.FindActionMap(UiActionMap, false) != null)
            {
                // Setting the asset wires every action the module wants by name out of the
                // UI map, which is why that map has to exist and has to be called that.
                module.actionsAsset = controls;
            }
            else
            {
                Debug.LogWarning(
                    $"IronFlag: {VehicleSandboxScene.ActionsPath} has no '{UiActionMap}' action "
                    + "map, so the editor's panels will not respond to the mouse.");
            }
        }

        /// <summary>
        /// Creates the canvas the selection and drag markers are drawn on.
        /// </summary>
        private static void CreateOverlay(LevelEditorSession session, Camera view)
        {
            var host = new GameObject("Editor Overlay", typeof(Canvas), typeof(EditorOverlay));
            var overlay = host.GetComponent<EditorOverlay>();
            overlay.Configure(session, view);
        }

        /// <summary>
        /// Creates the canvas the panels are drawn on.
        /// </summary>
        /// <remarks>
        /// The panels themselves are not built here. They are generated at runtime from the
        /// palette and the rules, so the saved scene carries an empty canvas rather than a
        /// snapshot of six props and four problems that were true when somebody pressed a
        /// menu item.
        /// </remarks>
        private static void CreatePanels(LevelEditorSession session, Camera view)
        {
            var host = new GameObject(
                "Editor Ui", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster),
                typeof(EditorUi));

            var ui = host.GetComponent<EditorUi>();
            ui.Configure(session, view);
        }

        /// <summary>
        /// Generates the panels and brings them up to date, for the still.
        /// </summary>
        private static void ShowPanels()
        {
            foreach (EditorOverlay overlay in
                Object.FindObjectsByType<EditorOverlay>(FindObjectsInactive.Include))
            {
                overlay.Build();
            }

            foreach (EditorUi ui in Object.FindObjectsByType<EditorUi>(FindObjectsInactive.Include))
            {
                ui.Build();
            }

            RefreshPanels();
        }

        /// <summary>
        /// Brings the generated panels up to date and lays them out.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The layout pass that normally happens at the end of a frame never runs in a scene
        /// nobody is playing, so it has to be asked for - twice, and the order is the point.
        /// </para>
        /// <para>
        /// The panels measure their own canvas to work out how much of the view they cover,
        /// and the camera frames the map in what is left. A canvas that has never been laid
        /// out reports whatever size it was created at, which on the shipped map comes out as
        /// a view zoomed to its limit with the island in the wrong half of it. So: lay out,
        /// then refresh the panels against a canvas that knows its own size, then lay out
        /// again and put the markers on top.
        /// </para>
        /// </remarks>
        private static void RefreshPanels()
        {
            Canvas.ForceUpdateCanvases();

            foreach (EditorUi ui in Object.FindObjectsByType<EditorUi>(FindObjectsInactive.Include))
            {
                ui.Refresh();
            }

            Canvas.ForceUpdateCanvases();

            foreach (EditorOverlay overlay in
                Object.FindObjectsByType<EditorOverlay>(FindObjectsInactive.Include))
            {
                overlay.Refresh();
            }
        }

        /// <summary>
        /// Makes sure the controls asset has the action map the UI module reads.
        /// </summary>
        /// <returns>The controls asset, or <c>null</c> when it is missing.</returns>
        /// <remarks>
        /// <para>
        /// The same shape as the game scene's <c>EnsureInterfaceLayers</c>: a piece of project
        /// configuration the runtime cannot create for itself, written once by the generator
        /// from the same name the runtime looks it up by. In practice it finds the map already
        /// there - the Input System maintains one in the project-wide asset - so this normally
        /// does nothing, and exists for the case where somebody has taken it out.
        /// </para>
        /// <para>
        /// Added to the existing asset rather than a second one, because that asset is the
        /// project-wide input actions - so a module told to use anything else would be reading
        /// a different asset from every other thing in the game.
        /// </para>
        /// </remarks>
        internal static InputActionAsset EnsureUiActions()
        {
            string path = VehicleSandboxScene.ActionsPath;
            var controls = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);

            if (controls == null)
            {
                Debug.LogWarning($"IronFlag: {path} is missing; the editor will not respond.");
                return null;
            }

            if (controls.FindActionMap(UiActionMap, false) != null)
            {
                return controls;
            }

            InputActionMap map = controls.AddActionMap(UiActionMap);

            Pass(map, "Point", "Vector2", "<Mouse>/position");
            Pass(map, "ScrollWheel", "Vector2", "<Mouse>/scroll");
            Press(map, "Click", "<Mouse>/leftButton");
            Press(map, "MiddleClick", "<Mouse>/middleButton");
            Press(map, "RightClick", "<Mouse>/rightButton");
            Press(map, "Submit", "<Keyboard>/enter");
            Press(map, "Cancel", "<Keyboard>/escape");

            InputAction navigate = map.AddAction("Navigate", InputActionType.PassThrough);
            navigate.expectedControlType = "Vector2";
            navigate.AddBinding("<Gamepad>/dpad");
            navigate.AddBinding("<Gamepad>/leftStick");

            File.WriteAllText(Path.GetFullPath(path), controls.ToJson());
            AssetDatabase.ImportAsset(path);
            Debug.Log($"IronFlag: added a '{UiActionMap}' action map to {path}.");

            return AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
        }

        private static void Pass(InputActionMap map, string name, string type, string path)
        {
            InputAction action = map.AddAction(name, InputActionType.PassThrough);
            action.expectedControlType = type;
            action.AddBinding(path);
        }

        private static void Press(InputActionMap map, string name, string path)
        {
            InputAction action = map.AddAction(name, InputActionType.PassThrough, path);
            action.expectedControlType = "Button";
        }

        private static GameObject InstantiatePrefab(GameObject prefab)
            => (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    }
}
