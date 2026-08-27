using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Editor.ArtPipeline;
using IronFlag.Supply;
using IronFlag.Vehicles;

namespace IronFlag.Editor.Gameplay
{
    /// <summary>
    /// Turns the exported vehicle models into drivable prefabs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Prefabs are generated for the same reason the models are: what a vehicle prefab
    /// contains follows mechanically from what the model contains, and a generated prefab
    /// picks up an art change - a renamed wheel, a new turret - by being rebuilt rather
    /// than by someone remembering to rewire it. Nothing here is meant to be hand-edited;
    /// tuning numbers belong in <see cref="VehicleTuning.For"/>, not in the prefab.
    /// </para>
    /// <para>
    /// The generated hierarchy is always root, <c>Visual</c>, model:
    /// </para>
    /// <code>
    /// RF_Vehicle_Tank      rigidbody, collider, controller, turret, team paint,
    ///                      health, supply, weapon, bunker bay
    /// +- Visual            cosmetic tilt only, so the collider never leans
    ///    +- Model          the imported .glb, still a prefab instance
    ///       +- Turret, TeamTrim, LightsFront, ...
    ///          +- Muzzle          the dark barrel mouth Blender exports
    ///             +- MuzzlePoint  where rounds actually leave, measured from it
    /// </code>
    /// <para>
    /// The model stays a nested prefab instance so re-importing the <c>.glb</c> flows
    /// through, and the cosmetic tilt gets its own node so a banking helicopter does not
    /// bank its collider with it.
    /// </para>
    /// </remarks>
    public static class VehiclePrefabBuilder
    {
        /// <summary>Folder the generated prefabs are written to.</summary>
        public const string PrefabFolder = "Assets/RF/Prefabs/Vehicles";

        /// <summary>Folder the exported models are read from.</summary>
        public const string ModelFolder = "Assets/RF/Art/Models";

        /// <summary>Name of the node the cosmetic tilt is applied to.</summary>
        public const string VisualNodeName = "Visual";

        /// <summary>Name the imported model takes inside the prefab.</summary>
        public const string ModelNodeName = "Model";

        /// <summary>Child object the Blender pipeline puts the team-colored faces in.</summary>
        private const string TeamTrimName = "TeamTrim";

        /// <summary>Prefix shared by every wheel object in a model.</summary>
        private const string WheelPrefix = "Wheel";

        /// <summary>The wheels that steer, by the names the jeep exports them under.</summary>
        private static readonly string[] SteeredWheelNames = { "Wheel_FL", "Wheel_FR" };

        /// <summary>Names a rotating weapon mount goes by, in the order they are looked for.</summary>
        private static readonly string[] TurretNames = { "Turret", "Launcher" };

        /// <summary>Child object the Blender pipeline puts the barrel mouth in.</summary>
        private const string MuzzleName = "Muzzle";

        /// <summary>Name of the generated node rounds actually leave from.</summary>
        public const string MuzzlePointName = "MuzzlePoint";

        /// <summary>Seconds a wrecked vehicle spends being repaired in the bunker.</summary>
        private const float RepairSeconds = 4.0f;

        /// <summary>Seconds the ride out of the bunker takes, on the lift or off the pad.</summary>
        private const float DeployRideSeconds = 1.2f;

        /// <summary>Smallest explosion a wreck is allowed to make, in metres.</summary>
        private const float SmallestWreckBlast = 2.5f;

        /// <summary>Main rotor object name.</summary>
        private const string MainRotorName = "MainRotor";

        /// <summary>Tail rotor object name.</summary>
        private const string TailRotorName = "TailRotor";

