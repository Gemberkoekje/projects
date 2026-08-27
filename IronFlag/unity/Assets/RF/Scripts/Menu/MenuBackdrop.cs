using UnityEngine;
using IronFlag.Core;
using IronFlag.Levels;

namespace IronFlag.Menu
{
    /// <summary>
    /// The map behind the menu, and the slow turn the camera makes around it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The menu could have been panels on a dark screen and it would have worked. It stands on
    /// a real map instead because the game already builds one from a file, lights it the way it
    /// lights a match and grades it through the same volume - so the cost of showing the game
    /// on the way into the game is one camera and this file, and the alternative is a boot
    /// screen that shows nothing the project has spent nine milestones on.
    /// </para>
    /// <para>
    /// It <em>orbits</em> rather than drifting across, and that is the one decision here worth
    /// arguing about. A pan looks better for about forty seconds and then reaches the coast,
    /// and everything that fixes it - turning round, easing back, wrapping - is a rule about
    /// where the edges are on a map whose size is read out of a file. An orbit has no edge to
    /// reach: it is one angle advancing forever, it never has to be told how big the map is
    /// except to know how far to stand back, and a player who leaves the menu up while they
    /// find a controller sees the whole island rather than the far end of it.
    /// </para>
    /// <para>
    /// The angle is the game's own - <see cref="TopDownCameraRig.SolveCameraPosition"/> and
    /// <see cref="TopDownCameraRig.SolveRotation"/>, the same two functions a player's chase
    /// camera is placed by - pulled back and tilted down a little further because a menu frames
    /// an island and a chase camera frames a jeep. Sharing them is what stops the menu looking
    /// like a different game from the one it launches.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("IronFlag/Menu Backdrop")]
    public sealed class MenuBackdrop : MonoBehaviour
    {
        /// <summary>Downward tilt of the view, in degrees.</summary>
        /// <remarks>
        /// Near enough the chase camera's 58 to be recognisably the same game, tilted up a
        /// little because this one is looking at an island rather than at a jeep. Not flatter,
        /// because a flatter angle from any useful distance puts the horizon in frame - and
        /// this game has no horizon: see <see cref="StandBack"/>.
        /// </remarks>
        public const float PitchDegrees = 52.0f;

        /// <summary>Vertical angle of view, in degrees.</summary>
        /// <remarks>
        /// A long lens, against the 60 degrees every other camera here uses, and it is the
        /// setting that makes the rest of this framing possible. At 60 degrees the horizontal
        /// field on a 16:9 screen is 91 degrees, and the corners of a frame that wide reach
        /// past the edge of the world from any distance that shows a useful amount of map. It
        /// also flatters what it looks at, which is what a menu is for.
        /// </remarks>
        public const float FieldOfView = 34.0f;

        /// <summary>How far round the map the view travels each second, in degrees.</summary>
        /// <remarks>
        /// A full turn every four minutes. Slow enough that a glance at the screen does not
        /// register it as motion, fast enough that a player who reads the whole level list
        /// looks up at a different view of the map.
        /// </remarks>
        public const float DegreesPerSecond = 1.5f;

        /// <summary>Fraction of the map's half-extent the camera stands back by.</summary>
        /// <remarks>
        /// <para>
        /// <strong>This game has no horizon, and that is what sets this number.</strong>
        /// <c>LevelBuilder</c> builds the sea as a slab exactly as wide as the map - two
        /// half-extents, no margin - so past the bounds there is nothing at all, and an oblique
        /// camera that can see that far photographs the edge of the world as a hard diagonal
        /// across the sky. Every other view in the project is either close to the ground or
        /// pointing straight down at it, so this is the first camera the question has ever come
        /// up for.
        /// </para>
        /// <para>
        /// The arithmetic says a wide shot of the whole island is simply not available: to hold
        /// 240 metres across the frame the far corners have to be past 120 metres out, which is
        /// where the sea stops. So the menu shows a close view of part of the map instead -
        /// which is the better picture anyway, because it is the one where the bunkers and the
        /// towers are models rather than dots. At this fraction, with
        /// <see cref="PitchDegrees"/> and <see cref="FieldOfView"/>, the far edge of the frame
        /// lands at about a third of a half-extent past the middle, with the corners closer
        /// still.
        /// </para>
        /// </remarks>
        public const float StandBack = 0.75f;

        /// <summary>Closest the camera is ever placed to the middle of the map, in metres.</summary>
        public const float NearestStandBack = 45.0f;

        /// <summary>Furthest the camera is ever placed from the middle of the map, in metres.</summary>
        public const float FurthestStandBack = 200.0f;

        /// <summary>
        /// How far to one side the camera steps, as a fraction of how far back it stands.
        /// </summary>
        /// <remarks>
        /// The menu's column covers the left third of the screen, so a camera aimed at the
        /// middle of the map would spend a third of its subject behind a panel. Stepping the
        /// camera left puts the map in the middle of what is actually visible. Measured against
        /// the distance rather than in metres, so it stays right on a map of any size.
        /// </remarks>
        public const float SideStep = 0.17f;

