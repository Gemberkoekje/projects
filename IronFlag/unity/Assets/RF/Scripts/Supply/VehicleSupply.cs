using UnityEngine;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Vehicles;

namespace IronFlag.Supply
{
    /// <summary>
    /// One vehicle's tank and ammunition box: what it leaves the bunker with, what running
    /// around costs it, and what happens when either runs out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fuel is measured in seconds of running at full demand, so a pool and an endurance
    /// are the same number - see <see cref="VehicleTuning.FuelCapacity"/>. Ammunition is
    /// measured in rounds, and what a load of them is worth is seconds of holding the
    /// trigger down - see <see cref="WeaponTuning.Rounds"/>. Both are read back out as a
    /// fraction, because a bar is what a player actually reads mid-fight.
    /// </para>
    /// <para>
    /// Running out of fuel <em>strands</em> the vehicle rather than switching it off: the
    /// design document is explicit that a stranded vehicle can still fight where it stands,
    /// and can self-destruct to get back to the bunker immediately. So this takes the
    /// throttle away and leaves the turret and the trigger alone. A stranded helicopter is
    /// given a descent instead of a hover, which puts it on the ground where it can be shot
    /// at rather than parked in the sky for ever.
    /// </para>
    /// <para>
    /// What the vehicle is driving on is part of the bill. Soft ground makes the engine work
    /// harder for the same speed - see <see cref="IronFlag.Levels.SurfaceTuning.FuelDraw"/> -
    /// so a beach costs range as well as time. Every vehicle pays that in full, unlike grip,
    /// which is weighed per vehicle: see <see cref="VehicleTuning.SurfaceSensitivity"/> for
    /// why the two are different questions.
    /// </para>
    /// <para>
    /// A vehicle with no supply component never runs out of anything, which is what a
    /// vehicle assembled in a test is. A rig should not need an economy bolted to it before
    /// it can drive across a room or fire a shot.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Vehicle Supply")]
    [DefaultExecutionOrder(ExecutionOrder)]
    public sealed class VehicleSupply : MonoBehaviour
    {
        /// <summary>
        /// Runs after the pilot, so the throttle it takes away stays taken away.
        /// </summary>
        /// <remarks>
        /// <see cref="IronFlag.Players.PlayerVehicleDriver"/> writes one frame of intent at
        /// the default order, and a stranded vehicle has to overwrite it afterwards or the
        /// throttle would come back every frame. The weapon needs no ordering, because it
        /// asks for a round rather than being told it may not have one.
        /// </remarks>
        public const int ExecutionOrder = 50;

        [SerializeField]
        [Tooltip("Seconds of running at full demand on a full tank. From VehicleTuning.")]
        private float fuelCapacity = 120.0f;

        [SerializeField]
        [Range(0.0f, 1.0f)]
        [Tooltip("Fuel drawn standing still, as a fraction of the flat-out draw.")]
        private float idleFuelDraw = 0.15f;

        [SerializeField]
        [Tooltip("Rounds carried when full. From WeaponTuning.Rounds.")]
        private int roundsCarried = 30;

        private VehicleController vehicle;
        private GroundVehicle driver;
        private Helicopter flyer;
        private VehicleHealth health;
        private VehicleTeamPaint paint;
        private SupplyPoint serving;
        private float fuel = -1.0f;
        private int rounds = -1;
        private bool looked;

        /// <summary>Seconds of running at full demand a full tank holds.</summary>
        public float FuelCapacity => fuelCapacity;

        /// <summary>Fuel left, in seconds of running at full demand.</summary>
        public float Fuel => fuel < 0.0f ? fuelCapacity : fuel;

        /// <summary>Fuel left as a fraction of a full tank, in 0..1.</summary>
        public float FuelFraction
            => fuelCapacity <= 0.0f ? 1.0f : Mathf.Clamp01(Fuel / fuelCapacity);

        /// <summary>Whether the tank is empty and the vehicle is going nowhere.</summary>
        public bool IsStranded => Fuel <= 0.0f;

        /// <summary>Rounds carried when full.</summary>
        public int RoundsCarried => roundsCarried;

        /// <summary>Rounds left.</summary>
        public int Rounds => rounds < 0 ? roundsCarried : rounds;

        /// <summary>Rounds left as a fraction of a full load, in 0..1.</summary>
        public float RoundsFraction
            => roundsCarried <= 0 ? 1.0f : Mathf.Clamp01((float)Rounds / roundsCarried);

        /// <summary>Whether the gun has nothing left to fire.</summary>
        public bool IsOutOfAmmunition => roundsCarried > 0 && Rounds <= 0;

        /// <summary>The point currently refilling this vehicle, or null when it is on none.</summary>
        public SupplyPoint Serving => serving;

        /// <summary>Whether this vehicle has to land before anything will serve it.</summary>
        /// <remarks>
        /// Looked up on first use rather than only on <c>Awake</c>, so that a prefab on
        /// disk can be asked the same question a vehicle in a scene can. Nothing has woken
        /// up in an asset, and "does this thing fly" is a fact about the asset either way.
        /// </remarks>
        public bool IsAircraft
        {
            get
            {
                if (!looked)
                {
                    looked = true;
                    flyer = GetComponent<Helicopter>();
                }

                return flyer != null;
            }
        }

