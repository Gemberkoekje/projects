using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using IronFlag.Core;
using IronFlag.Destruction;
using IronFlag.Levels;

namespace IronFlag.Editing
{
    /// <summary>
    /// The level editor itself: the map being worked on, the tool in hand, and what a click
    /// on the map does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// M7 left the editor three seams and said it needed nothing else built first, and that
    /// turned out to be true: editing is <see cref="LevelLoader.Show"/> with a changed
    /// definition, saving is <see cref="LevelFile.TryWrite"/> into
    /// <see cref="LevelLibrary.UserFolder"/>, and playing it is loading the game scene with
    /// the name handed over. What is here is the part that was missing - a state machine for
    /// the mouse, and somewhere to keep what is selected.
    /// </para>
    /// <para>
    /// It decides nothing about what a level <em>is</em>. Every change goes through
    /// <see cref="LevelEdits"/>, every question about what is under the cursor goes through
    /// <see cref="LevelPick"/>, and whether the result is playable is
    /// <see cref="LevelValidation"/>'s answer, unchanged and unfiltered. That is the same
    /// split the rest of the project draws between a rule and the thing that runs it, and it
    /// is why almost all of this feature is testable without a scene.
    /// </para>
    /// <para>
    /// The map is <em>not</em> rebuilt while the mouse is down. A rebuild destroys and
    /// re-instantiates every prefab on the map, which is fine once per edit and absurd once
    /// per frame of a drag; what moves during a drag is a rectangle on the overlay, and the
    /// world catches up when the button comes up. See <see cref="EditorOverlay"/>.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Level Editor Session")]
    public sealed class LevelEditorSession : MonoBehaviour
    {
        /// <summary>Grid spacings the editor offers, in metres.</summary>
        /// <remarks>
        /// Zero first, because a coastline that has to meet another one exactly is the one
        /// thing snapping cannot help with. A metre is the working default: it is finer than
        /// anything on the map matters to and coarse enough that two rectangles meant to line
        /// up actually do.
        /// </remarks>
        public static readonly float[] GridSteps = { 0.0f, 0.5f, 1.0f, 2.0f, 5.0f };

        /// <summary>Degrees one nudge of the rotate keys turns something by.</summary>
        private const float YawStep = 15.0f;

        /// <summary>The sides a map can be built for.</summary>
        private static readonly Team[] Sides = Teams.Playing;

        [SerializeField]
        [Tooltip("The loader whose map this edits.")]
        private LevelLoader loader;

        [SerializeField]
        [Tooltip("The overhead view the map is edited through.")]
        private EditorCameraRig camera;

        [SerializeField]
        [Tooltip("Level file the editor opens with, when nothing else has been asked for.")]
        private string levelName = LevelLibrary.DefaultLevel;

        private readonly EditHistory history = new EditHistory();
        private readonly List<string> problems = new List<string>();

        private LevelDefinition level;
        private EditTool tool = EditTool.Select;
        private EditSelection selection = EditSelection.Nothing;
        private StructureKind paletteKind = StructureKind.Tree;
        private Team paletteSide = Team.Green;
        private int gridIndex = 2;

        private Grab grab = Grab.None;
        private LandGrip grip = LandGrip.None;
        private Vector3 grabbedAt;
        private Vector3 grabOffset;
        private bool moved;
        private Vector3 pointer;
        private string note = string.Empty;
        private bool quitConfirmed;

        /// <summary>What a drag is currently doing.</summary>
        private enum Grab
        {
            /// <summary>Nothing; the button is up.</summary>
            None = 0,

            /// <summary>Sliding the view.</summary>
            Panning = 1,

            /// <summary>Carrying whatever is selected.</summary>
            Moving = 2,

            /// <summary>Pulling one corner of a rectangle of land.</summary>
            Resizing = 3,

            /// <summary>Drawing a new rectangle of land.</summary>
            Drawing = 4,
        }

        /// <summary>Raised whenever the map, the selection or the tool changes.</summary>
        /// <remarks>
        /// One event rather than three. The panels are small enough to redraw whole, and an
        /// editor whose inspector is refreshed by a different signal from its problem list is
        /// one where the two can disagree about which map they are describing.
        /// </remarks>
        public event Action Changed;

        /// <summary>The map being worked on, or <c>null</c> before one is opened.</summary>
        public LevelDefinition Level => level;

        /// <summary>The file the map is saved as, without a folder or an extension.</summary>
        public string LevelName => levelName;

        /// <summary>Whether there are changes that have not been written to a file.</summary>
        public bool IsDirty { get; private set; }

