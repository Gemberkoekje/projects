using System;
using UnityEngine;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Supply;

namespace IronFlag.Destruction
{
    /// <summary>
    /// A building, a tree or a depot that can be shot down: one hit point pool, three
    /// models, and the moment it swaps from one to the next.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the design document's v0.1 destruction in one component - "intact -> damaged
    /// -> destroyed mesh per structure, with a debris VFX burst on each transition" - and
    /// the state <em>is</em> the model: each of the three is a child object carrying its own
    /// meshes and its own colliders, and swapping states is turning one on and the others
    /// off. Nothing is scaled, faded or reshaped, so what a player sees and what a vehicle
    /// bumps into can never disagree.
    /// </para>
    /// <para>
    /// It is the second <see cref="IDamageable"/>, after <see cref="VehicleHealth"/>, and
    /// the two are deliberately the same shape: a round asks the same three questions of a
    /// wall as of a tank. What differs is what running out of hit points means - a vehicle
    /// disappears and is repaired in its bunker, and a building stays exactly where it is
    /// as rubble.
    /// </para>
    /// <para>
    /// A destroyed structure stops being a <see cref="SupplyPoint"/>, which is the whole of
    /// what the design document means by the depots being "destroyable/contestable": the
    /// component is switched off rather than removed, so a rebuilt structure is a rebuilt
    /// depot without anybody re-configuring it.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Destructible")]
    public sealed class Destructible : MonoBehaviour, IDamageable
    {
        /// <summary>Name of the child carrying the untouched model.</summary>
        public const string IntactNodeName = "Intact";

        /// <summary>Name of the child carrying the hurt-but-standing model.</summary>
        public const string DamagedNodeName = "Damaged";

        /// <summary>Name of the child carrying the rubble.</summary>
        public const string DestroyedNodeName = "Destroyed";

        [SerializeField]
        [Tooltip("Which destructible this is. Stamped by the prefab builder.")]
        private StructureKind kind = StructureKind.None;

        [SerializeField]
        [Tooltip("The numbers, from StructureTuning.For. Editable while playing.")]
        private StructureTuning tuning = new StructureTuning();

        [SerializeField]
        [Tooltip("Child carrying the untouched model.")]
        private GameObject intact;

        [SerializeField]
        [Tooltip("Child carrying the hurt-but-standing model. Optional; a bridge has none.")]
        private GameObject damaged;

        [SerializeField]
        [Tooltip("Child carrying the rubble.")]
        private GameObject destroyed;

        [SerializeField]
        [Tooltip("Debris burst thrown on each transition. Optional.")]
        private DebrisBurst debris;

        private DestructionState state = DestructionState.None;
        private float hitPoints = -1.0f;

        /// <summary>Raised whenever the structure swaps to a different model.</summary>
        public event Action<Destructible> StateChanged;

        /// <summary>Raised the moment the structure becomes rubble.</summary>
        public event Action<Destructible> Collapsed;

        /// <summary>Which destructible this is.</summary>
        public StructureKind Kind => kind;

        /// <summary>The numbers this structure is running on.</summary>
        public StructureTuning Tuning => tuning;

        /// <summary>Which model is showing.</summary>
        public DestructionState State
        {
            get
            {
                EnsureStanding();
                return state;
            }
        }

        /// <summary>Hit points remaining.</summary>
        public float HitPoints => hitPoints < 0.0f ? tuning.HitPoints : hitPoints;

        /// <summary>Hit points remaining as a fraction of full, in 0..1.</summary>
        public float Fraction
            => tuning.HitPoints <= 0.0f ? 0.0f : Mathf.Clamp01(HitPoints / tuning.HitPoints);

        /// <summary>Whether this structure has been knocked down.</summary>
        public bool IsDestroyed => HitPoints <= 0.0f;

        /// <summary>Whether this structure has a middle state to show.</summary>
        public bool HasDamagedState => damaged != null;

        /// <summary>
        /// Map furniture belongs to nobody, so everybody's fire counts against it.
        /// </summary>
        /// <remarks>
        /// Not a serialized field, because there is no such thing as a green building: a
        /// side's own ground is the bunker and the bunker is not destructible, and a depot
        /// both sides want is the entire point of a depot. See <see cref="Teams.IsHostile"/>.
        /// </remarks>
        public Team Team => Team.None;

