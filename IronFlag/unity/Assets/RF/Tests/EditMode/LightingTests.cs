using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using IronFlag.Core;
using IronFlag.Editor.ArtPipeline;
using IronFlag.UI;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// Checks the look: the lighting table, what it does to a scene, and the two pieces of
    /// pipeline configuration it cannot set for itself.
    /// </summary>
    /// <remarks>
    /// The last two are the ones worth having. Shadow distance and the volume profile both
    /// live in hand-editable assets that nothing in code reads back, so the way they break is
    /// that somebody opens the inspector, drags a slider to see what it does, and saves - and
    /// the game ships looking different with no diff anybody reviewed. Asserting the assets
    /// still say what the tables say turns that from invisible into a red test.
    /// </remarks>
    public sealed class LightingTests
    {
        private const float Tolerance = 0.0005f;

        [Test]
        public void AnUnnamedConditionIsLitLikeDaylight()
        {
            LightingTuning fallback = LightingTuning.For(LightingMood.None);
            LightingTuning daylight = LightingTuning.For(LightingMood.Daylight);

            Assert.That(fallback.SunPitch, Is.EqualTo(daylight.SunPitch));
            Assert.That(fallback.SunColour, Is.EqualTo(daylight.SunColour));
            Assert.That(fallback.Fog, Is.EqualTo(daylight.Fog));
        }

        /// <summary>
        /// Callers stamp and edit their copy - both overhead views drop the fog that way - so
        /// handing out a shared instance would have one of them restyling the game.
        /// </summary>
        [Test]
        public void EachAskForAConditionAnswersWithAFreshCopy()
        {
            LightingTuning first = LightingTuning.For(LightingMood.Daylight);
            first.Fog = false;

            Assert.That(LightingTuning.For(LightingMood.Daylight).Fog, Is.True);
        }

        [Test]
        public void TheArtPreviewIsLitDifferentlyFromTheGame()
        {
            LightingTuning studio = LightingTuning.For(LightingMood.Studio);
            LightingTuning daylight = LightingTuning.For(LightingMood.Daylight);

            Assert.That(studio.SunPitch, Is.Not.EqualTo(daylight.SunPitch));
            Assert.That(studio.Fog, Is.False, "a backdrop has no far edge to lose");
        }

        /// <summary>
        /// Asserts against the light created here by name, rather than merely against
        /// whichever one <see cref="LightingRig.Sun"/> returns, because a scene that already
        /// has a directional light in it - left behind by a scene-building tool an earlier
        /// test in the run called - is exactly the state this test used to fail under: two
        /// directional lights sharing one scene, with no fact <see cref="LightingRig.Sun"/>
        /// can see that says which one this test means. Its own remarks are explicit that it
        /// cannot resolve that - it can only be repeatable about it - so this test resolves it
        /// instead, by clearing every directional light it finds before making its own. What
        /// is asserted then really is "the rig configured the light this test cares about",
        /// not "the rig configured whichever light survived to be asked about".
        /// </summary>
        [Test]
        public void ApplyingAConditionPutsItOnTheScene()
        {
            // See the remarks above: a light this test did not make, still around from
            // whatever ran earlier in the same batch, is the ambiguity LightingRig.Sun()
            // cannot be asked to resolve - only avoided.
            foreach (Light stray in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                if (stray.type == LightType.Directional)
                {
                    Object.DestroyImmediate(stray.gameObject);
                }
            }

            var host = new GameObject("Test Sun", typeof(Light));
            host.GetComponent<Light>().type = LightType.Directional;

            try
            {
                LightingTuning lighting = LightingTuning.For(LightingMood.Daylight);
                LightingRig.Apply(lighting, null);

                Light sun = LightingRig.Sun();
                Assert.That(
                    sun,
                    Is.SameAs(host.GetComponent<Light>()),
                    "the rig picked a leftover light instead of the one just created for this scene");
                Assert.That(sun.intensity, Is.EqualTo(lighting.SunIntensity).Within(Tolerance));
                Assert.That(sun.shadows, Is.EqualTo(LightShadows.Soft));
                Assert.That(
                    sun.transform.rotation.eulerAngles.x,
                    Is.EqualTo(lighting.SunPitch).Within(0.01f));

                Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Trilight));
                Assert.That(RenderSettings.ambientSkyColor, Is.EqualTo(lighting.AmbientSky));
                Assert.That(RenderSettings.fog, Is.True);
                Assert.That(
                    RenderSettings.fogStartDistance, Is.EqualTo(lighting.FogStart).Within(Tolerance));

                // The procedural sky draws its disc wherever this points, and Unity's own
                // guess is whichever directional light is brightest.
                Assert.That(RenderSettings.sun, Is.SameAs(sun));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// The overhead views turn the haze off on their copy, and the thing that would go
        /// wrong quietly is applying it anyway.
        /// </summary>
        [Test]
        public void AConditionWithNoHazeLeavesTheFogOff()
        {
            LightingTuning lighting = LightingTuning.For(LightingMood.Daylight);
            lighting.Fog = false;

            LightingRig.Apply(lighting, null);

            Assert.That(RenderSettings.fog, Is.False);
        }

        [Test]
        public void PaintingASkyIsSafeOnAMaterialThatIsNotOne()
        {
            var material = new Material(Shader.Find("Sprites/Default"));

            try
            {
                Assert.DoesNotThrow(
                    () => LightingRig.Paint(material, LightingTuning.For(LightingMood.Daylight)));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void EveryConditionHasASkyGeneratedForIt()
        {
            foreach (LightingMood mood in System.Enum.GetValues(typeof(LightingMood)))
            {
                if (mood == LightingMood.None)
                {
                    continue;
                }

                string path = GeneratedMaterials.PathOf(GeneratedMaterials.SkyMaterial(mood));
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<Material>(path),
                    Is.Not.Null,
                    $"{path} is missing; run Tools > IronFlag > Rebuild All Art from Blender");
            }
        }

        /// <summary>
        /// URP reads its own asset and ignores <see cref="QualitySettings.shadowDistance"/>,
        /// so the number that matters is one nothing in code sets. This is the guard on it.
        /// </summary>
        [Test]
        public void ThePipelineShadowsReachAsFarAsTheCameraSees()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Assert.That(pipeline, Is.Not.Null, "the project is not on URP any more");

            Assert.That(
                pipeline.shadowDistance,
                Is.EqualTo(LightingRig.ShadowDistance).Within(Tolerance),
                $"{pipeline.name} disagrees with LightingRig.ShadowDistance");
        }

        [Test]
        public void ThePipelineKeepsTheHdrBufferBloomNeeds()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            Assert.That(pipeline, Is.Not.Null);
            Assert.That(
                pipeline.supportsHDR,
                Is.True,
                "without HDR nothing is ever brighter than 1, so nothing ever blooms");
        }

        /// <summary>
        /// The profile is what URP hands every camera before any scene volume has its say.
        /// It is also an asset somebody can drag a slider in.
        /// </summary>
        [Test]
        public void TheVolumeProfileSaysWhatTheTuningSays()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(
                VolumeProfileBuilder.ProfilePath);

            Assert.That(
                profile,
                Is.Not.Null,
                $"{VolumeProfileBuilder.ProfilePath} is missing");

            Assert.That(
                profile.TryGet(out Tonemapping tone), Is.True, "no tone curve, so emissives clip");
            Assert.That(tone.mode.value, Is.EqualTo(PostTuning.Tonemapping));

            Assert.That(profile.TryGet(out Bloom bloom), Is.True);
            Assert.That(
                bloom.intensity.value, Is.EqualTo(PostTuning.BloomIntensity).Within(Tolerance));
            Assert.That(
                bloom.threshold.value, Is.EqualTo(PostTuning.BloomThreshold).Within(Tolerance));

            Assert.That(profile.TryGet(out Vignette vignette), Is.True);
            Assert.That(
                vignette.intensity.value,
                Is.EqualTo(PostTuning.VignetteIntensity).Within(Tolerance));
        }

        /// <summary>
        /// Every effect in the profile has to be one this project asked for. The state it was
        /// found in had seven components left over from the render pipeline's own test suites,
        /// two of them with no script in the project at all.
        /// </summary>
        [Test]
        public void TheVolumeProfileCarriesNothingElse()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(
                VolumeProfileBuilder.ProfilePath);
            Assert.That(profile, Is.Not.Null);

            foreach (VolumeComponent effect in profile.components)
            {
                Assert.That(
                    effect,
                    Is.Not.Null,
                    "an effect in the profile has no script behind it; "
                        + "run Tools > IronFlag > Build Volume Profile");
            }

            Assert.That(profile.components.Count, Is.EqualTo(4));
        }

        [Test]
        public void AWorldCameraSeesNoInterfaceAtAll()
        {
            int mask = InterfaceLayers.WorldMask();

            for (int slot = 0; slot < InterfaceLayers.Count; slot++)
            {
                int layer = InterfaceLayers.LayerFor(slot);
                if (layer >= 0)
                {
                    Assert.That(
                        mask & (1 << layer),
                        Is.Zero,
                        $"a world camera would draw {InterfaceLayers.NameFor(slot)}");
                }
            }

            int editor = InterfaceLayers.EditorLayer();
            if (editor >= 0)
            {
                Assert.That(mask & (1 << editor), Is.Zero);
            }

            Assert.That(mask & 1, Is.Not.Zero, "a world camera has to draw the default layer");
        }

        [Test]
        public void PaintingACanvasReachesEverythingUnderIt()
        {
            var root = new GameObject("Test Canvas");
            var child = new GameObject("Panel");
            var grandchild = new GameObject("Label");

            try
            {
                child.transform.SetParent(root.transform, false);
                grandchild.transform.SetParent(child.transform, false);
                root.layer = 9;

                InterfaceLayers.Paint(root);

                Assert.That(child.layer, Is.EqualTo(9));
                Assert.That(grandchild.layer, Is.EqualTo(9));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
