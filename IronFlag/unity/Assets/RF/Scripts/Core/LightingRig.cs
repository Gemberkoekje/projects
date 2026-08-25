using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace IronFlag.Core
{
    /// <summary>
    /// Puts one <see cref="LightingTuning"/> onto a scene: the sun, the ambient fill, the
    /// haze and the sky.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The single place a scene gets lit. Before this there were two near-identical copies of
    /// the same block - one in the sandbox builder that the level editor and the map overview
    /// both called, one private to the art preview - which is the arrangement where a value
    /// gets fixed in one and not the other. There is now one function and a table of rows for
    /// it to apply.
    /// </para>
    /// <para>
    /// Everything here is scene state (<see cref="RenderSettings"/>) or the sky material, and
    /// that boundary is deliberate. <strong>Shadow distance is not here</strong>, even though
    /// it belongs to the same look, because URP reads its own
    /// <c>UniversalRenderPipelineAsset.shadowDistance</c> and ignores
    /// <see cref="QualitySettings.shadowDistance"/> entirely - so the only way to set it from
    /// here would be to write to a shared pipeline asset, and then the last scene built would
    /// decide what every other scene ships with. It lives in the two pipeline assets instead,
    /// and <see cref="ShadowDistance"/> below records what they are supposed to say so a test
    /// can catch them drifting.
    /// </para>
    /// <para>
    /// Safe to call outside play mode, which is what every caller does: all four are editor
    /// tools that build a scene, save it, or render a still from it.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// LightingRig.Apply(LightingTuning.For(LightingMood.Daylight), sky);
    /// </code>
    /// </example>
    public static class LightingRig
    {
        /// <summary>
        /// What both pipeline assets' shadow distance is set to, in metres.
        /// </summary>
        /// <remarks>
        /// Sized to what the gameplay camera can see rather than to the map, which is the
        /// distinction the 40 metres this replaces got wrong. The camera sits 34 metres out
        /// at 58 degrees, which puts it 28.8 metres above the ground it is looking at; the
        /// top of a 50 degree frame leaves 33 degrees of that to spare, so the furthest
        /// ground down the middle of the view is about 53 metres away, and the far corners of
        /// a wide split-screen letterbox reach roughly 100. Everything past that is out of
        /// frame, so shadowing it buys nothing and spends cascade resolution. 120 covers the
        /// corners with room over, and puts the four cascades of a 2048 map at 15, 35, 64 and
        /// 120 metres, which leaves the vehicle you are driving inside the first one.
        ///
        /// A map can be far larger than this - a level's bounds default to 120 metres of half
        /// extent, so 240 across - and that is fine. Shadow distance is a property of the
        /// view, not of the map.
        /// </remarks>
        public const float ShadowDistance = 120.0f;

        /// <summary>Sun disc mode on Unity's procedural skybox: 2 is its high quality disc.</summary>
        private const float HighQualitySunDisc = 2.0f;

        private static readonly int SkyTint = Shader.PropertyToID("_SkyTint");
        private static readonly int SkyGround = Shader.PropertyToID("_GroundColor");
        private static readonly int SkyExposure = Shader.PropertyToID("_Exposure");
        private static readonly int SkyAtmosphere = Shader.PropertyToID("_AtmosphereThickness");
        private static readonly int SunDisc = Shader.PropertyToID("_SunDisk");
        private static readonly int SunSize = Shader.PropertyToID("_SunSize");

        /// <summary>
        /// Lights the open scene.
        /// </summary>
        /// <param name="lighting">The condition to light it under.</param>
        /// <param name="sky">
        /// The sky material to paint and hang behind everything, or <c>null</c> to leave
        /// whichever sky the scene already had. Passing nothing is what a caller running
        /// before the generated materials exist does, and it degrades to Unity's default sky
        /// rather than to no sky at all.
        /// </param>
        /// <remarks>
        /// The sun is whichever directional light the scene already has, because every scene
        /// here is built by a tool that made one. A scene with no directional light is lit by
        /// ambient alone rather than being an error: it is a scene that has not finished
        /// building, and the tool building it is about to add one.
        /// </remarks>
        public static void Apply(LightingTuning lighting, Material sky)
        {
            if (lighting == null)
            {
                return;
            }

            Light sun = Sun();
            if (sun != null)
            {
                sun.type = LightType.Directional;
                sun.transform.rotation = Quaternion.Euler(lighting.SunPitch, lighting.SunYaw, 0.0f);
                sun.intensity = lighting.SunIntensity;
                sun.color = lighting.SunColour;
                sun.shadows = LightShadows.Soft;
            }

            // Named explicitly rather than left to Unity's guess, because the procedural sky
            // draws its disc wherever this points and the guess is whichever directional
            // light happens to be brightest.
            RenderSettings.sun = sun;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = lighting.AmbientSky;
            RenderSettings.ambientEquatorColor = lighting.AmbientEquator;
            RenderSettings.ambientGroundColor = lighting.AmbientGround;

            RenderSettings.fog = lighting.Fog;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = lighting.FogColour;
            RenderSettings.fogStartDistance = lighting.FogStart;
            RenderSettings.fogEndDistance = lighting.FogEnd;

            if (sky != null)
            {
                Paint(sky, lighting);
                RenderSettings.skybox = sky;
            }

            // The reflection every smooth material sees is generated from the sky, so this
            // has to happen after the sky is painted or the METAL palette spends a session
            // reflecting the previous tuning.
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            DynamicGI.UpdateEnvironment();
        }

        /// <summary>
        /// Writes one lighting condition's sky values onto a material.
        /// </summary>
        /// <param name="sky">A material using Unity's procedural skybox shader.</param>
        /// <param name="lighting">The condition whose sky to paint.</param>
        /// <remarks>
        /// Every property is guarded, so pointing this at a material with some other shader
        /// on it paints whatever it recognises and leaves the rest alone rather than
        /// throwing. That matters because the sky material is generated, and a half-migrated
        /// project is a normal state to be in for one recompile.
        /// </remarks>
        public static void Paint(Material sky, LightingTuning lighting)
        {
            if (sky == null || lighting == null)
            {
                return;
            }

            Set(sky, SkyTint, lighting.SkyTint);
            Set(sky, SkyGround, lighting.SkyGround);
            Set(sky, SkyExposure, lighting.SkyExposure);
            Set(sky, SkyAtmosphere, lighting.SkyAtmosphere);
            Set(sky, SunDisc, HighQualitySunDisc);
            Set(sky, SunSize, lighting.SunDiscSize);
        }

        /// <summary>
        /// Finds the scene's sun.
        /// </summary>
        /// <returns>
        /// A directional light belonging to the active scene when one exists, otherwise any
        /// directional light found loaded; the same one every time for the same set of
        /// lights, and <c>null</c> when there is none at all.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Directional specifically, rather than the first light of any kind: a scene that
        /// has grown a muzzle flash or a lit window has point lights in it, and pointing the
        /// sky's sun at one of those puts the sun inside a building.
        /// </para>
        /// <para>
        /// Sorted by instance ID explicitly rather than left at the engine's own unspecified
        /// order, which is what this replaced and which is how it was found: Unity's own
        /// order for an unsorted <c>FindObjectsByType</c> call is not guaranteed stable
        /// between calls, so two directional lights coexisting in one scene - a scene-building
        /// tool's default and something else's leftover, say - could each get picked from one
        /// call to the next. See <c>LIGHTING_NOTES.md</c> for the test failure this caused:
        /// the rig configured a leftover light and left the one the test had just made at its
        /// default, silently, until a comparison caught the number left behind.
        /// </para>
        /// <para>
        /// This makes the choice <em>repeatable</em>, not omniscient - instance ID says
        /// nothing about which light a caller actually meant, only that the same input always
        /// gives the same output. Two directional lights genuinely coexisting in the same
        /// scene is a state this method has no way to resolve correctly, because "correctly"
        /// is a fact about the caller's intent that nothing here can see; every production
        /// caller avoids the question by loading a fresh scene immediately before calling this,
        /// which leaves exactly one. A test that wants a specific light has to leave exactly
        /// one too, for the same reason - see
        /// <c>LightingTests.ApplyingAConditionPutsItOnTheScene</c>. Scene membership is
        /// checked first for the one case that does have a right answer regardless of how many
        /// lights exist: a light left behind in a scene that is no longer the active one, e.g.
        /// one loaded additively and never unloaded, should never outrank one in the scene
        /// actually being lit.
        /// </para>
        /// </remarks>
        public static Light Sun()
        {
            Light[] lights = Object.FindObjectsByType<Light>(
                FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);

            Scene active = SceneManager.GetActiveScene();
            Light fallback = null;

            foreach (Light light in lights)
            {
                if (light.type != LightType.Directional)
                {
                    continue;
                }

                if (light.gameObject.scene == active)
                {
                    return light;
                }

                if (fallback == null)
                {
                    fallback = light;
                }
            }

            return fallback;
        }

        private static void Set(Material material, int property, Color value)
        {
            if (material.HasProperty(property))
            {
                material.SetColor(property, value);
            }
        }

        private static void Set(Material material, int property, float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }
    }
}
