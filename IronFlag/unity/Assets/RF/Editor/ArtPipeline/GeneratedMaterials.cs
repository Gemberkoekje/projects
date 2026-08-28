using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using IronFlag.Core;
using IronFlag.Levels;

namespace IronFlag.Editor.ArtPipeline
{
    /// <summary>
    /// The small set of materials Unity has to supply because a baked vertex color cannot
    /// express them: team accents, glowing lights, the ground, and the two things combat
    /// draws that were never modelled - a round in flight and the flash it makes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nearly all color on an IronFlag model is vertex color on one shared material, so
    /// this list stays short by design. Everything here is generated rather than authored,
    /// for the same reason the models are: the palette is fixed and a generated asset can
    /// be re-derived instead of maintained.
    /// </para>
    /// <para>
    /// Both the art preview and the vehicle prefabs bind these materials, which is why they
    /// live here rather than inside either tool - and why <see cref="EnsureAssets"/> updates
    /// existing assets in place. Recreating them would hand out fresh GUIDs and quietly
    /// unbind every prefab that referenced the old ones.
    /// </para>
    /// </remarks>
    public static class GeneratedMaterials
    {
        /// <summary>Folder holding the generated materials.</summary>
        public const string Folder = "Assets/RF/Art/Materials";

        /// <summary>Asset name of the ground material.</summary>
        /// <remarks>
        /// No longer worn by anything on a map: land is painted per surface now, out of
        /// <see cref="SurfaceTuning"/>. It stays because the art preview stands its models
        /// on it, and because it is a neutral grey that a new model wants to be photographed
        /// against rather than a colour a level has an opinion about.
        /// </remarks>
        public const string Ground = "RF_Ground";

        /// <summary>Name of the material the sea wears.</summary>
        /// <remarks>
        /// Also the surface material for <see cref="SurfaceKind.DeepWater"/>, which is why
        /// it is not called <c>RF_Surface_DeepWater</c>: the sea already wears this asset,
        /// its colour is already a measured result, and a second dark blue generated beside
        /// it would be one more thing that could drift. Its colour now comes out of the
        /// surface table like every other surface.
        /// </remarks>
        public const string Water = "RF_Water";

        /// <summary>Prefix on the generated material for each ground surface.</summary>
        public const string SurfacePrefix = "RF_Surface_";

        /// <summary>Prefix of the generated bank materials, one per ground surface.</summary>
        public const string BankPrefix = "RF_Bank_";

        /// <summary>Asset name of the green team material.</summary>
        public const string Green = "RF_Team_Green";

        /// <summary>Asset name of the brown team material.</summary>
        public const string Brown = "RF_Team_Brown";

        /// <summary>Asset name of the emissive head-light material.</summary>
        public const string FrontLight = "RF_Light_Front";

        /// <summary>Asset name of the emissive tail-light material.</summary>
        public const string RearLight = "RF_Light_Rear";

        /// <summary>Asset name of the material every round in flight wears.</summary>
        public const string Tracer = "RF_Tracer";

        /// <summary>Asset name of the material an explosion flash wears.</summary>
        public const string Blast = "RF_Blast";

        /// <summary>Asset name of the material a flying piece of a building wears.</summary>
        public const string Debris = "RF_Debris";

        /// <summary>Asset name of the material a scorch on the ground wears.</summary>
        /// <remarks>
        /// Two assets rather than one for the two marks, because the shader has to be told
        /// whether it is drawing a disc or a ribbon and that is a property rather than a
        /// keyword. They are otherwise the same material with different numbers - which is
        /// the same argument <see cref="Particle"/> makes for one material serving smoke,
        /// dust and spray, arriving at two instead of one only because a wheel track has no
        /// middle to fall off from.
        /// </remarks>
        public const string Scorch = "RF_Scorch";

        /// <summary>Asset name of the material a wheel track wears.</summary>
        public const string Track = "RF_Track";

        /// <summary>Asset name of the material every particle system wears.</summary>
        /// <remarks>
        /// One material for smoke, dust and spray alike. It is plain white and transparent,
        /// and every effect tints itself through its own start colour - a particle system
        /// multiplies its material by the particle's colour, so a colour per effect costs a
        /// field rather than an asset. That is also what lets a dust trail take its colour
        /// from the ground it is being kicked off, which no material could do.
        /// </remarks>
        public const string Particle = "RF_Particle";

