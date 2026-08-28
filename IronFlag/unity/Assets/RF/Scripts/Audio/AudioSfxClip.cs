using System;
using UnityEngine;

namespace IronFlag.Audio
{
    /// <summary>
    /// One row of <see cref="AudioCatalog"/>: a sound and the clip it is played from.
    /// </summary>
    /// <remarks>
    /// A class rather than a dictionary because Unity does not serialise dictionaries, which
    /// is the same reason <see cref="IronFlag.Levels.LevelStructurePrefab"/> exists.
    /// </remarks>
    [Serializable]
    public sealed class AudioSfxClip
    {
        /// <summary>Which sound this row is for.</summary>
        public SfxKind Kind = SfxKind.None;

        /// <summary>The clip it is played from.</summary>
        public AudioClip Clip;
    }
}