        /// <summary>
        /// Rebuilds every vehicle prefab from the models currently on disk.
        /// </summary>
        [MenuItem("Tools/IronFlag/Build Vehicle Prefabs", false, 150)]
        public static void BuildAll()
        {
            GeneratedMaterials.EnsureAssets();
            // A gun cannot be bound to a round that does not exist, and the combat prefabs
            // are cheap to check for, so building vehicles implies building those first.
            CombatPrefabBuilder.EnsureAssets();
            GeneratedMaterials.EnsureAssetFolder(PrefabFolder);

            var built = new List<string>();
            foreach (VehicleKind kind in Roster())
            {
                GameObject prefab = Build(kind);
                if (prefab != null)
                {
                    built.Add(prefab.name);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"IronFlag: built {built.Count} vehicle prefabs - {string.Join(", ", built)}.");
        }

        /// <summary>
        /// The four vehicles, in roster order.
        /// </summary>
        /// <returns>Every <see cref="VehicleKind"/> except <see cref="VehicleKind.None"/>.</returns>
        /// <remarks>
        /// The game's own list rather than a second copy of it. It used to be written out
        /// here because nothing in the runtime needed one; the level format now counts a
        /// stock of each vehicle, so <see cref="VehicleRoster.Kinds"/> exists on the other
        /// side of the assembly line and this is the same four names, once.
        /// </remarks>
        public static IEnumerable<VehicleKind> Roster() => VehicleRoster.Kinds;

        /// <summary>
        /// Returns the asset name shared by one vehicle's model and prefab.
        /// </summary>
        /// <param name="kind">Vehicle to name.</param>
        /// <returns>The <c>RF_Vehicle_*</c> asset name.</returns>
        /// <remarks>
        /// The mapping is spelled out rather than derived from the enum because the ASV is
        /// an acronym in the asset spec and a word in C#.
        /// </remarks>
        public static string AssetNameFor(VehicleKind kind)
        {
            switch (kind)
            {
                case VehicleKind.Jeep:
                    return "RF_Vehicle_Jeep";
                case VehicleKind.Tank:
                    return "RF_Vehicle_Tank";
                case VehicleKind.Asv:
                    return "RF_Vehicle_ASV";
                case VehicleKind.Helicopter:
                    return "RF_Vehicle_Helicopter";
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Returns where one vehicle's model is imported from.
        /// </summary>
        /// <param name="kind">Vehicle to look up.</param>
        /// <returns>The project-relative path of the <c>.glb</c>.</returns>
        public static string ModelPathFor(VehicleKind kind)
            => $"{ModelFolder}/{AssetNameFor(kind)}.glb";

        /// <summary>
        /// Returns where one vehicle's prefab is written to.
        /// </summary>
        /// <param name="kind">Vehicle to look up.</param>
        /// <returns>The project-relative path of the prefab.</returns>
        public static string PrefabPathFor(VehicleKind kind)
            => $"{PrefabFolder}/{AssetNameFor(kind)}.prefab";

        /// <summary>
        /// Builds one vehicle prefab, replacing any previous version at the same path.
        /// </summary>
        /// <param name="kind">Vehicle to build.</param>
        /// <returns>The saved prefab asset, or <c>null</c> when the model is missing.</returns>
        public static GameObject Build(VehicleKind kind)
        {
            string modelPath = ModelPathFor(kind);
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                Debug.LogWarning($"IronFlag: {modelPath} is missing; run blender/build.ps1 first.");
                return null;
            }

            var root = new GameObject(AssetNameFor(kind));
            try
            {
                var visual = new GameObject(VisualNodeName);
                visual.transform.SetParent(root.transform, false);

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                instance.name = ModelNodeName;
                instance.transform.SetParent(visual.transform, false);

                // The team trim keeps Blender's neutral placeholder here; which side a
                // vehicle is on is a runtime decision, made by VehicleTeamPaint.
                GeneratedMaterials.Apply(instance, string.Empty);

                VehicleTuning tuning = VehicleTuning.For(kind);
                VehicleController controller = AddController(root, kind, tuning, visual.transform);
                Bounds hull = AddCollider(root, instance);
                AddWheels(root, instance, controller);
                AddTurret(root, instance, controller, tuning);
                AddRotors(root, instance);
                AddTeamPaint(root, instance);
                AddHealth(root, tuning, hull);
                AddSupply(root, tuning, kind);
                AddWeapon(root, instance, controller, kind);
                AddSmoke(root, hull);
                AddDust(root, controller, hull);
                root.AddComponent<VehicleBay>().Configure(RepairSeconds, DeployRideSeconds);

                string prefabPath = PrefabPathFor(kind);
                return PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Adds the movement component the vehicle's kind calls for.
        /// </summary>
        /// <param name="root">Prefab root.</param>
        /// <param name="kind">Vehicle being built.</param>
        /// <param name="tuning">Handling numbers to stamp.</param>
        /// <param name="visual">Node the cosmetic tilt is applied to.</param>
        /// <returns>The controller that was added.</returns>
        private static VehicleController AddController(
            GameObject root, VehicleKind kind, VehicleTuning tuning, Transform visual)
        {
            root.AddComponent<Rigidbody>();

            if (kind == VehicleKind.Helicopter)
            {
                Helicopter helicopter = root.AddComponent<Helicopter>();
                helicopter.Configure(kind, tuning);
                helicopter.ConfigureFlight(new FlightTuning(), visual);
                helicopter.ApplyBodySettings();
                return helicopter;
            }

            GroundVehicle ground = root.AddComponent<GroundVehicle>();
            ground.Configure(kind, tuning);
            ground.ApplyBodySettings();
            return ground;
        }

        /// <summary>
        /// Fits a box collider to the vehicle's body.
        /// </summary>
        /// <param name="root">Prefab root the collider goes on.</param>
        /// <param name="instance">The imported model, for its renderers.</param>
        /// <returns>The bounds it was fitted to, which is also how big the wreck burns.</returns>
        /// <remarks>
        /// Rotor discs are left out: a helicopter whose collider is as wide as its rotor
        /// span cannot fit anywhere its fuselage plainly can. A single box is enough for
        /// vehicles this size at this camera distance, and it keeps the physics cheap
        /// enough that split-screen never has to pay for the difference.
        /// </remarks>
        private static Bounds AddCollider(GameObject root, GameObject instance)
        {
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool started = false;

            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (IsUnder(renderer.transform, MainRotorName) || IsUnder(renderer.transform, TailRotorName))
                {
                    continue;
                }

                if (started)
                {
                    bounds.Encapsulate(renderer.bounds);
                }
                else
                {
                    bounds = renderer.bounds;
                    started = true;
                }
            }

            if (!started)
            {
                Debug.LogWarning($"IronFlag: {root.name} has no renderers to fit a collider to.");
                return new Bounds(Vector3.zero, Vector3.one);
            }

            BoxCollider box = root.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;
            return bounds;
        }

        /// <summary>
        /// Gives the vehicle a health pool and something to do when it runs out.
        /// </summary>
        /// <param name="root">Prefab root the component goes on.</param>
        /// <param name="tuning">Handling numbers, for the hit points.</param>
        /// <param name="hull">Bounds of the vehicle, for how big the wreck burns.</param>
        /// <remarks>
        /// The explosion is sized to the vehicle rather than fixed, so the ASV going up is
        /// visibly a bigger event than the jeep going up - which is the only cue in M3 that
        /// says which of the two you just killed.
        /// </remarks>
        private static void AddHealth(GameObject root, VehicleTuning tuning, Bounds hull)
        {
            float blast = Mathf.Max(SmallestWreckBlast, hull.extents.magnitude);
            root.AddComponent<VehicleHealth>()
                .Configure(tuning.HitPoints, CombatPrefabBuilder.LoadExplosion(), blast);
        }

        /// <summary>
        /// Gives the vehicle something to smoke with once it has been hurt.
        /// </summary>
        /// <param name="root">Prefab root the component goes on.</param>
        /// <param name="hull">Bounds of the vehicle, for where the smoke comes from.</param>
        /// <remarks>
        /// The plume sits high on the hull rather than at its middle, because smoke comes off
        /// an engine deck and not out of a floor. It is sized off the same number the wreck
        /// explosion is, so the column that marks where a vehicle died is the same size as the
        /// blast that killed it - the ASV going up is visibly a bigger event than the jeep
        /// going up in both.
        /// </remarks>
        private static void AddSmoke(GameObject root, Bounds hull)
        {
            var at = new Vector3(
                hull.center.x, hull.center.y + (hull.extents.y * 0.6f), hull.center.z);
            VfxPrefabBuilder.AddDamageSmoke(
                root, at, Mathf.Max(SmallestWreckBlast, hull.extents.magnitude));
        }

        /// <summary>
        /// Gives a vehicle that drives on the ground something to kick up off it.
        /// </summary>
        /// <param name="root">Prefab root the component goes on.</param>
        /// <param name="controller">The movement component, to tell a driver from a pilot.</param>
        /// <param name="hull">Bounds of the vehicle, for where the dust comes off.</param>
        /// <remarks>
        /// The helicopter is excluded by the one question that already tells the two apart -
        /// is this a <see cref="GroundVehicle"/> - rather than by naming it, which is the same
        /// move <see cref="DustTrail"/> makes and for the same reason: a vehicle that does not
        /// touch the ground cannot lift anything off it.
        /// </remarks>
        private static void AddDust(GameObject root, VehicleController controller, Bounds hull)
        {
            if (!(controller is GroundVehicle))
            {
                return;
            }

            // Behind the back axle and just off the ground. Vehicles are authored facing +Z,
            // so behind is negative, and the floor of the hull is where the wheels meet it.
            var at = new Vector3(
                hull.center.x,
                Mathf.Max(0.1f, hull.center.y - hull.extents.y + 0.15f),
                hull.center.z - (hull.extents.z * 0.85f));

            VfxPrefabBuilder.AddDustTrail(root, at, hull.size.x);
        }

        /// <summary>
        /// Gives the vehicle a tank of fuel and a load of ammunition.
        /// </summary>
        /// <param name="root">Prefab root the component goes on.</param>
        /// <param name="tuning">Handling numbers, for the fuel half.</param>
        /// <param name="kind">Vehicle being built, for the weapon table lookup.</param>
        /// <remarks>
        /// The two pools come from the two tables that already exist rather than from a
        /// third one: how far a vehicle can go is a handling number and how long it can
        /// shoot is a weapon number, and stamping them together here is the only place
        /// they meet.
        /// </remarks>
        private static void AddSupply(GameObject root, VehicleTuning tuning, VehicleKind kind)
            => root.AddComponent<VehicleSupply>().Configure(
                tuning.FuelCapacity, tuning.IdleFuelDraw, WeaponTuning.For(kind).Rounds);

        /// <summary>
        /// Mounts the vehicle's gun on the muzzle its model exports.
        /// </summary>
        /// <param name="root">Prefab root the component goes on.</param>
        /// <param name="instance">The imported model, to find the muzzle in.</param>
        /// <param name="controller">Vehicle whose fire input the gun reads.</param>
        /// <param name="kind">Vehicle being built, for the weapon table lookup.</param>
        /// <remarks>
        /// The point rounds leave from is measured off the muzzle's own geometry rather
        /// than taken from its origin, for the same reason the wheel radius is measured
        /// rather than hard-coded: the models are generated, and a barrel that gets longer
        /// should not need this file edited. It is parented to the muzzle, so a muzzle that
        /// belongs to a turret traverses the firing point with it - which is what makes
        /// "the gun fires where it is pointing" true of all four vehicles at once.
        /// </remarks>
        private static void AddWeapon(
            GameObject root, GameObject instance, VehicleController controller, VehicleKind kind)
        {
            WeaponTuning weapon = WeaponTuning.For(kind);
            if (!weapon.Exists)
            {
                return;
            }

            Transform muzzle = Find(instance.transform, MuzzleName);
            if (muzzle == null)
            {
                Debug.LogWarning(
                    $"IronFlag: {root.name} carries a {weapon.Kind} but its model has no "
                    + $"{MuzzleName} object; it will not be able to fire.");
                return;
            }

            var point = new GameObject(MuzzlePointName);
            point.transform.SetParent(muzzle, false);
            point.transform.SetPositionAndRotation(MuzzleTip(muzzle), Quaternion.identity);

            root.AddComponent<VehicleWeapon>().Configure(
                controller,
                point.transform,
                weapon,
                CombatPrefabBuilder.LoadProjectile(weapon.Kind),
                CombatPrefabBuilder.LoadMuzzleFlash());
        }

        /// <summary>
        /// Returns the world-space point at the business end of a muzzle.
        /// </summary>
        /// <param name="muzzle">The muzzle object exported with the model.</param>
        /// <returns>
        /// The front face of the muzzle geometry, or the muzzle's origin when it has
        /// nothing to render.
        /// </returns>
        /// <remarks>
        /// Vehicles are built facing +Z with their transforms applied, so "the front" is
        /// the far end of the bounds along Z. Spawning a round at the middle of the muzzle
        /// instead would start it inside the barrel it just came out of.
        /// </remarks>
        private static Vector3 MuzzleTip(Transform muzzle)
        {
            Renderer mouth = muzzle.GetComponentInChildren<Renderer>(true);
            if (mouth == null)
            {
                return muzzle.position;
            }

            Bounds bounds = mouth.bounds;
            return bounds.center + (Vector3.forward * bounds.extents.z);
        }

        private static void AddWheels(GameObject root, GameObject instance, VehicleController controller)
        {
            var rolling = new List<Transform>();
            foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
            {
                if (child.parent == instance.transform
                    && child.name.StartsWith(WheelPrefix, StringComparison.Ordinal))
                {
                    rolling.Add(child);
                }
            }

            if (rolling.Count == 0)
            {
                return;
            }

            var steered = new List<Transform>();
            foreach (Transform wheel in rolling)
            {
                foreach (string name in SteeredWheelNames)
                {
                    if (wheel.name == name)
                    {
                        steered.Add(wheel);
                    }
                }
            }

            VehicleWheels wheels = root.AddComponent<VehicleWheels>();
            wheels.Configure(controller, rolling.ToArray(), steered.ToArray(), WheelRadius(rolling[0]));
        }

        /// <summary>
        /// Measures a wheel's radius from the mesh it renders.
        /// </summary>
        /// <param name="wheel">A wheel object.</param>
        /// <returns>The radius in metres, or a sane default when the wheel has no renderer.</returns>
        /// <remarks>
        /// Wheels are cylinders lying on their side with their axle along X, so the radius
        /// is the vertical half-extent. Measuring beats hard-coding: the models are
        /// generated, and a wheel that changes size should not need this file edited.
        /// </remarks>
        private static float WheelRadius(Transform wheel)
        {
            Renderer renderer = wheel.GetComponentInChildren<Renderer>(true);
            return renderer == null ? 0.35f : renderer.bounds.extents.y;
        }

        private static void AddTurret(
            GameObject root, GameObject instance, VehicleController controller, VehicleTuning tuning)
        {
            if (tuning.TurretTurnRate <= 0.0f)
            {
                return;
            }

            Transform turret = FindAny(instance.transform, TurretNames);
            if (turret == null)
            {
                Debug.LogWarning($"IronFlag: {root.name} has a traverse rate but no turret object.");
                return;
            }

            VehicleTurret mount = root.AddComponent<VehicleTurret>();
            mount.Configure(controller, turret, tuning.TurretTurnRate);
        }

        private static void AddRotors(GameObject root, GameObject instance)
        {
            Transform main = Find(instance.transform, MainRotorName);
            Transform tail = Find(instance.transform, TailRotorName);
            if (main == null && tail == null)
            {
                return;
            }

            VehicleRotors rotors = root.AddComponent<VehicleRotors>();
            rotors.Configure(main, tail);
        }

        private static void AddTeamPaint(GameObject root, GameObject instance)
        {
            Renderer trim = null;
            Transform node = Find(instance.transform, TeamTrimName);
            if (node != null)
            {
                trim = node.GetComponent<Renderer>();
            }

            if (trim == null)
            {
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    if (GeneratedMaterials.IsTeamTrim(renderer))
                    {
                        trim = renderer;
                        break;
                    }
                }
            }

            if (trim == null)
            {
                Debug.LogWarning($"IronFlag: {root.name} has no team trim; it will not take a side.");
                return;
            }

            VehicleTeamPaint paint = root.AddComponent<VehicleTeamPaint>();
            paint.Configure(
                trim,
                GeneratedMaterials.Load(GeneratedMaterials.Green),
                GeneratedMaterials.Load(GeneratedMaterials.Brown));
        }

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

        private static Transform FindAny(Transform root, IEnumerable<string> names)
        {
            foreach (string name in names)
            {
                Transform found = Find(root, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static bool IsUnder(Transform node, string ancestorName)
        {
            for (Transform current = node; current != null; current = current.parent)
            {
                if (current.name == ancestorName)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
