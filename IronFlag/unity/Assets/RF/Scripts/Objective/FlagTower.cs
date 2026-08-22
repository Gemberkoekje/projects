using System;
using System.Collections.Generic;
using UnityEngine;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Destruction;

namespace IronFlag.Objective
{
    /// <summary>
    /// One of the pyramids a side's flag might be on. Breaking it open is the only way to
    /// find out which, and the only way to take what is inside.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The design document puts "one real flag tower + one decoy tower" on the map to test
    /// "the confirm before committing mechanic from the original", and the asset spec makes
    /// the two "visually identical to each other", neutral coloured, with no team tint. Three
    /// rules make that mean something:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// An <em>intact</em> tower looks the same whether it holds the flag or not. There is no
    /// distance you can drive to and no angle you can look from that answers the question.
    /// </description></item>
    /// <item><description>
    /// A <em>broken</em> one - damaged or destroyed - shows what it was holding. A cracked
    /// tower with a flag on it is the real one; a cracked tower with nothing on it is the
    /// decoy, and you have just spent the shells to learn that.
    /// </description></item>
    /// <item><description>
    /// A jeep may only take a flag off a tower that has been broken open. Driving to a
    /// pristine tower achieves nothing at all.
    /// </description></item>
    /// </list>
    /// <para>
    /// Together those turn the design document's first pillar into a mechanic rather than a
    /// slogan. The jeep carries the flag and cannot fight; the tower costs five cannon shells
    /// to open; so the normal shape of a raid is a tank sortie that opens a tower followed by
    /// a jeep that takes what is in it. "Everything else exists to clear its path" is now
    /// literally true - there is a wall in the path, and the jeep is the worst thing in the
    /// game at removing it.
    /// </para>
    /// <para>
    /// This replaces the proximity scouting the tower had until now, and deliberately does
    /// not sit alongside it: two ways to learn the same fact would mean the cheaper one is
    /// the only one anybody uses, and driving past is always cheaper than shooting.
    /// </para>
    /// <para>
    /// A tower that has been opened stays open, and it is open for <em>everybody</em> -
    /// there is one world and two viewports of it. The side being raided watches its own
    /// tower come apart, which is the defender's warning that they have been found. Nothing
    /// repairs a tower, so the second raid down the same lane is cheaper than the first.
    /// </para>
    /// </remarks>
    [ExecuteAlways]
    [RequireComponent(typeof(Destructible))]
    [AddComponentMenu("IronFlag/Flag Tower")]
    public sealed class FlagTower : MonoBehaviour
    {
        /// <summary>Name of the object in each state model that a flag stands on.</summary>
        /// <remarks>
        /// Exported by <c>blender/assets/structure_flagtower.py</c>, once per destruction
        /// state, with its origin at the foot of the flag. Unity reads that origin rather
        /// than a height, so where the flag sits on a half-collapsed tower is an art
        /// decision - the same arrangement as the bunker's lift and the vehicles' muzzles.
        /// </remarks>
        public const string MountNodeName = "FlagMount";

        [SerializeField]
        [Tooltip("Which side's base this tower stands in. That side's flag might be on it.")]
        private Team team = Team.None;

        [SerializeField]
        [Tooltip("Whether this is the real tower. Exactly one per side is; the rest are decoys.")]
        private bool holdsTheFlag;

        /// <summary>Every flag tower currently in the scene, in the order they woke up.</summary>
        private static readonly List<FlagTower> Live = new List<FlagTower>();

        private Destructible shell;
        private bool announced;

        /// <summary>Raised the first time this tower is broken open.</summary>
        public event Action<FlagTower> Opened;

        /// <summary>Which side's base this tower stands in.</summary>
        public Team Team => team;

        /// <summary>Whether this is the tower that actually flies its side's flag.</summary>
        public bool HoldsTheFlag => holdsTheFlag;

        /// <summary>What state the masonry is in.</summary>
        public DestructionState State => Shell == null ? DestructionState.Intact : Shell.State;

