using UnityEngine;

namespace IronFlag.Vehicles
{
    /// <summary>
    /// Traverses the tank's turret or the ASV's launcher towards where the pilot is aiming.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Aiming is separate from steering, which is the whole point of having a turret: the
    /// tank can hold a target while repositioning. The traverse rate is what stops that
    /// from being free - a tank that snaps its gun anywhere instantly has no reason to
    /// point its hull at anything.
    /// </para>
    /// <para>
    /// The turret only yaws, in world space, so it stays level regardless of the hull. The
    /// gun it carries does not read <see cref="AimYawDegrees"/> and does not need to: the
    /// vehicle's muzzle hangs off <see cref="Turret"/>, so pointing the turret and pointing
    /// the gun are the same act and cannot disagree.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Vehicle Turret")]
    public sealed class VehicleTurret : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Vehicle whose aim input the turret follows.")]
        private VehicleController vehicle;

        [SerializeField]
        [Tooltip("The turret or launcher object, pivoted on its ring.")]
        private Transform turret;

        [SerializeField]
        [Tooltip("Traverse rate in degrees per second.")]
        private float turnRate = 60.0f;

        /// <summary>Direction the gun is pointing, in degrees clockwise from world +Z.</summary>
        public float AimYawDegrees => turret == null ? 0.0f : turret.eulerAngles.y;

        /// <summary>The turret transform, for anything that needs to hang off the gun.</summary>
        public Transform Turret => turret;

        /// <summary>
        /// Points this component at the part it traverses.
        /// </summary>
        /// <param name="owner">Vehicle whose aim input to follow.</param>
        /// <param name="turretTransform">The turret or launcher object.</param>
        /// <param name="degreesPerSecond">Traverse rate.</param>
        /// <remarks>Called by the prefab builder, which finds the turret in the model.</remarks>
        public void Configure(VehicleController owner, Transform turretTransform, float degreesPerSecond)
        {
            vehicle = owner;
            turret = turretTransform;
            turnRate = Mathf.Max(0.0f, degreesPerSecond);
        }

        private void Update()
        {
            if (vehicle == null || turret == null)
            {
                return;
            }

            float target = TargetYaw(vehicle.CurrentInput.Aim, vehicle.YawDegrees);
            float next = Mathf.MoveTowardsAngle(
                turret.eulerAngles.y, target, turnRate * Time.deltaTime);
            turret.rotation = Quaternion.Euler(0.0f, next, 0.0f);
        }

        /// <summary>
        /// Returns the heading the gun should be traversing towards.
        /// </summary>
        /// <param name="aim">World-space aim direction on the ground plane, or zero.</param>
        /// <param name="hullYawDegrees">Heading of the hull, as the fallback.</param>
        /// <returns>A heading in degrees clockwise from world +Z.</returns>
        /// <remarks>
        /// With no aim this frame the gun returns to hull forward rather than staying where
        /// it was left, so an abandoned or idling vehicle always looks stowed.
        /// </remarks>
        public static float TargetYaw(Vector2 aim, float hullYawDegrees)
        {
            const float deadZone = 0.01f;
            return aim.sqrMagnitude < deadZone
                ? hullYawDegrees
                : Mathf.Atan2(aim.x, aim.y) * Mathf.Rad2Deg;
        }
    }
}
