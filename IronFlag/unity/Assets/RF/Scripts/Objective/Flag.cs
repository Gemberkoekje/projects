using System;
using System.Collections.Generic;
using UnityEngine;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Supply;
using IronFlag.Vehicles;

namespace IronFlag.Objective
{
    /// <summary>
    /// One side's flag: where it is, who is carrying it, and the moment somebody gets it
    /// home.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The design document's core loop ends here - "jeep locates and grabs the enemy flag
    /// from its tower; jeep returns the flag to the home bunker, win" - and so does the
    /// first pillar, because <see cref="FlagRules.CanCarry"/> is asked before anything else.
    /// Everything a match is won or lost by is four state transitions on this component.
    /// </para>
    /// <para>
    /// The flag looks for its own carrier rather than being found by one. Every other way
    /// round means a component on all four vehicles asking every frame whether it is allowed
    /// to touch something, and three of the four would exist only to answer no; this way the
    /// vehicles know nothing about the objective at all. It walks
    /// <see cref="VehicleController.OnTheField"/>, which is at most four entries and only
    /// ever contains vehicles somebody is actually driving.
    /// </para>
    /// <para>
    /// A carried flag is <em>placed</em> above its carrier every frame rather than parented
    /// to it. Parenting would put the flag inside the vehicle's own hierarchy, where
    /// <see cref="VehicleBay"/> hides it along with the hull and drags it back to the bunker
    /// with the wreck - and a flag that goes home with the jeep that stole it is the one
    /// thing this component must never do.
    /// </para>
    /// <para>
    /// Registered in the editor as well as in play, like <see cref="TeamBunker"/>, because
    /// the command-line still stages a raid in a scene nobody is playing. Nothing moves
    /// there: the state machine below is guarded on <see cref="Application.isPlaying"/>, and
    /// the still drives it through the public methods instead.
    /// </para>
    /// </remarks>
    [ExecuteAlways]
    [AddComponentMenu("IronFlag/Flag")]
    public sealed class Flag : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Which side this flag belongs to. That side defends it; the other steals it.")]
        private Team team = Team.None;

        [SerializeField]
        [Tooltip("The tower this flag stands on and goes back to.")]
        private FlagTower home;

        /// <summary>Every flag currently in the scene, in the order they woke up.</summary>
        private static readonly List<Flag> Live = new List<Flag>();

        private FlagState state = FlagState.None;
        private VehicleController carrier;
        private Team carriedBy = Team.None;
        private Vector3 carriedAt;
        private float returning;
        private Renderer[] skin = Array.Empty<Renderer>();
        private bool found;

        /// <summary>Raised when a jeep lifts this flag off its tower or off the ground.</summary>
        public static event Action<Flag> AnyTaken;

        /// <summary>Raised when this flag reaches the enemy bunker and ends the match.</summary>
        /// <remarks>
        /// Static, unlike <see cref="VehicleHealth.Destroyed"/> and
        /// <see cref="IronFlag.Destruction.Destructible.Collapsed"/>, because the one thing
        /// that listens to it is <see cref="Match"/> - and a match that subscribed to each
        /// flag in turn would have to be created after both of them and would silently miss
        /// a third. A static event is the same wiring with the ordering removed.
        /// </remarks>
        public static event Action<Flag> AnyCaptured;

        /// <summary>Raised whenever this flag changes hands, place or state.</summary>
        public event Action<Flag> StateChanged;

        /// <summary>Which side this flag belongs to.</summary>
        public Team Team => team;

        /// <summary>The tower this flag stands on and goes back to.</summary>
        public FlagTower Home => home;

        /// <summary>Where this flag is in the round trip between tower and bunker.</summary>
        public FlagState State => state;

        /// <summary>The vehicle carrying this flag, or <c>null</c> when nobody is.</summary>
        public VehicleController Carrier => carrier;

