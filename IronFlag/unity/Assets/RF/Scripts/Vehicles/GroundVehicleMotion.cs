using System;
using UnityEngine;
using IronFlag.Levels;

namespace IronFlag.Vehicles
{
    /// <summary>
    /// The driving model shared by every vehicle that steers, ground or air.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is deliberately free of <see cref="MonoBehaviour"/> and of the physics engine:
    /// it takes a state and one frame of intent and returns the next state, so the feel of
    /// each vehicle can be tested without a scene, a rigidbody or a play-mode harness. The
    /// components in this folder are thin adapters that hand the result to a
    /// <see cref="Rigidbody"/>.
    /// </para>
    /// <para>
    /// The helicopter uses this too, for its horizontal motion; only the vertical axis is
    /// its own, in <see cref="HelicopterMotion"/>.
    /// </para>
    /// <para>
    /// What a vehicle is standing on is handed in rather than looked up, for the same
    /// reason: a <see cref="SurfaceTuning"/> is a row of a table, so the model can be driven
    /// across a beach in an edit-mode test with no map, no field and no scene. Sampling the
    /// ground is <see cref="GroundVehicle"/>'s job, and the helicopter simply never does it.
    /// </para>
    /// </remarks>
    public static class GroundVehicleMotion
    {
        /// <summary>
        /// Advances one vehicle by a time step.
        /// </summary>
        /// <param name="state">Current heading and speed.</param>
        /// <param name="input">This frame's pilot intent; only <see cref="VehicleInput.Drive"/> is read.</param>
        /// <param name="tuning">Handling numbers for this vehicle.</param>
        /// <param name="surface">
        /// The ground it is standing on, or <c>null</c> for nothing in particular - which is
        /// what an aircraft is over and what a test rig with no map is on.
        /// </param>
        /// <param name="deltaTime">Length of the step in seconds. Zero or less is a no-op.</param>
        /// <returns>The state at the end of the step.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="tuning"/> is null.</exception>
        /// <example>
        /// <code>
        /// GroundMotionState state = GroundMotionState.Still;
        /// var forward = new VehicleInput(Vector2.up, Vector2.zero, 0.0f);
        /// state = GroundVehicleMotion.Step(
        ///     state, forward, VehicleTuning.For(VehicleKind.Jeep),
        ///     SurfaceTuning.For(SurfaceKind.Sand), 0.02f);
        /// </code>
        /// </example>
        public static GroundMotionState Step(
            GroundMotionState state,
            VehicleInput input,
            VehicleTuning tuning,
            SurfaceTuning surface,
            float deltaTime)
        {
            if (tuning == null)
            {
                throw new ArgumentNullException(nameof(tuning));
            }

            if (deltaTime <= 0.0f)
            {
                return state;
            }

            float speed = StepSpeed(state.Speed, input.Throttle, tuning, surface, deltaTime);
            float yaw = StepYaw(state.YawDegrees, speed, input.Steer, tuning, surface, deltaTime);
            return new GroundMotionState(speed, yaw);
        }

        /// <summary>
        /// Returns how much of the ground's grip this vehicle is actually getting.
        /// </summary>
        /// <param name="tuning">Handling numbers for this vehicle.</param>
        /// <param name="surface">The ground it is standing on, or <c>null</c> for none.</param>
        /// <returns>
        /// What to multiply the vehicle's top speed, acceleration and turn rate by. One on
        /// ground that neither helps nor hinders, and one for anything standing on nothing.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="tuning"/> is null.</exception>
        /// <remarks>
        /// <para>
        /// A straight line from "this vehicle does not notice the ground" to "this vehicle
        /// gets exactly what the ground offers", walked by
        /// <see cref="VehicleTuning.SurfaceSensitivity"/>. So a jeep on sand loses the whole
        /// fifth the table charges and a tank on the same sand loses a twentieth, out of one
        /// column rather than a surface-by-vehicle matrix.
        /// </para>
        /// <para>
        /// Grip is the only thing weighed this way. Braking is deliberately not scaled at
        /// all - soft ground that also took the brakes away would make a beach a death trap
        /// rather than a slow lane - and a surface's thirst is weighed by nothing, per
        /// <see cref="VehicleTuning.SurfaceSensitivity"/>.
        /// </para>
        /// </remarks>
        public static float Traction(VehicleTuning tuning, SurfaceTuning surface)
        {
            if (tuning == null)
            {
                throw new ArgumentNullException(nameof(tuning));
            }

            if (surface == null)
            {
                return 1.0f;
            }

            return Mathf.Lerp(
                1.0f, Mathf.Max(0.0f, surface.Grip), Mathf.Clamp01(tuning.SurfaceSensitivity));
        }

