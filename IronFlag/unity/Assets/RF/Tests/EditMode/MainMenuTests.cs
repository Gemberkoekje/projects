using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using IronFlag.Levels;
using IronFlag.Menu;
using IronFlag.UI;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// Checks that the generated menu scene is a menu, and that the settings behind it store
    /// what they say they store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every wiring failure here presents as silence, like the level editor's: a missing event
    /// system is "the buttons do nothing", a scene left off the build list is "Play does
    /// nothing", and a menu that is not first on that list is a game that boots straight into a
    /// match exactly as it did before any of this was written.
    /// </para>
    /// <para>
    /// The settings tests read and write <see cref="PlayerPrefs"/>, which is the machine's
    /// rather than the project's - so they put back whatever was there. They also never call
    /// <see cref="GameSettings.Apply"/>: applying a setting resizes the window and switches the
    /// quality tier, and a test suite that did that would rearrange the editor of whoever ran
    /// it.
    /// </para>
    /// </remarks>
    public sealed class MainMenuTests
    {
        /// <summary>
        /// Stands in for a preference that was never set, so putting the machine back means
        /// deleting the key rather than writing a zero into it.
        /// </summary>
        private const int Missing = int.MinValue;

        private readonly Dictionary<string, int> savedNumbers = new Dictionary<string, int>();
        private string savedQuality = string.Empty;
        private bool hadQuality;

        [SetUp]
        public void RememberWhateverThisMachineHadSet()
        {
            savedNumbers.Clear();
            KeepNumber(GameSettings.FullscreenKey);
            KeepNumber(GameSettings.WidthKey);
            KeepNumber(GameSettings.HeightKey);

            // Read back by type, because PlayerPrefs is typed and asking a stored integer for
            // its string is an error rather than an empty answer.
            hadQuality = PlayerPrefs.HasKey(GameSettings.QualityKey);
            savedQuality = hadQuality
                ? PlayerPrefs.GetString(GameSettings.QualityKey, string.Empty)
                : string.Empty;
        }

        [TearDown]
        public void PutTheMachineBackTheWayItWas()
        {
            foreach (KeyValuePair<string, int> was in savedNumbers)
            {
                if (was.Value == Missing)
                {
                    PlayerPrefs.DeleteKey(was.Key);
                }
                else
                {
                    PlayerPrefs.SetInt(was.Key, was.Value);
                }
            }

            if (hadQuality)
            {
                PlayerPrefs.SetString(GameSettings.QualityKey, savedQuality);
            }
            else
            {
                PlayerPrefs.DeleteKey(GameSettings.QualityKey);
            }

            PlayerPrefs.Save();
        }

        [Test]
        public void TheMenuSceneIsWhereEverythingExpectsItToBe()
        {
            Assert.That(
                AssetDatabase.LoadAssetAtPath<SceneAsset>(LevelScenes.MainMenuPath),
                Is.Not.Null,
                $"{LevelScenes.MainMenuPath} is missing; run Tools > IronFlag > Build Main Menu Scene");
        }

        /// <summary>
        /// The menu has to be index 0 or it is not the menu: Unity starts a built game in the
        /// first scene on this list, and a menu at index 1 is a screen the game never shows.
        /// </summary>
        [Test]
        public void TheGameBootsIntoTheMenuAndCanReachEverythingElse()
        {
            var listed = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                {
                    listed.Add(scene.path);
                }
            }

            Assert.That(listed, Contains.Item(LevelScenes.MainMenuPath));
            Assert.That(listed, Contains.Item(LevelScenes.GamePath));
            Assert.That(listed, Contains.Item(LevelScenes.EditorPath));

            Assert.That(
                listed.IndexOf(LevelScenes.MainMenuPath),
                Is.EqualTo(0),
                "a built copy would start somewhere other than the menu");
            Assert.That(
                listed.IndexOf(LevelScenes.GamePath),
                Is.LessThan(listed.IndexOf(LevelScenes.EditorPath)),
                "the game and the editor have swapped places on the build list");
        }

        [Test]
        public void TheMenuSceneHasAMapBehindItAndSomethingToPressInFrontOfIt()
        {
            using (var scene = new OpenMenu())
            {
                Assert.That(scene.One<LevelLoader>(), Is.Not.Null, "the menu has no map behind it");

                MenuBackdrop backdrop = scene.One<MenuBackdrop>();
                Assert.That(backdrop, Is.Not.Null, "the menu has no camera");
                Assert.That(
                    backdrop.Distance,
                    Is.GreaterThan(0.0f),
                    "the menu camera is standing on the middle of the map");

                Assert.That(
                    scene.One<MainMenuController>(),
                    Is.Not.Null,
                    "the menu scene has no menu in it");
                Assert.That(
                    scene.One<EventSystem>(),
                    Is.Not.Null,
                    "the menu has no event system, so nothing on it can be pressed");
                Assert.That(
                    scene.One<GraphicRaycaster>(),
                    Is.Not.Null,
                    "the menu canvas cannot be clicked on");
            }
        }

        /// <summary>
        /// The map is baked into the scene for the same reason the other two scenes' maps are:
        /// a scene that opened empty would be one nobody could look at, and the still would
        /// have nothing to photograph.
        /// </summary>
        [Test]
        public void TheMenuSceneCarriesABakedCopyOfItsMap()
        {
            using (var scene = new OpenMenu())
            {
                LevelLoader loader = scene.One<LevelLoader>();
                Assert.That(
                    loader.transform.childCount,
                    Is.GreaterThan(0),
                    "the menu scene has no map baked into it");
            }
        }

        /// <summary>
        /// The menu is interface, so it is drawn by a camera stacked on the one showing the map
        /// with the grade switched off - see <see cref="IronFlag.Core.ViewStack"/>. Without
        /// this it is tone-mapped along with the island behind it, and the hand-picked panel
        /// colours are not the colours anybody sees.
        /// </summary>
        [Test]
        public void TheMenuIsDrawnOverTheGradeRatherThanThroughIt()
        {
            using (var scene = new OpenMenu())
            {
                int layer = InterfaceLayers.EditorLayer();
                if (layer < 0)
                {
                    Assert.Ignore("this project has no UI layer, so there is nothing to check");
                }

                MainMenuController menu = scene.One<MainMenuController>();
                Assert.That(
                    menu.gameObject.layer,
                    Is.EqualTo(layer),
                    "the menu is on the world's layer, so the world camera draws it too");

                MenuBackdrop backdrop = scene.One<MenuBackdrop>();
                Assert.That(
                    backdrop.View.cullingMask & (1 << layer),
                    Is.Zero,
                    "the camera showing the map is drawing the menu as well");

                var canvas = menu.GetComponent<Canvas>();
                Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceCamera));
                Assert.That(
                    canvas.worldCamera,
                    Is.Not.Null,
                    "the menu canvas has no camera, so it appears in no capture");
            }
        }

        /// <summary>
        /// The one thing about the menu that cannot be seen by looking at it: a match launched
        /// from here has never been in the level editor, and a game scene told otherwise puts a
        /// "back to the editor" notice over it and binds F1 to a scene nobody asked for.
        /// </summary>
        [Test]
        public void AMapChosenOnTheMenuDoesNotClaimToBeAPlaytest()
        {
            try
            {
                LevelHandoff.Play("iron-channel");

                Assert.That(LevelHandoff.Level, Is.EqualTo("iron-channel"));
                Assert.That(LevelHandoff.IsAsked, Is.True);
                Assert.That(
                    LevelHandoff.FromEditor,
                    Is.False,
                    "a map played from the menu says the editor sent it");
            }
            finally
            {
                LevelHandoff.Clear();
            }
        }

        [Test]
        public void TheSettingsRememberWhatTheyWereGiven()
        {
            PlayerPrefs.SetInt(GameSettings.FullscreenKey, 0);
            PlayerPrefs.SetInt(GameSettings.WidthKey, 1600);
            PlayerPrefs.SetInt(GameSettings.HeightKey, 900);
            PlayerPrefs.SetString(GameSettings.QualityKey, "PC");

            Assert.That(GameSettings.Fullscreen, Is.False);
            Assert.That(GameSettings.Size, Is.EqualTo(new Vector2Int(1600, 900)));
            Assert.That(GameSettings.Quality, Is.EqualTo("PC"));
        }

        /// <summary>
        /// A tier name written by an older build should cost the player their choice, not drop
        /// them onto whichever tier happens to be first.
        /// </summary>
        [Test]
        public void AQualityTierThatNoLongerExistsIsLeftAloneRatherThanGuessedAt()
        {
            Assert.That(GameSettings.IndexOfQuality("a tier nobody has"), Is.EqualTo(-1));

            List<string> tiers = GameSettings.Qualities();
            Assert.That(tiers, Is.Not.Empty, "the project has no quality tiers at all");
            Assert.That(GameSettings.IndexOfQuality(tiers[0]), Is.EqualTo(0));
        }

        /// <summary>
        /// The editor's panels are laid out in pixels, so a window narrow enough to be offered
        /// here would be one the level editor did not fit in.
        /// </summary>
        [Test]
        public void NoWindowSmallerThanTheInterfaceIsOffered()
        {
            foreach (Vector2Int size in GameSettings.Sizes())
            {
                if (size == GameSettings.Size)
                {
                    // Where the window already is, which is listed whatever its size - see
                    // the test below.
                    continue;
                }

                Assert.That(size.x, Is.GreaterThanOrEqualTo(GameSettings.Smallest.x));
                Assert.That(size.y, Is.GreaterThanOrEqualTo(GameSettings.Smallest.y));
            }
        }

        /// <summary>
        /// The stepper finds where it is by looking the current size up in this list, so a
        /// size that is in use but not listed is a row that reads one thing while the list
        /// holds another - and no way to step off it. A batch run at 640x480 is exactly that
        /// case, which is how this was found.
        /// </summary>
        [Test]
        public void TheSizeTheWindowIsAtIsAlwaysOnTheList()
        {
            Assert.That(GameSettings.Sizes(), Contains.Item(GameSettings.Size));
        }

        [Test]
        public void EveryWindowSizeIsOfferedOnlyOnce()
        {
            List<Vector2Int> sizes = GameSettings.Sizes();
            Assert.That(
                new HashSet<Vector2Int>(sizes).Count,
                Is.EqualTo(sizes.Count),
                "the same resolution is on the list once per refresh rate the monitor runs it at");
        }

        /// <summary>
        /// The two tiers this project has are named after machines rather than after what they
        /// do, and one of those machines is not a thing this game runs on.
        /// </summary>
        [Test]
        public void TheQualityTiersAreNamedForWhatTheyDo()
        {
            Assert.That(GameSettings.NameOfQuality("PC"), Is.EqualTo("FULL"));
            Assert.That(GameSettings.NameOfQuality("Mobile"), Is.EqualTo("SIMPLE"));
            Assert.That(
                GameSettings.NameOfQuality("Ultra"),
                Is.EqualTo("ULTRA"),
                "a tier nobody has a friendly name for should show under its own");
        }

        /// <summary>
        /// A menu somebody leaves up all afternoon is a float that would otherwise lose its
        /// fractional digits, which reads as a camera that starts stepping instead of turning.
        /// </summary>
        [Test]
        public void TheBackdropKeepsTurningWithoutRunningOutOfPrecision()
        {
            Assert.That(MenuBackdrop.TurnedAfter(0.0f), Is.EqualTo(0.0f).Within(0.001f));
            Assert.That(
                MenuBackdrop.TurnedAfter(360.0f / MenuBackdrop.DegreesPerSecond),
                Is.EqualTo(0.0f).Within(0.01f),
                "one full turn does not come back round to where it started");
            Assert.That(MenuBackdrop.TurnedAfter(100000.0f), Is.InRange(0.0f, 360.0f));
        }

        private void KeepNumber(string key)
            => savedNumbers[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key, 0) : Missing;

        private sealed class OpenMenu : System.IDisposable
        {
            private readonly Scene scene;

            public OpenMenu()
                => scene = EditorSceneManager.OpenScene(
                    LevelScenes.MainMenuPath, OpenSceneMode.Additive);

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
