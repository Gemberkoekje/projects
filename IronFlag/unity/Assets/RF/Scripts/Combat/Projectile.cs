using System.Collections.Generic;
using UnityEngine;
using IronFlag.Audio;
using IronFlag.Core;
using IronFlag.Levels;
using IronFlag.Vfx;

namespace IronFlag.Combat
{
    /// <summary>
    /// One round in the air: it flies the arc <see cref="ProjectileMotion"/> gives it,
    /// sweeps the space it crosses for something to hit, and goes off.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rounds are not rigidbodies. They have no collider and take part in no collisions;
    /// each one moves itself and asks the physics engine what lies between where it was and
    /// where it now is. That is the only way a 140 m/s chaingun round can be reliable at a
    /// 50 Hz fixed step - a collider moving 2.8 m per step passes clean through a jeep
    /// without ever touching it, and the bug looks like "the helicopter's gun does nothing
    /// at close range" rather than like tunnelling.
    /// </para>
    /// <para>
    /// What it asks about is a <see cref="CombatPlane"/> column rather than a point, and it
    /// asks in two dimensions: the round is drawn following its own path through the air and
    /// resolved as a vertical column crossing the map. Read <see cref="CombatPlane"/> before
    /// changing any of this - the split between the two is the whole design, and collapsing
    /// it back into one three-dimensional round is exactly what made the tank shoot over the
    /// jeep and the helicopter hit nothing at all.
    /// </para>
    /// <para>
    /// What it can hurt is an <see cref="IDamageable"/> rather than a vehicle: M5's
    /// buildings answer the same three questions a hull does, and a round that knew the
    /// difference between a wall and a tank would need a third branch for whatever M7 puts
    /// on the map.
    /// </para>
    /// <para>
    /// The round is also what decides who it can hurt, which is one question
    /// (<see cref="Teams.IsHostile"/>) asked once: fire that cannot hurt a vehicle passes
    /// through it rather than stopping on it, so a player firing down their own start line
    /// neither wrecks their roster nor has their shots eaten by it.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Projectile")]
    public sealed class Projectile : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The visible part of the round. Scaled to the weapon's calibre when fired.")]
        private Transform visual;

        [SerializeField]
        [Tooltip("Explosion prefab spawned where the round goes off. Optional.")]
        private Explosion impact;

        [SerializeField]
        [Tooltip("Spark prefab thrown off a target that survives this round. Optional.")]
        private ImpactSparks sparks;

        [SerializeField]
        [Tooltip("Spray prefab put up when this round goes off in the water. Optional.")]
        private ParticleBurst splash;

        /// <summary>Reused sweep results, so a chaingun does not allocate per round.</summary>
        private static readonly RaycastHit[] SweepHits = new RaycastHit[32];

        /// <summary>Reused blast results, shared for the same reason.</summary>
        private static readonly Collider[] BlastHits = new Collider[32];

        /// <summary>Targets already damaged by the blast being resolved right now.</summary>
        private static readonly List<IDamageable> Damaged = new List<IDamageable>();

        private WeaponTuning weapon = new WeaponTuning();
        private ProjectileState state;
        private Transform owner;
        private Team team = Team.None;
        private float travelled;

        /// <summary>The weapon that fired this round.</summary>
        public WeaponTuning Weapon => weapon;

        /// <summary>Where the round is and how fast it is going.</summary>
        public ProjectileState State => state;

        /// <summary>Which side fired it.</summary>
        public Team Team => team;

        /// <summary>How far above the waterline a round still counts as hitting the sea.</summary>
        /// <remarks>
        /// A metre and a half. A flat-flying round is aimed down onto the
        /// <see cref="CombatPlane"/>, so one that expires over open water is already near the
        /// surface - and a round that goes off well above it hit something, which is the
        /// other half of the rule.
        /// </remarks>
        public const float SprayReach = 1.5f;

        /// <summary>The sparks this round throws off a target it fails to kill, or null.</summary>
        /// <remarks>
        /// Exposed for the reason <see cref="VehicleWeapon.Flash"/> is: so a test can tell a
        /// round built without one from a round whose wiring was dropped.
        /// </remarks>
        public ImpactSparks Sparks => sparks;

        /// <summary>The spray this round puts up when it lands in water, or null.</summary>
        public ParticleBurst Splash => splash;

        /// <summary>Whether the fuze has armed and the round can hit anything yet.</summary>
        public bool IsArmed => travelled >= weapon.ArmingDistance;

