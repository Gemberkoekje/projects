using UnityEngine;
using IronFlag.Levels;

namespace IronFlag.Editing
{
    /// <summary>
    /// What the editor draws on top of the map: what is selected, what a drag is describing,
    /// where the middle of the world is, and where a thing's opposite number would stand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Screen-space rather than in the world, and that is the whole trick. The editor's camera
    /// looks straight down at the plane the game is played on, so world x and z map onto
    /// screen x and y exactly - which means a selection box can be four flat images on a
    /// canvas instead of geometry that would need a material, a shader and a way of not being
    /// buried under the bunker it is drawn around.
    /// </para>
    /// <para>
    /// It reads the session and never writes to it, so the picture can be wrong about nothing:
    /// there is no state here to fall out of step with the map.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Editor Overlay")]
    [RequireComponent(typeof(Canvas))]
    public sealed class EditorOverlay : MonoBehaviour
    {
        /// <summary>Name of the generated child every marker hangs off.</summary>
        public const string MarkersNodeName = "Markers";

        /// <summary>How thick an outline is drawn, in canvas units.</summary>
        private const float BarThickness = 2.0f;

        /// <summary>How far the origin cross reaches from the middle, in canvas units.</summary>
        private const float CrossReach = 22.0f;

        [SerializeField]
        [Tooltip("The editor whose selection and drag this draws.")]
        private LevelEditorSession session;

        private EditorOutline selection;
        private EditorOutline dragged;
        private EditorOutline twin;
        private EditorOutline origin;

        /// <summary>Whether the markers have been generated yet.</summary>
        public bool IsBuilt => selection != null;

        /// <summary>
        /// Points the overlay at the editor it draws for, and sets its canvas up.
        /// </summary>
        /// <param name="editor">The session.</param>
        /// <param name="view">The camera the markers are drawn in front of.</param>
        /// <remarks>
        /// <para>
        /// Deliberately without a <c>CanvasScaler</c>, unlike the panels: a marker is pinned to
        /// a place in the world rather than sized to be readable, so there is nothing here for
        /// a scaler to decide. Everything is laid out as a fraction of this canvas, which
        /// makes the scaler's absence a simplification rather than a dependency.
        /// </para>
        /// <para>
        /// It also gets no raycaster, so the markers can never take a click the map wanted.
        /// </para>
        /// </remarks>
        public void Configure(LevelEditorSession editor, Camera view)
        {
            session = editor;

            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = view;
            canvas.planeDistance = 2.0f;
            canvas.sortingOrder = 5;
        }

        /// <summary>
        /// Generates the markers, replacing any that were there before.
        /// </summary>
        public void Build()
        {
            Clear();

            var host = new GameObject(MarkersNodeName, typeof(RectTransform));
            host.transform.SetParent(transform, false);

            var rect = host.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            origin = EditorOutline.Build(host.transform, "Origin");
            twin = EditorOutline.Build(host.transform, "Opposite Number");
            selection = EditorOutline.Build(host.transform, "Selection");
            dragged = EditorOutline.Build(host.transform, "Drag");
        }

        /// <summary>
        /// Brings every marker up to date with what the editor is doing.
        /// </summary>
        public void Refresh()
        {
            if (!IsBuilt || session == null)
            {
                return;
            }

            EditorCameraRig view = session.View;
            LevelDefinition level = session.Level;

            if (view == null || level == null)
            {
                HideAll();
                return;
            }

            DrawOrigin(view);
            DrawSelection(view, level);
            DrawDrag(view);
        }

        private void Awake()
        {
            if (!IsBuilt)
            {
                Build();
            }
        }

        private void LateUpdate() => Refresh();

        /// <summary>
        /// Draws a small cross at the middle of the world.
        /// </summary>
        /// <remarks>
        /// Not decoration. Every map in this game is laid out in pairs rotated half a turn
        /// about the origin - it is the rule that stops one side having a shorter run to the
        /// other's flag - and the origin is otherwise the one place on the map with nothing
        /// standing on it to mark where it is.
        /// </remarks>
        private void DrawOrigin(EditorCameraRig view)
            => origin.ShowCross(Screen(view, Vector3.zero), CrossReach, EditorTheme.Origin, 1.0f);