        /// <summary>Prefix on the generated sky material for each lighting condition.</summary>
        /// <remarks>
        /// One asset per <see cref="LightingMood"/> rather than a single sky that whichever
        /// scene builder ran last repaints. The sandbox and the art preview are lit
        /// differently on purpose, they both save a scene that references its sky, and one
        /// shared asset between them would be a file whose committed contents flip-flopped
        /// with whatever was rebuilt most recently.
        /// </remarks>
        public const string SkyPrefix = "RF_Sky_";

        /// <summary>Material name prefix marking the geometry that carries team color.</summary>
        public const string AccentPrefix = ModelPaint.AccentPrefix;

        /// <summary>Material name prefix on head-light geometry, as exported by Blender.</summary>
        public const string FrontLightPrefix = ModelPaint.FrontLightPrefix;

        /// <summary>Material name prefix on tail-light geometry, as exported by Blender.</summary>
        public const string RearLightPrefix = ModelPaint.RearLightPrefix;

        private static readonly Color TeamGreenColor = new Color(0.24f, 0.49f, 0.23f);
        private static readonly Color TeamBrownColor = new Color(0.54f, 0.35f, 0.17f);
        private static readonly Color GroundColor = new Color(0.17f, 0.18f, 0.17f);
        private static readonly Color FrontLightColor = new Color(0.95f, 0.92f, 0.82f);
        private static readonly Color RearLightColor = new Color(0.62f, 0.09f, 0.07f);
        private static readonly Color TracerColor = new Color(1.00f, 0.72f, 0.30f);
        private static readonly Color BlastColor = new Color(1.00f, 0.86f, 0.58f);

        /// <summary>
        /// What the two ground marks multiply the ground by, and how hard their edges are.
        /// </summary>
        /// <remarks>
        /// Multipliers rather than colours, which is why neither of them is in the palette:
        /// white leaves the ground alone and these two take a share of it away, so the same
        /// number is a burn on grass, a burn on sand and a burn on a road. A scorch takes
        /// nearly two thirds and has a badly eaten edge; a track takes a bit over a third at
        /// its deepest and is nearly straight-sided, because a rut is a shape a wheel made.
        /// </remarks>
        private static readonly Color ScorchColor = new Color(0.38f, 0.36f, 0.33f);
        private static readonly Color TrackColor = new Color(0.62f, 0.60f, 0.56f);

        /// <summary>
        /// The asset spec's charred dark grey, which every destructible's damaged and
        /// destroyed meshes already wear. Debris is a piece of one of those, so it is the
        /// same color: chunks that flew off in a lighter grey read as a different material
        /// than the rubble they land next to.
        /// </summary>
        private static readonly Color DebrisColor = new Color(0.16f, 0.15f, 0.14f);

        /// <summary>Emission colors, above 1 so the lights read as lit rather than pale.</summary>
        private static readonly Color FrontLightEmission = new Color(1.70f, 1.55f, 1.20f);
        private static readonly Color RearLightEmission = new Color(1.50f, 0.16f, 0.10f);

        /// <summary>
        /// Rounds and blasts glow harder than the lights do: they have to read against a
        /// sunlit ground from thirty-four metres up, in half a screen, in a moment. Not
        /// harder than that, though - the first pass ran the blast at six and it clipped to
        /// a flat white disc, which is a worse explosion than a warm one.
        /// </summary>
        private static readonly Color TracerEmission = new Color(3.00f, 1.60f, 0.50f);
        private static readonly Color BlastEmission = new Color(3.40f, 1.85f, 0.70f);

        /// <summary>Emission property on URP's Lit shader.</summary>
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        /// <summary>Smoothness property on URP's Lit shader.</summary>
        private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");

        /// <summary>Smoothness URP's Lit material ships with.</summary>
        private const float DefaultSmoothness = 0.5f;

        /// <summary>Unity's own procedural sky, which is what the sky materials wear.</summary>
        /// <remarks>
        /// Retuned rather than replaced with a hand-written gradient shader, which was the
        /// other option on the table. It is worth the saving here because of where this
        /// project's sky is actually seen: never as sky, because the gameplay camera's frame
        /// stops 33 degrees below the horizon, and only as the reflection on metal and as the
        /// gap past the edge of a level's sea slab. Both of those are answered by two colours
        /// and an exposure, which this shader already exposes. See
        /// <see cref="LightingTuning"/>.
        /// </remarks>
        private const string SkyShaderName = "Skybox/Procedural";