        /// <summary>
        /// Whether this tower has been broken open, and so shows and surrenders what it holds.
        /// </summary>
        /// <remarks>
        /// Damaged counts. The difference between damaged and destroyed is how much is left
        /// standing and where the flag ends up, not whether you may have it: a tower that
        /// only gave up its flag once flattened would make the damaged state a stage nobody
        /// stops at, and the five shells that got you there wasted.
        /// </remarks>
        public bool IsOpen => State != DestructionState.Intact;

        /// <summary>Where a flag stands when it is on this tower.</summary>
        /// <remarks>
        /// Read off the mount inside whichever state model is showing, so the flag climbs
        /// down the tower as the tower comes apart: the top of the pole, then the base of
        /// the leaning one, then the stump. Falls back to a height above the tower's own
        /// origin when a model has no mount, which is what a tower rebuilt from an older
        /// <c>.glb</c> looks like.
        /// </remarks>
        public Vector3 FlagPoint
        {
            get
            {
                Transform mount = Mount();
                return mount == null
                    ? transform.position + (Vector3.up * FlagRules.TowerFlagHeight)
                    : mount.position;
            }
        }

        /// <summary>Which way a flag faces when it is on this tower, in degrees.</summary>
        /// <remarks>
        /// Off the mount as well, for the same reason its position is: a banner turned
        /// edge-on to the breach is a green line two pixels wide from the gameplay camera,
        /// and which way it should face is a fact about the hole in the wall it has to be
        /// seen through. Falls back to the tower's own facing when a model has no mount.
        /// </remarks>
        public float FlagYaw
        {
            get
            {
                Transform mount = Mount();
                return mount == null
                    ? transform.eulerAngles.y
                    : mount.eulerAngles.y;
            }
        }

        /// <summary>Every flag tower on the map.</summary>
        public static IReadOnlyList<FlagTower> All => Live;

        /// <summary>
        /// Returns how far a point on the map is from this tower's walls.
        /// </summary>
        /// <param name="at">Where something is standing; its height is ignored.</param>
        /// <returns>Metres to the nearest wall, or zero for a point inside the footprint.</returns>
        /// <remarks>
        /// <para>
        /// What "close enough to take the flag" has to be measured against, because the flag
        /// stands <em>inside</em> four and a half metres of masonry that nothing can drive
        /// into. Measured from the middle of the tower instead, a jeep nose-on to a wall sits
        /// 4.2 m out - its own length past the plinth - and a jeep side-on sits 3.1 m out, so
        /// the same pickup radius let one in and refused the other. That is not a rule
        /// anybody can learn; it is the vehicle's own shape leaking into the objective.
        /// </para>
        /// <para>
        /// Off the colliders of whichever state is showing, rather than a number, so a
        /// tower that is remodelled or knocked down to a stump brings its own answer.
        /// </para>
        /// </remarks>
        public float DistanceFrom(Vector3 at)
        {
            if (!TryFootprint(out Bounds footprint))
            {
                return CombatPlane.DistanceOnMap(FlagPoint, at);
            }

            float x = Mathf.Max(0.0f, Mathf.Max(footprint.min.x - at.x, at.x - footprint.max.x));
            float z = Mathf.Max(0.0f, Mathf.Max(footprint.min.z - at.z, at.z - footprint.max.z));
            return Mathf.Sqrt((x * x) + (z * z));
        }

        /// <summary>
        /// Returns the tower one side's flag actually flies from.
        /// </summary>
        /// <param name="side">Side to look up.</param>
        /// <returns>That side's real tower, or <c>null</c> when it has none.</returns>
        public static FlagTower RealFor(Team side)
        {
            foreach (FlagTower tower in Live)
            {
                if (tower != null && tower.team == side && tower.holdsTheFlag)
                {
                    return tower;
                }
            }

            return null;
        }

        /// <summary>
        /// Sets which side this tower belongs to and whether it is the real one.
        /// </summary>
        /// <param name="side">Side whose base this tower stands in.</param>
        /// <param name="real">Whether this tower flies that side's flag.</param>
        /// <remarks>
        /// Puts the masonry back together as well, so a tower placed into a scene that has
        /// already had a match in it starts the next one sealed.
        /// </remarks>
        public void Configure(Team side, bool real)
        {
            team = side;
            holdsTheFlag = real;
            announced = false;

            if (Shell != null)
            {
                Shell.Restore();
            }
        }