        /// <summary>
        /// Returns the child name one state's model hangs off.
        /// </summary>
        /// <param name="state">State to name.</param>
        /// <returns>The node name a prefab's state model is parented to.</returns>
        /// <remarks>
        /// Here rather than in the prefab builder because the game reads these too: a flag
        /// tower has to find the flag mount inside whichever state it is currently showing,
        /// and a built player has no editor code to ask.
        /// </remarks>
        public static string NodeNameFor(DestructionState state)
        {
            switch (state)
            {
                case DestructionState.Intact:
                    return IntactNodeName;
                case DestructionState.Damaged:
                    return DamagedNodeName;
                case DestructionState.Destroyed:
                    return DestroyedNodeName;
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Points this component at its three models and the numbers it runs on.
        /// </summary>
        /// <param name="structure">Which destructible this is.</param>
        /// <param name="numbers">Hit points, threshold and debris size.</param>
        /// <param name="intactModel">Child carrying the untouched model.</param>
        /// <param name="damagedModel">Child carrying the middle model, or null for none.</param>
        /// <param name="destroyedModel">Child carrying the rubble.</param>
        /// <param name="burst">Debris prefab thrown on each transition, or null for none.</param>
        /// <remarks>Called by the prefab builder.</remarks>
        public void Configure(
            StructureKind structure,
            StructureTuning numbers,
            GameObject intactModel,
            GameObject damagedModel,
            GameObject destroyedModel,
            DebrisBurst burst)
        {
            kind = structure;
            tuning = numbers ?? new StructureTuning();
            intact = intactModel;
            damaged = damagedModel;
            destroyed = destroyedModel;
            debris = burst;
            hitPoints = tuning.HitPoints;
            state = DestructionState.None;
            Enter(DestructionState.Intact, throwDebris: false);
        }

        /// <summary>
        /// Takes a hit, and swaps model if that is what the hit means.
        /// </summary>
        /// <param name="amount">Hit points to remove; zero or less does nothing.</param>
        /// <param name="from">Side the shot came from.</param>
        /// <returns>The hit points actually lost, which is zero once it is rubble.</returns>
        /// <remarks>
        /// Rubble absorbs nothing, so the shot that flattens a building cannot raise
        /// <see cref="Collapsed"/> twice - the same rule, and the same reason, as a wreck
        /// that cannot send its pilot back to the bunker twice.
        /// </remarks>
        public float TakeDamage(float amount, Team from)
        {
            EnsureStanding();

            if (amount <= 0.0f || IsDestroyed || !Teams.IsHostile(from, Team))
            {
                return 0.0f;
            }

            float before = HitPoints;
            hitPoints = Mathf.Max(0.0f, before - amount);
            Enter(tuning.StateAt(Fraction, HasDamagedState), throwDebris: true);
            return before - hitPoints;
        }

        /// <summary>
        /// Puts the structure back up, whole.
        /// </summary>
        /// <remarks>
        /// Nothing in the game calls this - the design document has no repairs for scenery,
        /// and a building that came back would undo the one permanent change a player can
        /// make to the map. It exists because a test that knocks something down wants to
        /// stand it up again, and because M7's map builder will want to reset a scene it is
        /// generating.
        /// </remarks>
        public void Restore()
        {
            hitPoints = tuning.HitPoints;
            Enter(DestructionState.Intact, throwDebris: false);
        }

        private void Awake() => EnsureStanding();

        /// <summary>
        /// Brings the structure up whole the first time anybody asks anything of it.
        /// </summary>
        /// <remarks>
        /// Lazy rather than only in <see cref="Awake"/>, for the same reason
        /// <see cref="IronFlag.Core.VehicleBay"/> is: <c>Awake</c> does not run outside play
        /// mode, and the command-line still knocks a building down in a scene nobody is
        /// playing.
        /// </remarks>
        private void EnsureStanding()
        {
            if (state != DestructionState.None)
            {
                return;
            }

            if (hitPoints < 0.0f)
            {
                hitPoints = tuning.HitPoints;
            }

            Enter(tuning.StateAt(Fraction, HasDamagedState), throwDebris: false);
        }

        /// <summary>
        /// Shows one of the three models, and throws debris if that is a change.
        /// </summary>
        /// <param name="wanted">State to show.</param>
        /// <param name="throwDebris">Whether a transition should make a mess.</param>
        private void Enter(DestructionState wanted, bool throwDebris)
        {
            if (wanted == state)
            {
                return;
            }

            state = wanted;

            // Measured before the swap: the debris is pieces of what was standing here a
            // moment ago, and taking the middle of the rubble that replaced it would throw
            // a building's worth of masonry out of a knee-high pile.
            Vector3 origin = Centre();

            Show(intact, wanted == DestructionState.Intact);
            Show(damaged, wanted == DestructionState.Damaged);
            Show(destroyed, wanted == DestructionState.Destroyed);

            // A depot that has been flattened stops handing anything out. Switched off
            // rather than destroyed, so putting the structure back up puts the depot back.
            foreach (SupplyPoint point in GetComponentsInChildren<SupplyPoint>(true))
            {
                point.enabled = wanted != DestructionState.Destroyed;
            }

            if (throwDebris)
            {
                // Bigger for the collapse than for the first crack, because the two are the
                // same event at different scales and the player should be able to tell them
                // apart from across the map.
                float spread = tuning.DebrisRadius
                    * (wanted == DestructionState.Destroyed ? 1.0f : 0.6f);
                DebrisBurst.Spawn(debris, origin, spread);
            }

            StateChanged?.Invoke(this);

            if (wanted == DestructionState.Destroyed)
            {
                Collapsed?.Invoke(this);
            }
        }

        /// <summary>
        /// Returns where the debris comes from: the middle of what is standing there.
        /// </summary>
        /// <returns>A world-space point, which is the object's own origin when it has no bounds.</returns>
        /// <remarks>
        /// Measured off the renderers rather than the transform because a model's origin is
        /// on the ground at its foot, and debris that comes out of the floor of a building
        /// reads as the ground breaking rather than the building.
        /// </remarks>
        private Vector3 Centre()
        {
            Renderer[] parts = GetComponentsInChildren<Renderer>(false);
            if (parts.Length == 0)
            {
                return transform.position;
            }

            Bounds bounds = parts[0].bounds;
            for (int index = 1; index < parts.Length; index++)
            {
                bounds.Encapsulate(parts[index].bounds);
            }

            return bounds.center;
        }

        private static void Show(GameObject model, bool visible)
        {
            if (model != null && model.activeSelf != visible)
            {
                model.SetActive(visible);
            }
        }
    }
}