        /// <summary>How far this round has carried across the map, in metres.</summary>
        /// <remarks>
        /// Measured flat, because reach is a distance on the map. A round fired downwards
        /// out of a helicopter covers more ground through the air than across it, and it is
        /// the second number that <see cref="WeaponTuning.Range"/> is talking about.
        /// </remarks>
        public float Travelled => travelled;

        /// <summary>
        /// Puts a round in the air.
        /// </summary>
        /// <param name="prefab">Projectile prefab to instantiate; null does nothing.</param>
        /// <param name="launch">Where the round starts and how fast it is going.</param>
        /// <param name="tuning">The weapon that fired it, for damage, reach and calibre.</param>
        /// <param name="firedBy">Side that fired it, which decides what it can hurt.</param>
        /// <param name="firedFrom">
        /// Root of the vehicle that fired it, so the round ignores the hull it is inside
        /// when it is created. Null is allowed and means nothing is exempt.
        /// </param>
        /// <returns>The round, or <c>null</c> when there was no prefab to spawn.</returns>
        /// <example>
        /// <code>
        /// ProjectileState launch = ProjectileMotion.Launch(
        ///     muzzle.position, muzzle.forward, tuning.MuzzleSpeed, tuning.ElevationDegrees);
        /// Projectile.Fire(round, launch, tuning, Team.Green, transform);
        /// </code>
        /// </example>
        public static Projectile Fire(
            Projectile prefab,
            ProjectileState launch,
            WeaponTuning tuning,
            Team firedBy,
            Transform firedFrom)
        {
            if (prefab == null || tuning == null)
            {
                return null;
            }

            Projectile round = Instantiate(prefab, launch.Position, Facing(launch.Velocity));
            // A round built in a scene rather than saved as a prefab has to be switched off
            // to stop it flying away before anyone fires it, and Instantiate copies that.
            round.gameObject.SetActive(true);
            round.weapon = tuning;
            round.state = launch;
            round.team = firedBy;
            round.owner = firedFrom;
            round.travelled = 0.0f;

            if (round.visual != null)
            {
                round.visual.localScale = Vector3.one * (tuning.Radius * 2.0f);
            }

            return round;
        }

        /// <summary>
        /// Points this component at the parts the prefab builder generated for it.
        /// </summary>
        /// <param name="body">The visible part of the round.</param>
        /// <param name="explosion">Explosion prefab for the impact, or null for none.</param>
        /// <param name="shower">Spark prefab for a hit that does not kill, or null for none.</param>
        /// <param name="spray">Spray prefab for a hit on water, or null for none.</param>
        /// <remarks>Called by the prefab builder.</remarks>
        public void Configure(
            Transform body, Explosion explosion, ImpactSparks shower, ParticleBurst spray)
        {
            visual = body;
            impact = explosion;
            sparks = shower;
            splash = spray;
        }

        /// <summary>
        /// Returns whether a round that went off here should throw spray rather than fire.
        /// </summary>
        /// <param name="overWater">Whether the ground under the point is water.</param>
        /// <param name="struckSomething">Whether the round hit a target rather than expiring.</param>
        /// <param name="height">How high the round went off, in metres.</param>
        /// <param name="waterSurface">Height of the water surface, in metres.</param>
        /// <returns><c>true</c> when it went into the sea.</returns>
        /// <remarks>
        /// <para>
        /// Static and side-effect free, so the rule can be checked without a sea. All three
        /// conditions earn their place. Over water is obvious. <em>Not</em> having struck
        /// anything is what stops a helicopter shot down over the bay throwing up a splash
        /// from ten metres in the air - it was hit, so the round found a hull rather than the
        /// water. And the height is the backstop for the same case where the round hit
        /// nothing, since a shot that sails past a helicopter still expires up where it was
        /// aimed.
        /// </para>
        /// <para>
        /// Note this replaces the explosion rather than joining it. A shell landing in the
        /// sea and a shell landing on the beach beside it drew the same orange fireball
        /// before, which is simply wrong: the sea is the darkest thing on the map and the
        /// one place fire cannot happen.
        /// </para>
        /// </remarks>
        public static bool DrawsSpray(
            bool overWater, bool struckSomething, float height, float waterSurface)
            => overWater && !struckSomething && height <= waterSurface + SprayReach;

