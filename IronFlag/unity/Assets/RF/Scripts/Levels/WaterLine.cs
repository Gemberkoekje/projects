using UnityEngine;
using IronFlag.Combat;
using IronFlag.Vehicles;

namespace IronFlag.Levels
{
    /// <summary>
    /// The sea, and what it does to anything that drives into it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Water on this map is not a wall. Driving into it destroys the vehicle - the same
    /// explosion, the same trip back to the bunker, the same four seconds of repairs as
    /// being shot - which is the rule the original this is a homage to had, and the reason
    /// the design document's amphibious jeep is worth being a stretch goal. An invisible
    /// wall at the shoreline would be a lie the player cannot see, and would make a dropped
    /// bridge cost a route and nothing else.
    /// </para>
    /// <para>
    /// The helicopter never drowns, and does not have to be excluded to manage it: it
    /// flies at <see cref="FlightTuning.CruiseAltitude"/>, and even one that has run dry
    /// settles onto <see cref="FlightTuning.GroundedAltitude"/>, which is skids-down at
    /// zero and still above the line. That is the design document's "ignores ground
    /// terrain", falling out of a rule about height rather than out of a check on a
    /// vehicle type.
    /// </para>
    /// <para>
    /// Like <see cref="IronFlag.Objective.Flag"/>, this walks
    /// <see cref="VehicleController.OnTheField"/> rather than putting a trigger volume on the
    /// sea and a collider script on all four vehicles: the roll-call already means exactly
    /// "drivable, and out of its bunker", which is the set of things that can drown.
    /// </para>
    /// </remarks>
    [ExecuteAlways]
    [AddComponentMenu("IronFlag/Water Line")]
    public sealed class WaterLine : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Height of the water surface, in metres.")]
        private float surface = -0.7f;

        [SerializeField]
        [Tooltip("Height below which a vehicle counts as being in the water, in metres.")]
        private float drownDepth = -0.35f;

        /// <summary>The sea in the loaded level, or <c>null</c> when there is none.</summary>
        /// <remarks>
        /// One per level, so a single reference rather than a roll-call. Set on enable, and
        /// only cleared by the instance that set it - a level being replaced enables its new
        /// sea before disabling the old one, and the loser of that race must not blank a
        /// live sea.
        /// </remarks>
        public static WaterLine Active { get; private set; }

        /// <summary>Height of the water surface, in metres.</summary>
        public float Surface => surface;

        /// <summary>Height below which a vehicle counts as being in the water.</summary>
        public float DrownDepth => drownDepth;

        /// <summary>
        /// Sets where the water is.
        /// </summary>
        /// <param name="waterLevel">Height of the water surface, in metres.</param>
        /// <param name="drownsBelow">Height below which a vehicle is in the water.</param>
        public void Configure(float waterLevel, float drownsBelow)
        {
            surface = waterLevel;
            drownDepth = drownsBelow;
        }

        /// <summary>
        /// Reports whether a point is in the water.
        /// </summary>
        /// <param name="at">Point to test; only its height is used.</param>
        /// <returns><c>true</c> when it is below the drowning line.</returns>
        public bool Drowns(Vector3 at) => at.y < drownDepth;

        /// <summary>
        /// Drowns anything that has driven off the land.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Backwards over the roll-call, because drowning a vehicle takes it off the roll-call
        /// inside this loop: <see cref="VehicleHealth.SelfDestruct"/> raises the same event
        /// being shot does, which sends the wreck home and disables its controller. Forwards
        /// over a list that is shrinking skips the vehicle after every one that dies.
        /// </para>
        /// <para>
        /// A vehicle carrying a flag drops it where it went in, which stands in the water
        /// where nothing can reach it and goes back to its tower on the usual clock. That is
        /// deliberate: pushing a carrier into the sea is a way to deny a capture, and it
        /// costs the defender the contest over the drop.
        /// </para>
        /// </remarks>
        private void Update()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            for (int index = VehicleController.OnTheField.Count - 1; index >= 0; index--)
            {
                VehicleController vehicle = VehicleController.OnTheField[index];
                if (vehicle == null || !Drowns(vehicle.transform.position))
                {
                    continue;
                }

                VehicleHealth health = vehicle.GetComponent<VehicleHealth>();
                if (health != null)
                {
                    health.SelfDestruct();
                }
            }
        }

        private void OnEnable() => Active = this;

        private void OnDisable()
        {
            if (Active == this)
            {
                Active = null;
            }
        }
    }
}
