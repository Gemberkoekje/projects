using UnityEngine;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Vehicles;

namespace IronFlag.Destruction
{
    /// <summary>
    /// A gun emplacement with nobody in it: it picks the nearest enemy vehicle in range,
    /// traverses onto it, and fires until it is gone or the turret is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The design document's "automated turrets that shoot at the enemy team", and the whole
    /// of it is <em>which way the gun points</em>. Everything after that is the combat
    /// pipeline every other gun in the game already goes through - a
    /// <see cref="VehicleWeapon"/> with its muzzle on the part that traverses, firing a
    /// <see cref="Projectile"/> that resolves as a <see cref="CombatPlane"/> column. A second
    /// firing path for the thing with no pilot would be a second answer to what a shot is.
    /// </para>
    /// <para>
    /// It is a <see cref="Destructible"/> like every other prop, which is what makes it
    /// answerable rather than merely dangerous: the turret is knocked down by shooting it,
    /// exactly as a building is, and a wrecked one stops firing without this component
    /// checking - <see cref="VehicleWeapon.IsLoaded"/> asks its mount whether it is still
    /// standing, and a turret in rubble says no.
    /// </para>
    /// <para>
    /// The gun lives inside whichever state model is showing rather than on the root, for
    /// the same reason the flag tower's mount does: the intact turret and the shot-up one
    /// are different meshes with the barrel in different places, and a muzzle bolted to the
    /// root would fire out of thin air once the emplacement was half wrecked. So the mount
    /// is re-found whenever the state changes, and a state with no <c>Turret</c> node in it
    /// - which the rubble deliberately has not - simply has no gun.
    /// </para>
    /// <para>
    /// Targets come off <see cref="VehicleController.OnTheField"/> rather than out of a
    /// physics sweep, like <see cref="IronFlag.Objective.Flag"/> and
    /// <see cref="IronFlag.Objective.FlagTower"/>: that roll-call is exactly "drivable, and
    /// out of its bunker", so a turret cannot shoot at something parked in an enemy bunker
    /// and cannot be distracted by a collider that happens to be switched on.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Destructible))]
    [AddComponentMenu("IronFlag/Auto Turret")]
    public sealed class AutoTurret : MonoBehaviour
    {
        /// <summary>Name of the object inside a state model that traverses.</summary>
        /// <remarks>
        /// The same name the tank's turret goes by, and for the same reason: the Blender
        /// pipeline names the part whose origin sits on the turret ring, and Unity finds it
        /// by that name rather than by a hierarchy position that re-exporting would move.
        /// </remarks>
        public const string TurretNodeName = "Turret";

        /// <summary>Name of the object the rounds actually leave from.</summary>
        /// <remarks>
        /// Added by the prefab builder at the tip of the barrel, exactly as it is on the
        /// tank and the ASV. It is a runtime constant rather than an editor one because the
        /// turret has to find it again every time its state changes, which happens in a
        /// built player where there is no prefab builder to ask.
        /// </remarks>
        public const string MuzzlePointName = "MuzzlePoint";

        /// <summary>
        /// Degrees the gun may be off target and still fire.
        /// </summary>
        /// <remarks>
        /// Narrow on purpose. A turret that fired while still swinging would spray rounds
        /// across everything between where it was pointing and what it is pointing at, which
        /// reads as a bug from either end and makes a turret dangerous to walk past rather
        /// than dangerous to stand in front of.
        /// </remarks>
        public const float AimTolerance = 6.0f;

        [SerializeField]
        [Tooltip("The gun this turret fires. Stamped by the prefab builder.")]
        private VehicleWeapon weapon;

        [SerializeField]
        [Tooltip("Traverse rate in degrees per second.")]
        private float turnRate = 80.0f;

        private Destructible shell;
        private Transform turret;
        private DestructionState mounted = DestructionState.None;

        /// <summary>Side this turret defends. Read off its own <see cref="Destructible"/>.</summary>
        public Team Team => Shell == null ? Team.None : Shell.Team;

        /// <summary>The gun this turret fires, or <c>null</c> when it has none.</summary>
        public VehicleWeapon Weapon => weapon;

        /// <summary>Traverse rate in degrees per second.</summary>
        public float TurnRate => turnRate;

        /// <summary>How far this turret can reach, in metres across the map.</summary>
        public float Range => weapon == null ? 0.0f : weapon.Tuning.Range;

        /// <summary>The part currently traversing, or <c>null</c> when this state has none.</summary>
        public Transform Turret
        {
            get
            {
                Mount();
                return turret;
            }
        }

        /// <summary>Direction the gun is pointing, in degrees clockwise from world +Z.</summary>
        public float AimYawDegrees => Turret == null ? RestYawDegrees : turret.eulerAngles.y;

        /// <summary>Where the gun points when there is nothing to shoot at.</summary>
        private float RestYawDegrees => transform.eulerAngles.y;

        private Destructible Shell
        {
            get
            {
                if (shell == null)
                {
                    shell = GetComponent<Destructible>();
                }

                return shell;
            }
        }

