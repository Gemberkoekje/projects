using System;
using UnityEngine;
using IronFlag.Combat;
using IronFlag.Supply;
using IronFlag.Vehicles;

namespace IronFlag.Core
{
    /// <summary>
    /// One vehicle's place in its bunker: where it waits, how long it takes to put back
    /// together, and the ride out onto the field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is M3's <c>VehicleRespawn</c> with the decision taken out of it. There, a wreck
    /// counted to four and drove itself back out; here it is repaired and then <em>waits</em>,
    /// because the design document's core loop has the pilot choose what comes out next.
    /// <see cref="IronFlag.Players.PlayerVehicleDriver"/> is what chooses; everything
    /// physical is here.
    /// </para>
    /// <para>
    /// The ride out is a real beat rather than a teleport, which the design document asks
    /// for by name: ground vehicles rise on the lift outside the bunker door and the
    /// helicopter climbs off the roof pad. It costs a second and a bit, it is the same
    /// second and a bit for everyone, and it is the only thing standing between choosing a
    /// vehicle and driving it. During the beat the movement model is switched off and the
    /// body is kinematic, so nothing fights the animation for control of the transform.
    /// </para>
    /// <para>
    /// A vehicle is hidden rather than destroyed while it waits, for the same reason it was
    /// in M3: every vehicle is a fixed roster entry its owner will drive again, so deleting
    /// one would mean rebuilding the player's roster, its camera target and its team paint
    /// around the replacement.
    /// </para>
    /// <para>
    /// Nothing here stows itself. A vehicle with a bay and no player is simply on the field,
    /// which is what a vehicle assembled in a test is, and it stays drivable.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Vehicle Bay")]
    public sealed class VehicleBay : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Seconds a wrecked vehicle spends being put back together in the bunker.")]
        private float repairSeconds = 4.0f;

        [SerializeField]
        [Tooltip("Seconds the ride out of the bunker takes, on the lift or off the pad.")]
        private float deploySeconds = 1.2f;

        private VehicleController controller;
        private VehicleHealth health;
        private VehicleSupply supply;
        private VehicleTeamPaint paint;
        private Rigidbody body;
        private Renderer[] skin = Array.Empty<Renderer>();
        private Collider[] shell = Array.Empty<Collider>();
        private Vector3 homePosition;
        private float homeYawDegrees;
        private float hullHeight = 2.0f;
        private float hullLength = 4.0f;
        private VehicleBayState state = VehicleBayState.None;
        private bool found;
        private float repairCountdown;
        private Vector3 rideFrom;
        private Vector3 rideTo;
        private float rideYawDegrees;
        private float ridden;

        /// <summary>Raised when the vehicle is out, drivable, and the beat has finished.</summary>
        public event Action<VehicleBay> Deployed;

        /// <summary>Raised when the vehicle leaves the field, whether wrecked or stowed.</summary>
        public event Action<VehicleBay> Returned;

        /// <summary>Seconds a wrecked vehicle spends being repaired.</summary>
        public float RepairSeconds => repairSeconds;

        /// <summary>Seconds the ride out of the bunker takes.</summary>
        public float DeploySeconds => deploySeconds;

        /// <summary>Where this vehicle is in the loop between the bunker and the field.</summary>
        public VehicleBayState State => state;

        /// <summary>Whether this vehicle is in the bunker and could be deployed now.</summary>
        public bool IsReady => state == VehicleBayState.Ready || state == VehicleBayState.None;

        /// <summary>Whether this vehicle is being put back together.</summary>
        public bool IsRepairing => state == VehicleBayState.Repairing;

        /// <summary>Whether this vehicle is riding out and is not drivable yet.</summary>
        public bool IsDeploying => state == VehicleBayState.Deploying;

        /// <summary>Whether this vehicle is out on the field.</summary>
        public bool IsOnField => state == VehicleBayState.OnField || state == VehicleBayState.None;

