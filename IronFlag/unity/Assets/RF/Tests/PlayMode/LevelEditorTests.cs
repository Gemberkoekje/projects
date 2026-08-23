using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using IronFlag.Core;
using IronFlag.Destruction;
using IronFlag.Editing;
using IronFlag.Levels;
using IronFlag.Objective;

namespace IronFlag.Tests.PlayMode
{
    /// <summary>
    /// The level editor, running: a map opened, changed, undone and saved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The arithmetic of editing has its own tests and needs no scene. What needs one is the
    /// claim the whole feature rests on - that a change to the definition becomes a change to
    /// the <em>world</em>, through exactly the same builder the game loads a map with. An
    /// editor whose panel says a tree is there and whose map does not have one is worse than
    /// no editor.
    /// </para>
    /// <para>
    /// These write real files into <see cref="LevelLibrary.UserFolder"/>, which outlives the
    /// test run, so they clean up after themselves. A leftover would shadow a shipped map for
    /// every later run - which is exactly the mechanism being tested.
    /// </para>
    /// </remarks>
    public sealed class LevelEditorTests
    {
        /// <summary>Name of the scene these tests play, as the scene manager knows it.</summary>
        private const string EditorSceneName = "LevelEditor";

        /// <summary>Name a saved map is written under, and deleted again.</summary>
        private const string ScratchLevel = "editor-test-scratch";

        /// <summary>Name of the game scene, as the scene manager knows it.</summary>
        private const string GameSceneName = "Sandbox";

        /// <summary>
        /// Takes the editor - and anything it played - back out of the world, and deletes the
        /// map it saved.
        /// </summary>
        /// <returns>The coroutine the framework steps through.</returns>
        /// <remarks>
        /// The file matters as much as the scene. A level written into
        /// <see cref="LevelLibrary.UserFolder"/> outlives the editor, and a leftover would
        /// shadow a shipped map for every later run - which is exactly the mechanism these
        /// tests are checking.
        /// </remarks>
        [UnityTearDown]
        public IEnumerator LeaveNoEditorOrFileBehind()
        {
            LevelHandoff.Clear();

            string written = LevelLibrary.UserPathFor(ScratchLevel);
            if (File.Exists(written))
            {
                File.Delete(written);
            }

            foreach (string name in new[] { EditorSceneName, GameSceneName })
            {
                Scene loaded = SceneManager.GetSceneByName(name);
                if (!loaded.IsValid() || !loaded.isLoaded)
                {
                    continue;
                }

                SceneManager.SetActiveScene(SceneManager.CreateScene($"After{name}"));
                yield return SceneManager.UnloadSceneAsync(loaded);
            }
        }

        /// <summary>
        /// The editor opens on the map the loader has already built, rather than reading the
        /// file a second time and quietly working on a different copy of it.
        /// </summary>
        [UnityTest]
        public IEnumerator TheEditorOpensOnTheMapThatIsUp()
        {
            yield return OpenTheEditor();

            LevelEditorSession session = Session();
            var loader = Object.FindAnyObjectByType<LevelLoader>();

            Assert.That(session.Level, Is.Not.Null, "the editor opened on nothing");
            Assert.That(
                session.Level,
                Is.SameAs(loader.Shown),
                "the editor is working on a different copy of the map from the one on screen");
            Assert.That(session.LevelName, Is.Not.Empty);
            Assert.That(session.IsDirty, Is.False, "a map opens with unsaved changes on it");
        }

        /// <summary>
        /// Placing something puts it on the map, through the same builder the game loads with.
        /// </summary>
        [UnityTest]
        public IEnumerator PlacingAPropPutsItInTheWorld()
        {
            yield return OpenTheEditor();

            LevelEditorSession session = Session();
            int before = Scenery();

            int placed = LevelEdits.AddStructure(
                session.Level, StructureKind.Tree, SomewhereOnLand(session.Level));
            Assert.That(placed, Is.GreaterThanOrEqualTo(0));
            session.Commit("placed for a test");
            yield return null;

            Assert.That(Scenery(), Is.EqualTo(before + 1), "the tree is in the file but not on the map");
            Assert.That(session.IsDirty, Is.True, "an edit did not mark the map unsaved");
        }

