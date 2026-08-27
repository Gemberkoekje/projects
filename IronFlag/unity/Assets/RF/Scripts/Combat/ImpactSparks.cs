using UnityEngine;

namespace IronFlag.Combat
{
    /// <summary>
    /// The shower a round throws off something it did not kill: a handful of hot shards
    /// sprayed back the way it came, arcing down and going out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists to answer one question the game could not previously answer:
    /// <em>did that shot connect?</em> Every round already draws an
    /// <see cref="Explosion"/> where it goes off, whether it struck a hull, a wall, or bare
    /// ground at the end of its reach - so a chaingun burst that lands on a jeep and one
    /// that lands in the sand beside it drew exactly the same picture, and the only way to
    /// tell them apart was to watch the health bar. Sparks fly off armour and nothing else,
    /// so a burst that produces them is a burst that is doing damage.
    /// </para>
    /// <para>
    /// Kills are deliberately left alone. A target that dies to the shot already gets the
    /// wreck explosion (<see cref="VehicleHealth"/>) or a debris burst
    /// (<see cref="IronFlag.Destruction.Destructible"/>), and putting sparks on top of those
    /// would take the loudest event in the game and make it slightly louder. Sparks mean
    /// "hit, still standing", which is the reading that was missing.
    /// </para>
    /// <para>
    /// Same construction as <see cref="IronFlag.Destruction.DebrisBurst"/> and for the same
    /// reasons: closed-form arcs rather than particles or rigidbodies, and an even fan
    /// rather than a random scatter, so a burst can be photographed by the command-line
    /// still and asserted on in a test. <see cref="Offset"/> is the whole animation, static
    /// and side-effect free.
    /// </para>
    /// <para>
    /// The one thing it does that a debris burst does not is take an aim.
    /// <see cref="Offset"/> is handed the direction the shards are thrown in and builds its
    /// fan around that axis, because sparks that came off a hull come off it <em>facing the
    /// gun</em> - a hemisphere is what an explosion looks like, and this is not one.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Impact Sparks")]
    public sealed class ImpactSparks : MonoBehaviour
    {
        /// <summary>Metres per second per second the shards fall at.</summary>
        /// <remarks>
        /// Lighter than <see cref="IronFlag.Destruction.DebrisBurst.Gravity"/> on purpose,
        /// and it is the one number here that is not shared with it. A chunk of a building
        /// is heavy and has to look heavy; a spark is a fleck of hot metal that spends a
        /// fifth of a second in the air, and pulling it down at the debris rate bends every
        /// shard into the ground before it has drawn a streak.
        /// </remarks>
        public const float Gravity = 14.0f;

        /// <summary>How many calibres across a spark burst is thrown.</summary>
        /// <remarks>
        /// The number that turns a weapon into a burst, kept here rather than in the weapon
        /// table for the reason <see cref="MuzzleFlash.Calibres"/> is: it is a drawing
        /// decision and no shot lands differently for it. It puts a chaingun's sparks at
        /// 0.8 m and a cannon's at 1.3 m - a real difference, and both well inside the hull
        /// they came off, so a hit never looks bigger than the thing that was hit.
        /// </remarks>
        public const float Calibres = 8.0f;

        [SerializeField]
        [Tooltip("The shards that fly. Positioned and scaled by this component; do not move them by hand.")]
        private Transform[] shards = new Transform[0];

        [SerializeField]
        [Tooltip("Seconds from the strike to gone.")]
        private float duration = 0.22f;

        [SerializeField]
        [Tooltip("Metres the shards are thrown across.")]
        private float spread = 1.0f;

        [SerializeField]
        [Tooltip("Direction the shards are thrown in, in world space. Set when the burst is spawned.")]
        private Vector3 rebound = Vector3.up;

        private float elapsed;

        /// <summary>Seconds from the strike to gone.</summary>
        public float Duration => duration;

        /// <summary>Metres the shards are thrown across.</summary>
        public float Spread => spread;

        /// <summary>How many shards this burst throws.</summary>
        public int ShardCount => shards == null ? 0 : shards.Length;

        /// <summary>
        /// Throws sparks off something that has just been hit and survived it.
        /// </summary>
        /// <param name="prefab">Spark prefab to instantiate; null does nothing.</param>
        /// <param name="at">Where the round struck, in world space.</param>
        /// <param name="along">
        /// Which way the shards go, in world space - the round's flight, reversed. A zero
        /// vector throws them straight up, which is what a round that arrived from nowhere
        /// deserves.
        /// </param>
        /// <param name="calibre">Radius of the round that struck, in metres.</param>
        /// <returns>The burst, or <c>null</c> when there was no prefab to spawn.</returns>
        /// <remarks>
        /// Null-tolerant for the reason <see cref="Explosion.Spawn"/> is: a rig assembled in
        /// a test has no spark prefab bound, and shooting at it should still work.
        /// </remarks>
        public static ImpactSparks Spawn(ImpactSparks prefab, Vector3 at, Vector3 along, float calibre)
        {
            if (prefab == null)
            {
                return null;
            }

            ImpactSparks burst = Instantiate(prefab, at, Quaternion.identity);
            // A burst built in a scene rather than saved as a prefab has to be switched on,
            // the same trick and the same fix as a debris burst and an unfired round.
            burst.gameObject.SetActive(true);
            burst.spread = Mathf.Max(0.05f, calibre * Calibres);
            burst.rebound = along;
            return burst;
        }

