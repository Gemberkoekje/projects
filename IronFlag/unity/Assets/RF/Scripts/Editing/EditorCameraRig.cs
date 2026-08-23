using UnityEngine;
using IronFlag.Levels;

namespace IronFlag.Editing
{
    /// <summary>
    /// The editor's view of a map: straight down, orthographic, panned and zoomed by hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same shot <c>LevelPreview</c> renders a map still with, for the same reason: a map
    /// read at a perspective is one whose far end is a different scale from its near end, and
    /// most of what a level designer does is compare one half of a map with the other. It is
    /// also what makes the mouse usable - with the camera looking straight down at the plane
    /// the whole game is resolved on, a pixel is a place, and it stays the same place at
    /// every zoom.
    /// </para>
    /// <para>
    /// Deliberately not the game's <see cref="IronFlag.Core.TopDownCameraRig"/>. That one is
    /// perspective, tilted, and follows a vehicle at a fixed height; every one of those is
    /// right for driving and wrong for drawing.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Editor Camera Rig")]
    [RequireComponent(typeof(Camera))]
    public sealed class EditorCameraRig : MonoBehaviour
    {
        /// <summary>Metres the camera sits above the ground plane.</summary>
        /// <remarks>
        /// High enough to be clear of anything a map can contain and well inside the far
        /// clip. Nothing about an orthographic view changes with height, so this is only ever
        /// about what is in front of the near plane.
        /// </remarks>
        public const float Height = 200.0f;

        /// <summary>Closest the view can be zoomed, as an orthographic half-height in metres.</summary>
        /// <remarks>
        /// About two vehicle lengths, which is close enough to place a tree against a wall
        /// and far enough that the map has not become a texture.
        /// </remarks>
        public const float NearestZoom = 12.0f;

        /// <summary>Furthest the view can be zoomed out, as a half-height in metres.</summary>
        public const float FurthestZoom = 220.0f;

        /// <summary>Metres of sea left showing around the land when the view is framed.</summary>
        private const float FramingMargin = 14.0f;

        /// <summary>How much one notch of the wheel multiplies the zoom by.</summary>
        /// <remarks>
        /// Multiplicative rather than a fixed number of metres, so a notch covers the same
        /// proportion of the view at every zoom - which is what makes going from a whole map
        /// to one building take a handful of notches rather than forty.
        /// </remarks>
        private const float ZoomPerNotch = 1.14f;

        [SerializeField]
        [Tooltip("Where the middle of the view is, on the ground plane.")]
        private Vector3 focus = Vector3.zero;

        [SerializeField]
        [Tooltip("Half the height of the view, in metres.")]
        private float zoom = 110.0f;

        [SerializeField]
        [Tooltip("Metres from the origin the view may be panned, in each direction.")]
        private float leash = 240.0f;

        [SerializeField]
        [Tooltip("The part of the view the panels do not cover, in fractions of the screen.")]
        private Rect usable = new Rect(0.0f, 0.0f, 1.0f, 1.0f);

        private Camera view;

        /// <summary>The camera this drives.</summary>
        public Camera View
        {
            get
            {
                if (view == null)
                {
                    view = GetComponent<Camera>();
                }

                return view;
            }
        }

        /// <summary>Where the middle of the view is, on the ground plane.</summary>
        public Vector3 Focus => focus;

        /// <summary>
        /// Metres one pixel of the view covers on the ground.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The conversion the whole editor's feel rests on: a pan has to move the map by
        /// exactly the distance the mouse moved, or the map slides out from under the cursor.
        /// Measured off height rather than width, because that is the dimension an
        /// orthographic size is stated in.
        /// </para>
        /// <para>
        /// Off the camera rather than off <c>Screen</c>, so it is still right when the camera
        /// is rendering into a texture - which is what the command-line still does, and is the
        /// one case where the two are different numbers.
        /// </para>
        /// </remarks>
        public float MetresPerPixel
            => View.pixelHeight <= 0 ? 0.0f : zoom * 2.0f / View.pixelHeight;

        /// <summary>
        /// Sets the camera up as an overhead orthographic view.
        /// </summary>
        /// <param name="worldExtent">Half-width of the world, for the pan limit.</param>
        /// <remarks>
        /// Called by the scene generator. Kept separate from <see cref="Apply"/> so the
        /// settings that never change are not written every frame.
        /// </remarks>
        public void Configure(float worldExtent)
        {
            SetWorldExtent(worldExtent);

            Camera camera = View;
            camera.orthographic = true;
            // Well inside a metre, because the editor's panels hang on this camera at a plane
            // distance of one: a near plane at that distance would clip them away entirely.
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = Height * 2.0f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            // Not black. A level's sea is a slab of a stated size, so an editor zoomed out
            // far enough to judge a map is looking past the edge of the world on two sides -
            // and a black band there reads as a rendering fault rather than as the end of the
            // map. A shade under the deep water says "there is nothing here" quietly.
            camera.backgroundColor = new Color(0.045f, 0.055f, 0.065f, 1.0f);
            camera.rect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);
            Apply();
        }

        /// <summary>
        /// Moves the pan limit to fit a world of this size.
        /// </summary>
        /// <param name="worldExtent">Half-width of the world, for the pan limit.</param>
        /// <remarks>
        /// Split out of <see cref="Configure"/> because the world is not fixed at scene
        /// generation time: the inspector lets the bounds be edited live, and a leash sized
        /// for the map the editor opened on would leave anything drawn past the old edge
        /// unreachable by pan, zoom or frame - a piece of a bigger map the tools cannot get
        /// back to. <see cref="LevelEditorSession.Rebuild"/> calls this every time the level
        /// is rebuilt, so the limit never falls behind what the level file actually says.
        /// </remarks>
        public void SetWorldExtent(float worldExtent)
            => leash = Mathf.Max(1.0f, Mathf.Abs(worldExtent)) + FurthestZoom;

