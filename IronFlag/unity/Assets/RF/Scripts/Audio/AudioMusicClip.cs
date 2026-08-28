using System;
using UnityEngine;

namespace IronFlag.Audio
{
    /// <summary>
    /// One row of <see cref="AudioCatalog"/>: a piece of music and the clip it is played
    /// from.
    /// </summary>
    [Serializable]
    public sealed class AudioMusicClip
    {
        /// <summary>Which piece of music this row is for.</summary>
        public MusicKind Kind = MusicKind.None;

        /// <summary>The clip it is played from.</summary>
        public AudioClip Clip;
    }
}
