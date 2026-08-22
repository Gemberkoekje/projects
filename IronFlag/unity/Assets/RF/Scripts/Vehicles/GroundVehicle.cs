using UnityEngine;

namespace IronFlag.Vehicles
{
    /// <summary>
    /// The jeep, the tank and the ASV: everything that drives on the ground.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A thin adapter over <see cref="GroundVehicleMotion"/>. The movement model owns the
    /// heading and the speed; the rigidbody exists so the vehicle collides with the world
    /// and falls onto it, not so it is simulated by it. Horizontal velocity is written every
    /// fixed step and the vertical component is left to gravity.
    /// </para>
    /// <para>
    /// The three vehicles differ only by their <see cref="VehicleTuning"/>. Nothing here
    /// branches on <see cref="VehicleController.Kind"/>.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Ground Vehicle")]
    public sealed class GroundVehicle : VehicleController
    {
        private GroundMotionState motion = GroundMotionState.Still;

        /// <inheritdoc/>
        public override float ForwardSpeed => motion.Speed;

        /// <inheritdoc/>
        public override Vector3 Velocity => Body == null ? motion.Velocity : Body.linearVelocity;

        /// <summary>How much of the turn rate the current speed is earning, in 0..1.</summary>
        /// <remarks>
        /// Zero for a stationary jeep, which is why it will not pirouette on the spot, and
        /// always one for the tracked vehicles.
        /// </remarks>
        public float SteeringAuthority => GroundVehicleMotion.SteeringAuthority(motion.Speed, Tuning);

        /// <inheritdoc/>
        public override void Teleport(Vector3 position, float yawDegrees)
        {
            base.Teleport(position, yawDegrees);
            motion = new GroundMotionState(0.0f, yawDegrees);
        }

        /// <inheritdoc/>
        protected override void Awake()
        {
            base.Awake();
            motion = new GroundMotionState(0.0f, transform.eulerAngles.y);
        }

        /// <inheritdoc/>
        protected override void ConfigureBody()
        {
            base.ConfigureBody();
            Body.useGravity = true;
            // Yaw is driven by the movement model, and the other two axes are held level:
            // a top-down game reads far worse when vehicles tip, and a tipped vehicle with
            // a velocity written along its own forward axis climbs into the air.
            Body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        /// <summary>
        /// Steps the movement model and puts the result on the rigidbody.
        /// </summary>
        /// <remarks>
        /// A kinematic body sits the step out whole. Physics is not moving it, so there is no
        /// measured speed for <see cref="SpeedAfterObstruction"/> to reconcile against and
        /// nowhere for the planned velocity to go - and writing one anyway logs "Setting
        /// linear velocity of a kinematic body is not supported" with a stack trace, once per
        /// fixed step for as long as the vehicle stays frozen. Same reason
        /// <see cref="IronFlag.Core.VehicleBay"/> only clears the velocities on a body that is
        /// still simulated. Returning is the fix rather than disabling the controller, because
        /// <see cref="VehicleController.OnEnable"/> is what puts a vehicle on
        /// <see cref="VehicleController.OnTheField"/>: switching the component off to quiet the
        /// log would take the vehicle off the roll-call everything else looks for it on.
        /// </remarks>
        private void FixedUpdate()
        {
            if (Body.isKinematic)
            {
                return;
            }

            float deltaTime = Time.fixedDeltaTime;

            motion = motion.WithSpeed(GroundVehicleMotion.SpeedAfterObstruction(motion.Speed, Body.linearVelocity));
            motion = GroundVehicleMotion.Step(motion, CurrentInput, Tuning, deltaTime);

            Body.MoveRotation(motion.Rotation);

            Vector3 planned = motion.Velocity;
            Vector3 current = Body.linearVelocity;
            Body.linearVelocity = new Vector3(planned.x, current.y, planned.z);
        }
    }
}