        /// <summary>
        /// Draws a box around what is selected, and a fainter one where its twin would be.
        /// </summary>
        private void DrawSelection(EditorCameraRig view, LevelDefinition level)
        {
            EditSelection picked = session.Selection;

            if (!LevelEdits.Holds(level, picked))
            {
                selection.Hide();
                twin.Hide();
                return;
            }

            Rect ground = GroundRect(level, picked);
            selection.Show(ToScreen(view, ground), EditorTheme.Selected, BarThickness);

            // Where the same thing would stand on the other side of the map. Nothing is
            // placed there and nothing is checked against it - it is a ruler, held up next to
            // whatever is being moved, so a map can be kept symmetrical by eye.
            var opposite = new Rect(
                -ground.xMin - ground.width, -ground.yMin - ground.height, ground.width, ground.height);
            twin.Show(ToScreen(view, opposite), EditorTheme.Twin, 1.0f);
        }

        /// <summary>
        /// Draws whatever the mouse is dragging out.
        /// </summary>
        private void DrawDrag(EditorCameraRig view)
        {
            if (!session.IsDragging)
            {
                dragged.Hide();
                return;
            }

            Rect drawn = session.DrawnLand;
            if (drawn.width > 0.0f || drawn.height > 0.0f)
            {
                dragged.Show(ToScreen(view, drawn), EditorTheme.Drawn, BarThickness);
                return;
            }

            // Moving something point-like: the box goes where it would land, at the size the
            // thing being carried is picked at.
            float reach = Mathf.Max(1.0f, LevelPick.ReachOf(session.Level, session.Selection));
            Vector3 to = session.DraggedTo;
            var ghost = new Rect(to.x - reach, to.z - reach, reach * 2.0f, reach * 2.0f);
            dragged.Show(ToScreen(view, ghost), EditorTheme.Drawn, BarThickness);
        }

        /// <summary>
        /// Returns the patch of ground one selection covers.
        /// </summary>
        /// <param name="level">The map.</param>
        /// <param name="picked">What is selected.</param>
        /// <returns>Its rectangle on the ground plane, in metres.</returns>
        /// <remarks>
        /// A rectangle of land answers with itself; everything else answers with a square the
        /// size it is picked at, so the box somebody sees is the box they have to click in.
        /// </remarks>
        private static Rect GroundRect(LevelDefinition level, EditSelection picked)
        {
            if (picked.Target == EditTarget.Land)
            {
                LevelLand piece = level.Land[picked.Index];
                return Rect.MinMaxRect(piece.MinX, piece.MinZ, piece.MaxX, piece.MaxZ);
            }

            Vector3 at = LevelEdits.PositionOf(level, picked);
            float reach = Mathf.Max(1.0f, LevelPick.ReachOf(level, picked));
            return new Rect(at.x - reach, at.z - reach, reach * 2.0f, reach * 2.0f);
        }

        /// <summary>
        /// Turns a rectangle on the ground into one on the view.
        /// </summary>
        /// <param name="view">The camera looking at the map.</param>
        /// <param name="ground">The rectangle, in metres, x across and z up.</param>
        /// <returns>The rectangle in fractions of the view from its bottom left.</returns>
        private static Rect ToScreen(EditorCameraRig view, Rect ground)
        {
            Vector2 low = Screen(view, new Vector3(ground.xMin, 0.0f, ground.yMin));
            Vector2 high = Screen(view, new Vector3(ground.xMax, 0.0f, ground.yMax));

            return Rect.MinMaxRect(
                Mathf.Min(low.x, high.x),
                Mathf.Min(low.y, high.y),
                Mathf.Max(low.x, high.x),
                Mathf.Max(low.y, high.y));
        }

        /// <summary>
        /// Turns a point in the world into a fraction of the view.
        /// </summary>
        /// <param name="view">The camera looking at the map.</param>
        /// <param name="world">The point.</param>
        /// <returns>Where it falls, in fractions of the view from its bottom left.</returns>
        /// <remarks>
        /// The <em>viewport</em> rather than the screen, and never converted to pixels. The
        /// two are the same number while the game is running and are not while the camera is
        /// rendering into a texture - there the still is 1920 across and the canvas is
        /// whatever the window happens to be - so a marker measured in pixels lands somewhere
        /// else in the picture. A fraction is the same fraction in both, and
        /// <see cref="EditorOutline"/> anchors by fractions for exactly this reason.
        /// </remarks>
        private static Vector2 Screen(EditorCameraRig view, Vector3 world)
        {
            Vector3 point = view.View.WorldToViewportPoint(world);
            return new Vector2(point.x, point.y);
        }

        private void HideAll()
        {
            selection.Hide();
            dragged.Hide();
            twin.Hide();
            origin.Hide();
        }

        /// <summary>
        /// Removes any markers already built, so building twice is not building twice over.
        /// </summary>
        private void Clear()
        {
            Transform existing = transform.Find(MarkersNodeName);
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

            selection = null;
            dragged = null;
            twin = null;
            origin = null;
        }
    }
}
