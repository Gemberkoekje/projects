using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using IronFlag.Destruction;
using IronFlag.Editing;
using IronFlag.Levels;

namespace IronFlag.Tests.PlayMode
{
    /// <summary>
    /// Drives the level editor through a virtual mouse and its real, generated buttons -
    /// the two paths <c>LevelEditorTests.cs</c> never touches, because it calls
    /// <see cref="LevelEditorSession"/>'s own methods directly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Calling <c>session.Commit(...)</c> by hand proves the arithmetic is right; it proves
    /// nothing about <see cref="LevelEditorSession.Update"/>'s reading of the mouse, or about
    /// whether a generated <see cref="Button"/> is wired to the action its caption claims.
    /// Both of those are exactly where three of the bugs this file guards against were found:
    /// a right-click pan that silently ate a drag in progress, a stationary click that
    /// silently relocated an off-grid object, and a confirmation button that, once pressed
    /// once, stayed spent no matter how much unrelated work happened before it was pressed
    /// again.
    /// </para>
    /// <para>
    /// <see cref="InputTestFixture"/> swaps the whole input system out for an empty one and a
    /// virtual mouse is added to it, the same arrangement <c>SplitScreenTests</c> uses for two
    /// virtual gamepads. Full state events are queued rather than built with the fixture's own
    /// <c>Set</c>/<c>Press</c> helpers, because those build delta events against the control's
    /// current-state pointer, and that pointer is null under the batch-mode test runner.
    /// </para>
    /// </remarks>
    public sealed class LevelEditorInputTests : InputTestFixture
    {
        /// <summary>Name of the scene these tests play, as the scene manager knows it.</summary>
        private const string EditorSceneName = "LevelEditor";

        /// <summary>Name a saved map is written under, and deleted again.</summary>
        private const string ScratchLevel = "editor-input-test-scratch";

        private Mouse mouse;

        /// <summary>
        /// Empties the input system and plugs in one virtual mouse.
        /// </summary>
        public override void Setup()
        {
            base.Setup();
            mouse = InputSystem.AddDevice<Mouse>();
        }

        /// <summary>
        /// Takes the editor back out of the world, and deletes the map it saved.
        /// </summary>
        /// <returns>The coroutine the framework steps through.</returns>
        [UnityTearDown]
        public IEnumerator LeaveNoEditorOrFileBehind()
        {
            LevelHandoff.Clear();

            string written = LevelLibrary.UserPathFor(ScratchLevel);
            if (File.Exists(written))
            {
                File.Delete(written);
            }

            Scene editor = SceneManager.GetSceneByName(EditorSceneName);
            if (!editor.IsValid() || !editor.isLoaded)
            {
                yield break;
            }

            SceneManager.SetActiveScene(SceneManager.CreateScene("AfterLevelEditorInput"));
            yield return SceneManager.UnloadSceneAsync(editor);
        }

        /// <summary>
        /// Panning with the right button never abandons a drag the left button already has
        /// hold of - the fix for a bug where reaching for the right button mid-drag silently
        /// discarded whatever was being moved, with no commit and no message.
        /// </summary>
        [UnityTest]
        public IEnumerator PanningWithTheRightButtonDoesNotAbandonADragInProgress()
        {
            yield return OpenTheEditor();

            LevelEditorSession session = Session();
            Vector3 start = session.Level.Towers[0].Position;
            Vector3 target = start + new Vector3(9.0f, 0.0f, 6.0f);

            Vector2 fromScreen = ScreenPoint(session, start);
            Vector2 toScreen = ScreenPoint(session, target);

            // Press the left button on the tower - it selects and starts carrying it.
            Hold(At(fromScreen, left: true));
            yield return Settled();
            Assert.That(session.Selection.Target, Is.EqualTo(EditTarget.Tower), "the tower was not picked up");

            // Reach for the right button without letting go of the left - a normal two-hand
            // gesture with a scroll-wheel mouse. Under the bug this switched the state
            // machine to panning and the drag was never seen again.
            Hold(At(fromScreen, left: true, right: true));
            yield return Settled();

            // Let go of the right button and actually move the mouse.
            Hold(At(toScreen, left: true));
            yield return Settled();

            // Release the left button - this is where the move used to be lost.
            Hold(At(toScreen));
            yield return Settled();

            Vector3 moved = session.Level.Towers[0].Position;
            Assert.That(
                Vector3.Distance(moved, start),
                Is.GreaterThan(3.0f),
                "the drag was abandoned when the right button was pressed mid-move");
            Assert.That(session.IsDirty, Is.True, "the move did not commit");
        }

