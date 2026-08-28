using System;
using UnityEditor;
using UnityEngine;

namespace IronFlag.Editor.ArtPipeline
{
    /// <summary>
    /// Applies IronFlag's audio import settings to everything under
    /// <c>Assets/RF/Audio</c>, so that a freshly rendered clip is configured correctly the
    /// moment it lands in the project.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rest of this project commits generated assets together with a hand-written
    /// <c>.meta</c> file. Audio does not, because an <c>AudioImporter</c> has considerably
    /// more settings than a model importer and hand-authoring that YAML is a good way to
    /// end up with clips that are subtly wrong in a built player. Encoding the rules here
    /// instead means the settings are reviewable code, they apply to every clip
    /// automatically, and a re-render never silently reverts them.
    /// </para>
    /// <para>
    /// The two folders are treated differently for one reason: length. SFX are all under
    /// three seconds, so they are stored uncompressed and decompressed once at load - there
    /// is no CPU cost when a gun fires, which matters when the chaingun fires several times
    /// a second. Music runs to twenty-odd seconds in stereo, which is far too much to hold
    /// in memory uncompressed, so it streams from disk instead.
    /// </para>
    /// <para>
    /// Note that looping is deliberately not set here. Unity has no per-clip loop flag - it
    /// is a property of the <c>AudioSource</c> that plays the clip. The engine loops and
    /// music beds are rendered to loop seamlessly (see <c>audio/sounds/vehicles.scd</c>),
    /// but whatever plays them still has to set <c>AudioSource.loop</c>.
    /// </para>
    /// </remarks>
    public sealed class AudioImportSettings : AssetPostprocessor
    {
        /// <summary>Project-relative folder holding every clip this rule applies to.</summary>
        public const string AudioRoot = SuperColliderAudioPipeline.AudioOutputFolder + "/";

        /// <summary>Sub-folder holding short, non-positional one-shots.</summary>
        public const string SfxFolder = AudioRoot + "SFX/";

        /// <summary>Sub-folder holding the menu theme, per-vehicle themes and end cues.</summary>
        public const string MusicFolder = AudioRoot + "Music/";

        /// <summary>
        /// Configures a clip as it is imported, before Unity builds its representation.
        /// </summary>
        /// <remarks>
        /// Runs for every audio asset in the project, including any that arrive inside a
        /// package, so it returns immediately for anything outside <see cref="AudioRoot"/>.
        /// </remarks>
        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(AudioRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var importer = assetImporter as AudioImporter;
            if (importer == null)
            {
                return;
            }

            bool isMusic = assetPath.StartsWith(MusicFolder, StringComparison.OrdinalIgnoreCase);
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;

            if (isMusic)
            {
                // Long and stereo: stream it rather than paying for it in memory, and let
                // it decode off the main thread so opening a menu does not hitch.
                settings.loadType = AudioClipLoadType.Streaming;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.7f;
                settings.preloadAudioData = false;
                importer.forceToMono = false;
                importer.loadInBackground = true;
            }
            else
            {
                // Short and mono: keep it uncompressed so firing a weapon costs nothing at
                // play time, and have it resident before the match starts.
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.PCM;
                settings.preloadAudioData = true;

                // Deliberately NOT forceToMono, even though every SFX is meant to be mono.
                // Unity pairs that setting with a Normalize flag that rescales the result,
                // and these clips are rendered at levels chosen relative to each other - a
                // menu click is quiet and a cannon is loud on purpose. Normalising would
                // flatten all of that to full scale and throw the mix away. The renderer
                // already guarantees mono here (see the channel fold in audio/rf/engine.scd),
                // so the conversion has nothing to do anyway; OnPostprocessAudio below
                // checks the guarantee held rather than re-imposing it.
                importer.forceToMono = false;
                importer.loadInBackground = false;
            }

            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings;
        }

        /// <summary>
        /// Checks what actually landed, rather than assuming the renderer got it right.
        /// </summary>
        /// <param name="clip">The clip Unity has just finished importing.</param>
        /// <remarks>
        /// A stereo SFX clip is worth catching because Unity cannot pan one in 3D at all -
        /// it would quietly become the single sound in the game that ignores positioning,
        /// which is close to impossible to notice by ear in a firefight. Since
        /// <see cref="OnPreprocessAudio"/> deliberately does not force the conversion, this
        /// says so instead of hiding it.
        /// </remarks>
        private void OnPostprocessAudio(AudioClip clip)
        {
            if (!assetPath.StartsWith(SfxFolder, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (clip.channels != 1)
            {
                Debug.LogWarning(
                    $"IronFlag: {assetPath} imported with {clip.channels} channels; SFX must be mono "
                    + "or Unity cannot position them. Set channels: 1 on its recipe and re-render.");
            }
        }
    }
}
