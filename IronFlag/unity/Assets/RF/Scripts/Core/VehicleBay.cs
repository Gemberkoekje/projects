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
    /// for by name, and it is now a real journey rather than a metre of empty ground: the
    /// vehicle rolls out of its bay onto the lift car, rides the shaft to the surface, and
    /// steps off - three legs, and the same total for everyone whichever deck they started
    /// on, because "the same second and a bit for everyone" is worth more than a constant
    /// speed nobody can measure. During the beat the movement model is switched off and the
    /// body is kinematic, so nothing fights the animation for control of the transform.
    /// </para>
    /// <para>
    /// A vehicle is parked rather than destroyed while it waits, for the same reason it was
    /// in M3: every vehicle is a fixed roster entry its owner will drive again, so deleting
    /// one would mean rebuilding the player's roster, its camera target and its team paint
    /// around the replacement. It is also <em>visible</em> where it waits, which is what the
    /// hall under the bunker is for - what used to make a stowed vehicle unreachable was
    /// having its renderers off, and what makes it unreachable now is being twelve metres
    /// underground with no collider. A vehicle being repaired is the exception: an empty bay
    /// is the honest picture of a wreck in the shop, and the model this game has of a wreck
    /// is the intact one.
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
        private float rideYawDegrees;
        private float ridden;

        /// <summary>The ride out, as the corners it turns, in order.</summary>
        private readonly Vector3[] leg = new Vector3[4];

        /// <summary>Which way the vehicle points at each of those corners.</summary>
        private readonly float[] legYaw = new float[4];

        /// <summary>How many corners of <see cref="leg"/> are in use this ride.</summary>
        private int legs;

        /// <summary>
        /// How the ride out is shared between rolling out of the bay, riding the shaft, and
        /// stepping off at the top.
        /// </summary>
        /// <remarks>
        /// Fractions of the whole ride rather than metres a second, so a vehicle on the lower
        /// deck and one on the upper take exactly as long as each other. The alternative -
        /// a constant speed - would make the jeep's deploy noticeably slower than the tank's
        /// for a reason no player could see, and the ride is a pacing beat rather than a
        /// simulation of a hoist.
        /// </remarks>
        private static readonly float[] LegShare = { 0.30f, 0.55f, 0.15f };

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
        /// Which bay of its bunker this vehicle waits in.
        /// </summary>
        /// <remarks>
        /// Its own place in the roster, which is the same order every side's bunker, every
        /// level file's reserve and every panel in the game already uses. Read off the
        /// vehicle rather than wired up by whoever built the scene, so a vehicle assembled in
        /// a test knows its own bay without being told.
        /// </remarks>
        public int Slot
        {
            get
            {
                EnsureParts();
                return controller == null
                    ? -1
                    : Array.IndexOf(VehicleRoster.Kinds, controller.Kind);
            }
        }

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

            Park(true);
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
            PlanTheRide(bunker, at);
            ridden = 0.0f;
            state = VehicleBayState.Deploying;

            Restore();
            SetRenderers(true);
            SetColliders(true);
            Freeze();
            PlaceAlongRide(0.0f);

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
                    SetRenderers(true);
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
        /// Straight lines at a constant rate along each of them. The lift is a platform on a
        /// hydraulic ram, and neither it nor a helicopter climbing away is a movement
        /// anybody watches closely enough to want easing on - what is being bought is the
        /// second, not the curve. The one thing that is eased is the heading, because a
        /// vehicle that turned from facing across its bay to facing the field in a single
        /// frame would read as a different vehicle appearing.
        /// </remarks>
        private void Ride()
        {
            ridden += Time.deltaTime;
            float travelled = deploySeconds <= 0.0f ? 1.0f : Mathf.Clamp01(ridden / deploySeconds);
            PlaceAlongRide(travelled);

            if (travelled >= 1.0f)
            {
                Arrive();
            }
        }

        /// <summary>
        /// Works out the corners the ride out turns, and which way the vehicle faces at each.
        /// </summary>
        /// <param name="bunker">The bunker being left, or null for a vehicle with none.</param>
        /// <param name="at">The bunker's deploy point for this vehicle.</param>
        /// <remarks>
        /// Two shapes, and which one it is depends entirely on whether there is a hall to
        /// come out of. With one, the ride is bay to shaft to surface to standing-place, and
        /// the vehicle turns from facing across its bay to facing the field while the shaft
        /// carries it. Without one - which is every vehicle in every test that builds a
        /// bunker out of two empty markers - it is the single lift a vehicle has always
        /// made, out of the ground and onto it.
        /// </remarks>
        private void PlanTheRide(TeamBunker bunker, Vector3 at)
        {
            Vector3 end = RideTo(at);
            int slot = Slot;

            if (bunker == null || !bunker.HasHall || bunker.BayNode(slot) == null)
            {
                leg[0] = RideFrom(at);
                leg[1] = end;
                legYaw[0] = rideYawDegrees;
                legYaw[1] = rideYawDegrees;
                legs = 2;
                return;
            }

            Vector3 bay = bunker.BayFor(slot);
            leg[0] = bay;
            leg[1] = bunker.ShaftPoint(bay.y);
            leg[2] = bunker.LiftPoint;
            leg[3] = end;

            legYaw[0] = BayYaw(bunker, slot);
            legYaw[1] = legYaw[0];
            legYaw[2] = rideYawDegrees;
            legYaw[3] = rideYawDegrees;
            legs = 4;
        }

        /// <summary>
        /// Puts the vehicle - and the car under it - at one point along the ride.
        /// </summary>
        /// <param name="travelled">How much of the ride is done, from zero to one.</param>
        /// <remarks>
        /// The car is told where to be rather than asked, because it is the deck this
        /// vehicle is standing on and a deck that eased into place a frame late would be a
        /// vehicle hovering. It stops at the top of the shaft: the last leg is the vehicle
        /// stepping off, which for the helicopter means climbing away over the roof.
        /// </remarks>
        private void PlaceAlongRide(float travelled)
        {
            float t = Mathf.Clamp01(travelled);
            int corner;
            float within;

            if (legs - 1 == LegShare.Length)
            {
                float cumulative = 0.0f;
                corner = LegShare.Length - 1;
                within = 1.0f;
                for (int i = 0; i < LegShare.Length; i++)
                {
                    float share = Mathf.Max(LegShare[i], 0.0001f);
                    float next = cumulative + share;
                    if (t < next || i == LegShare.Length - 1)
                    {
                        corner = i;
                        within = Mathf.Clamp01((t - cumulative) / share);
                        break;
                    }

                    cumulative = next;
                }
            }
            else
            {
                float along = t * (legs - 1);
                corner = Mathf.Min((int)along, legs - 2);
                within = along - corner;
            }

            Vector3 at = Vector3.Lerp(leg[corner], leg[corner + 1], within);
            Place(at, Mathf.LerpAngle(legYaw[corner], legYaw[corner + 1], within));

            TeamBunker bunker = TeamBunker.For(Team);
            if (bunker != null && bunker.Car != null && legs > 2)
            {
                bunker.Car.Snap(Mathf.Min(at.y, bunker.LiftPoint.y));
            }
        }

        /// <summary>
        /// Returns which way a vehicle faces while it waits in its bay.
        /// </summary>
        /// <param name="bunker">The bunker whose hall it waits in.</param>
        /// <param name="slot">Roster index of the bay.</param>
        /// <returns>A heading, clockwise from world +Z.</returns>
        /// <remarks>
        /// Across the bay, towards the shaft, which is two things at once: every vehicle
        /// presents its flank to the only camera that ever looks in here - a tank seen
        /// nose-on in a dark room is a box - and every one of them drives forwards out of its
        /// own bay rather than reversing onto the lift.
        /// </remarks>
        private static float BayYaw(TeamBunker bunker, int slot)
        {
            Vector3 bay = bunker.transform.InverseTransformPoint(bunker.BayFor(slot));
            return bunker.FacingYawDegrees + (bay.x >= 0.0f ? -90.0f : 90.0f);
        }

        /// <summary>
        /// Hands the vehicle over to its pilot at the end of the ride.
        /// </summary>
        private void Arrive()
        {
            state = VehicleBayState.OnField;
            Thaw();

            Vector3 end = leg[legs - 1];
            if (controller is Helicopter flyer)
            {
                flyer.Deploy(new Vector3(end.x, 0.0f, end.z), rideYawDegrees);
            }
            else if (controller != null)
            {
                controller.Teleport(end, rideYawDegrees);
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
        /// the roof, and with the collective gone the pilot has no way to climb off it.
        /// The ride out is now the <em>only</em> time a helicopter is ever between the pad
        /// and its altitude.
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
                return new Vector3(at.x, flyer.Flight.CruiseAltitude, at.z);
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

            // A wreck is not drawn in its bay. The only model this game has of a vehicle is
            // the intact one, and an intact tank sitting under the word REPAIRING would be
            // saying the opposite of what the panel says.
            Park(false);
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
        /// <param name="shown">Whether the vehicle is drawn where it is parked.</param>
        private void Park(bool shown)
        {
            Freeze();
            SetColliders(false);
            SetRenderers(shown);

            TeamBunker bunker = TeamBunker.For(Team);
            int slot = Slot;
            bool inABay = bunker != null && bunker.HasHall && bunker.BayNode(slot) != null;

            Place(
                BunkerRestingPlace(bunker, slot),
                inABay ? BayYaw(bunker, slot) : homeYawDegrees);
        }

        /// <summary>
        /// Returns where a parked vehicle sits while it waits to be picked.
        /// </summary>
        /// <param name="bunker">Its own side's bunker, or null when it has none.</param>
        /// <param name="slot">Its place in the roster, which is its bay.</param>
        /// <returns>Its bay, the top of the shaft, or where it started.</returns>
        /// <remarks>
        /// A real place rather than the world origin, and now a real place you can look at:
        /// a vehicle in its bay is the thing the select camera is pointed at. A bunker with
        /// no hall still parks its vehicles somewhere sensible - the top of the shaft, which
        /// is inside the bunker's own supply radius, so a parked vehicle is still served by
        /// the building it is parked in whichever of the two it is.
        /// </remarks>
        private Vector3 BunkerRestingPlace(TeamBunker bunker, int slot)
        {
            if (bunker == null)
            {
                return homePosition;
            }

            return bunker.HasHall && bunker.BayNode(slot) != null
                ? bunker.BayFor(slot)
                : bunker.transform.position;
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

        private void Place(Vector3 at, float yawDegrees)
        {
            Quaternion facing = Quaternion.Euler(0.0f, yawDegrees, 0.0f);
            transform.SetPositionAndRotation(at, facing);

            if (body != null)
            {
                body.position = at;
                body.rotation = facing;
            }
        }

        /// <summary>
        /// Draws or stops drawing the vehicle.
        /// </summary>
        /// <param name="visible">Whether it is drawn.</param>
        /// <remarks>
        /// Split from <see cref="SetColliders"/>, which used to be the same call. A parked
        /// vehicle is now visible <em>and</em> intangible, which was not a state this needed
        /// before the bays were somewhere to look.
        /// </remarks>
        private void SetRenderers(bool visible)
        {
            foreach (Renderer part in skin)
            {
                if (part != null)
                {
                    part.enabled = visible;
                }
            }
        }

        /// <summary>
        /// Puts the vehicle in or out of the physics world.
        /// </summary>
        /// <param name="solid">Whether it can be touched.</param>
        /// <remarks>
        /// This is what makes a parked vehicle unreachable, and it is the whole of it. Being
        /// underground is where it is; being intangible is why nothing can hit it there.
        /// </remarks>
        private void SetColliders(bool solid)
        {
            foreach (Collider part in shell)
            {
                if (part != null)
                {
                    part.enabled = solid;
                }
            }
        }
    }
}
