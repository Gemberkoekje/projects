using System;
using NUnit.Framework;
using UnityEngine;
using IronFlag.Vehicles;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// Covers the driving model: throttle, braking, and how much of the turn rate a
    /// vehicle earns at a given speed.
    /// </summary>
    /// <remarks>
    /// These are the rules that make the four vehicles feel different, so they are worth
    /// pinning down away from a scene. Everything here runs on the pure model - no
    /// rigidbody, no fixed timestep, no play mode.
    /// </remarks>
    public sealed class GroundVehicleMotionTests
    {
        private const float Step = 0.02f;

        [Test]
        public void FullThrottleReachesTopSpeedAndStopsThere()
        {
            VehicleTuning jeep = VehicleTuning.For(VehicleKind.Jeep);
            GroundMotionState state = Drive(GroundMotionState.Still, Forward, jeep, 5.0f);

            Assert.That(state.Speed, Is.EqualTo(jeep.MaxSpeed).Within(0.001f));
        }

        [Test]
        public void ReverseIsCappedBelowForwardSpeed()
        {
            VehicleTuning jeep = VehicleTuning.For(VehicleKind.Jeep);
            GroundMotionState state = Drive(GroundMotionState.Still, Backward, jeep, 5.0f);

            Assert.That(state.Speed, Is.EqualTo(-jeep.ReverseSpeed).Within(0.001f));
            Assert.That(jeep.ReverseSpeed, Is.LessThan(jeep.MaxSpeed));
        }

        [Test]
        public void ReleasingTheThrottleBrakesRatherThanCoasts()
        {
            VehicleTuning jeep = VehicleTuning.For(VehicleKind.Jeep);
            var moving = new GroundMotionState(jeep.MaxSpeed, 0.0f);

            GroundMotionState state = GroundVehicleMotion.Step(moving, VehicleInput.Idle, jeep, Step);

            Assert.That(state.Speed, Is.EqualTo(jeep.MaxSpeed - (jeep.Braking * Step)).Within(0.001f));
        }

        [Test]
        public void CoastingComesToAFullStop()
        {
            VehicleTuning tank = VehicleTuning.For(VehicleKind.Tank);
            var moving = new GroundMotionState(tank.MaxSpeed, 0.0f);

            GroundMotionState state = Drive(moving, VehicleInput.Idle, tank, 5.0f);

            Assert.That(state.Speed, Is.EqualTo(0.0f).Within(0.001f));
        }

        [Test]
        public void AStationaryWheeledVehicleCannotSteer()
        {
            VehicleTuning jeep = VehicleTuning.For(VehicleKind.Jeep);

            GroundMotionState state = Drive(GroundMotionState.Still, SteerRight, jeep, 1.0f);

            Assert.That(state.YawDegrees, Is.EqualTo(0.0f).Within(0.001f));
        }

        [Test]
        public void ATrackedVehicleTurnsOnTheSpot()
        {
            VehicleTuning tank = VehicleTuning.For(VehicleKind.Tank);

            GroundMotionState state = Drive(GroundMotionState.Still, SteerRight, tank, 1.0f);

            Assert.That(state.YawDegrees, Is.EqualTo(tank.TurnRate).Within(0.5f));
            Assert.That(state.Speed, Is.EqualTo(0.0f).Within(0.001f));
        }

        [Test]
        public void AWheeledVehicleSteersBetterTheFasterItGoes()
        {
            VehicleTuning jeep = VehicleTuning.For(VehicleKind.Jeep);

            float crawling = GroundVehicleMotion.SteeringAuthority(1.0f, jeep);
            float rolling = GroundVehicleMotion.SteeringAuthority(jeep.SteerReferenceSpeed, jeep);
            float flatOut = GroundVehicleMotion.SteeringAuthority(jeep.MaxSpeed, jeep);

            Assert.That(crawling, Is.GreaterThan(0.0f).And.LessThan(1.0f));
            Assert.That(rolling, Is.EqualTo(1.0f).Within(0.001f));
            Assert.That(flatOut, Is.EqualTo(1.0f).Within(0.001f), "authority is capped, not scaled");
        }

        [Test]
        public void SteeringReversesWhenBackingUp()
        {
            VehicleTuning jeep = VehicleTuning.For(VehicleKind.Jeep);
            var reversing = new GroundMotionState(-jeep.ReverseSpeed, 0.0f);

            GroundMotionState state = GroundVehicleMotion.Step(reversing, SteerRight, jeep, Step);

            Assert.That(Mathf.DeltaAngle(0.0f, state.YawDegrees), Is.LessThan(0.0f));
        }

        [Test]
        public void HeadingWrapsRatherThanGrowing()
        {
            VehicleTuning tank = VehicleTuning.For(VehicleKind.Tank);
            var almostNorth = new GroundMotionState(0.0f, 359.0f);

            GroundMotionState state = Drive(almostNorth, SteerRight, tank, 1.0f);

            Assert.That(state.YawDegrees, Is.InRange(0.0f, 360.0f));
            Assert.That(Mathf.DeltaAngle(359.0f, state.YawDegrees), Is.EqualTo(tank.TurnRate).Within(0.5f));
        }

        [Test]
        public void AZeroLengthStepChangesNothing()
        {
            VehicleTuning jeep = VehicleTuning.For(VehicleKind.Jeep);
            var moving = new GroundMotionState(6.0f, 90.0f);

            GroundMotionState state = GroundVehicleMotion.Step(moving, Forward, jeep, 0.0f);

            Assert.That(state, Is.EqualTo(moving));
        }

        [Test]
        public void VelocityPointsAlongTheHeading()
        {
            var eastbound = new GroundMotionState(10.0f, 90.0f);

            Assert.That(eastbound.Velocity.x, Is.EqualTo(10.0f).Within(0.001f));
            Assert.That(eastbound.Velocity.z, Is.EqualTo(0.0f).Within(0.001f));
            Assert.That(eastbound.Velocity.y, Is.EqualTo(0.0f).Within(0.001f));
        }

        [Test]
        public void SteppingWithoutTuningIsAProgrammerError()
        {
            Assert.Throws<ArgumentNullException>(
                () => GroundVehicleMotion.Step(GroundMotionState.Still, Forward, null, Step));
        }

        private static VehicleInput Forward => new VehicleInput(Vector2.up, Vector2.zero, 0.0f);

        private static VehicleInput Backward => new VehicleInput(Vector2.down, Vector2.zero, 0.0f);

        private static VehicleInput SteerRight => new VehicleInput(Vector2.right, Vector2.zero, 0.0f);

        /// <summary>
        /// Runs the model for a while at a fixed step, the way the fixed update would.
        /// </summary>
        /// <param name="state">Starting state.</param>
        /// <param name="input">Intent to hold for the whole run.</param>
        /// <param name="tuning">Handling numbers to drive with.</param>
        /// <param name="seconds">How long to drive for.</param>
        /// <returns>The state at the end.</returns>
        private static GroundMotionState Drive(
            GroundMotionState state, VehicleInput input, VehicleTuning tuning, float seconds)
        {
            int steps = Mathf.RoundToInt(seconds / Step);
            for (int index = 0; index < steps; index++)
            {
                state = GroundVehicleMotion.Step(state, input, tuning, Step);
            }

            return state;
        }
    }
}