        /// <summary>
        /// Tells the view how much of itself the panels are covering.
        /// </summary>
        /// <param name="free">
        /// The part of the screen nothing is drawn over, in fractions from the bottom left.
        /// </param>
        /// <remarks>
        /// Only <see cref="Frame"/> uses it, and that is the point: panning and zooming are
        /// done against the cursor, which is somewhere the player can see by definition.
        /// Framing is the one thing that decides for itself where to put the map, and a map
        /// centred on the screen is a map a third of which is behind an inspector.
        /// </remarks>
        public void SetUsableArea(Rect free)
        {
            if (free.width > 0.05f && free.height > 0.05f)
            {
                usable = free;
            }
        }

        /// <summary>
        /// Frames the view on a whole map, in the part of the screen the panels leave free.
        /// </summary>
        /// <param name="level">The map, or <c>null</c> to leave the view alone.</param>
        /// <remarks>
        /// On the land rather than on the bounds, so opening a level shows the island rather
        /// than the sea around it. What a designer wants to see first is the shape of the
        /// thing they drew.
        /// </remarks>
        public void Frame(LevelDefinition level)
        {
            if (level == null)
            {
                return;
            }

            Bounds land = level.LandBounds();
            float aspect = View.aspect <= 0.0f ? 1.0f : View.aspect;

            // The land has to fit the free window rather than the whole view, so the zoom is
            // whichever of the two demands the most: half a screen of width buys half as many
            // metres of it.
            float forHeight = (land.extents.z + FramingMargin) / usable.height;
            float forWidth = (land.extents.x + FramingMargin) / (usable.width * aspect);
            zoom = Mathf.Clamp(Mathf.Max(forHeight, forWidth), NearestZoom, FurthestZoom);

            // The camera looks at the middle of the screen, so putting the land in the middle
            // of the free window means aiming off by however far that window's centre is from
            // the screen's.
            Vector2 middle = usable.center;
            focus = new Vector3(
                land.center.x - ((middle.x - 0.5f) * 2.0f * zoom * aspect),
                0.0f,
                land.center.z - ((middle.y - 0.5f) * 2.0f * zoom));

            Apply();
        }

        /// <summary>
        /// Slides the view by a distance in metres on the ground plane.
        /// </summary>
        /// <param name="metres">How far to move the middle of the view.</param>
        public void Pan(Vector3 metres)
        {
            focus = new Vector3(
                Mathf.Clamp(focus.x + metres.x, -leash, leash),
                0.0f,
                Mathf.Clamp(focus.z + metres.z, -leash, leash));

            Apply();
        }

        /// <summary>
        /// Zooms the view, keeping one place on the map under the cursor.
        /// </summary>
        /// <param name="notches">Wheel notches; positive zooms in.</param>
        /// <param name="hold">The point on the ground the cursor is over, which stays there.</param>
        /// <remarks>
        /// Zooming towards the cursor rather than towards the middle of the screen. The
        /// alternative makes reaching a corner of the map a sequence of zoom, pan, zoom, pan;
        /// this makes it one gesture, and it is the behaviour every map tool has taught
        /// everybody to expect.
        /// </remarks>
        public void ZoomBy(float notches, Vector3 hold)
        {
            if (Mathf.Approximately(notches, 0.0f))
            {
                return;
            }

            float wanted = Mathf.Clamp(
                zoom / Mathf.Pow(ZoomPerNotch, notches), NearestZoom, FurthestZoom);

            // How far the held point is from the middle shrinks by exactly the same factor
            // the view does, which is what keeps it under the cursor.
            float share = wanted / zoom;
            var offset = new Vector3(hold.x - focus.x, 0.0f, hold.z - focus.z);

            zoom = wanted;
            focus = new Vector3(
                Mathf.Clamp(hold.x - (offset.x * share), -leash, leash),
                0.0f,
                Mathf.Clamp(hold.z - (offset.z * share), -leash, leash));

            Apply();
        }

        /// <summary>
        /// Moves the view so a place on the map is in the middle of it.
        /// </summary>
        /// <param name="at">The place; its height is ignored.</param>
        public void LookAt(Vector3 at)
        {
            focus = new Vector3(
                Mathf.Clamp(at.x, -leash, leash), 0.0f, Mathf.Clamp(at.z, -leash, leash));
            Apply();
        }

        /// <summary>
        /// Writes the focus and the zoom onto the camera.
        /// </summary>
        /// <remarks>
        /// Called by every change rather than in <c>Update</c>, so the camera is correct in a
        /// saved scene and in a still as well as while playing - the same arrangement the
        /// generated scenes rely on everywhere else in this project.
        /// </remarks>
        public void Apply()
        {
            Camera camera = View;
            camera.orthographicSize = zoom;
            camera.transform.SetPositionAndRotation(
                new Vector3(focus.x, Height, focus.z), Quaternion.Euler(90.0f, 0.0f, 0.0f));
        }

        private void OnValidate()
        {
            zoom = Mathf.Clamp(zoom, NearestZoom, FurthestZoom);
            Apply();
        }
    }
}