        /// <summary>
        /// Points this component at the shards the prefab builder generated for it.
        /// </summary>
        /// <param name="pieces">The shards that fly.</param>
        /// <param name="seconds">Seconds from the strike to gone.</param>
        /// <param name="throwDistance">Metres the shards are thrown across.</param>
        /// <remarks>Called by the prefab builder.</remarks>
        public void Configure(Transform[] pieces, float seconds, float throwDistance)
        {
            shards = pieces ?? new Transform[0];
            duration = Mathf.Max(0.02f, seconds);
            spread = Mathf.Max(0.05f, throwDistance);
        }

        /// <summary>
        /// Returns where one shard is, relative to where the round struck.
        /// </summary>
        /// <param name="index">Which shard, zero-based.</param>
        /// <param name="count">How many shards the burst throws.</param>
        /// <param name="along">Direction the shards are thrown in; need not be normalised.</param>
        /// <param name="throwDistance">Metres the shards are thrown across.</param>
        /// <param name="seconds">Seconds since the strike.</param>
        /// <returns>The offset from the point of impact, in metres.</returns>
        /// <remarks>
        /// <para>
        /// A cone around <paramref name="along"/> rather than a sphere. Each shard is tilted
        /// a little further off the axis than the last and thrown a little slower for it, so
        /// the fast ones lead straight back at the gun and the slow ones fan out sideways -
        /// which is the shape that reads as metal coming off a plate rather than as a small
        /// firework.
        /// </para>
        /// <para>
        /// The golden angle does the same job it does in
        /// <see cref="IronFlag.Destruction.DebrisBurst.Offset"/>: successive shards never
        /// line up, however many there are, so an even fan does not come out as a ring.
        /// </para>
        /// </remarks>
        public static Vector3 Offset(
            int index, int count, Vector3 along, float throwDistance, float seconds)
        {
            if (count <= 0)
            {
                return Vector3.zero;
            }

            Vector3 axis = along.sqrMagnitude < 0.000001f ? Vector3.up : along.normalized;

            // Any vector that is not parallel to the axis will do to start the basis off;
            // the check is only there because an axis pointing straight up cannot use up.
            Vector3 seed = Mathf.Abs(axis.y) > 0.9f ? Vector3.forward : Vector3.up;
            Vector3 right = Vector3.Normalize(Vector3.Cross(axis, seed));
            Vector3 up = Vector3.Cross(axis, right);

            // 137.5 degrees, the angle a sunflower packs its seeds at.
            float bearing = index * 137.5f * Mathf.Deg2Rad;
            float share = (index + 1.0f) / count;
            float tilt = Mathf.Lerp(10.0f, 58.0f, share) * Mathf.Deg2Rad;
            float speed = throwDistance * Mathf.Lerp(5.5f, 2.4f, share);

            Vector3 heading = (axis * Mathf.Cos(tilt))
                + (((right * Mathf.Cos(bearing)) + (up * Mathf.Sin(bearing))) * Mathf.Sin(tilt));

            return (heading * (speed * seconds))
                + (Vector3.down * (0.5f * Gravity * seconds * seconds));
        }

        /// <summary>
        /// Returns how long a shard is drawn, as a fraction of full, at one moment.
        /// </summary>
        /// <param name="progress">How far through the burst is, in 0..1.</param>
        /// <returns>A fraction in 0..1, falling steadily from one to nothing.</returns>
        /// <remarks>
        /// Linear, and deliberately not
        /// <see cref="IronFlag.Destruction.DebrisBurst.Scale"/>'s hold-then-shrink. Debris
        /// is held at full size because a chunk of a building that starts shrinking on
        /// appearance reads as smoke; a spark is <em>meant</em> to read as cooling, and over
        /// a fifth of a second there is no room for a hold before the shrink anyway.
        /// </remarks>
        public static float Fade(float progress) => 1.0f - Mathf.Clamp01(progress);

        /// <summary>
        /// Draws the burst as it looks at one moment of its life.
        /// </summary>
        /// <param name="seconds">Seconds since the strike.</param>
        /// <remarks>
        /// Everything <c>Update</c> does, and public for the reason
        /// <see cref="MuzzleFlash.PoseAt"/> is: <c>Update</c> does not tick outside play
        /// mode, and a burst laid into the preview scene has to be posed by hand or it is
        /// not in the picture at all. Takes seconds rather than a fraction because the arcs
        /// are written in real time - only the fade is a fraction, and it is derived here.
        /// </remarks>
        public void PoseAt(float seconds)
        {
            float size = Fade(seconds / duration);

            for (int index = 0; index < shards.Length; index++)
            {
                Transform shard = shards[index];
                if (shard == null)
                {
                    continue;
                }

                Vector3 here = Offset(index, shards.Length, rebound, spread, seconds);
                shard.localPosition = here;

                // Long and thin, because a streak is what the eye reads as a spark; a cube
                // of the same volume reads as confetti. Both dimensions are fractions of the
                // throw so that a chaingun's sparks are finer than a cannon's, and both
                // shrink together so a shard goes out rather than thinning to a wire.
                float thickness = spread * 0.045f * size;
                shard.localScale = new Vector3(thickness, thickness, spread * 0.30f * size);

                // Pointed along its own flight, which is what turns a fleck into a streak.
                // Read off the same curve a moment later rather than differentiated by hand,
                // so the shard cannot point somewhere its path does not go.
                Vector3 ahead = Offset(index, shards.Length, rebound, spread, seconds + 0.01f) - here;
                if (ahead.sqrMagnitude > 0.000001f)
                {
                    shard.localRotation = Quaternion.LookRotation(ahead);
                }
            }
        }

        private void Update()
        {
            elapsed += Time.deltaTime;

            if (elapsed >= duration)
            {
                Destroy(gameObject);
                return;
            }

            PoseAt(elapsed);
        }
    }
}
