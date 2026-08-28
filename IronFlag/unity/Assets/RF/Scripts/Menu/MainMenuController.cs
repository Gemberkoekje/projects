using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using IronFlag.Audio;
using IronFlag.Editing;
using IronFlag.Levels;
using IronFlag.UI;

namespace IronFlag.Menu
{
    /// <summary>
    /// The screen the game starts on: what to play, where to build it, how the window should
    /// look, and the way out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Generated from code like every other piece of interface here, and for the reason that
    /// matters most on this one screen: the list of maps is
    /// <see cref="LevelLibrary.Names"/>, so a map the player drew in the editor five minutes
    /// ago is on the menu without anybody opening a prefab. An authored menu would have been a
    /// list of the maps that shipped.
    /// </para>
    /// <para>
    /// It reuses <see cref="EditorTheme"/> rather than inventing a look of its own. The two
    /// screens are the same kind of thing - dark plates, a column of choices, read while
    /// sitting still - and nothing in that file turned out to be about editing: the four
    /// widgets it makes are a plate, a label, a button and a field, and the menu uses three of
    /// them. When the visual identity pass lands, one file changes rather than two.
    /// </para>
    /// <para>
    /// Three panels in one column, switched rather than stacked - see <see cref="MenuPanel"/>.
    /// The column is narrow on purpose: the map turning behind it is the other two-thirds of
    /// the screen, and a menu that covered it would have wasted the whole reason
    /// <see cref="MenuBackdrop"/> exists.
    /// </para>
    /// <para>
    /// Unlike the level editor's panels this canvas scales with the screen rather than being
    /// pixel-exact. The editor is worked in, so a bigger screen should mean more map; a menu is
    /// read once at a glance and then left, so it should take the same share of any screen -
    /// the same reasoning as <see cref="PlayerHud"/>, arrived at from the opposite direction.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Main Menu")]
    [RequireComponent(typeof(Canvas))]
    public sealed class MainMenuController : MonoBehaviour
    {
        /// <summary>Name of the generated child the whole menu hangs off.</summary>
        public const string MenuNodeName = "Menu";

        /// <summary>Width the menu is laid out for, in canvas units.</summary>
        public const float ReferenceWidth = 1920.0f;

        /// <summary>Height the menu is laid out for, in canvas units.</summary>
        public const float ReferenceHeight = 1080.0f;

        /// <summary>How many maps the list shows at once.</summary>
        public const int LevelRows = 8;

        private const float ColumnWidth = 620.0f;
        private const float Margin = 32.0f;
        private const float RowHeight = 58.0f;
        private const float Gutter = 10.0f;
        private const float HeaderHeight = 178.0f;
        private const float NoteHeight = 34.0f;
        private const float LevelRowHeight = 74.0f;
        private const float CaptionWidth = 140.0f;
        private const float ArrowWidth = 58.0f;

        /// <summary>How far below its place a screen starts, in canvas units.</summary>
        /// <remarks>
        /// Deliberately small. A screen that slid a visible distance would be a screen the
        /// player had to wait for; this is far enough that the eye registers movement and
        /// short enough that it is over before anybody could have read the first line.
        /// </remarks>
        private const float EntryRise = 18.0f;

        private readonly List<EditorButton> levelButtons = new List<EditorButton>();
        private readonly List<Text> levelNotes = new List<Text>();
        private readonly List<MapCard> cards = new List<MapCard>();

        private RectTransform menu;
        private RectTransform rootPanel;
        private RectTransform levelsPanel;
        private RectTransform settingsPanel;
        private CanvasGroup rootFade;
        private CanvasGroup levelsFade;
        private CanvasGroup settingsFade;
        private Vector2 panelRest;
        private float entered = 1.0f;
        private EditorButton moreButton;
        private EditorButton fullscreenButton;
        private EditorButton sizeButton;
        private EditorButton qualityButton;
        private EditorButton soundButton;
        private EditorButton musicButton;
        private Text note;
        private MenuPanel showing = MenuPanel.None;
        private int page;

        /// <summary>Whether the menu has been generated yet.</summary>
        public bool IsBuilt => menu != null;

        /// <summary>Which screen is currently up.</summary>
        public MenuPanel Showing => showing;

        /// <summary>How many maps the list found the last time it was filled.</summary>
        public int MapCount => cards.Count;

