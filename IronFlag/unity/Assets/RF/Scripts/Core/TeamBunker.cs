using System.Collections.Generic;
using UnityEngine;
using IronFlag.Vehicles;

namespace IronFlag.Core
{
    /// <summary>
    /// Where one side's vehicles live: the lift the ground vehicles come up on, the pad the
    /// helicopter takes off from, and the roster waiting inside for somebody to pick one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The design document's core loop starts here - "player selects a vehicle from their
    /// bunker" - and its second step is the reason this is a place rather than a spawn
    /// point: a lift for ground vehicles and a launch pad for the helicopter, "as a
    /// deliberate pacing moment, not just a menu". Both are modelled in the bunker asset,
    /// so both are found in the model rather than guessed at from a number in this file.
    /// </para>
    /// <para>
    /// Which point a vehicle leaves from is the only thing here that branches on what the
    /// vehicle is, and it branches once: everything that flies uses the pad, everything
    /// else uses the lift. <see cref="VehicleBay"/> is what actually moves
    /// a vehicle through one of them.
    /// </para>
    /// <para>
    /// A bunker with no model behind it - which is what a test builds - falls back to two
    /// measurements taken from its own transform. That keeps a bunker useful without an
    /// asset behind it, and the fallback is deliberately crude so that a real scene missing
    /// its markers is visible rather than merely slightly wrong.
    /// </para>
    /// <para>
    /// Registered in the editor as well as in play, which is what <c>ExecuteAlways</c> is
    /// here for: the command-line still puts a vehicle out of a bunker without ever starting
    /// the game. A bunker has no update of its own, so there is nothing else it sets running.
    /// </para>
    /// </remarks>
    [ExecuteAlways]
    [AddComponentMenu("IronFlag/Team Bunker")]
    public sealed class TeamBunker : MonoBehaviour
    {
        /// <summary>Name of the object in the bunker model that ground vehicles rise on.</summary>
        public const string LiftNodeName = "LiftPlatform";

        /// <summary>Name of the object in the bunker model the helicopter takes off from.</summary>
        public const string HelipadNodeName = "Helipad";

        [SerializeField]
        [Tooltip("Which side this bunker belongs to.")]
        private Team team = Team.None;

        [SerializeField]
        [Tooltip("Lift platform in the bunker model. Ground vehicles rise on its origin.")]
        private Transform lift;

        [SerializeField]
        [Tooltip("Helipad in the bunker model. The helicopter lifts off from its origin.")]
        private Transform helipad;

        [SerializeField]
        [Tooltip("Fallback metres in front of the bunker, used only when the model has no lift.")]
        private float doorwayDistance = 6.0f;

        [SerializeField]
        [Tooltip("Fallback metres above the bunker, used only when the model has no helipad.")]
        private float roofHeight = 4.0f;

        /// <summary>Every bunker currently in the scene, in the order they woke up.</summary>
        private static readonly List<TeamBunker> Live = new List<TeamBunker>();

        /// <summary>Which side this bunker belongs to.</summary>
        public Team Team
        {
            get => team;
            set => team = value;
        }

        /// <summary>Heading a vehicle faces when it leaves, clockwise from world +Z.</summary>
        public float FacingYawDegrees => transform.eulerAngles.y;

        /// <summary>Where a ground vehicle stands when it has finished rising.</summary>
        public Vector3 LiftPoint
            => lift == null ? transform.position + (transform.forward * doorwayDistance) : lift.position;

        /// <summary>Where the helicopter sits before it takes off.</summary>
        public Vector3 HelipadPoint
            => helipad == null ? transform.position + (Vector3.up * roofHeight) : helipad.position;

        /// <summary>Whether this bunker found both of its deploy points in a model.</summary>
        public bool IsModelled => lift != null && helipad != null;

        /// <summary>
        /// Returns the bunker one side deploys from.
        /// </summary>
        /// <param name="side">Side to look up.</param>
        /// <returns>That side's bunker, or <c>null</c> when the scene has none.</returns>
        /// <remarks>
        /// Null is a normal answer, not a failure: a vehicle assembled in a test has no
        /// bunker to go back to, and redeploys where it started instead.
        /// </remarks>
        public static TeamBunker For(Team side)
        {
            foreach (TeamBunker bunker in Live)
            {
                if (bunker != null && bunker.team == side)
                {
                    return bunker;
                }
            }

            return null;
        }

        /// <summary>
        /// Sets which side this bunker belongs to and where its two deploy points are.
        /// </summary>
        /// <param name="side">Side this bunker belongs to.</param>
        /// <param name="liftPlatform">Lift platform from the model, or null for the fallback.</param>
        /// <param name="pad">Helipad from the model, or null for the fallback.</param>
        /// <remarks>Called by the sandbox scene builder, which finds both by name.</remarks>
        public void Configure(Team side, Transform liftPlatform, Transform pad)
        {
            team = side;
            lift = liftPlatform;
            helipad = pad;
        }

        /// <summary>
        /// Returns where one vehicle appears when it is deployed.
        /// </summary>
        /// <param name="kind">Vehicle being deployed.</param>
        /// <returns>The deploy position in world space.</returns>
        /// <remarks>
        /// The helicopter leaves from the roof and everything else from the lift, which is
        /// the whole of the difference between a launch pad and a lift as far as any code
        /// is concerned. The pacing beat that plays out between here and being drivable
        /// belongs to <see cref="VehicleBay"/>.
        /// </remarks>
        public Vector3 DeployPointFor(VehicleKind kind)
            => kind == VehicleKind.Helicopter ? HelipadPoint : LiftPoint;

        private void OnEnable() => Live.Add(this);

        private void OnDisable() => Live.Remove(this);
    }
}
