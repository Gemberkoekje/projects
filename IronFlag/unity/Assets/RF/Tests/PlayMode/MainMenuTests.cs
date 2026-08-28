using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using IronFlag.Core;
using IronFlag.Editing;
using IronFlag.Levels;
using IronFlag.Menu;
using IronFlag.Objective;

namespace IronFlag.Tests.PlayMode
{
    /// <summary>
    /// The menu, running: a map list built out of the level folders, a match started from it,
    /// and the way back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What needs a scene here is the claim the whole feature rests on - that the buttons on
    /// this screen reach the other two scenes and arrive with the right map. Everything about
    /// the menu that can be checked without playing it is in the edit-mode suite.
    /// </para>
    /// <para>
    /// The menu applies the stored settings on its first frame, which includes switching the
    /// quality tier - so these put the tier back on the way out. A test suite that left the
    /// editor on a different render pipeline asset than it found it on would be a test suite
    /// that quietly changed how every later still was graded.
    /// </para>
    /// </remarks>
    public sealed class MainMenuTests
    {
        /// <summary>Name of the menu scene, as the scene manager knows it.</summary>
        private const string MenuSceneName = "MainMenu";

        /// <summary>Name of the game scene, as the scene manager knows it.</summary>
        private const string GameSceneName = "Sandbox";

        /// <summary>Name of the editor scene, as the scene manager knows it.</summary>
        private const string EditorSceneName = "LevelEditor";

        private int tierOnTheWayIn;

        [SetUp]
        public void RememberTheQualityTier() => tierOnTheWayIn = QualitySettings.GetQualityLevel();

        /// <summary>
        /// Puts the world back to nothing between tests, scenes included.
        /// </summary>
        /// <returns>The coroutine the framework steps through.</returns>
        /// <remarks>
        /// The same debt <see cref="LevelLoadingTests"/> and <see cref="SplitScreenTests"/>
        /// pay, and this class owes it three times over: a scene loaded <c>Single</c> stays
        /// loaded for whoever runs next, and one of the three opened here is an entire match
        /// - two bunkers, both flags, and a side's whole reserve of vehicles. Everything
        /// after it that asks the scene a question by team was getting that map's answer,
        /// which is a failure that shows up in a class with nothing to do with the menu.
        /// </remarks>
        [UnityTearDown]
        public IEnumerator LeaveNothingBehind()
        {
            LevelHandoff.Clear();
            QualitySettings.SetQualityLevel(tierOnTheWayIn, true);

            // A test that fails between PauseMenu.Open() and the matching Close() or
            // BackToMenu() would otherwise leave the engine paused for whatever the next
            // test in the process runs, which is a failure that shows up far from here.
            Time.timeScale = 1.0f;
            yield return null;

            foreach (string name in new[] { MenuSceneName, GameSceneName, EditorSceneName })
            {
                Scene open = SceneManager.GetSceneByName(name);
                if (!open.IsValid() || !open.isLoaded)
                {
                    continue;
                }

                SceneManager.SetActiveScene(SceneManager.CreateScene($"After{name}"));
                yield return SceneManager.UnloadSceneAsync(open);
            }
        }

        [UnityTest]
        public IEnumerator TheMenuComesUpWithTheMapsThatExist()
        {
            yield return Open(MenuSceneName);

            MainMenuController menu = Menu();
            Assert.That(menu, Is.Not.Null, "the menu scene came up with no menu in it");
            Assert.That(menu.IsBuilt, Is.True, "the menu never generated itself");
            Assert.That(menu.Showing, Is.EqualTo(MenuPanel.Root));

            menu.Show(MenuPanel.Levels);
            Assert.That(
                menu.MapCount,
                Is.EqualTo(LevelLibrary.Names().Count),
                "the list is not the maps that are actually there");
        }

        /// <summary>
        /// The four buttons on the front screen are the whole of what the menu is, and a
        /// listener that was never attached looks exactly like a button that is not there.
        /// </summary>
        [UnityTest]
        public IEnumerator TheButtonsOnTheFrontScreenAreWiredToSomething()
        {
            yield return Open(MenuSceneName);

            MainMenuController menu = Menu();

            Press(menu, "Play");
            Assert.That(
                menu.Showing,
                Is.EqualTo(MenuPanel.Levels),
                "PLAY does not reach the map list");

            Press(menu, "Levels Back");
            Assert.That(menu.Showing, Is.EqualTo(MenuPanel.Root));

            Press(menu, "Settings");
            Assert.That(
                menu.Showing,
                Is.EqualTo(MenuPanel.Settings),
                "SETTINGS does not reach the settings");

            Press(menu, "Settings Back");
            Assert.That(menu.Showing, Is.EqualTo(MenuPanel.Root));
        }

        /// <summary>
        /// The one thing about the menu that cannot be seen by looking at it: a match launched
        /// from here has never been in the level editor, so it must not be handed the notice and
        /// the key that say otherwise.
        /// </summary>
        [UnityTest]
        public IEnumerator ChoosingAMapStartsAMatchThatIsNotAPlaytest()
        {
            yield return Open(MenuSceneName);

            Menu().PlayLevel(LevelLibrary.DefaultLevel);
            yield return null;
            yield return null;

            Assert.That(
                SceneManager.GetActiveScene().name,
                Is.EqualTo(GameSceneName),
                "choosing a map did not reach the game");
            Assert.That(LevelLoader.Current, Is.Not.Null, "the game came up with no map");
            Assert.That(
                PlaytestReturn.IsPlaytest,
                Is.False,
                "a match started from the menu thinks the editor sent it");
        }

