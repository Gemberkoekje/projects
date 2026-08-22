using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using IronFlag.Destruction;
using IronFlag.Editor.ArtPipeline;

namespace IronFlag.Editor.Gameplay
{
    /// <summary>
    /// Assembles one prefab per destructible out of the three models Blender exported for
    /// it, so that a building on the map is a single object that can be shot rather than
    /// three separate props somebody has to keep in step.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The asset spec exports a destructible as separate <c>.glb</c> files - one per state,
    /// "not a modifier or hidden-state trick" - and the design document swaps between them
    /// at runtime. Something has to put the three back together, and doing it here rather
    /// than by hand is what makes a re-exported building a rebuilt prefab instead of a
    /// merge conflict.
    /// </para>
    /// <para>
    /// Each state becomes one child object named for the state, carrying that model and its
    /// own mesh colliders, and <see cref="Destructible"/> shows exactly one of them. The
    /// colliders travel with the model on purpose: a rubble pile is a different shape from
    /// the building that stood there, and a single collider fitted to the intact state
    /// would leave a vehicle bumping into a wall that is no longer there.
    /// </para>
    /// </remarks>
    public static class DestructiblePrefabBuilder
    {
        /// <summary>Folder the props are written to.</summary>
        public const string PropFolder = "Assets/RF/Prefabs/Props";

        /// <summary>Folder the structures are written to.</summary>
        public const string StructureFolder = "Assets/RF/Prefabs/Structures";

        /// <summary>Folder the state models are imported into.</summary>
        public const string ModelFolder = "Assets/RF/Art/Models";

        /// <summary>
        /// Rebuilds every destructible prefab.
        /// </summary>
        [MenuItem("Tools/IronFlag/Build Destructible Prefabs", false, 153)]
        public static void BuildAll()
        {
            GeneratedMaterials.EnsureAssets();
            CombatPrefabBuilder.EnsureAssets();
            GeneratedMaterials.EnsureAssetFolder(PropFolder);
            GeneratedMaterials.EnsureAssetFolder(StructureFolder);

            var built = new List<string>();
            foreach (StructureKind kind in StructureTuning.Roster())
            {
                GameObject prefab = Build(kind);
                if (prefab != null)
                {
                    built.Add(prefab.name);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"IronFlag: built {built.Count} destructible prefabs - {string.Join(", ", built)}.");
        }

        /// <summary>
        /// Builds every destructible prefab that is missing, leaving any that exist alone.
        /// </summary>
        /// <remarks>
        /// Called by the sandbox scene builder, which cannot place a building that has not
        /// been assembled yet.
        /// </remarks>
        public static void EnsureAssets()
        {
            foreach (StructureKind kind in StructureTuning.Roster())
            {
                if (Load(kind) == null)
                {
                    BuildAll();
                    return;
                }
            }
        }

        /// <summary>
        /// Returns the asset name of one destructible's prefab.
        /// </summary>
        /// <param name="kind">Structure to name.</param>
        /// <returns>The prefab asset name, which is the model name without its state.</returns>
        public static string AssetNameFor(StructureKind kind) => $"RF_{CategoryOf(kind)}_{kind}";

        /// <summary>
        /// Returns the name of one of a destructible's state models.
        /// </summary>
        /// <param name="kind">Structure to name.</param>
        /// <param name="state">Which state to name.</param>
        /// <returns>The <c>.glb</c> asset name, without the extension.</returns>
        public static string ModelNameFor(StructureKind kind, DestructionState state)
            => $"{AssetNameFor(kind)}_{state}";

        /// <summary>
        /// Returns where one destructible's prefab is written to.
        /// </summary>
        /// <param name="kind">Structure to look up.</param>
        /// <returns>The project-relative path of the prefab.</returns>
        public static string PrefabPathFor(StructureKind kind)
            => $"{FolderFor(kind)}/{AssetNameFor(kind)}.prefab";

        /// <summary>
        /// Loads one destructible's prefab.
        /// </summary>
        /// <param name="kind">Structure to look up.</param>
        /// <returns>The prefab, or <c>null</c> when it has not been built yet.</returns>
        public static GameObject Load(StructureKind kind)
            => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPathFor(kind));

