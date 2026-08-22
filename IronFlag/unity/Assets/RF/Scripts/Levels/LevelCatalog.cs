using System.Collections.Generic;
using UnityEngine;
using IronFlag.Core;
using IronFlag.Destruction;

namespace IronFlag.Levels
{
    /// <summary>
    /// Everything a level file names but does not contain: the prefabs and materials a map
    /// is built out of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A level file says "a fuel depot stands here". Turning that into geometry needs the
    /// depot prefab, and a built game has no asset database to look one up in - that is an
    /// editor-only luxury the rest of this project has been able to rely on because every
    /// scene was generated in the editor. A map that loads at runtime cannot, so the
    /// references are serialised once, here, and the scene carries this asset.
    /// </para>
    /// <para>
    /// It is also the palette a level editor will offer: the set of things that can be put
    /// on a map is exactly the set of rows in this catalog, which is why the lookups warn
    /// by name rather than returning quietly.
    /// </para>
    /// <para>
    /// Built and refreshed by <c>Tools &gt; IronFlag &gt; Build Level Catalog</c>. Nothing
    /// here is hand-assigned, for the same reason no prefab in this project is hand-built.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "LevelCatalog", menuName = "IronFlag/Level Catalog")]
    public sealed class LevelCatalog : ScriptableObject
    {
        [Header("Structures")]
        [SerializeField]
        [Tooltip("One row per destructible kind a level may place.")]
        private List<LevelStructurePrefab> structures = new List<LevelStructurePrefab>();

        [Header("Objective")]
        [SerializeField]
        [Tooltip("The bunker model, one per side.")]
        private GameObject bunker;

        [SerializeField]
        [Tooltip("The flag tower prefab, used for both the real one and the decoy.")]
        private GameObject tower;

        [SerializeField]
        [Tooltip("The flag prefab, one per side.")]
        private GameObject flag;

        [Header("Materials")]
        [SerializeField]
        [Tooltip("Material the land wears.")]
        private Material ground;

        [SerializeField]
        [Tooltip("Material the sea wears.")]
        private Material water;

        [SerializeField]
        [Tooltip("Team accent worn by the green side.")]
        private Material greenTrim;

        [SerializeField]
        [Tooltip("Team accent worn by the brown side.")]
        private Material brownTrim;

        [SerializeField]
        [Tooltip("Material worn by headlight geometry.")]
        private Material frontLight;

        [SerializeField]
        [Tooltip("Material worn by tail-light geometry.")]
        private Material rearLight;

        /// <summary>The bunker model, one per side.</summary>
        public GameObject Bunker => bunker;

        /// <summary>The flag tower prefab.</summary>
        public GameObject Tower => tower;

        /// <summary>The flag prefab.</summary>
        public GameObject Flag => flag;

        /// <summary>Material the land wears.</summary>
        public Material Ground => ground;

        /// <summary>Material the sea wears.</summary>
        public Material Water => water;

        /// <summary>Material worn by headlight geometry.</summary>
        public Material FrontLight => frontLight;

        /// <summary>Material worn by tail-light geometry.</summary>
        public Material RearLight => rearLight;

        /// <summary>
        /// Points the catalog at everything it hands out.
        /// </summary>
        /// <param name="rows">One row per destructible kind.</param>
        /// <param name="bunkerModel">The bunker model.</param>
        /// <param name="towerPrefab">The flag tower prefab.</param>
        /// <param name="flagPrefab">The flag prefab.</param>
        /// <remarks>Called by the editor's catalog builder; nothing assigns these by hand.</remarks>
        public void Configure(
            List<LevelStructurePrefab> rows,
            GameObject bunkerModel,
            GameObject towerPrefab,
            GameObject flagPrefab)
        {
            structures = rows == null ? new List<LevelStructurePrefab>() : rows;
            bunker = bunkerModel;
            tower = towerPrefab;
            flag = flagPrefab;
        }

        /// <summary>
        /// Points the catalog at the materials a map is painted with.
        /// </summary>
        /// <param name="groundMaterial">Material the land wears.</param>
        /// <param name="waterMaterial">Material the sea wears.</param>
        /// <param name="green">Team accent worn by the green side.</param>
        /// <param name="brown">Team accent worn by the brown side.</param>
        /// <param name="front">Material worn by headlight geometry.</param>
        /// <param name="rear">Material worn by tail-light geometry.</param>
        public void ConfigureMaterials(
            Material groundMaterial,
            Material waterMaterial,
            Material green,
            Material brown,
            Material front,
            Material rear)
        {
            ground = groundMaterial;
            water = waterMaterial;
            greenTrim = green;
            brownTrim = brown;
            frontLight = front;
            rearLight = rear;
        }

        /// <summary>
        /// Returns the prefab one kind of destructible is built from.
        /// </summary>
        /// <param name="kind">Kind to look up.</param>
        /// <returns>The prefab, or <c>null</c> when the catalog has no row for it.</returns>
        public GameObject PrefabFor(StructureKind kind)
        {
            foreach (LevelStructurePrefab row in structures)
            {
                if (row != null && row.Kind == kind)
                {
                    return row.Prefab;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the team accent one side wears.
        /// </summary>
        /// <param name="side">Side to look up.</param>
        /// <returns>
        /// The material, or <c>null</c> for <see cref="Team.None"/> - which leaves the model
        /// wearing the neutral placeholder Blender exported, and is what a depot wants.
        /// </returns>
        public Material TrimFor(Team side)
        {
            switch (side)
            {
                case Team.Green:
                    return greenTrim;
                case Team.Brown:
                    return brownTrim;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Lists everything the catalog is missing.
        /// </summary>
        /// <returns>One sentence per gap; empty when a map can be built from this.</returns>
        /// <remarks>
        /// Read by the tests and by <see cref="LevelBuilder"/>, so a catalog that was never
        /// rebuilt after an asset moved says so once, by name, rather than producing a map
        /// with holes in it.
        /// </remarks>
        public List<string> Problems()
        {
            var problems = new List<string>();

            if (bunker == null)
            {
                problems.Add("The catalog has no bunker model: nobody would have anywhere to deploy from.");
            }

            if (tower == null)
            {
                problems.Add("The catalog has no flag tower prefab.");
            }

            if (flag == null)
            {
                problems.Add("The catalog has no flag prefab: there would be nothing to win.");
            }

            if (ground == null || water == null)
            {
                problems.Add("The catalog is missing the ground or water material.");
            }

            if (greenTrim == null || brownTrim == null)
            {
                problems.Add("The catalog is missing a team accent, so a bunker would be colourless.");
            }

            foreach (StructureKind kind in StructureTuning.Roster())
            {
                if (PrefabFor(kind) == null)
                {
                    problems.Add($"The catalog has no prefab for {kind}.");
                }
            }

            return problems;
        }
    }
}
