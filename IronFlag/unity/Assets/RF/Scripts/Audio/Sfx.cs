using UnityEngine;

namespace IronFlag.Audio
{
    /// <summary>
    /// How the rest of the game makes a noise: two static calls, no references to carry
    /// around, and nothing to check first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same shape as <c>Explosion.Spawn</c> and <c>MuzzleFlash.Spawn</c>, deliberately.
    /// Sound is the sort of thing that ends up wired through six components as a serialised
    /// field nobody remembers to fill in; making it a static call that quietly does nothing
    /// when there is no <see cref="AudioDirector"/> means a gun, a flag and a button all
    /// gain a voice in one line each, and a test rig that never built a scene keeps working
    /// exactly as it did.
    /// </para>
    /// <para>
    /// Two methods, and the choice between them is the only sound decision a call site ever
    /// makes: <see cref="Play"/> for something that matters wherever it happened, and
    /// <see cref="PlayAt"/> for something that happened <em>somewhere</em>. A flag changing
    /// hands is the first kind - it is news, and news from the far end of the map is still
    /// news. A shell landing is the second.
    /// </para>
    /// </remarks>
    public static class Sfx
    {
        /// <summary>
        /// Plays a sound at full volume, wherever it came from.
        /// </summary>
        /// <param name="kind">Sound to play.</param>
        /// <returns><c>true</c> when something was actually heard.</returns>
        /// <remarks>
        /// For the interface and for the objective: a button, a flag, the end of a match.
        /// These are announcements rather than events on the field, so putting them through
        /// the distance rule would make the flag being taken quieter than the gun that was
        /// fired at it.
        /// </remarks>
        public static bool Play(SfxKind kind)
        {
            AudioDirector director = AudioDirector.Current;
            return director != null && director.Play(kind, 1.0f);
        }

        /// <summary>
        /// Plays a sound as loud as somewhere on the map deserves.
        /// </summary>
        /// <param name="kind">Sound to play.</param>
        /// <param name="at">Where it happened, in world space.</param>
        /// <returns><c>true</c> when something was actually heard.</returns>
        /// <remarks>
        /// Distance only ever changes the volume, never the direction - see
        /// <see cref="AudioMixdown"/> for why that is the one part of positional audio a
        /// split screen can keep. Far enough away and nothing plays at all, which is what
        /// stops a busy map turning into a wall of noise.
        /// </remarks>
        /// <example>
        /// <code>
        /// Sfx.PlayAt(SfxKind.Explosion, blast.position);
        /// </code>
        /// </example>
        public static bool PlayAt(SfxKind kind, Vector3 at)
        {
            AudioDirector director = AudioDirector.Current;
            return director != null && director.Play(kind, AudioDirector.LoudnessAt(at));
        }
    }
}