        /// <summary>
        /// Breaks this tower open without anybody having to shoot it.
        /// </summary>
        /// <returns><c>true</c> when this call is the one that opened it.</returns>
        /// <remarks>
        /// For tests and the command-line still, which have no time to drive a tank up and
        /// fire five shells. It goes through <see cref="Destructible.TakeDamage"/> rather
        /// than setting a flag, so a staged tower is open for the same reason a shot one is
        /// - and a still that looks right stays evidence about the game.
        /// </remarks>
        public bool Open()
        {
            if (Shell == null || IsOpen)
            {
                return false;
            }

            // Just past whatever threshold this tower's next state comes in at - which is
            // the damaged one normally, and destruction for a tower with no damaged mesh.
            // Aiming at the damaged threshold regardless would leave such a tower standing
            // and this method quietly returning false.
            float threshold = Shell.HasDamagedState
                ? Shell.Tuning.HitPoints * Shell.Tuning.DamagedAt
                : 0.0f;

            // Structures are neutral, so the damage has to come from *some* side to count
            // as hostile; which one it was is not recorded anywhere and does not matter.
            Shell.TakeDamage(Shell.HitPoints - threshold + 0.01f, Team.Green);
            return IsOpen;
        }

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

        private void OnEnable()
        {
            Live.Add(this);

            if (Shell != null)
            {
                Shell.StateChanged += OnStateChanged;
            }
        }

        private void OnDisable()
        {
            Live.Remove(this);

            if (shell != null)
            {
                shell.StateChanged -= OnStateChanged;
            }
        }

        /// <summary>
        /// Announces the first crack, once.
        /// </summary>
        /// <param name="structure">The masonry that changed state.</param>
        /// <remarks>
        /// Once, because a tower goes from intact to damaged to destroyed and only the first
        /// of those is news - by the second, everybody watching already knows what it holds.
        /// </remarks>
        private void OnStateChanged(Destructible structure)
        {
            if (announced || !IsOpen)
            {
                return;
            }

            announced = true;
            Opened?.Invoke(this);
        }

        /// <summary>
        /// Measures the box the standing part of this tower occupies.
        /// </summary>
        /// <param name="footprint">The bounds, in world space.</param>
        /// <returns><c>false</c> when this state has nothing solid in it to measure.</returns>
        /// <remarks>
        /// Only the colliders that are switched on, which is the state currently showing.
        /// A tower assembled in a test out of empty state nodes has none, and the caller
        /// falls back to measuring from the flag itself.
        /// </remarks>
        private bool TryFootprint(out Bounds footprint)
        {
            footprint = default;
            Transform model = Child(transform, Destructible.NodeNameFor(State));
            if (model == null)
            {
                return false;
            }

            bool found = false;
            foreach (Collider part in model.GetComponentsInChildren<Collider>())
            {
                if (found)
                {
                    footprint.Encapsulate(part.bounds);
                }
                else
                {
                    footprint = part.bounds;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// Finds the flag mount inside the state model that is showing.
        /// </summary>
        /// <returns>The mount, or <c>null</c> when this state's model has none.</returns>
        /// <remarks>
        /// By state name rather than by whichever mount happens to be active, because all
        /// three models are switched on in a scene that has not been played yet and the
        /// first one found would be a coin flip.
        /// </remarks>
        private Transform Mount()
        {
            Transform state = Child(transform, Destructible.NodeNameFor(State));
            return state == null ? null : Child(state, MountNodeName);
        }

        /// <summary>
        /// Finds a named object anywhere under a root.
        /// </summary>
        /// <param name="root">Object to search under.</param>
        /// <param name="name">Name to look for.</param>
        /// <returns>The transform, or <c>null</c>.</returns>
        private static Transform Child(Transform root, string name)
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