        /// <summary>URP's particle shader, which is the one that reads a particle's colour.</summary>
        /// <remarks>
        /// Not <c>Universal Render Pipeline/Unlit</c>, which looks like it would do and does
        /// not: it ignores vertex colour, so every particle in a system comes out the same
        /// shade and nothing can fade out. Multiplying by the particle colour is the whole
        /// reason this shader exists and the whole reason one material can serve smoke, dust
        /// and spray at once.
        /// </remarks>
        private const string ParticleShaderName = "Universal Render Pipeline/Particles/Unlit";

        /// <summary>This project's own shader, worn by scorches and wheel tracks.</summary>
        public const string MarkShaderName = "IronFlag/Mark";

        /// <summary>This project's own shader, worn by every sheet of land and every bank.</summary>
        /// <remarks>
        /// The first shader the project has ever had, and hand-written HLSL rather than a
        /// Shader Graph for the reason <c>DebrisBurst.cs</c> gives for hand-rolling a debris
        /// burst: a graph is a serialised asset nobody can review in a diff, and every line
        /// of what these two do is an argument about how the map should read. See
        /// <c>Assets/RF/Art/Shaders/RF_Surface.hlsl</c> for the half they share.
        /// </remarks>
        public const string GroundShaderName = "IronFlag/Ground";

        /// <summary>This project's own shader, worn by the sea and by the shelf.</summary>
        public const string WaterShaderName = "IronFlag/Water";

        /// <summary>Base color property used by URP's own shaders.</summary>
        private static readonly int UniversalBaseColor = Shader.PropertyToID("_BaseColor");

        /// <summary>Base color property used by glTFast's imported materials.</summary>
        private static readonly int GltfBaseColor = Shader.PropertyToID("baseColorFactor");

        /// <summary>The properties behind URP's Surface Type drop-down.</summary>
        private static readonly int SurfaceType = Shader.PropertyToID("_Surface");
        private static readonly int BlendMode = Shader.PropertyToID("_Blend");
        private static readonly int SourceBlend = Shader.PropertyToID("_SrcBlend");
        private static readonly int DestinationBlend = Shader.PropertyToID("_DstBlend");
        private static readonly int DepthWrite = Shader.PropertyToID("_ZWrite");
        private static readonly int AlphaClip = Shader.PropertyToID("_AlphaClip");

        /// <summary>The properties of this project's own two surface shaders.</summary>
        /// <remarks>
        /// Every one of them, including the ones no surface disagrees with another surface
        /// about, because a material keeps whatever a shader's default was on the day it was
        /// created and never reads that default again. Leaving the shared ones to the shader
        /// would make them constants nobody could ever change. See <see cref="SurfaceLook"/>,
        /// which is where both kinds live.
        /// </remarks>
        private static readonly int DetailStrength = Shader.PropertyToID("_DetailStrength");
        private static readonly int DetailScale = Shader.PropertyToID("_DetailScale");
        private static readonly int SwellStrength = Shader.PropertyToID("_SwellStrength");
        private static readonly int SwellScale = Shader.PropertyToID("_SwellScale");
        private static readonly int SwellSpeed = Shader.PropertyToID("_SwellSpeed");
        private static readonly int ChopStrength = Shader.PropertyToID("_ChopStrength");
        private static readonly int ChopScale = Shader.PropertyToID("_ChopScale");
        private static readonly int GlintColour = Shader.PropertyToID("_GlintColour");
        private static readonly int Glint = Shader.PropertyToID("_Glint");
        private static readonly int GlintSharpness = Shader.PropertyToID("_GlintSharpness");
        private static readonly int FresnelColour = Shader.PropertyToID("_FresnelColour");
        private static readonly int Fresnel = Shader.PropertyToID("_Fresnel");
        private static readonly int FresnelPower = Shader.PropertyToID("_FresnelPower");
        private static readonly int FoamColour = Shader.PropertyToID("_FoamColour");
        private static readonly int FoamWidth = Shader.PropertyToID("_FoamWidth");
        private static readonly int FoamEdge = Shader.PropertyToID("_FoamEdge");
        private static readonly int FoamSpeed = Shader.PropertyToID("_FoamSpeed");
        private static readonly int ShoreWash = Shader.PropertyToID("_ShoreWash");

