using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using IronFlag.Audio;
using IronFlag.Editor.ArtPipeline;

namespace IronFlag.Editor.Gameplay
{
    /// <summary>
    /// Builds the <see cref="AudioCatalog"/> the game plays its clips out of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same job, and the same reason, as <see cref="LevelCatalogBuilder"/>: a built
    /// player has no asset database, so the references have to be serialised into an asset
    /// ahead of time. What is different is where the names come from - a level catalog is
    /// filled from a roster of prefabs somebody wrote down, and this one is filled from
    /// <see cref="AudioRoster.AssetNameOf(SfxKind)"/>, which computes them.
    /// </para>
    /// <para>
    /// So there is nothing here to keep in step with the recipes. A sound is added by
    /// writing a <c>.scd</c> recipe and an enum value with the same name, rendering, and
    /// pressing this; a sound whose recipe was renamed shows up as a missing row the moment
    /// this runs, with the file it was looking for spelled out.
    /// </para>
    /// </remarks>
    public static class AudioCatalogBuilder
    {
        /// <summary>Folder the catalog lives in.</summary>
        public const string Folder = SuperColliderAudioPipeline.AudioOutputFolder;

        /// <summary>Path of the catalog asset.</summary>
        public const string CatalogPath = Folder + "/AudioCatalog.asset";

        /// <summary>Folder the sound effects are read from.</summary>
        public const string SfxFolder = Folder + "/SFX";

        /// <summary>Folder the music is read from.</summary>
        public const string MusicFolder = Folder + "/Music";

        /// <summary>
        /// Rebuilds the audio catalog from whatever the audio pipeline last rendered.
        /// </summary>
        [MenuItem("Tools/IronFlag/Build Audio Catalog", false, 156)]
        public static void BuildAndSave()
        {
            AudioCatalog catalog = Build();
            if (catalog == null)
            {
                return;
            }

            foreach (string problem in catalog.Problems())
            {
                Debug.LogWarning($"IronFlag: {problem}");
            }

            Debug.Log($"IronFlag: audio catalog saved to {CatalogPath}");
        }

        /// <summary>
        /// Creates the catalog if it is missing, and refreshes it either way.
        /// </summary>
        /// <returns>The catalog asset.</returns>
        public static AudioCatalog Build()
        {
            GeneratedMaterials.EnsureAssetFolder(Folder);

            AudioCatalog catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<AudioCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            var sounds = new List<AudioSfxClip>();
            foreach (SfxKind kind in AudioRoster.Sounds())
            {
                sounds.Add(new AudioSfxClip
                {
                    Kind = kind,
                    Clip = Load(SfxFolder, AudioRoster.AssetNameOf(kind)),
                });
            }

            var music = new List<AudioMusicClip>();
            foreach (MusicKind kind in AudioRoster.Themes())
            {
                music.Add(new AudioMusicClip
                {
                    Kind = kind,
                    Clip = Load(MusicFolder, AudioRoster.AssetNameOf(kind)),
                });
            }

            catalog.Configure(sounds, music);

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return catalog;
        }

        /// <summary>
        /// Loads the catalog, building it first if it is not there.
        /// </summary>
        /// <returns>The catalog asset.</returns>
        public static AudioCatalog Load()
        {
            AudioCatalog catalog = AssetDatabase.LoadAssetAtPath<AudioCatalog>(CatalogPath);
            return catalog == null ? Build() : catalog;
        }

        /// <summary>
        /// Puts the thing that makes a noise into the scene being built.
        /// </summary>
        /// <param name="withMusic">Whether this scene has a soundtrack as well as sounds.</param>
        /// <returns>The director that was created.</returns>
        /// <remarks>
        /// <para>
        /// Here rather than in each of the three scene builders, because all three want the
        /// identical object and the only thing they disagree about is whether it has music.
        /// The match and the menu do; the level editor does not - it is a workspace, and a
        /// soundtrack under somebody dragging a coastline about for an hour is a soundtrack
        /// they turn off.
        /// </para>
        /// <para>
        /// <strong>Nothing here touches the audio listener.</strong> There is still exactly
        /// one in the whole split-screen rig, on seat one's camera, which
        /// <c>SandboxWiringTests</c> enforces - and one ear serving two seats is what
        /// <see cref="AudioMixdown"/> is for. A director is not a listener; it is the thing
        /// holding the clips.
        /// </para>
        /// </remarks>
        public static AudioDirector AddToScene(bool withMusic)
        {
            AudioCatalog catalog = Load();

            var host = new GameObject("Audio");
            AudioDirector director = host.AddComponent<AudioDirector>();
            director.Configure(catalog);

            if (withMusic)
            {
                host.AddComponent<MusicPlayer>().Configure(catalog);
            }

            return director;
        }

        /// <summary>
        /// Loads one rendered clip by name.
        /// </summary>
        /// <param name="folder">Folder it should be in.</param>
        /// <param name="name">Asset name, without extension.</param>
        /// <returns>The clip, or <c>null</c> when nothing of that name has been rendered.</returns>
        /// <remarks>
        /// Silent about a miss, because <see cref="AudioCatalog.Problems"/> is what reports
        /// one and it says which sound is affected rather than which file is absent - the
        /// first is what somebody can act on.
        /// </remarks>
        private static AudioClip Load(string folder, string name)
            => AssetDatabase.LoadAssetAtPath<AudioClip>($"{folder}/{name}.wav");
    }
}