        /// <summary>
        /// How many real edits have been committed since the map was opened.
        /// </summary>
        /// <remarks>
        /// What <see cref="EditorUi"/>'s New/Open/Revert confirmation checks for staleness: a
        /// warning armed by one press has to ask again if anything happened in between,
        /// rather than being spent by an unrelated later press on the same button. Bumped
        /// only when <see cref="EditHistory.Record"/> actually adds a step, so retyping a
        /// field with the value it already had - a genuine no-op - does not count as an edit
        /// here either.
        /// </remarks>
        public int EditVersion { get; private set; }

        /// <summary>What a click on the map does.</summary>
        public EditTool Tool => tool;

        /// <summary>What is selected.</summary>
        public EditSelection Selection => selection;

        /// <summary>What the structure tool places.</summary>
        public StructureKind PaletteKind => paletteKind;

        /// <summary>Which side the tower and bunker tools place for.</summary>
        public Team PaletteSide => paletteSide;

        /// <summary>Grid spacing in metres; zero snaps to nothing.</summary>
        public float GridStep => GridSteps[Mathf.Clamp(gridIndex, 0, GridSteps.Length - 1)];

        /// <summary>Everything wrong with the map as it stands.</summary>
        public IReadOnlyList<string> Problems => problems;

        /// <summary>Where on the ground the pointer is.</summary>
        public Vector3 Pointer => pointer;

        /// <summary>The last thing the editor did, in a few words, for the status line.</summary>
        public string Note => note;

        /// <summary>Whether there is a change to undo.</summary>
        public bool CanUndo => history.CanUndo;

        /// <summary>Whether there is a change to put back.</summary>
        public bool CanRedo => history.CanRedo;

        /// <summary>The overhead view the map is edited through.</summary>
        public EditorCameraRig View => camera;

        /// <summary>Whether the mouse is dragging something out on the map right now.</summary>
        public bool IsDragging => grab == Grab.Moving || grab == Grab.Resizing || grab == Grab.Drawing;

        /// <summary>
        /// The rectangle a land drag is describing, while one is happening.
        /// </summary>
        /// <remarks>
        /// The overlay's preview. Empty when nothing is being drawn or resized, so a caller
        /// can draw it unconditionally and get nothing.
        /// </remarks>
        public Rect DrawnLand { get; private set; }

        /// <summary>
        /// Where the thing being dragged would land if the button came up now.
        /// </summary>
        public Vector3 DraggedTo { get; private set; }

        /// <summary>
        /// Points the editor at the loader and the camera it works through.
        /// </summary>
        /// <param name="levelLoader">The loader whose map this edits.</param>
        /// <param name="view">The overhead view.</param>
        /// <param name="opening">Level file to open when nothing else has been asked for.</param>
        /// <remarks>Called by the scene generator; opening is a separate step.</remarks>
        public void Configure(LevelLoader levelLoader, EditorCameraRig view, string opening)
        {
            loader = levelLoader;
            camera = view;
            levelName = opening;
        }

        /// <summary>
        /// Opens a level file for editing.
        /// </summary>
        /// <param name="name">Level name, without a folder or an extension.</param>
        /// <returns><c>true</c> when the level was opened.</returns>
        /// <remarks>
        /// A level that will not load leaves the editor on the map it was already on, for the
        /// same reason <see cref="LevelLoader.Load"/> does: an empty world is a worse place to
        /// be told about a broken file than the map you were working on.
        /// </remarks>
        public bool Open(string name)
        {
            string path = LevelLibrary.PathFor(name);
            if (!LevelFile.TryRead(path, out LevelDefinition opened, out string problem))
            {
                Say(problem);
                return false;
            }

            levelName = name;
            Adopt(opened, false);
            Say($"Opened {name}.");
            return true;
        }

        /// <summary>
        /// Starts a new map.
        /// </summary>
        /// <remarks>
        /// Marked as unsaved from the first frame, because nothing on disk is called this
        /// yet - and a new map that looked saved would be one somebody could close without
        /// being warned.
        /// </remarks>
        public void NewLevel()
        {
            levelName = UnusedName();
            Adopt(LevelEdits.Starter("New Map"), true);
            Say($"New map. It will be saved as {levelName}.");
        }

        /// <summary>
        /// Takes a level as the one being edited, framing the view on it.
        /// </summary>
        /// <param name="opened">The map.</param>
        /// <param name="dirty">Whether it already differs from what is on disk.</param>
        /// <param name="name">
        /// What to call it, or <c>null</c> to leave the file name as it was. The batch-mode
        /// still is the one caller that adopts a map without going through
        /// <see cref="Open"/> or <see cref="NewLevel"/>, and without this the toolbar would
        /// go on showing whichever map the scene happened to bake rather than the one
        /// actually on screen.
        /// </param>
        public void Adopt(LevelDefinition opened, bool dirty, string name = null)
        {
            level = opened;
            selection = EditSelection.Nothing;
            IsDirty = dirty;

            if (!string.IsNullOrWhiteSpace(name))
            {
                levelName = name;
            }

            history.Reset(level);

            if (camera != null)
            {
                camera.Frame(level);
            }

            Rebuild();
            Revalidate();

            note = opened == null
                ? "There is no map."
                : $"{opened.Name}. Press 2 and drag to draw land, or 3 to place something.";

            Changed?.Invoke();
        }

