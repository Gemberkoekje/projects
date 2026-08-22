using System;
using UnityEngine;

namespace IronFlag.Vehicles
{
    /// <summary>
    /// The vertical axis the helicopter adds on top of the shared driving model.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The collective holds altitude when it is released, rather than settling towards the
    /// ground: a helicopter the pilot has to keep feeding input to just to stay level makes
    /// every other control harder to use, and the interesting decision is which altitude to
    /// sit at, not whether to keep sitting there.
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
        /// <param name="lift">Collective in -1..1: 1 climbs, -1 descends, 0 holds.</param>
        /// <param name="tuning">Flight numbers for this aircraft.</param>
        /// <param name="deltaTime">Length of the step in seconds. Zero or less is a no-op.</param>
        /// <returns>The flight state at the end of the step.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="tuning"/> is null.</exception>
        public static FlightState Step(FlightState state, float lift, FlightTuning tuning, float deltaTime)
        {
            if (tuning == null)
            {
                throw new ArgumentNullException(nameof(tuning));
            }

            if (deltaTime <= 0.0f)
            {
                return state;
            }

            float target = Mathf.Clamp(lift, -1.0f, 1.0f) * tuning.ClimbRate;
            float vertical = Mathf.MoveTowards(
                state.VerticalSpeed, target, tuning.ClimbAcceleration * deltaTime);

            float altitude = state.Altitude + (vertical * deltaTime);
            float held = Mathf.Clamp(altitude, tuning.MinAltitude, tuning.MaxAltitude);

            // Stopped by the floor or the ceiling: drop the vertical speed too, so releasing
            // the collective does not leave a stored climb that fires the moment the pilot
            // pushes away from the limit.
            if (!Mathf.Approximately(held, altitude))
            {
                vertical = 0.0f;
            }

            return new FlightState(held, vertical);
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