        /// <summary>
        /// Undo puts the world back, not just the file - which is what makes the history worth
        /// having rather than a list of maps nobody can see.
        /// </summary>
        [UnityTest]
        public IEnumerator UndoTakesItBackOffTheMap()
        {
            yield return OpenTheEditor();

            LevelEditorSession session = Session();
            int before = Scenery();

            LevelEdits.AddStructure(
                session.Level, StructureKind.Tree, SomewhereOnLand(session.Level));
            session.Commit("placed for a test");
            yield return null;

            Assert.That(session.CanUndo, Is.True, "there is nothing to undo after an edit");
            Assert.That(session.Undo(), Is.True);
            yield return null;

            Assert.That(Scenery(), Is.EqualTo(before), "undo left the tree standing");

            Assert.That(session.Redo(), Is.True);
            yield return null;
            Assert.That(Scenery(), Is.EqualTo(before + 1), "redo did not put the tree back");
        }

        /// <summary>
        /// Deleting the selection takes it out of the world and leaves nothing selected, so
        /// the inspector cannot go on offering to move something that is gone.
        /// </summary>
        [UnityTest]
        public IEnumerator DeletingTheSelectionTakesItOutOfTheWorld()
        {
            yield return OpenTheEditor();

            LevelEditorSession session = Session();
            int before = Scenery();

            session.Select(new EditSelection(EditTarget.Structure, 0));
            Assert.That(session.DeleteSelected(), Is.True);
            yield return null;

            Assert.That(Scenery(), Is.EqualTo(before - 1));
            Assert.That(session.Selection.IsSomething, Is.False, "the deleted thing is still selected");
        }

        /// <summary>
        /// Moving a tower moves the tower on the map, which is the drag path's whole claim.
        /// </summary>
        [UnityTest]
        public IEnumerator MovingATowerMovesItInTheWorld()
        {
            yield return OpenTheEditor();

            LevelEditorSession session = Session();
            var picked = new EditSelection(EditTarget.Tower, 0);
            Team side = session.Level.Towers[0].Side;
            bool real = session.Level.Towers[0].HoldsTheFlag;

            Vector3 to = LevelEdits.PositionOf(session.Level, picked) + new Vector3(6.0f, 0.0f, 4.0f);
            Assert.That(LevelEdits.MoveTo(session.Level, picked, to), Is.True);
            session.Commit("moved for a test");
            yield return null;

            FlagTower moved = real ? FlagTower.RealFor(side) : NearestTower(to);
            Assert.That(moved, Is.Not.Null, "the tower is gone from the map");
            Assert.That(
                moved.transform.position,
                Is.EqualTo(to).Using(new PositionComparer(0.01f)),
                "the tower moved in the file but not on the map");
        }

        /// <summary>
        /// Saving writes into the player's own folder, never over the shipped map - and the
        /// game would pick that copy up, because a player's copy shadows the shipped one.
        /// </summary>
        [UnityTest]
        public IEnumerator SavingWritesACopyTheGameWouldOpen()
        {
            yield return OpenTheEditor();

            LevelEditorSession session = Session();
            Assert.That(session.SaveAs(ScratchLevel), Is.True, "the map would not save");

            string mine = LevelLibrary.UserPathFor(ScratchLevel);
            Assert.That(File.Exists(mine), Is.True, $"nothing was written to {mine}");
            Assert.That(
                LevelLibrary.PathFor(ScratchLevel),
                Is.EqualTo(mine),
                "the game would not open the copy the editor just wrote");
            Assert.That(session.IsDirty, Is.False, "the map is still marked unsaved after a save");
            Assert.That(session.LevelName, Is.EqualTo(ScratchLevel));

            Assert.That(
                LevelFile.TryRead(mine, out LevelDefinition read, out string problem),
                Is.True,
                problem);
            Assert.That(read.Name, Is.EqualTo(session.Level.Name));
            Assert.That(read.Structures.Length, Is.EqualTo(session.Level.Structures.Length));
        }

