using UnityEngine;

namespace IronFlag.Vfx
{
    /// <summary>
    /// A cloud that happens once and then removes itself: the smoke a wreck throws up, the
    /// spray a shell puts up out of the sea.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The particle counterpart of <see cref="IronFlag.Combat.Explosion"/>, and deliberately
    /// the same shape of thing - spawn it where something happened, tell it how big, forget
    /// about it. What differs is that the animation is not here. An explosion's whole design
    /// is <c>Explosion.Scale</c>; a burst's is a <see cref="IronFlag.Editor.ArtPipeline.ParticleRig.Look"/>
    /// baked into a prefab, so this component only has to place it, size it and time it out.
    /// </para>
    /// <para>
    /// One component for two prefabs, because the difference between a smoke column and a
    /// water splash is entirely in the numbers. That is the whole argument for having let
    /// particle systems in: the two effects share every line of code and disagree only about
    /// colour, speed and which way the particles go.
    /// </para>
    /// <para>
    /// It counts itself out rather than leaning on <c>ParticleSystemStopAction.Destroy</c>.
    /// A prefab whose spray is two systems - a crown going up and a ring going out - has two
    /// things that could claim to have finished, and the one that finishes first must not
    /// take the other one's particles with it.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Particle Burst")]
    public sealed class ParticleBurst : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Seconds from the event to gone, including the tail of the last particle.")]
        private float duration = 2.5f;

        [SerializeField]
        [Tooltip("Metres across the prefab's own numbers were written for.")]
        private float authoredSize = 3.0f;

        private float elapsed;

        /// <summary>Seconds from the event to gone.</summary>
        public float Duration => duration;

        /// <summary>Metres across the prefab's own numbers were written for.</summary>
        public float AuthoredSize => authoredSize;

        /// <summary>
        /// Puts a burst in the world, sized to what caused it.
        /// </summary>
        /// <param name="prefab">Burst prefab to instantiate; null does nothing.</param>
        /// <param name="at">Where it happens, in world space.</param>
        /// <param name="size">How many metres across it should be.</param>
        /// <returns>The burst, or <c>null</c> when there was no prefab to spawn.</returns>
        /// <remarks>
        /// Null-tolerant for the reason <see cref="IronFlag.Combat.Explosion.Spawn"/> is: a
        /// rig assembled in a test has no burst prefab bound, and blowing it up should still
        /// work.
        /// </remarks>
        public static ParticleBurst Spawn(ParticleBurst prefab, Vector3 at, float size)
        {
            if (prefab == null)
            {
                return null;
            }

            ParticleBurst burst = Instantiate(prefab, at, Quaternion.identity);
            // A burst built in a scene rather than saved as a prefab has to be switched on,
            // the same trick and the same fix as a debris burst and an unfired round.
            burst.gameObject.SetActive(true);
            burst.Resize(size);
            burst.Restart();
            return burst;
        }

        /// <summary>
        /// Sets how long the burst lasts and what size its numbers were written for.
        /// </summary>
        /// <param name="seconds">Seconds from the event to gone.</param>
        /// <param name="metres">Metres across the prefab's own numbers assume.</param>
        /// <remarks>Called by the prefab builder.</remarks>
        public void Configure(float seconds, float metres)
        {
            duration = Mathf.Max(0.05f, seconds);
            authoredSize = Mathf.Max(0.05f, metres);
        }

        /// <summary>
        /// Scales the whole burst to a size in metres.
        /// </summary>
        /// <param name="size">How many metres across it should be.</param>
        /// <remarks>
        /// Through the transform rather than by reaching into every module, because every
        /// system this builds is set to scale with its own transform - see
        /// <c>ParticleRig.Create</c>. That is one line here instead of a start size, a
        /// speed, a shape radius and a gravity to rescale, each of which could be missed.
        /// </remarks>
        public void Resize(float size)
            => transform.localScale = Vector3.one * (Mathf.Max(0.05f, size) / authoredSize);

        /// <summary>
        /// Draws the burst as it looks at one moment of its life.
        /// </summary>
        /// <param name="seconds">Seconds since the event.</param>
        /// <remarks>
        /// For the preview scene, for the reason <c>MuzzleFlash.PoseAt</c> exists: particles
        /// do not simulate outside play mode, so a burst dropped into a generated scene is an
        /// empty emitter until something winds it forward by hand.
        /// </remarks>
        public void PoseAt(float seconds)
        {
            foreach (ParticleSystem system in GetComponentsInChildren<ParticleSystem>(true))
            {
                system.Simulate(Mathf.Max(0.0f, seconds), false, true);
            }
        }

        /// <summary>Starts every system in the prefab from nothing.</summary>
        private void Restart()
        {
            foreach (ParticleSystem system in GetComponentsInChildren<ParticleSystem>(true))
            {
                system.Clear(false);
                system.Play(false);
            }
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            if (elapsed >= duration)
            {
                Destroy(gameObject);
            }
        }
    }
}
