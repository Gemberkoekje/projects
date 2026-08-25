using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using IronFlag.Core;
using IronFlag.Destruction;
using IronFlag.Levels;
using IronFlag.UI;

namespace IronFlag.Editing
{
    /// <summary>
    /// The level editor's panels: what to do with, what to place, what is selected, what is
    /// wrong with the map, and what everything is called.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Generated rather than authored, like every other piece of interface in this project.
    /// The palette is the rows of <see cref="StructureTuning.Roster"/> and the problem list is
    /// whatever <see cref="LevelValidation"/> says, so a seventh prop or a ninth rule appears
    /// here by existing rather than by somebody opening a prefab and remembering to add it.
    /// </para>
    /// <para>
    /// Everything on it is redrawn from the session on one signal - <c>Changed</c> - rather
    /// than each panel watching its own thing. The panels are small, and an editor whose
    /// inspector is refreshed by a different event from its problem list is one where the two
    /// can end up describing different maps.
    /// </para>
    /// <para>
    /// Two canvases, not one. This is the panel canvas: it scales with the screen, it
    /// raycasts, and it is what <see cref="LevelEditorSession"/> asks before it lets a click
    /// reach the map. The other is <see cref="EditorOverlay"/>'s, which is in raw pixels
    /// because what it draws is pinned to places in the world.
    /// </para>
    /// <para>
    /// Both hang on the editor's camera rather than being screen-space overlays, for the same
    /// reason <see cref="IronFlag.UI.PlayerHud"/> does: an overlay canvas is drawn by the
    /// engine after every camera and therefore appears in nothing that is rendered to a
    /// texture. The command-line still would be a picture of a map with no editor around it.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Editor Ui")]
    [RequireComponent(typeof(Canvas))]
    public sealed class EditorUi : MonoBehaviour
    {
        /// <summary>Name of the generated child everything on the panels hangs off.</summary>
        public const string PanelsNodeName = "Panels";

        /// <summary>Width the panels are laid out for, in canvas units.</summary>
        public const float ReferenceWidth = 1920.0f;

        /// <summary>Height the panels are laid out for, in canvas units.</summary>
        /// <remarks>
        /// Also the step the interface is enlarged in: a screen twice this tall draws
        /// everything at double size, which is what keeps a 4K display from rendering the
        /// whole editor at half the physical size of a 1080p one.
        /// </remarks>
        public const float ReferenceHeight = 1080.0f;

        private const float TopBarHeight = 64.0f;
        private const float StatusHeight = 42.0f;
        private const float LeftWidth = 236.0f;
        private const float RightWidth = 344.0f;
        private const float ProblemsHeight = 268.0f;
        private const float Margin = 12.0f;
        private const float RowHeight = 40.0f;
        private const float Gutter = 6.0f;
        private const float OpenPanelWidth = 520.0f;
        private const float OpenPanelHeight = 560.0f;
        private const float MakePanelWidth = 588.0f;
        private const float MakePanelHeight = 456.0f;
        private const float MakeCaptionWidth = 104.0f;

        /// <summary>How many level files the open panel lists.</summary>
        private const int OpenRows = 11;

        [SerializeField]
        [Tooltip("The editor these panels drive.")]
        private LevelEditorSession session;

        private readonly List<EditorButton> toolButtons = new List<EditorButton>();
        private readonly List<EditTool> toolOrder = new List<EditTool>();
        private readonly List<EditorButton> paletteButtons = new List<EditorButton>();
        private readonly List<StructureKind> paletteOrder = new List<StructureKind>();
        private readonly List<EditorButton> sideButtons = new List<EditorButton>();
        private readonly List<EditorButton> openButtons = new List<EditorButton>();
        private readonly List<EditorButton> sizeButtons = new List<EditorButton>();
        private readonly List<EditorButton> playerButtons = new List<EditorButton>();
        private readonly List<EditorButton> halfButtons = new List<EditorButton>();
        private readonly List<EditorButton> groundButtons = new List<EditorButton>();

        /// <summary>What the generate panel is currently asking for.</summary>
        /// <remarks>
        /// Kept here rather than read back off the buttons, because the buttons are how it is
        /// said and this is what was said. It survives the panel being closed and reopened, so
        /// somebody generating a run of hard channel maps sets that up once.
        /// </remarks>
        private readonly MapOptions wanted = new MapOptions();

        /// <summary>
        /// Whether <see cref="wanted"/> has ever been given a seed.
        /// </summary>
        /// <remarks>
        /// <see cref="MapOptions.Seed"/> has no value that means "unset" - zero is a seed like
        /// any other - so the panel cannot tell a fresh roll from a typed zero by reading the
        /// field back. This is the difference, set once on the panel's first showing and never
        /// touched again, so a zero typed in on purpose survives every later reopening.
        /// </remarks>
        private bool seedRolled;

        private string armed = string.Empty;
        private int armedAtVersion = -1;
        private int rolls;
        private RectTransform panels;
        private RectTransform openPanel;
        private RectTransform makePanel;
        private InputField seedField;
        private Text makeNote;
        private EditorInspector inspector;
        private InputField fileField;
        private EditorButton saveButton;
        private EditorButton revertButton;
        private EditorButton undoButton;
        private EditorButton redoButton;
        private EditorButton gridButton;
        private Text dirtyMark;
        private Text problemsTitle;
        private Text problemsBody;
        private Text cursorLine;
        private Text noteLine;

        /// <summary>The editor these panels drive.</summary>
        public LevelEditorSession Session => session;

        /// <summary>Whether the panels have been generated yet.</summary>
        public bool IsBuilt => panels != null;

