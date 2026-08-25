using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using IronFlag.Core;

namespace IronFlag.Editor.ArtPipeline
{
    /// <summary>
    /// Writes the project's default volume profile from <see cref="PostTuning"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The profile at <see cref="ProfilePath"/> is what URP hands every camera as the state
    /// of every post-processing effect before any volume in a scene has its say, and it was
    /// Unity's untouched template: every effect at zero, plus seven leftover components from
    /// the render pipeline's own test suites whose scripts are not in this project at all.
    /// Generating it makes the settings a diff of C# rather than a diff of YAML with file IDs
    /// in it, in the same arrangement as <see cref="GeneratedMaterials"/> - and, once there is
    /// a second lighting condition to build a second profile for, it makes that a loop rather
    /// than a second asset to keep in step by hand.
    /// </para>
    /// <para>
    /// <strong>The asset is rewritten in place and never recreated</strong>, because
    /// <c>UniversalRenderPipelineGlobalSettings.asset</c> points at it by GUID.
    /// <see cref="AssetDatabase.CreateAsset"/> over an existing path deletes the old asset
    /// first, which takes its <c>.meta</c> and its GUID with it - and the failure that causes
    /// is not an error, it is every post-processing effect quietly reverting to its class
    /// default with nothing in the console.
    /// </para>
    /// <para>
    /// Only the effects this game actually uses are written. Everything left out - depth of
    /// field, motion blur, film grain, lens distortion and the rest - falls back to its own
    /// class default, which is off, so the shorter asset and the longer one describe the same
    /// frame. The short one can be read.
    /// </para>
    /// </remarks>
    public static class VolumeProfileBuilder
    {
        /// <summary>Where the profile URP hands to every camera lives.</summary>
        public const string ProfilePath = "Assets/Settings/DefaultVolumeProfile.asset";

        /// <summary>
        /// Rebuilds the default volume profile and saves it.
        /// </summary>
        [MenuItem("Tools/IronFlag/Build Volume Profile", false, 157)]
        public static void BuildAndSave()
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                Debug.LogError($"IronFlag: no volume profile at {ProfilePath}. It is referenced "
                    + "by GUID from the pipeline's global settings, so it is rebuilt in place "
                    + "rather than created; restore it from source control first.");
                return;
            }

            Clear(profile);
            Fill(profile);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"IronFlag: wrote {profile.components.Count} effects to {ProfilePath}.");
        }

        /// <summary>
        /// Strips every effect off a profile, sub-assets and all.
        /// </summary>
        /// <param name="profile">The profile to empty.</param>
        /// <remarks>
        /// The list is cleared rather than emptied through <see cref="VolumeProfile.Remove(System.Type)"/>,
        /// because this profile shipped holding two components whose scripts do not exist in
        /// this project, and asking a missing script what type it is throws.
        /// </remarks>
        private static void Clear(VolumeProfile profile)
        {
            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(ProfilePath))
            {
                if (sub == null || sub == profile)
                {
                    continue;
                }

                AssetDatabase.RemoveObjectFromAsset(sub);
                Object.DestroyImmediate(sub, true);
            }

            profile.components.Clear();
        }

        /// <summary>
        /// Adds the effects this game uses, set from <see cref="PostTuning"/>.
        /// </summary>
        /// <param name="profile">The profile to write.</param>
        private static void Fill(VolumeProfile profile)
        {
            Tonemapping tone = Add<Tonemapping>(profile);
            tone.mode.Override(PostTuning.Tonemapping);

            Bloom bloom = Add<Bloom>(profile);
            bloom.threshold.Override(PostTuning.BloomThreshold);
            bloom.intensity.Override(PostTuning.BloomIntensity);
            bloom.scatter.Override(PostTuning.BloomScatter);
            bloom.tint.Override(PostTuning.BloomTint);
            bloom.highQualityFiltering.Override(true);

            ColorAdjustments grade = Add<ColorAdjustments>(profile);
            grade.postExposure.Override(PostTuning.PostExposure);
            grade.contrast.Override(PostTuning.Contrast);
            grade.saturation.Override(PostTuning.Saturation);

            Vignette vignette = Add<Vignette>(profile);
            vignette.color.Override(PostTuning.VignetteColour);
            vignette.intensity.Override(PostTuning.VignetteIntensity);
            vignette.smoothness.Override(PostTuning.VignetteSmoothness);
        }

        /// <summary>
        /// Adds one effect to a profile as a sub-asset of it.
        /// </summary>
        /// <typeparam name="T">The effect to add.</typeparam>
        /// <param name="profile">The profile to add it to.</param>
        /// <returns>The effect, with nothing overridden yet.</returns>
        /// <remarks>
        /// <see cref="VolumeProfile.Add{T}"/> creates the component and puts it in the list
        /// but does not make it part of the asset on disk - that is the editor's job, and
        /// forgetting it writes a profile whose components all load back as null.
        /// </remarks>
        private static T Add<T>(VolumeProfile profile)
            where T : VolumeComponent
        {
            T component = profile.Add<T>();
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }
    }
}
