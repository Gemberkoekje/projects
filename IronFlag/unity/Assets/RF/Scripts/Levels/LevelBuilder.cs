using System;
using UnityEngine;
using IronFlag.Core;
using IronFlag.Destruction;
using IronFlag.Objective;
using IronFlag.Supply;

namespace IronFlag.Levels
{
    /// <summary>
    /// Turns a <see cref="LevelDefinition"/> into a map you can drive on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This class knows no coordinates.</strong> Not the width of the channel, not
    /// where a bunker goes, not how far apart the towers stand. Everything it builds it was
    /// told, and that is the test of whether the level format is really the map: if a number
    /// about this map appeared in this file, the format would be a description of the map
    /// rather than the map itself, and the level editor that comes later would have half a
    /// map to edit.
    /// </para>
    /// <para>
    /// It is runtime code called from both sides. The editor's scene generator bakes a level
    /// into the saved scene with it, so opening <c>Sandbox.unity</c> shows the map and the
    /// still can be rendered without pressing Play; <see cref="LevelLoader"/> calls the same
    /// method at load and throws the bake away. One builder, so those two can never disagree
    /// about what a level file means - and the editor passes its own instantiation, which is
    /// the only difference between them.
    /// </para>
    /// <para>
    /// What it deliberately does not build: the players, the cameras, the HUD and the match.
    /// None of those are map, and a level that carried them could not be swapped for another
    /// one mid-session.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// GameObject world = LevelBuilder.Build(level, catalog);
    /// world.transform.SetParent(sceneRoot, false);
    /// </code>
    /// </example>
    public static class LevelBuilder
    {
        /// <summary>Name of the object everything a level builds hangs off.</summary>
        private const string RootName = "Level";

        /// <summary>Name of the sea.</summary>
        private const string SeaName = "Sea";

        /// <summary>Name of the object the land rectangles hang off.</summary>
        private const string LandName = "Land";

        /// <summary>Name of the object the bunkers hang off.</summary>
        private const string BunkerName = "Bunkers";

        /// <summary>Name of the object the towers and flags hang off.</summary>
        private const string ObjectiveName = "Objective";

        /// <summary>Name of the object the destructibles hang off.</summary>
        private const string SceneryName = "Scenery";

        /// <summary>
        /// Builds a whole map, and hands back the object it hangs off.
        /// </summary>
        /// <param name="level">The map to build.</param>
        /// <param name="catalog">The prefabs and materials to build it out of.</param>
        /// <param name="instantiate">
        /// How to make one prefab instance, or <c>null</c> for
        /// <see cref="UnityEngine.Object.Instantiate(UnityEngine.Object)"/>. The editor
        /// passes prefab instantiation, so a baked scene keeps its links to the prefabs it
        /// was built from and picks up a rebuilt one.
        /// </param>
        /// <returns>
        /// A new object named <see cref="RootName"/>, unparented, holding the whole map. It
        /// is returned even when the level was empty or the catalog was short, because a
        /// caller that has to destroy the old map before building the new one needs
        /// something to hold on to either way.
        /// </returns>
        public static GameObject Build(
            LevelDefinition level, LevelCatalog catalog, Func<GameObject, GameObject> instantiate = null)
        {
            var root = new GameObject(RootName);
            if (level == null)
            {
                Debug.LogWarning("IronFlag: there is no level to build, so the map is empty.");
                return root;
            }

            if (catalog == null)
            {
                Debug.LogWarning(
                    $"IronFlag: '{level.Name}' has no level catalog, so there is nothing to build "
                    + "it out of. Run Tools > IronFlag > Build Level Catalog.");
                return root;
            }

            foreach (string problem in catalog.Problems())
            {
                Debug.LogWarning($"IronFlag: {problem}");
            }

            Func<GameObject, GameObject> make = instantiate ?? Copy;

            BuildSea(level, catalog, root.transform);
            BuildLand(level, catalog, root.transform);
            BuildBunkers(level, catalog, root.transform, make);
            BuildObjective(level, catalog, root.transform, make);
            BuildStructures(level, catalog, root.transform, make);

            return root;
        }

