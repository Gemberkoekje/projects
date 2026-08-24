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

        /// <summary>Base color property used by URP's own shaders.</summary>
        private static readonly int UniversalBaseColor = Shader.PropertyToID("_BaseColor");

        /// <summary>Base color property used by glTFast's imported materials.</summary>
        private static readonly int GltfBaseColor = Shader.PropertyToID("baseColorFactor");

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
        /// The ground surfaces are read out of <see cref="SurfaceTuning"/> rather than
        /// listed here, so that adding a surface is one enum member and one row of that
        /// table - and so that the colour a level is painted and the colour a level is
        /// balanced around are the same number. Everything else is a material Unity has to
        /// supply because a baked vertex colour cannot express it, and those are still
        /// listed by hand because there is no table for them to fall out of.
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

            foreach (SurfaceKind kind in SurfaceTuning.Roster())
            {
                SurfaceTuning surface = SurfaceTuning.For(kind);
                set.Add((SurfaceMaterial(kind), surface.Colour, Color.black, surface.Smoothness));
            }

            // And a bank for every surface that is ground, painted the same colour taken
            // down a step. Derived rather than listed, so that repainting a surface repaints
            // its coastline with it and the map cannot grow a colour the ramp does not have.
            foreach (SurfaceKind kind in SurfaceTuning.Stack(false))
            {
                SurfaceTuning surface = SurfaceTuning.For(kind);
                set.Add((BankMaterial(kind), surface.Bank, Color.black, surface.Smoothness));
            }

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