        /// <summary>
        /// Panning still works when nothing is being dragged - the fix above must not have
        /// made panning itself harder to start.
        /// </summary>
        [UnityTest]
        public IEnumerator PanningStillWorksWhenNothingIsSelected()
        {
            yield return OpenTheEditor();

            LevelEditorSession session = Session();
            session.Select(EditSelection.Nothing);
            Vector3 before = session.View.Focus;

            var from = new Vector2(400.0f, 300.0f);
            var to = new Vector2(250.0f, 220.0f);

            Hold(At(from, right: true));
            yield return Settled();
            Hold(At(to, right: true, delta: to - from));
            yield return Settled();
            Hold(At(to));
            yield return Settled();

            Assert.That(
                Vector3.Distance(session.View.Focus, before),
                Is.GreaterThan(0.01f),
                "an ordinary right-drag no longer pans the view");
        }

        /// <summary>
        /// A click that never moves the mouse must not relocate the thing it selected, even
        /// when that thing is sitting off the active grid - which is exactly what typing a
        /// coordinate into the inspector produces, and what dragging never would.
        /// </summary>
        [UnityTest]
        public IEnumerator AStationaryClickOnAnOffGridObjectDoesNotMoveIt()
        {
            yield return OpenTheEditor();

            LevelEditorSession session = Session();
            EditSelection target = PlaceTestTarget(session);

            // A fractional offset is off any grid step this editor offers (0.5, 1, 2, 5 m).
            Vector3 offGrid = LevelEdits.PositionOf(session.Level, target)
                + new Vector3(0.37f, 0.0f, 0.61f);
            LevelEdits.MoveTo(session.Level, target, offGrid);
            session.Commit("moved off-grid for a test");
            yield return null;

            string before = LevelFile.ToJson(session.Level);
            Vector2 screen = ScreenPoint(session, offGrid);

            Hold(At(screen, left: true));
            yield return Settled();

            // The button is still down and the mouse has not moved - exactly the frame that
            // used to run the active grid over the object's real position regardless.
            Hold(At(screen, left: true));
            yield return Settled();

            Hold(At(screen));
            yield return Settled();

            Assert.That(
                LevelFile.ToJson(session.Level),
                Is.EqualTo(before),
                "a click that never moved the mouse changed the map");
            Assert.That(
                LevelEdits.PositionOf(session.Level, target),
                Is.EqualTo(offGrid).Using(new PositionComparer(0.001f)),
                "a stationary click snapped an off-grid object onto the grid");
        }

        /// <summary>
        /// A genuine drag on the same object still commits normally - the click guard above
        /// must not have made real drags stop working.
        /// </summary>
        [UnityTest]
        public IEnumerator ADragThatActuallyMovesTheMouseStillCommits()
        {
            yield return OpenTheEditor();

            LevelEditorSession session = Session();
            EditSelection target = PlaceTestTarget(session);
            Vector3 start = LevelEdits.PositionOf(session.Level, target);
            Vector3 goal = start + new Vector3(7.0f, 0.0f, -5.0f);

            Hold(At(ScreenPoint(session, start), left: true));
            yield return Settled();
            Hold(At(ScreenPoint(session, goal), left: true));
            yield return Settled();
            Hold(At(ScreenPoint(session, goal)));
            yield return Settled();

            Assert.That(
                Vector3.Distance(LevelEdits.PositionOf(session.Level, target), start),
                Is.GreaterThan(3.0f),
                "a real drag no longer moves the object");
        }