        /// <summary>
        /// Lays the sea out under everything, and puts the drowning rule on it.
        /// </summary>
        /// <param name="level">The map being built.</param>
        /// <param name="catalog">What to paint it with.</param>
        /// <param name="parent">Object to hang it off.</param>
        /// <remarks>
        /// A slab rather than a surface, so it has a top to land on and an edge at the
        /// horizon. It keeps its collider: nothing should ever fall past the sea, and a
        /// wreck thrown into it by a blast has to come to rest somewhere.
        /// </remarks>
        private static void BuildSea(LevelDefinition level, LevelCatalog catalog, Transform parent)
        {
            LevelBounds bounds = level.Bounds;
            if (bounds == null)
            {
                return;
            }

            float across = Mathf.Abs(bounds.HalfExtent) * 2.0f;
            GameObject sea = Slab(SeaName, across, bounds.SeaThickness, across, catalog.Water);
            sea.transform.SetParent(parent, false);
            sea.transform.localPosition = new Vector3(
                0.0f, bounds.WaterLevel - (bounds.SeaThickness * 0.5f), 0.0f);

            sea.AddComponent<WaterLine>().Configure(bounds.WaterLevel, bounds.DrownDepth);
        }

        /// <summary>
        /// Raises every rectangle of dry land out of the sea.
        /// </summary>
        /// <param name="level">The map being built.</param>
        /// <param name="catalog">What to paint it with.</param>
        /// <param name="parent">Object to hang it off.</param>
        /// <remarks>
        /// Each rectangle is one box whose top face is exactly <c>y = 0</c>, which is the
        /// plane the whole game is played on: <see cref="IronFlag.Combat.CombatPlane"/>
        /// resolves rounds against it and every vehicle drives on it. The rest of the box is
        /// the bank, and a vehicle that leaves the top of one is in the sea by the time it
        /// reaches the bottom.
        /// </remarks>
        private static void BuildLand(LevelDefinition level, LevelCatalog catalog, Transform parent)
        {
            if (level.Land.Length == 0)
            {
                return;
            }

            float thickness = level.Bounds == null ? 1.0f : level.Bounds.LandThickness;
            var group = new GameObject(LandName);
            group.transform.SetParent(parent, false);

            foreach (LevelLand piece in level.Land)
            {
                if (piece == null || !piece.IsDrawn)
                {
                    Debug.LogWarning("IronFlag: a piece of land has no area and was not built.");
                    continue;
                }

                string name = string.IsNullOrWhiteSpace(piece.Name) ? LandName : piece.Name;
                GameObject slab = Slab(name, piece.Width, thickness, piece.Depth, catalog.Ground);
                slab.transform.SetParent(group.transform, false);
                slab.transform.localPosition = new Vector3(
                    piece.Centre.x, -thickness * 0.5f, piece.Centre.z);
            }
        }

        /// <summary>
        /// Puts each side's bunker on the map.
        /// </summary>
        /// <param name="level">The map being built.</param>
        /// <param name="catalog">What to build it out of.</param>
        /// <param name="parent">Object to hang them off.</param>
        /// <param name="make">How to instantiate a prefab.</param>
        /// <remarks>
        /// The lift and the helipad a vehicle leaves from are found in the model by name, so
        /// moving them is an art change rather than a code change - and the bunker is the
        /// only structure the map gives a supply point of its own side, which is what makes
        /// it home for the fuel gauge and for the win condition at once.
        /// </remarks>
        private static void BuildBunkers(
            LevelDefinition level, LevelCatalog catalog, Transform parent, Func<GameObject, GameObject> make)
        {
            if (level.Bunkers.Length == 0 || catalog.Bunker == null)
            {
                return;
            }

            var group = new GameObject(BunkerName);
            group.transform.SetParent(parent, false);

            foreach (LevelBunker placement in level.Bunkers)
            {
                if (placement == null)
                {
                    continue;
                }

                GameObject instance = PlaceInstance(
                    catalog.Bunker, $"{catalog.Bunker.name} ({placement.Side})", group.transform,
                    placement.Position, placement.YawDegrees, make);

                Paint(instance, catalog, Team.None);
                AddStaticColliders(instance);

                Transform lift = Find(instance.transform, TeamBunker.LiftNodeName);
                Transform pad = Find(instance.transform, TeamBunker.HelipadNodeName);
                if (lift == null || pad == null)
                {
                    Debug.LogWarning(
                        $"IronFlag: {instance.name} has no {TeamBunker.LiftNodeName} or "
                        + $"{TeamBunker.HelipadNodeName}; rebuild the bunker in Blender. "
                        + "Vehicles will deploy from a guess at where the door is.");
                }

                instance.AddComponent<TeamBunker>().Configure(placement.Side, lift, pad);

                // Home ground: the only place that fills both pools, and the only one that
                // will serve the helicopter - which is why it has a pad on the roof.
                instance.AddComponent<SupplyPoint>().Configure(
                    placement.Side,
                    placement.SupplyRadius,
                    placement.SupplyRate,
                    placement.SupplyRate,
                    true);
            }
        }