        /// <summary>Seconds of repairs left, or zero when there are none outstanding.</summary>
        public float RepairCountdown => Mathf.Max(0.0f, repairCountdown);

        /// <summary>The vehicle this bay holds.</summary>
        public VehicleController Vehicle => controller;

        /// <summary>Which side's bunker this vehicle belongs in.</summary>
        public Team Team => paint == null ? Team.None : paint.Team;

        /// <summary>
        /// Sets how long the two waits are.
        /// </summary>
        /// <param name="repair">Seconds spent being repaired after being wrecked.</param>
        /// <param name="ride">Seconds the ride out of the bunker takes.</param>
        /// <remarks>Called by the prefab builder.</remarks>
        public void Configure(float repair, float ride)
        {
            repairSeconds = Mathf.Max(0.0f, repair);
            deploySeconds = Mathf.Max(0.0f, ride);
        }

        /// <summary>
        /// Takes the vehicle off the field and puts it back in the bunker, intact.
        /// </summary>
        /// <remarks>
        /// This is what driving home and swapping vehicles does, and it is deliberately
        /// free: the vehicle is repaired and refuelled the moment it is inside, and can be
        /// picked again straight away. Dying costs <see cref="RepairSeconds"/> and this
        /// costs the drive home, which is the whole reason to make the drive.
        /// </remarks>
        public void Stow()
        {
            EnsureParts();
            bool wasOut = state == VehicleBayState.OnField || state == VehicleBayState.Deploying;

            Hide();
            Restore();
            state = VehicleBayState.Ready;
            repairCountdown = 0.0f;

            if (wasOut)
            {
                Returned?.Invoke(this);
            }
        }

        /// <summary>
        /// Starts the ride out of the bunker.
        /// </summary>
        /// <returns><c>true</c> when the vehicle was available and is on its way.</returns>
        /// <remarks>
        /// Refused rather than queued when the vehicle is still being repaired. A pilot
        /// holding the button on a wreck is asking for something that will be true in three
        /// seconds, and the panel is already showing them how long that is; quietly
        /// remembering the request would make the deploy happen at a moment nobody chose.
        /// </remarks>
        public bool Deploy()
        {
            if (state == VehicleBayState.Repairing || state == VehicleBayState.Deploying)
            {
                return false;
            }

            EnsureParts();
            TeamBunker bunker = TeamBunker.For(Team);
            Vector3 at = bunker == null
                ? homePosition
                : bunker.DeployPointFor(controller == null ? VehicleKind.None : controller.Kind);

            rideYawDegrees = bunker == null ? homeYawDegrees : bunker.FacingYawDegrees;
            rideTo = RideTo(at);
            rideFrom = RideFrom(at);
            ridden = 0.0f;
            state = VehicleBayState.Deploying;

            Restore();
            Show();
            Freeze();
            Place(rideFrom);

            if (deploySeconds <= 0.0f)
            {
                Arrive();
            }

            return true;
        }

        /// <summary>
        /// Puts the vehicle on the field immediately, skipping the ride out.
        /// </summary>
        /// <returns><c>true</c> when the vehicle was available.</returns>
        /// <remarks>
        /// For the tests, and for anything that wants a vehicle in play without waiting out
        /// a pacing beat nobody is watching.
        /// </remarks>
        public bool DeployNow()
        {
            if (!Deploy())
            {
                return false;
            }

            if (state == VehicleBayState.Deploying)
            {
                Arrive();
            }

            return true;
        }

        private void Awake() => EnsureParts();

        /// <summary>
        /// Finds the pieces of the vehicle this bay moves around, once.
        /// </summary>
        /// <remarks>
        /// Called from <c>Awake</c> in the game and from the two entry points that move a
        /// vehicle, because the command-line still deploys one without ever starting the
        /// game - and outside play mode nothing has woken up to have cached anything.
        /// </remarks>
        private void EnsureParts()
        {
            if (found)
            {
                return;
            }

            found = true;
            controller = GetComponent<VehicleController>();
            health = GetComponent<VehicleHealth>();
            supply = GetComponent<VehicleSupply>();
            paint = GetComponent<VehicleTeamPaint>();
            body = GetComponent<Rigidbody>();
            skin = GetComponentsInChildren<Renderer>(true);
            shell = GetComponentsInChildren<Collider>(true);
            homePosition = transform.position;
            homeYawDegrees = transform.eulerAngles.y;

            var box = GetComponent<BoxCollider>();
            if (box != null)
            {
                hullHeight = box.size.y;
                hullLength = box.size.z;
            }
        }

