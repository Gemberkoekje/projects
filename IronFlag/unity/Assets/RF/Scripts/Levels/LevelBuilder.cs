using System;
using UnityEngine;
using IronFlag.Core;
using IronFlag.Destruction;
using IronFlag.Objective;
using IronFlag.Supply;
using IronFlag.Vehicles;

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

        /// <summary>Name of the object the derived shelf hangs off.</summary>
        private const string CoastName = "Coast";

        /// <summary>Name of the one collider the whole island is driven on.</summary>
        private const string GroundName = "Ground";

        /// <summary>Name of the object the bunkers hang off.</summary>
        private const string BunkerName = "Bunkers";

        /// <summary>Name of the object the towers and flags hang off.</summary>
        private const string ObjectiveName = "Objective";

        /// <summary>Name of the object the destructibles hang off.</summary>
        private const string SceneryName = "Scenery";

        /// <summary>
        /// How far each sheet of the map is drawn above the one below it, in metres.
        /// </summary>
        /// <remarks>
        /// A surface is not a piece of ground; it is a colour laid over the top of one, and
        /// the map is a stack of those - see <see cref="SurfaceTuning.Layer"/>. Two
        /// centimetres is enough to settle which of two sheets the depth buffer draws from
        /// two hundred metres up, small enough that a vehicle resting on the island's own
        /// collider does not visibly sink into the road it is standing on, and far enough
        /// below <see cref="IronFlag.Combat.CombatPlane.Floor"/> that no round ever meets
        /// one.
        /// </remarks>
        private const float CoastLift = 0.02f;

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
            BuildCoast(level, catalog, root.transform);
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
        /// <para>
        /// A slab rather than a surface, so it has a top to land on and an edge at the
        /// horizon. It keeps its collider: nothing should ever fall past the sea, and a
        /// wreck thrown into it by a blast has to come to rest somewhere.
        /// </para>
        /// <para>
        /// The whole slab is the open sea, and the shelf is laid over it by
        /// <see cref="BuildCoast"/>. That way the sea is one collider and one drowning rule
        /// however many colours it is drawn in, which is what keeps a shelf from being a
        /// thing a vehicle could be standing on.
        /// </para>
        /// <para>
        /// It also carries the <see cref="WaterClock"/>, which is the only thing that makes
        /// the water move. One per level and on the sea itself, so a map with no sea has a
        /// still one by construction rather than by a check.
        /// </para>
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
            sea.AddComponent<WaterClock>();
        }

        /// <summary>
        /// Cuts the island out of the field and raises it out of the sea.
        /// </summary>
        /// <param name="level">The map being built.</param>
        /// <param name="catalog">What to paint it with.</param>
        /// <param name="parent">Object to hang it off.</param>
        /// <remarks>
        /// <para>
        /// Nothing here is drawn per rectangle any more, and that is the point of the phase:
        /// the land is one shape cut from <see cref="SurfaceField"/>, so the coastline the
        /// game measures against and the coastline you can see are the same line and cannot
        /// come apart. A rectangle in a level file is now a thing that <em>describes</em> the
        /// island rather than a thing that is built.
        /// </para>
        /// <para>
        /// One flat sheet per surface, stacked in <see cref="SurfaceTuning.Layer"/> order
        /// with each drawn a couple of centimetres over the last, so the lowest is the whole
        /// island and every other is a patch over it. Their tops are all at <c>y = 0</c> to
        /// within that: <see cref="IronFlag.Combat.CombatPlane"/> resolves every round on
        /// that plane, and these are paint rather than ground.
        /// </para>
        /// <para>
        /// The ground itself is one non-convex <see cref="MeshCollider"/> over the lowest
        /// sheet, which is the whole island, and it is deliberately the top face only. A
        /// collider per sheet would leave a vehicle resting on a two-centimetre step wherever
        /// a road met a field, and giving the bank one would turn the coastline into a wall -
        /// which would stop a vehicle driving into the sea, and driving into the sea is one
        /// of the things this game is about.
        /// </para>
        /// </remarks>
        private static void BuildLand(LevelDefinition level, LevelCatalog catalog, Transform parent)
        {
            if (level.Land.Length == 0)
            {
                return;
            }

            SurfaceField field = level.Field;
            float thickness = level.Bounds == null ? 1.0f : level.Bounds.LandThickness;
            var group = new GameObject(LandName);
            group.transform.SetParent(parent, false);

            Mesh island = null;
            int rank = 0;

            foreach (SurfaceKind kind in SurfaceTuning.Stack(false))
            {
                if (!field.Covers(kind))
                {
                    continue;
                }

                Mesh sheet = SurfaceMesh.Build(field, kind, $"{LandName} ({kind})");
                if (sheet == null)
                {
                    continue;
                }

                float lift = rank * CoastLift;
                Sheet(kind.ToString(), sheet, catalog.MaterialFor(kind), group.transform, lift);
                rank++;

                if (island == null)
                {
                    island = sheet;
                }

                Mesh bank = SurfaceMesh.Bank(field, kind, thickness, $"{LandName} bank ({kind})");
                if (bank != null)
                {
                    Sheet($"{kind} bank", bank, catalog.BankFor(kind), group.transform, lift);
                }
            }

            if (island == null)
            {
                return;
            }

            var ground = new GameObject(GroundName);
            ground.transform.SetParent(group.transform, false);
            ground.AddComponent<MeshCollider>().sharedMesh = island;
            MarkStatic(ground);
        }

        /// <summary>
        /// Lays the shelf, and anything else drawn over the sea, over the sea.
        /// </summary>
        /// <param name="level">The map being built.</param>
        /// <param name="catalog">What to paint them with.</param>
        /// <param name="parent">Object to hang them off.</param>
        /// <remarks>
        /// <para>
        /// The difference between an island and two rectangles in a pond: a pale shelf
        /// hugging every coast, derived from distance to the realised coastline rather than
        /// written into a level file - so every map has one and no map has to remember it.
        /// Its inner edge is the coastline itself rather than a second line that has to
        /// agree with it, which is what stops the open sea showing through between the two.
        /// </para>
        /// <para>
        /// The lowest layer of the water is skipped, because <see cref="BuildSea"/> has
        /// already drawn it: the open sea is a slab rather than a sheet, since it needs a
        /// collider, a thickness and an edge at the horizon. Everything above it is a colour
        /// laid a couple of centimetres over that, with nothing to stand on and nothing to
        /// fall off - which is what keeps a shelf from being something a vehicle could be
        /// standing on while it drowns.
        /// </para>
        /// </remarks>
        private static void BuildCoast(LevelDefinition level, LevelCatalog catalog, Transform parent)
        {
            SurfaceKind[] stack = SurfaceTuning.Stack(true);
            if (stack.Length < 2 || level.Land.Length == 0)
            {
                return;
            }

            SurfaceField field = level.Field;
            float waterLevel = level.Bounds == null ? 0.0f : level.Bounds.WaterLevel;
            GameObject group = null;

            for (int layer = 1; layer < stack.Length; layer++)
            {
                SurfaceKind kind = stack[layer];
                Mesh sheet = SurfaceMesh.Build(
                    field, kind, $"{CoastName} ({kind})", measureShore: true);
                if (sheet == null)
                {
                    continue;
                }

                if (group == null)
                {
                    group = new GameObject(CoastName);
                    group.transform.SetParent(parent, false);
                }

                Sheet(
                    kind.ToString(),
                    sheet,
                    catalog.MaterialFor(kind),
                    group.transform,
                    waterLevel + (layer * CoastLift));
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

                // Everything this side has to spend, on the building it spends it from. A
                // level that says nothing about it is a level played on the standard
                // allotment - see LevelReserve - rather than one with no vehicles at all.
                TeamReserve reserve = instance.AddComponent<TeamReserve>();
                reserve.Configure(placement.Side);
                LevelReserve stock = level.Reserve == null ? new LevelReserve() : level.Reserve;
                foreach (VehicleKind kind in VehicleRoster.Kinds)
                {
                    reserve.Give(kind, stock.For(kind));
                }

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
        /// same drum with both rates at zero is scenery. So is a turret's side, and for the
        /// same reason: which emplacement is whose is a fact about this map.
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

                // A turret and a door are the only things ever on a side, and being on one
                // is the whole of what makes either work: the same answer paints it, aims
                // the gun or decides who the gate opens for, and makes it immune to its
                // owner's fire. A level that gives a side to anything else is refused by
                // LevelValidation rather than quietly obeyed here.
                if (placement.NeedsASide)
                {
                    Team side = placement.Team;
                    var shell = instance.GetComponent<Destructible>();
                    if (shell != null)
                    {
                        shell.SetTeam(side);
                    }

                    Paint(instance, catalog, side);
                }

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
        /// Hangs one generated sheet of map off the level, painted and placed.
        /// </summary>
        /// <param name="name">What to call it.</param>
        /// <param name="mesh">The geometry.</param>
        /// <param name="material">What it wears, or <c>null</c> to leave it default.</param>
        /// <param name="parent">Object to hang it off.</param>
        /// <param name="height">How far above the plane it is drawn, in metres.</param>
        /// <returns>The sheet.</returns>
        /// <remarks>
        /// No collider, ever. Every sheet the map is drawn as is paint over something else;
        /// the one thing a vehicle rests on is the island's own collider, and having exactly
        /// one of those is what keeps a vehicle from finding a two-centimetre step wherever
        /// two colours meet.
        /// </remarks>
        private static GameObject Sheet(
            string name, Mesh mesh, Material material, Transform parent, float height)
        {
            var sheet = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            sheet.transform.SetParent(parent, false);
            sheet.transform.localPosition = new Vector3(0.0f, height, 0.0f);
            sheet.GetComponent<MeshFilter>().sharedMesh = mesh;

            if (material != null)
            {
                sheet.GetComponent<MeshRenderer>().sharedMaterial = material;
            }

            MarkStatic(sheet);
            return sheet;
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
