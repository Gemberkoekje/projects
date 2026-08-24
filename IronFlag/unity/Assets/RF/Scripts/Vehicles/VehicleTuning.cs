using System;
using UnityEngine;

namespace IronFlag.Vehicles
{
    /// <summary>
    /// The numbers that make one vehicle different from the next: how it handles, how much
    /// it can take, and how long it can stay out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The values live in code, in <see cref="For"/>, rather than in a per-vehicle asset:
    /// the roster is balanced by reading the four rows against each other, and a table in
    /// one file is reviewable in a diff in a way four YAML assets are not. The prefab
    /// builder stamps a copy onto each prefab, so the numbers stay editable in the
    /// inspector while playing; rebuilding the prefabs resets them to this table.
    /// </para>
    /// <para>
    /// Speeds are metres per second, rates degrees per second, accelerations metres per
    /// second squared, and fuel is in seconds of running. The gun a vehicle carries is the
    /// one thing not here: see <see cref="IronFlag.Combat.WeaponTuning.For"/>, which is a
    /// table of the same shape and which is also where the rounds it carries live.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class VehicleTuning
    {
        /// <summary>Top speed driving forwards.</summary>
        [Tooltip("Top speed driving forwards, in m/s.")]
        public float MaxSpeed = 10.0f;

        /// <summary>Top reverse speed, as a fraction of <see cref="MaxSpeed"/>.</summary>
        [Range(0.0f, 1.0f)]
        [Tooltip("Top reverse speed as a fraction of the forward top speed.")]
        public float ReverseFraction = 0.45f;

        /// <summary>How quickly the throttle builds speed.</summary>
        [Tooltip("How quickly the throttle builds speed, in m/s^2.")]
        public float Acceleration = 6.0f;

        /// <summary>How quickly releasing or reversing the throttle sheds speed.</summary>
        [Tooltip("How quickly the vehicle sheds speed when braking or coasting, in m/s^2.")]
        public float Braking = 12.0f;

        /// <summary>Yaw rate at full steering authority.</summary>
        [Tooltip("Yaw rate at full steering authority, in degrees per second.")]
        public float TurnRate = 80.0f;

        /// <summary>Whether the vehicle can rotate on the spot.</summary>
        /// <remarks>
        /// True for tracked and airborne vehicles, which turn by driving one side against
        /// the other. False for the wheeled jeep, which has to be rolling to steer - that
        /// difference is most of why the jeep feels like a car and the tank does not.
        /// </remarks>
        [Tooltip("Tracked and airborne vehicles rotate on the spot; wheeled ones must be rolling.")]
        public bool PivotTurn = false;

        /// <summary>Speed at which a non-pivoting vehicle reaches full steering authority.</summary>
        [Tooltip("Speed at which a wheeled vehicle steers at its full turn rate, in m/s.")]
        public float SteerReferenceSpeed = 5.0f;

        /// <summary>How much of the ground's grip this vehicle actually feels.</summary>
        /// <remarks>
        /// <para>
        /// The ground under a vehicle multiplies its top speed, its acceleration and its
        /// turn rate - see <see cref="IronFlag.Levels.SurfaceTuning.Grip"/> - and this is how
        /// much of that multiplication reaches this particular vehicle. One is at the mercy
        /// of what it is driving on; zero ignores it entirely. Anything between is a straight
        /// line towards the surface's figure, which is
        /// <see cref="GroundVehicleMotion.Traction"/>.
        /// </para>
        /// <para>
        /// One column rather than a surface-by-vehicle matrix: four numbers instead of
        /// twenty, and it says something true - the wheeled jeep is at the mercy of what it
        /// is driving on and the tracked tank is not. That is the same distinction
        /// <see cref="PivotTurn"/> already draws, and it is the project's habit: the
        /// helicopter is unmoved by the ground because of a number about how much terrain it
        /// feels, not because of a check on its type.
        /// </para>
        /// <para>
        /// It weighs <em>grip</em> and nothing else. A surface's thirst - see
        /// <see cref="IronFlag.Levels.SurfaceTuning.FuelDraw"/> - is paid in full by
        /// everything that drives on it, because the two are different claims: grip is about
        /// how a vehicle puts its power down, which is what tracks are for, and thirst is
        /// about how much power the ground demands, which soft sand demands of a tank as
        /// readily as of a jeep.
        /// </para>
        /// </remarks>
        [Range(0.0f, 1.0f)]
        [Tooltip("How much of the ground's grip this vehicle feels. 1 is at its mercy, 0 ignores it.")]
        public float SurfaceSensitivity = 0.0f;

        /// <summary>Rigidbody mass, which decides who wins a collision.</summary>
        [Tooltip("Rigidbody mass in kg. The relative values are what matter, not the absolute ones.")]
        public float Mass = 2000.0f;

        /// <summary>Traverse rate of the turret or launcher; zero when there is none.</summary>
        [Tooltip("Turret or launcher traverse rate, in degrees per second. Zero means no turret.")]
        public float TurretTurnRate = 0.0f;

        /// <summary>How much damage the vehicle absorbs before it is wrecked.</summary>
        /// <remarks>
        /// Armor is a column of the design document's roster table, next to speed and
        /// weapon, so it is a column of this one. Read it against
        /// <see cref="IronFlag.Combat.WeaponTuning.For"/>: what matters is not the number
        /// but how many of somebody else's rounds it survives.
        /// </remarks>
        [Tooltip("Damage the vehicle absorbs before it is wrecked, in hit points.")]
        public float HitPoints = 100.0f;

        /// <summary>How long the vehicle can run flat out on a full tank, in seconds.</summary>
        /// <remarks>
        /// <para>
        /// Fuel is measured in seconds rather than in litres because seconds are the only
        /// unit anybody balances in: what matters about a tank of fuel is how long a sortie
        /// it pays for, and a number that says so directly cannot be misread. One unit is
        /// one second of running at full demand, so this figure is also the endurance at
        /// full throttle - multiply by <see cref="MaxSpeed"/> for the range in metres.
        /// </para>
        /// <para>
        /// Refuelling rates are fractions of it per second rather than absolute figures,
        /// which is what lets one depot serve four pools this different. See
        /// <see cref="IronFlag.Supply.SupplyPoint"/>.
        /// </para>
        /// </remarks>
        [Tooltip("Seconds of running at full demand on a full tank. Times MaxSpeed is the range.")]
        public float FuelCapacity = 120.0f;

        /// <summary>Fuel drawn while standing still, as a fraction of the flat-out draw.</summary>
        /// <remarks>
        /// The design document has fuel deplete with use <em>and</em> time, and this is the
        /// time half: an engine that is running costs something even when the vehicle is
        /// not going anywhere. It is small for the three that drive and most of the way to
        /// one for the helicopter, because a helicopter holding a hover is doing nearly as
        /// much work as one crossing the map - which is the whole reason the roster table
        /// lists "must return to refuel" as its drawback.
        /// </remarks>
        [Range(0.0f, 1.0f)]
        [Tooltip("Fuel drawn standing still, as a fraction of the flat-out draw.")]
        public float IdleFuelDraw = 0.15f;

        /// <summary>Top speed in reverse, in metres per second.</summary>
        public float ReverseSpeed => MaxSpeed * ReverseFraction;

        /// <summary>Metres the vehicle covers flat out on a full tank.</summary>
        public float FuelRange => MaxSpeed * FuelCapacity;

        /// <summary>Seconds a full tank lasts with the vehicle sitting still.</summary>
        /// <remarks>
        /// The number that matters to a helicopter, which spends its fuel holding station
        /// rather than travelling, and the one that decides how long a stranded pilot can
        /// keep fighting before the tank they are hiding behind matters more than the map.
        /// </remarks>
        public float IdleEndurance
            => IdleFuelDraw <= 0.0f ? Mathf.Infinity : FuelCapacity / IdleFuelDraw;

        /// <summary>
        /// Returns the tuning for one vehicle, as a fresh instance the caller may edit.
        /// </summary>
        /// <param name="kind">Vehicle to look up.</param>
        /// <returns>
        /// A new tuning carrying that vehicle's numbers. <see cref="VehicleKind.None"/>
        /// returns the field defaults, which are deliberately unremarkable.
        /// </returns>
        /// <example>
        /// <code>
        /// VehicleTuning tuning = VehicleTuning.For(VehicleKind.Jeep);
        /// float topSpeed = tuning.MaxSpeed;
        /// </code>
        /// </example>
        public static VehicleTuning For(VehicleKind kind)
        {
            switch (kind)
            {
                // Fastest and twitchiest, and the only one that has to be rolling to
                // steer. Everything about the jeep rewards committing to a run - including
                // the acceleration, which is deliberately unrealistic: under a second to top
                // speed, because the jeep's whole job is to be somewhere else before anyone
                // can react, and a jeep that spends twenty metres getting up to speed reads
                // as heavy rather than as fast.
                case VehicleKind.Jeep:
                    return new VehicleTuning
                    {
                        MaxSpeed = 22.0f,
                        ReverseFraction = 0.45f,
                        Acceleration = 26.0f,
                        Braking = 34.0f,
                        TurnRate = 130.0f,
                        PivotTurn = false,
                        SteerReferenceSpeed = 5.0f,

                        // The anchor of the column, and the only one at its top. Four
                        // narrow tyres are all the jeep has, so a fifth off the grip is a
                        // fifth off the jeep: it is the vehicle a beach genuinely punishes
                        // and the one a road genuinely rewards, which is what makes the
                        // fastest line across the map a decision rather than a straight
                        // line.
                        SurfaceSensitivity = 1.0f,
                        Mass = 1200.0f,
                        TurretTurnRate = 0.0f,
                        HitPoints = 50.0f,
                        // Two kilometres flat out, which is eight crossings of the map: the
                        // jeep is the one vehicle that has to make a round trip with the
                        // flag on board, and running it dry on the way home would be a
                        // punishment for doing its job rather than for taking a risk.
                        FuelCapacity = 90.0f,
                        IdleFuelDraw = 0.15f,
                    };

                case VehicleKind.Tank:
                    return new VehicleTuning
                    {
                        MaxSpeed = 12.0f,
                        ReverseFraction = 0.5f,
                        Acceleration = 5.0f,
                        Braking = 9.0f,
                        TurnRate = 75.0f,
                        PivotTurn = true,
                        SteerReferenceSpeed = 1.0f,

                        // Lowest of the three that drive. Tracks spread six tonnes over
                        // enough ground that soft going is an inconvenience rather than a
                        // problem, so the tank crosses a beach at a twentieth off its speed
                        // where the jeep loses a fifth. It still pays the sand's thirst in
                        // full - see SurfaceSensitivity - so the beach costs it range
                        // rather than time.
                        SurfaceSensitivity = 0.25f,
                        Mass = 6000.0f,
                        TurretTurnRate = 65.0f,
                        HitPoints = 100.0f,
                        FuelCapacity = 150.0f,
                        IdleFuelDraw = 0.18f,
                    };

                case VehicleKind.Asv:
                    return new VehicleTuning
                    {
                        MaxSpeed = 9.0f,
                        ReverseFraction = 0.5f,
                        Acceleration = 3.5f,
                        Braking = 7.0f,
                        TurnRate = 60.0f,
                        PivotTurn = true,
                        SteerReferenceSpeed = 1.0f,

                        // Between the two, and nearer the tank. The ASV is tracked as well,
                        // but it is two-thirds of the tank's weight on a narrower footprint,
                        // so it feels the ground about twice as much as the tank does and
                        // less than half as much as the jeep.
                        SurfaceSensitivity = 0.45f,
                        Mass = 4000.0f,
                        TurretTurnRate = 45.0f,
                        HitPoints = 140.0f,
                        // The longest endurance in the game on the shortest legs. The ASV
                        // is where a side parks something that has to still be there in ten
                        // minutes, so it is the one vehicle that never has to think about
                        // the trip home.
                        FuelCapacity = 180.0f,
                        IdleFuelDraw = 0.18f,
                    };

                // Fast, but short of the jeep: the helicopter's advantage is the straight
                // line it can take, not the speed it takes it at.
                case VehicleKind.Helicopter:
                    return new VehicleTuning
                    {
                        MaxSpeed = 20.0f,
                        ReverseFraction = 0.4f,
                        Acceleration = 8.0f,
                        Braking = 10.0f,
                        TurnRate = 110.0f,
                        PivotTurn = true,
                        SteerReferenceSpeed = 1.0f,

                        // Zero, which is the design document's "ignores ground terrain"
                        // written as a number rather than as a check on a type. Nothing
                        // samples the ground under an aircraft in the first place - see
                        // Helicopter.FixedUpdate - so this is the belt to that pair of
                        // braces: a helicopter handed a surface by mistake would still fly
                        // exactly as it flies over the sea.
                        SurfaceSensitivity = 0.0f,
                        Mass = 1800.0f,
                        TurretTurnRate = 0.0f,
                        HitPoints = 40.0f,
                        // The drawback the design document's roster table gives the
                        // helicopter, spelled as two numbers: the smallest tank in the game,
                        // and an idle draw that is most of the full one because holding a
                        // hover is nearly as much work as crossing the map. Just under two
                        // minutes airborne even if it never goes anywhere, and it cannot
                        // take fuel from a field depot - see SupplyPoint.ServesAircraft.
                        FuelCapacity = 70.0f,
                        IdleFuelDraw = 0.55f,
                    };

                default:
                    return new VehicleTuning();
            }
        }
    }
}