        private void OnEnable()
        {
            if (health != null)
            {
                health.Destroyed += OnDestroyed;
            }
        }

        private void OnDisable()
        {
            if (health != null)
            {
                health.Destroyed -= OnDestroyed;
            }
        }

        private void Update()
        {
            if (state == VehicleBayState.Repairing)
            {
                repairCountdown -= Time.deltaTime;
                if (repairCountdown <= 0.0f)
                {
                    repairCountdown = 0.0f;
                    Restore();
                    state = VehicleBayState.Ready;
                }

                return;
            }

            if (state == VehicleBayState.Deploying)
            {
                Ride();
            }
        }

        /// <summary>
        /// Moves the vehicle one frame further along its ride out of the bunker.
        /// </summary>
        /// <remarks>
        /// A straight line at a constant rate. The lift is a platform on a hydraulic ram
        /// and the helicopter is climbing off a pad, and neither of those is a movement
        /// anybody watches closely enough to want easing on - what is being bought is the
        /// second, not the curve.
        /// </remarks>
        private void Ride()
        {
            ridden += Time.deltaTime;
            float travelled = deploySeconds <= 0.0f ? 1.0f : Mathf.Clamp01(ridden / deploySeconds);
            Place(Vector3.Lerp(rideFrom, rideTo, travelled));

            if (travelled >= 1.0f)
            {
                Arrive();
            }
        }

        /// <summary>
        /// Hands the vehicle over to its pilot at the end of the ride.
        /// </summary>
        private void Arrive()
        {
            state = VehicleBayState.OnField;
            Thaw();

            if (controller is Helicopter flyer)
            {
                flyer.Deploy(new Vector3(rideTo.x, 0.0f, rideTo.z), rideYawDegrees);
            }
            else if (controller != null)
            {
                controller.Teleport(rideTo, rideYawDegrees);
            }

            Deployed?.Invoke(this);
        }

        /// <summary>
        /// Returns where a vehicle stands once it is out.
        /// </summary>
        /// <param name="at">The bunker's deploy point for this vehicle.</param>
        /// <returns>The end of the ride, in world space.</returns>
        /// <remarks>
        /// <para>
        /// The helicopter finishes at its own cruising altitude rather than on the pad it
        /// started from: an aircraft handed to its pilot at roof height would be flown into
        /// the roof.
        /// </para>
        /// <para>
        /// A ground vehicle finishes half its own length past the lift, because the lift
        /// platform is under three metres deep and the tank is five and a half metres long.
        /// Standing it on the middle of the platform would leave its back end inside the
        /// bunker wall, and physics would spend the first second of every deploy shoving it
        /// out - which reads as the vehicle being kicked as it appears.
        /// </para>
        /// </remarks>
        private Vector3 RideTo(Vector3 at)
        {
            if (controller is Helicopter flyer)
            {
                return new Vector3(at.x, flyer.Flight.DeployAltitude, at.z);
            }

            Vector3 outwards = Quaternion.Euler(0.0f, rideYawDegrees, 0.0f) * Vector3.forward;
            return at + (outwards * (hullLength * 0.5f));
        }

        /// <summary>
        /// Returns where a vehicle starts its ride.
        /// </summary>
        /// <param name="at">The bunker's deploy point for this vehicle.</param>
        /// <returns>The start of the ride, in world space.</returns>
        /// <remarks>
        /// A ground vehicle starts far enough under the lift platform to be out of sight,
        /// which is its own height: the lift bay is a hole in the ground and a vehicle
        /// whose roof is showing has not risen out of anything.
        /// </remarks>
        private Vector3 RideFrom(Vector3 at)
            => controller is Helicopter ? at : at - (Vector3.up * (hullHeight + 0.4f));

