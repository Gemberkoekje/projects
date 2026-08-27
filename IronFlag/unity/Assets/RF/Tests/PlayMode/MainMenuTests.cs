using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using IronFlag.Editing;
using IronFlag.Levels;
using IronFlag.Menu;

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
        /// Escape throws away a match in progress, so it asks first - the same way the editor's
        /// New, Open and Revert buttons do, and for the same reason.
        /// </summary>
        [UnityTest]
        public IEnumerator LeavingAMatchTakesTwoPressesAndEndsUpOnTheMenu()
        {
            yield return Open(GameSceneName);

            var exit = Object.FindAnyObjectByType<MenuReturn>();
            Assert.That(exit, Is.Not.Null, "a match has no way back to the menu");
            Assert.That(exit.IsArmed, Is.False);

            Assert.That(exit.Press(), Is.False, "one press left the match");
            Assert.That(exit.IsArmed, Is.True, "the first press did not arm anything");
            Assert.That(
                SceneManager.GetActiveScene().name,
                Is.EqualTo(GameSceneName),
                "one press of escape abandoned the match");

            Assert.That(exit.Press(), Is.True, "the second press did not leave");
            yield return null;
            yield return null;

            Assert.That(
                SceneManager.GetActiveScene().name,
                Is.EqualTo(MenuSceneName),
                "leaving a match did not reach the menu");
        }

        /// <summary>
        /// A match left through the menu must not leave the map it was on lying in the handoff,
        /// which is what a later scene would open without anybody choosing it.
        /// </summary>
        [UnityTest]
        public IEnumerator LeavingAMatchForgetsWhichMapItWas()
        {
            LevelHandoff.Play(LevelLibrary.DefaultLevel);
            yield return Open(GameSceneName);

            Object.FindAnyObjectByType<MenuReturn>().BackToMenu();
            yield return null;
            yield return null;

            Assert.That(LevelHandoff.IsAsked, Is.False, "the menu is still holding the last map");
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
        /// would pass whether or not anybody could click it.
        /// </remarks>
        private static void Press(MainMenuController menu, string name)
        {
            foreach (Button button in menu.GetComponentsInChildren<Button>(true))
            {
                if (button.name == name)
                {
                    button.onClick.Invoke();
                    return;
                }
            }

            Assert.Fail($"the menu has no button called '{name}'");
        }
    }
}
