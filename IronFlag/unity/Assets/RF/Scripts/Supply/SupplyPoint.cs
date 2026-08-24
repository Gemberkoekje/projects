using System.Collections.Generic;
using UnityEngine;
using IronFlag.Core;

namespace IronFlag.Supply
{
    /// <summary>
    /// A place on the map that puts fuel and ammunition back into whatever parks on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One component covers both halves of the design document's economy - the bunker a
    /// side comes home to, and the fuel and ammo depots scattered across the map - because
    /// the difference between them is entirely in the numbers. A bunker belongs to a side,
    /// fills both pools quickly and takes the helicopter; a depot belongs to nobody, fills
    /// one pool slowly, and does not.
    /// </para>
    /// <para>
    /// Rates are <em>fractions of a full pool per second</em> rather than litres or rounds.
    /// The four vehicles carry pools that differ by a factor of twenty - twelve rockets
    /// against two hundred and forty chaingun rounds - so an absolute rate would either be
    /// meaningless to the ASV or instant for the helicopter. A fraction says the thing the
    /// designer actually means: how long a vehicle has to sit still.
    /// </para>
    /// <para>
    /// Depots are contestable rather than owned, which is what the asset spec means by
    /// giving them no team colour: a depot near the middle of the map is a place both sides
    /// want, and one that only worked for its owner would just be a second bunker.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Supply Point")]
    public sealed class SupplyPoint : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Side this point serves. None serves everybody, which is what a depot does.")]
        private Team team = Team.None;

        [SerializeField]
        [Tooltip("Metres across the map a vehicle may be and still be served.")]
        private float radius = 9.0f;

        [SerializeField]
        [Tooltip("Fraction of a full tank put in per second. 0.25 fills a dry one in four seconds.")]
        private float fuelRate = 0.25f;

        [SerializeField]
        [Tooltip("Fraction of a full load put in per second. Zero means this point has no ammunition.")]
        private float ammoRate = 0.25f;

        [SerializeField]
        [Tooltip("Whether a helicopter can be served here. Only the bunkers can.")]
        private bool servesAircraft = false;

        /// <summary>Every supply point currently in the scene, in the order they woke up.</summary>
        private static readonly List<SupplyPoint> Live = new List<SupplyPoint>();

        /// <summary>Side this point serves, or <see cref="Team.None"/> when it serves everybody.</summary>
        public Team Team => team;

        /// <summary>Metres across the map a vehicle may be and still be served.</summary>
        public float Radius => radius;

        /// <summary>Fraction of a full tank this point puts in per second.</summary>
        public float FuelRate => fuelRate;

        /// <summary>Fraction of a full load of ammunition this point puts in per second.</summary>
        public float AmmoRate => ammoRate;

        /// <summary>Whether a helicopter can be served here.</summary>
        public bool ServesAircraft => servesAircraft;

        /// <summary>Seconds this point takes to fill a dry tank.</summary>
        public float SecondsToRefuel => fuelRate <= 0.0f ? Mathf.Infinity : 1.0f / fuelRate;

        /// <summary>Seconds this point takes to fill an empty gun.</summary>
        public float SecondsToRearm => ammoRate <= 0.0f ? Mathf.Infinity : 1.0f / ammoRate;

        /// <summary>
        /// Sets what this point serves, to whom, and how fast.
        /// </summary>
        /// <param name="side">Side served, or <see cref="Team.None"/> for everybody.</param>
        /// <param name="servedRadius">Metres across the map a vehicle may be.</param>
        /// <param name="fuelPerSecond">Fraction of a full tank put in per second.</param>
        /// <param name="ammoPerSecond">Fraction of a full load put in per second.</param>
        /// <param name="aircraft">Whether a helicopter can be served here.</param>
        /// <remarks>Called by the sandbox scene builder and by the level builder.</remarks>
        public void Configure(
            Team side, float servedRadius, float fuelPerSecond, float ammoPerSecond, bool aircraft)
        {
            team = side;
            radius = Mathf.Max(0.0f, servedRadius);
            fuelRate = Mathf.Max(0.0f, fuelPerSecond);
            ammoRate = Mathf.Max(0.0f, ammoPerSecond);
            servesAircraft = aircraft;
        }