        /// <summary>
        /// Points the menu at the camera that draws it and generates it.
        /// </summary>
        /// <param name="view">The camera the interface is drawn by.</param>
        /// <remarks>
        /// The interface camera rather than the one showing the map, so the menu is not graded
        /// along with the island behind it - see <see cref="IronFlag.Core.ViewStack"/>. The
        /// layer is set before anything is generated, because everything generated is born on
        /// whatever layer its canvas is on.
        /// </remarks>
        public void Configure(Camera view)
        {
            int layer = InterfaceLayers.EditorLayer();
            if (layer >= 0)
            {
                gameObject.layer = layer;
            }

            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = view;
            canvas.planeDistance = 1.0f;
            canvas.sortingOrder = 10;

            var scaler = GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = gameObject.AddComponent<CanvasScaler>();
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);

            // Matched on height alone, unlike the HUD's half-and-half. Everything here is one
            // column measured down from the top and one row measured up from the bottom, so the
            // only dimension the layout can run out of is the vertical one - and splitting the
            // match makes the canvas shorter than 1080 units on a wide screen, which on a
            // 32:9 monitor put the last map in the list underneath the BACK button. Matching
            // height keeps the column exactly as tall as it was laid out for and lets a wider
            // screen simply show more of the map.
            scaler.matchWidthOrHeight = 1.0f;

            if (GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            if (!IsBuilt)
            {
                Build();
            }

            InterfaceLayers.Paint(gameObject);
        }

        /// <summary>
        /// Generates the menu, replacing any that was there before.
        /// </summary>
        public void Build()
        {
            Clear();

            var host = new GameObject(MenuNodeName, typeof(RectTransform));
            host.transform.SetParent(transform, false);
            menu = host.GetComponent<RectTransform>();
            menu.anchorMin = Vector2.zero;
            menu.anchorMax = Vector2.one;
            menu.offsetMin = Vector2.zero;
            menu.offsetMax = Vector2.zero;

            HudPlate plate = EditorTheme.Panel("Column", menu, EditorTheme.Chrome);
            EditorTheme.Cling(plate.rectTransform, RectTransform.Edge.Left, ColumnWidth, 0.0f, 0.0f);
            RectTransform column = plate.rectTransform;

            BuildHeader(column);
            BuildNote(column);

            rootPanel = BuildPanel(column, "Root");
            levelsPanel = BuildPanel(column, "Levels");
            settingsPanel = BuildPanel(column, "Settings");

            rootFade = rootPanel.GetComponent<CanvasGroup>();
            levelsFade = levelsPanel.GetComponent<CanvasGroup>();
            settingsFade = settingsPanel.GetComponent<CanvasGroup>();

            // Where a screen sits when it has finished arriving. Read off one of them rather
            // than worked out, and shared by all three because BuildPanel gives them identical
            // anchors - a screen that slid to a place it was never built in would arrive
            // somewhere slightly wrong and stay there.
            panelRest = rootPanel.anchoredPosition;

            BuildRoot();
            BuildLevels();
            BuildSettings();

            Show(MenuPanel.Root);
            InterfaceLayers.Paint(gameObject);
        }

        /// <summary>
        /// Brings whichever screen is up back into agreement with the world.
        /// </summary>
        /// <remarks>
        /// Whichever screen is up, and only that one - which is not the shape the editor's
        /// panels use, because unlike them these three share a line. The note at the foot of
        /// the column belongs to whatever is showing, and refreshing all three put the map
        /// list's "no maps found" under the title screen, where nobody had asked about maps.
        /// </remarks>
        public void Refresh()
        {
            if (showing == MenuPanel.Levels)
            {
                RefreshLevels();
            }
            else if (showing == MenuPanel.Settings)
            {
                RefreshSettings();
            }
        }

        /// <summary>
        /// Puts one of the three screens up and takes the other two down.
        /// </summary>
        /// <param name="panel">The screen to show.</param>
        public void Show(MenuPanel panel)
        {
            showing = panel;

            rootPanel.gameObject.SetActive(panel == MenuPanel.Root);
            levelsPanel.gameObject.SetActive(panel == MenuPanel.Levels);
            settingsPanel.gameObject.SetActive(panel == MenuPanel.Settings);

            // Already arrived when nothing is running. The menu's still is rendered from an
            // edit-mode scene where no Update ever fires, so a screen that started faded out
            // there would stay faded out - and the picture of the menu would be a picture of
            // an empty column.
            entered = Application.isPlaying ? 0.0f : 1.0f;
            ApplyEntry();

            Say(string.Empty);

            if (panel == MenuPanel.Levels)
            {
                ReadMaps();
                page = 0;
                RefreshLevels();
            }
            else if (panel == MenuPanel.Settings)
            {
                RefreshSettings();
            }
        }

