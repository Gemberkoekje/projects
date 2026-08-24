using System;
using UnityEngine;

namespace IronFlag.Vehicles
{
    /// <summary>
    /// The vertical axis the helicopter adds on top of the shared driving model.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There is no collective. A helicopter flies at one altitude and holds it, and the
    /// only thing that moves it off that altitude is running out of fuel. What used to be
    /// a pilot's decision is now a fact about the aircraft, which is what makes the
    /// helicopter's difference from the other three <em>horizontal</em>: it ignores the
    /// ground and it cannot be blocked, and it no longer also gets to choose how visible
    /// or how safe it is by sitting at a different height.
    /// </para>
    /// <para>
    /// The climb is still modelled rather than assigned, because the aircraft has to cross
    /// the gap between the pad it deploys off and the altitude it holds, and it has to
    /// come back after something shoves it. A helicopter that snapped to its altitude
    /// would look like a teleport in exactly those two places.
    /// </para>
    /// <para>
    /// Like <see cref="GroundVehicleMotion"/> this is pure: state in, state out, no
    /// <see cref="MonoBehaviour"/> and no physics engine.
    /// </para>
    /// </remarks>
    public static class HelicopterMotion
    {
        /// <summary>
        /// Advances the vertical axis by a time step.
        /// </summary>
        /// <param name="state">Current altitude and rate of climb.</param>
        /// <param name="powered">Whether the engine is running; a dry one settles instead.</param>
        /// <param name="tuning">Flight numbers for this aircraft.</param>
        /// <param name="deltaTime">Length of the step in seconds. Zero or less is a no-op.</param>
        /// <returns>The flight state at the end of the step.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="tuning"/> is null.</exception>
        /// <example>
        /// <code>
        /// FlightState next = HelicopterMotion.Step(state, powered: true, tuning, Time.fixedDeltaTime);
        /// </code>
        /// </example>
        public static FlightState Step(FlightState state, bool powered, FlightTuning tuning, float deltaTime)
        {
            if (tuning == null)
            {
                throw new ArgumentNullException(nameof(tuning));
            }

            if (deltaTime <= 0.0f)
            {
                return state;
            }

            float held = tuning.AltitudeHeld(powered);
            float gap = held - state.Altitude;

            // The rate that would close the whole gap this step, capped at what the rotor
            // can do. Asking for the gap rather than for a flat climb rate is what eases
            // the aircraft onto its altitude instead of sailing through it and coming back.
            float wanted = Mathf.Clamp(gap / deltaTime, -tuning.ClimbRate, tuning.ClimbRate);
            float vertical = Mathf.MoveTowards(
                state.VerticalSpeed, wanted, tuning.ClimbAcceleration * deltaTime);

            float altitude = state.Altitude + (vertical * deltaTime);

            // Never past the altitude it is holding. The acceleration limit can leave the
            // aircraft unable to slow down in time, and one that sails through its own
            // altitude and comes back is a bounce nobody asked for.
            altitude = gap >= 0.0f ? Mathf.Min(altitude, held) : Mathf.Max(altitude, held);

            // Arrived, so it is holding rather than still climbing: a stored rate here is
            // one that fires the moment anything nudges the aircraft off its altitude.
            if (Mathf.Approximately(altitude, held))
            {
                altitude = held;
                vertical = 0.0f;
            }

            return new FlightState(altitude, vertical);
        }

        /// <summary>
        /// Returns the cosmetic tilt for a given set of controls.
        /// </summary>
        /// <param name="speed">Current forward speed in m/s.</param>
        /// <param name="steer">Steering in -1..1; positive turns right.</param>
        /// <param name="tuning">Handling numbers, for the top speed the pitch scales against.</param>
        /// <param name="flight">Flight numbers carrying the tilt limits.</param>
        /// <returns>Pitch in X and roll in Z, in degrees, as Unity euler angles.</returns>
        /// <remarks>
        /// Purely visual - it is applied to a child transform, never to the collider - but
        /// it is the main cue that tells the two airborne states apart from above: a
        /// helicopter that is moving looks different from one that is hovering.
        /// </remarks>
        public static Vector2 Attitude(float speed, float steer, VehicleTuning tuning, FlightTuning flight)
        {
            if (tuning == null)
            {
                throw new ArgumentNullException(nameof(tuning));
            }

            if (flight == null)
            {
                throw new ArgumentNullException(nameof(flight));
            }

            float topSpeed = Mathf.Max(0.01f, tuning.MaxSpeed);
            float pitch = Mathf.Clamp(speed / topSpeed, -1.0f, 1.0f) * flight.MaxPitch;
            float roll = -Mathf.Clamp(steer, -1.0f, 1.0f) * flight.MaxRoll;
            return new Vector2(pitch, roll);
        }
    }
}