        /// <summary>The panel that lists the level files, shown only while opening one.</summary>
        public RectTransform OpenPanel => openPanel;

        /// <summary>The panel that asks what map to draw, shown only while generating one.</summary>
        public RectTransform MakePanel => makePanel;

        /// <summary>
        /// Points the panels at the editor they drive and sets the canvas up.
        /// </summary>
        /// <param name="editor">The session.</param>
        /// <param name="view">The camera the panels are drawn in front of.</param>
        public void Configure(LevelEditorSession editor, Camera view)
        {
            session = editor;

            // Before Build, so everything it generates is born on the right layer. This is
            // what keeps the panels off the world camera and therefore out of the grade -
            // see IronFlag.Core.ViewStack for why an editor cannot simply use an overlay
            // canvas instead.
            int layer = InterfaceLayers.EditorLayer();
            if (layer >= 0)
            {
                gameObject.layer = layer;
            }

            // The scene generator constructs this component with the GameObject's own
            // constructor - new GameObject(name, ..., typeof(EditorUi)) - which fires Awake
            // synchronously, on that very line, before this method ever runs. Awake used to
            // build the panels unconditionally and threw the moment it reached the
            // inspector, which reads session.Level - so every regeneration of the scene
            // logged a NullReferenceException and saved a half-built hierarchy with no
            // status bar and no open-map dialog. Building here instead, now that a session
            // actually exists, is what Awake was trying to do too early.
            if (!IsBuilt)
            {
                Build();
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

            // Pixel-exact rather than scaled to the screen, which is the opposite of what the
            // HUD does and is right for the opposite reason. A HUD is read at a glance while
            // driving, so it should take the same share of any screen; an editor is worked on,
            // and a bigger screen should mean more map rather than bigger buttons. It also
            // makes a canvas unit a pixel, which is what lets everything here work out how
            // much of the view it is covering without measuring anything - see
            // <see cref="ReportUsableArea"/>.
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1.0f;

            if (GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            InterfaceLayers.Paint(gameObject);
        }

        /// <summary>
        /// Generates the panels, replacing any that were there before.
        /// </summary>
        public void Build()
        {
            Clear();

            var host = new GameObject(PanelsNodeName, typeof(RectTransform));
            host.transform.SetParent(transform, false);
            panels = host.GetComponent<RectTransform>();
            panels.anchorMin = Vector2.zero;
            panels.anchorMax = Vector2.one;
            panels.offsetMin = Vector2.zero;
            panels.offsetMax = Vector2.zero;

            BuildTopBar();
            BuildToolColumn();
            BuildInspectorColumn();
            BuildStatusBar();
            BuildOpenPanel();
            BuildMakePanel();

            ReportUsableArea();
            Refresh();

            // Everything on these panels is generated, and generated objects arrive on the
            // default layer. Once here is enough: Refresh only ever changes text and which
            // rows are visible, and the inspector works from a pool of rows built above.
            InterfaceLayers.Paint(gameObject);
        }

        /// <summary>
        /// Tells the camera how much of the view these panels are covering.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Computed straight from the camera's own pixel size and the scale this canvas is
        /// drawn at - never by measuring a laid-out rectangle. That is not a micro-optimisation:
        /// a canvas reports whatever size it was created at until a layout pass has run, and
        /// the layout pass that normally happens at the end of a frame never runs at all in the
        /// scene the command-line still is rendered from. Measuring it there gave a canvas 640
        /// units wide, a tool column that was apparently a third of the screen, and a map
        /// framed at the zoom limit in the wrong half of the picture.
        /// </para>
        /// <para>
        /// Without any of it, framing a map centres it on the screen, which puts a fifth of it
        /// behind the inspector and a seventh behind the tools. That is not a rounding error on
        /// a map whose whole point is that both halves are the same shape.
        /// </para>
        /// </remarks>
        private void ReportUsableArea()
        {
            EditorCameraRig rig = session == null ? null : session.View;
            if (rig == null)
            {
                return;
            }

            Camera view = rig.View;
            float across = view.pixelWidth;
            float up = view.pixelHeight;
            if (across <= 0.0f || up <= 0.0f)
            {
                return;
            }

            float scale = ApplyScale(up);

            rig.SetUsableArea(Rect.MinMaxRect(
                LeftWidth * scale / across,
                StatusHeight * scale / up,
                1.0f - (RightWidth * scale / across),
                1.0f - (TopBarHeight * scale / up)));
        }

        /// <summary>
        /// Sets how big the panels are drawn, and returns it.
        /// </summary>
        /// <param name="up">Height of the view, in pixels.</param>
        /// <returns>Pixels per canvas unit.</returns>
        /// <remarks>
        /// Whole steps rather than a smooth ratio, because the type on these panels is a
        /// bitmap font at a fixed size: doubling it is crisp and multiplying it by 1.3 is a row
        /// of blurred captions. One step per <see cref="ReferenceHeight"/>, so a 4K screen
        /// draws the editor at double size and a 1440p one draws it at the size it was
        /// designed at.
        /// </remarks>
        private float ApplyScale(float up)
        {
            float wanted = Mathf.Max(1.0f, Mathf.Floor(up / ReferenceHeight));

            var scaler = GetComponent<CanvasScaler>();
            if (scaler != null && !Mathf.Approximately(scaler.scaleFactor, wanted))
            {
                scaler.scaleFactor = wanted;
            }

            return wanted;
        }

        /// <summary>
        /// Brings every panel up to date with the editor behind it.
        /// </summary>
        public void Refresh()
        {
            if (!IsBuilt || session == null)
            {
                return;
            }

            for (int slot = 0; slot < toolButtons.Count; slot++)
            {
                toolButtons[slot].SetChosen(toolOrder[slot] == session.Tool);
            }

            for (int slot = 0; slot < paletteButtons.Count; slot++)
            {
                paletteButtons[slot].SetChosen(
                    session.Tool == EditTool.Structure && paletteOrder[slot] == session.PaletteKind);
            }

            IReadOnlyList<Team> playable = LevelEditorSession.PlayableSides();
            for (int slot = 0; slot < sideButtons.Count && slot < playable.Count; slot++)
            {
                sideButtons[slot].SetChosen(playable[slot] == session.PaletteSide);
            }

            if (fileField != null && !fileField.isFocused)
            {
                fileField.text = session.LevelName;
            }

            undoButton.SetEnabled(session.CanUndo);
            redoButton.SetEnabled(session.CanRedo);
            saveButton.SetEnabled(session.Level != null);
            revertButton.SetEnabled(session.Level != null);

            dirtyMark.text = session.IsDirty ? "UNSAVED" : "SAVED";
            dirtyMark.color = session.IsDirty ? EditorTheme.Unsaved : EditorTheme.Clean;

            gridButton.SetText(GridCaption());

            // Here as well as in LateUpdate, because the command-line still never runs one -
            // and framing a map on the wrong window is the difference between a picture of an
            // editor and a picture of a map with panels beside it.
            ReportUsableArea();
            RefreshProblems();

            noteLine.text = session.Note;
            cursorLine.text =
                $"x {session.Pointer.x:0.#}   z {session.Pointer.z:0.#}";

            inspector.Refresh();
        }

        /// <remarks>
        /// Guarded on <c>session</c> rather than building unconditionally, because Awake fires
        /// the instant this component is added - including from
        /// <c>new GameObject(name, ..., typeof(EditorUi))</c>, before the very next line can
        /// call <see cref="Configure"/>. A scene loaded from disk is the opposite case: Unity
        /// restores <c>[SerializeField] session</c> before Awake runs at all, so this still
        /// builds immediately there, exactly as it always has.
        /// </remarks>
        private void Awake()
        {
            if (!IsBuilt && session != null)
            {
                Build();
            }
        }

        private void OnEnable()
        {
            if (session != null)
            {
                session.Changed += Refresh;
            }
        }

        private void OnDisable()
        {
            if (session != null)
            {
                session.Changed -= Refresh;
            }
        }

        /// <summary>
        /// The cursor's position is the one reading that changes without anything happening,
        /// so it is the one thing here that cannot wait for a change to be signalled - and the
        /// window can be resized without anything happening either.
        /// </summary>
        private void LateUpdate()
        {
            if (!IsBuilt || session == null)
            {
                return;
            }

            cursorLine.text = $"x {session.Pointer.x:0.#}   z {session.Pointer.z:0.#}";
            ReportUsableArea();
        }

        /// <summary>
        /// Builds the strip along the top: which file, and what to do with it.
        /// </summary>
        private void BuildTopBar()
        {
            Image plate = EditorTheme.Plate("Top Bar", panels, EditorTheme.Chrome);
            EditorTheme.Cling(plate.rectTransform, RectTransform.Edge.Top, TopBarHeight, 0.0f, 0.0f);

            RectTransform bar = plate.rectTransform;
            float bottom = (TopBarHeight - RowHeight) * 0.5f;
            float left = Margin;

            Text caption = EditorTheme.Label("File Caption", bar, 19, TextAnchor.MiddleLeft);
            caption.color = EditorTheme.FadedInk;
            EditorTheme.Place(caption.rectTransform, left, bottom, 48.0f, RowHeight);
            left += 52.0f;

            fileField = EditorTheme.Field("File", bar, 20, typed => Rename(typed));
            EditorTheme.Place((RectTransform)fileField.transform, left, bottom, 260.0f, RowHeight);
            caption.text = "FILE";
            left += 260.0f + Gutter;

            left = Command(
                bar, "New", "NEW", left, bottom, 84.0f,
                () => Guard("new", "Starting a new map", () => session.NewLevel()));
            left = Command(
                bar, "Open", "OPEN", left, bottom, 92.0f,
                () => Guard("open", "Opening another map", ShowOpenPanel));

            // Not guarded, and it is the one button on this bar that is not. Opening the panel
            // throws nothing away - it has a CANCEL - and the press that does throw the map
            // away is the panel's own, which is guarded there. Guarding both would mean four
            // presses to draw a map, on the one feature whose whole appeal is pressing it
            // again. The open panel's rows are guarded individually for exactly this reason:
            // what has to be guarded is whatever actually discards, not whatever leads to it.
            left = Command(
                bar, "Generate", "GENERATE", left, bottom, 128.0f, ShowMakePanel);

            saveButton = Make(bar, "Save", "SAVE", left, bottom, 96.0f, () => session.Save());
            left += 96.0f + Gutter;

            revertButton = Make(
                bar, "Revert", "REVERT", left, bottom, 108.0f,
                () => Guard("revert", "Reverting", () => session.Revert()));
            left += 108.0f + Gutter * 2.0f;

            undoButton = Make(bar, "Undo", "UNDO", left, bottom, 84.0f, () => session.Undo());
            left += 84.0f + Gutter;

            redoButton = Make(bar, "Redo", "REDO", left, bottom, 84.0f, () => session.Redo());
            left += 84.0f + Gutter;

            dirtyMark = EditorTheme.Label("Dirty", bar, 19, TextAnchor.MiddleLeft);
            EditorTheme.Place(dirtyMark.rectTransform, left + Gutter, bottom, 140.0f, RowHeight);

            // Play is the only button on this bar that leaves the editor, so it goes on the
            // far side of it, away from everything that does not.
            EditorButton play = Make(
                bar, "Play", "PLAY THIS MAP", 0.0f, bottom, 210.0f, () => session.Playtest());
            RectTransform playRect = play.Rect;
            playRect.anchorMin = new Vector2(1.0f, 0.0f);
            playRect.anchorMax = new Vector2(1.0f, 0.0f);
            playRect.pivot = new Vector2(1.0f, 0.0f);
            playRect.anchoredPosition = new Vector2(-Margin, bottom);
        }

        /// <summary>
        /// Builds the column down the left: the tools, and what they place.
        /// </summary>
        private void BuildToolColumn()
        {
            Image plate = EditorTheme.Plate("Tools", panels, EditorTheme.Chrome);
            EditorTheme.Cling(
                plate.rectTransform, RectTransform.Edge.Left, LeftWidth, StatusHeight, TopBarHeight);

            RectTransform column = plate.rectTransform;
            float inner = LeftWidth - (Margin * 2.0f);
            float top = -Margin;

            top = Heading(column, "TOOLS", top, inner);

            AddTool(column, EditTool.Select, "1  SELECT", ref top, inner);
            AddTool(column, EditTool.Land, "2  LAND", ref top, inner);
            AddTool(column, EditTool.Structure, "3  PROP", ref top, inner);
            AddTool(column, EditTool.Tower, "4  TOWER", ref top, inner);
            AddTool(column, EditTool.Bunker, "5  BUNKER", ref top, inner);

            top -= Gutter * 2.0f;
            top = Heading(column, "PROPS", top, inner);

            foreach (StructureKind kind in LevelEdits.Palette())
            {
                StructureKind placing = kind;
                EditorButton button = Make(
                    column, $"Palette {kind}", Spaced(kind.ToString()), Margin, 0.0f, inner,
                    () => session.SetPaletteKind(placing));
                Hang(button.Rect, Margin, top, inner, RowHeight - Gutter);
                top -= RowHeight;

                paletteButtons.Add(button);
                paletteOrder.Add(kind);
            }

            top -= Gutter * 2.0f;
            top = Heading(column, "SIDE", top, inner);

            IReadOnlyList<Team> playable = LevelEditorSession.PlayableSides();
            float half = (inner - Gutter) * 0.5f;
            for (int slot = 0; slot < playable.Count; slot++)
            {
                Team side = playable[slot];
                EditorButton button = Make(
                    column, $"Side {side}", side.ToString().ToUpperInvariant(), Margin, 0.0f, half,
                    () => session.SetPaletteSide(side));
                Hang(button.Rect, Margin + (slot * (half + Gutter)), top, half, RowHeight - Gutter);
                sideButtons.Add(button);
            }

            top -= RowHeight + (Gutter * 2.0f);

            gridButton = Make(
                column, "Grid", "GRID", Margin, 0.0f, inner, () => session.CycleGrid());
            Hang(gridButton.Rect, Margin, top, inner, RowHeight - Gutter);
        }

        /// <summary>
        /// Builds the column down the right: what is selected, and what is wrong.
        /// </summary>
        private void BuildInspectorColumn()
        {
            Image plate = EditorTheme.Plate("Inspector", panels, EditorTheme.Chrome);
            EditorTheme.Cling(
                plate.rectTransform, RectTransform.Edge.Right, RightWidth, StatusHeight, TopBarHeight);

            inspector = EditorInspector.Build(plate.rectTransform, session, RightWidth);

            Image trouble = EditorTheme.Plate("Problems", plate.rectTransform, EditorTheme.Header);
            EditorTheme.Cling(
                trouble.rectTransform, RectTransform.Edge.Bottom, ProblemsHeight, 0.0f, 0.0f);

            problemsTitle = EditorTheme.Label(
                "Problems Title", trouble.rectTransform, 20, TextAnchor.MiddleLeft);
            Hang(
                problemsTitle.rectTransform, Margin, -Margin, RightWidth - (Margin * 2.0f), 26.0f);

            problemsBody = EditorTheme.Paragraph("Problems Body", trouble.rectTransform, 17);
            Hang(
                problemsBody.rectTransform,
                Margin,
                -Margin - 30.0f,
                RightWidth - (Margin * 2.0f),
                ProblemsHeight - Margin - 40.0f);
        }

        /// <summary>
        /// Builds the strip along the bottom: where the cursor is, what just happened, and
        /// which keys do what.
        /// </summary>
        private void BuildStatusBar()
        {
            Image plate = EditorTheme.Plate("Status", panels, EditorTheme.Chrome);
            EditorTheme.Cling(
                plate.rectTransform, RectTransform.Edge.Bottom, StatusHeight, 0.0f, 0.0f);

            // Three readings across one strip, and they must not be able to run into each
            // other: the cursor is a fixed width, the keys are a fixed width against the far
            // edge, and what is left over is the sentence about what just happened.
            const float cursorWidth = 200.0f;
            const float helpWidth = 900.0f;
            float noteLeft = Margin + cursorWidth + Margin;
            float noteWidth = ReferenceWidth - helpWidth - noteLeft - (Margin * 2.0f);

            RectTransform bar = plate.rectTransform;

            cursorLine = EditorTheme.Label("Cursor", bar, 19, TextAnchor.MiddleLeft);
            cursorLine.color = EditorTheme.FadedInk;
            EditorTheme.Place(cursorLine.rectTransform, Margin, 0.0f, cursorWidth, StatusHeight);

            noteLine = EditorTheme.Label("Note", bar, 19, TextAnchor.MiddleLeft);
            EditorTheme.Place(noteLine.rectTransform, noteLeft, 0.0f, noteWidth, StatusHeight);

            Text help = EditorTheme.Label("Help", bar, 16, TextAnchor.MiddleRight);
            help.color = EditorTheme.FadedInk;
            help.text = "RIGHT-DRAG pan · WHEEL zoom · SHIFT force-place · ALT free-move · "
                + "Q/E turn · DEL remove · CTRL+Z undo · CTRL+S save";
            RectTransform helpRect = help.rectTransform;
            helpRect.anchorMin = new Vector2(1.0f, 0.0f);
            helpRect.anchorMax = new Vector2(1.0f, 1.0f);
            helpRect.pivot = new Vector2(1.0f, 0.5f);
            helpRect.anchoredPosition = new Vector2(-Margin, 0.0f);
            helpRect.sizeDelta = new Vector2(helpWidth, 0.0f);
        }

        /// <summary>
        /// Builds the panel that lists the level files, hidden until it is asked for.
        /// </summary>
        /// <remarks>
        /// Both folders at once, which is what <see cref="LevelLibrary.Names"/> already
        /// answers: a map shipped with the game and a map the player has edited are the same
        /// map under the same name, and listing it twice would be a menu that says the player
        /// has two of something they have one of.
        /// </remarks>
        private void BuildOpenPanel()
        {
            Image plate = EditorTheme.Plate("Open", panels, EditorTheme.Chrome);
            openPanel = plate.rectTransform;
            openPanel.anchorMin = new Vector2(0.5f, 0.5f);
            openPanel.anchorMax = new Vector2(0.5f, 0.5f);
            openPanel.pivot = new Vector2(0.5f, 0.5f);
            openPanel.anchoredPosition = Vector2.zero;
            openPanel.sizeDelta = new Vector2(OpenPanelWidth, OpenPanelHeight);

            float inner = OpenPanelWidth - (Margin * 2.0f);

            Text heading = EditorTheme.Label("Open Title", openPanel, 26, TextAnchor.MiddleLeft);
            heading.text = "OPEN A MAP";
            Hang(heading.rectTransform, Margin, -Margin, inner, 32.0f);

            float top = -Margin - 44.0f;
            for (int row = 0; row < OpenRows; row++)
            {
                EditorButton button = Make(
                    openPanel, $"Open {row}", string.Empty, Margin, 0.0f, inner, null);
                Hang(button.Rect, Margin, top, inner, RowHeight - Gutter);
                top -= RowHeight;
                openButtons.Add(button);
            }

            EditorButton close = Make(
                openPanel, "Open Close", "CANCEL", Margin, 0.0f, inner, HideOpenPanel);
            Hang(close.Rect, Margin, -OpenPanelHeight + Margin + RowHeight, inner, RowHeight - Gutter);

            openPanel.gameObject.SetActive(false);
        }

        /// <summary>
        /// Fills the open panel with whatever level files there are and shows it.
        /// </summary>
        private void ShowOpenPanel()
        {
            List<string> names = LevelLibrary.Names();

            for (int row = 0; row < openButtons.Count; row++)
            {
                EditorButton button = openButtons[row];
                bool used = row < names.Count;
                button.SetVisible(used);

                if (!used)
                {
                    continue;
                }

                string name = names[row];
                button.SetText(name);
                button.SetChosen(name == session.LevelName);

                // Guarded the same way the toolbar's own Open button is, and for a reason
                // that button alone cannot cover: the panel stays showing, and the map
                // underneath it stays fully editable - Delete, a drag, anything - the whole
                // time it is up. A row picked after the panel opened clean but the map got
                // dirtied behind it used to discard that work with no warning at all, because
                // only the button that opened the panel was ever asked to check.
                button.OnPress(() => Guard($"open:{name}", $"Opening '{name}'", () =>
                {
                    session.Open(name);
                    HideOpenPanel();
                }));
            }

            openPanel.gameObject.SetActive(true);
            openPanel.SetAsLastSibling();
        }

        private void HideOpenPanel() => openPanel.gameObject.SetActive(false);

        /// <summary>
        /// Builds the panel that asks what kind of map to draw, hidden until it is asked for.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The open panel's shape - a centred plate switched on and off - because it is the
        /// same kind of thing: a question with a handful of answers and a way out of it. Four
        /// rows of choices and a seed, which is the whole of what <see cref="MapOptions"/>
        /// carries; everything else about a generated map comes out of the seed, so the
        /// dialogue asks four questions rather than forty.
        /// </para>
        /// <para>
        /// The seed is on it deliberately. A generator that could not be asked for the same
        /// map twice would be one where "that one was good" is a thing nobody can act on, and
        /// the whole of <see cref="Dice"/> exists so that it can be.
        /// </para>
        /// </remarks>
        private void BuildMakePanel()
        {
            Image plate = EditorTheme.Plate("Generate Panel", panels, EditorTheme.Chrome);
            makePanel = plate.rectTransform;
            makePanel.anchorMin = new Vector2(0.5f, 0.5f);
            makePanel.anchorMax = new Vector2(0.5f, 0.5f);
            makePanel.pivot = new Vector2(0.5f, 0.5f);
            makePanel.anchoredPosition = Vector2.zero;
            makePanel.sizeDelta = new Vector2(MakePanelWidth, MakePanelHeight);

            float inner = MakePanelWidth - (Margin * 2.0f);

            Text heading = EditorTheme.Label("Generate Title", makePanel, 26, TextAnchor.MiddleLeft);
            heading.text = "GENERATE A MAP";
            Hang(heading.rectTransform, Margin, -Margin, inner, 32.0f);

            float top = -Margin - 44.0f;

            top = Choices(
                "SIZE", new[] { "EASY", "MEDIUM", "HARD" }, sizeButtons, top, inner,
                pick =>
                {
                    wanted.Difficulty = (MapDifficulty)(pick + 1);
                    RefreshMakePanel();
                });

            top = Choices(
                "GROUND", new[] { "ANY", "ISLAND", "CHANNEL", "LAGOON" }, groundButtons, top, inner,
                pick =>
                {
                    wanted.Layout = (MapLayout)pick;
                    RefreshMakePanel();
                });

            top = Choices(
                "HALVES", new[] { "MIRRORED", "ASYMMETRICAL" }, halfButtons, top, inner,
                pick =>
                {
                    wanted.Symmetry = (MapSymmetry)(pick + 1);
                    RefreshMakePanel();
                });

            top = Choices(
                "PLAYERS", new[] { "1", "2" }, playerButtons, top, inner,
                pick =>
                {
                    wanted.Players = pick + 1;
                    RefreshMakePanel();
                });

            Text seedCaption = EditorTheme.Label(
                "Seed Caption", makePanel, 18, TextAnchor.MiddleLeft);
            seedCaption.color = EditorTheme.FadedInk;
            seedCaption.text = "SEED";
            Hang(seedCaption.rectTransform, Margin, top, MakeCaptionWidth, RowHeight);

            float fields = Margin + MakeCaptionWidth + Gutter;
            seedField = EditorTheme.Field("Seed", makePanel, 20, typed => Reseed(typed));
            Hang((RectTransform)seedField.transform, fields, top, 180.0f, RowHeight - Gutter);

            EditorButton roll = Make(makePanel, "Seed Roll", "ROLL", 0.0f, 0.0f, 100.0f, Reroll);
            Hang(roll.Rect, fields + 186.0f, top, 100.0f, RowHeight - Gutter);
            top -= RowHeight + Gutter;

            makeNote = EditorTheme.Paragraph("Generate Note", makePanel, 17);
            makeNote.color = EditorTheme.FadedInk;
            Hang(makeNote.rectTransform, Margin, top, inner, 104.0f);

            float bottom = -MakePanelHeight + Margin + RowHeight;
            float half = (inner - Gutter) * 0.5f;

            EditorButton draw = Make(
                makePanel, "Generate Now", "GENERATE", 0.0f, 0.0f, half,
                () => Guard("generate", "Generating a map", MakeNow));
            Hang(draw.Rect, Margin, bottom, half, RowHeight - Gutter);

            EditorButton close = Make(
                makePanel, "Generate Close", "CANCEL", 0.0f, 0.0f, half, HideMakePanel);
            Hang(close.Rect, Margin + half + Gutter, bottom, half, RowHeight - Gutter);

            makePanel.gameObject.SetActive(false);
        }

        /// <summary>
        /// Builds one row of the generate panel: a caption and a run of choices.
        /// </summary>
        /// <param name="caption">What the row is asking.</param>
        /// <param name="words">What is on each button.</param>
        /// <param name="into">List the buttons are kept in, so the chosen one can be lit.</param>
        /// <param name="top">Where the row hangs from.</param>
        /// <param name="inner">How wide the panel is inside its margins.</param>
        /// <param name="chose">What to do with the index that was pressed.</param>
        /// <returns>Where the next row hangs from.</returns>
        /// <remarks>
        /// The index is copied into a local before the closure is made. Capturing the loop
        /// variable itself is the oldest bug in this shape of code, and its symptom is every
        /// button on the row doing what the last one says.
        /// </remarks>
        private float Choices(
            string caption,
            IReadOnlyList<string> words,
            List<EditorButton> into,
            float top,
            float inner,
            System.Action<int> chose)
        {
            Text label = EditorTheme.Label(
                $"{caption} Caption", makePanel, 18, TextAnchor.MiddleLeft);
            label.color = EditorTheme.FadedInk;
            label.text = caption;
            Hang(label.rectTransform, Margin, top, MakeCaptionWidth, RowHeight);

            float left = Margin + MakeCaptionWidth + Gutter;
            float room = inner - MakeCaptionWidth - Gutter;
            float width = (room - (Gutter * (words.Count - 1))) / words.Count;

            for (int pick = 0; pick < words.Count; pick++)
            {
                int chosen = pick;
                EditorButton button = Make(
                    makePanel, $"{caption} {pick}", words[pick], 0.0f, 0.0f, width,
                    () => chose(chosen));
                Hang(button.Rect, left, top, width, RowHeight - Gutter);
                left += width + Gutter;
                into.Add(button);
            }

            return top - RowHeight - Gutter;
        }

        /// <summary>
        /// Fills the generate panel in from what is currently being asked for, and shows it.
        /// </summary>
        private void ShowMakePanel()
        {
            if (!seedRolled)
            {
                wanted.Seed = RolledSeed();
                seedRolled = true;
            }

            RefreshMakePanel();
            makePanel.gameObject.SetActive(true);
            makePanel.SetAsLastSibling();
        }

        /// <summary>
        /// Closes the generate panel without drawing anything.
        /// </summary>
        /// <remarks>
        /// Also forgets an armed GENERATE confirmation, if there is one. Without this, arming
        /// it, closing the panel with CANCEL, and coming back later to change a setting and
        /// press GENERATE once fires the *previous* confirmation against a *different* map
        /// than the one it was given for - the same "armed forever" mistake
        /// <see cref="Guard"/>'s own version stamp exists to catch, reached by a door the
        /// stamp does not cover.
        /// </remarks>
        private void HideMakePanel()
        {
            DisarmGenerate();
            makePanel.gameObject.SetActive(false);
        }

        /// <summary>
        /// Forgets an armed GENERATE confirmation, if there is one.
        /// </summary>
        /// <remarks>
        /// Called by everything that changes what GENERATE would draw - every row of options,
        /// a typed or rolled seed, and closing the panel - so a confirmation given for one set
        /// of settings can never fire against a different one it was never shown for.
        /// </remarks>
        private void DisarmGenerate()
        {
            if (armed == "generate")
            {
                armed = string.Empty;
                armedAtVersion = -1;
            }
        }

        /// <summary>
        /// Lights whichever button on each row matches what is being asked for.
        /// </summary>
        private void RefreshMakePanel()
        {
            DisarmGenerate();

            Lit(sizeButtons, (int)wanted.Difficulty - 1);
            Lit(groundButtons, (int)wanted.Layout);
            Lit(halfButtons, (int)wanted.Symmetry - 1);
            Lit(playerButtons, wanted.Players - 1);

            if (seedField != null && !seedField.isFocused)
            {
                seedField.text = wanted.Seed.ToString();
            }

            makeNote.text = wanted.IsSolo
                ? "One bunker, and an enemy that is a field of flag towers behind their own "
                    + "emplacements. A solo map cannot be played yet, so the Problems panel "
                    + "will name green's missing towers and brown's missing bunker: both are "
                    + "expected."
                : "The same seed and the same settings always draw the same map, so a seed "
                    + "worth keeping is worth writing down. What comes out is an ordinary map: "
                    + "every tool works on it, and the Problems panel says whether it plays.";
        }

        /// <summary>
        /// Lights one button in a row and puts the rest out.
        /// </summary>
        /// <param name="buttons">The row.</param>
        /// <param name="chosen">Which one, or out of range for none.</param>
        private static void Lit(List<EditorButton> buttons, int chosen)
        {
            for (int row = 0; row < buttons.Count; row++)
            {
                buttons[row].SetChosen(row == chosen);
            }
        }

        /// <summary>
        /// Draws a map from what the panel is asking for.
        /// </summary>
        /// <remarks>
        /// The panel closes, exactly as the open panel does when a row is picked, because what
        /// there is to look at now is the map rather than the question. A fresh seed is rolled
        /// on the way out, so pressing GENERATE again is a different map rather than the same
        /// one drawn twice.
        /// </remarks>
        private void MakeNow()
        {
            session.GenerateLevel(wanted);
            wanted.Seed = RolledSeed();
            HideMakePanel();
        }

        /// <summary>
        /// Takes a typed seed.
        /// </summary>
        /// <param name="typed">What was typed into the seed field.</param>
        /// <remarks>
        /// Anything that is not a number is put back rather than argued with. The field shows
        /// the seed that will be used, so the way to say "that was not a seed" is to show the
        /// one that still is.
        /// </remarks>
        private void Reseed(string typed)
        {
            if (int.TryParse(typed, out int given))
            {
                wanted.Seed = given;
            }

            RefreshMakePanel();
        }

        /// <summary>
        /// Rolls a fresh seed into the panel.
        /// </summary>
        private void Reroll()
        {
            wanted.Seed = RolledSeed();
            RefreshMakePanel();
        }

        /// <summary>
        /// Returns a seed nobody asked for.
        /// </summary>
        /// <returns>A number to draw a map from.</returns>
        /// <remarks>
        /// <para>
        /// The one place in this whole feature where something genuinely arbitrary happens,
        /// and it happens <em>outside</em> the map. This picks which map; everything from here
        /// down is a pure function of it - see <see cref="Dice"/>. The number is written into
        /// the level file and shown in the field, so nothing about the map is unrepeatable.
        /// </para>
        /// <para>
        /// The press count is mixed in because the clock only moves every millisecond or so,
        /// and two presses inside one tick would otherwise draw the same map twice and look
        /// like a button that did not work.
        /// </para>
        /// </remarks>
        private int RolledSeed()
        {
            unchecked
            {
                rolls++;
                return System.Environment.TickCount + (rolls * 7919);
            }
        }

        /// <summary>
        /// Runs something that would throw away unsaved work, once it has been asked twice.
        /// </summary>
        /// <param name="id">What is being armed, so a different button disarms this one.</param>
        /// <param name="what">What the action is, for the sentence on the status line.</param>
        /// <param name="does">The action.</param>
        /// <remarks>
        /// <para>
        /// Three buttons on this bar discard the map: new, open and revert - and so does
        /// picking a row out of the open-map list, guarded the same way. A modal dialogue
        /// would be the usual answer and it is a lot of interface for a question with one
        /// consequence, so the button asks in the status line and waits to be pressed again.
        /// </para>
        /// <para>
        /// It arms nothing at all when there is nothing to lose, so the ordinary case - open a
        /// map, look at it, open another - costs no extra clicks. It disarms itself as soon as
        /// a different guarded action is pressed, and a save makes all of them harmless again.
        /// </para>
        /// <para>
        /// The arming is also stamped with <see cref="LevelEditorSession.EditVersion"/> at the
        /// moment it happens, and a second press only counts as confirmation when that number
        /// has not moved since. Without the stamp, one press that armed the guard stayed armed
        /// forever - the player could keep editing for as long as they liked and the very next
        /// press of the same button, however much later, discarded everything since the first
        /// press with no second warning at all, which is exactly backwards for a confirmation
        /// that exists to catch unsaved work.
        /// </para>
        /// </remarks>
        private void Guard(string id, string what, System.Action does)
        {
            if (!session.IsDirty || (armed == id && armedAtVersion == session.EditVersion))
            {
                armed = string.Empty;
                does();
                return;
            }

            armed = id;
            armedAtVersion = session.EditVersion;
            session.Announce($"{what} would throw away unsaved changes. Press it again to do it.");
        }

        /// <summary>
        /// Renames the file the map is saved as.
        /// </summary>
        /// <param name="typed">What was typed into the file field.</param>
        /// <remarks>
        /// A rename here is a <em>save as</em>: the map is written under the new name at once.
        /// The alternative - remembering a name and writing it later - is an editor in which
        /// the field says one thing and the file on disk is called another, which is the state
        /// somebody discovers after closing it.
        /// </remarks>
        private void Rename(string typed)
        {
            string tidied = LevelEditorSession.Tidy(typed);
            if (tidied.Length == 0 || tidied == session.LevelName)
            {
                Refresh();
                return;
            }

            session.SaveAs(tidied);
        }

        private void RefreshProblems()
        {
            IReadOnlyList<string> problems = session.Problems;

            if (problems.Count == 0)
            {
                problemsTitle.text = "PLAYABLE";
                problemsTitle.color = EditorTheme.Clean;
                problemsBody.text = "Every rule a map has to obey is obeyed. Whether it is any "
                    + "good is a question only playing it answers.";
                problemsBody.color = EditorTheme.FadedInk;
                return;
            }

            problemsTitle.text = problems.Count == 1
                ? "1 PROBLEM"
                : $"{problems.Count} PROBLEMS";
            problemsTitle.color = EditorTheme.Problem;

            var written = new StringBuilder();
            foreach (string problem in problems)
            {
                written.Append("· ").AppendLine(problem);
            }

            problemsBody.text = written.ToString();
            problemsBody.color = EditorTheme.Ink;
        }

        private string GridCaption()
        {
            float step = session.GridStep;
            return step <= 0.0f ? "GRID  OFF" : $"GRID  {step:0.##} m";
        }

        private void AddTool(
            RectTransform column, EditTool which, string words, ref float top, float inner)
        {
            EditorButton button = Make(
                column, $"Tool {which}", words, Margin, 0.0f, inner, () => session.SetTool(which));
            Hang(button.Rect, Margin, top, inner, RowHeight - Gutter);
            top -= RowHeight;

            toolButtons.Add(button);
            toolOrder.Add(which);
        }

        private float Heading(RectTransform column, string words, float top, float inner)
        {
            Text label = EditorTheme.Label($"{words} Heading", column, 18, TextAnchor.MiddleLeft);
            label.color = EditorTheme.FadedInk;
            label.text = words;
            Hang(label.rectTransform, Margin, top, inner, 26.0f);
            return top - 30.0f;
        }

        private float Command(
            RectTransform bar,
            string name,
            string words,
            float left,
            float bottom,
            float width,
            System.Action does)
        {
            Make(bar, name, words, left, bottom, width, does);
            return left + width + Gutter;
        }

        private EditorButton Make(
            RectTransform parent,
            string name,
            string words,
            float left,
            float bottom,
            float width,
            System.Action does)
        {
            EditorButton button = EditorTheme.Button(name, parent, words, 19, does);
            EditorTheme.Place(button.Rect, left, bottom, width, RowHeight);
            return button;
        }

        /// <summary>
        /// Turns a run-together enum name into words.
        /// </summary>
        /// <param name="name">The name, e.g. <c>DepotFuel</c>.</param>
        /// <returns>The name with spaces before its capitals, in upper case.</returns>
        /// <remarks>
        /// The palette is read by somebody deciding what to place, not by somebody reading
        /// code, and <c>DEPOT FUEL</c> is a thing on a map while <c>DEPOTFUEL</c> is an
        /// identifier that leaked.
        /// </remarks>
        private static string Spaced(string name)
        {
            var written = new StringBuilder(name.Length + 4);

            for (int letter = 0; letter < name.Length; letter++)
            {
                if (letter > 0 && char.IsUpper(name[letter]))
                {
                    written.Append(' ');
                }

                written.Append(char.ToUpperInvariant(name[letter]));
            }

            return written.ToString();
        }

        private static void Hang(
            RectTransform rect, float left, float top, float width, float height)
        {
            rect.anchorMin = new Vector2(0.0f, 1.0f);
            rect.anchorMax = new Vector2(0.0f, 1.0f);
            rect.pivot = new Vector2(0.0f, 1.0f);
            rect.anchoredPosition = new Vector2(left, top);
            rect.sizeDelta = new Vector2(width, height);
        }

        /// <summary>
        /// Removes any panels already built, so building twice is not building twice over.
        /// </summary>
        private void Clear()
        {
            Transform existing = transform.Find(PanelsNodeName);
            if (existing != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(existing.gameObject);
                }
                else
                {
                    DestroyImmediate(existing.gameObject);
                }
            }

            panels = null;
            openPanel = null;
            makePanel = null;
            inspector = null;
            fileField = null;
            seedField = null;
            makeNote = null;
            toolButtons.Clear();
            toolOrder.Clear();
            paletteButtons.Clear();
            paletteOrder.Clear();
            sideButtons.Clear();
            openButtons.Clear();
            sizeButtons.Clear();
            playerButtons.Clear();
            halfButtons.Clear();
            groundButtons.Clear();
        }
    }
}