        /// <summary>
        /// Writes the map to the player's own level folder.
        /// </summary>
        /// <returns><c>true</c> when the file was written.</returns>
        /// <remarks>
        /// Always into <see cref="LevelLibrary.UserFolder"/>, never over the shipped copy.
        /// That is M7's rule and it is what makes editing a shipped map safe: the edit lands
        /// next to the player's saves, the game picks it up because a player's copy shadows
        /// the shipped one, and reverting is deleting one file. Nothing ever has to write into
        /// the install folder, which on a real machine it often cannot.
        /// </remarks>
        public bool Save()
        {
            if (level == null)
            {
                return false;
            }

            string path = LevelLibrary.UserPathFor(levelName);
            if (!LevelFile.TryWrite(path, level, out string problem))
            {
                Say(problem);
                return false;
            }

            IsDirty = false;

            // The name rather than the path. A status line is one line, and the path is a
            // hundred characters of somebody's home directory to say something the field at
            // the top of the screen is already showing.
            Say($"Saved as {levelName}{LevelLibrary.Extension}, in your own levels folder.");
            return true;
        }

        /// <summary>
        /// Saves the map under a different name.
        /// </summary>
        /// <param name="name">Level name, without a folder or an extension.</param>
        /// <returns><c>true</c> when the file was written.</returns>
        public bool SaveAs(string name)
        {
            string tidied = Tidy(name);
            if (tidied.Length == 0)
            {
                Say("A map needs a file name.");
                return false;
            }

            levelName = tidied;
            return Save();
        }

        /// <summary>
        /// Throws away every unsaved change and reads the map back off disk.
        /// </summary>
        /// <returns><c>true</c> when the map was reloaded.</returns>
        public bool Revert() => Open(levelName);

        /// <summary>
        /// Saves the map and loads the game scene to play it.
        /// </summary>
        /// <returns><c>true</c> when the game scene was asked for.</returns>
        /// <remarks>
        /// Saving first is not a convenience. The game reads the map from a file, so a
        /// playtest of unsaved changes would be a playtest of the last thing that was saved,
        /// which is the most confusing possible way for an editor to behave.
        /// </remarks>
        public bool Playtest()
        {
            if (level == null || !Save())
            {
                return false;
            }

            LevelHandoff.Playtest(levelName);
            SceneManager.LoadScene(LevelScenes.Game);
            return true;
        }

        /// <summary>
        /// Chooses what a click on the map does.
        /// </summary>
        /// <param name="wanted">The tool.</param>
        public void SetTool(EditTool wanted)
        {
            if (wanted == EditTool.None || tool == wanted)
            {
                return;
            }

            tool = wanted;
            Changed?.Invoke();
        }

        /// <summary>
        /// Chooses what the structure tool places.
        /// </summary>
        /// <param name="kind">The structure.</param>
        /// <remarks>
        /// Picking something off the palette also picks up the tool that places it, because
        /// the alternative is clicking a tree, clicking the map, and having moved a shore.
        /// </remarks>
        public void SetPaletteKind(StructureKind kind)
        {
            if (kind == StructureKind.None)
            {
                return;
            }

            paletteKind = kind;
            tool = EditTool.Structure;
            Changed?.Invoke();
        }

        /// <summary>
        /// Chooses which side the tower and bunker tools place for.
        /// </summary>
        /// <param name="side">The side.</param>
        public void SetPaletteSide(Team side)
        {
            if (side == Team.None || paletteSide == side)
            {
                return;
            }

            paletteSide = side;
            Changed?.Invoke();
        }

        /// <summary>
        /// Steps to the next grid spacing.
        /// </summary>
        public void CycleGrid()
        {
            gridIndex = (gridIndex + 1) % GridSteps.Length;
            Changed?.Invoke();
        }

        /// <summary>
        /// Selects something, or nothing.
        /// </summary>
        /// <param name="wanted">What to select.</param>
        public void Select(EditSelection wanted)
        {
            EditSelection held = LevelEdits.Holds(level, wanted) ? wanted : EditSelection.Nothing;
            if (held == selection)
            {
                return;
            }

            selection = held;
            Changed?.Invoke();
        }