        /// <summary>The side that took this flag, or <see cref="Team.None"/> when nobody has.</summary>
        /// <remarks>
        /// Remembered past the drop, so a flag lying on the ground still knows whose raid it
        /// belongs to and <see cref="Match"/> can name a winner from the flag alone.
        /// </remarks>
        public Team CarriedBy => carriedBy;

        /// <summary>Seconds until a dropped flag goes home, or zero when it is not dropped.</summary>
        public float ReturnCountdown
            => state == FlagState.Dropped ? Mathf.Max(0.0f, returning) : 0.0f;

        /// <summary>Whether this flag is away from its tower, one way or another.</summary>
        public bool IsOut => state == FlagState.Carried || state == FlagState.Dropped;

        /// <summary>
        /// Whether this flag can currently be seen on the map.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A flag inside an intact tower is invisible, which is the entire reason the design
        /// document's decoy works: two identical pyramids, and the only way to tell them
        /// apart is to break one open. Once it is off the tower it is visible for good -
        /// everybody is meant to know who is carrying.
        /// </para>
        /// <para>
        /// This is also the pickup rule, deliberately: a flag can be taken exactly when it
        /// can be seen. Writing "you may take it from a broken tower" as a second condition
        /// would be a second place for the two to disagree, and a flag you could take
        /// without seeing would hand the decoy's answer to whoever drove into a pyramid on
        /// spec.
        /// </para>
        /// </remarks>
        public bool IsVisible => state != FlagState.AtTower || home == null || home.IsOpen;

        /// <summary>
        /// Reports whether a vehicle standing at a point could pick this flag up from there.
        /// </summary>
        /// <param name="at">Where the vehicle is, in world space.</param>
        /// <returns><c>true</c> when only the jeep-only rule stands between them.</returns>
        /// <remarks>
        /// Deliberately says nothing about <em>who</em> is standing there, because the one
        /// thing that asks is the HUD, telling the player driving a tank why nothing
        /// happened when they drove over it. A flag still sealed inside its tower answers
        /// <c>false</c>: a hint about something the player cannot see is a hint that gives
        /// the decoy away.
        /// </remarks>
        public bool IsWithinReach(Vector3 at)
        {
            EnsureFlying();

            if (!IsVisible || (state != FlagState.AtTower && state != FlagState.Dropped))
            {
                return false;
            }

            return ReachFrom(at) <= FlagRules.PickupRadius;
        }

        /// <summary>
        /// Returns how far a vehicle standing at a point is from being able to take this flag.
        /// </summary>
        /// <param name="at">Where the vehicle is, in world space.</param>
        /// <returns>Metres, across the map.</returns>
        /// <remarks>
        /// A flag on its tower is measured from the tower's <em>walls</em> rather than from
        /// the flag, because the flag is inside them and a jeep is not allowed to be. Every
        /// other state - dropped on open ground, riding a mast - is measured from the flag
        /// itself, which is a thing you really can drive onto.
        /// </remarks>
        private float ReachFrom(Vector3 at)
        {
            return state == FlagState.AtTower && home != null
                ? home.DistanceFrom(at)
                : CombatPlane.DistanceOnMap(transform.position, at);
        }