        /// <summary>
        /// Builds one destructible's prefab.
        /// </summary>
        /// <param name="kind">Structure to assemble.</param>
        /// <returns>The saved prefab asset, or <c>null</c> when its models are missing.</returns>
        /// <remarks>
        /// A missing damaged model is not an error: the asset spec gives the bridge two
        /// states rather than three, because a bridge is either crossable or it is not.
        /// A missing intact or destroyed model is, because there is nothing left to build.
        /// </remarks>
        public static GameObject Build(StructureKind kind)
        {
            GameObject root = Assemble(kind);
            if (root == null)
            {
                return null;
            }

            try
            {
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPathFor(kind));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Builds one destructible in the scene, without saving it as a prefab.
        /// </summary>
        /// <param name="kind">Structure to assemble.</param>
        /// <returns>
        /// The root object, which the caller owns and must destroy, or <c>null</c> when its
        /// models are missing.
        /// </returns>
        /// <remarks>
        /// Split out from <see cref="Build"/> so the flag tower can be assembled here and
        /// finished elsewhere. A tower is a destructible with the same three states and the
        /// same numbers as everything else on this list, plus one component and a different
        /// owner - see <see cref="ObjectivePrefabBuilder.BuildTower"/>. A second assembler
        /// for it would be a second answer to what a destruction state is.
        /// </remarks>
        public static GameObject Assemble(StructureKind kind)
        {
            GameObject intactModel = LoadModel(kind, DestructionState.Intact);
            GameObject destroyedModel = LoadModel(kind, DestructionState.Destroyed);

            if (intactModel == null || destroyedModel == null)
            {
                Debug.LogWarning(
                    $"IronFlag: {kind} is missing its intact or destroyed model; "
                    + "re-run the Blender art pipeline. It will not be buildable.");
                return null;
            }

            var root = new GameObject(AssetNameFor(kind));

            GameObject intact = AddState(root, DestructionState.Intact, intactModel);
            GameObject damaged = AddState(
                root, DestructionState.Damaged, LoadModel(kind, DestructionState.Damaged));
            GameObject destroyed = AddState(root, DestructionState.Destroyed, destroyedModel);

            root.AddComponent<Destructible>().Configure(
                kind,
                StructureTuning.For(kind),
                intact,
                damaged,
                destroyed,
                CombatPrefabBuilder.LoadDebris());

            return root;
        }

        /// <summary>
        /// Hangs one state's model off the prefab root, under a child named for the state.
        /// </summary>
        /// <param name="root">Prefab root being assembled.</param>
        /// <param name="state">State this model is.</param>
        /// <param name="model">The imported model, or null when the state has none.</param>
        /// <returns>The state's child object, or <c>null</c> when there was no model.</returns>
        /// <remarks>
        /// The model is unpacked rather than left as a nested prefab instance, because a
        /// nested instance cannot have components added to its children - and every mesh
        /// under it needs a collider.
        /// </remarks>
        private static GameObject AddState(GameObject root, DestructionState state, GameObject model)
        {
            if (model == null)
            {
                return null;
            }

            var host = new GameObject(NodeNameFor(state));
            host.transform.SetParent(root.transform, false);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.transform.SetParent(host.transform, false);
            PrefabUtility.UnpackPrefabInstance(
                instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            GeneratedMaterials.Apply(instance, string.Empty);
            AddColliders(instance);

            return host;
        }

        /// <summary>
        /// Gives one state's model something to be bumped into.
        /// </summary>
        /// <param name="instance">The unpacked model.</param>
        /// <remarks>
        /// Mesh colliders, matching the sandbox's static scenery: these are handfuls of
        /// boxes already, so there is nothing to approximate. Not marked static, unlike the
        /// scenery the sandbox places directly - an object that is turned on and off is not
        /// static, and telling Unity otherwise is how a batched building stays visible after
        /// it has been knocked down.
        /// </remarks>
        private static void AddColliders(GameObject instance)
        {
            foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null)
                {
                    filter.gameObject.AddComponent<MeshCollider>();
                }
            }
        }

        /// <summary>
        /// Returns the child name one state's model hangs off.
        /// </summary>
        /// <param name="state">State to name.</param>
        /// <returns>The node name <see cref="Destructible"/> looks for.</returns>
        public static string NodeNameFor(DestructionState state)
        {
            switch (state)
            {
                case DestructionState.Intact:
                    return Destructible.IntactNodeName;
                case DestructionState.Damaged:
                    return Destructible.DamagedNodeName;
                case DestructionState.Destroyed:
                    return Destructible.DestroyedNodeName;
                default:
                    return state.ToString();
            }
        }

        /// <summary>
        /// Returns which half of the asset spec's naming a structure falls under.
        /// </summary>
        /// <param name="kind">Structure to look up.</param>
        /// <returns><c>Structure</c> for the depots, <c>Prop</c> for everything else.</returns>
        /// <remarks>
        /// Not a judgement about what these things are - it is what the exported files are
        /// called, and the models are the only place that distinction exists.
        /// </remarks>
        private static string CategoryOf(StructureKind kind)
            => kind == StructureKind.DepotFuel
                || kind == StructureKind.DepotAmmo
                || kind == StructureKind.FlagTower
                ? "Structure"
                : "Prop";

        private static string FolderFor(StructureKind kind)
            => CategoryOf(kind) == "Structure" ? StructureFolder : PropFolder;

        private static GameObject LoadModel(StructureKind kind, DestructionState state)
            => AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{ModelFolder}/{ModelNameFor(kind, state)}.glb");
    }
}