        /// <summary>The properties of the ground-mark shader.</summary>
        private static readonly int MarkEdge = Shader.PropertyToID("_Edge");
        private static readonly int MarkRagged = Shader.PropertyToID("_Ragged");
        private static readonly int MarkRaggedScale = Shader.PropertyToID("_RaggedScale");
        private static readonly int MarkRound = Shader.PropertyToID("_Round");

        /// <summary>
        /// Returns the project path of one generated material.
        /// </summary>
        /// <param name="name">Material asset name, from the constants on this class.</param>
        /// <returns>The project-relative asset path.</returns>
        public static string PathOf(string name) => $"{Folder}/{name}.mat";

        /// <summary>
        /// Loads one generated material.
        /// </summary>
        /// <param name="name">Material asset name, from the constants on this class.</param>
        /// <returns>The material, or <c>null</c> when <see cref="EnsureAssets"/> has not run.</returns>
        public static Material Load(string name)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(PathOf(name));
            if (material == null)
            {
                Debug.LogError($"IronFlag: material '{name}' was requested before it was created.");
            }

            return material;
        }

        /// <summary>
        /// Creates any missing generated material and refreshes the colors of the rest.
        /// </summary>
        /// <remarks>
        /// The template is deliberately taken from a throwaway primitive: Unity hands
        /// primitives URP's default Lit material, which is fully set up and is therefore the
        /// only reliable starting point. A URP Lit material built from <c>Shader.Find</c>
        /// misses the keyword and property setup the material inspector applies and renders
        /// as though it had no base color at all. Copying glTFast's imported material fails
        /// differently: its Shader Graph ignores <c>baseColorFactor</c> assigned after
        /// import, and renders white.
        /// </remarks>
        public static void EnsureAssets()
        {
            EnsureAssetFolder(Folder);

            GameObject probe = null;

            foreach ((string name, Color color, Color emission, float smoothness) in MaterialSet())
            {
                string path = PathOf(name);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (material == null)
                {
                    if (probe == null)
                    {
                        probe = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    }

                    material = new Material(probe.GetComponent<Renderer>().sharedMaterial);
                    AssetDatabase.CreateAsset(material, path);
                }

                ApplyBaseColor(material, color);
                ApplyEmission(material, emission);
                ApplySmoothness(material, smoothness);
                EditorUtility.SetDirty(material);
            }

            if (probe != null)
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }

            EnsureSkies();
            EnsureSurfaces();
            EnsureMarks();
            EnsureParticle();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Rebinds an instance's material groups to the generated materials.
        /// </summary>
        /// <param name="instance">Scene object or prefab contents holding imported renderers.</param>
        /// <param name="teamMaterial">
        /// Asset name of the team material to apply, or an empty string to leave the team
        /// trim wearing the neutral placeholder Blender exported.
        /// </param>
        /// <remarks>
        /// The rule about which group goes where is <see cref="ModelPaint"/>, in the runtime
        /// assembly, because the game paints its own map now and a built player has no asset
        /// database. This half is the half that only the editor can do: turning an asset name
        /// into the material asset behind it.
        /// </remarks>
        public static void Apply(GameObject instance, string teamMaterial)
            => ModelPaint.Apply(
                instance,
                LoadOrNothing(teamMaterial),
                Load(FrontLight),
                Load(RearLight));

        /// <summary>
        /// Reports whether a renderer wears the team accent Blender exported.
        /// </summary>
        /// <param name="renderer">Renderer to check.</param>
        /// <returns><c>true</c> when one of its materials is the accent placeholder.</returns>
        public static bool IsTeamTrim(Renderer renderer) => ModelPaint.IsTeamTrim(renderer);

        /// <summary>
        /// Loads a material, treating an empty name as "leave it alone".
        /// </summary>
        /// <param name="name">Material asset name, or an empty string.</param>
        /// <returns>The material, or <c>null</c>.</returns>
        /// <remarks>
        /// Distinct from <see cref="Load"/>, which logs a missing material as an error. An
        /// empty name is not a missing material - it is a prefab being built neutral, which
        /// is what every prefab in the project is.
        /// </remarks>
        private static Material LoadOrNothing(string name)
            => string.IsNullOrEmpty(name) ? null : Load(name);