        /// <summary>
        /// Loads a map and starts a match on it.
        /// </summary>
        /// <param name="name">Level name, without a folder or an extension.</param>
        /// <remarks>
        /// <see cref="LevelHandoff.Play"/> rather than
        /// <see cref="LevelHandoff.Playtest"/>, and the difference is the whole reason that
        /// method exists: a playtest is a map the <em>editor</em> sent here, and the game scene
        /// reads that to decide whether F1 has anywhere to go back to. A match launched from
        /// the menu that claimed to be a playtest would put a "back to the editor" notice over
        /// a session that had never been in one.
        /// </remarks>
        public void PlayLevel(string name)
        {
            LevelHandoff.Play(name);
            SceneManager.LoadScene(LevelScenes.Game);
        }

        /// <summary>
        /// Opens the level editor.
        /// </summary>
        /// <remarks>
        /// The first direct way into the editor the game has ever had: until now the only door
        /// was the playtest round trip, which needed somebody to already be in the editor.
        /// </remarks>
        public void OpenEditor()
        {
            // Cleared so the editor opens the map its scene was built around rather than
            // whichever one the last match happened to be on.
            LevelHandoff.Clear();
            SceneManager.LoadScene(LevelScenes.Editor);
        }

        /// <summary>
        /// Ends the session.
        /// </summary>
        /// <remarks>
        /// <see cref="Application.Quit"/> does nothing at all inside the Unity editor, so the
        /// button would be the one thing on this screen that silently did not work in the only
        /// place it is ever pressed during development. Stopping play mode is the honest
        /// equivalent, and it is the one place in the runtime assembly that needs the editor.
        /// </remarks>
        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// Writes a line at the foot of the column.
        /// </summary>
        /// <param name="words">What to say, or nothing to clear it.</param>
        public void Say(string words)
        {
            if (note != null)
            {
                note.text = words == null ? string.Empty : words;
            }
        }

        /// <remarks>
        /// <para>
        /// The saved scene carries a baked copy of the menu so the still has something to
        /// photograph, and it is thrown away and rebuilt here on the first frame of play - the
        /// same arrangement <see cref="EditorUi"/> uses, and for a sharper reason: what this
        /// screen lists is the level folders and the display's own modes, so a menu restored
        /// from disk is a menu showing the maps and resolutions that existed when somebody
        /// pressed a menu item.
        /// </para>
        /// <para>
        /// Guarded on actually playing rather than on a serialized field, because
        /// The scene generator (<c>MainMenuScene</c>) constructs this component with the GameObject's own
        /// constructor and <c>Awake</c> fires on that line, in edit mode, before
        /// <see cref="Configure"/> can run.
        /// </para>
        /// </remarks>
        private void Awake()
        {
            if (Application.isPlaying)
            {
                Build();
            }
        }

        /// <remarks>
        /// Every session passes through this scene, so this is where the stored settings become
        /// the settings - see <see cref="GameSettings.Apply"/>.
        /// <para>
        /// <c>Start</c> rather than <c>Awake</c>, and that is not a style choice.
        /// The scene generator (<c>MainMenuScene</c>) constructs this component with the GameObject's own
        /// constructor, which fires <c>Awake</c> synchronously on that line, in edit mode -
        /// so applying the settings there would resize the Unity editor's game view and switch
        /// its quality tier as a side effect of pressing a menu item.
        /// </para>
        /// </remarks>
        /// <summary>
        /// Puts the stored settings into effect and starts the menu's theme.
        /// </summary>
        /// <remarks>
        /// The menu is the first scene in the build, so this is the first music anybody
        /// hears and the only place it is asked for by name - everything after this is the
        /// match, and <see cref="IronFlag.Audio.MatchMusic"/> decides that from what is being
        /// driven. A scene assembled without a player - the still that photographs this menu -
        /// simply has none, which is why the reference is checked rather than assumed.
        /// </remarks>
        private void Start()
        {
            GameSettings.Apply();

            MusicPlayer music = MusicPlayer.Current;
            if (music != null)
            {
                music.Play(MusicKind.MenuTheme);
            }
        }

