using System;
using UnityEngine;

namespace IronFlag.Vehicles
{
    /// <summary>
    /// Rolls a vehicle's wheels at the speed it is travelling, and turns the steered ones.
    /// </summary>
    /// <remarks>
    /// Cosmetic, and worth having early: from directly above, wheel spin is most of what
    /// separates "driving" from "sliding" on a model this simple. The wheels are the
    /// separate objects the Blender pipeline leaves unjoined, each with its origin already
    /// on its axle, so rolling one is a rotation about its own local X.
    /// </remarks>
    [AddComponentMenu("IronFlag/Vehicle Wheels")]
    public sealed class VehicleWheels : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Vehicle whose speed and steering the wheels follow.")]
        private VehicleController vehicle;

        [SerializeField]
        [Tooltip("Every wheel that rolls. Rotated about its own local X axis.")]
        private Transform[] wheels = Array.Empty<Transform>();

        [SerializeField]
        [Tooltip("The subset of wheels that also steer. Usually the front axle.")]
        private Transform[] steeredWheels = Array.Empty<Transform>();

        [SerializeField]
        [Tooltip("Wheel radius in metres. Decides how fast the wheels roll for a given speed.")]
        private float wheelRadius = 0.35f;

        [SerializeField]
        [Tooltip("How far the steered wheels turn at full lock, in degrees.")]
        private float maxSteerAngle = 26.0f;

        [SerializeField]
        [Tooltip("Seconds for the steered wheels to reach the angle the pilot is asking for.")]
        private float steerDamping = 0.08f;

        private float rollDegrees;
        private float steerAngle;
        private float steerVelocity;

        /// <summary>
        /// Points this component at the parts it animates.
        /// </summary>
        /// <param name="owner">Vehicle whose speed and steering to follow.</param>
        /// <param name="rolling">Every wheel that rolls.</param>
        /// <param name="steered">The subset that also steers; may be empty.</param>
        /// <param name="radius">Wheel radius in metres.</param>
        /// <remarks>Called by the prefab builder, which reads the wheels out of the model.</remarks>
        public void Configure(VehicleController owner, Transform[] rolling, Transform[] steered, float radius)
        {
            vehicle = owner;
            wheels = rolling == null ? Array.Empty<Transform>() : rolling;
            steeredWheels = steered == null ? Array.Empty<Transform>() : steered;
            wheelRadius = Mathf.Max(0.05f, radius);
        }

        private void Update()
        {
            if (vehicle == null || wheels.Length == 0)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            float circumferenceRate = vehicle.ForwardSpeed / Mathf.Max(0.05f, wheelRadius);
            rollDegrees = Mathf.Repeat(
                rollDegrees + (circumferenceRate * Mathf.Rad2Deg * deltaTime), 360.0f);

            float targetSteer = vehicle.CurrentInput.Steer * maxSteerAngle;
            steerAngle = Mathf.SmoothDamp(
                steerAngle, targetSteer, ref steerVelocity, steerDamping, Mathf.Infinity, deltaTime);

            Quaternion roll = Quaternion.Euler(rollDegrees, 0.0f, 0.0f);
            Quaternion steer = Quaternion.Euler(0.0f, steerAngle, 0.0f);

            foreach (Transform wheel in wheels)
            {
                if (wheel == null)
                {
                    continue;
                }

                wheel.localRotation = IsSteered(wheel) ? steer * roll : roll;
            }
        }

        private bool IsSteered(Transform wheel)
        {
            foreach (Transform steered in steeredWheels)
            {
                if (steered == wheel)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