        private void FixedUpdate()
        {
            Vector3 from = state.Position;
            ProjectileState next = ProjectileMotion.Step(state, weapon.Drop, Time.fixedDeltaTime);

            Vector3 across = CombatPlane.Flatten(next.Position - from);
            float distance = across.magnitude;

            if (distance > 0.0f)
            {
                // Only the part of this step the fuze is armed for can hit anything. Doing
                // it per step rather than per round matters: a chaingun round covers over
                // two metres a step, and rounding the arming distance up to the next whole
                // step would move it by a quarter of itself.
                float asleep = Mathf.Clamp(weapon.ArmingDistance - travelled, 0.0f, distance);
                Vector3 direction = across / distance;
                Vector3 live = from + (direction * asleep);

                if (asleep < distance
                    && Sweep(live, direction, distance - asleep, out RaycastHit hit))
                {
                    // A sweep that starts already overlapping reports no useful point, so
                    // where the round armed is the honest answer for one that goes off the
                    // instant it does.
                    Detonate(
                        hit.distance <= 0.0f ? live : hit.point,
                        hit.collider.GetComponentInParent<IDamageable>());
                    return;
                }
            }

            state = next;
            travelled += distance;
            transform.SetPositionAndRotation(state.Position, Facing(state.Velocity));

            if (travelled >= weapon.Range)
            {
                Detonate(state.Position, null);
            }
        }

