using UnityEngine;

namespace IronFlag.Destruction
{
    /// <summary>
    /// The mess a structure makes when it changes state: a handful of chunks thrown outwards
    /// and upwards, falling and shrinking away to nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The design document asks for "a debris VFX burst on each transition", and this is it.
    /// Deliberately not a particle system and deliberately not rigidbodies: a particle
    /// system is an asset nobody can review in a diff, and a dozen rigidbodies per building
    /// is a physics bill paid for something that is over in a second. Each chunk is a cube
    /// on a ballistic arc computed in closed form, which is the same trick
    /// <see cref="IronFlag.Combat.ProjectileMotion"/> plays and for the same reason.
    /// </para>
    /// <para>
    /// <see cref="Offset"/> is the whole animation, and it is static and side-effect free -
    /// where a chunk is at a given moment can be checked without blowing anything up.
    /// </para>
    /// <para>
    /// The ground is taken to be world <c>y = 0</c>, which is what the map is until M7 puts
    /// any height on it. Chunks come to rest on it rather than falling through, and the day
    /// the ground stops being flat this is the line that has to learn about it.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Debris Burst")]
    public sealed class DebrisBurst : MonoBehaviour
    {
        /// <summary>Metres per second per second the chunks fall at.</summary>
        /// <remarks>
        /// Heavier than the world's gravity on purpose. Debris that falls at 9.81 hangs in
        /// the air long enough to read as floating, seen from thirty-four metres up.
        /// </remarks>
        public const float Gravity = 24.0f;

        [SerializeField]
        [Tooltip("The chunks that fly. Positioned and scaled by this component; do not move them by hand.")]
        private Transform[] chunks = new Transform[0];

        [SerializeField]
        [Tooltip("Seconds from the bang to gone.")]
        private float duration = 0.9f;

        [SerializeField]
        [Tooltip("Metres the chunks are thrown across, which is the size of what broke.")]
        private float radius = 3.0f;

        private float elapsed;

        /// <summary>Seconds from the bang to gone.</summary>
        public float Duration => duration;

        /// <summary>Metres the chunks are thrown across.</summary>
        public float Radius => radius;

        /// <summary>How many chunks this burst throws.</summary>
        public int ChunkCount => chunks == null ? 0 : chunks.Length;

        /// <summary>
        /// Throws debris where something broke.
        /// </summary>
        /// <param name="prefab">Debris prefab to instantiate; null does nothing.</param>
        /// <param name="at">Where it broke, in world space.</param>
        /// <param name="spread">Metres the chunks are thrown across.</param>
        /// <returns>The burst, or <c>null</c> when there was no prefab to spawn.</returns>
        /// <remarks>
        /// Null-tolerant for the same reason <see cref="IronFlag.Combat.Explosion.Spawn"/>
        /// is: a structure assembled in a test has no debris prefab bound, and knocking it
        /// down should still work.
        /// </remarks>
        public static DebrisBurst Spawn(DebrisBurst prefab, Vector3 at, float spread)
        {
            if (prefab == null)
            {
                return null;
            }

            DebrisBurst burst = Instantiate(prefab, at, Quaternion.identity);
            // A burst built in a scene rather than saved as a prefab has to be switched off
            // so that it does not play where it stands, and Instantiate copies that - the
            // same trick, and the same fix, as a round that has not been fired yet.
            burst.gameObject.SetActive(true);
            burst.radius = Mathf.Max(0.1f, spread);
            return burst;
        }

        /// <summary>
        /// Points this component at the chunks the prefab builder generated for it.
        /// </summary>
        /// <param name="pieces">The chunks that fly.</param>
        /// <param name="seconds">Seconds from the bang to gone.</param>
        /// <param name="spread">Metres the chunks are thrown across.</param>
        /// <remarks>Called by the prefab builder.</remarks>
        public void Configure(Transform[] pieces, float seconds, float spread)
        {
            chunks = pieces ?? new Transform[0];
            duration = Mathf.Max(0.05f, seconds);
            radius = Mathf.Max(0.1f, spread);
        }

        /// <summary>
        /// Returns where one chunk is, relative to where the structure broke.
        /// </summary>
        /// <param name="index">Which chunk, zero-based.</param>
        /// <param name="count">How many chunks the burst throws.</param>
        /// <param name="spread">Metres the chunks are thrown across.</param>
        /// <param name="seconds">Seconds since the bang.</param>
        /// <returns>The offset from the origin of the burst, in metres.</returns>
        /// <remarks>
        /// The chunks are fanned evenly around the circle rather than scattered randomly:
        /// a burst that is different every time cannot be photographed by the command-line
        /// still or asserted on in a test, and an even fan reads as an explosion from above
        /// anyway because the arcs differ in height. The golden-angle step is what stops
        /// eight chunks landing in a ring - each one is thrown a little further than the
        /// last.
        /// </remarks>
        public static Vector3 Offset(int index, int count, float spread, float seconds)
        {
            if (count <= 0)
            {
                return Vector3.zero;
            }

            // 137.5 degrees, the angle a sunflower packs its seeds at: successive chunks
            // never line up, however many there are.
            float bearing = index * 137.5f * Mathf.Deg2Rad;
            float share = (index + 1.0f) / count;
            float outwards = spread * Mathf.Lerp(0.5f, 1.4f, share);
            float upwards = spread * Mathf.Lerp(1.6f, 0.7f, share);

            return new Vector3(
                Mathf.Cos(bearing) * outwards * seconds,
                (upwards * seconds) - (0.5f * Gravity * seconds * seconds),
                Mathf.Sin(bearing) * outwards * seconds);
        }

        /// <summary>
        /// Returns how big a chunk is drawn, as a fraction of full, at one moment.
        /// </summary>
        /// <param name="progress">How far through the burst is, in 0..1.</param>
        /// <returns>A fraction in 0..1: full for most of it, then shrinking away.</returns>
        /// <remarks>
        /// Held at full for the first two thirds rather than faded from the start, because
        /// debris that begins shrinking the moment it appears reads as smoke rather than as
        /// pieces of a building.
        /// </remarks>
        public static float Scale(float progress)
            => 1.0f - Mathf.Clamp01((Mathf.Clamp01(progress) - 0.66f) / 0.34f);

        private void Update()
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / duration;

            if (progress >= 1.0f)
            {
                Destroy(gameObject);
                return;
            }

            float size = Scale(progress) * radius * 0.16f;

            for (int index = 0; index < chunks.Length; index++)
            {
                Transform chunk = chunks[index];
                if (chunk == null)
                {
                    continue;
                }

                Vector3 flung = Offset(index, chunks.Length, radius, elapsed);

                // Where the ground is, in this burst's own space. A chunk that has fallen
                // through it lies on it rather than sinking out of sight - and the burst is
                // thrown from the middle of what broke, which for a building is several
                // metres up, so the floor is not at the chunk's own zero.
                float floor = (size * 0.5f) - transform.position.y;
                chunk.localPosition = new Vector3(flung.x, Mathf.Max(floor, flung.y), flung.z);
                chunk.localScale = Vector3.one * size;
                chunk.localRotation = Quaternion.Euler(
                    (index + 1) * 90.0f * elapsed,
                    (index + 2) * 70.0f * elapsed,
                    (index + 3) * 50.0f * elapsed);
            }
        }
    }
}