        private void Update()
        {
            // Unscaled, unlike everything on the HUD. Nothing here is ever drawn over a
            // running match, and a menu that stopped animating because somebody had paused
            // something is a menu that has stopped working - see HudMotion.
            Advance(Time.unscaledDeltaTime);

            Keyboard keys = Keyboard.current;
            if (keys == null || !keys.escapeKey.wasPressedThisFrame)
            {
                return;
            }

            // Escape backs out of a screen and stops at the top one. Quitting the game from
            // there would make the one key every player presses to get out of a submenu also
            // the key that closes the game.
            if (showing == MenuPanel.Levels || showing == MenuPanel.Settings)
            {
                Show(MenuPanel.Root);
            }
        }

        /// <summary>
        /// Moves the screen that is up a little further into place.
        /// </summary>
        /// <param name="deltaTime">Seconds since this was last called.</param>
        /// <remarks>
        /// A screen fades up and rises the last few units into place rather than appearing.
        /// It is a small thing and it is doing one job: three screens that share a column and
        /// a set of anchors are otherwise indistinguishable at the moment they swap, and a
        /// player who presses PLAY and gets a differently-worded list in the same rectangle
        /// has to read it to find out anything happened. Motion says "this is a new screen"
        /// before a single word of it has been read.
        /// </remarks>
        private void Advance(float deltaTime)
        {
            if (entered >= 1.0f)
            {
                return;
            }

            entered = HudMotion.Ease(entered, 1.0f, HudMotion.FadeRate, deltaTime);
            ApplyEntry();
        }

        /// <summary>
        /// Puts the screen that is up where its arrival has got to.
        /// </summary>
        private void ApplyEntry()
        {
            CanvasGroup fade = FadeOf(showing);
            if (fade == null)
            {
                return;
            }

            float left = 1.0f - entered;
            fade.alpha = entered;

            var rect = (RectTransform)fade.transform;
            rect.anchoredPosition = panelRest - new Vector2(0.0f, left * EntryRise);
        }

        /// <summary>
        /// Returns the fade belonging to one of the three screens.
        /// </summary>
        /// <param name="panel">The screen to look up.</param>
        /// <returns>Its canvas group, or nothing at all before the menu is built.</returns>
        private CanvasGroup FadeOf(MenuPanel panel)
        {
            switch (panel)
            {
                case MenuPanel.Root:
                    return rootFade;
                case MenuPanel.Levels:
                    return levelsFade;
                case MenuPanel.Settings:
                    return settingsFade;
                default:
                    return null;
            }
        }

        private void BuildHeader(RectTransform column)
        {
            float inner = ColumnWidth - (Margin * 2.0f);

            // The game's name is the one place a stencil belongs most obviously, and one of
            // only three in the whole project entitled to it - see HudPalette.DisplayFace.
            Text title = HudPalette.Headline("Title", column, 62, TextAnchor.LowerLeft);
            title.text = "IRON FLAG";
            Hang(title.rectTransform, Margin, -Margin - 20.0f, inner, 72.0f);

            Text subtitle = EditorTheme.Label("Subtitle", column, 20, TextAnchor.UpperLeft);
            subtitle.text = "A RETURN FIRE HOMAGE";
            subtitle.color = EditorTheme.FadedInk;
            Hang(subtitle.rectTransform, Margin, -Margin - 96.0f, inner, 26.0f);

            Image rule = EditorTheme.Plate("Rule", column, EditorTheme.Header);
            rule.raycastTarget = false;
            Hang(rule.rectTransform, Margin, -HeaderHeight + 26.0f, inner, 2.0f);
        }

        private void BuildNote(RectTransform column)
        {
            note = EditorTheme.Label("Note", column, 18, TextAnchor.LowerLeft);
            note.color = EditorTheme.FadedInk;
            EditorTheme.Place(
                note.rectTransform, Margin, Margin, ColumnWidth - (Margin * 2.0f), NoteHeight);
        }

        /// <summary>
        /// Creates one of the three screens: an empty rectangle filling the column between the
        /// title and the note.
        /// </summary>
        private static RectTransform BuildPanel(RectTransform column, string name)
        {
            var host = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            host.transform.SetParent(column, false);

            var rect = host.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(Margin, Margin + NoteHeight + Gutter);
            rect.offsetMax = new Vector2(-Margin, -HeaderHeight);
            return rect;
        }