        /// <summary>
        /// Applies the throttle for one step.
        /// </summary>
        /// <param name="speed">Current speed along the heading.</param>
        /// <param name="throttle">Throttle in -1..1; negative reverses.</param>
        /// <param name="tuning">Handling numbers for this vehicle.</param>
        /// <param name="surface">The ground it is standing on, or <c>null</c> for none.</param>
        /// <param name="deltaTime">Length of the step in seconds.</param>
        /// <returns>The speed at the end of the step.</returns>
        /// <remarks>
        /// <para>
        /// Coasting brakes rather than drifting: releasing the throttle uses
        /// <see cref="VehicleTuning.Braking"/>, because a vehicle that keeps rolling for
        /// ten seconds is unpleasant to place precisely at a depot or a flag tower.
        /// </para>
        /// <para>
        /// The ground scales what the throttle is asking for and how quickly it gets there,
        /// and never the brakes. Driving off a road onto sand therefore needs no special
        /// case: the target drops below the speed the vehicle already has, which is the
        /// definition of slowing down, so it sheds the difference at its own braking rate
        /// and settles at what the sand allows.
        /// </para>
        /// </remarks>
        public static float StepSpeed(
            float speed, float throttle, VehicleTuning tuning, SurfaceTuning surface, float deltaTime)
        {
            throttle = Mathf.Clamp(throttle, -1.0f, 1.0f);
            float traction = Traction(tuning, surface);
            float target = traction * (throttle >= 0.0f
                ? throttle * tuning.MaxSpeed
                : throttle * tuning.ReverseSpeed);

            bool slowing = Mathf.Abs(target) < Mathf.Abs(speed) || (target * speed) < 0.0f;
            float rate = slowing ? tuning.Braking : tuning.Acceleration * traction;
            return Mathf.MoveTowards(speed, target, rate * deltaTime);
        }

        /// <summary>
        /// Applies the steering for one step.
        /// </summary>
        /// <param name="yawDegrees">Current heading.</param>
        /// <param name="speed">Speed the steering acts at.</param>
        /// <param name="steer">Steering in -1..1; positive turns right.</param>
        /// <param name="tuning">Handling numbers for this vehicle.</param>
        /// <param name="surface">The ground it is standing on, or <c>null</c> for none.</param>
        /// <param name="deltaTime">Length of the step in seconds.</param>
        /// <returns>The heading at the end of the step, wrapped into 0..360.</returns>
        /// <remarks>
        /// A wheeled vehicle turns only as well as it is rolling, and its steering reverses
        /// when it backs up, exactly as a car's does. A tracked or airborne one turns at its
        /// full rate from a standstill. The ground scales the rate and not the authority:
        /// how much of its turn a vehicle has earned is a fact about how fast it is going,
        /// and soft going has already made it slower.
        /// </remarks>
        public static float StepYaw(
            float yawDegrees,
            float speed,
            float steer,
            VehicleTuning tuning,
            SurfaceTuning surface,
            float deltaTime)
        {
            steer = Mathf.Clamp(steer, -1.0f, 1.0f);
            float authority = SteeringAuthority(speed, tuning);
            float direction = tuning.PivotTurn ? 1.0f : Mathf.Sign(speed);
            float rate = tuning.TurnRate * Traction(tuning, surface);
            float delta = steer * direction * authority * rate * deltaTime;
            return Mathf.Repeat(yawDegrees + delta, 360.0f);
        }

        /// <summary>
        /// Returns how much of the turn rate is available at a given speed.
        /// </summary>
        /// <param name="speed">Speed along the heading; the sign is ignored.</param>
        /// <param name="tuning">Handling numbers for this vehicle.</param>
        /// <returns>A factor in 0..1 to scale <see cref="VehicleTuning.TurnRate"/> by.</returns>
        public static float SteeringAuthority(float speed, VehicleTuning tuning)
        {
            if (tuning.PivotTurn)
            {
                return 1.0f;
            }

            float reference = Mathf.Max(0.01f, tuning.SteerReferenceSpeed);
            return Mathf.Clamp01(Mathf.Abs(speed) / reference);
        }

        /// <summary>
        /// Reconciles the model's believed speed with the speed physics actually allowed.
        /// </summary>
        /// <param name="intended">Speed the movement model believes it has.</param>
        /// <param name="measuredVelocity">The rigidbody's actual velocity this step.</param>
        /// <returns>The intended speed, or the measured horizontal speed when something got in the way.</returns>
        /// <remarks>
        /// Without this, driving or flying into an obstacle leaves the model at full speed, so
        /// the vehicle bursts forward the moment the obstacle stops touching it. Only ever
        /// slowing down keeps the reconciliation from becoming a second, invisible source of
        /// thrust. The comparison is against horizontal speed rather than the forward
        /// component, because steering rotates the body between the write and the read and
        /// would otherwise read as a small permanent drag. Shared by every vehicle in this
        /// folder that writes its own horizontal velocity onto a non-kinematic rigidbody,
        /// ground or air.
        /// </remarks>
        public static float SpeedAfterObstruction(float intended, Vector3 measuredVelocity)
        {
            var horizontal = new Vector3(measuredVelocity.x, 0.0f, measuredVelocity.z);
            float measured = horizontal.magnitude * Mathf.Sign(intended);
            return Mathf.Abs(measured) < Mathf.Abs(intended) ? measured : intended;
        }
    }
}
