using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using IronFlag.Core;
using IronFlag.Editing;
using IronFlag.Editor.Gameplay;
using IronFlag.Levels;
using IronFlag.UI;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// Checks that the generated editor scene is an editor rather than a scene full of
    /// objects that happen to compile.
    /// </summary>
    /// <remarks>
    /// Every one of these is a wiring failure that presents as silence. A missing event
    /// system is "the buttons do nothing"; a scene that is not in the build list is "the Play
    /// button does nothing"; a UI action map under the wrong name is "the mouse does nothing".
    /// None of them log anything, and all of them look exactly like a scene that has not
    /// finished loading.
    /// </remarks>
    public sealed class LevelEditorWiringTests
    {
        [Test]
        public void TheEditorSceneIsWhereEverythingExpectsItToBe()
        {
            Assert.That(
                AssetDatabase.LoadAssetAtPath<SceneAsset>(LevelScenes.EditorPath),
                Is.Not.Null,
                $"{LevelScenes.EditorPath} is missing; run Tools > IronFlag > Build Level Editor Scene");
        }

        /// <summary>
        /// Both scenes have to be listed, because <c>SceneManager.LoadScene</c> can only reach
        /// a scene in the build - and the round trip between them is the whole feature.
        /// </summary>
        [Test]
        public void BothScenesAreInTheBuildWithTheGameFirst()
        {
            var listed = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                {
                    listed.Add(scene.path);
                }
            }

            Assert.That(listed, Contains.Item(LevelScenes.GamePath));
            Assert.That(listed, Contains.Item(LevelScenes.EditorPath));
            Assert.That(
                listed.IndexOf(LevelScenes.GamePath),
                Is.LessThan(listed.IndexOf(LevelScenes.EditorPath)),
                "a built copy would start in the level editor rather than in the game");
        }

        [Test]
        public void TheEditorSceneHasAMapAndAViewAndSomethingToEditWith()
        {
            using (var editor = new OpenEditor())
            {
                Assert.That(editor.One<LevelLoader>(), Is.Not.Null, "the editor has no map");
                Assert.That(editor.One<EditorCameraRig>(), Is.Not.Null, "the editor has no view");

                LevelEditorSession session = editor.One<LevelEditorSession>();
                Assert.That(session, Is.Not.Null, "the editor has no editor in it");
                Assert.That(session.View, Is.Not.Null, "the editor is not wired to its camera");
            }
        }

        /// <summary>
        /// The map is baked into the scene for the same reason the game's is: a scene that
        /// opened empty would be one nobody could look at, and the still would have nothing to
        /// photograph. It is thrown away and rebuilt on the first frame of play.
        /// </summary>
        [Test]
        public void TheEditorSceneCarriesABakedCopyOfTheMap()
        {
            using (var editor = new OpenEditor())
            {
                LevelLoader loader = editor.One<LevelLoader>();
                Assert.That(
                    loader.transform.childCount,
                    Is.GreaterThan(0),
                    "the editor scene has no map baked into it");

                Assert.That(
                    LevelFile.TryRead(
                        LevelLibrary.PathFor(loader.LevelName),
                        out LevelDefinition level,
                        out string problem),
                    Is.True,
                    problem);

                Assert.That(
                    editor.All<IronFlag.Objective.FlagTower>().Count,
                    Is.EqualTo(level.Towers.Length),
                    "the baked map does not match the file the loader is pointed at");
            }
        }

        [Test]
        public void TheEditorSceneCanBeClickedOn()
        {
            using (var editor = new OpenEditor())
            {
                Assert.That(
                    editor.One<EventSystem>(),
                    Is.Not.Null,
                    "the editor has no event system, so nothing on it can be pressed");
                Assert.That(
                    editor.One<InputSystemUIInputModule>(),
                    Is.Not.Null,
                    "the event system has no input module for the Input System");

                EditorUi ui = editor.One<EditorUi>();
                Assert.That(ui, Is.Not.Null, "the editor has no panels");
                Assert.That(ui.Session, Is.Not.Null, "the panels are not wired to the editor");
                Assert.That(
                    ui.GetComponent<GraphicRaycaster>(),
                    Is.Not.Null,
                    "the panel canvas cannot be clicked");
            }
        }

        /// <summary>
        /// An overlay canvas would be drawn after every camera and therefore appear in nothing
        /// rendered to a texture, which is what the command-line still does.
        /// </summary>
        /// <remarks>
        /// Which is why the panels moved onto a camera stacked on the editor's own rather than
        /// becoming overlay canvases when post-processing arrived: they still render through a
        /// camera, so they still land in a still, but the camera drawing them skips the grade.
        /// See <see cref="ViewStack"/>.
        /// </remarks>
        [Test]
        public void BothEditorCanvasesAreDrawnByTheEditorsOwnCamera()
        {
            using (var editor = new OpenEditor())
            {
                Camera world = editor.One<EditorCameraRig>().View;
                Camera view = ViewStack.Existing(world);
                List<Canvas> canvases = editor.All<Canvas>();

                Assert.That(view, Is.Not.Null, "the editor has no camera for its panels");
                Assert.That(
                    view.GetUniversalAdditionalCameraData().renderPostProcessing,
                    Is.False,
                    "the panels would be tone-mapped along with the map");
                Assert.That(
                    world.cullingMask & (1 << InterfaceLayers.EditorLayer()),
                    Is.Zero,
                    "the map camera would draw the panels a second time, through the grade");

                // A count first, or a future change that drops the overlay's or the panels'
                // Canvas entirely - renaming the object, forgetting the component - passes
                // this test by leaving nothing for the loop below to check.
                Assert.That(canvases.Count, Is.EqualTo(2), "the editor should have two canvases");

                foreach (Canvas canvas in canvases)
                {
                    Assert.That(
                        canvas.renderMode,
                        Is.EqualTo(RenderMode.ScreenSpaceCamera),
                        $"{canvas.name} would not appear in a rendered still");
                    Assert.That(
                        canvas.worldCamera,
                        Is.EqualTo(view),
                        $"{canvas.name} is drawn in front of the wrong camera");
                }
            }
        }

        /// <summary>
        /// The markers must never take a click the map wanted, which is what a raycaster on
        /// the overlay canvas would do.
        /// </summary>
        [Test]
        public void TheOverlayDoesNotSwallowClicks()
        {
            using (var editor = new OpenEditor())
            {
                EditorOverlay overlay = editor.One<EditorOverlay>();
                Assert.That(overlay, Is.Not.Null, "the editor has no overlay");
                Assert.That(
                    overlay.GetComponent<GraphicRaycaster>(),
                    Is.Null,
                    "the selection markers are taking the mouse");
            }
        }

        /// <summary>
        /// The names Unity's own UI module looks actions up by. A renamed one is a mouse that
        /// silently does nothing.
        /// </summary>
        [Test]
        public void TheControlsCarryTheUiActionsThePanelsAreClickedWith()
        {
            var controls = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                VehicleSandboxScene.ActionsPath);
            Assert.That(controls, Is.Not.Null, VehicleSandboxScene.ActionsPath);

            InputActionMap map = controls.FindActionMap(LevelEditorScene.UiActionMap, false);
            Assert.That(map, Is.Not.Null, LevelEditorScene.UiActionMap);

            foreach (string action in new[] { "Point", "Click", "ScrollWheel", "Submit", "Cancel" })
            {
                Assert.That(map.FindAction(action, false), Is.Not.Null, action);
            }
        }

        /// <summary>
        /// The way back out of a playtest. Without it, playing a map you have just made is a
        /// one-way trip and the iteration loop this whole feature is about stops after one go.
        /// </summary>
        [Test]
        public void TheGameSceneCarriesTheWayBackToTheEditor()
        {
            Scene game = EditorSceneManager.OpenScene(
                LevelScenes.GamePath, OpenSceneMode.Additive);

            try
            {
                var found = new List<PlaytestReturn>();
                foreach (GameObject root in game.GetRootGameObjects())
                {
                    found.AddRange(root.GetComponentsInChildren<PlaytestReturn>(true));
                }

                Assert.That(found.Count, Is.EqualTo(1), "the game scene has no way back");
            }
            finally
            {
                EditorSceneManager.CloseScene(game, true);
            }
        }

        /// <summary>
        /// The scene generator constructs <c>EditorUi</c> with
        /// <c>new GameObject(name, ..., typeof(EditorUi))</c>, which fires <c>Awake</c>
        /// synchronously, before the very next line can call <c>Configure</c>. Awake used to
        /// build the panels unconditionally and threw reaching the inspector, which reads the
        /// still-null session - so every regeneration of the scene logged an exception and
        /// left the status bar and the open-map dialog never built, though the saved file
        /// self-healed the next time anything actually loaded it (a fresh instance restores
        /// its serialized session before Awake runs at all). This reproduces the exact
        /// construction order the generator uses, without needing a whole scene regeneration
        /// to prove it no longer throws and no longer leaves anything half-built.
        /// </summary>
        [Test]
        public void ThePanelsAreFullyBuiltEvenThoughAwakeFiresBeforeConfigure()
        {
            var sessionHost = new GameObject("Test Session", typeof(LevelEditorSession));
            var cameraHost = new GameObject("Test Camera", typeof(Camera), typeof(EditorCameraRig));
            GameObject uiHost = null;

            try
            {
                var session = sessionHost.GetComponent<LevelEditorSession>();
                var rig = cameraHost.GetComponent<EditorCameraRig>();

                uiHost = new GameObject(
                    "Test Ui", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster),
                    typeof(EditorUi));
                var ui = uiHost.GetComponent<EditorUi>();

                ui.Configure(session, rig.View);

                Assert.That(ui.IsBuilt, Is.True, "Configure did not complete the panels");
                Assert.That(ui.OpenPanel, Is.Not.Null, "the open-map panel was never built");
                Assert.That(ui.MakePanel, Is.Not.Null, "the generate panel was never built");
                Assert.That(
                    ui.MakePanel.gameObject.activeSelf, Is.False,
                    "the generate panel is up before anybody asked for it");
            }
            finally
            {
                if (uiHost != null)
                {
                    Object.DestroyImmediate(uiHost);
                }

                Object.DestroyImmediate(cameraHost);
                Object.DestroyImmediate(sessionHost);
            }
        }

        /// <summary>
        /// Opens the generated editor scene alongside whatever is already up, and closes it
        /// again afterwards.
        /// </summary>
        private sealed class OpenEditor : System.IDisposable
        {
            private readonly Scene scene;

            public OpenEditor()
                => scene = EditorSceneManager.OpenScene(
                    LevelScenes.EditorPath, OpenSceneMode.Additive);

            public List<T> All<T>()
                where T : Component
            {
                var found = new List<T>();
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    found.AddRange(root.GetComponentsInChildren<T>(true));
                }

                return found;
            }

            public T One<T>()
                where T : Component
            {
                List<T> found = All<T>();
                return found.Count == 0 ? null : found[0];
            }

            public void Dispose() => EditorSceneManager.CloseScene(scene, true);
        }
    }
}