        private void BuildRoot()
        {
            // Worked out from the constants rather than measured off the panel, because a
            // canvas that has never been laid out reports whatever size it was created at -
            // and the layout pass that would fix it never runs at all in the scene the menu's
            // still is rendered from.
            float inner = ColumnWidth - (Margin * 2.0f);
            float top = 0.0f;

            Make(rootPanel, "Play", "PLAY", top, inner, () => Show(MenuPanel.Levels));
            top -= RowHeight + Gutter;

            Make(rootPanel, "Editor", "LEVEL EDITOR", top, inner, OpenEditor);
            top -= RowHeight + Gutter;

            Make(rootPanel, "Settings", "SETTINGS", top, inner, () => Show(MenuPanel.Settings));
            top -= RowHeight + Gutter;

            Make(rootPanel, "Quit", "QUIT", top, inner, Quit);
        }

        private void BuildLevels()
        {
            float inner = ColumnWidth - (Margin * 2.0f);

            Text heading = EditorTheme.Label("Levels Title", levelsPanel, 24, TextAnchor.UpperLeft);
            heading.text = "CHOOSE A MAP";
            heading.color = EditorTheme.FadedInk;
            Hang(heading.rectTransform, 0.0f, 0.0f, inner, 28.0f);

            float top = -40.0f;
            for (int row = 0; row < LevelRows; row++)
            {
                EditorButton button = EditorTheme.Button(
                    $"Level {row}", levelsPanel, string.Empty, 25, null);
                Hang(button.Rect, 0.0f, top, inner, LevelRowHeight - Gutter);
                top -= LevelRowHeight;

                // The caption a fresh button is born with fills its plate and is centred, which
                // is right for a command and wrong for a row that carries two lines about a
                // map. It is the only text on a new button, so this is a lookup rather than a
                // guess about the hierarchy.
                Text caption = button.Rect.GetComponentInChildren<Text>();
                caption.alignment = TextAnchor.LowerLeft;
                EditorTheme.Place(caption.rectTransform, 16.0f, 32.0f, inner - 32.0f, 28.0f);

                Text under = EditorTheme.Label($"Level {row} Note", button.Rect, 17, TextAnchor.UpperLeft);
                under.color = EditorTheme.FadedInk;
                EditorTheme.Place(under.rectTransform, 16.0f, 8.0f, inner - 32.0f, 24.0f);

                levelButtons.Add(button);
                levelNotes.Add(under);
            }

            float half = (inner - Gutter) * 0.5f;

            EditorButton back = EditorTheme.Button(
                "Levels Back", levelsPanel, "BACK", 22, () => Show(MenuPanel.Root), SfxKind.UiBack);
            Foot(back.Rect, 0.0f, half);

            moreButton = EditorTheme.Button("Levels More", levelsPanel, "MORE", 22, NextPage);
            Foot(moreButton.Rect, half + Gutter, half);
        }

        private void BuildSettings()
        {
            float inner = ColumnWidth - (Margin * 2.0f);

            Text heading = EditorTheme.Label("Settings Title", settingsPanel, 24, TextAnchor.UpperLeft);
            heading.text = "SETTINGS";
            heading.color = EditorTheme.FadedInk;
            Hang(heading.rectTransform, 0.0f, 0.0f, inner, 28.0f);

            float top = -46.0f;

            fullscreenButton = Row(
                "Screen", "SCREEN", top, inner, () => GameSettings.SetFullscreen(!GameSettings.Fullscreen));
            top -= RowHeight + Gutter;

            sizeButton = Stepper("Size", "SIZE", top, inner, StepSize);
            top -= RowHeight + Gutter;

            qualityButton = Stepper("Quality", "DETAIL", top, inner, StepQuality);
            top -= RowHeight + Gutter;

            // Two volumes rather than one. The soundtrack is the half a player turns down
            // after the fortieth match; the gunfire is the half telling them where they are
            // being shot from, and tying the two together makes silencing one silence both.
            soundButton = Stepper("Sound", "SOUND", top, inner, StepSound, SfxKind.None);
            top -= RowHeight + Gutter;

            musicButton = Stepper("Music", "MUSIC", top, inner, StepMusic);
            top -= RowHeight + (Gutter * 2.0f);

            Text explain = EditorTheme.Paragraph("Settings Note", settingsPanel, 17);
            explain.color = EditorTheme.FadedInk;
            Hang(explain.rectTransform, 0.0f, top, inner, 96.0f);
            explain.text =
                "Every setting is remembered between sessions. Detail chooses which render "
                + "pipeline tier the game draws with; the volumes take effect as you press them.";

            EditorButton back = EditorTheme.Button(
                "Settings Back", settingsPanel, "BACK", 22, () => Show(MenuPanel.Root), SfxKind.UiBack);
            Foot(back.Rect, 0.0f, inner);
        }