        /// <summary>
        /// A second press of New, made after fresh edits, asks again instead of discarding
        /// them - the fix for a guard that stayed armed forever once pressed the first time.
        /// </summary>
        [UnityTest]
        public IEnumerator PressingNewAgainAfterFreshEditsAsksOnceMoreRatherThanDiscarding()
        {
            yield return OpenTheEditor();

            LevelEditorSession session = Session();
            EditorUi ui = Object.FindAnyObjectByType<EditorUi>();
            Button newButton = FindButton(ui, "New");
            string originalName = session.Level.Name;

            LevelEdits.AddStructure(session.Level, StructureKind.Tree, SomewhereOnLand(session.Level));
            session.Commit("dirtied for a test");
            yield return null;

            newButton.onClick.Invoke();
            yield return null;
            Assert.That(session.Level.Name, Is.EqualTo(originalName), "New ran on the very first press");

            LevelEdits.AddStructure(session.Level, StructureKind.Tree, SomewhereOnLand(session.Level));
            session.Commit("more edits made after the first press, before confirming");
            yield return null;

            newButton.onClick.Invoke();
            yield return null;
            Assert.That(
                session.Level.Name,
                Is.EqualTo(originalName),
                "a stale confirmation discarded edits made after it was armed");

            newButton.onClick.Invoke();
            yield return null;
            Assert.That(session.Level.Name, Is.EqualTo("New Map"), "New still never ran on a genuine repeat press");
        }

        /// <summary>
        /// Picking a map out of the open-file list asks first if the current one has become
        /// dirty since the list was raised - the fix for a row that discarded work behind the
        /// dialog with no warning, even though the button that opened the dialog was guarded.
        /// </summary>
        [UnityTest]
        public IEnumerator PickingAMapRowAsksIfEditsHappenedWhileThePanelWasOpen()
        {
            yield return OpenTheEditor();

            LevelEditorSession session = Session();
            Assert.That(session.SaveAs(ScratchLevel), Is.True);
            EditorUi ui = Object.FindAnyObjectByType<EditorUi>();

            FindButton(ui, "Open").onClick.Invoke();
            yield return null;
            Assert.That(ui.OpenPanel.gameObject.activeSelf, Is.True, "the open panel did not appear on a clean map");

            session.Select(new EditSelection(EditTarget.Structure, 0));
            session.DeleteSelected();
            yield return null;
            Assert.That(session.IsDirty, Is.True, "the deletion behind the panel was not recorded");

            string originalName = session.LevelName;
            Button row = FindOpenRow(ui, LevelLibrary.DefaultLevel);

            row.onClick.Invoke();
            yield return null;
            Assert.That(
                session.LevelName,
                Is.EqualTo(originalName),
                "opening another map discarded a deletion made while the dialog was open");

            row.onClick.Invoke();
            yield return null;
            Assert.That(session.LevelName, Is.EqualTo(LevelLibrary.DefaultLevel));
        }

        /// <summary>
        /// Typing a new name into the FILE field and pressing enter saves under it, proving
        /// the field is actually wired to <see cref="LevelEditorSession.SaveAs"/> rather than
        /// just existing on screen.
        /// </summary>
        [UnityTest]
        public IEnumerator TypingANewFileNameAndPressingEnterSavesUnderIt()
        {
            yield return OpenTheEditor();

            LevelEditorSession session = Session();
            EditorUi ui = Object.FindAnyObjectByType<EditorUi>();
            InputField file = FindField(ui, "File");

            file.text = ScratchLevel;
            file.onEndEdit.Invoke(ScratchLevel);
            yield return null;

            Assert.That(session.LevelName, Is.EqualTo(ScratchLevel));
            Assert.That(
                File.Exists(LevelLibrary.UserPathFor(ScratchLevel)),
                Is.True,
                "typing a new name into the FILE field did not save under it");
        }

        /// <summary>
        /// A frame and a half - one for the input system to deliver a queued state, and a
        /// second so <see cref="LevelEditorSession.Update"/> is guaranteed to have read it at
        /// least once. A single <c>yield return null</c> occasionally left the last of a
        /// short burst of queued states undelivered by the time an assertion ran, under
        /// batch mode's own frame-timing jitter - a false failure in the test rather than a
        /// real one in the editor, but a real one all the same until it stopped happening.
        /// </summary>
        private static IEnumerator Settled()
        {
            yield return null;
            yield return null;
        }