        /// <summary>
        /// Returns the point serving a vehicle where it currently stands.
        /// </summary>
        /// <param name="at">Where the vehicle is, in world space.</param>
        /// <param name="side">Side the vehicle is on.</param>
        /// <param name="aircraft">Whether the vehicle is a helicopter.</param>
        /// <returns>The point serving it, or <c>null</c> when it is not on one.</returns>
        /// <remarks>
        /// The first match wins, and points are not allowed to overlap in the generated
        /// scene, so which one is arbitrary only in a scene that already has a layout bug.
        /// </remarks>
        public static SupplyPoint ServingAt(Vector3 at, Team side, bool aircraft)
        {
            foreach (SupplyPoint point in Live)
            {
                if (point != null && point.Serves(at, side, aircraft))
                {
                    return point;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the vehicle's <em>own side's</em> point serving it where it stands.
        /// </summary>
        /// <param name="at">Where the vehicle is, in world space.</param>
        /// <param name="side">Side the vehicle is on.</param>
        /// <param name="aircraft">Whether the vehicle is a helicopter.</param>
        /// <returns>That side's point serving it, or <c>null</c> when it is not on one.</returns>
        /// <remarks>
        /// This is the definition of "home", and three separate rules turn on it: swapping
        /// vehicles rather than scuttling one, the note on the HUD, and - since M6 - winning
        /// the match. Asked separately from <see cref="ServingAt"/> because that one answers
        /// with the first point of any team, so a neutral depot parked over a bunker would
        /// shadow it; a depot that could stop a flag being captured is not a bug anybody
        /// would find by reading the code.
        /// </remarks>
        public static SupplyPoint HomeFor(Vector3 at, Team side, bool aircraft)
        {
            if (side == Team.None)
            {
                return null;
            }

            foreach (SupplyPoint point in Live)
            {
                if (point != null && point.team == side && point.Serves(at, side, aircraft))
                {
                    return point;
                }
            }

            return null;
        }

        /// <summary>
        /// Reports whether this point serves a vehicle where it currently stands.
        /// </summary>
        /// <param name="at">Where the vehicle is, in world space.</param>
        /// <param name="side">Side the vehicle is on.</param>
        /// <param name="aircraft">Whether the vehicle is a helicopter.</param>
        /// <returns><c>true</c> when it is being served.</returns>
        /// <remarks>
        /// <para>
        /// The distance is measured across the map and never through the air, for the same
        /// reason a shot is - see <see cref="IronFlag.Combat.CombatPlane"/>. Height is not
        /// used at all. It used to be: a helicopter had to have come down onto the pad
        /// before anything would serve it, which was the whole of what stopped one taking
        /// on fuel from thirty metres up. That rule died with the collective - a helicopter
        /// now flies at one altitude and cannot descend to anything - so a height gate here
        /// would simply mean no aircraft is ever served anywhere.
        /// </para>
        /// <para>
        /// What survives, and is the drawback the design document's roster table actually
        /// gives the helicopter, is <see cref="ServesAircraft"/>: only a bunker has it, so
        /// the aircraft is still the one vehicle that cannot rearm at a field depot and
        /// still has to fly all the way home. It is served hovering over its own bunker
        /// rather than landed on the pad, which is the only thing it is now able to do.
        /// </para>
        /// <para>
        /// A point with no team serves everybody, including anything on no side at all.
        /// </para>
        /// </remarks>
        public bool Serves(Vector3 at, Team side, bool aircraft)
        {
            if (aircraft && !servesAircraft)
            {
                return false;
            }

            if (team != Team.None && team != side)
            {
                return false;
            }

            Vector3 middle = transform.position;
            float acrossX = at.x - middle.x;
            float acrossZ = at.z - middle.z;
            return (acrossX * acrossX) + (acrossZ * acrossZ) <= radius * radius;
        }

        private void OnEnable() => Live.Add(this);

        private void OnDisable() => Live.Remove(this);
    }
}
