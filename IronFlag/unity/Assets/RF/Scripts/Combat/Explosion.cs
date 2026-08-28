using UnityEngine;
using IronFlag.Audio;
using IronFlag.Vfx;

namespace IronFlag.Combat
{
    /// <summary>
    /// The flash something makes when it detonates: a ball of light that swells fast and
    /// fades slowly, then removes itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately not particles. The design document gives debris to M5, where it belongs
    /// with the destruction states that throw it, and screen shake and the rest of the juice
    /// to M8. What M3 needs is for a hit to be unmistakable at gameplay camera distance,
    /// which one bright expanding sphere does, in a generated prefab, with no art to build
    /// and nothing to tune.
    /// </para>
    /// <para>
    /// The shape of the flash is <see cref="Scale"/> - static and side-effect free, like the
    /// camera placement and the split-screen layout, so what it looks like can be checked
    /// without watching one go off.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Explosion")]
    public sealed class Explosion : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The sphere that swells. Scaled by this component; do not scale it by hand.")]
        private Transform ball;

        [SerializeField]
        [Tooltip("Point light that flashes with the ball. Optional.")]
        private Light flash;

        [SerializeField]
        [Tooltip("Seconds from ignition to gone.")]
        private float duration = 0.55f;

        [SerializeField]
        [Tooltip("Radius the ball reaches at its widest, in metres.")]
        private float radius = 2.0f;

        [SerializeField]
        [Tooltip("Light intensity at the peak of the flash.")]
        private float peakIntensity = 40.0f;

        [SerializeField]
        [Tooltip("Scorch left on the ground underneath. Optional.")]
        private GroundMark scorch;

        /// <summary>Blasts smaller than this radius go off in silence, in metres.</summary>
        /// <remarks>
        /// <para>
        /// Deliberately the same line <see cref="GroundMark.SmallestBlast"/> draws, converted
        /// from a mark's width back to the radius that made it, so that a blast leaves a
        /// scorch if and only if it is worth hearing. Two thresholds for the same question
        /// would eventually disagree, and the version where they disagree is a bang with no
        /// mark under it.
        /// </para>
        /// <para>
        /// The floor exists for exactly the reason the mark's does: a chaingun round goes off
        /// at about 0.4 metres and the gun fires eight of them a second, so without it a
        /// single ASV would sound like an artillery barrage. Rounds that small are already
        /// heard - as <see cref="Audio.SfxKind.Impact"/>, off the armour they struck, from
        /// <see cref="Projectile"/>'s spark - and that is the right sound for them. Above the
        /// floor are the cannon, the rocket, the grenade and every wreck: the shots that are
        /// events.
        /// </para>
        /// </remarks>
        public static readonly float HeardAbove =
            GroundMark.SmallestBlast / (GroundMark.BlastShare * 2.0f);

        private float elapsed;

        /// <summary>Radius the ball reaches at its widest, in metres.</summary>
        public float Radius => radius;

        /// <summary>Seconds from ignition to gone.</summary>
        public float Duration => duration;

        /// <summary>The scorch this leaves on the ground, or null when it leaves none.</summary>
        public GroundMark Scorch => scorch;

        /// <summary>
        /// Puts an explosion in the world, sized to what went off.
        /// </summary>
        /// <param name="prefab">Explosion prefab to instantiate; null does nothing.</param>
        /// <param name="at">Where it goes off, in world space.</param>
        /// <param name="blastRadius">Radius the flash should reach, in metres.</param>
        /// <returns>The explosion, or <c>null</c> when there was no prefab to spawn.</returns>
        /// <remarks>
        /// <para>
        /// Null-tolerant on purpose: a vehicle assembled in a test has no explosion prefab
        /// bound, and blowing it up should still work.
        /// </para>
        /// <para>
        /// The scorch is left here rather than at the call sites, and that is the whole
        /// reason it is one line of wiring instead of six: every blast in the game - a shell,
        /// a rocket, a wreck, a bunker's own defences - already comes through this method
        /// with its radius, so a mark hung off it is the size of what made it without anybody
        /// choosing a size. <see cref="GroundMark.Spawn"/> refuses the ones too small to be
        /// events and the ones over water.
        /// </para>
        /// <para>
        /// The bang is hung off it for exactly that reason too, and it is the one place in
        /// the game a detonation is heard from - but only above
        /// <see cref="HeardAbove"/>, because every round in the game comes through here and
        /// most of them are bullets. Note that it is inside the null check rather than above
        /// it: a caller with no explosion prefab is a test rig, and a rig that draws nothing
        /// should not be the one thing on the map still making a noise.
        /// </para>
        /// </remarks>
        public static Explosion Spawn(Explosion prefab, Vector3 at, float blastRadius)
        {
            if (prefab == null)
            {
                return null;
            }

            Explosion burst = Instantiate(prefab, at, Quaternion.identity);
            burst.radius = Mathf.Max(0.1f, blastRadius);

            if (burst.radius >= HeardAbove)
            {
                Sfx.PlayAt(SfxKind.Explosion, at);
            }

            GroundMark.Spawn(
                burst.scorch, at, burst.radius * GroundMark.BlastShare * 2.0f, GroundMark.Fade);

            return burst;
        }

        /// <summary>
        /// Points this component at the parts it animates.
        /// </summary>
        /// <param name="sphere">The ball that swells.</param>
        /// <param name="light">Point light to flash with it, or null for no light.</param>
        /// <param name="seconds">Seconds from ignition to gone.</param>
        /// <param name="blastRadius">Radius the ball reaches at its widest.</param>
        /// <remarks>Called by the prefab builder.</remarks>
        public void Configure(Transform sphere, Light light, float seconds, float blastRadius)
        {
            ball = sphere;
            flash = light;
            duration = Mathf.Max(0.01f, seconds);
            radius = Mathf.Max(0.1f, blastRadius);
        }

        /// <summary>
        /// Gives this explosion a scorch to leave on the ground under it.
        /// </summary>
        /// <param name="mark">The scorch prefab, or null for none.</param>
        /// <remarks>
        /// Its own setter rather than a fifth argument to <see cref="Configure"/>, for the
        /// reason <see cref="IronFlag.Destruction.Destructible.Scorches"/> gives: a stain on
        /// the floor is not one of the things an explosion cannot be built without, and
        /// widening the method would have made every test that assembles one say so.
        /// </remarks>
        public void Scorches(GroundMark mark) => scorch = mark;

        /// <summary>
        /// Returns how big the flash is, as a fraction of its widest, at one moment.
        /// </summary>
        /// <param name="progress">How far through the explosion is, in 0..1.</param>
        /// <returns>A fraction in 0..1, peaking at one a quarter of the way through.</returns>
        /// <remarks>
        /// The square root before the sine is what makes it read as an explosion rather than
        /// as a pulsing ball: it swells to full in the first quarter of its life and spends
        /// the remaining three quarters fading, which is the asymmetry the eye expects.
        /// </remarks>
        public static float Scale(float progress)
            => Mathf.Sin(Mathf.Sqrt(Mathf.Clamp01(progress)) * Mathf.PI);

        private void Update()
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / duration;

            if (progress >= 1.0f)
            {
                Destroy(gameObject);
                return;
            }

            float size = Scale(progress);

            if (ball != null)
            {
                ball.localScale = Vector3.one * (radius * 2.0f * size);
            }

            if (flash != null)
            {
                flash.range = Mathf.Max(1.0f, radius * 4.0f);
                flash.intensity = peakIntensity * size;
            }
        }
    }
}
