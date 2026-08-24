using UnityEngine;
using IronFlag.Levels;

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
    /// <para>
    /// This is also the one thing in the game that asks what it is standing on. The answer
    /// comes from <see cref="SurfaceField"/>, which is a grid lookup rather than a raycast,
    /// and it goes to the movement model and to <see cref="IronFlag.Supply.VehicleSupply"/>.
    /// <see cref="Helicopter"/> deliberately never asks, which is the design document's
    /// "ignores ground terrain" arriving for free rather than as a check on a vehicle type -
    /// the same way <see cref="WaterLine"/> never has to exclude it from drowning.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Ground Vehicle")]
    public sealed class GroundVehicle : VehicleController
    {
        private GroundMotionState motion = GroundMotionState.Still;
        private SurfaceKind standing = SurfaceKind.None;
        private SurfaceTuning underfoot = SurfaceTuning.For(SurfaceKind.None);

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

        /// <summary>What the vehicle is standing on, as at the last fixed step.</summary>
        /// <remarks>
        /// <see cref="SurfaceKind.None"/> when there is no map up at all, which is what a
        /// vehicle assembled in a test rig is standing on. Everything else the ground can be
        /// is a row of the table, including the two waters - a vehicle over water is a
        /// vehicle about to drown, and that is <see cref="WaterLine"/>'s business rather
        /// than this one's.
        /// </remarks>
        public SurfaceKind Standing => standing;

        /// <summary>What the ground under the vehicle does to it. Never null.</summary>
        /// <remarks>
        /// The row <see cref="Standing"/> indexes, looked up when the ground changes rather
        /// than fifty times a second: <see cref="SurfaceTuning.For"/> hands back a fresh copy
        /// on purpose, and a vehicle crosses a handful of boundaries a minute. With no map
        /// up this is the <see cref="SurfaceKind.None"/> row, which the table answers with
        /// grass - so a rig with no world under it drives exactly as the tuning says.
        /// </remarks>
        public SurfaceTuning Underfoot => underfoot;

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
            ReadTheGround();
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
            ReadTheGround();

            motion = motion.WithSpeed(GroundVehicleMotion.SpeedAfterObstruction(motion.Speed, Body.linearVelocity));
            motion = GroundVehicleMotion.Step(motion, CurrentInput, Tuning, underfoot, deltaTime);

            Body.MoveRotation(motion.Rotation);

            Vector3 planned = motion.Velocity;
            Vector3 current = Body.linearVelocity;
            Body.linearVelocity = new Vector3(planned.x, current.y, planned.z);
        }

        /// <summary>
        /// Looks up what is under the vehicle, and the row that says what it does.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="LevelLoader.Current"/> rather than a static of this pass's own: it
        /// already means "the map that is up", it already survives a scene change, and a
        /// second static holding the same answer is a second thing that can be wrong. The
        /// field behind it rebuilds itself when the land has moved, so an editor that drags
        /// a coastline is driven on straight away.
        /// </para>
        /// <para>
        /// It does mean a vehicle in a scene with no loader reads whatever map was up last.
        /// That is the same caveat <see cref="LevelLoader.Current"/> documents, and the worst
        /// it can do here is drive a test rig on somebody else's grass.
        /// </para>
        /// </remarks>
        private void ReadTheGround()
        {
            LevelDefinition level = LevelLoader.Current;
            SurfaceKind under = level == null ? SurfaceKind.None : level.Field.At(transform.position);
            if (under == standing)
            {
                return;
            }

            standing = under;
            underfoot = SurfaceTuning.For(under);
        }
    }
}
