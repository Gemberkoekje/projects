using UnityEngine;

namespace IronFlag.Vehicles
{
    /// <summary>
    /// Spins a helicopter's rotors.
    /// </summary>
    /// <remarks>
    /// The two rotors are separate objects in the model, pivoted on their own hubs: the
    /// main one turns about its mast, which is Unity's up, and the tail one about its
    /// shaft, which is Unity's X. A wide spinning disc is the clearest "this one flies" cue
    /// from directly above, which is the only angle the game is played at.
    /// </remarks>
    [AddComponentMenu("IronFlag/Vehicle Rotors")]
    public sealed class VehicleRotors : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Main rotor, turning about its mast.")]
        private Transform mainRotor;

        [SerializeField]
        [Tooltip("Tail rotor, turning about its shaft.")]
        private Transform tailRotor;

        [SerializeField]
        [Tooltip("Main rotor speed in RPM. Fast enough to blur, slow enough to read.")]
        private float mainRotorRpm = 420.0f;

        [SerializeField]
        [Tooltip("Tail rotor speed in RPM.")]
        private float tailRotorRpm = 900.0f;

        /// <summary>
        /// Points this component at the rotors it spins.
        /// </summary>
        /// <param name="main">Main rotor transform, or null when the model has none.</param>
        /// <param name="tail">Tail rotor transform, or null when the model has none.</param>
        /// <remarks>Called by the prefab builder, which finds the rotors in the model.</remarks>
        public void Configure(Transform main, Transform tail)
        {
            mainRotor = main;
            tailRotor = tail;
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            if (mainRotor != null)
            {
                mainRotor.Rotate(Vector3.up, DegreesThisFrame(mainRotorRpm, deltaTime), Space.Self);
            }

            if (tailRotor != null)
            {
                tailRotor.Rotate(Vector3.right, DegreesThisFrame(tailRotorRpm, deltaTime), Space.Self);
            }
        }

        private static float DegreesThisFrame(float rpm, float deltaTime) => rpm * 6.0f * deltaTime;
    }
}
