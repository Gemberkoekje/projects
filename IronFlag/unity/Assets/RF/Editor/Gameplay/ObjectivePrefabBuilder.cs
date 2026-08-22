using UnityEditor;
using UnityEngine;
using IronFlag.Destruction;
using IronFlag.Editor.ArtPipeline;
using IronFlag.Objective;

namespace IronFlag.Editor.Gameplay
{
    /// <summary>
    /// Assembles the two prefabs a match is won with: the flag, and the tower it might be
    /// standing on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Small, because neither of these is made of much - a model and one component each.
    /// They are prefabs rather than models placed directly by the scene builder for the same
    /// reason the destructibles are: M7 builds the real map, and placing an objective should
    /// be one line there rather than a copy of the wiring below.
    /// </para>
    /// <para>
    /// The tower is built from its intact model alone. Its other two states are on disk
    /// because the asset spec asks every structure for three, but nothing in the game can
    /// shoot the tower down, so nothing ever shows them - see <see cref="FlagTower"/> for
    /// why that is deliberate rather than unfinished.
    /// </para>
    /// </remarks>
    public static class ObjectivePrefabBuilder
    {
        /// <summary>Folder the flag is written to.</summary>
        public const string PropFolder = "Assets/RF/Prefabs/Props";

        /// <summary>Folder the tower is written to.</summary>
        public const string StructureFolder = "Assets/RF/Prefabs/Structures";

        /// <summary>Folder the models are imported into.</summary>
        public const string ModelFolder = "Assets/RF/Art/Models";

        /// <summary>Asset name of the flag, which is also its model name.</summary>
        public const string FlagAsset = "RF_Prop_Flag";

        /// <summary>Asset name of the flag tower prefab.</summary>
        public const string TowerAsset = "RF_Structure_FlagTower";

        /// <summary>Where the flag prefab is written.</summary>
        public static string FlagPrefabPath => $"{PropFolder}/{FlagAsset}.prefab";

        /// <summary>Where the tower prefab is written.</summary>
        public static string TowerPrefabPath => $"{StructureFolder}/{TowerAsset}.prefab";

        /// <summary>
        /// Rebuilds the flag and the flag tower.
        /// </summary>
        [MenuItem("Tools/IronFlag/Build Objective Prefabs", false, 154)]
        public static void BuildAll()
        {
            GeneratedMaterials.EnsureAssets();
            GeneratedMaterials.EnsureAssetFolder(PropFolder);
            GeneratedMaterials.EnsureAssetFolder(StructureFolder);

            GameObject flag = BuildFlag();
            GameObject tower = BuildTower();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                $"IronFlag: built the objective prefabs - {Named(flag)}, {Named(tower)}.");
        }

        /// <summary>
        /// Builds whichever objective prefab is missing, leaving any that exist alone.
        /// </summary>
        /// <remarks>
        /// Called by the sandbox scene builder, which cannot place a flag that has not been
        /// assembled yet.
        /// </remarks>
        public static void EnsureAssets()
        {
            if (LoadFlag() == null || LoadTower() == null)
            {
                BuildAll();
            }
        }

        /// <summary>Loads the flag prefab.</summary>
        /// <returns>The prefab, or <c>null</c> when it has not been built yet.</returns>
        public static GameObject LoadFlag()
            => AssetDatabase.LoadAssetAtPath<GameObject>(FlagPrefabPath);

        /// <summary>Loads the flag tower prefab.</summary>
        /// <returns>The prefab, or <c>null</c> when it has not been built yet.</returns>
        public static GameObject LoadTower()
            => AssetDatabase.LoadAssetAtPath<GameObject>(TowerPrefabPath);

        /// <summary>
        /// Builds the flag prefab.
        /// </summary>
        /// <returns>The saved prefab asset, or <c>null</c> when its model is missing.</returns>
        /// <remarks>
        /// No collider, deliberately. A flag is picked up by being driven near rather than
        /// driven into - see <see cref="FlagRules.PickupRadius"/> - and a flag a vehicle
        /// could bump into would let a defender park on top of their own and physically
        /// shove the raider off it.
        /// </remarks>
        public static GameObject BuildFlag()
        {
            GameObject model = LoadModel(FlagAsset);
            if (model == null)
            {
                Debug.LogWarning(
                    $"IronFlag: {FlagAsset} is missing; re-run the Blender art pipeline. "
                    + "There will be nothing to capture.");
                return null;
            }

            var root = new GameObject(FlagAsset);
            try
            {
                Fit(root, model);
                root.AddComponent<Flag>();
                return PrefabUtility.SaveAsPrefabAsset(root, FlagPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Builds the flag tower prefab: a destructible with an objective bolted to it.
        /// </summary>
        /// <returns>The saved prefab asset, or <c>null</c> when its models are missing.</returns>
        /// <remarks>
        /// <para>
        /// The masonry is assembled by <see cref="DestructiblePrefabBuilder.Assemble"/>,
        /// which gives the tower the same three states, the same colliders and the same
        /// debris as every other thing on the map that can be knocked down. What is added
        /// here is the one component that makes it an objective rather than scenery.
        /// </para>
        /// <para>
        /// Built here rather than by the destructible builder because a tower is placed as
        /// an objective - it needs a side and a real-or-decoy flag - so it is not on
        /// <see cref="StructureTuning.Roster"/> and nothing scatters it as cover. The paths
        /// agree either way: both builders name it <c>RF_Structure_FlagTower</c>.
        /// </para>
        /// </remarks>
        public static GameObject BuildTower()
        {
            GameObject root = DestructiblePrefabBuilder.Assemble(StructureKind.FlagTower);
            if (root == null)
            {
                return null;
            }

            try
            {
                root.AddComponent<FlagTower>();
                return PrefabUtility.SaveAsPrefabAsset(root, TowerPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Hangs a model off a prefab root and rebinds its material groups.
        /// </summary>
        /// <param name="root">Prefab root being assembled.</param>
        /// <param name="model">Imported model to instantiate.</param>
        /// <returns>The unpacked model instance.</returns>
        /// <remarks>
        /// Only the flag comes through here now that the tower is assembled as a
        /// destructible. Unpacked rather than left as a nested prefab instance for the
        /// reason <see cref="DestructiblePrefabBuilder"/> already documents: a nested
        /// instance cannot have components added to its children.
        /// </remarks>
        private static GameObject Fit(GameObject root, GameObject model)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.transform.SetParent(root.transform, false);
            PrefabUtility.UnpackPrefabInstance(
                instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            // Left wearing the neutral placeholder Blender exported. The flag's team colour
            // is assigned where it is placed, because one prefab serves both sides.
            GeneratedMaterials.Apply(instance, string.Empty);
            return instance;
        }

        private static GameObject LoadModel(string asset)
            => AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelFolder}/{asset}.glb");

        private static string Named(GameObject prefab) => prefab == null ? "nothing" : prefab.name;
    }
}
