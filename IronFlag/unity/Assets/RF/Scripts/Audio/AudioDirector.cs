using System.Collections.Generic;
using UnityEngine;
using IronFlag.Core;
using IronFlag.Menu;

namespace IronFlag.Audio
{
    /// <summary>
    /// The one thing in a scene that makes a noise: it holds the catalog, owns the source
    /// every sound effect comes out of, and works out how loud each one should be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One per scene, placed by the scene builders alongside the other session-wide pieces,
    /// and found statically through <see cref="Current"/> - the same shape
    /// <see cref="IronFlag.Objective.Match"/> uses, and for the same reason: a shot fired
    /// anywhere in the world needs to reach it without carrying a reference to it through
    /// six components that have no interest in sound.
    /// </para>
    /// <para>
    /// <strong>A scene with no director is silent, not broken.</strong> Every path in and
    /// out of here tolerates its own absence, because the art previews, the prefab builders
    /// and most of the test suite assemble a vehicle and fire it without ever building a
    /// scene to hear it in. That is the same bargain <c>MuzzleFlash.Spawn</c> and
    /// <c>GroundMark.Spawn</c> already make.
    /// </para>
    /// <para>
    /// One <see cref="AudioSource"/> serves every one-shot in the game.
    /// <see cref="AudioSource.PlayOneShot"/> mixes rather than interrupts, so a chaingun
    /// firing over the top of a collapsing wall needs no pool and no voice stealing; what a
    /// pool would add is a way for two sounds to end up at different volumes by accident.
    /// The source is flat - <c>spatialBlend</c> at zero - and the positioning that would
    /// have done is done arithmetically instead by <see cref="AudioMixdown"/>, which
    /// explains why.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Audio Director")]
    public sealed class AudioDirector : MonoBehaviour
    {
        /// <summary>Name of the generated child the sound effects come out of.</summary>
        public const string SourceNodeName = "Sound Effects";

        [SerializeField]
        [Tooltip("Every clip the game can play. Built by Tools > IronFlag > Build Audio Catalog.")]
        private AudioCatalog catalog;

        private static AudioDirector current;

        private AudioSource source;
        private SfxKind last = SfxKind.None;
        private int played;

        /// <summary>The director in the scene, or null when nothing is listening.</summary>
        public static AudioDirector Current => current;

        /// <summary>Every clip this scene can play.</summary>
        public AudioCatalog Catalog => catalog;

        /// <summary>The last sound that actually reached the speakers.</summary>
        /// <remarks>
        /// For the tests. There is no way to ask Unity what came out of a mixed one-shot
        /// source, and "the gun fired" is not the same claim as "the gun was heard" - the
        /// interesting failures are a missing catalog row and a shot too far away to play.
        /// Nothing in the game reads either of these.
        /// </remarks>
        public SfxKind LastSound => last;

        /// <summary>How many sounds this director has played.</summary>
        public int SoundsPlayed => played;

        /// <summary>
        /// Points the director at the clips it is allowed to play.
        /// </summary>
        /// <param name="from">The catalog.</param>
        /// <remarks>Called by the scene builders; nothing assigns it by hand.</remarks>
        public void Configure(AudioCatalog from) => catalog = from;

        /// <summary>
        /// Plays one sound at a chosen volume.
        /// </summary>
        /// <param name="kind">Sound to play.</param>
        /// <param name="loudness">Volume scale in 0..1, before the player's own setting.</param>
        /// <returns><c>true</c> when something was actually heard.</returns>
        /// <remarks>
        /// A loudness of zero is refused rather than played silently, so that a sound
        /// happening off the far end of the map costs nothing at all - and so that
        /// <see cref="LastSound"/> means "heard" rather than "attempted".
        /// </remarks>
        public bool Play(SfxKind kind, float loudness)
        {
            if (kind == SfxKind.None || catalog == null)
            {
                return false;
            }

            float level = Mathf.Clamp01(loudness) * GameSettings.SoundVolume;
            if (level <= 0.0f)
            {
                return false;
            }

            AudioClip clip = catalog.ClipFor(kind);
            if (clip == null || Source == null)
            {
                return false;
            }

            Source.PlayOneShot(clip, level);
            last = kind;
            played++;
            return true;
        }

        /// <summary>
        /// Returns how loud something happening at a point on the map should be.
        /// </summary>
        /// <param name="at">Where it is happening, in world space.</param>
        /// <returns>A volume scale in 0..1.</returns>
        /// <remarks>
        /// <para>
        /// Measured to the nearest seat, so on a split screen a shell landing in front of
        /// either player is loud for both. That is not a compromise - it is the correct
        /// answer for two people sharing one pair of speakers, and the alternative (an
        /// average, or the first seat's distance) makes half the events on screen quieter
        /// than they look.
        /// </para>
        /// <para>
        /// A scene with no seats - the menu, the level editor, a test rig - hears everything
        /// at full volume. There is no view to be far from.
        /// </para>
        /// </remarks>
        public static float LoudnessAt(Vector3 at)
        {
            IReadOnlyList<TopDownCameraRig> seats = TopDownCameraRig.Seats;
            if (seats.Count == 0)
            {
                return 1.0f;
            }

            float nearest = float.MaxValue;
            foreach (TopDownCameraRig seat in seats)
            {
                if (seat == null)
                {
                    continue;
                }

                float metres = Vector3.Distance(seat.Focus, at);
                nearest = Mathf.Min(nearest, metres);
            }

            return nearest == float.MaxValue ? 1.0f : AudioMixdown.Loudness(nearest);
        }

        /// <summary>The source every sound effect comes out of, built on first use.</summary>
        private AudioSource Source
        {
            get
            {
                if (source == null)
                {
                    var host = new GameObject(SourceNodeName);
                    host.transform.SetParent(transform, false);
                    source = host.AddComponent<AudioSource>();
                    source.playOnAwake = false;
                    source.loop = false;

                    // Flat, always. See AudioMixdown for the whole argument; the short
                    // version is that one listener cannot pan for two seats, so nothing here
                    // pans for either of them.
                    source.spatialBlend = 0.0f;
                    source.dopplerLevel = 0.0f;

                    // The mix is the levels the clips were rendered at, times the distance
                    // and the player's setting. Nothing scales it a fourth time - and in
                    // particular nothing here opts out of the listener's own volume, which is
                    // what lets the test suite mute the game without changing what it decides.
                    source.volume = 1.0f;
                }

                return source;
            }
        }

        private void Awake() => current = this;

        private void OnDestroy()
        {
            if (current == this)
            {
                current = null;
            }
        }
    }
}
