using System;
using UnityEngine;
using IronFlag.Core;
using IronFlag.Vehicles;

namespace IronFlag.Combat
{
    /// <summary>
    /// A vehicle's hit points, and the moment it runs out of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The design document gives vehicles a simple pool rather than per-part damage, so this
    /// is a number, a rule about whose fire counts, and an event. Everything that reacts to a
    /// vehicle dying - the wreck disappearing, the pilot going back to the bunker, the
    /// vehicle-select screen coming up - hangs off <see cref="Destroyed"/> rather than being
    /// wired in here, because none of those are the health pool's business.
    /// </para>
    /// <para>
    /// Which side a vehicle is on is read from <see cref="VehicleTeamPaint"/> rather than
    /// stored again. That component was already the answer to "whose vehicle is this", and a
    /// second copy of the same fact is a second thing to keep in step - a vehicle painted
    /// green that shoots as brown is not a bug anyone would find quickly.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Vehicle Health")]
    public sealed class VehicleHealth : MonoBehaviour, IDamageable
    {
        [SerializeField]
        [Tooltip("Hit points when fresh. Stamped from VehicleTuning.HitPoints by the prefab builder.")]
        private float maxHitPoints = 100.0f;

        [SerializeField]
        [Tooltip("Explosion prefab spawned where the vehicle dies. Optional.")]
        private Explosion wreckExplosion;

        [SerializeField]
        [Tooltip("Radius of the explosion the wreck makes, in metres.")]
        private float wreckBlastRadius = 3.5f;

        private VehicleTeamPaint paint;
        private float hitPoints = -1.0f;

        /// <summary>Raised the moment this vehicle's hit points reach zero.</summary>
        public event Action<VehicleHealth> Destroyed;

        /// <summary>Raised when the vehicle comes back with a full pool.</summary>
        public event Action<VehicleHealth> Repaired;

        /// <summary>Hit points this vehicle has when fresh.</summary>
        public float MaxHitPoints => maxHitPoints;

        /// <summary>Hit points remaining.</summary>
        public float HitPoints => hitPoints < 0.0f ? maxHitPoints : hitPoints;

        /// <summary>Hit points remaining as a fraction of full, in 0..1.</summary>
        public float Fraction
            => maxHitPoints <= 0.0f ? 0.0f : Mathf.Clamp01(HitPoints / maxHitPoints);

        /// <summary>Whether this vehicle has been shot to pieces.</summary>
        public bool IsDestroyed => HitPoints <= 0.0f;

        /// <summary>Which side this vehicle is on, as decided by its paint.</summary>
        public Team Team => Paint == null ? Team.None : Paint.Team;

        private VehicleTeamPaint Paint
        {
            get
            {
                if (paint == null)
                {
                    paint = GetComponent<VehicleTeamPaint>();
                }

                return paint;
            }
        }

        /// <summary>
        /// Sets the size of the pool and what the wreck looks like.
        /// </summary>
        /// <param name="hitPointPool">Hit points when fresh.</param>
        /// <param name="explosion">Explosion prefab for the wreck, or null for none.</param>
        /// <param name="blastRadius">Radius of that explosion, in metres.</param>
        /// <remarks>Called by the prefab builder, and by anything that spawns a vehicle.</remarks>
        public void Configure(float hitPointPool, Explosion explosion, float blastRadius)
        {
            maxHitPoints = Mathf.Max(1.0f, hitPointPool);
            wreckExplosion = explosion;
            wreckBlastRadius = Mathf.Max(0.1f, blastRadius);
            hitPoints = maxHitPoints;
        }

        /// <summary>
        /// Takes a hit.
        /// </summary>
        /// <param name="amount">Hit points to remove; zero or less does nothing.</param>
        /// <param name="from">Side the shot came from.</param>
        /// <returns>The hit points actually lost, which is zero for a friendly shot.</returns>
        /// <remarks>
        /// A vehicle that is already wrecked absorbs nothing, so the blast that finishes it
        /// cannot raise <see cref="Destroyed"/> a second time and send its pilot back to the
        /// bunker twice.
        /// </remarks>
        public float TakeDamage(float amount, Team from)
        {
            if (amount <= 0.0f || IsDestroyed || !Teams.IsHostile(from, Team))
            {
                return 0.0f;
            }

            float before = HitPoints;
            hitPoints = Mathf.Max(0.0f, before - amount);

            if (hitPoints <= 0.0f)
            {
                Die();
            }

            return before - hitPoints;
        }

        /// <summary>
        /// Blows the vehicle up on its pilot's own orders.
        /// </summary>
        /// <returns><c>true</c> when it went up; <c>false</c> when it was already a wreck.</returns>
        /// <remarks>
        /// The design document's answer to running out of fuel in the middle of nowhere:
        /// "it can still fight in place, or self-destruct to redeploy immediately". It is
        /// deliberately the same event as being shot - the same explosion, the same trip
        /// back to the bunker, the same repairs - because a free way off the field would be
        /// a better move than fighting, and the whole tension of being stranded is that
        /// leaving costs exactly what dying costs.
        /// </remarks>
        public bool SelfDestruct()
        {
            if (IsDestroyed)
            {
                return false;
            }

            hitPoints = 0.0f;
            Die();
            return true;
        }

        /// <summary>
        /// Fills the pool back up, which is what redeploying from the bunker does.
        /// </summary>
        public void Repair()
        {
            hitPoints = maxHitPoints;
            Repaired?.Invoke(this);
        }

        private void Awake()
        {
            paint = GetComponent<VehicleTeamPaint>();
            hitPoints = maxHitPoints;
        }

        private void Die()
        {
            Explosion.Spawn(wreckExplosion, transform.position, wreckBlastRadius);
            Destroyed?.Invoke(this);
        }
    }
}
