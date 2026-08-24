using System;
using NUnit.Framework;
using UnityEngine;
using IronFlag.Levels;
using IronFlag.Vehicles;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// Covers the driving model: throttle, braking, how much of the turn rate a vehicle
    /// earns at a given speed, and what the ground under it does to all three.
    /// </summary>
    /// <remarks>
    /// These are the rules that make the four vehicles feel different, so they are worth
    /// pinning down away from a scene. Everything here runs on the pure model - no
    /// rigidbody, no fixed timestep, no play mode, and no map: a surface is a row of a
    /// table, so a beach in here is one argument rather than a level file.
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

            GroundMotionState state = GroundVehicleMotion.Step(moving, VehicleInput.Idle, jeep, Nothing, Step);

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

            GroundMotionState state = GroundVehicleMotion.Step(reversing, SteerRight, jeep, Nothing, Step);

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

            GroundMotionState state = GroundVehicleMotion.Step(moving, Forward, jeep, Nothing, 0.0f);

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
                () => GroundVehicleMotion.Step(GroundMotionState.Still, Forward, null, Nothing, Step));
            Assert.Throws<ArgumentNullException>(
                () => GroundVehicleMotion.Traction(null, SurfaceTuning.For(SurfaceKind.Sand)));
        }

        /// <summary>
        /// Nothing underfoot is not slippery ground: a model driven with no surface at all
        /// behaves exactly as the vehicle's own numbers say, which is what every rig that
        /// has no map under it depends on.
        /// </summary>
        [Test]
        public void StandingOnNothingDrivesExactlyAsTheTuningSays()
        {
            VehicleTuning jeep = VehicleTuning.For(VehicleKind.Jeep);

            GroundMotionState state = Drive(GroundMotionState.Still, Forward, jeep, Nothing, 5.0f);

            Assert.That(GroundVehicleMotion.Traction(jeep, Nothing), Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(state.Speed, Is.EqualTo(jeep.MaxSpeed).Within(0.001f));
        }

        /// <summary>
        /// The wheeled vehicle gets everything the ground is offering and the tracked ones
        /// get a fraction of it, which is the whole of the surface-sensitivity column.
        /// </summary>
        [Test]
        public void TheJeepIsAtTheMercyOfTheGroundAndTheTrackedVehiclesAreNot()
        {
            SurfaceTuning sand = SurfaceTuning.For(SurfaceKind.Sand);

            float jeep = GroundVehicleMotion.Traction(VehicleTuning.For(VehicleKind.Jeep), sand);
            float asv = GroundVehicleMotion.Traction(VehicleTuning.For(VehicleKind.Asv), sand);
            float tank = GroundVehicleMotion.Traction(VehicleTuning.For(VehicleKind.Tank), sand);
            float air = GroundVehicleMotion.Traction(VehicleTuning.For(VehicleKind.Helicopter), sand);

            Assert.That(jeep, Is.EqualTo(sand.Grip).Within(0.0001f), "the jeep is the anchor at 1.0");
            Assert.That(jeep, Is.LessThan(asv));
            Assert.That(asv, Is.LessThan(tank));
            Assert.That(tank, Is.LessThan(1.0f), "the tank feels nothing at all");
            Assert.That(air, Is.EqualTo(1.0f).Within(0.0001f), "the ground reached an aircraft");
        }

        /// <summary>
        /// The numbers the phase is actually for: a fifth off the jeep on sand, a twentieth
        /// off the tank, and a road that is the fastest ground on the map.
        /// </summary>
        [Test]
        public void SandCostsTheJeepAFifthAndTheTankATwentieth()
        {
            float jeepOnSand = TopSpeed(VehicleKind.Jeep, SurfaceKind.Sand);
            float jeepOnGrass = TopSpeed(VehicleKind.Jeep, SurfaceKind.Grass);
            float tankOnSand = TopSpeed(VehicleKind.Tank, SurfaceKind.Sand);
            float tankOnGrass = TopSpeed(VehicleKind.Tank, SurfaceKind.Grass);

            Assert.That(jeepOnSand / jeepOnGrass, Is.EqualTo(0.80f).Within(0.01f));
            Assert.That(tankOnSand / tankOnGrass, Is.EqualTo(0.95f).Within(0.01f));
        }

        /// <summary>
        /// The fastest line across the map is a line somebody drew, which is the whole
        /// reason a road has a grip figure above one.
        /// </summary>
        [Test]
        public void TheRoadIsTheFastestGroundThereIs()
        {
            float road = TopSpeed(VehicleKind.Jeep, SurfaceKind.Asphalt);
            float country = TopSpeed(VehicleKind.Jeep, SurfaceKind.Grass);
            float beach = TopSpeed(VehicleKind.Jeep, SurfaceKind.Sand);

            Assert.That(road, Is.GreaterThan(country));
            Assert.That(country, Is.GreaterThan(beach));
        }

        /// <summary>
        /// Soft ground costs the climb to speed as well as the speed, so a beach is slow to
        /// leave as well as slow to cross.
        /// </summary>
        [Test]
        public void SoftGroundSlowsTheClimbToSpeedAsWellAsTheSpeed()
        {
            VehicleTuning jeep = VehicleTuning.For(VehicleKind.Jeep);
            SurfaceTuning sand = SurfaceTuning.For(SurfaceKind.Sand);

            // Short enough that neither has reached its ceiling, so this is the acceleration
            // being compared rather than the two top speeds again.
            float onSand = Drive(GroundMotionState.Still, Forward, jeep, sand, 0.2f).Speed;
            float onGrass = Drive(GroundMotionState.Still, Forward, jeep, Grass, 0.2f).Speed;

            Assert.That(onSand, Is.LessThan(jeep.MaxSpeed * sand.Grip), "it was already at the ceiling");
            Assert.That(onSand, Is.LessThan(onGrass * 0.99f));
        }

        /// <summary>
        /// The ground never touches the brakes. Slippery going that also took the stopping
        /// distance away would make a beach a death trap rather than a slow lane.
        /// </summary>
        [Test]
        public void TheGroundNeverTouchesTheBrakes()
        {
            VehicleTuning jeep = VehicleTuning.For(VehicleKind.Jeep);
            var moving = new GroundMotionState(10.0f, 0.0f);

            float onSand = GroundVehicleMotion
                .Step(moving, VehicleInput.Idle, jeep, SurfaceTuning.For(SurfaceKind.Sand), Step).Speed;
            float onGrass = GroundVehicleMotion.Step(moving, VehicleInput.Idle, jeep, Grass, Step).Speed;

            Assert.That(onSand, Is.EqualTo(onGrass).Within(0.0001f));
            Assert.That(onSand, Is.EqualTo(10.0f - (jeep.Braking * Step)).Within(0.0001f));
        }

        /// <summary>
        /// Driving off a road onto sand needs no special case: the target drops, which is
        /// already the definition of slowing down.
        /// </summary>
        [Test]
        public void LeavingARoadForSandSettlesAtWhatTheSandAllows()
        {
            VehicleTuning jeep = VehicleTuning.For(VehicleKind.Jeep);
            SurfaceTuning sand = SurfaceTuning.For(SurfaceKind.Sand);
            var flatOut = new GroundMotionState(TopSpeed(VehicleKind.Jeep, SurfaceKind.Asphalt), 0.0f);

            GroundMotionState state = Drive(flatOut, Forward, jeep, sand, 5.0f);

            Assert.That(state.Speed, Is.LessThan(flatOut.Speed));
            Assert.That(state.Speed, Is.EqualTo(jeep.MaxSpeed * sand.Grip).Within(0.001f));
        }

        /// <summary>
        /// Grip is grip: a vehicle that cannot put its power down cannot put its steering
        /// down either.
        /// </summary>
        [Test]
        public void TheGroundScalesTheTurnRateAsWell()
        {
            VehicleTuning tank = VehicleTuning.For(VehicleKind.Tank);
            SurfaceTuning sand = SurfaceTuning.For(SurfaceKind.Sand);

            float onSand = GroundVehicleMotion.StepYaw(0.0f, 0.0f, 1.0f, tank, sand, 1.0f);
            float onGrass = GroundVehicleMotion.StepYaw(0.0f, 0.0f, 1.0f, tank, Grass, 1.0f);

            Assert.That(onGrass, Is.EqualTo(tank.TurnRate).Within(0.01f));
            Assert.That(
                onSand,
                Is.EqualTo(tank.TurnRate * GroundVehicleMotion.Traction(tank, sand)).Within(0.01f));
            Assert.That(onSand, Is.LessThan(onGrass));
        }

        /// <summary>Ordinary open country is the one row nothing is measured against.</summary>
        private static SurfaceTuning Grass => SurfaceTuning.For(SurfaceKind.Grass);

        /// <summary>Standing on nothing: no map, no field, no surface.</summary>
        private static SurfaceTuning Nothing => null;

        /// <summary>
        /// Drives one vehicle flat out over one surface until it can go no faster.
        /// </summary>
        /// <param name="kind">Vehicle to drive.</param>
        /// <param name="ground">Surface to drive it over.</param>
        /// <returns>The speed it settles at.</returns>
        private static float TopSpeed(VehicleKind kind, SurfaceKind ground)
            => Drive(
                GroundMotionState.Still,
                Forward,
                VehicleTuning.For(kind),
                SurfaceTuning.For(ground),
                10.0f).Speed;

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
            => Drive(state, input, tuning, Nothing, seconds);

        /// <summary>
        /// Runs the model for a while over one surface, the way the fixed update would.
        /// </summary>
        /// <param name="state">Starting state.</param>
        /// <param name="input">Intent to hold for the whole run.</param>
        /// <param name="tuning">Handling numbers to drive with.</param>
        /// <param name="surface">Ground to drive over, or null for nothing in particular.</param>
        /// <param name="seconds">How long to drive for.</param>
        /// <returns>The state at the end.</returns>
        private static GroundMotionState Drive(
            GroundMotionState state,
            VehicleInput input,
            VehicleTuning tuning,
            SurfaceTuning surface,
            float seconds)
        {
            int steps = Mathf.RoundToInt(seconds / Step);
            for (int index = 0; index < steps; index++)
            {
                state = GroundVehicleMotion.Step(state, input, tuning, surface, Step);
            }

            return state;
        }
    }
}