        /// <summary>
        /// Looks for the first thing this round can hit along one step of its flight.
        /// </summary>
        /// <param name="from">Where the step starts.</param>
        /// <param name="direction">Unit vector along the step, across the map.</param>
        /// <param name="distance">Length of the step across the map, in metres.</param>
        /// <param name="nearest">The hit, when there is one.</param>
        /// <returns><c>true</c> when the round hit something it can hit.</returns>
        /// <remarks>
        /// The round sweeps a <see cref="CombatPlane"/> column rather than a ball, so what it
        /// hits depends on where things are and never on how tall they are. Anything
        /// belonging to the vehicle that fired it is skipped - the muzzle is inside its own
        /// hull, so without that every shot would go off in the barrel - and so is anything
        /// on the same side, which is what makes friendly fire pass through rather than be
        /// absorbed.
        /// </remarks>
        private bool Sweep(Vector3 from, Vector3 direction, float distance, out RaycastHit nearest)
        {
            nearest = default;
            CombatPlane.Column(from, out Vector3 bottom, out Vector3 top);

            int found = Physics.CapsuleCastNonAlloc(
                bottom,
                top,
                weapon.Radius,
                direction,
                SweepHits,
                distance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            bool any = false;
            for (int index = 0; index < found; index++)
            {
                RaycastHit hit = SweepHits[index];
                if (!CanHit(hit.collider))
                {
                    continue;
                }

                if (!any || hit.distance < nearest.distance)
                {
                    nearest = hit;
                    any = true;
                }
            }

            return any;
        }

        private bool CanHit(Collider what)
        {
            if (what == null)
            {
                return false;
            }

            if (owner != null && what.transform.IsChildOf(owner))
            {
                return false;
            }

            IDamageable target = what.GetComponentInParent<IDamageable>();
            if (target == null)
            {
                return true;
            }

            // Rubble is not cover. A round that could still be stopped by a building it has
            // already knocked down would make destroying one pointless - opening a firing
            // lane is most of what destruction is for.
            return !target.IsDestroyed && Teams.IsHostile(team, target.Team);
        }

        /// <summary>
        /// Goes off, hurting whatever the weapon says it should.
        /// </summary>
        /// <param name="at">Where the round went off, in world space.</param>
        /// <param name="direct">What it struck, or null when it hit the world or expired.</param>
        /// <remarks>
        /// A weapon with no blast radius only hurts what it struck. One with a blast radius
        /// hurts everything hostile inside it, falling off to nothing at the edge, measured
        /// from the nearest point of each hull rather than its origin - a tank is five and a
        /// half metres long, and measuring to the middle of it makes the same rocket lethal
        /// or harmless depending on which end it lands at. The blast is a column too, and
        /// its falloff is measured across the map, so a rocket that goes off underneath a
        /// helicopter is as dangerous to it as one that goes off beside a tank.
        /// </remarks>
        private void Detonate(Vector3 at, IDamageable direct)
        {
            if (!TrySplash(at, direct))
            {
                Explosion.Spawn(impact, at, BlastRadius(weapon));
            }

            if (weapon.SplashRadius <= 0.0f)
            {
                if (direct != null)
                {
                    direct.TakeDamage(weapon.Damage, team);
                }

                Spark(at, direct);
                Destroy(gameObject);
                return;
            }

            Damaged.Clear();
            CombatPlane.Column(at, out Vector3 bottom, out Vector3 top);
            int found = Physics.OverlapCapsuleNonAlloc(
                bottom,
                top,
                weapon.SplashRadius,
                BlastHits,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            for (int index = 0; index < found; index++)
            {
                IDamageable hull = BlastHits[index] == null
                    ? null
                    : BlastHits[index].GetComponentInParent<IDamageable>();
                if (hull == null || Damaged.Contains(hull))
                {
                    continue;
                }

                Damaged.Add(hull);
                float reach = CombatPlane.DistanceOnMap(at, BlastHits[index].ClosestPoint(at));
                float share = hull == direct ? 1.0f : 1.0f - Mathf.Clamp01(reach / weapon.SplashRadius);
                hull.TakeDamage(weapon.Damage * share, team);
            }

            if (direct != null && !Damaged.Contains(direct))
            {
                direct.TakeDamage(weapon.Damage, team);
            }

            Damaged.Clear();
            Spark(at, direct);
            Destroy(gameObject);
        }

        /// <summary>
        /// Puts up spray if this round went into the water.
        /// </summary>
        /// <param name="at">Where the round went off, in world space.</param>
        /// <param name="direct">What it struck, or null when it hit the world or expired.</param>
        /// <returns><c>true</c> when spray was drawn, so no fireball should be.</returns>
        /// <remarks>
        /// The spray is put on the waterline rather than where the round happened to expire,
        /// which may be a little under it or a little over. Water is thrown up from the
        /// surface, and a splash centred half a metre down is a splash with its bottom half
        /// inside the sea.
        /// </remarks>
        private bool TrySplash(Vector3 at, IDamageable direct)
        {
            WaterLine water = WaterLine.Active;
            LevelDefinition level = LevelLoader.Current;
            if (splash == null || water == null || level == null)
            {
                return false;
            }

            bool overWater = SurfaceTuning.For(level.Field.At(at)).Drowns;
            if (!DrawsSpray(overWater, direct != null, at.y, water.Surface))
            {
                return false;
            }

            ParticleBurst.Spawn(
                splash,
                new Vector3(at.x, water.Surface, at.z),
                Mathf.Max(weapon.SplashRadius, weapon.Radius * 6.0f));
            return true;
        }

        /// <summary>
        /// Returns how wide a fireball one weapon's round makes.
        /// </summary>
        /// <param name="weapon">The weapon that fired it.</param>
        /// <returns>The blast radius handed to <see cref="Explosion.Spawn"/>, in metres.</returns>
        /// <remarks>
        /// The bigger of what the weapon splashes and a few times the round itself, so a gun
        /// with no splash at all still makes a flash you can see it land by. It is public
        /// because it is also what decides whether a shot leaves a scorch - see
        /// <see cref="IronFlag.Vfx.GroundMark.SmallestBlast"/> - and that relationship
        /// between two tables is worth being able to check without firing anything.
        /// </remarks>
        public static float BlastRadius(WeaponTuning weapon)
            => weapon == null ? 0.0f : Mathf.Max(weapon.SplashRadius, weapon.Radius * 4.0f);

        /// <summary>
        /// Throws sparks off a target this round struck and failed to kill.
        /// </summary>
        /// <param name="at">Where the round struck, in world space.</param>
        /// <param name="direct">What it struck, or null when it hit the world or expired.</param>
        /// <remarks>
        /// <para>
        /// Called after the damage rather than before it, because whether the target is
        /// still standing is the whole question: a hit that kills already draws a wreck or a
        /// collapse, and a hit that does not previously drew nothing the ground would not
        /// have drawn too.
        /// </para>
        /// <para>
        /// A direct hit only. Everything a blast catches at the edge of its radius is
        /// already inside an explosion several metres across, and lighting each of them up
        /// as well would turn one rocket into a firework - sparks say "this shot connected
        /// with <em>that</em>", which is a sentence about the thing the round actually
        /// struck.
        /// </para>
        /// </remarks>
        private void Spark(Vector3 at, IDamageable direct)
        {
            if (direct == null || direct.IsDestroyed)
            {
                return;
            }

            ImpactSparks.Spawn(sparks, at, -state.Velocity, weapon.Radius);

            // The same sentence in sound: a round off armour, and only where the sparks
            // agree there was some. A kill is already an explosion and a collapse is already
            // its own noise, so this never doubles up with either.
            Sfx.PlayAt(SfxKind.Impact, at);
        }

        /// <summary>
        /// Returns the rotation that points a round along its flight.
        /// </summary>
        /// <param name="velocity">How fast the round is going.</param>
        /// <returns>A rotation looking along the velocity, or identity when it has none.</returns>
        private static Quaternion Facing(Vector3 velocity)
            => velocity.sqrMagnitude < 0.000001f ? Quaternion.identity : Quaternion.LookRotation(velocity);
    }
}