        /// <summary>
        /// Puts the flag towers up and a flag on the real one of each pair.
        /// </summary>
        /// <param name="level">The map being built.</param>
        /// <param name="catalog">What to build it out of.</param>
        /// <param name="parent">Object to hang them off.</param>
        /// <param name="make">How to instantiate a prefab.</param>
        /// <remarks>
        /// The towers are named for their side and nothing else. Two objects called "real"
        /// and "decoy" in the hierarchy would hand the answer to anybody who opened the
        /// scene, and the scene is the first thing a new player of this project opens.
        /// </remarks>
        private static void BuildObjective(
            LevelDefinition level, LevelCatalog catalog, Transform parent, Func<GameObject, GameObject> make)
        {
            if (level.Towers.Length == 0 || catalog.Tower == null)
            {
                return;
            }

            var group = new GameObject(ObjectiveName);
            group.transform.SetParent(parent, false);

            foreach (LevelTower placement in level.Towers)
            {
                if (placement == null)
                {
                    continue;
                }

                GameObject instance = PlaceInstance(
                    catalog.Tower, $"{catalog.Tower.name} ({placement.Side})", group.transform,
                    placement.Position, placement.YawDegrees, make);

                var tower = instance.GetComponent<FlagTower>();
                if (tower == null)
                {
                    Debug.LogWarning($"IronFlag: {instance.name} is not a flag tower.");
                    continue;
                }

                tower.Configure(placement.Side, placement.HoldsTheFlag);

                if (placement.HoldsTheFlag)
                {
                    BuildFlag(catalog, group.transform, placement.Side, tower, make);
                }
            }
        }

        /// <summary>
        /// Puts one side's flag on its tower.
        /// </summary>
        /// <param name="catalog">What to build it out of.</param>
        /// <param name="parent">Object to hang it off.</param>
        /// <param name="side">Side the flag belongs to.</param>
        /// <param name="tower">The tower it flies from.</param>
        /// <param name="make">How to instantiate a prefab.</param>
        /// <remarks>
        /// The team colour is applied here rather than in the prefab, because one prefab
        /// serves both sides - the same arrangement the vehicles use, minus the component,
        /// since a flag never changes hands between teams.
        /// </remarks>
        private static void BuildFlag(
            LevelCatalog catalog,
            Transform parent,
            Team side,
            FlagTower tower,
            Func<GameObject, GameObject> make)
        {
            if (catalog.Flag == null)
            {
                return;
            }

            GameObject instance = make(catalog.Flag);
            instance.name = $"{catalog.Flag.name} ({side})";
            instance.transform.SetParent(parent, false);

            Paint(instance, catalog, side);
            instance.GetComponent<Flag>().Configure(side, tower);
        }

        /// <summary>
        /// Scatters everything that can be shot down.
        /// </summary>
        /// <param name="level">The map being built.</param>
        /// <param name="catalog">What to build it out of.</param>
        /// <param name="parent">Object to hang them off.</param>
        /// <param name="make">How to instantiate a prefab.</param>
        /// <remarks>
        /// How tough each of these is comes out of <see cref="StructureTuning.For"/> rather
        /// than out of the level file: a level places props, it does not rebalance them. The
        /// supply rates are the exception, and they are placement rather than balance - the
        /// same drum with both rates at zero is scenery.
        /// </remarks>
        private static void BuildStructures(
            LevelDefinition level, LevelCatalog catalog, Transform parent, Func<GameObject, GameObject> make)
        {
            if (level.Structures.Length == 0)
            {
                return;
            }

            var group = new GameObject(SceneryName);
            group.transform.SetParent(parent, false);

            foreach (LevelStructure placement in level.Structures)
            {
                if (placement == null)
                {
                    continue;
                }

                StructureKind kind = placement.Structure;
                GameObject prefab = kind == StructureKind.None ? null : catalog.PrefabFor(kind);
                if (prefab == null)
                {
                    Debug.LogWarning(
                        $"IronFlag: '{placement.Kind}' is not something this game can place, so "
                        + $"the map is missing whatever stood at {placement.Position}.");
                    continue;
                }

                GameObject instance = PlaceInstance(
                    prefab, placement.Name, group.transform, placement.Position, placement.YawDegrees, make);

                if (placement.Supplies)
                {
                    // Ground vehicles only. The helicopter's drawback in the design
                    // document's roster table is that it has to go home to rearm, and a
                    // depot it could hover over would delete it.
                    instance.AddComponent<SupplyPoint>().Configure(
                        Team.None,
                        placement.SupplyRadius,
                        placement.FuelRate,
                        placement.AmmoRate,
                        false);
                }
            }
        }

