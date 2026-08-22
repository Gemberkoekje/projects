using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using IronFlag.Destruction;
using IronFlag.Editor.ArtPipeline;
using IronFlag.Levels;

namespace IronFlag.Editor.Gameplay
{
    /// <summary>
    /// Builds the <see cref="LevelCatalog"/> the game loads maps with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other reference in this project is resolved in the editor, by path, at the
    /// moment a scene or a prefab is generated. A map that loads at runtime cannot do that -
    /// a built player has no asset database - so the references have to be serialised into
    /// an asset ahead of time, and this is what fills that asset in.
    /// </para>
    /// <para>
    /// Generated rather than hand-assigned, like everything else here: a catalog somebody
    /// dragged prefabs into is a catalog that silently rots when a prefab is rebuilt under a
    /// new name, and the symptom would be a map missing its depots.
    /// </para>
    /// </remarks>
    public static class LevelCatalogBuilder
    {
        /// <summary>Folder the catalog lives in.</summary>
        public const string Folder = "Assets/RF/Levels";

        /// <summary>Path of the catalog asset.</summary>
        public const string CatalogPath = Folder + "/LevelCatalog.asset";

        /// <summary>Folder the imported models are read from.</summary>
        public const string ModelFolder = "Assets/RF/Art/Models";

        /// <summary>Model the bunkers are placed from.</summary>
        public const string BunkerModel = "RF_Structure_Bunker";

        /// <summary>
        /// Rebuilds the level catalog from whatever is on disk now.
        /// </summary>
        [MenuItem("Tools/IronFlag/Build Level Catalog", false, 155)]
        public static void BuildAndSave()
        {
            LevelCatalog catalog = Build();
            if (catalog == null)
            {
                return;
            }

            foreach (string problem in catalog.Problems())
            {
                Debug.LogWarning($"IronFlag: {problem}");
            }

            Debug.Log($"IronFlag: level catalog saved to {CatalogPath}");
        }

        /// <summary>
        /// Creates the catalog if it is missing, and refreshes it either way.
        /// </summary>
        /// <returns>The catalog asset.</returns>
        public static LevelCatalog Build()
        {
            DestructiblePrefabBuilder.EnsureAssets();
            ObjectivePrefabBuilder.EnsureAssets();
            GeneratedMaterials.EnsureAssets();
            GeneratedMaterials.EnsureAssetFolder(Folder);

            LevelCatalog catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<LevelCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            var rows = new List<LevelStructurePrefab>();
            foreach (StructureKind kind in StructureTuning.Roster())
            {
                rows.Add(new LevelStructurePrefab
                {
                    Kind = kind,
                    Prefab = DestructiblePrefabBuilder.Load(kind),
                });
            }

            catalog.Configure(
                rows,
                AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelFolder}/{BunkerModel}.glb"),
                ObjectivePrefabBuilder.LoadTower(),
                ObjectivePrefabBuilder.LoadFlag());

            catalog.ConfigureMaterials(
                GeneratedMaterials.Load(GeneratedMaterials.Ground),
                GeneratedMaterials.Load(GeneratedMaterials.Water),
                GeneratedMaterials.Load(GeneratedMaterials.Green),
                GeneratedMaterials.Load(GeneratedMaterials.Brown),
                GeneratedMaterials.Load(GeneratedMaterials.FrontLight),
                GeneratedMaterials.Load(GeneratedMaterials.RearLight));

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return catalog;
        }

        /// <summary>
        /// Loads the catalog, building it first if it is not there.
        /// </summary>
        /// <returns>The catalog asset.</returns>
        public static LevelCatalog Load()
        {
            LevelCatalog catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(CatalogPath);
            return catalog == null ? Build() : catalog;
        }
    }
}
