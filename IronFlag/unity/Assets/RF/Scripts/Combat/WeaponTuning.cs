using System;
using UnityEngine;
using IronFlag.Vehicles;

namespace IronFlag.Combat
{
    /// <summary>
    /// The numbers that make one vehicle's gun different from the next one's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The companion to <see cref="VehicleTuning"/>, and it lives in code for the same
    /// reason: four weapons are balanced by reading their rows against each other, and a
    /// table in one file can be reviewed in a diff in a way four assets cannot. The prefab
    /// builder stamps a copy onto each vehicle, so the numbers stay editable in the
    /// inspector while playing; rebuilding the prefabs resets them to this table.
    /// </para>
    /// <para>
    /// Ammunition <em>is</em> here, as of M4: <see cref="Rounds"/> is what a full vehicle
    /// carries, and it is the second thing limiting a gun after its
    /// <see cref="ShotInterval"/>. The two are read together - what a magazine is worth is
    /// <see cref="SustainedFire"/> seconds of holding the trigger down, and that is the
    /// number the four are balanced against each other on.
    /// </para>
    /// <para>
    /// Reach is the number to reach for first. Every weapon here is shorter than it looks
    /// on paper because the camera is the real constraint: it shows roughly thirty metres
    /// ahead of the vehicle it is following, and a weapon that outranges the screen is one
    /// whose hits the player never sees. The tank is deliberately right at that edge and
    /// everything else is inside it.
    /// </para>
    /// <para>
    /// Damage is in hit points, speeds in metres per second, distances in metres and
    /// intervals in seconds.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class WeaponTuning
    {
        /// <summary>Which weapon this is.</summary>
        [Tooltip("Which weapon this is. Decides the projectile prefab the mount fires.")]
        public WeaponKind Kind = WeaponKind.None;

        /// <summary>Hit points taken off by a direct hit.</summary>
        [Tooltip("Hit points taken off by a direct hit.")]
        public float Damage = 10.0f;

        /// <summary>Radius the blast reaches; zero hurts only what was hit.</summary>
        [Tooltip("Blast radius in metres. Zero means the round only hurts what it hits.")]
        public float SplashRadius = 0.0f;

        /// <summary>Speed the round leaves the barrel at.</summary>
        [Tooltip("Muzzle speed in m/s.")]
        public float MuzzleSpeed = 60.0f;

        /// <summary>Seconds between shots.</summary>
        [Tooltip("Seconds between shots. Read it against Rounds: the two are the whole economy.")]
        public float ShotInterval = 1.0f;

        /// <summary>How far the round carries across the map before it gives up.</summary>
        /// <remarks>
        /// A distance on the map, not through the air, because that is the only distance
        /// this game has - see <see cref="CombatPlane"/>. It is also the number every weapon
        /// is balanced on, so it is authored here and the launch angle is solved from it
        /// rather than the other way round.
        /// </remarks>
        [Tooltip("Reach across the map in metres. The launch angle is solved from it.")]
        public float Range = 30.0f;

        /// <summary>Downward acceleration on the round; zero flies flat.</summary>
        [Tooltip("Downward acceleration in m/s^2. Zero flies flat; above zero lobs.")]
        public float Drop = 0.0f;

        /// <summary>Angle above the horizon a lobbed round is launched at.</summary>
        /// <remarks>
        /// Solved from <see cref="Range"/> by <see cref="ProjectileMotion.LaunchElevation"/>,
        /// so the arc a player watches comes down exactly where the reach says it does. A
        /// flat-flying round ignores this and is aimed per shot instead, because how far it
        /// has to nose down depends on how high the muzzle firing it happens to be.
        /// </remarks>
        [Tooltip("Launch angle for a lobbed round. Solved from Range; only useful with Drop.")]
        public float ElevationDegrees = 0.0f;

        /// <summary>Radius of the round, for both the sweep it does and the size it draws.</summary>
        [Tooltip("Radius of the round in metres, for both collision and how big it looks.")]
        public float Radius = 0.15f;

        /// <summary>How far a round travels before its fuze arms.</summary>
        /// <remarks>
        /// <para>
        /// Nothing can be hit inside this distance: the round passes through whatever is
        /// there and carries on. It exists because a round is resolved as a
        /// <see cref="CombatPlane"/> column, and a column has no idea that the thing at its
        /// near end is thirty metres <em>below</em> the barrel rather than in front of it. A
        /// helicopter hovering over a building was detonating its own burst on the roof it
        /// was passing over, which reads as the gun being broken rather than as a rule.
        /// </para>
        /// <para>
        /// It is a real cost, not a fudge: the two weapons that carry one cannot fight at
        /// contact range at all, which is the price of being the most mobile vehicle on the
        /// field and of carrying the heaviest warhead. The two guns fired from near hull
        /// height have no such problem and no arming distance.
        /// </para>
        /// </remarks>
        [Tooltip("Metres a round travels before it can hit anything. Zero arms at the muzzle.")]
        public float ArmingDistance = 0.0f;

        /// <summary>Rounds a vehicle carries when it leaves the bunker full.</summary>
        /// <remarks>
        /// <para>
        /// Read this as <see cref="SustainedFire"/> rather than as a count: thirty seconds
        /// of trigger is thirty seconds whether it is twelve rockets or two hundred and
        /// forty chaingun rounds, and it is the only figure on which four guns firing at
        /// eight times each other's rate can be compared at all.
        /// </para>
        /// <para>
        /// A gun with no <see cref="IronFlag.Supply.VehicleSupply"/> behind it never runs
        /// out, which is what a vehicle assembled in a test is: a rig should not need an
        /// ammunition economy bolted to it before it can fire a shot.
        /// </para>
        /// </remarks>
        [Tooltip("Rounds carried when full. Times ShotInterval is the seconds of fire it buys.")]
        public int Rounds = 30;

        /// <summary>Whether this tuning describes a gun at all.</summary>
        public bool Exists => Kind != WeaponKind.None && Damage > 0.0f;

        /// <summary>The band this weapon can actually hit in, in metres.</summary>
        /// <remarks>
        /// A weapon whose fuze arms after its reach runs out can never hit anything, which
        /// is the one way these two numbers can be set against each other.
        /// </remarks>
        public float EffectiveRange => Mathf.Max(0.0f, Range - ArmingDistance);

        /// <summary>Shots per second with the trigger held down.</summary>
        public float RoundsPerSecond => ShotInterval <= 0.0f ? 0.0f : 1.0f / ShotInterval;

        /// <summary>Damage per second with the trigger held down and every round hitting.</summary>
        public float DamagePerSecond => Damage * RoundsPerSecond;

        /// <summary>Seconds of holding the trigger down a full load of ammunition buys.</summary>
        public float SustainedFire => Rounds * ShotInterval;

        /// <summary>Damage a full load is worth, if every round of it hits something.</summary>
        public float LoadDamage => Rounds * Damage;

        /// <summary>
        /// Returns the weapon one vehicle carries, as a fresh instance the caller may edit.
        /// </summary>
        /// <param name="kind">Vehicle to look up.</param>
        /// <returns>
        /// A new tuning carrying that vehicle's weapon. <see cref="VehicleKind.None"/>
        /// returns a tuning with no weapon in it, which mounts nothing.
        /// </returns>
        /// <example>
        /// <code>
        /// WeaponTuning gun = WeaponTuning.For(VehicleKind.Tank);
        /// float reach = gun.Range;
        /// </code>
        /// </example>
        public static WeaponTuning For(VehicleKind kind)
        {
            switch (kind)
            {
                // Fourteen metres, which is nothing: the jeep is the fastest thing on the
                // field and the only one that can take the flag, so it is not also allowed
                // to fight at a distance. It has to be almost touching what it wants to
                // hit. The lob is what makes that reach look like a choice rather than a
                // bug - a round you can see arc out and fall short is a different feeling
                // from one that stops in mid-air. Twenty-four of them, which is the
                // shortest sortie in the game: the jeep is not out there to fight.
                case VehicleKind.Jeep:
                    return Lobbed(
                        WeaponKind.Grenade,
                        damage: 22.0f,
                        splashRadius: 4.0f,
                        muzzleSpeed: 28.0f,
                        shotInterval: 1.0f,
                        range: 14.0f,
                        drop: 22.0f,
                        radius: 0.18f,
                        rounds: 24);

                // The general-purpose damage dealer: the longest reach in the game, which
                // is to say as far as the camera can see and not a metre more, on a turret
                // that can hold a target while the hull repositions. No arming distance -
                // it fires from 1.9 m, so its column barely reaches anything its barrel is
                // not already pointing at, and a heavy that cannot defend itself at contact
                // range would be a worse problem than the one arming it would solve.
                case VehicleKind.Tank:
                    return new WeaponTuning
                    {
                        Kind = WeaponKind.Cannon,
                        Damage = 34.0f,
                        SplashRadius = 1.5f,
                        MuzzleSpeed = 70.0f,
                        ShotInterval = 1.5f,
                        Range = 36.0f,
                        Drop = 0.0f,
                        ElevationDegrees = 0.0f,
                        Radius = 0.16f,
                        // Thirty seconds of trigger, which is the figure the other three
                        // are set against: the tank is the general-purpose one, so it is
                        // the one whose endurance reads as normal.
                        Rounds = 20,
                    };

                // The heaviest single hit in the game - one rocket ends a jeep or a
                // helicopter outright - on the slowest round, at a speed you can watch
                // coming and drive out of. Slow vehicle, slow gun, enormous payload.
                case VehicleKind.Asv:
                    return new WeaponTuning
                    {
                        Kind = WeaponKind.Rocket,
                        Damage = 55.0f,
                        SplashRadius = 4.5f,
                        MuzzleSpeed = 30.0f,
                        ShotInterval = 2.5f,
                        Range = 30.0f,
                        Drop = 0.0f,
                        ElevationDegrees = 0.0f,
                        Radius = 0.22f,
                        // Six metres of it is spent arming, which is most of the length of
                        // the vehicle firing it. A four-and-a-half-metre blast going off an
                        // arm's length from the launcher is not a shot anybody meant to take.
                        ArmingDistance = 6.0f,
                        // Twelve rockets is six kills if none of them misses, on the vehicle
                        // that can least afford to chase anything down to take the shot
                        // again. The same thirty seconds as the tank, spent far more slowly.
                        Rounds = 12,
                    };

                // Small rounds fired faster than anything else, which adds up to the
                // highest sustained damage on the field. It is paid for twice: the
                // helicopter has the least armor of the four, and it is the one vehicle
                // that cannot rearm anywhere but at home.
                case VehicleKind.Helicopter:
                    return new WeaponTuning
                    {
                        Kind = WeaponKind.Chaingun,
                        Damage = 4.0f,
                        SplashRadius = 0.0f,
                        MuzzleSpeed = 110.0f,
                        ShotInterval = 0.125f,
                        Range = 26.0f,
                        Drop = 0.0f,
                        ElevationDegrees = 0.0f,
                        Radius = 0.10f,
                        // The longest arming distance in the game, because the helicopter is
                        // the only thing that fires from thirty metres above what it is
                        // shooting at. Eight metres clears whatever it happens to be flying
                        // over - a building is ten across - and leaves it eighteen to fight
                        // in. Hovering on top of someone is not a firing position.
                        ArmingDistance = 8.0f,
                        // The other half of the price of the highest sustained damage in the
                        // game. Two hundred and forty rounds is thirty seconds of trigger -
                        // the same as everyone else - but the helicopter is the one vehicle
                        // that cannot rearm at a field depot, so running dry means flying
                        // all the way home.
                        Rounds = 240,
                    };

                default:
                    return new WeaponTuning { Kind = WeaponKind.None, Damage = 0.0f, Rounds = 0 };
            }
        }

        /// <summary>
        /// Returns the gun an automated turret carries.
        /// </summary>
        /// <returns>A new tuning the caller may edit.</returns>
        /// <remarks>
        /// <para>
        /// Its own entry point rather than a row in <see cref="For(VehicleKind)"/>, because
        /// that table answers "what does this vehicle carry" and there is no vehicle here.
        /// </para>
        /// <para>
        /// Read it against the tank, which is what a player brings to remove one. Twenty
        /// metres of reach is well inside the tank's thirty-six - sixteen metres of standoff
        /// the tank and nothing else in the roster has - so an emplacement can be fought from
        /// outside its own range by the one vehicle equipped to, and is something the jeep
        /// and the helicopter have to close with instead. No blast, so it hurts exactly what
        /// it hits and a turret cannot clear the ground around itself.
        /// </para>
        /// <para>
        /// Fifteen a hit, once every second and a half - the tank's own cadence, for under
        /// half the tank's damage. Every round is worth feeling: three of them end a
        /// helicopter and four end a jeep, so a hit is an event rather than a tick, and the
        /// second and a half between them is the room a driver has to do something about
        /// it. Ten damage a second is what that adds up to, a tenth of a tank's hit points
        /// for every second it spends inside twenty metres - which makes an emplacement
        /// something the tank can close with and beat rather than something it has to
        /// outrange. Five cannon shells is six seconds of standing there and four or five
        /// rounds taken, sixty to seventy-five of its hundred. The jeep still cannot: eight
        /// grenades is eight seconds parked fourteen metres out, and six rounds is ninety
        /// damage against fifty hit points. That gap is the whole reason for the rate.
        /// </para>
        /// <para>
        /// It used to fire three times a second for twelve, which is thirty-six damage a
        /// second and a dead tank in under three. That is not a defence anybody plans
        /// around; it is a line on the map, and the only decision it leaves is which
        /// vehicle can shoot from outside it. The slow gun is worth more precisely because
        /// it can be walked into and answered - and it is loud about the attempt, because
        /// <see cref="IronFlag.Destruction.AutoTurret.WatchMargin"/> has the barrel come
        /// round twelve metres before the first round could be fired.
        /// </para>
        /// <para>
        /// It carries no <see cref="Rounds"/> that matter, because a turret has no
        /// <see cref="IronFlag.Supply.VehicleSupply"/> behind it and never runs out. That is
        /// deliberate: an emplacement that could be emptied by driving past it out of range
        /// would be a puzzle with one answer, and the thing that stops a turret is shooting
        /// it.
        /// </para>
        /// </remarks>
        public static WeaponTuning Emplacement()
            => new WeaponTuning
            {
                Kind = WeaponKind.Autocannon,
                Damage = 15.0f,
                SplashRadius = 0.0f,
                MuzzleSpeed = 90.0f,
                ShotInterval = 1.5f,
                Range = 20.0f,
                Drop = 0.0f,
                ElevationDegrees = 0.0f,
                Radius = 0.12f,
                // It fires from about two metres up at things on the ground beside it, so a
                // column starting at the muzzle is already past anything underneath. Nothing
                // to arm around, unlike the two guns fired from above the map.
                ArmingDistance = 0.0f,
                Rounds = 0,
            };

        /// <summary>
        /// Builds a weapon that throws its rounds along an arc, angled to reach exactly as
        /// far as it claims to.
        /// </summary>
        /// <param name="kind">Which weapon this is.</param>
        /// <param name="damage">Hit points taken off by a direct hit.</param>
        /// <param name="splashRadius">Blast radius in metres.</param>
        /// <param name="muzzleSpeed">Speed the round leaves the barrel at.</param>
        /// <param name="shotInterval">Seconds between shots.</param>
        /// <param name="range">How far the round should carry across the map.</param>
        /// <param name="drop">Downward acceleration on the round.</param>
        /// <param name="radius">Radius of the round.</param>
        /// <param name="rounds">Rounds carried when full.</param>
        /// <returns>
        /// The tuning, with <see cref="ElevationDegrees"/> solved rather than stated.
        /// </returns>
        /// <remarks>
        /// Reach is authored and the angle is derived, because reach is what the roster is
        /// balanced on and an angle is only ever a way of getting one. Solving it here is
        /// what stops the arc a player watches from disagreeing with the distance the weapon
        /// claims; a steeper launch also flies higher, and the higher the arc the further
        /// the drawn round strays from the column that actually decides the hit.
        /// </remarks>
        private static WeaponTuning Lobbed(
            WeaponKind kind,
            float damage,
            float splashRadius,
            float muzzleSpeed,
            float shotInterval,
            float range,
            float drop,
            float radius,
            int rounds)
        {
            return new WeaponTuning
            {
                Kind = kind,
                Damage = damage,
                SplashRadius = splashRadius,
                MuzzleSpeed = muzzleSpeed,
                ShotInterval = shotInterval,
                Range = range,
                Drop = drop,
                ElevationDegrees = ProjectileMotion.LaunchElevation(muzzleSpeed, range, drop),
                Radius = radius,
                Rounds = rounds,
            };
        }
    }
}
