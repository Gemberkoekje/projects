using UnityEngine;

namespace IronFlag.Vehicles
{
    /// <summary>
    /// The one vehicle that ignores the ground: horizontal motion like the others, at a
    /// fixed altitude nobody chooses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Gravity is off and the altitude in <see cref="FlightState"/> is authoritative; the
    /// rigidbody is flown towards it each fixed step rather than allowed to decide where
    /// the aircraft ends up. That keeps "hover means hover" true even after a collision
    /// nudges the body, and it means the pilot never fights a settling aircraft.
    /// </para>
    /// <para>
    /// The pilot has no collective. Which altitude to sit at was the one decision the
    /// third axis offered and it has been taken away, so a helicopter is at
    /// <see cref="FlightTuning.CruiseAltitude"/> whenever its engine is running and on the
    /// ground when it is not - see <see cref="SetPowered"/>. Everything that used to read
    /// height off an aircraft to decide something, chiefly whether a bunker will serve it,
    /// had to stop: at one altitude, a rule about height is a rule about nothing.
    /// </para>
    /// <para>
    /// The visible tilt is applied to a child transform, so the collider stays upright and
    /// square to the world however hard the aircraft is banking.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Helicopter")]
    public sealed class Helicopter : VehicleController
    {
        [SerializeField]
        [Tooltip("Altitude and cosmetic-tilt numbers. Stamped when the prefab is built.")]
        private FlightTuning flight = new FlightTuning();

        [SerializeField]
        [Tooltip("Child transform the cosmetic pitch and roll are applied to. Never the collider.")]
        private Transform attitudePivot;

        private GroundMotionState motion = GroundMotionState.Still;
        private FlightState flightState = FlightState.Landed;
        private bool powered = true;
        private Vector2 attitude = Vector2.zero;
        private Vector2 attitudeVelocity = Vector2.zero;

        /// <summary>The altitude and tilt numbers this aircraft is flying with.</summary>
        public FlightTuning Flight => flight;

        /// <summary>Height above the ground plane the pilot is holding, in metres.</summary>
        public float Altitude => flightState.Altitude;

        /// <summary>Rate of climb in m/s; negative is descending.</summary>
        public float VerticalSpeed => flightState.VerticalSpeed;

        /// <summary>Whether the engine is running and the aircraft is holding its altitude.</summary>
        public bool IsPowered => powered;

        /// <inheritdoc/>
        public override float ForwardSpeed => motion.Speed;

        /// <inheritdoc/>
        public override Vector3 Velocity => Body == null ? motion.Velocity : Body.linearVelocity;

        /// <inheritdoc/>
        public override void Teleport(Vector3 position, float yawDegrees)
        {
            base.Teleport(position, yawDegrees);
            motion = new GroundMotionState(0.0f, yawDegrees);
            flightState = FlightState.Hovering(position.y);
        }

        /// <summary>
        /// Says whether the engine is still running.
        /// </summary>
        /// <param name="running">
        /// <c>true</c> while there is fuel in the tank; <c>false</c> once there is not.
        /// </param>
        /// <remarks>
        /// The only thing that moves a helicopter off its cruising altitude, and it is not
        /// the pilot. <see cref="IronFlag.Supply.VehicleSupply"/> is what says so, for the
        /// same reason it takes a stranded ground vehicle's throttle away: running dry is
        /// the economy's business, and a helicopter that hung in the sky after it did would
        /// be permanent cover nobody paid for. An aircraft with no supply component behind
        /// it is never told anything and simply keeps flying, which is what a test rig is.
        /// </remarks>
        public void SetPowered(bool running) => powered = running;

        /// <summary>
        /// Sets the flight numbers and the node the cosmetic tilt is applied to.
        /// </summary>
        /// <param name="flightTuning">Altitude and tilt numbers to fly with.</param>
        /// <param name="pivot">Child transform to tilt; never the one carrying the collider.</param>
        /// <remarks>Called by the prefab builder, alongside <see cref="VehicleController.Configure"/>.</remarks>
        public void ConfigureFlight(FlightTuning flightTuning, Transform pivot)
        {
            flight = flightTuning == null ? new FlightTuning() : flightTuning;
            attitudePivot = pivot;
        }