        /// <summary>
        /// How far short of the middle of the map the view is aimed, as a fraction of how far
        /// back the camera stands.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Aimed at the near half of the map rather than at the middle of it, and this is the
        /// one number here that is about what the picture is <em>of</em> rather than about
        /// where the edges are. Every map in this game is built as a pair rotated half a turn
        /// about the origin, so the middle is the one place that belongs to nobody - on the
        /// shipped map it is open water, and a menu aimed there is a menu showing a channel.
        /// Aiming short, toward whichever side the camera happens to be on, always lands on
        /// somebody's half: a bunker, its depots, its end of a bridge.
        /// </para>
        /// <para>
        /// It also buys back most of the margin <see cref="StandBack"/> spends. The frame ends
        /// this much closer to the far edge of the world than the camera distance alone would
        /// suggest, which is what lets the camera stand further off than it otherwise could.
        /// </para>
        /// </remarks>
        public const float LookInset = 0.35f;

        [SerializeField]
        [Tooltip("Compass heading the view starts at, in degrees.")]
        private float startYaw = 34.0f;

        [SerializeField]
        [Tooltip("Metres from the middle of the map, back along the view direction.")]
        private float distance = 150.0f;

        [SerializeField]
        [Tooltip("Metres above sea level the view is aimed at.")]
        private float focusHeight = 4.0f;

        private Camera view;
        private float turned;

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

        /// <summary>Where the view is aimed: the near half of the map - see <see cref="LookInset"/>.</summary>
        public Vector3 Focus
        {
            get
            {
                Vector3 along = TopDownCameraRig.SolveRotation(PitchDegrees, Yaw) * Vector3.forward;
                along.y = 0.0f;
                along = along.sqrMagnitude > 0.000001f ? along.normalized : Vector3.forward;
                return new Vector3(0.0f, focusHeight, 0.0f) - (along * (distance * LookInset));
            }
        }

        /// <summary>Metres from the middle of the map, back along the view direction.</summary>
        public float Distance => distance;

        /// <summary>Compass heading the view is currently at, in degrees.</summary>
        public float Yaw => startYaw + turned;

        /// <summary>
        /// Stands the camera back far enough to hold a map of this size.
        /// </summary>
        /// <param name="worldExtent">Half the width of the map, in metres.</param>
        /// <remarks>
        /// Read off the level rather than fixed, so the menu frames a 500 m map and a 120 m one
        /// the same way. Clamped at both ends because a level file is a text file somebody can
        /// put any number in, and a half-extent of 4 would put the camera inside a bunker.
        /// </remarks>
        public void Configure(float worldExtent)
        {
            distance = Mathf.Clamp(
                Mathf.Abs(worldExtent) * StandBack, NearestStandBack, FurthestStandBack);

            // Before Place, and before anything copies this camera's projection - the camera
            // drawing the menu over it takes a copy of the lens when it is attached, and one
            // that copied 60 degrees would lay the column out for a frame this one is not
            // showing.
            View.fieldOfView = FieldOfView;
            Place();
        }

        /// <summary>
        /// Stands the camera back far enough to hold a map, and aims it at the middle of it.
        /// </summary>
        /// <param name="level">The map being shown, or null to leave the framing alone.</param>
        public void Configure(LevelDefinition level)
        {
            if (level != null && level.Bounds != null)
            {
                Configure(level.Bounds.HalfExtent);
                return;
            }

            Place();
        }

        /// <summary>
        /// Puts the camera where the current heading says it should be.
        /// </summary>
        /// <remarks>
        /// Public and side-effect free apart from the transform, so the scene builder can leave
        /// the saved scene with its camera already in place. A scene whose camera only moved on
        /// the first frame of play is a scene that photographs as a view of the origin.
        /// </remarks>
        public void Place()
        {
            Quaternion facing = TopDownCameraRig.SolveRotation(PitchDegrees, Yaw);
            Vector3 at = TopDownCameraRig.SolveCameraPosition(Focus, PitchDegrees, Yaw, distance);

            // Stepped along the camera's own right rather than the world's, so the offset turns
            // with the orbit: the map stays in the same part of the screen the whole way round
            // instead of drifting behind the panel and back out again.
            at -= facing * Vector3.right * (distance * SideStep);

            transform.SetPositionAndRotation(at, facing);
        }

        /// <summary>
        /// Returns how far round the map the view has travelled after a while.
        /// </summary>
        /// <param name="seconds">How long the menu has been up.</param>
        /// <returns>Degrees turned, wrapped into a single revolution.</returns>
        /// <remarks>
        /// Wrapped rather than accumulated, so a menu somebody leaves up all afternoon is not a
        /// float that has quietly lost its fractional digits - which reads as a camera that
        /// starts stepping instead of turning.
        /// </remarks>
        public static float TurnedAfter(float seconds) => Mathf.Repeat(seconds * DegreesPerSecond, 360.0f);

        private void Awake() => view = GetComponent<Camera>();

        private void LateUpdate()
        {
            turned = Mathf.Repeat(turned + (DegreesPerSecond * Time.deltaTime), 360.0f);
            Place();
        }
    }
}