        /// <summary>
        /// The rules panel is the real <see cref="LevelValidation"/>, unfiltered, so a map
        /// broken in the editor says so before it is played rather than after.
        /// </summary>
        [UnityTest]
        public IEnumerator BreakingAMapShowsUpInTheProblemList()
        {
            yield return OpenTheEditor();

            LevelEditorSession session = Session();
            Assert.That(session.Problems, Is.Empty, "the shipped map does not validate");

            LevelBunker green = session.Level.BunkerFor(Team.Green);
            Assert.That(green, Is.Not.Null);
            green.Position = new Vector3(0.0f, 0.0f, session.Level.Bounds.HalfExtent - 1.0f);
            session.Commit("broken for a test");
            yield return null;

            Assert.That(
                session.Problems,
                Is.Not.Empty,
                "a bunker dragged into the sea is not reported as a problem");
        }

        /// <summary>
        /// The whole loop this feature exists for: change a map, play it, and come back to it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every step of it is a place the map could be lost. The editor has to save before it
        /// leaves, because the game reads a file rather than whatever the editor is holding;
        /// the game has to open the map that was handed over rather than the one baked into
        /// its scene; and coming back has to land on the same map rather than on the default.
        /// None of those would look like a failure - each one shows a perfectly good map that
        /// is the wrong one.
        /// </para>
        /// <para>
        /// Saved under a scratch name on purpose. Playtesting saves, and saving under the
        /// shipped map's name would leave a player's copy of it behind that shadows the real
        /// one for every later test in the run.
        /// </para>
        /// </remarks>
        [UnityTest]
        public IEnumerator AMapCanBePlayedAndComeBackToTheEditorStillOpen()
        {
            yield return OpenTheEditor();

            LevelEditorSession session = Session();
            Assert.That(session.SaveAs(ScratchLevel), Is.True);

            LevelEdits.AddStructure(
                session.Level, StructureKind.Tree, SomewhereOnLand(session.Level));
            session.Commit("placed for a test");
            int props = session.Level.Structures.Length;
            string title = session.Level.Name;

            Assert.That(session.Playtest(), Is.True, "the map would not go to the game");
            Assert.That(
                session.IsDirty,
                Is.False,
                "playing a map did not save it first, so the game would open the last save");

            yield return null;
            yield return null;

            Assert.That(
                SceneManager.GetActiveScene().name,
                Is.EqualTo(GameSceneName),
                "playing the map did not reach the game");
            Assert.That(LevelLoader.Current, Is.Not.Null, "the game came up with no map");
            Assert.That(
                LevelLoader.Current.Name,
                Is.EqualTo(title),
                "the game opened a different map from the one the editor sent it");
            Assert.That(
                LevelLoader.Current.Structures.Length,
                Is.EqualTo(props),
                "the game is playing the map as it was before the last edit");

            var back = Object.FindAnyObjectByType<PlaytestReturn>();
            Assert.That(back, Is.Not.Null, "the game has no way back to the editor");
            Assert.That(
                PlaytestReturn.IsPlaytest,
                Is.True,
                "the game does not know it was the editor that sent it here");
            Assert.That(back.BackToEditor(), Is.True);

            yield return null;
            yield return null;

            Assert.That(
                SceneManager.GetActiveScene().name,
                Is.EqualTo(EditorSceneName),
                "there was no way back to the editor");
            Assert.That(
                Session().LevelName,
                Is.EqualTo(ScratchLevel),
                "the editor came back on a different map from the one that was played");
            Assert.That(
                PlaytestReturn.IsPlaytest,
                Is.False,
                "the editor still thinks there is a playtest to go back to");
        }