        private static IEnumerator OpenTheEditor()
        {
            LevelHandoff.Clear();
            SceneManager.LoadScene(EditorSceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        private static LevelEditorSession Session()
        {
            var session = Object.FindAnyObjectByType<LevelEditorSession>();
            Assert.That(session, Is.Not.Null, "the editor scene has no editor in it");
            return session;
        }

        /// <summary>
        /// Places a fresh tree near the middle of the map and returns it, selected.
        /// </summary>
        /// <remarks>
        /// Deliberately not an existing prop off in a corner of the map, such as a bridge
        /// out near the coast: <see cref="EditorCameraRig.Frame"/> centres the whole map in
        /// whatever the panels leave free, but the test runner's own window can be small
        /// enough that something near an edge of a wide map projects to a screen point still
        /// under the tool column - which reads as "nothing was clicked" rather than as a
        /// failure, and made an earlier version of this file pass for the wrong reason. The
        /// middle of the map is never under a panel at any window size the panels are laid
        /// out for.
        /// </remarks>
        private static EditSelection PlaceTestTarget(LevelEditorSession session)
        {
            int placed = LevelEdits.AddStructure(session.Level, StructureKind.Tree, Vector3.zero);
            Assert.That(placed, Is.GreaterThanOrEqualTo(0));
            session.Commit("placed a target for a test");
            return new EditSelection(EditTarget.Structure, placed);
        }

        private static Vector2 ScreenPoint(LevelEditorSession session, Vector3 world)
        {
            Vector3 point = session.View.View.WorldToScreenPoint(world);
            return new Vector2(point.x, point.y);
        }

        /// <summary>
        /// Finds a spot on the map with room around it, read off the level rather than
        /// written down, so this still works after somebody moves the coastline.
        /// </summary>
        private static Vector3 SomewhereOnLand(LevelDefinition level)
        {
            foreach (LevelLand piece in level.Land)
            {
                if (piece != null && piece.IsDrawn
                    && piece.Width > LevelValidation.ShoreMargin * 4.0f
                    && piece.Depth > LevelValidation.ShoreMargin * 4.0f)
                {
                    return piece.Centre;
                }
            }

            return Vector3.zero;
        }

        private static Button FindButton(EditorUi ui, string objectName)
        {
            foreach (Button button in ui.GetComponentsInChildren<Button>(true))
            {
                if (button.name == objectName)
                {
                    return button;
                }
            }

            Assert.Fail($"no '{objectName}' button was generated");
            return null;
        }

        private static InputField FindField(EditorUi ui, string objectName)
        {
            foreach (InputField field in ui.GetComponentsInChildren<InputField>(true))
            {
                if (field.name == objectName)
                {
                    return field;
                }
            }

            Assert.Fail($"no '{objectName}' field was generated");
            return null;
        }

        private static Button FindOpenRow(EditorUi ui, string levelName)
        {
            foreach (Button button in ui.OpenPanel.GetComponentsInChildren<Button>(true))
            {
                Text label = button.GetComponentInChildren<Text>();
                if (label != null && label.text == levelName)
                {
                    return button;
                }
            }

            Assert.Fail($"no row for '{levelName}' is showing in the open panel");
            return null;
        }

        /// <summary>
        /// Queues a full mouse state for the input system to deliver at the top of the next
        /// frame, rather than applying it now - which is what lets a single-frame press be
        /// seen as pressed <em>this</em> frame by whatever reads it in <c>Update</c>.
        /// </summary>
        private void Hold(MouseState state) => InputSystem.QueueStateEvent(mouse, state);

        /// <remarks>
        /// <c>delta</c> is a field of its own on <see cref="MouseState"/>, not something the
        /// input system derives from successive <c>position</c> values - a real mouse driver
        /// reports both, and a queued state that only sets <c>position</c> reads as zero
        /// delta forever. Only the panning tests need it; a drag reads the absolute pointer
        /// position instead and never touches it.
        /// </remarks>
        private static MouseState At(
            Vector2 position, bool left = false, bool right = false, Vector2 delta = default)
        {
            MouseState state = new MouseState { position = position, delta = delta };
            if (left)
            {
                state = state.WithButton(MouseButton.Left);
            }

            if (right)
            {
                state = state.WithButton(MouseButton.Right);
            }

            return state;
        }

        private sealed class PositionComparer : System.Collections.Generic.IEqualityComparer<Vector3>
        {
            private readonly float slack;

            public PositionComparer(float within) => slack = within;

            public bool Equals(Vector3 left, Vector3 right) => Vector3.Distance(left, right) <= slack;

            public int GetHashCode(Vector3 at) => at.GetHashCode();
        }
    }
}
