using UnityEngine;
using IronFlag.Menu;

namespace IronFlag.Audio
{
    /// <summary>
    /// The music: one bed at a time, faded rather than cut when it changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="AudioDirector"/> because music is not a loud sound effect.
    /// It runs on its own volume setting, it streams from disk rather than sitting in memory
    /// (see <c>AudioImportSettings</c>), and exactly one of it plays at a time - none of
    /// which is true of the one-shots.
    /// </para>
    /// <para>
    /// <strong>Two sources, so a change of theme is a crossfade.</strong> The themes switch
    /// when the player changes vehicle, which happens mid-match, at a moment they chose - a
    /// hard cut there sounds like a bug in the game rather than a change of character. A
    /// single source could only fade out and then in, leaving a hole; two overlap for
    /// <see cref="FadeSeconds"/> and neither one is ever heard starting.
    /// </para>
    /// <para>
    /// The fade runs on unscaled time on purpose. Pausing sets
    /// <see cref="Time.timeScale"/> to zero, and a crossfade that froze halfway through
    /// would leave two themes playing at half volume for as long as the panel was up.
    /// </para>
    /// <para>
    /// Mechanism only: it is told what to play and knows nothing about why.
    /// <see cref="MatchMusic"/> is the half that decides.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Music Player")]
    public sealed class MusicPlayer : MonoBehaviour
    {
        /// <summary>Seconds one theme takes to give way to the next.</summary>
        /// <remarks>
        /// Long enough to be a transition rather than a cut, short enough that a player who
        /// swapped vehicles deliberately hears the answer to what they just did. Both themes
        /// are audible for all of it, which is why they are written in one key.
        /// </remarks>
        public const float FadeSeconds = 0.9f;

        /// <summary>Name of the first of the two generated sources.</summary>
        public const string FirstNodeName = "Music A";

        /// <summary>Name of the second of the two generated sources.</summary>
        public const string SecondNodeName = "Music B";

        [SerializeField]
        [Tooltip("Every clip the game can play. Built by Tools > IronFlag > Build Audio Catalog.")]
        private AudioCatalog catalog;

        private static MusicPlayer current;

        private AudioSource playing;
        private AudioSource leaving;
        private MusicKind wanted = MusicKind.None;
        private float fade = 1.0f;
        private float leavingStartVolume = 0.0f;

        /// <summary>The music player in the scene, or null when the scene has none.</summary>
        public static MusicPlayer Current => current;

        /// <summary>What is playing, or being faded up to.</summary>
        public MusicKind Playing => wanted;

        /// <summary>Whether a crossfade is still running.</summary>
        public bool IsFading => fade < 1.0f;

        /// <summary>
        /// Points the player at the clips it is allowed to play.
        /// </summary>
        /// <param name="from">The catalog.</param>
        /// <remarks>Called by the scene builders; nothing assigns it by hand.</remarks>
        public void Configure(AudioCatalog from) => catalog = from;

        /// <summary>
        /// Fades into a new piece of music.
        /// </summary>
        /// <param name="kind">What to play; <see cref="MusicKind.None"/> fades out to silence.</param>
        /// <remarks>
        /// Asking for what is already playing does nothing at all, which is what lets the
        /// callers say what they want every frame without having to remember what they said
        /// last. That is the whole reason <see cref="MatchMusic"/> can be a rule rather than
        /// a state machine.
        /// </remarks>
        public void Play(MusicKind kind)
        {
            if (kind == wanted)
            {
                return;
            }

            wanted = kind;
            Swap();

            AudioClip clip = catalog == null ? null : catalog.ClipFor(kind);
            if (clip == null)
            {
                // Nothing to fade up to, but the outgoing theme still has to leave - a
                // missing row should make the music stop, not make it stick.
                return;
            }

            playing.clip = clip;
            playing.loop = AudioRoster.Loops(kind);
            playing.volume = 0.0f;
            playing.Play();
        }

        /// <summary>
        /// Fades out whatever is playing.
        /// </summary>
        public void Stop() => Play(MusicKind.None);

        /// <summary>
        /// Puts the current theme back to silence immediately, with no fade.
        /// </summary>
        /// <remarks>
        /// For leaving a scene, where a fade has nothing left to run in. Nothing during a
        /// match uses it.
        /// </remarks>
        public void Silence()
        {
            wanted = MusicKind.None;
            fade = 1.0f;

            if (playing != null)
            {
                playing.Stop();
                playing.clip = null;
            }

            if (leaving != null)
            {
                leaving.Stop();
                leaving.clip = null;
            }
        }

        /// <summary>
        /// Makes the incoming source the outgoing one and starts the fade over.
        /// </summary>
        /// <remarks>
        /// Captures the outgoing source's actual volume rather than assuming it was at full
        /// level: a theme changed again before its own fade-in finished is still only
        /// partway up, and starting its fade-out from an assumed full level would jump it
        /// louder for a frame before it decayed.
        /// </remarks>
        private void Swap()
        {
            AudioSource was = playing;
            playing = leaving;
            leaving = was;
            leavingStartVolume = was == null ? 0.0f : was.volume;
            fade = 0.0f;
        }

        private void Awake()
        {
            current = this;
            playing = Build(FirstNodeName);
            leaving = Build(SecondNodeName);
        }

        private void OnDestroy()
        {
            if (current == this)
            {
                current = null;
            }
        }

        /// <summary>
        /// Creates one of the two music sources.
        /// </summary>
        /// <param name="name">What to call it in the hierarchy.</param>
        /// <returns>The source.</returns>
        private AudioSource Build(string name)
        {
            var host = new GameObject(name);
            host.transform.SetParent(transform, false);

            var made = host.AddComponent<AudioSource>();
            made.playOnAwake = false;
            made.loop = true;
            made.spatialBlend = 0.0f;
            made.volume = 0.0f;
            return made;
        }

        private void Update()
        {
            if (fade < 1.0f)
            {
                fade = FadeSeconds <= 0.0f
                    ? 1.0f
                    : Mathf.Min(1.0f, fade + (Time.unscaledDeltaTime / FadeSeconds));
            }

            float level = GameSettings.MusicVolume;

            if (playing != null)
            {
                playing.volume = level * fade;
            }

            if (leaving != null)
            {
                leaving.volume = leavingStartVolume * (1.0f - fade);
                if (fade >= 1.0f && leaving.isPlaying)
                {
                    // Stopped rather than left at zero volume: these clips stream from disk,
                    // and a finished fade should give the file handle back.
                    leaving.Stop();
                    leaving.clip = null;
                }
            }
        }
    }
}