        /// <summary>
        /// Sets the gun this turret fires and how fast it traverses.
        /// </summary>
        /// <param name="gun">The weapon bolted to the traversing part.</param>
        /// <param name="degreesPerSecond">Traverse rate.</param>
        /// <remarks>
        /// Called by the prefab builder. Which <em>side</em> a turret is on is not set here
        /// - it comes off the level file, through
        /// <see cref="Destructible.SetTeam"/>, because one prefab serves both sides.
        /// </remarks>
        public void Configure(VehicleWeapon gun, float degreesPerSecond)
        {
            weapon = gun;
            turnRate = Mathf.Max(0.0f, degreesPerSecond);
        }

        /// <summary>
        /// Returns the enemy vehicle this turret would shoot at right now.
        /// </summary>
        /// <returns>The nearest hostile vehicle in range, or <c>null</c> when there is none.</returns>
        /// <remarks>
        /// <para>
        /// Nearest across the map rather than through the air, so a helicopter overhead is
        /// as much of a target as a tank beside it - which is the same rule
        /// <see cref="CombatPlane"/> resolves the shot by, and the two have to agree or a
        /// turret would track something it could never hit.
        /// </para>
        /// <para>
        /// A turret on no side has no enemies and shoots at nothing. That is the safe way
        /// round for an authoring slip: <see cref="Teams.IsHostile"/> says everybody is
        /// hostile to <see cref="Team.None"/>, so reading it the other way would make an
        /// unconfigured emplacement open fire on both players at once.
        /// </para>
        /// </remarks>
        public VehicleController Target()
        {
            Team side = Team;
            if (side == Team.None || weapon == null || Shell.IsDestroyed)
            {
                return null;
            }

            VehicleController closest = null;
            float nearest = weapon.Tuning.Range;

            foreach (VehicleController vehicle in VehicleController.OnTheField)
            {
                if (vehicle == null || !Teams.IsHostile(side, TeamOf(vehicle)))
                {
                    continue;
                }

                float away = CombatPlane.DistanceOnMap(transform.position, vehicle.transform.position);
                if (away <= nearest)
                {
                    nearest = away;
                    closest = vehicle;
                }
            }

            return closest;
        }

        private void Awake()
        {
            Mount();
            Shell.StateChanged += OnStateChanged;
        }

        private void OnDestroy()
        {
            if (shell != null)
            {
                shell.StateChanged -= OnStateChanged;
            }
        }

        private void Update()
        {
            Mount();

            if (turret == null)
            {
                return;
            }

            VehicleController target = Target();
            float wanted = target == null
                ? RestYawDegrees
                : VehicleTurret.TargetYaw(Bearing(target.transform.position), RestYawDegrees);

            float next = Mathf.MoveTowardsAngle(
                turret.eulerAngles.y, wanted, turnRate * Time.deltaTime);
            turret.rotation = Quaternion.Euler(0.0f, next, 0.0f);

            if (target != null && Mathf.Abs(Mathf.DeltaAngle(next, wanted)) <= AimTolerance)
            {
                weapon.TryFire();
            }
        }

        /// <summary>
        /// Finds the gun inside whichever state model is showing, when that has changed.
        /// </summary>
        /// <remarks>
        /// Cheap on every frame it changes nothing, which is nearly all of them: a turret
        /// swaps model at most twice in a match. The weapon is re-pointed at the new
        /// muzzle rather than rebuilt, so its cooldown carries across the transition and a
        /// turret cannot dodge its own rate of fire by being shot.
        /// </remarks>
        private void Mount()
        {
            DestructionState showing = Shell.State;
            if (showing == mounted)
            {
                return;
            }

            mounted = showing;
            Transform model = Find(transform, Destructible.NodeNameFor(showing));
            turret = model == null ? null : Find(model, TurretNodeName);

            if (weapon != null)
            {
                weapon.SetMuzzle(turret == null ? null : Find(turret, MuzzlePointName));
            }

            if (turret != null)
            {
                // A fresh model comes out of the prefab facing the way it was authored, so
                // the traverse starts stowed rather than wherever the previous state's
                // barrel had swung to.
                turret.rotation = Quaternion.Euler(0.0f, RestYawDegrees, 0.0f);
            }
        }

        /// <summary>
        /// Returns the direction from this turret to a point, as the map sees it.
        /// </summary>
        /// <param name="at">Where the target is; its height is ignored.</param>
        /// <returns>An XZ direction, X to world X and Y to world Z.</returns>
        private Vector2 Bearing(Vector3 at)
        {
            Vector3 across = CombatPlane.Flatten(at - transform.position);
            return new Vector2(across.x, across.z);
        }

        private void OnStateChanged(Destructible structure) => Mount();

        private static Team TeamOf(VehicleController vehicle)
        {
            var paint = vehicle.GetComponent<VehicleTeamPaint>();
            return paint == null ? Team.None : paint.Team;
        }

        /// <summary>
        /// Finds a named object anywhere under a root, including switched-off ones.
        /// </summary>
        /// <param name="root">Object to search under.</param>
        /// <param name="name">Name to look for.</param>
        /// <returns>The transform, or <c>null</c>.</returns>
        private static Transform Find(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }
    }
}