        /// <summary>
        /// Puts the aircraft in the air at its deploy altitude, ready to fly.
        /// </summary>
        /// <param name="groundPosition">Where on the ground plane to deploy from.</param>
        /// <param name="yawDegrees">Heading to face, clockwise from world +Z.</param>
        public void Deploy(Vector3 groundPosition, float yawDegrees)
        {
            var position = new Vector3(groundPosition.x, flight.CruiseAltitude, groundPosition.z);
            Teleport(position, yawDegrees);
        }

        /// <inheritdoc/>
        protected override void Awake()
        {
            base.Awake();
            motion = new GroundMotionState(0.0f, transform.eulerAngles.y);
            flightState = FlightState.Hovering(transform.position.y);
        }

        /// <inheritdoc/>
        protected override void ConfigureBody()
        {
            base.ConfigureBody();
            // The altitude model is the only thing allowed to move this aircraft vertically.
            Body.useGravity = false;
            Body.constraints = RigidbodyConstraints.FreezeRotation;
        }

        /// <summary>
        /// Steps the movement and altitude models and puts the result on the rigidbody.
        /// </summary>
        /// <remarks>
        /// A kinematic body sits the step out whole, exactly as
        /// <see cref="GroundVehicle"/> does: nothing is flying the aircraft towards its
        /// modelled altitude while physics is not moving it, and writing a velocity anyway
        /// logs "Setting linear velocity of a kinematic body is not supported" with a stack
        /// trace, once per fixed step. The controller stays enabled while it does nothing,
        /// because <see cref="VehicleController.OnEnable"/> is what puts a vehicle on
        /// <see cref="VehicleController.OnTheField"/>.
        /// </remarks>
        private void FixedUpdate()
        {
            if (Body.isKinematic)
            {
                return;
            }

            float deltaTime = Time.fixedDeltaTime;

            motion = motion.WithSpeed(GroundVehicleMotion.SpeedAfterObstruction(motion.Speed, Body.linearVelocity));

            // No surface, because there is nothing under an aircraft that it is standing on.
            // Whatever the map says is down there is somebody else's problem, which is the
            // design document's "ignores ground terrain" costing exactly one null.
            motion = GroundVehicleMotion.Step(motion, CurrentInput, Tuning, null, deltaTime);
            flightState = HelicopterMotion.Step(flightState, powered, flight, deltaTime);

            Body.MoveRotation(motion.Rotation);

            Vector3 planned = motion.Velocity;
            Body.linearVelocity = new Vector3(planned.x, ClimbVelocity(deltaTime), planned.z);
        }

        private void Update()
        {
            if (attitudePivot == null)
            {
                return;
            }

            Vector2 target = HelicopterMotion.Attitude(motion.Speed, CurrentInput.Steer, Tuning, flight);
            attitude = Vector2.SmoothDamp(
                attitude, target, ref attitudeVelocity, flight.AttitudeDamping, Mathf.Infinity, Time.deltaTime);
            attitudePivot.localRotation = Quaternion.Euler(attitude.x, 0.0f, attitude.y);
        }

        /// <summary>
        /// Returns the vertical velocity that lands the body on the modelled altitude.
        /// </summary>
        /// <param name="deltaTime">Length of the fixed step, in seconds.</param>
        /// <returns>A vertical velocity, clamped to a recoverable rate.</returns>
        /// <remarks>
        /// Tracking the modelled altitude rather than integrating vertical speed keeps the
        /// two from drifting apart when something pushes the body. The clamp is what makes
        /// that recovery look like flying rather than teleporting.
        /// </remarks>
        private float ClimbVelocity(float deltaTime)
        {
            float error = flightState.Altitude - Body.position.y;
            float limit = Mathf.Max(1.0f, flight.ClimbRate * 2.0f);
            return Mathf.Clamp(error / deltaTime, -limit, limit);
        }
    }
}
