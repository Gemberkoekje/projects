using UnityEngine;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Vehicles;

namespace IronFlag.Destruction
{
    /// <summary>
    /// A gate with nobody on it: it drops its leaf into the ground when a vehicle of its
    /// own side comes near, and stands there like a wall for everybody else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The second destructible that belongs to a side, after <see cref="AutoTurret"/>, and
    /// the opposite answer to the same question. A turret is a side's way of making ground
    /// expensive for the enemy; a gate is a side's way of making its <em>own</em> wall
    /// cheap for itself. Both read off <see cref="Destructible.Team"/>, and neither prefab
    /// carries a side - the level file hands it over, because which gate is whose is a fact
    /// about the map.
    /// </para>
    /// <para>
    /// <strong>Everything here is one number: where the leaf is.</strong> There is no
    /// separate open/closed state, no latch and no animation clip - just
    /// <see cref="Openness"/> moving between nought and one at <see cref="Speed"/>, and the
    /// leaf sitting wherever that says. A state machine would have to answer what a gate
    /// interrupted halfway is, and the honest answer is "halfway".
    /// </para>
    /// <para>
    /// The leaf lives inside whichever state model is showing rather than on the root, for
    /// the same reason the turret's head and the flag tower's mount do: the intact gate and
    /// the shot-up one are different meshes, and a leaf bolted to the root would slide a
    /// door that is no longer there. So it is re-found whenever the state changes, and a
    /// state with no <c>Leaf</c> node in it - which the rubble deliberately has not -
    /// simply has no gate. That is what makes a destroyed door a permanent hole rather
    /// than a hole this component has to remember not to close.
    /// </para>
    /// <para>
    /// Targets come off <see cref="VehicleController.OnTheField"/> rather than a physics
    /// sweep, exactly as the turret's do: that roll-call is "drivable, and out of its
    /// bunker", so a gate cannot be held open from inside a bunker and cannot be fooled by
    /// a collider that happens to be switched on.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Destructible))]
    [AddComponentMenu("IronFlag/Auto Door")]
    public sealed class AutoDoor : MonoBehaviour
    {
        /// <summary>Name of the object inside a state model that slides.</summary>
        /// <remarks>
        /// The name <c>blender/assets/structure_door.py</c> gives the leaf. Unity finds it
        /// by that name rather than by a hierarchy position that re-exporting would move -
        /// the same arrangement as <see cref="AutoTurret.TurretNodeName"/>, and worth
        /// noting that a typo here is quieter than a typo there: a turret that never fires
        /// is obvious, and a gate that never opens looks exactly like an enemy's.
        /// </remarks>
        public const string LeafNodeName = "Leaf";

        /// <summary>
        /// Metres past flush the leaf's top is driven when the gate is fully open.
        /// </summary>
        /// <remarks>
        /// The map is drawn as a stack of flat sheets a couple of centimetres apart - see
        /// <see cref="IronFlag.Levels.LevelBuilder"/> - so a leaf whose cap stopped exactly
        /// at the ground plane would fight the road for the same pixels from two hundred
        /// metres up. A tenth of a metre is far enough below the whole stack to be out of
        /// the argument and short enough that opening does not visibly overshoot.
        /// </remarks>
        public const float Tuck = 0.10f;

        /// <summary>
        /// Metres around the closed leaf that count as standing in the doorway.
        /// </summary>
        /// <remarks>
        /// Half the widest vehicle in the game - the tank, at 3.19 m - and a little over,
        /// so a vehicle whose flank is across the threshold counts even when its middle is
        /// not. It is what <see cref="IsBlocked"/> measures, and the reason that check
        /// exists at all: the leaf is a collider that moves, and driving it up through a
        /// vehicle standing on it would throw that vehicle into the air.
        /// </remarks>
        public const float Clearance = 1.8f;

        [SerializeField]
        [Tooltip("Metres a vehicle of this side opens it from. Stamped by the prefab builder.")]
        private float reach = 16.0f;

        [SerializeField]
        [Tooltip("How fast the leaf travels, in metres per second.")]
        private float speed = 3.5f;

        private Destructible shell;
        private Transform leaf;
        private DestructionState mounted = DestructionState.None;
        private Vector3 seated;
        private float travel;
        private Bounds doorway;
        private float openness;

        /// <summary>Side this gate opens for. Read off its own <see cref="Destructible"/>.</summary>
        public Team Team => Shell == null ? Team.None : Shell.Team;

        /// <summary>Metres a vehicle of this side opens it from.</summary>
        public float Reach => reach;

        /// <summary>How fast the leaf travels, in metres per second.</summary>
        public float Speed => speed;

        /// <summary>How far the leaf is down, from nought shut to one fully open.</summary>
        public float Openness => openness;

        /// <summary>Metres the leaf travels between shut and open, measured off the model.</summary>
        public float Travel
        {
            get
            {
                Mount();
                return travel;
            }
        }