        /// <summary>
        /// Builds and places one settings row's caption label, shared by <see cref="Row"/>
        /// and <see cref="Stepper"/> so the two kinds of row still label themselves the same
        /// way.
        /// </summary>
        private void Caption(string name, string text, float top)
        {
            Text label = EditorTheme.Label($"{name} Caption", settingsPanel, 20, TextAnchor.MiddleLeft);
            label.text = text;
            label.color = EditorTheme.FadedInk;
            Hang(label.rectTransform, 0.0f, top, CaptionWidth, RowHeight);
        }

        /// <summary>
        /// Builds a settings row: a caption on the left and one wide button on the right.
        /// </summary>
        private EditorButton Row(
            string name, string caption, float top, float inner, System.Action does)
        {
            Caption(name, caption, top);

            float width = inner - CaptionWidth - Gutter;
            EditorButton button = EditorTheme.Button(
                name, settingsPanel, string.Empty, 21, does, SfxKind.UiSelect);
            Hang(button.Rect, CaptionWidth + Gutter, top, width, RowHeight);
            return button;
        }

        /// <summary>
        /// Builds a settings row that steps through a list: an arrow, a value, an arrow.
        /// </summary>
        /// <remarks>
        /// A stepper rather than a drop-down or a scrolling list, because both of the things
        /// stepped through here are short - a handful of resolutions the interface fits in, two
        /// quality tiers - and a list widget for four items is a list widget that has to be
        /// built, laid out and dismissed.
        /// </remarks>
        private EditorButton Stepper(
            string name, string caption, float top, float inner, System.Action<int> step)
            => Stepper(name, caption, top, inner, step, SfxKind.UiSelect);

        /// <summary>
        /// Builds a stepper whose arrows make a particular noise.
        /// </summary>
        /// <remarks>
        /// One caller, and it wants silence: the sound stepper plays its own sample
        /// <em>after</em> it has applied the change, because a volume control that clicked at
        /// the level you just left is the one control on this panel that would be lying. See
        /// <see cref="StepSound"/>; the arrow click and that sample are the same press, and
        /// only one of them can be it.
        /// </remarks>
        private EditorButton Stepper(
            string name,
            string caption,
            float top,
            float inner,
            System.Action<int> step,
            SfxKind arrow)
        {
            Caption(name, caption, top);

            float left = CaptionWidth + Gutter;
            float width = inner - left - (ArrowWidth * 2.0f) - (Gutter * 2.0f);

            EditorButton down = EditorTheme.Button(
                $"{name} Down", settingsPanel, "<", 22, () => step(-1), arrow);
            Hang(down.Rect, left, top, ArrowWidth, RowHeight);

            EditorButton value = EditorTheme.Button($"{name} Value", settingsPanel, string.Empty, 21, null);
            Hang(value.Rect, left + ArrowWidth + Gutter, top, width, RowHeight);

            EditorButton up = EditorTheme.Button(
                $"{name} Up", settingsPanel, ">", 22, () => step(1), arrow);
            Hang(up.Rect, left + ArrowWidth + Gutter + width + Gutter, top, ArrowWidth, RowHeight);

            return value;
        }

        private void StepSize(int by)
        {
            List<Vector2Int> sizes = GameSettings.Sizes();
            if (sizes.Count == 0)
            {
                return;
            }

            int at = sizes.IndexOf(GameSettings.Size);
            GameSettings.SetSize(sizes[Wrap(at + by, sizes.Count)]);
            RefreshSettings();
        }

        private void StepQuality(int by)
        {
            List<string> tiers = GameSettings.Qualities();
            if (tiers.Count == 0)
            {
                return;
            }

            int at = tiers.IndexOf(GameSettings.Quality);
            if (at < 0)
            {
                // A stored tier that no longer exists - see GameSettings.Apply's own
                // remarks. Left alone rather than guessed at, the same rule Apply() keeps.
                return;
            }

            GameSettings.SetQuality(tiers[Wrap(at + by, tiers.Count)]);
            RefreshSettings();
        }