        /// <summary>
        /// Returns the flag belonging to one side.
        /// </summary>
        /// <param name="side">Side to look up.</param>
        /// <returns>That side's flag, or <c>null</c> when the scene has none.</returns>
        public static Flag Of(Team side)
        {
            foreach (Flag flag in Live)
            {
                if (flag != null && flag.team == side)
                {
                    return flag;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the flag one side is trying to steal.
        /// </summary>
        /// <param name="side">Side doing the stealing.</param>
        /// <returns>The first flag hostile to that side, or <c>null</c> when there is none.</returns>
        /// <remarks>
        /// Asked rather than derived from a "the other team" helper, because with more than
        /// two sides there is no such thing - and <see cref="Teams.IsHostile"/> already says
        /// which flags are worth taking.
        /// </remarks>
        public static Flag EnemyOf(Team side)
        {
            foreach (Flag flag in Live)
            {
                if (flag != null && Teams.IsHostile(side, flag.team))
                {
                    return flag;
                }
            }

            return null;
        }

        /// <summary>
        /// Sets which side this flag belongs to and which tower it flies from.
        /// </summary>
        /// <param name="side">Side that defends this flag.</param>
        /// <param name="tower">Tower it stands on and goes back to.</param>
        /// <remarks>Called by the sandbox scene builder.</remarks>
        public void Configure(Team side, FlagTower tower)
        {
            team = side;
            home = tower;
            state = FlagState.None;
            carrier = null;
            carriedBy = Team.None;
            returning = 0.0f;
            EnsureFlying();
        }

        /// <summary>
        /// Hands this flag to a vehicle, if the rules allow that vehicle to take it.
        /// </summary>
        /// <param name="vehicle">Vehicle trying to pick it up.</param>
        /// <returns><c>true</c> when it is now riding that vehicle's mast.</returns>
        /// <remarks>
        /// The one place the jeep-only rule is enforced, and it is enforced against the
        /// vehicle rather than against the player: a tank that drives over a dropped flag
        /// gets the same answer whoever is at the controls. A flag already carried or
        /// already captured cannot be taken again, which is what stops two jeeps sharing it
        /// on the frame they arrive together.
        /// </remarks>
        public bool GiveTo(VehicleController vehicle)
        {
            EnsureFlying();

            if (vehicle == null || state == FlagState.Carried || state == FlagState.Captured)
            {
                return false;
            }

            // Nothing comes out of a tower nobody has broken open, however it was asked.
            // The check is here as well as in the loop that looks for a carrier, because
            // this method is what the tests and the command-line still drive the game
            // through - and a raid staged past a rule is a picture of a game that does not
            // exist.
            if (state == FlagState.AtTower && !IsVisible)
            {
                return false;
            }

            Team side = TeamOf(vehicle);
            if (!Teams.IsHostile(side, team) || !FlagRules.CanCarry(vehicle.Kind))
            {
                return false;
            }

            carrier = vehicle;
            carriedBy = side;
            carriedAt = vehicle.transform.position;
            returning = 0.0f;
            Enter(FlagState.Carried);
            Ride();
            AnyTaken?.Invoke(this);
            return true;
        }

        /// <summary>
        /// Plants this flag on the ground where its carrier last stood.
        /// </summary>
        /// <returns><c>true</c> when it was being carried and now is not.</returns>
        /// <remarks>
        /// The position is the one recorded while the carrier was alive rather than the
        /// carrier's current one: by the time a wreck raises its event
        /// <see cref="VehicleBay"/> has already moved the hull back inside its own bunker,
        /// and reading the position then would drop the flag in the enemy's base.
        /// </remarks>
        public bool Drop()
        {
            EnsureFlying();

            if (state != FlagState.Carried)
            {
                return false;
            }

            carrier = null;
            returning = FlagRules.ReturnSeconds;
            Place(CombatPlane.Flatten(carriedAt), 0.0f);
            Enter(FlagState.Dropped);
            return true;
        }

        /// <summary>
        /// Puts this flag back on its own tower.
        /// </summary>
        /// <remarks>
        /// What the countdown on a dropped flag ends in, and the only way a side gets its
        /// flag back. Deliberately not something a defender can trigger by driving over it -
        /// see <see cref="FlagRules.ReturnSeconds"/>.
        /// </remarks>
        public void SendHome()
        {
            carrier = null;
            carriedBy = Team.None;
            returning = 0.0f;
            Place(HomePoint(), HomeYaw());
            Enter(FlagState.AtTower);
        }

        /// <summary>
        /// Delivers this flag, which is how a match is won.
        /// </summary>
        /// <returns><c>true</c> when this call is the one that ended the match.</returns>
        /// <remarks>
        /// The flag is left standing where it was delivered rather than removed, because the
        /// last frame of a match is a picture of a jeep at its own bunker with the other
        /// side's colours on the mast, and that picture is the entire point of the game.
        /// </remarks>
        public bool Capture()
        {
            if (state != FlagState.Carried)
            {
                return false;
            }

            Place(CombatPlane.Flatten(carriedAt), carrier == null ? 0.0f : carrier.YawDegrees);
            carrier = null;
            Enter(FlagState.Captured);
            AnyCaptured?.Invoke(this);
            return true;
        }

        private void Awake() => EnsureFlying();

        private void OnEnable()
        {
            Live.Add(this);
            EnsureFlying();
        }

        private void OnDisable() => Live.Remove(this);

        /// <summary>
        /// Runs the flag: looks for a carrier, follows the one it has, and counts down.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Late, because a carried flag is placed above a vehicle that has already moved
        /// this frame. Doing it in <c>Update</c> puts the flag one frame behind the jeep,
        /// which at the jeep's top speed is most of a metre of daylight between the mast and
        /// the vehicle it is bolted to.
        /// </para>
        /// <para>
        /// Nothing runs outside play mode. This component is <c>ExecuteAlways</c> only so
        /// that it registers itself for the still, and a state machine ticking in the editor
        /// would quietly walk a saved scene through a match nobody asked for.
        /// </para>
        /// </remarks>
        private void LateUpdate()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            EnsureFlying();

            if (Match.IsFinished)
            {
                return;
            }

            switch (state)
            {
                case FlagState.AtTower:
                    Rest();
                    break;

                case FlagState.Carried:
                    Follow();
                    break;

                case FlagState.Dropped:
                    CountDown();
                    break;
            }
        }

        /// <summary>
        /// Rides the carrier, and hands the match over if it has reached home.
        /// </summary>
        private void Follow()
        {
            if (carrier == null || !carrier.isActiveAndEnabled)
            {
                // The carrier was wrecked, scuttled or parked. Whichever it was, the flag
                // stays out here on the map.
                Drop();
                return;
            }

            carriedAt = carrier.transform.position;
            Ride();

            if (SupplyPoint.HomeFor(carriedAt, carriedBy, carrier is Helicopter) != null)
            {
                Capture();
            }
        }

        /// <summary>
        /// Counts a dropped flag down, and lets a jeep pick it up before it goes home.
        /// </summary>
        private void CountDown()
        {
            returning -= Time.deltaTime;
            if (returning <= 0.0f)
            {
                SendHome();
                return;
            }

            LookForACarrier();
        }

        /// <summary>
        /// Sits on the tower: follows the mount, shows or hides itself, and waits.
        /// </summary>
        /// <remarks>
        /// Every frame rather than once, because the tower moves the flag underneath it. A
        /// tower that is shot from intact to damaged to destroyed swaps in a different model
        /// each time, each with its own mount, and the flag has to climb down with it -
        /// otherwise a flattened tower leaves its flag hanging two metres in the air where
        /// its roof used to be. It is the same call that reveals the flag when the first
        /// shell lands, which is why the two can never disagree.
        /// </remarks>
        private void Rest()
        {
            Place(HomePoint(), HomeYaw());
            SetVisible(IsVisible);

            if (IsVisible)
            {
                LookForACarrier();
            }
        }

        /// <summary>
        /// Offers this flag to the nearest eligible vehicle standing on it.
        /// </summary>
        /// <remarks>
        /// Nearest rather than first, so two jeeps arriving together is decided by where
        /// they are instead of by which one happened to be enabled first. Distance is
        /// measured across the map, like everything else in this game - see
        /// <see cref="CombatPlane"/>. Eligibility is checked here as well as in
        /// <see cref="GiveTo"/>: a defending jeep parked on its own flag is not a candidate at
        /// all, rather than the one candidate this method tries and watches <see cref="GiveTo"/>
        /// refuse - otherwise it would keep a hostile jeep standing in range from ever being
        /// offered the flag for as long as the nearer friendly one stayed put.
        /// </remarks>
        private void LookForACarrier()
        {
            VehicleController closest = null;
            float nearest = FlagRules.PickupRadius;

            foreach (VehicleController vehicle in VehicleController.OnTheField)
            {
                if (vehicle == null || !FlagRules.CanCarry(vehicle.Kind) || !Teams.IsHostile(TeamOf(vehicle), team))
                {
                    continue;
                }

                float away = ReachFrom(vehicle.transform.position);
                if (away <= nearest)
                {
                    nearest = away;
                    closest = vehicle;
                }
            }

            if (closest != null)
            {
                GiveTo(closest);
            }
        }

        /// <summary>
        /// Puts the flag on its carrier's mast for this frame.
        /// </summary>
        private void Ride()
        {
            if (carrier == null)
            {
                return;
            }

            Place(
                carrier.transform.position + (Vector3.up * FlagRules.MastHeight),
                carrier.YawDegrees);
        }

        /// <summary>
        /// Brings the flag up on its tower the first time anybody asks anything of it.
        /// </summary>
        /// <remarks>
        /// Lazy for the same reason <see cref="IronFlag.Destruction.Destructible"/> and
        /// <see cref="VehicleBay"/> are: <c>Awake</c> does not run outside play mode, and the
        /// command-line still stages a raid in a scene nobody is playing.
        /// </remarks>
        private void EnsureFlying()
        {
            if (!found)
            {
                found = true;
                skin = GetComponentsInChildren<Renderer>(true);
            }

            if (state == FlagState.None)
            {
                Place(HomePoint(), HomeYaw());
                Enter(FlagState.AtTower);
            }
        }

        /// <summary>
        /// Moves to a new state, shows or hides the flag, and tells anybody listening.
        /// </summary>
        /// <param name="wanted">State to move to.</param>
        private void Enter(FlagState wanted)
        {
            state = wanted;
            SetVisible(IsVisible);
            StateChanged?.Invoke(this);
        }

        /// <summary>Where this flag stands when it is home.</summary>
        /// <remarks>
        /// The tower's own answer rather than a height above it, because a broken tower
        /// carries its flag lower than a whole one - see <see cref="FlagTower.FlagPoint"/>.
        /// </remarks>
        private Vector3 HomePoint()
            => home == null ? transform.position : home.FlagPoint;

        /// <summary>Which way this flag faces when it is home.</summary>
        /// <remarks>
        /// The tower's answer rather than its transform: which way a flag faces inside a
        /// tower is decided by where the hole in the wall is, and that is art - see
        /// <see cref="FlagTower.FlagYaw"/>.
        /// </remarks>
        private float HomeYaw()
            => home == null ? transform.eulerAngles.y : home.FlagYaw;

        private void Place(Vector3 at, float yawDegrees)
            => transform.SetPositionAndRotation(at, Quaternion.Euler(0.0f, yawDegrees, 0.0f));

        private void SetVisible(bool visible)
        {
            foreach (Renderer part in skin)
            {
                if (part != null)
                {
                    part.enabled = visible;
                }
            }
        }

        /// <summary>
        /// Returns which side a vehicle is on.
        /// </summary>
        /// <param name="vehicle">Vehicle to look up.</param>
        /// <returns>Its side, or <see cref="Team.None"/> when it has no paint.</returns>
        /// <remarks>
        /// Read off <see cref="VehicleTeamPaint"/>, the same place
        /// <see cref="VehicleHealth.Team"/> reads it, so a vehicle cannot shoot as one side
        /// and steal as another.
        /// </remarks>
        private static Team TeamOf(VehicleController vehicle)
        {
            var paint = vehicle.GetComponent<VehicleTeamPaint>();
            return paint == null ? Team.None : paint.Team;
        }
    }
}