        /// <summary>
        /// Returns the asset name of the sky for one lighting condition.
        /// </summary>
        /// <param name="mood">The condition the sky is painted for.</param>
        /// <returns>The material asset name.</returns>
        public static string SkyMaterial(LightingMood mood) => $"{SkyPrefix}{mood}";

        /// <summary>
        /// Loads the sky for one lighting condition.
        /// </summary>
        /// <param name="mood">The condition the sky is painted for.</param>
        /// <returns>The material, or <c>null</c> when <see cref="EnsureAssets"/> has not run.</returns>
        public static Material LoadSky(LightingMood mood) => Load(SkyMaterial(mood));

        /// <summary>
        /// Creates a sky material for every lighting condition and repaints the rest.
        /// </summary>
        /// <remarks>
        /// <see cref="Shader.Find"/> is safe here where it is not for the lit materials above:
        /// the reason those are cloned off a throwaway primitive is that URP's Lit shader
        /// needs keyword and property setup a bare material misses, and a procedural sky has
        /// neither - it is a handful of floats and two colours.
        /// </remarks>
        private static void EnsureSkies()
        {
            Shader shader = Shader.Find(SkyShaderName);
            if (shader == null)
            {
                Debug.LogError($"IronFlag: the '{SkyShaderName}' shader is missing; "
                    + "every scene will keep Unity's default sky.");
                return;
            }

            foreach (LightingMood mood in Enum.GetValues(typeof(LightingMood)))
            {
                if (mood == LightingMood.None)
                {
                    continue;
                }

                string path = PathOf(SkyMaterial(mood));
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, path);
                }