        /// <summary>Whether a vehicle could drive through right now.</summary>
        /// <remarks>
        /// A gate with nothing to slide is never open, which is why this asks about the
        /// leaf as well as the number. The rubble has no leaf and is not a gate at all -
        /// it is a hole, and <see cref="Destructible"/> has already switched its colliders
        /// off.
        /// </remarks>
        public bool IsOpen => Leaf != null && openness >= 1.0f;

        /// <summary>The part currently sliding, or <c>null</c> when this state has none.</summary>
        public Transform Leaf
        {
            get
            {
                Mount();
                return leaf;
            }
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

        /// <summary>
        /// Returns how far a gate's leaf has to drop to be out of the way.
        /// </summary>
        /// <param name="root">The gate, whose origin sits on the ground it stands on.</param>
        /// <param name="closed">The leaf, in its shut position.</param>
        /// <returns>The distance in metres, or zero when there is nothing to measure.</returns>
        /// <remarks>
        /// <para>
        /// <strong>Measured off the built mesh, not written down.</strong> The number a
        /// door's own art file would give is its nominal height, and the walls pass caught
        /// that being a lie twice over for want of asking Blender what it had actually
        /// built. Asking the renderers here means re-exporting a taller gate produces a
        /// longer drop without anybody editing C#, and means a test can compare the tuning
        /// against the asset rather than against a second copy of a number.
        /// </para>
        /// <para>
        /// The world-space Y of a bounding box is safe to read even though this is a yaw
        /// rotation: a level places every structure with
        /// <c>Quaternion.Euler(0, yaw, 0)</c>, and turning about Y is the one rotation that
        /// cannot change how tall a thing is.
        /// </para>
        /// </remarks>
        public static float TravelFor(Transform root, Transform closed)
        {
            if (root == null || closed == null)
            {
                return 0.0f;
            }

            Renderer[] parts = closed.GetComponentsInChildren<Renderer>(true);
            if (parts.Length == 0)
            {
                return 0.0f;
            }

            Bounds box = parts[0].bounds;
            for (int index = 1; index < parts.Length; index++)
            {
                box.Encapsulate(parts[index].bounds);
            }

            return Mathf.Max(0.0f, box.max.y - root.position.y) + Tuck;
        }

        /// <summary>
        /// Sets how far away this gate notices its own side, and how fast it moves.
        /// </summary>
        /// <param name="metres">Reach, in metres.</param>
        /// <param name="metresPerSecond">Leaf speed, in metres per second.</param>
        /// <remarks>
        /// Called by the prefab builder. Which <em>side</em> a gate is on is not set here -
        /// it comes off the level file through <see cref="Destructible.SetTeam"/>, because
        /// one prefab serves both sides.
        /// </remarks>
        public void Configure(float metres, float metresPerSecond)
        {
            reach = Mathf.Max(0.0f, metres);
            speed = Mathf.Max(0.0f, metresPerSecond);
        }

        /// <summary>
        /// Returns the vehicle this gate is currently opening for.
        /// </summary>
        /// <returns>The nearest friendly vehicle in reach, or <c>null</c> when there is none.</returns>
        /// <remarks>
        /// <para>
        /// Nearest across the map rather than through the air, which is the same measure
        /// the turret picks a target by and the same one <see cref="CombatPlane"/> resolves
        /// a shot by. It means a helicopter of the right side counts, which is deliberate
        /// and costs nothing: an aircraft has no use for a gate itself, so the only thing
        /// it can do by hovering over one is hold it open for a team-mate, and two players
        /// on one side helping each other is a thing worth having rather than a hole to
        /// plug.
        /// </para>
        /// <para>
        /// A gate on no side opens for nobody, which is the safe way round for an
        /// authoring slip: <see cref="Teams.IsHostile"/> counts everybody as hostile to
        /// <see cref="Team.None"/>, so reading it the other way would make an unconfigured
        /// gate stand open for both players at once - a hole in a wall that looks exactly
        /// like a gate that is working.
        /// </para>
        /// </remarks>
        public VehicleController Opener()
        {
            Team side = Team;
            if (side == Team.None || Shell.IsDestroyed)
            {
                return null;
            }

            VehicleController closest = null;
            float nearest = reach;

            foreach (VehicleController vehicle in VehicleController.OnTheField)
            {
                if (vehicle == null || Teams.IsHostile(side, TeamOf(vehicle)))
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

        /// <summary>
        /// Reports whether anything is standing where the leaf would come back to.
        /// </summary>
        /// <returns><c>true</c> when a vehicle is in the doorway.</returns>
        /// <remarks>
        /// <para>
        /// Measured against the volume the <em>shut</em> leaf fills, grown by
        /// <see cref="Clearance"/>, rather than against wherever the leaf happens to be:
        /// the question is what the leaf is about to sweep through, and an open gate's leaf
        /// is underground where nothing ever is. Anything on either side of it counts,
        /// because a gate cannot tell which way a vehicle was going.
        /// </para>
        /// <para>
        /// A helicopter falls out of this on its own without a check for one. The box is
        /// two metres tall and a little over; an aircraft holds ten.
        /// </para>
        /// </remarks>
        public bool IsBlocked()
        {
            if (Leaf == null)
            {
                return false;
            }

            foreach (VehicleController vehicle in VehicleController.OnTheField)
            {
                if (vehicle != null && doorway.Contains(vehicle.transform.position))
                {
                    return true;
                }
            }

            return false;
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

        /// <summary>
        /// Moves the leaf a step towards where it should be.
        /// </summary>
        /// <remarks>
        /// In the fixed step rather than the frame, unlike <see cref="AutoTurret"/>, and
        /// the difference is that the turret's head carries no collider while this does.
        /// A gun barrel swinging is a picture, so it may move whenever a picture is drawn;
        /// a leaf is something a six-tonne rigidbody is resting against, and moving it out
        /// of step with the solver it is resolved by is how a vehicle ends up inside it.
        /// </remarks>
        private void FixedUpdate()
        {
            Mount();

            if (leaf == null || travel <= 0.0f)
            {
                return;
            }

            // Speed is metres per second and openness is a fraction of the drop, so the
            // step is one converted into the other by the length of the drop. That is what
            // makes a taller gate take longer rather than move faster.
            float wanted = Wants() ? 1.0f : 0.0f;
            openness = Mathf.MoveTowards(
                openness, wanted, (speed * Time.fixedDeltaTime) / travel);
            Place();
        }

        /// <summary>
        /// Decides whether the gate should be open this step.
        /// </summary>
        /// <returns><c>true</c> to open or stay open.</returns>
        /// <remarks>
        /// <para>
        /// Two reasons to be open, and the second one is the interesting one. The first is
        /// the whole feature: somebody of this side wants through. The second is that the
        /// leaf may not close on a vehicle, so anything standing in the doorway pins it.
        /// </para>
        /// <para>
        /// That falls out as a tactic rather than being designed as one: an enemy who gets
        /// a vehicle into the gateway behind a defender holds the gate open, at the price
        /// of parking in a doorway, in the open, doing nothing else. It is checked only
        /// once the gate is off its seat, so a vehicle nosed up against a <em>shut</em>
        /// gate - which is what an attacker who cannot get in looks like - does not jam
        /// its own side's door from the wrong side of it.
        /// </para>
        /// </remarks>
        private bool Wants() => Opener() != null || (openness > 0.0f && IsBlocked());

        /// <summary>
        /// Finds the leaf inside whichever state model is showing, when that has changed.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Cheap on every step it changes nothing, which is nearly all of them: a gate
        /// swaps model at most twice in a match.
        /// </para>
        /// <para>
        /// <strong>How far open it was carries across the swap.</strong> This is the one
        /// place a door deliberately does the opposite of what the turret does - a turret
        /// re-stows its barrel on a new model, because a gun's rest position is a fact
        /// about the gun. A gate's position is a fact about the traffic, and a gate that
        /// slammed shut the instant it was hit would crush whoever was driving through it,
        /// which is the single worst moment in a match to introduce a barrier from below.
        /// </para>
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
            leaf = model == null ? null : Find(model, LeafNodeName);

            if (leaf == null)
            {
                travel = 0.0f;
                return;
            }

            // The new model comes out of the prefab shut, so this is the one moment all
            // three of these can be measured - where the leaf sits when it is closed, how
            // far below that it has to go, and what it would sweep on the way back up.
            seated = leaf.localPosition;
            travel = TravelFor(transform, leaf);
            doorway = Sweep(leaf);
            Place();
        }

        /// <summary>
        /// Puts the leaf where <see cref="openness"/> says it is.
        /// </summary>
        /// <remarks>
        /// Offset from where the model actually seats it rather than from the origin, so
        /// an asset that hangs its leaf somewhere other than dead centre still shuts flush
        /// instead of jumping to nought the first time this runs.
        /// </remarks>
        private void Place()
            => leaf.localPosition = seated + (Vector3.down * (travel * openness));

        /// <summary>
        /// Returns the volume a shut leaf fills, with room for a vehicle around it.
        /// </summary>
        /// <param name="closed">The leaf, in its shut position.</param>
        /// <returns>A world-space box, or an empty one when there is nothing to measure.</returns>
        private static Bounds Sweep(Transform closed)
        {
            Renderer[] parts = closed.GetComponentsInChildren<Renderer>(true);
            if (parts.Length == 0)
            {
                return new Bounds(closed.position, Vector3.zero);
            }

            Bounds box = parts[0].bounds;
            for (int index = 1; index < parts.Length; index++)
            {
                box.Encapsulate(parts[index].bounds);
            }

            box.Expand(Clearance * 2.0f);
            return box;
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
