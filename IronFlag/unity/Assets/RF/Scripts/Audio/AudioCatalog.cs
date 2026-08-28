using System.Collections.Generic;
using UnityEngine;

namespace IronFlag.Audio
{
    /// <summary>
    /// Every clip the game can play, and which sound each one is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same asset, for the same reason, as <see cref="IronFlag.Levels.LevelCatalog"/>:
    /// a built player has no asset database, so nothing at run time can look a clip up by
    /// path. The references are serialised once, here, and every scene that makes a noise
    /// carries this asset on its <see cref="AudioDirector"/>.
    /// </para>
    /// <para>
    /// Built and refreshed by <c>Tools &gt; IronFlag &gt; Build Audio Catalog</c>, which
    /// fills it by name from whatever the SuperCollider pipeline last rendered. Nothing here
    /// is hand-assigned - a catalog somebody dragged clips into is a catalog that silently
    /// loses a row when a recipe is renamed, and the symptom is a gun that stops making a
    /// noise for no reason anybody can see.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "AudioCatalog", menuName = "IronFlag/Audio Catalog")]
    public sealed class AudioCatalog : ScriptableObject
    {
        [Header("Sound effects")]
        [SerializeField]
        [Tooltip("One row per sound the game can make.")]
        private List<AudioSfxClip> sounds = new List<AudioSfxClip>();

        [Header("Music")]
        [SerializeField]
        [Tooltip("One row per piece of music.")]
        private List<AudioMusicClip> music = new List<AudioMusicClip>();

        /// <summary>
        /// Points the catalog at every clip it hands out.
        /// </summary>
        /// <param name="sfxRows">One row per sound.</param>
        /// <param name="musicRows">One row per piece of music.</param>
        /// <remarks>Called by the editor's catalog builder; nothing assigns these by hand.</remarks>
        public void Configure(List<AudioSfxClip> sfxRows, List<AudioMusicClip> musicRows)
        {
            sounds = sfxRows == null ? new List<AudioSfxClip>() : sfxRows;
            music = musicRows == null ? new List<AudioMusicClip>() : musicRows;
        }

        /// <summary>
        /// Returns the clip one sound is played from.
        /// </summary>
        /// <param name="kind">Sound to look up.</param>
        /// <returns>The clip, or <c>null</c> when the catalog has no row for it.</returns>
        /// <remarks>
        /// Silence rather than a warning per call. A missing row is a real fault, but the
        /// place to say so once is <see cref="Problems"/> - a chaingun firing eight times a
        /// second into an empty row would otherwise fill the console faster than anybody
        /// could read it.
        /// </remarks>
        public AudioClip ClipFor(SfxKind kind)
        {
            foreach (AudioSfxClip row in sounds)
            {
                if (row != null && row.Kind == kind)
                {
                    return row.Clip;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the clip one piece of music is played from.
        /// </summary>
        /// <param name="kind">Music to look up.</param>
        /// <returns>The clip, or <c>null</c> when the catalog has no row for it.</returns>
        public AudioClip ClipFor(MusicKind kind)
        {
            foreach (AudioMusicClip row in music)
            {
                if (row != null && row.Kind == kind)
                {
                    return row.Clip;
                }
            }

            return null;
        }

        /// <summary>
        /// Lists everything the catalog is missing.
        /// </summary>
        /// <returns>One sentence per gap; empty when the game can be played from this.</returns>
        /// <remarks>
        /// Read by the tests and by the builder, so a catalog that was never rebuilt after a
        /// recipe was renamed says so once, by name, rather than producing a game that is
        /// quietly missing a noise.
        /// </remarks>
        public List<string> Problems()
        {
            var problems = new List<string>();

            foreach (SfxKind kind in AudioRoster.Sounds())
            {
                if (ClipFor(kind) == null)
                {
                    problems.Add($"The catalog has no clip for {kind}, so "
                        + $"{AudioRoster.AssetNameOf(kind)} would never be heard.");
                }
            }

            foreach (MusicKind kind in AudioRoster.Themes())
            {
                if (ClipFor(kind) == null)
                {
                    problems.Add($"The catalog has no clip for {kind}, so "
                        + $"{AudioRoster.AssetNameOf(kind)} would never be heard.");
                }
            }

            return problems;
        }
    }
}