        /// <summary>
        /// Records a change that has already been made to the map, and shows it.
        /// </summary>
        /// <param name="what">What changed, for the status line.</param>
        /// <remarks>
        /// <para>
        /// The one method everything that edits a level goes through. It rebuilds the world,
        /// re-runs the rules, adds an undo step and marks the map unsaved - in that order,
        /// and never partly. An edit that skipped it would be one the map showed and undo
        /// could not reach.
        /// </para>
        /// <para>
        /// Called <em>after</em> the change rather than before, which is what lets a caller
        /// use the plain functions in <see cref="LevelEdits"/> directly and then say so.
        /// </para>
        /// </remarks>
        public void Commit(string what)
        {
            if (level == null)
            {
                return;
            }

            if (history.Record(level))
            {
                IsDirty = true;
                EditVersion++;
                quitConfirmed = false;
            }

            Rebuild();
            Revalidate();

            if (!string.IsNullOrEmpty(what))
            {
                note = what;
            }

            Changed?.Invoke();
        }

        /// <summary>
        /// Says something on the status line.
        /// </summary>
        /// <param name="what">What to say.</param>
        /// <remarks>
        /// For the panels, which have things to say that are not about the map - that a button
        /// is about to throw away unsaved work, most of all. Keeping one status line rather
        /// than giving the panels their own is what stops two of them contradicting each
        /// other about what just happened.
        /// </remarks>
        public void Announce(string what) => Say(what);

        /// <summary>
        /// Steps back one change.
        /// </summary>
        /// <returns><c>true</c> when something was undone.</returns>
        public bool Undo() => Restore(history.Undo(), "Undone.");

        /// <summary>
        /// Puts back a change that was undone.
        /// </summary>
        /// <returns><c>true</c> when something was put back.</returns>
        public bool Redo() => Restore(history.Redo(), "Redone.");

        /// <summary>
        /// Takes whatever is selected off the map.
        /// </summary>
        /// <returns><c>true</c> when something was removed.</returns>
        public bool DeleteSelected()
        {
            if (!LevelEdits.Holds(level, selection))
            {
                return false;
            }

            string what = LevelEdits.Describe(level, selection);
            if (!LevelEdits.Remove(level, selection))
            {
                return false;
            }

            selection = EditSelection.Nothing;
            Commit($"Deleted {what}.");
            return true;
        }

        /// <summary>
        /// Moves the view to whatever is selected, or to the whole map when nothing is.
        /// </summary>
        public void FrameSelection()
        {
            if (camera == null)
            {
                return;
            }

            if (LevelEdits.Holds(level, selection))
            {
                camera.LookAt(LevelEdits.PositionOf(level, selection));
            }
            else
            {
                camera.Frame(level);
            }
        }

        /// <summary>
        /// Rebuilds the map from the level being edited.
        /// </summary>
        /// <remarks>
        /// Quietly: the problems are already on screen in a panel, and logging them once per
        /// edit would bury everything else in the console under a map somebody is in the
        /// middle of drawing. Also the one place the camera's pan limit is kept in step with
        /// the level's own bounds - see <see cref="EditorCameraRig.SetWorldExtent"/> - so
        /// typing a bigger half-extent into the inspector does not leave the newly available
        /// part of the map behind a pan limit sized for the old one.
        /// </remarks>
        public void Rebuild()
        {
            if (loader != null)
            {
                loader.Show(level, false);
            }

            if (camera != null && level != null && level.Bounds != null)
            {
                camera.SetWorldExtent(level.Bounds.HalfExtent);
            }
        }

        /// <summary>
        /// Decides whether the process may actually quit, and arms a warning if not.
        /// </summary>
        /// <returns><c>true</c> when nothing would be lost, or the player already confirmed.</returns>
        /// <remarks>
        /// The same shape as <see cref="EditorUi"/>'s New/Open/Revert guard, for the reason
        /// it exists at all: closing the window is how most desktop sessions actually end,
        /// and the only place unsaved work was protected used to be three toolbar buttons -
        /// so Alt+F4 or the close box dropped a session's work with no warning whatsoever,
        /// the one path every other way of losing it already refused to take silently.
        /// </remarks>
        public bool ConfirmQuit()
        {
            if (!IsDirty || quitConfirmed)
            {
                return true;
            }

            quitConfirmed = true;
            Say("Unsaved changes. Quit again to discard them, or Ctrl+S to save first.");
            return false;
        }

        private bool HandleWantsToQuit() => ConfirmQuit();

        private void OnEnable() => Application.wantsToQuit += HandleWantsToQuit;

        private void OnDisable() => Application.wantsToQuit -= HandleWantsToQuit;

        private void Start()
        {
            // The loader has already read a file and built it - in Awake, before this runs -
            // so the map is up and there is nothing to load. Adopting what it built is what
            // keeps the editor and the game agreeing about what "the current map" is, and it
            // is the whole of how a playtest comes back to the map it left.
            //
            // What the loader put up, not LevelLoader.Current: the static survives a scene
            // change, so a loader whose file would not read would hand the editor the map from
            // the scene before under this scene's file name - and the first save would write
            // one map into the other's file.
            if (loader != null && loader.Shown != null)
            {
                levelName = loader.LevelName;
                Adopt(loader.Shown, false);
                return;
            }

            if (!Open(LevelHandoff.LevelOr(levelName)))
            {
                NewLevel();
            }
        }

