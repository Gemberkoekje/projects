using NUnit.Framework;
using UnityEngine;
using IronFlag.Vehicles;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// Covers the helicopter's vertical axis: climbing, holding altitude, and the limits at
    /// either end of it.
    /// </summary>
    public sealed class HelicopterMotionTests
    {
        private const float Step = 0.02f;

        [Test]
        public void FullCollectiveClimbsAtTheRatedRate()
        {
            var tuning = new FlightTuning();
            FlightState state = Fly(FlightState.Hovering(5.0f), 1.0f, tuning, 1.0f);

            Assert.That(state.VerticalSpeed, Is.EqualTo(tuning.ClimbRate).Within(0.01f));
            Assert.That(state.Altitude, Is.GreaterThan(5.0f));
        }

        [Test]
        public void ReleasingTheCollectiveHoldsAltitude()
        {
            var tuning = new FlightTuning();
            FlightState climbing = Fly(FlightState.Hovering(5.0f), 1.0f, tuning, 1.0f);

            FlightState held = Fly(climbing, 0.0f, tuning, 3.0f);
            FlightState later = Fly(held, 0.0f, tuning, 3.0f);

            Assert.That(held.VerticalSpeed, Is.EqualTo(0.0f).Within(0.001f));
            Assert.That(later.Altitude, Is.EqualTo(held.Altitude).Within(0.001f));
        }

        [Test]
        public void TheCeilingStopsTheClimbAndTheClimbRate()
        {
            var tuning = new FlightTuning();
            FlightState state = Fly(FlightState.Hovering(tuning.MaxAltitude - 1.0f), 1.0f, tuning, 5.0f);

            Assert.That(state.Altitude, Is.EqualTo(tuning.MaxAltitude).Within(0.001f));
            Assert.That(state.VerticalSpeed, Is.EqualTo(0.0f).Within(0.001f));
        }

        [Test]
        public void DescendingStopsAtTheFloor()
        {
            var tuning = new FlightTuning();
            FlightState state = Fly(FlightState.Hovering(6.0f), -1.0f, tuning, 5.0f);

            Assert.That(state.Altitude, Is.EqualTo(tuning.MinAltitude).Within(0.001f));
            Assert.That(state.VerticalSpeed, Is.EqualTo(0.0f).Within(0.001f));
        }

        [Test]
        public void ClimbingIsRampedRatherThanInstant()
        {
            var tuning = new FlightTuning();
            FlightState afterOneStep = HelicopterMotion.Step(FlightState.Landed, 1.0f, tuning, Step);

            Assert.That(afterOneStep.VerticalSpeed, Is.LessThan(tuning.ClimbRate));
            Assert.That(afterOneStep.VerticalSpeed,
                Is.EqualTo(tuning.ClimbAcceleration * Step).Within(0.001f));
        }

        [Test]
        public void FlyingForwardsPitchesTheNoseDown()
        {
            VehicleTuning tuning = VehicleTuning.For(VehicleKind.Helicopter);
            var flight = new FlightTuning();

            Vector2 attitude = HelicopterMotion.Attitude(tuning.MaxSpeed, 0.0f, tuning, flight);

            Assert.That(attitude.x, Is.EqualTo(flight.MaxPitch).Within(0.001f));
            Assert.That(attitude.y, Is.EqualTo(0.0f).Within(0.001f));
        }

        [Test]
        public void TurningRightBanksRight()
        {
            VehicleTuning tuning = VehicleTuning.For(VehicleKind.Helicopter);
            var flight = new FlightTuning();

            Vector2 attitude = HelicopterMotion.Attitude(0.0f, 1.0f, tuning, flight);

            // Roll is a Z euler, and a positive Z rotation lifts the right side, so
            // banking right is the negative one.
            Assert.That(attitude.y, Is.EqualTo(-flight.MaxRoll).Within(0.001f));
        }

        [Test]
        public void AZeroLengthStepChangesNothing()
        {
            var tuning = new FlightTuning();
            FlightState hovering = FlightState.Hovering(9.0f);

            Assert.That(HelicopterMotion.Step(hovering, 1.0f, tuning, 0.0f), Is.EqualTo(hovering));
        }

        /// <summary>
        /// Runs the vertical model for a while at a fixed step.
        /// </summary>
        /// <param name="state">Starting flight state.</param>
        /// <param name="lift">Collective to hold for the whole run.</param>
        /// <param name="tuning">Flight numbers to fly with.</param>
        /// <param name="seconds">How long to fly for.</param>
        /// <returns>The flight state at the end.</returns>
        private static FlightState Fly(FlightState state, float lift, FlightTuning tuning, float seconds)
        {
            int steps = Mathf.RoundToInt(seconds / Step);
            for (int index = 0; index < steps; index++)
            {
                state = HelicopterMotion.Step(state, lift, tuning, Step);
            }

            return state;
        }
    }
}
