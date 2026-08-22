using UnityEngine;
using IronFlag.Vehicles;

namespace IronFlag.Core
{
    /// <summary>
    /// One player's view: a camera at a fixed angle, following whichever vehicle that
    /// player is driving, inside its own slice of the screen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The angle never changes. The original this is a homage to reads instantly because
    /// every player always sees the world the same way up, and a camera that rotates behind
    /// the vehicle would make the two halves of a split screen disagree about which way
    /// north is. Only the focus point moves.
    /// </para>
    /// <para>
    /// One rig drives one camera, so split-screen is two of these with different
    /// <see cref="Viewport"/> rectangles rather than anything new. The placement maths is
    /// static and side-effect free so the framing can be checked without a scene.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("IronFlag/Top Down Camera Rig")]
    public sealed class TopDownCameraRig : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Downward tilt in degrees. 90 is straight down; less shows the silhouettes.")]
        private float pitchDegrees = 58.0f;

        [SerializeField]
        [Tooltip("Compass heading of the view. The same for every player, always.")]
        private float yawDegrees = 0.0f;

        [SerializeField]
        [Tooltip("Metres from the focus point back along the view direction.")]
        private float distance = 34.0f;

        [SerializeField]
        [Tooltip("Seconds for the camera to catch up with its target.")]
        private float followDamping = 0.22f;

        [SerializeField]
        [Tooltip("Seconds of travel to look ahead by, so the view leads a moving vehicle.")]
        private float leadSeconds = 0.45f;

        [SerializeField]
        [Tooltip("Cap on the look-ahead, in metres.")]
        private float maxLead = 9.0f;

        [SerializeField]
        [Tooltip("Fraction of a flying target's altitude the focus rises by.")]
        [Range(0.0f, 1.0f)]
        private float altitudeFollow = 0.35f;

        private VehicleController target;
        private Camera view;
        private Vector3 focus;
        private Vector3 focusVelocity;

        /// <summary>Downward tilt of the view, in degrees.</summary>
        public float PitchDegrees => pitchDegrees;

        /// <summary>Compass heading of the view, in degrees.</summary>
        public float YawDegrees => yawDegrees;

        /// <summary>Metres from the focus point back along the view direction.</summary>
        public float Distance => distance;

        /// <summary>The vehicle being followed, or null when the rig is parked.</summary>
        public VehicleController Target => target;

        /// <summary>The camera this rig drives.</summary>
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

        /// <summary>
        /// The slice of the screen this player's view occupies, in normalised coordinates.
        /// </summary>
        /// <remarks>
        /// Set from <see cref="SplitScreenLayout.ViewportFor"/> by
        /// <c>LocalMultiplayer</c>, which is the only thing that knows how many players are
        /// sharing the screen.
        /// </remarks>
        public Rect Viewport
        {
            get => View.rect;
            set => View.rect = value;
        }

        /// <summary>
        /// Follows a different vehicle.
        /// </summary>
        /// <param name="vehicle">Vehicle to follow; null parks the camera where it is.</param>
        /// <param name="snap">
        /// Whether to jump straight to the new framing. True when the player switches
        /// vehicles, since sweeping the camera across the map is disorienting rather than
        /// smooth.
        /// </param>
        public void SetTarget(VehicleController vehicle, bool snap)
        {
            target = vehicle;
            if (snap)
            {
                SnapToTarget();
            }
        }

        /// <summary>
        /// Parks the camera on a fixed point on the map and stops following anything.
        /// </summary>
        /// <param name="point">Point on the map to centre on.</param>
        /// <remarks>
        /// What a player looking at their own bunker between vehicles is shown. There is no
        /// unsnapped version of this on purpose: the two moments it is used for - arriving
        /// at the select screen and leaving it - are cuts, and a camera sweeping the length
        /// of the map to reach a menu is a camera the player is waiting on.
        /// </remarks>
        public void Park(Vector3 point)
        {
            target = null;
            focus = point;
            focusVelocity = Vector3.zero;
            ApplyFocus();
        }

        /// <summary>
        /// Places the camera at its final framing for the current target immediately.
        /// </summary>
        public void SnapToTarget()
        {
            if (target == null)
            {
                return;
            }

            focus = SolveFocus(target.transform.position, target.Velocity, leadSeconds, maxLead, altitudeFollow);
            focusVelocity = Vector3.zero;
            ApplyFocus();
        }

        /// <summary>
        /// Returns the point on the map the camera should be centred on.
        /// </summary>
        /// <param name="targetPosition">Where the followed vehicle is.</param>
        /// <param name="targetVelocity">How fast it is going, for the look-ahead.</param>
        /// <param name="leadSeconds">Seconds of travel to look ahead by.</param>
        /// <param name="maxLead">Cap on the look-ahead, in metres.</param>
        /// <param name="altitudeFollow">Fraction of the target's height the focus rises by.</param>
        /// <returns>The focus point in world space.</returns>
        /// <remarks>
        /// Only horizontal travel leads the camera - a climbing helicopter should not push
        /// the view off the map - and altitude is followed at a fraction, so flying high
        /// shows more ground instead of chasing the aircraft into the sky.
        /// </remarks>
        public static Vector3 SolveFocus(
            Vector3 targetPosition,
            Vector3 targetVelocity,
            float leadSeconds,
            float maxLead,
            float altitudeFollow)
        {
            var planar = new Vector3(targetVelocity.x, 0.0f, targetVelocity.z);
            Vector3 lead = Vector3.ClampMagnitude(planar * leadSeconds, Mathf.Max(0.0f, maxLead));

            Vector3 focus = targetPosition + lead;
            focus.y = targetPosition.y * Mathf.Clamp01(altitudeFollow);
            return focus;
        }

        /// <summary>
        /// Returns where the camera sits to look at a focus point from the fixed angle.
        /// </summary>
        /// <param name="focus">Point being looked at.</param>
        /// <param name="pitchDegrees">Downward tilt of the view.</param>
        /// <param name="yawDegrees">Compass heading of the view.</param>
        /// <param name="distance">Metres back along the view direction.</param>
        /// <returns>The camera position in world space.</returns>
        public static Vector3 SolveCameraPosition(
            Vector3 focus, float pitchDegrees, float yawDegrees, float distance)
            => focus - (SolveRotation(pitchDegrees, yawDegrees) * Vector3.forward * distance);

        /// <summary>
        /// Returns the camera's orientation, which depends on nothing but the fixed angle.
        /// </summary>
        /// <param name="pitchDegrees">Downward tilt of the view.</param>
        /// <param name="yawDegrees">Compass heading of the view.</param>
        /// <returns>The camera rotation.</returns>
        public static Quaternion SolveRotation(float pitchDegrees, float yawDegrees)
            => Quaternion.Euler(pitchDegrees, yawDegrees, 0.0f);

        private void Awake() => view = GetComponent<Camera>();

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            Vector3 wanted = SolveFocus(
                target.transform.position, target.Velocity, leadSeconds, maxLead, altitudeFollow);
            focus = Vector3.SmoothDamp(
                focus, wanted, ref focusVelocity, followDamping, Mathf.Infinity, Time.deltaTime);
            ApplyFocus();
        }

        private void ApplyFocus()
            => transform.SetPositionAndRotation(
                SolveCameraPosition(focus, pitchDegrees, yawDegrees, distance),
                SolveRotation(pitchDegrees, yawDegrees));
    }
}