        /// <summary>
        /// Steps the sound effects up or down one notch.
        /// </summary>
        /// <param name="by">Which way to step: -1 quieter, 1 louder.</param>
        /// <remarks>
        /// Clamped at both ends rather than wrapped, unlike the two steppers above it. A
        /// resolution list has no quiet end and a player stepping off it wants to come round;
        /// a volume does, and coming round from OFF to full is how somebody deafens
        /// themselves at two in the morning.
        /// </remarks>
        private void StepSound(int by)
        {
            GameSettings.SetSoundVolume(
                GameSettings.SoundVolume + (by * GameSettings.VolumeStep));
            RefreshSettings();

            // Played after the change rather than by the arrow itself, so the press is heard
            // at the level it has just set - which is the only way a volume control tells you
            // anything. It is also the only feedback there is when stepping up off OFF, where
            // a click fired before the change would have been played at a volume of nothing.
            Sfx.Play(SfxKind.UiSelect);
        }

        /// <summary>
        /// Steps the music up or down one notch.
        /// </summary>
        /// <param name="by">Which way to step: -1 quieter, 1 louder.</param>
        /// <remarks>
        /// No sample to play afterwards, unlike <see cref="StepSound"/>: the music is already
        /// playing, and <see cref="IronFlag.Audio.MusicPlayer"/> reads the setting every frame,
        /// so the bed under the menu changes level as the button is pressed.
        /// </remarks>
        private void StepMusic(int by)
        {
            GameSettings.SetMusicVolume(
                GameSettings.MusicVolume + (by * GameSettings.VolumeStep));
            RefreshSettings();
        }

        private void RefreshSettings()
        {
            if (fullscreenButton == null)
            {
                return;
            }

            bool full = GameSettings.Fullscreen;
            fullscreenButton.SetText(full ? "FULLSCREEN" : "WINDOWED");
            fullscreenButton.SetChosen(full);

            sizeButton.SetText(GameSettings.NameOfSize(GameSettings.Size));
            qualityButton.SetText(GameSettings.NameOfQuality(GameSettings.Quality));
            soundButton.SetText(GameSettings.NameOfVolume(GameSettings.SoundVolume));
            musicButton.SetText(GameSettings.NameOfVolume(GameSettings.MusicVolume));

            // The size is what the window is, not what it can be, so it stops meaning anything
            // while the game is filling the screen - and a stepper that changed a number
            // nothing acted on would be the worst kind of setting.
            sizeButton.SetEnabled(!full);
        }

        /// <summary>
        /// Reads every level file there is, so the list can say what each map is.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Read rather than merely listed, which is the difference between this and the
        /// editor's open panel: a menu is where somebody chooses a map they have not seen, and
        /// a column of file names tells them nothing. What it costs is one parse per map on the
        /// way into the list, which for a folder of a dozen text files is nothing.
        /// </para>
        /// <para>
        /// A map that cannot be <em>read</em> is listed and refused, with the parse error where
        /// its size would be. A map that reads but breaks a rule is offered as normal, and
        /// judged by nothing here: whether a map plays is
        /// <see cref="LevelValidation"/>'s question, it is advisory everywhere else in this
        /// game, and a menu that quietly hid maps would be the one place it was not.
        /// </para>
        /// </remarks>
        private void ReadMaps()
        {
            cards.Clear();

            foreach (string name in LevelLibrary.Names())
            {
                if (!LevelFile.TryRead(LevelLibrary.PathFor(name), out LevelDefinition level, out string problem))
                {
                    cards.Add(new MapCard(name, name.ToUpperInvariant(), problem, false));
                    continue;
                }

                string title = string.IsNullOrWhiteSpace(level.Name) ? name : level.Name;
                cards.Add(new MapCard(name, title.ToUpperInvariant(), Describe(name, level), true));
            }
        }

        /// <summary>
        /// Returns the line under a map's name on the list.
        /// </summary>
        /// <param name="name">The level name, which is what the file is called.</param>
        /// <param name="level">The map that was read.</param>
        /// <returns>One line about what the map is.</returns>
        /// <remarks>
        /// How many people it is for comes first, and only when the answer is one. Every
        /// other map on the list is a two-player match - that is what this game was until
        /// now - so marking those would be marking all of them, while a player looking for
        /// something to play on their own is looking for exactly this word.
        /// </remarks>
        private static string Describe(string name, LevelDefinition level)
        {
            float across = level.Bounds == null ? 0.0f : level.Bounds.HalfExtent * 2.0f;
            string seats = level.IsSolo ? "1 PLAYER   ·   " : string.Empty;
            return $"{seats}{name}   ·   {across:0} m across   ·   {level.Towers.Length} towers"
                + $"   ·   {level.Structures.Length} props";
        }