        /// <summary>
        /// Makes one box of world with a material on it.
        /// </summary>
        /// <param name="name">What to call it.</param>
        /// <param name="width">Size across x, in metres.</param>
        /// <param name="height">Size across y, in metres.</param>
        /// <param name="depth">Size across z, in metres.</param>
        /// <param name="material">What it wears, or <c>null</c> to leave it default.</param>
        /// <returns>The box, unparented, at the origin.</returns>
        private static GameObject Slab(
            string name, float width, float height, float depth, Material material)
        {
            GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = name;
            slab.transform.localScale = new Vector3(width, height, depth);

            if (material != null)
            {
                slab.GetComponent<Renderer>().sharedMaterial = material;
            }

            MarkStatic(slab);
            return slab;
        }

        /// <summary>
        /// Instantiates one prefab, names it, parents it and places it on the map.
        /// </summary>
        /// <param name="prefab">Prefab to instantiate.</param>
        /// <param name="name">What to call the instance, or blank to leave the name <paramref name="make"/> gave it.</param>
        /// <param name="parent">Object to hang it off.</param>
        /// <param name="position">World position to place it at.</param>
        /// <param name="yawDegrees">Heading to face, clockwise from world +Z.</param>
        /// <param name="make">How to instantiate a prefab.</param>
        /// <returns>The placed, parented instance.</returns>
        /// <remarks>
        /// Shared by every placement that instantiates, names, parents and orients a prefab
        /// the same way, so a future change to that sequence - a null-prefab guard, a scale,
        /// a yaw-sign convention - cannot land in only one of the three call sites and leave
        /// the others placed inconsistently with it.
        /// </remarks>
        private static GameObject PlaceInstance(
            GameObject prefab, string name, Transform parent, Vector3 position, float yawDegrees,
            Func<GameObject, GameObject> make)
        {
            GameObject instance = make(prefab);
            if (!string.IsNullOrWhiteSpace(name))
            {
                instance.name = name;
            }

            instance.transform.SetParent(parent, false);
            instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0.0f, yawDegrees, 0.0f));
            return instance;
        }

        /// <summary>
        /// Puts an instance on a side.
        /// </summary>
        /// <param name="instance">The instance to paint.</param>
        /// <param name="catalog">Where the materials come from.</param>
        /// <param name="side">Side to paint it, or <see cref="Team.None"/> to leave it neutral.</param>
        private static void Paint(GameObject instance, LevelCatalog catalog, Team side)
            => ModelPaint.Apply(
                instance, catalog.TrimFor(side), catalog.FrontLight, catalog.RearLight);

        /// <summary>
        /// Gives an imported model something to be bumped into.
        /// </summary>
        /// <param name="instance">Placed model.</param>
        /// <remarks>
        /// Mesh colliders, because these are static and the meshes are already the handful
        /// of primitives the asset spec limits them to - there is nothing to approximate.
        /// Vehicles get a fitted box instead, since they move. The destructible prefabs
        /// carry their own, so this is only ever the bunker, which is placed from its model.
        /// </remarks>
        private static void AddStaticColliders(GameObject instance)
        {
            foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                {
                    continue;
                }

                filter.gameObject.AddComponent<MeshCollider>();
                MarkStatic(filter.gameObject);
            }
        }

        /// <summary>
        /// Marks a piece of map as static, when that means anything.
        /// </summary>
        /// <param name="instance">The object.</param>
        /// <remarks>
        /// Only while the level is being baked into a scene. Static batching and lightmaps
        /// are decided when a scene is built, so the flag on an object created at runtime
        /// buys nothing and only claims something untrue about it.
        /// </remarks>
        private static void MarkStatic(GameObject instance)
        {
            if (!Application.isPlaying)
            {
                instance.isStatic = true;
            }
        }

        /// <summary>
        /// Finds a named object anywhere under a root.
        /// </summary>
        /// <param name="root">Object to search under.</param>
        /// <param name="name">Name to look for.</param>
        /// <returns>The transform, or <c>null</c> when the model does not carry one.</returns>
        private static Transform Find(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }

        private static GameObject Copy(GameObject prefab) => UnityEngine.Object.Instantiate(prefab);
    }
}