        /// <summary>Which side this vehicle is on, for deciding whose bunker will serve it.</summary>
        public Team Team => paint == null ? Team.None : paint.Team;

        /// <summary>What the ground under this vehicle multiplies the engine's demand by.</summary>
        /// <remarks>
        /// One for anything that is not on the ground, which is the helicopter and anything
        /// assembled without a <see cref="GroundVehicle"/> on it. Nothing here asks what kind
        /// of vehicle this is: the thing that samples the map is the thing that drives on it,
        /// so an aircraft has no surface to be charged for and does not have to be excluded
        /// from being charged.
        /// </remarks>
        public float GroundDraw => driver == null ? 1.0f : driver.Underfoot.FuelDraw;

        /// <summary>
        /// Sets the size of both pools and fills them.
        /// </summary>
        /// <param name="capacity">Seconds of running at full demand a full tank holds.</param>
        /// <param name="idleDraw">Fuel drawn standing still, as a fraction of the full draw.</param>
        /// <param name="carried">Rounds carried when full.</param>
        /// <remarks>Called by the prefab builder, from the two tuning tables.</remarks>
        public void Configure(float capacity, float idleDraw, int carried)
        {
            fuelCapacity = Mathf.Max(0.0f, capacity);
            idleFuelDraw = Mathf.Clamp01(idleDraw);
            roundsCarried = Mathf.Max(0, carried);
            FillUp();
        }

        /// <summary>
        /// Fills both pools, which is what leaving the bunker does.
        /// </summary>
        public void FillUp()
        {
            fuel = fuelCapacity;
            rounds = roundsCarried;
        }

        /// <summary>
        /// Empties the tank, for a test that would rather not drive around for a minute.
        /// </summary>
        public void RunDry() => fuel = 0.0f;

        /// <summary>
        /// Empties the ammunition box, for the same reason.
        /// </summary>
        public void FireOff() => rounds = 0;

        /// <summary>
        /// Burns fuel for a stretch of running.
        /// </summary>
        /// <param name="demand">How hard the vehicle is working, 0..1.</param>
        /// <param name="seconds">How long it worked for.</param>
        /// <remarks>
        /// Public so the burn can be checked without a vehicle to drive. The pool never
        /// goes below zero: a tank that owed fuel would take longer to refill the longer it
        /// had been empty. This is the burn on ground that costs nothing extra; the running
        /// vehicle uses the overload and hands in what it is driving on.
        /// </remarks>
        public void Burn(float demand, float seconds) => Burn(demand, 1.0f, seconds);

        /// <summary>
        /// Burns fuel for a stretch of running over ground of a given thirst.
        /// </summary>
        /// <param name="demand">How hard the vehicle is working, 0..1.</param>
        /// <param name="surfaceDraw">
        /// What the ground multiplies the working part of the draw by; one for ground that
        /// costs nothing extra. From <see cref="IronFlag.Levels.SurfaceTuning.FuelDraw"/>.
        /// </param>
        /// <param name="seconds">How long it worked for.</param>
        public void Burn(float demand, float surfaceDraw, float seconds)
            => fuel = Mathf.Max(
                0.0f,
                Fuel - (DrawFor(demand, idleFuelDraw, surfaceDraw) * Mathf.Max(0.0f, seconds)));

        /// <summary>
        /// Takes one round out of the box, if there is one.
        /// </summary>
        /// <returns><c>true</c> when a round was available and has been spent.</returns>
        /// <remarks>
        /// A supply that was never told what it is carrying does not stop anyone firing.
        /// That is the same answer as having no supply component at all, and it means a
        /// half-configured rig fails loudly on the thing it is testing rather than quietly
        /// on ammunition.
        /// </remarks>
        public bool TryTakeRound()
        {
            if (roundsCarried <= 0)
            {
                return true;
            }

            if (Rounds <= 0)
            {
                return false;
            }

            rounds = Rounds - 1;
            return true;
        }

        /// <summary>
        /// Returns the fuel drawn per second at a given level of demand.
        /// </summary>
        /// <param name="demand">How hard the vehicle is working, 0..1.</param>
        /// <param name="idleDraw">Draw while doing nothing, as a fraction of the full draw.</param>
        /// <returns>Fuel per second, where one is the flat-out figure.</returns>
        /// <remarks>
        /// A straight line between the idle draw and one. The design document asks for fuel
        /// that depletes with use <em>and</em> with time, and a line through both is the
        /// simplest thing that is true of both - a curve nobody can see is not worth the
        /// paragraph it would take to explain it.
        /// </remarks>
        public static float DrawFor(float demand, float idleDraw)
            => DrawFor(demand, idleDraw, 1.0f);