        private void RefreshLevels()
        {
            if (levelButtons.Count == 0)
            {
                return;
            }

            int pages = Pages();
            page = Mathf.Clamp(page, 0, Mathf.Max(0, pages - 1));
            int first = page * LevelRows;

            for (int row = 0; row < levelButtons.Count; row++)
            {
                int at = first + row;
                EditorButton button = levelButtons[row];
                bool used = at < cards.Count;

                button.SetVisible(used);
                levelNotes[row].gameObject.SetActive(used);

                if (!used)
                {
                    continue;
                }

                MapCard card = cards[at];
                button.SetText(card.Title);
                button.SetEnabled(card.CanBePlayed);
                levelNotes[row].text = card.Note;
                levelNotes[row].color = card.CanBePlayed ? EditorTheme.FadedInk : EditorTheme.Problem;

                string chosen = card.File;
                button.OnPress(() => PlayLevel(chosen));
            }

            moreButton.SetVisible(pages > 1);
            moreButton.SetText(pages > 1 ? $"MORE  {page + 1}/{pages}" : "MORE");

            if (cards.Count == 0)
            {
                Say("No maps found. Build one in the level editor.");
            }
        }

        private void NextPage()
        {
            int pages = Pages();
            if (pages <= 1)
            {
                return;
            }

            page = Wrap(page + 1, pages);
            RefreshLevels();
        }

        private int Pages()
            => cards.Count == 0 ? 1 : Mathf.CeilToInt(cards.Count / (float)LevelRows);

        private EditorButton Make(
            RectTransform panel, string name, string words, float top, float inner, System.Action does)
        {
            EditorButton button = EditorTheme.Button(name, panel, words, 28, does);
            Hang(button.Rect, 0.0f, top, inner, RowHeight);
            return button;
        }

        /// <summary>
        /// Places a rectangle down from the top left of its parent.
        /// </summary>
        private static void Hang(RectTransform rect, float left, float top, float width, float height)
        {
            rect.anchorMin = new Vector2(0.0f, 1.0f);
            rect.anchorMax = new Vector2(0.0f, 1.0f);
            rect.pivot = new Vector2(0.0f, 1.0f);
            rect.anchoredPosition = new Vector2(left, top);
            rect.sizeDelta = new Vector2(width, height);
        }

        /// <summary>
        /// Pins a rectangle to the bottom left of its parent, whatever height the panel is.
        /// </summary>
        /// <remarks>
        /// The one row on each screen that is measured from the bottom. A window can be any
        /// height, and a BACK button hung from the top would be a button that sits under the
        /// last map on one screen and in the middle of the column on another.
        /// </remarks>
        private static void Foot(RectTransform rect, float left, float width)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(left, 0.0f);
            rect.sizeDelta = new Vector2(width, RowHeight);
        }

        private static int Wrap(int at, int count) => count <= 0 ? 0 : ((at % count) + count) % count;

        private void Clear()
        {
            levelButtons.Clear();
            levelNotes.Clear();
            cards.Clear();
            menu = null;

            Transform existing = transform.Find(MenuNodeName);
            if (existing == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(existing.gameObject);
            }
            else
            {
                DestroyImmediate(existing.gameObject);
            }
        }

        /// <summary>
        /// One map as the list shows it: what it is called, what it is, and whether it can be
        /// played at all.
        /// </summary>
        private sealed class MapCard
        {
            internal MapCard(string file, string title, string note, bool canBePlayed)
            {
                File = file;
                Title = title;
                Note = note;
                CanBePlayed = canBePlayed;
            }

            /// <summary>The level name, which is what the file is called.</summary>
            internal string File { get; }

            /// <summary>What the map calls itself, which is what the row says.</summary>
            internal string Title { get; }

            /// <summary>The line under the title: what the map is, or why it will not open.</summary>
            internal string Note { get; }

            /// <summary>Whether the file could be read at all.</summary>
            internal bool CanBePlayed { get; }
        }
    }
}
