using NUnit.Framework;
using UnityEngine;
using IronFlag.Vehicles;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// Covers the helicopter's vertical axis: the one altitude it holds, how it gets back
    /// there, and what running dry does to it.
    /// </summary>
    /// <remarks>
    /// There is nothing here about a collective any more. The pilot has no vertical input,
    /// so every test in this file drives the model with the only thing that still decides
    /// altitude - whether the engine is running.
    /// </remarks>
    public sealed class HelicopterMotionTests
    {
        private const float Step = 0.02f;

        [Test]
        public void APoweredHelicopterClimbsToItsCruisingAltitudeAndStopsThere()
        {
            var tuning = new FlightTuning();
            FlightState state = Fly(FlightState.Landed, powered: true, tuning, 5.0f);

            Assert.That(state.Altitude, Is.EqualTo(tuning.CruiseAltitude).Within(0.001f));
            Assert.That(
                state.VerticalSpeed,
                Is.EqualTo(0.0f).Within(0.001f),
                "it arrived still climbing, which fires the moment anything nudges it");
        }

        /// <summary>
        /// The altitude is held rather than merely reached: once it is there, more time
        /// changes nothing at all.
        /// </summary>
        [Test]
        public void HoldingIsNotDrifting()
        {
            var tuning = new FlightTuning();
            FlightState arrived = Fly(FlightState.Landed, powered: true, tuning, 5.0f);
            FlightState later = Fly(arrived, powered: true, tuning, 10.0f);

            Assert.That(later, Is.EqualTo(arrived));
        }

        /// <summary>
        /// Pushed off its altitude from either side, it comes back to exactly the same
        /// number - which is what makes a collision a shove rather than a relocation.
        /// </summary>
        [Test]
        public void ItReturnsToTheSameAltitudeFromAboveOrBelow()
        {
            var tuning = new FlightTuning();

            FlightState fromBelow = Fly(
                FlightState.Hovering(tuning.CruiseAltitude - 6.0f), powered: true, tuning, 5.0f);
            FlightState fromAbove = Fly(
                FlightState.Hovering(tuning.CruiseAltitude + 6.0f), powered: true, tuning, 5.0f);

            Assert.That(fromBelow.Altitude, Is.EqualTo(tuning.CruiseAltitude).Within(0.001f));
            Assert.That(fromAbove.Altitude, Is.EqualTo(tuning.CruiseAltitude).Within(0.001f));
            Assert.That(fromAbove.VerticalSpeed, Is.EqualTo(0.0f).Within(0.001f));
        }

        /// <summary>
        /// It eases onto the altitude rather than sailing through it and coming back. An
        /// aircraft that overshoots its own cruising height bounces on every deploy.
        /// </summary>
        [Test]
        public void ItNeverClimbsPastTheAltitudeItIsHolding()
        {
            var tuning = new FlightTuning();
            FlightState state = FlightState.Landed;

            for (int index = 0; index < 500; index++)
            {
                state = HelicopterMotion.Step(state, powered: true, tuning, Step);
                Assert.That(
                    state.Altitude,
                    Is.LessThanOrEqualTo(tuning.CruiseAltitude + 0.0001f),
                    $"it overshot on step {index}");
            }
        }

        /// <summary>
        /// The other direction of the same rule: settling from above never dips past the
        /// altitude either. <see cref="ItNeverClimbsPastTheAltitudeItIsHolding"/> only ever
        /// starts below the target, which exercises one branch of the clamp in
        /// <see cref="HelicopterMotion.Step"/> and not the other - this is the twin that
        /// starts above it and checks every step of the descent, not just where it ends up.
        /// </summary>
        [Test]
        public void ItNeverSinksPastTheAltitudeItIsHoldingWhenComingDownFromAbove()
        {
            var tuning = new FlightTuning();
            FlightState state = FlightState.Hovering(tuning.CruiseAltitude + 40.0f);

            for (int index = 0; index < 500; index++)
            {
                state = HelicopterMotion.Step(state, powered: true, tuning, Step);
                Assert.That(
                    state.Altitude,
                    Is.GreaterThanOrEqualTo(tuning.CruiseAltitude - 0.0001f),
                    $"it undershot on step {index}");
            }
        }

        /// <summary>
        /// A dead engine is the only thing that moves a helicopter off its altitude, and it
        /// puts the aircraft on the ground rather than merely lower down.
        /// </summary>
        [Test]
        public void AnUnpoweredHelicopterSettlesOntoTheGround()
        {
            var tuning = new FlightTuning();
            FlightState flying = Fly(FlightState.Landed, powered: true, tuning, 5.0f);

            FlightState settled = Fly(flying, powered: false, tuning, 5.0f);

            Assert.That(settled.Altitude, Is.EqualTo(tuning.GroundedAltitude).Within(0.001f));
            Assert.That(settled.VerticalSpeed, Is.EqualTo(0.0f).Within(0.001f));
        }

        /// <summary>
        /// Refuelling puts it back in the air, so the two states are a switch rather than a
        /// one-way trip: a helicopter dragged home on fumes takes off again.
        /// </summary>
        [Test]
        public void PowerComingBackPutsItBackUp()
        {
            var tuning = new FlightTuning();
            FlightState grounded = Fly(FlightState.Landed, powered: false, tuning, 3.0f);

            FlightState flying = Fly(grounded, powered: true, tuning, 5.0f);

            Assert.That(flying.Altitude, Is.EqualTo(tuning.CruiseAltitude).Within(0.001f));
        }

        [Test]
        public void ClimbingIsRampedRatherThanInstant()
        {
            var tuning = new FlightTuning();
            FlightState afterOneStep = HelicopterMotion.Step(
                FlightState.Landed, powered: true, tuning, Step);

            Assert.That(afterOneStep.VerticalSpeed, Is.LessThan(tuning.ClimbRate));
            Assert.That(
                afterOneStep.VerticalSpeed,
                Is.EqualTo(tuning.ClimbAcceleration * Step).Within(0.001f));
        }

        /// <summary>
        /// The climb is capped at the rated rate however far away the altitude is, so an
        /// aircraft put down on the far side of the map does not teleport up to it.
        /// </summary>
        [Test]
        public void TheClimbNeverExceedsTheRatedRate()
        {
            var tuning = new FlightTuning();
            FlightState state = FlightState.Hovering(-200.0f);

            for (int index = 0; index < 100; index++)
            {
                state = HelicopterMotion.Step(state, powered: true, tuning, Step);
                Assert.That(
                    state.VerticalSpeed,
                    Is.LessThanOrEqualTo(tuning.ClimbRate + 0.0001f),
                    $"it climbed faster than the rotor can on step {index}");
            }
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

            Assert.That(
                HelicopterMotion.Step(hovering, powered: true, tuning, 0.0f), Is.EqualTo(hovering));
        }

        /// <summary>
        /// The cruising altitude has to clear the tallest thing on the map and stay well
        /// under the combat ceiling: below the first and a helicopter flies through a tower,
        /// above the second and it climbs out of the fight.
        /// </summary>
        [Test]
        public void TheCruisingAltitudeIsAboveTheMapAndUnderTheCeiling()
        {
            var tuning = new FlightTuning();

            Assert.That(
                tuning.CruiseAltitude,
                Is.GreaterThan(7.0f),
                "the flag tower is six and a half metres tall");
            Assert.That(
                tuning.CruiseAltitude,
                Is.LessThan(IronFlag.Combat.CombatPlane.Ceiling - 3.0f),
                "a helicopter at this altitude is outside the column shot at it");
            Assert.That(
                tuning.GroundedAltitude,
                Is.LessThan(tuning.CruiseAltitude),
                "running dry would make a helicopter climb");
        }

        /// <summary>
        /// Runs the vertical model for a while at a fixed step.
        /// </summary>
        /// <param name="state">Starting flight state.</param>
        /// <param name="powered">Whether the engine runs for the whole run.</param>
        /// <param name="tuning">Flight numbers to fly with.</param>
        /// <param name="seconds">How long to fly for.</param>
        /// <returns>The flight state at the end.</returns>
        private static FlightState Fly(
            FlightState state, bool powered, FlightTuning tuning, float seconds)
        {
            int steps = Mathf.RoundToInt(seconds / Step);
            for (int index = 0; index < steps; index++)
            {
                state = HelicopterMotion.Step(state, powered, tuning, Step);
            }

            return state;
        }
    }
}