        /// <summary>
        /// A player who launched the game normally has no editor behind them, and a key that
        /// quietly took them to one would be a way to lose a match by mistake.
        /// </summary>
        [UnityTest]
        public IEnumerator AMatchNobodyOpenedFromTheEditorHasNoWayIntoIt()
        {
            LevelHandoff.Clear();
            SceneManager.LoadScene(GameSceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;

            var back = Object.FindAnyObjectByType<PlaytestReturn>();
            Assert.That(back, Is.Not.Null, "the game scene lost its playtest component");
            Assert.That(PlaytestReturn.IsPlaytest, Is.False);
            Assert.That(back.enabled, Is.False, "the way back is live in an ordinary match");
            Assert.That(
                back.BackToEditor(),
                Is.False,
                "an ordinary match can be dropped into the level editor");
        }

        /// <summary>
        /// Reverting throws away every unsaved change and reads the map back off disk.
        /// </summary>
        [UnityTest]
        public IEnumerator RevertingPutsTheMapBackToWhatWasSaved()
        {
            yield return OpenTheEditor();

            LevelEditorSession session = Session();
            Assert.That(session.SaveAs(ScratchLevel), Is.True);
            int props = session.Level.Structures.Length;

            LevelEdits.AddStructure(
                session.Level, StructureKind.Tree, SomewhereOnLand(session.Level));
            session.Commit("placed for a test");
            yield return null;
            Assert.That(session.Level.Structures.Length, Is.EqualTo(props + 1));

            Assert.That(session.Revert(), Is.True);
            yield return null;

            Assert.That(session.Level.Structures.Length, Is.EqualTo(props));
            Assert.That(Scenery(), Is.EqualTo(props), "the map on screen kept the change");
            Assert.That(session.IsDirty, Is.False);
            Assert.That(session.CanUndo, Is.False, "reverting left the old map in the history");
        }

        /// <summary>
        /// A clean map never blocks quitting, and an unsaved one is refused exactly once.
        /// </summary>
        /// <remarks>
        /// Closing the window is how most desktop sessions actually end, and before this the
        /// only place unsaved work was protected was three toolbar buttons - so Alt+F4 or the
        /// close box dropped a whole session's work with no warning at all.
        /// </remarks>
        [UnityTest]
        public IEnumerator QuittingWithUnsavedWorkIsRefusedOnceThenAllowed()
        {
            yield return OpenTheEditor();

            LevelEditorSession session = Session();
            Assert.That(session.ConfirmQuit(), Is.True, "a clean map should not block quitting");

            LevelEdits.AddStructure(
                session.Level, StructureKind.Tree, SomewhereOnLand(session.Level));
            session.Commit("placed for a test");
            yield return null;

            Assert.That(session.ConfirmQuit(), Is.False, "unsaved work quit silently");
            Assert.That(session.ConfirmQuit(), Is.True, "asking twice should let it through");
        }

        /// <summary>
        /// Working on after a cancelled quit re-arms the warning, the same as the toolbar's
        /// New/Open/Revert guard does.
        /// </summary>
        [UnityTest]
        public IEnumerator MakingAnEditAfterCancellingAQuitAsksAgain()
        {
            yield return OpenTheEditor();

            LevelEditorSession session = Session();
            LevelEdits.AddStructure(
                session.Level, StructureKind.Tree, SomewhereOnLand(session.Level));
            session.Commit("placed for a test");
            yield return null;

            Assert.That(session.ConfirmQuit(), Is.False);

            LevelEdits.AddStructure(
                session.Level, StructureKind.Tree, SomewhereOnLand(session.Level));
            session.Commit("placed again for a test");
            yield return null;

            Assert.That(
                session.ConfirmQuit(),
                Is.False,
                "an edit made after confirming quit did not re-arm the warning");
        }

        /// <summary>
        /// A bigger world lets the camera actually reach the part of it that only just
        /// became available, rather than staying clamped to the size the map opened at.
        /// </summary>
        [UnityTest]
        public IEnumerator ResizingTheWorldLetsTheCameraReachTheNewEdge()
        {
            yield return OpenTheEditor();

            LevelEditorSession session = Session();
            EditorCameraRig rig = session.View;

            float original = session.Level.Bounds.HalfExtent;
            float farOut = original + 500.0f;

            session.Level.Bounds.HalfExtent = original + 600.0f;
            session.Commit("resized for a test");
            yield return null;

            rig.LookAt(new Vector3(farOut, 0.0f, 0.0f));

            Assert.That(
                rig.Focus.x,
                Is.EqualTo(farOut).Within(0.01f),
                "the camera could not reach a point inside the newly enlarged world");
        }

        /// <summary>
        /// A level name the handoff points at that does not exist falls back to a new map,
        /// the same as opening the editor cold with a bad name typed into the field would.
        /// </summary>
        /// <remarks>
        /// This is the one branch of <see cref="LevelEditorSession.Start"/> none of the other
        /// tests ever reach, because every other one opens on whatever the loader already
        /// built. It is also exactly the branch M8's own notes call out: adopting
        /// <c>LevelLoader.Current</c> instead of <c>loader.Shown</c> here would hand the
        /// editor a stale map under this scene's file name, and the first save would write
        /// one map into another map's file.
        /// </remarks>
        [UnityTest]
        public IEnumerator AskingForAMapThatDoesNotExistFallsBackToANewOne()
        {
            LevelHandoff.Clear();
            LevelHandoff.Playtest("no-such-map-anywhere");

            // LevelLoader.Awake tries the same missing name first and logs it, before the
            // session's own fallback in Start ever runs - expected, and part of what this
            // test is checking: the loader failing is exactly the case the fallback is for.
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("no-such-map-anywhere"));

            SceneManager.LoadScene(EditorSceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;

            LevelEditorSession session = Session();
            Assert.That(session.Level, Is.Not.Null, "the fallback left the editor with no map");
            Assert.That(session.IsDirty, Is.True, "a fresh fallback map should read as unsaved");
            Assert.That(
                LevelLibrary.Names(),
                Does.Not.Contain("no-such-map-anywhere"),
                "the missing name should never have become a real file");
        }

        /// <summary>
        /// Loading the editor scene and letting its first frame run, which is where the map is
        /// built and the session adopts it.
        /// </summary>
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
        /// Counts the scenery on the map, which is every destructible that is not a tower.
        /// </summary>
        private static int Scenery()
        {
            int found = 0;
            foreach (Destructible structure in Object.FindObjectsByType<Destructible>())
            {
                if (structure.GetComponent<FlagTower>() == null)
                {
                    found++;
                }
            }

            return found;
        }

        private static FlagTower NearestTower(Vector3 to)
        {
            FlagTower nearest = null;
            float closest = float.MaxValue;

            foreach (FlagTower tower in Object.FindObjectsByType<FlagTower>())
            {
                float away = Vector3.Distance(tower.transform.position, to);
                if (away < closest)
                {
                    closest = away;
                    nearest = tower;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Finds a spot on the map with room around it, read off the level rather than written
        /// down, so this still works after somebody moves the coastline.
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

        /// <summary>
        /// Compares two positions with a tolerance, which <c>Is.EqualTo</c> will not do for a
        /// <see cref="Vector3"/> without help.
        /// </summary>
        private sealed class PositionComparer : System.Collections.Generic.IEqualityComparer<Vector3>
        {
            private readonly float slack;

            public PositionComparer(float within) => slack = within;

            public bool Equals(Vector3 left, Vector3 right)
                => Vector3.Distance(left, right) <= slack;

            public int GetHashCode(Vector3 at) => at.GetHashCode();
        }
    }
}