        private void Update()
        {
            if (level == null)
            {
                return;
            }

            ReadKeys();
            ReadMouse();
        }

        /// <summary>
        /// Restores a map from the history.
        /// </summary>
        /// <param name="restored">The map, or <c>null</c> when there was nothing to restore.</param>
        /// <param name="what">What to say about it.</param>
        /// <returns><c>true</c> when a map came back.</returns>
        /// <remarks>
        /// Deliberately does not touch <see cref="IsDirty"/>. Undoing back to the state a
        /// file was saved in leaves the editor holding a map that matches the file, but
        /// working that out means comparing them, and an editor that quietly decides you have
        /// no unsaved work is one that eventually decides it wrongly.
        /// </remarks>
        private bool Restore(LevelDefinition restored, string what)
        {
            if (restored == null)
            {
                return false;
            }

            level = restored;

            if (!LevelEdits.Holds(level, selection))
            {
                selection = EditSelection.Nothing;
            }

            Rebuild();
            Revalidate();
            Say(what);
            return true;
        }

        /// <summary>
        /// Re-runs the level rules and keeps the answer for the panel.
        /// </summary>
        private void Revalidate()
        {
            problems.Clear();
            problems.AddRange(LevelValidation.Problems(level));
        }

        private void Say(string what)
        {
            note = what;
            Changed?.Invoke();
        }

        /// <summary>
        /// Reads the keyboard shortcuts.
        /// </summary>
        /// <remarks>
        /// Skipped entirely while a text field has the keyboard, or typing a map called
        /// "Sandy" would select the structure tool, delete the selection and save.
        /// </remarks>
        private void ReadKeys()
        {
            Keyboard keys = Keyboard.current;
            if (keys == null || IsTyping())
            {
                return;
            }

            bool control = keys.ctrlKey.isPressed || keys.leftCommandKey.isPressed
                || keys.rightCommandKey.isPressed;

            if (control)
            {
                ReadCommands(keys);
                return;
            }

            if (keys.digit1Key.wasPressedThisFrame)
            {
                SetTool(EditTool.Select);
            }
            else if (keys.digit2Key.wasPressedThisFrame)
            {
                SetTool(EditTool.Land);
            }
            else if (keys.digit3Key.wasPressedThisFrame)
            {
                SetTool(EditTool.Structure);
            }
            else if (keys.digit4Key.wasPressedThisFrame)
            {
                SetTool(EditTool.Tower);
            }
            else if (keys.digit5Key.wasPressedThisFrame)
            {
                SetTool(EditTool.Bunker);
            }

            if (keys.deleteKey.wasPressedThisFrame || keys.backspaceKey.wasPressedThisFrame)
            {
                DeleteSelected();
            }

            if (keys.escapeKey.wasPressedThisFrame)
            {
                Select(EditSelection.Nothing);
            }

            if (keys.fKey.wasPressedThisFrame)
            {
                FrameSelection();
            }

            if (keys.gKey.wasPressedThisFrame)
            {
                CycleGrid();
            }

            if (keys.qKey.wasPressedThisFrame)
            {
                NudgeYaw(-YawStep);
            }

            if (keys.eKey.wasPressedThisFrame)
            {
                NudgeYaw(YawStep);
            }
        }

        private void ReadCommands(Keyboard keys)
        {
            if (keys.zKey.wasPressedThisFrame)
            {
                if (keys.shiftKey.isPressed)
                {
                    Redo();
                }
                else
                {
                    Undo();
                }
            }

            if (keys.yKey.wasPressedThisFrame)
            {
                Redo();
            }

            if (keys.sKey.wasPressedThisFrame)
            {
                Save();
            }
        }

        private void NudgeYaw(float degrees)
        {
            if (!LevelEdits.Holds(level, selection) || selection.Target == EditTarget.Land)
            {
                return;
            }

            LevelEdits.SetYaw(level, selection, LevelEdits.YawOf(level, selection) + degrees);
            Commit($"Turned {LevelEdits.Describe(level, selection)}.");
        }

        /// <summary>
        /// Reads the mouse: the view, and whatever the left button is doing to the map.
        /// </summary>
        private void ReadMouse()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || camera == null)
            {
                return;
            }

            Vector2 screen = mouse.position.ReadValue();
            pointer = LevelPick.GroundUnder(camera.View, screen);

            bool overPanel = IsPointerOverPanel();