        [UnityTest]
        public IEnumerator TheMenuReachesTheLevelEditorDirectly()
        {
            yield return Open(MenuSceneName);

            Menu().OpenEditor();
            yield return null;
            yield return null;

            Assert.That(
                SceneManager.GetActiveScene().name,
                Is.EqualTo(EditorSceneName),
                "the menu has no way into the level editor");
        }

        /// <summary>
        /// Escape pauses a match in progress and offers a way out, rather than throwing it
        /// away outright - a second press lets it keep going instead.
        /// </summary>
        [UnityTest]
        public IEnumerator TheMenuPausesTheMatchAndASecondPressResumesIt()
        {
            yield return Open(GameSceneName);

            var menu = Object.FindAnyObjectByType<PauseMenu>();
            Assert.That(menu, Is.Not.Null, "a match has no way back to the menu");
            Assert.That(menu.IsOpen, Is.False);

            menu.Open();
            Assert.That(menu.IsOpen, Is.True, "opening the menu did not open it");
            Assert.That(Time.timeScale, Is.EqualTo(0.0f), "opening the menu did not pause the match");
            Assert.That(
                SceneManager.GetActiveScene().name,
                Is.EqualTo(GameSceneName),
                "opening the menu left the match");

            menu.Close();
            Assert.That(menu.IsOpen, Is.False, "closing the menu left it open");
            Assert.That(Time.timeScale, Is.EqualTo(1.0f), "closing the menu left the match paused");
        }

        /// <summary>
        /// A match left through the menu must not leave the map it was on lying in the
        /// handoff, which is what a later scene would open without anybody choosing it, and
        /// must not leave the engine paused for whatever scene loads next.
        /// </summary>
        [UnityTest]
        public IEnumerator TheMainMenuButtonLeavesAPausedMatchAndForgetsWhichMapItWas()
        {
            LevelHandoff.Play(LevelLibrary.DefaultLevel);
            yield return Open(GameSceneName);

            PauseMenu menu = Object.FindAnyObjectByType<PauseMenu>();
            menu.Open();

            menu.BackToMenu();
            yield return null;
            yield return null;

            Assert.That(
                SceneManager.GetActiveScene().name,
                Is.EqualTo(MenuSceneName),
                "leaving a paused match did not reach the menu");
            Assert.That(LevelHandoff.IsAsked, Is.False, "the menu is still holding the last map");
            Assert.That(Time.timeScale, Is.EqualTo(1.0f), "leaving a paused match left the engine paused");
        }

        /// <summary>
        /// The pause menu's buttons are the whole of what it offers once it is open, and a
        /// listener that was never attached looks exactly like a button that does nothing.
        /// </summary>
        [UnityTest]
        public IEnumerator ThePauseMenusButtonsAreWiredToSomething()
        {
            yield return Open(GameSceneName);

            PauseMenu menu = Object.FindAnyObjectByType<PauseMenu>();
            menu.Open();

            Press(menu, "Continue");
            Assert.That(menu.IsOpen, Is.False, "CONTINUE does not close the menu");

            menu.Open();
            Press(menu, "Main Menu");
            yield return null;
            yield return null;

            Assert.That(
                SceneManager.GetActiveScene().name,
                Is.EqualTo(MenuSceneName),
                "MAIN MENU does not reach the menu");
        }

        /// <summary>
        /// A finished match has nothing left to continue, so the menu offers only the way
        /// out - the result itself is already on screen, on both halves of the HUD.
        /// </summary>
        [UnityTest]
        public IEnumerator AFinishedMatchOffersOnlyTheWayOut()
        {
            yield return Open(GameSceneName);

            Match match = Object.FindAnyObjectByType<Match>();
            Assert.That(match, Is.Not.Null, "the match scene came up with no match in it");
            match.Win(Team.Green, Team.Brown, MatchOutcome.FlagCaptured);

            PauseMenu menu = Object.FindAnyObjectByType<PauseMenu>();
            menu.Open();

            bool continueShown = false;
            bool mainMenuShown = false;
            foreach (Button button in menu.GetComponentsInChildren<Button>(false))
            {
                continueShown |= button.name == "Continue";
                mainMenuShown |= button.name == "Main Menu";
            }

            Assert.That(continueShown, Is.False, "a finished match still offers to continue it");
            Assert.That(mainMenuShown, Is.True, "a finished match has no way out");
        }

        private static IEnumerator Open(string scene)
        {
            SceneManager.LoadScene(scene, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        private static MainMenuController Menu() => Object.FindAnyObjectByType<MainMenuController>();

        /// <summary>
        /// Presses a generated button by the name its object was given.
        /// </summary>
        /// <remarks>
        /// By name rather than by held reference, because the point of the test is that the
        /// thing on the screen is wired - a reference handed out by the code that built it
        /// would pass whether or not anybody could click it. Takes any component rather than
        /// specifically <see cref="MainMenuController"/> so <see cref="PauseMenu"/>'s own
        /// buttons can be pressed the same way.
        /// </remarks>
        private static void Press(Component root, string name)
        {
            foreach (Button button in root.GetComponentsInChildren<Button>(true))
            {
                if (button.name == name)
                {
                    button.onClick.Invoke();
                    return;
                }
            }

            Assert.Fail($"{root.GetType().Name} has no button called '{name}'");
        }
    }
}