        private void OnDestroyed(VehicleHealth wrecked)
        {
            bool wasOut = state != VehicleBayState.Repairing;

            Hide();
            state = VehicleBayState.Repairing;
            repairCountdown = Mathf.Max(0.0001f, repairSeconds);

            if (wasOut)
            {
                Returned?.Invoke(this);
            }
        }

        /// <summary>
        /// Refills everything the vehicle spent while it was out.
        /// </summary>
        /// <remarks>
        /// The bunker is the one place that fills both pools instantly rather than at a
        /// rate. Waiting to be repaired is already the cost of dying, and a vehicle that
        /// came out of its own bunker dry would be a second, invisible one.
        /// </remarks>
        private void Restore()
        {
            if (health != null)
            {
                health.Repair();
            }

            if (supply != null)
            {
                supply.FillUp();
            }
        }

        /// <summary>
        /// Takes the vehicle off the field without taking it out of the scene.
        /// </summary>
        /// <remarks>
        /// The movement model is switched off rather than the whole object, because this
        /// component has to keep counting while the vehicle is gone. The controller is still
        /// switched off before the body is made kinematic, but that ordering is no longer
        /// what keeps the log quiet: the movement models sit a fixed step out on a kinematic
        /// body of their own accord, so one left running writes nothing rather than a warning
        /// per vehicle per step.
        /// </remarks>
        private void Hide()
        {
            Freeze();
            SetVisible(false);
            Place(BunkerRestingPlace());
        }

        /// <summary>
        /// Returns where a stowed vehicle sits while it waits to be picked.
        /// </summary>
        /// <returns>A point inside its own bunker, or where it started when it has none.</returns>
        /// <remarks>
        /// Out of sight, but a real place rather than the world origin: a stowed vehicle
        /// still has a position, and having it be the bunker means the bunker's own supply
        /// point serves it and that nothing is ever found sitting in the middle of the map.
        /// </remarks>
        private Vector3 BunkerRestingPlace()
        {
            TeamBunker bunker = TeamBunker.For(Team);
            return bunker == null ? homePosition : bunker.transform.position;
        }

        /// <summary>
        /// Takes the vehicle out of the physics simulation and out of its driver's hands.
        /// </summary>
        /// <remarks>
        /// The velocities are only cleared on a body that is still simulated. A kinematic
        /// body has no velocity to clear - the engine holds it at zero already - and writing
        /// one anyway logs "Setting linear velocity of a kinematic body is not supported"
        /// with a stack trace. That is not a hypothetical: freezing something already frozen
        /// is the ordinary path, because <see cref="Deploy"/> freezes a vehicle that has been
        /// standing in its bunker since it was stowed.
        /// </remarks>
        private void Freeze()
        {
            if (controller != null)
            {
                controller.ReleaseControls();
                controller.enabled = false;
            }

            if (body != null)
            {
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }

                body.isKinematic = true;
            }
        }

        private void Thaw()
        {
            if (body != null)
            {
                body.isKinematic = false;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            if (controller != null)
            {
                controller.enabled = true;
            }
        }

        private void Show() => SetVisible(true);

        private void Place(Vector3 at)
        {
            Quaternion facing = Quaternion.Euler(0.0f, rideYawDegrees, 0.0f);
            transform.SetPositionAndRotation(at, facing);

            if (body != null)
            {
                body.position = at;
                body.rotation = facing;
            }
        }

        private void SetVisible(bool visible)
        {
            foreach (Renderer part in skin)
            {
                if (part != null)
                {
                    part.enabled = visible;
                }
            }

            foreach (Collider part in shell)
            {
                if (part != null)
                {
                    part.enabled = visible;
                }
            }
        }
    }
}