            if (!overPanel)
            {
                float notches = mouse.scroll.ReadValue().y;
                if (!Mathf.Approximately(notches, 0.0f))
                {
                    camera.ZoomBy(Mathf.Sign(notches), pointer);
                }
            }

            ReadPan(mouse);

            if (grab == Grab.Panning)
            {
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame && !overPanel)
            {
                Press(pointer);
            }
            else if (mouse.leftButton.isPressed && IsDragging)
            {
                Drag(pointer);
            }
            else if (mouse.leftButton.wasReleasedThisFrame && IsDragging)
            {
                Release(pointer);
            }
        }

        /// <summary>
        /// Slides the view under the right or middle button.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two buttons for one gesture, because a mouse without a wheel button and a laptop
        /// trackpad without a right button are both real, and panning is the one thing in
        /// this editor nobody can work without.
        /// </para>
        /// <para>
        /// A pan never interrupts a drag already in progress. It used to: holding the left
        /// button on a tower to move it, then reaching for the right button to pan without
        /// letting go, switched <c>grab</c> straight to <see cref="Grab.Panning"/> - and
        /// releasing the pan button afterward put it back to <see cref="Grab.None"/> rather
        /// than to the move that was happening, so the left button's own release never saw
        /// <see cref="IsDragging"/> and the drag simply vanished, uncommitted and unremarked.
        /// A drag in progress now keeps the left button's exclusive claim on <c>grab</c> and
        /// panning is refused until it lets go - the least surprising of the two, since the
        /// alternative is a gesture that quietly cancels whatever you were doing with your
        /// other hand.
        /// </para>
        /// </remarks>
        private void ReadPan(Mouse mouse)
        {
            bool held = mouse.rightButton.isPressed || mouse.middleButton.isPressed;

            if (!held)
            {
                if (grab == Grab.Panning)
                {
                    grab = Grab.None;
                }

                return;
            }

            if (grab != Grab.None && grab != Grab.Panning)
            {
                return;
            }

            grab = Grab.Panning;

            Vector2 moved = mouse.delta.ReadValue();
            float metres = camera.MetresPerPixel;
            camera.Pan(new Vector3(-moved.x * metres, 0.0f, -moved.y * metres));
        }

        /// <summary>
        /// Works out what the left button just started doing.
        /// </summary>
        /// <param name="at">Where on the ground it was pressed.</param>
        /// <remarks>
        /// <para>
        /// Every tool selects with the same button, and the only difference between them is
        /// what a press on <em>empty</em> ground does. Holding shift overrides that: it
        /// always creates, which is the only way to draw a rectangle over land that is already
        /// there or to put a second tree beside the first.
        /// </para>
        /// <para>
        /// Corners are tried before anything else, so a rectangle that is selected can be
        /// resized even where a prop is standing on its corner.
        /// </para>
        /// </remarks>
        private void Press(Vector3 at)
        {
            moved = false;

            Keyboard keys = Keyboard.current;
            bool forceNew = keys != null && keys.shiftKey.isPressed;

            if (!forceNew && selection.Target == EditTarget.Land
                && LevelEdits.Holds(level, selection))
            {
                LandGrip held = LevelPick.GripOn(level, selection.Index, at, GripReach());
                if (held != LandGrip.None && held != LandGrip.Body)
                {
                    grab = Grab.Resizing;
                    grip = held;
                    grabbedAt = at;
                    DrawnLand = RectOf(level.Land[selection.Index]);
                    return;
                }
            }

            EditSelection under = forceNew ? EditSelection.Nothing : LevelPick.At(level, at);

            if (under.IsSomething)
            {
                Select(under);
                grab = Grab.Moving;
                grabbedAt = at;
                grabOffset = LevelEdits.PositionOf(level, under) - at;
                DraggedTo = LevelEdits.PositionOf(level, under);
                return;
            }

            Create(at);
        }

        /// <summary>
        /// Puts something new on the map, or clears the selection.
        /// </summary>
        /// <param name="at">Where on the ground the button went down.</param>
        private void Create(Vector3 at)
        {
            Vector3 snapped = LevelEdits.Snap(at, EffectiveGrid());

            switch (tool)
            {
                case EditTool.Land:
                    grab = Grab.Drawing;
                    grabbedAt = snapped;
                    DrawnLand = new Rect(snapped.x, snapped.z, 0.0f, 0.0f);
                    return;

                case EditTool.Structure:
                    // The palette's side goes with it. Everything except a turret drops it
                    // on the way in, so the same click places a tree and a Green turret
                    // without the palette needing two modes.
                    Place(
                        EditTarget.Structure,
                        LevelEdits.AddStructure(level, paletteKind, snapped, paletteSide),
                        StructureTuning.BelongsToASide(paletteKind)
                            ? $"Placed a {paletteSide} {paletteKind}."
                            : $"Placed a {paletteKind}.");
                    return;

                case EditTool.Tower:
                    Place(
                        EditTarget.Tower,
                        LevelEdits.AddTower(level, paletteSide, snapped),
                        $"Placed a {paletteSide} tower.");
                    return;

                case EditTool.Bunker:
                    Place(
                        EditTarget.Bunker,
                        LevelEdits.AddBunker(level, paletteSide, snapped),
                        $"Placed the {paletteSide} bunker.");
                    return;

                default:
                    Select(EditSelection.Nothing);
                    return;
            }
        }

        private void Place(EditTarget target, int index, string what)
        {
            if (index < 0)
            {
                return;
            }

            selection = new EditSelection(target, index);
            Commit(what);
        }

        /// <summary>
        /// Follows the mouse while the button is down.
        /// </summary>
        /// <param name="at">Where on the ground it is now.</param>
        /// <remarks>
        /// Nothing on the map moves here: what changes is the preview the overlay draws. The
        /// world catches up once, on release, because a rebuild is every prefab on the map
        /// and a drag is sixty frames a second.
        /// </remarks>
        private void Drag(Vector3 at)
        {
            if (!moved && (at - grabbedAt).sqrMagnitude > 0.0001f)
            {
                moved = true;
            }

            float step = EffectiveGrid();

            switch (grab)
            {
                case Grab.Moving:
                    DraggedTo = LevelEdits.Snap(at + grabOffset, step);
                    return;

                case Grab.Drawing:
                    DrawnLand = Between(grabbedAt, LevelEdits.Snap(at, step));
                    return;

                case Grab.Resizing:
                    DrawnLand = Resized(level.Land[selection.Index], LevelEdits.Snap(at, step));
                    return;

                default:
                    return;
            }
        }

        /// <summary>
        /// Puts the change the drag was describing onto the map.
        /// </summary>
        /// <param name="at">Where on the ground the button came up.</param>
        private void Release(Vector3 at)
        {
            Grab was = grab;
            grab = Grab.None;
            grip = LandGrip.None;

            Rect drawn = DrawnLand;
            DrawnLand = Rect.zero;

            switch (was)
            {
                case Grab.Moving:
                    // A click that did not move is a click, and has to be checked before
                    // anything else here: DraggedTo was run through the active grid on every
                    // frame the button was held, including a stationary one, so a selected
                    // object sitting off the grid - typed into the inspector rather than
                    // dragged there - would otherwise be silently snapped to the nearest
                    // grid point by a plain click that never meant to move it at all.
                    if (!moved)
                    {
                        return;
                    }

                    // A drag that moved and still ended up back where the object already
                    // was - snapped onto the exact cell it started in - is not an edit
                    // either. Committing it would fill the undo stack with steps that
                    // changed nothing, which the history already refuses, but the rebuild
                    // and the status line would still happen for no reason.
                    if ((DraggedTo - LevelEdits.PositionOf(level, selection)).sqrMagnitude
                        < 0.0001f)
                    {
                        return;
                    }

                    LevelEdits.MoveTo(level, selection, DraggedTo);
                    Commit($"Moved {LevelEdits.Describe(level, selection)}.");
                    return;

                case Grab.Drawing:
                    int made = LevelEdits.AddLand(
                        level,
                        new Vector3(drawn.xMin, 0.0f, drawn.yMin),
                        new Vector3(drawn.xMax, 0.0f, drawn.yMax),
                        "Land");

                    if (made < 0)
                    {
                        Say($"A piece of land has to be at least "
                            + $"{LevelEdits.MinimumLandSide:0.#} m on each side.");
                        return;
                    }

                    selection = new EditSelection(EditTarget.Land, made);
                    Commit("Drew a piece of land.");
                    return;

                case Grab.Resizing:
                    // The same stationary-click guard as a move: pressing and releasing a
                    // corner without dragging it must not resize the rectangle just because
                    // that corner happened to sit off the active grid.
                    if (!moved)
                    {
                        return;
                    }

                    if (!LevelEdits.SetLandEdges(
                        level, selection.Index, drawn.xMin, drawn.xMax, drawn.yMin, drawn.yMax))
                    {
                        Say($"A piece of land has to be at least "
                            + $"{LevelEdits.MinimumLandSide:0.#} m on each side.");
                        return;
                    }

                    Commit($"Resized {LevelEdits.Describe(level, selection)}.");
                    return;

                default:
                    return;
            }
        }

        /// <summary>
        /// Returns the rectangle a resize drag is describing.
        /// </summary>
        /// <param name="piece">The rectangle being pulled.</param>
        /// <param name="to">Where the held corner has been dragged to.</param>
        /// <returns>The new rectangle, with the opposite corner left where it was.</returns>
        private Rect Resized(LevelLand piece, Vector3 to)
        {
            float minX = piece.MinX;
            float maxX = piece.MaxX;
            float minZ = piece.MinZ;
            float maxZ = piece.MaxZ;

            if (grip == LandGrip.SouthWest || grip == LandGrip.NorthWest)
            {
                minX = to.x;
            }
            else
            {
                maxX = to.x;
            }

            if (grip == LandGrip.SouthWest || grip == LandGrip.SouthEast)
            {
                minZ = to.z;
            }
            else
            {
                maxZ = to.z;
            }

            return Between(new Vector3(minX, 0.0f, minZ), new Vector3(maxX, 0.0f, maxZ));
        }

        /// <summary>
        /// Returns metres from a corner that counts as holding it, at the current zoom.
        /// </summary>
        /// <remarks>
        /// Scaled with the view rather than fixed in metres, so a corner is about the same
        /// number of pixels to hit whether the whole map is on screen or one building is.
        /// Fixed in metres, a zoomed-out map would be four corners nobody can separate and a
        /// zoomed-in one would be four corners nobody can find.
        /// </remarks>
        private float GripReach()
        {
            if (camera == null)
            {
                return LevelPick.GripReach;
            }

            const float pixels = 14.0f;
            return Mathf.Max(LevelEdits.MinimumLandSide * 0.4f, pixels * camera.MetresPerPixel);
        }

        /// <summary>
        /// Returns the grid spacing in force this frame.
        /// </summary>
        /// <remarks>
        /// Alt turns snapping off while it is held. Two rectangles that have to meet exactly
        /// want the grid, and a tree in the corner of a courtyard wants to be where it is
        /// put; making that a held key rather than a mode means neither costs a trip to a
        /// menu.
        /// </remarks>
        private float EffectiveGrid()
        {
            Keyboard keys = Keyboard.current;
            bool free = keys != null && (keys.leftAltKey.isPressed || keys.rightAltKey.isPressed);
            return free ? 0.0f : GridStep;
        }

        /// <summary>
        /// Reports whether the pointer is over a panel rather than over the map.
        /// </summary>
        /// <remarks>
        /// Without this, clicking the Save button also places a tree behind it - the button
        /// takes the click and the world takes it too, because they are two different input
        /// paths reading the same mouse.
        /// </remarks>
        private static bool IsPointerOverPanel()
            => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        /// <summary>
        /// Reports whether a text field currently has the keyboard.
        /// </summary>
        private static bool IsTyping()
        {
            EventSystem events = EventSystem.current;
            GameObject focused = events == null ? null : events.currentSelectedGameObject;
            return focused != null
                && focused.GetComponent<UnityEngine.UI.InputField>() != null;
        }

        /// <summary>
        /// Returns a file name nothing is using yet.
        /// </summary>
        /// <returns>A name, numbered upward until it is free in both level folders.</returns>
        private static string UnusedName()
        {
            var taken = new HashSet<string>(LevelLibrary.Names(), StringComparer.OrdinalIgnoreCase);

            for (int number = 1; number < 1000; number++)
            {
                string tried = $"new-map-{number}";
                if (!taken.Contains(tried))
                {
                    return tried;
                }
            }

            return "new-map";
        }

        /// <summary>
        /// Cleans a typed file name down to something that can be a file.
        /// </summary>
        /// <param name="name">What the player typed.</param>
        /// <returns>The name in lower case with runs of anything else turned into hyphens.</returns>
        /// <remarks>
        /// A level name becomes a path, so a name with a slash or a colon in it is a write
        /// that fails - or worse, one that succeeds somewhere else. Rewriting it is friendlier
        /// than refusing it, and the shipped map is already named this way.
        /// </remarks>
        public static string Tidy(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var tidied = new System.Text.StringBuilder(name.Length);
            bool hyphen = false;

            foreach (char letter in name.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(letter))
                {
                    tidied.Append(letter);
                    hyphen = false;
                }
                else if (!hyphen && tidied.Length > 0)
                {
                    tidied.Append('-');
                    hyphen = true;
                }
            }

            return tidied.ToString().Trim('-');
        }

        /// <summary>
        /// Returns the sides a map can be built for.
        /// </summary>
        /// <returns>Green and Brown, which is what a level has to give a bunker and a flag.</returns>
        public static IReadOnlyList<Team> PlayableSides() => Sides;

        private static Rect RectOf(LevelLand piece)
            => Rect.MinMaxRect(piece.MinX, piece.MinZ, piece.MaxX, piece.MaxZ);

        private static Rect Between(Vector3 from, Vector3 to)
            => Rect.MinMaxRect(
                Mathf.Min(from.x, to.x),
                Mathf.Min(from.z, to.z),
                Mathf.Max(from.x, to.x),
                Mathf.Max(from.z, to.z));
    }
}