                LightingRig.Paint(material, LightingTuning.For(mood));
                EditorUtility.SetDirty(material);
            }
        }

        /// <summary>
        /// Creates or refreshes the material every surface and every bank is painted with.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Separate from the loop above because these are the only generated materials that
        /// do not wear URP's Lit shader. They are still generated rather than authored, and
        /// still updated in place rather than recreated - a fresh GUID here would unbind
        /// every level catalog row that names one.
        /// </para>
        /// <para>
        /// Two colours come out of <see cref="SurfaceTuning"/> and everything else out of
        /// <see cref="SurfaceLook"/>, which is the split those two tables are for: what a
        /// surface is painted is a balance argument settled against a map shot, and how much
        /// grain it has is not. Note in particular that the smoothness set here is still the
        /// surfaces table's - zero on both waters - and that the sea gets its highlight from
        /// the water shader's own glint instead. See <see cref="SurfaceLook.Glint"/>.
        /// </para>
        /// <para>
        /// A bank is the same surface seen from the side, so it takes the same row: its own
        /// colour taken down a step, and its own grain. That is the same derivation
        /// <see cref="SurfaceTuning.BankShade"/> already makes for the colour, extended to
        /// the one other thing a bank could disagree with its surface about.
        /// </para>
        /// </remarks>
        private static void EnsureSurfaces()
        {
            Shader ground = Shader.Find(GroundShaderName);
            Shader water = Shader.Find(WaterShaderName);

            if (ground == null || water == null)
            {
                Debug.LogError($"IronFlag: the '{GroundShaderName}' and '{WaterShaderName}' "
                    + "shaders are missing; every map will keep whatever it was painted with.");
                return;
            }

            foreach (SurfaceKind kind in SurfaceTuning.Roster())
            {
                SurfaceTuning surface = SurfaceTuning.For(kind);
                Paint(
                    EnsureSurface(SurfaceMaterial(kind), surface.Drowns ? water : ground),
                    surface.Colour,
                    surface.Smoothness,
                    SurfaceLook.For(kind),
                    surface.Drowns);
            }

            foreach (SurfaceKind kind in SurfaceTuning.Stack(false))
            {
                SurfaceTuning surface = SurfaceTuning.For(kind);
                Paint(
                    EnsureSurface(BankMaterial(kind), ground),
                    surface.Bank,
                    surface.Smoothness,
                    SurfaceLook.For(kind),
                    false);
            }
        }

        /// <summary>
        /// Loads one surface material, creating it or moving it onto the right shader.
        /// </summary>
        /// <param name="name">Material asset name.</param>
        /// <param name="shader">Shader it should be wearing.</param>
        /// <returns>The material, on that shader.</returns>
        /// <remarks>
        /// Assigning the shader every time rather than only on creation, because every one of
        /// these assets already exists wearing URP's Lit: this is the pass that moves them,
        /// and it has to be able to move them again if a surface ever stops being water.
        /// </remarks>
        private static Material EnsureSurface(string name, Shader shader)
        {
            string path = PathOf(name);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            if (material.shader != shader)
            {
                material.shader = shader;
            }

            return material;
        }

        /// <summary>
        /// Sets everything one surface material carries.
        /// </summary>
        /// <param name="material">Material to paint.</param>
        /// <param name="colour">What it is painted, as URP takes a base colour.</param>
        /// <param name="smoothness">How glossy it is, out of the surfaces table.</param>
        /// <param name="look">How much detail it is drawn with.</param>
        /// <param name="wet">Whether it is one of the two waters.</param>
        /// <remarks>
        /// The wet and dry sets are disjoint and each shader only has its own, so setting the
        /// wrong one would be a silent no-op rather than an error - which is exactly the
        /// failure <see cref="ApplyBaseColor"/> exists to stop happening quietly. Hence the
        /// branch: a surface is asked which shader it is on once, here, and not once per
        /// property.
        /// </remarks>
        private static void Paint(
            Material material, Color colour, float smoothness, SurfaceLook look, bool wet)
        {
            ApplyBaseColor(material, colour);
            ApplySmoothness(material, smoothness);

            if (wet)
            {
                material.SetFloat(SwellStrength, look.Swell);
                material.SetFloat(SwellScale, look.SwellScale);
                material.SetFloat(SwellSpeed, SurfaceLook.SwellSpeed);
                material.SetFloat(ChopStrength, look.Chop);
                material.SetFloat(ChopScale, SurfaceLook.ChopScale);
                material.SetColor(GlintColour, SurfaceLook.GlintColour);
                material.SetFloat(Glint, look.Glint);
                material.SetFloat(GlintSharpness, SurfaceLook.GlintSharpness);
                material.SetColor(FresnelColour, SurfaceLook.FresnelColour);
                material.SetFloat(Fresnel, look.Fresnel);
                material.SetFloat(FresnelPower, SurfaceLook.FresnelPower);
                material.SetColor(FoamColour, SurfaceLook.FoamColour);
                material.SetFloat(FoamWidth, look.Foam);
                material.SetFloat(FoamEdge, SurfaceLook.FoamEdge);
                material.SetFloat(FoamSpeed, SurfaceLook.FoamSpeed);
                material.SetFloat(ShoreWash, look.Wash);
            }
            else
            {
                material.SetFloat(DetailStrength, look.Grain);
                material.SetFloat(DetailScale, look.GrainScale);
            }

            EditorUtility.SetDirty(material);
        }

        /// <summary>
        /// Creates or refreshes the two materials a mark on the ground wears.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Built from <c>Shader.Find</c> and set up by hand, which the class summary warns
        /// against for URP's Lit shader and which is safe here for the same reason it is safe
        /// for the skies: the warning is about a shader whose keyword and property setup the
        /// material inspector applies behind your back, and this is one of ours, with four
        /// properties and no keywords at all.
        /// </para>
        /// <para>
        /// Nothing sets a blend mode, a queue or a depth write here either, unlike
        /// <see cref="EnsureParticle"/>. Those six lines exist there because URP's particle
        /// shader is a general shader being asked to be a specific one; <c>RF_Mark</c> only
        /// knows how to be a stain, so its render state is written into the pass and cannot
        /// be got wrong by a material.
        /// </para>
        /// </remarks>
        private static void EnsureMarks()
        {
            Shader shader = Shader.Find(MarkShaderName);
            if (shader == null)
            {
                Debug.LogError($"IronFlag: the '{MarkShaderName}' shader is missing; "
                    + "nothing will leave a scorch or a wheel track.");
                return;
            }

            // A burn: badly eaten at the edge, at about the scale of the lumps a fireball
            // leaves, and taking most of the ground's brightness away in the middle.
            Mark(EnsureSurface(Scorch, shader), ScorchColor, 0.35f, 0.45f, 0.70f, round: true);

            // A rut: nearly straight-sided, because a wheel is, and only nibbled at the
            // edge so that it does not read as a painted line.
            Mark(EnsureSurface(Track, shader), TrackColor, 0.15f, 0.18f, 0.35f, round: false);
        }

        /// <summary>
        /// Sets everything a ground-mark material carries.
        /// </summary>
        /// <param name="material">Material to set.</param>
        /// <param name="stain">What it multiplies the ground by at its darkest.</param>
        /// <param name="edge">How far out from the middle the mark is at full strength.</param>
        /// <param name="ragged">How much noise eats into that edge.</param>
        /// <param name="raggedScale">How many metres one lump of that noise covers.</param>
        /// <param name="round">Whether it is a disc or a ribbon.</param>
        private static void Mark(
            Material material,
            Color stain,
            float edge,
            float ragged,
            float raggedScale,
            bool round)
        {
            ApplyBaseColor(material, stain);
            material.SetFloat(MarkEdge, edge);
            material.SetFloat(MarkRagged, ragged);
            material.SetFloat(MarkRaggedScale, raggedScale);
            material.SetFloat(MarkRound, round ? 1.0f : 0.0f);
            EditorUtility.SetDirty(material);
        }

        /// <summary>
        /// Creates or refreshes the one material every particle system wears.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Built from <c>Shader.Find</c> and set up by hand, which the class summary warns
        /// against for URP's Lit shader - the difference is that there is no primitive to
        /// steal a correctly configured particle material from, so there is no template to
        /// copy. The six lines below are what the material inspector's Surface Type
        /// drop-down actually does: a blend mode, a depth-write, a keyword and a queue.
        /// Getting one of them wrong renders solid white boxes rather than smoke, which is
        /// obvious the first time it is looked at and invisible in a diff.
        /// </para>
        /// <para>
        /// Deliberately untextured. Every particle in this game is a mesh - a sphere, drawn
        /// flat - rather than a billboard, so there is no soft round blob to sample and
        /// nothing to fade at the edges. That is a style decision rather than a saving: this
        /// game is boxes and spheres seen from above, and a soft photographic puff of smoke
        /// sitting on top of it would be the one thing in the frame that came from somewhere
        /// else.
        /// </para>
        /// </remarks>
        private static void EnsureParticle()
        {
            Shader shader = Shader.Find(ParticleShaderName);
            if (shader == null)
            {
                Debug.LogError($"IronFlag: the '{ParticleShaderName}' shader is missing; "
                    + "smoke, dust and spray will render as solid boxes.");
                return;
            }

            string path = PathOf(Particle);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetColor(UniversalBaseColor, Color.white);

            // Transparent, alpha-blended, writing no depth. This is the Surface Type
            // drop-down, expanded.
            material.SetFloat(SurfaceType, 1.0f);
            material.SetFloat(BlendMode, 0.0f);
            material.SetFloat(SourceBlend, (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat(DestinationBlend, (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat(DepthWrite, 0.0f);
            material.SetFloat(AlphaClip, 0.0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            EditorUtility.SetDirty(material);
        }

        /// <summary>
        /// Creates every missing folder along an <c>Assets/...</c> path in the asset database.
        /// </summary>
        /// <param name="folder">Project-relative folder path.</param>
        public static void EnsureAssetFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = $"{current}/{parts[index]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[index]);
                }

                current = next;
            }
        }

        /// <summary>
        /// Returns the asset name of the material one surface is painted with.
        /// </summary>
        /// <param name="kind">Surface to name.</param>
        /// <returns>The asset name, for <see cref="Load"/> and <see cref="PathOf"/>.</returns>
        /// <remarks>
        /// <see cref="SurfaceKind.DeepWater"/> is the sea, which already had a material and
        /// keeps it. Every other surface is named after itself, so a new row in the table is
        /// a new asset nobody has to name.
        /// </remarks>
        public static string SurfaceMaterial(SurfaceKind kind)
            => kind == SurfaceKind.DeepWater ? Water : $"{SurfacePrefix}{kind}";

        /// <summary>
        /// Returns the asset name of the material the bank below one surface is painted
        /// with.
        /// </summary>
        /// <param name="kind">Surface to name.</param>
        /// <returns>The asset name, for <see cref="Load"/> and <see cref="PathOf"/>.</returns>
        /// <remarks>
        /// Only the ground has one. The drop from the land to the water is the one place a
        /// surface is seen from the side, and what it is painted is that surface's own
        /// colour taken down a step - see <see cref="SurfaceTuning.BankShade"/> - so this is
        /// a second asset per surface rather than a second row in the table.
        /// </remarks>
        public static string BankMaterial(SurfaceKind kind) => $"{BankPrefix}{kind}";

        /// <summary>
        /// The materials this class generates, with the colors each one carries.
        /// </summary>
        /// <returns>Asset name, base color and emission color for each material.</returns>
        /// <remarks>
        /// The ground and water surfaces are not here: they wear this project's own
        /// shaders rather than URP's Lit, so they are built by <see cref="EnsureSurfaces"/>
        /// out of <see cref="SurfaceTuning"/> and <see cref="SurfaceLook"/>. Everything
        /// below is a material Unity has to supply because a baked vertex colour cannot
        /// express it, and those are still listed by hand because there is no table for them
        /// to fall out of.
        /// </remarks>
        private static List<(string Name, Color Color, Color Emission, float Smoothness)> MaterialSet()
        {
            var set = new List<(string Name, Color Color, Color Emission, float Smoothness)>
            {
                (Ground, GroundColor, Color.black, DefaultSmoothness),
                (Green, TeamGreenColor, Color.black, DefaultSmoothness),
                (Brown, TeamBrownColor, Color.black, DefaultSmoothness),
                (FrontLight, FrontLightColor, FrontLightEmission, DefaultSmoothness),
                (RearLight, RearLightColor, RearLightEmission, DefaultSmoothness),
                (Tracer, TracerColor, TracerEmission, DefaultSmoothness),
                (Blast, BlastColor, BlastEmission, DefaultSmoothness),
                (Debris, DebrisColor, Color.black, DefaultSmoothness),
            };

            return set;
        }

        /// <summary>
        /// Sets whichever base color property the material's shader actually exposes.
        /// </summary>
        /// <param name="material">Material to recolor.</param>
        /// <param name="color">Color to set.</param>
        /// <remarks>
        /// URP's shaders call it <c>_BaseColor</c>; glTFast's call it
        /// <c>baseColorFactor</c>. Setting a property the shader does not have is a silent
        /// no-op, which is exactly the failure that makes this worth centralising.
        /// </remarks>
        private static void ApplyBaseColor(Material material, Color color)
        {
            bool applied = false;
            if (material.HasProperty(UniversalBaseColor))
            {
                material.SetColor(UniversalBaseColor, color);
                applied = true;
            }

            if (material.HasProperty(GltfBaseColor))
            {
                material.SetColor(GltfBaseColor, color);
                applied = true;
            }

            if (!applied)
            {
                Debug.LogWarning($"IronFlag: {material.shader.name} exposes neither "
                    + "_BaseColor nor baseColorFactor; color not applied.");
            }
        }

        /// <summary>
        /// Turns emission on or off for a material.
        /// </summary>
        /// <param name="material">Material to change.</param>
        /// <param name="emission">Emission color; black turns emission off.</param>
        /// <remarks>
        /// This is the reason head and tail lights are their own material group rather than
        /// another palette color: emission is a material property, and a baked vertex color
        /// cannot glow.
        /// </remarks>
        /// <summary>
        /// Sets how glossy a generated material is, where the shader has an opinion.
        /// </summary>
        /// <param name="material">Material to set.</param>
        /// <param name="smoothness">Zero for matte, one for a mirror.</param>
        private static void ApplySmoothness(Material material, float smoothness)
        {
            if (material.HasProperty(Smoothness))
            {
                material.SetFloat(Smoothness, smoothness);
            }
        }

        private static void ApplyEmission(Material material, Color emission)
        {
            if (!material.HasProperty(EmissionColor))
            {
                return;
            }

            bool lit = emission.maxColorComponent > 0.0f;
            material.SetColor(EmissionColor, emission);
            material.globalIlluminationFlags = lit
                ? MaterialGlobalIlluminationFlags.RealtimeEmissive
                : MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            if (lit)
            {
                material.EnableKeyword("_EMISSION");
            }
            else
            {
                material.DisableKeyword("_EMISSION");
            }
        }
    }
}