        /// <summary>
        /// Returns the fuel drawn per second at a given level of demand over given ground.
        /// </summary>
        /// <param name="demand">How hard the vehicle is working, 0..1.</param>
        /// <param name="idleDraw">Draw while doing nothing, as a fraction of the full draw.</param>
        /// <param name="surfaceDraw">
        /// What the ground multiplies the working part of the draw by. One is ground that
        /// costs nothing extra; sand is more and a road is less.
        /// </param>
        /// <returns>Fuel per second, where one is the flat-out figure on ordinary ground.</returns>
        /// <remarks>
        /// <para>
        /// The ground scales the <em>working</em> half of the line and not the idle half,
        /// which is the difference between a true statement and a tidy one. An engine turning
        /// over is doing the same work whatever it is parked on, and charging a vehicle extra
        /// for standing still on a beach would also quietly retune every depot, because a
        /// refuelling rate is read against the draw it is racing.
        /// </para>
        /// <para>
        /// Scaling the demand instead would be worse than untidy: demand is clamped into
        /// 0..1, so a thirstier surface would cost nothing at all at full throttle - which is
        /// exactly where it should cost the most.
        /// </para>
        /// </remarks>
        public static float DrawFor(float demand, float idleDraw, float surfaceDraw)
        {
            float idle = Mathf.Clamp01(idleDraw);
            float working = Mathf.Clamp01(demand) * Mathf.Max(0.0f, surfaceDraw);
            return idle + ((1.0f - idle) * working);
        }

        /// <summary>
        /// Returns how hard one frame of pilot intent is working the engine.
        /// </summary>
        /// <param name="input">The frame of intent.</param>
        /// <returns>Demand in 0..1.</returns>
        /// <remarks>
        /// The larger of the throttle and the collective, so a helicopter climbing costs
        /// what a helicopter accelerating costs. Steering is deliberately free: a tank
        /// turning on the spot is not doing the work a tank crossing the map is, and
        /// charging for it would make the wheeled jeep the cheapest thing to drive badly.
        /// </remarks>
        public static float DemandFrom(VehicleInput input)
            => Mathf.Clamp01(Mathf.Max(Mathf.Abs(input.Throttle), Mathf.Abs(input.Lift)));

        /// <summary>
        /// Returns the intent left to a pilot whose tank is empty.
        /// </summary>
        /// <param name="input">What the pilot asked for.</param>
        /// <param name="aircraft">Whether this is the helicopter.</param>
        /// <returns>The same intent with the engine taken out of it.</returns>
        /// <remarks>
        /// The turret and the trigger survive, because the design document's stranded
        /// vehicle can still fight where it stands. The collective does not: an aircraft
        /// with no fuel sinks, which puts a dead helicopter on the ground instead of
        /// leaving it hanging over the map as permanent cover.
        /// </remarks>
        public static VehicleInput Stranded(VehicleInput input, bool aircraft)
            => new VehicleInput(Vector2.zero, input.Aim, aircraft ? -1.0f : 0.0f, input.Fire);

        private void Awake()
        {
            vehicle = GetComponent<VehicleController>();
            driver = vehicle as GroundVehicle;
            health = GetComponent<VehicleHealth>();
            paint = GetComponent<VehicleTeamPaint>();

            if (fuel < 0.0f)
            {
                FillUp();
            }
        }

        private void Update()
        {
            float seconds = Time.deltaTime;

            serving = SupplyPoint.ServingAt(transform.position, Team, IsAircraft);
            if (serving != null)
            {
                TakeOnSupplies(serving, seconds);
            }

            if (vehicle == null || !vehicle.isActiveAndEnabled
                || (health != null && health.IsDestroyed))
            {
                // Stowed in the bunker, or burning on the field. Either way the engine is
                // not running, and a vehicle that is not running burns nothing.
                return;
            }

            Burn(DemandFrom(vehicle.CurrentInput), GroundDraw, seconds);

            if (IsStranded)
            {
                vehicle.SetInput(Stranded(vehicle.CurrentInput, IsAircraft));
            }
        }

        /// <summary>
        /// Puts one frame's worth of a point's output into both pools.
        /// </summary>
        /// <param name="point">The point serving this vehicle.</param>
        /// <param name="seconds">Length of the frame.</param>
        /// <remarks>
        /// Rounds are whole things, and at sixty frames a second a depot owes a jeep less
        /// than a tenth of a grenade per frame. Rounding that down would refill nothing at
        /// all for ever, so a point that owes anything hands over at least one round - which
        /// makes a small load fill faster than its rate says, and is why the rate is written
        /// for the pool it is slowest on.
        /// </remarks>
        private void TakeOnSupplies(SupplyPoint point, float seconds)
        {
            if (point.FuelRate > 0.0f && Fuel < fuelCapacity)
            {
                fuel = Mathf.Min(fuelCapacity, Fuel + (point.FuelRate * fuelCapacity * seconds));
            }

            if (point.AmmoRate > 0.0f && Rounds < roundsCarried)
            {
                int taken = Mathf.Max(1, Mathf.RoundToInt(point.AmmoRate * roundsCarried * seconds));
                rounds = Mathf.Min(roundsCarried, Rounds + taken);
            }
        }
    }
}
