using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using IronFlag.Combat;
using IronFlag.Destruction;
using IronFlag.Editor.ArtPipeline;

namespace IronFlag.Editor.Gameplay
{
    /// <summary>
    /// Builds the five things combat draws that were never modelled: a round in flight, the
    /// flash a gun makes when it leaves, the sparks it throws off armour it does not get
    /// through, the flash it makes when it arrives, and the debris a structure throws when
    /// it breaks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything else in this game comes out of Blender, and these five deliberately do
    /// not. A tracer is a lit sphere, an explosion is a bigger one, a muzzle flash is a
    /// squashed one, and debris and sparks are cubes, so putting them through the asset
    /// pipeline would add five <c>.glb</c> files carrying no information that is not in the
    /// handful of numbers below. Generating them here also keeps them bound to the same
    /// generated materials the vehicles use, which is the part that is easy to get wrong by
    /// hand.
    /// </para>
    /// <para>
    /// The drawn round is deliberately larger than the round that does the hitting.
    /// <see cref="WeaponTuning.Radius"/> is the sphere the projectile sweeps with, and half a
    /// metre of shell is invisible from thirty-four metres up in half a screen; the prefab's
    /// own proportions do the exaggerating, so the size that decides whether a shot connects
    /// stays the honest one.
    /// </para>
    /// </remarks>
    public static class CombatPrefabBuilder
    {
        /// <summary>Folder the generated combat prefabs are written to.</summary>
        public const string PrefabFolder = "Assets/RF/Prefabs/Combat";

        /// <summary>Asset name of the explosion prefab.</summary>
        public const string ExplosionAssetName = "RF_Explosion";

        /// <summary>Asset name of the debris prefab a structure throws when it breaks.</summary>
        public const string DebrisAssetName = "RF_Debris";

        /// <summary>Asset name of the flash prefab a gun draws when it fires.</summary>
        public const string MuzzleFlashAssetName = "RF_MuzzleFlash";

        /// <summary>Asset name of the spark prefab a round throws off armour it does not kill.</summary>
        public const string SparksAssetName = "RF_Sparks";

        /// <summary>Name of the node a projectile's visible part hangs off.</summary>
        public const string BodyNodeName = "Body";

        /// <summary>Name of the node an explosion's swelling sphere hangs off.</summary>
        public const string BallNodeName = "Ball";

        /// <summary>Name of the node a muzzle flash's collapsing flame hangs off.</summary>
        public const string FlameNodeName = "Flame";

        /// <summary>Seconds an explosion takes from ignition to gone.</summary>
        private const float ExplosionDuration = 0.55f;

        /// <summary>Seconds a debris burst takes from the bang to gone.</summary>
        private const float DebrisDuration = 0.9f;

        /// <summary>Seconds a muzzle flash takes from the shot to gone.</summary>
        /// <remarks>
        /// Four frames at sixty. Shorter and a flash can fall between two of them and never
        /// be drawn at all; much longer and the chaingun - which fires every 125 ms - starts
        /// to look like it is holding a torch rather than firing a gun.
        /// </remarks>
        private const float MuzzleFlashDuration = 0.065f;

        /// <summary>Seconds a spark burst takes from the strike to gone.</summary>
        private const float SparksDuration = 0.22f;

        /// <summary>How many shards a spark burst throws.</summary>
        /// <remarks>
        /// Seven, and it is a small number on purpose: this is the one effect in the game
        /// that can be on screen eight times a second per chaingun, so it is priced as
        /// something that happens constantly rather than as something that happens once.
        /// </remarks>
        private const int SparkShardCount = 7;

        /// <summary>How many chunks a debris burst throws.</summary>
        /// <remarks>
        /// Ten reads as a building coming apart and costs ten transforms for under a second.
        /// Four looked like a mistake and twenty-four was a cloud rather than pieces.
        /// </remarks>
        private const int DebrisChunkCount = 10;

        /// <summary>How much bigger a round is drawn than the sphere it sweeps with.</summary>
        private const float TracerExaggeration = 1.6f;

        /// <summary>How much longer than wide a flat-flying round is drawn.</summary>
        private const float TracerElongation = 3.0f;

        /// <summary>
        /// Rebuilds the explosion and every projectile prefab.
        /// </summary>
        [MenuItem("Tools/IronFlag/Build Combat Prefabs", false, 152)]
        public static void BuildAll()
        {
            GeneratedMaterials.EnsureAssets();
            GeneratedMaterials.EnsureAssetFolder(PrefabFolder);
            // A round binds the spray it puts up as well as the flash it makes on arrival,
            // and the spray is not built here - see VfxPrefabBuilder for why smoke and water
            // live in their own folder.
            VfxPrefabBuilder.EnsureAssets();

            // The explosion and the sparks first: a projectile prefab binds both, and a
            // round built before them would be saved pointing at nothing.
            var built = new List<string>
            {
                BuildExplosion().name,
                BuildDebris().name,
                BuildMuzzleFlash().name,
                BuildSparks().name,
            };
            foreach (WeaponKind kind in Arsenal())
            {
                built.Add(BuildProjectile(kind).name);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"IronFlag: built {built.Count} combat prefabs - {string.Join(", ", built)}.");
        }

        /// <summary>
        /// The weapons that have a round to draw, in roster order.
        /// </summary>
        /// <returns>Every <see cref="WeaponKind"/> except <see cref="WeaponKind.None"/>.</returns>
        /// <remarks>
        /// The four the roster carries, plus the automated turret's - a round has to exist
        /// for every gun that can be built, and the emplacement is the one whose gun belongs
        /// to no vehicle.
        /// </remarks>
        public static IEnumerable<WeaponKind> Arsenal()
        {
            yield return WeaponKind.Grenade;
            yield return WeaponKind.Cannon;
            yield return WeaponKind.Rocket;
            yield return WeaponKind.Chaingun;
            yield return WeaponKind.Autocannon;
        }

        /// <summary>
        /// Returns the asset name of one weapon's round.
        /// </summary>
        /// <param name="kind">Weapon to name.</param>
        /// <returns>The <c>RF_Projectile_*</c> asset name.</returns>
        public static string ProjectileAssetNameFor(WeaponKind kind) => $"RF_Projectile_{kind}";

        /// <summary>
        /// Returns where one generated combat prefab is written to.
        /// </summary>
        /// <param name="assetName">Asset name, without the extension.</param>
        /// <returns>The project-relative path of the prefab.</returns>
        public static string PrefabPathFor(string assetName) => $"{PrefabFolder}/{assetName}.prefab";

        /// <summary>
        /// Loads the explosion prefab.
        /// </summary>
        /// <returns>The explosion, or <c>null</c> when it has not been built yet.</returns>
        public static Explosion LoadExplosion()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabPathFor(ExplosionAssetName));
            return prefab == null ? null : prefab.GetComponent<Explosion>();
        }

        /// <summary>
        /// Loads the debris prefab.
        /// </summary>
        /// <returns>The burst, or <c>null</c> when it has not been built yet.</returns>
        public static DebrisBurst LoadDebris()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabPathFor(DebrisAssetName));
            return prefab == null ? null : prefab.GetComponent<DebrisBurst>();
        }

        /// <summary>
        /// Loads the muzzle flash prefab.
        /// </summary>
        /// <returns>The flash, or <c>null</c> when it has not been built yet.</returns>
        public static MuzzleFlash LoadMuzzleFlash()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabPathFor(MuzzleFlashAssetName));
            return prefab == null ? null : prefab.GetComponent<MuzzleFlash>();
        }

        /// <summary>
        /// Loads the spark prefab.
        /// </summary>
        /// <returns>The burst, or <c>null</c> when it has not been built yet.</returns>
        public static ImpactSparks LoadSparks()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabPathFor(SparksAssetName));
            return prefab == null ? null : prefab.GetComponent<ImpactSparks>();
        }

        /// <summary>
        /// Loads one weapon's round.
        /// </summary>
        /// <param name="kind">Weapon to look up.</param>
        /// <returns>The projectile, or <c>null</c> when it has not been built yet.</returns>
        public static Projectile LoadProjectile(WeaponKind kind)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabPathFor(ProjectileAssetNameFor(kind)));
            return prefab == null ? null : prefab.GetComponent<Projectile>();
        }

        /// <summary>
        /// Builds every combat prefab that is missing, leaving any that already exist alone.
        /// </summary>
        /// <remarks>
        /// Called by <see cref="VehiclePrefabBuilder.BuildAll"/>, which cannot bind a gun to
        /// a round that does not exist yet.
        /// </remarks>
        public static void EnsureAssets()
        {
            bool missing = LoadExplosion() == null
                || LoadDebris() == null
                || LoadMuzzleFlash() == null
                || LoadSparks() == null;
            foreach (WeaponKind kind in Arsenal())
            {
                missing |= LoadProjectile(kind) == null;
            }

            if (missing)
            {
                BuildAll();
            }
        }

        /// <summary>
        /// Builds the explosion prefab.
        /// </summary>
        /// <returns>The saved prefab asset.</returns>
        /// <remarks>
        /// The light is on the root and the sphere is a child, because the sphere is scaled
        /// every frame and a scaled light is a light with a scaled range on top of the range
        /// it was given.
        /// </remarks>
        public static GameObject BuildExplosion()
        {
            var root = new GameObject(ExplosionAssetName);
            try
            {
                GameObject ball = CreateSphere(BallNodeName, root.transform, GeneratedMaterials.Blast);
                ball.transform.localScale = Vector3.zero;

                Light flash = root.AddComponent<Light>();
                flash.type = LightType.Point;
                flash.color = new Color(1.0f, 0.78f, 0.45f);
                flash.shadows = LightShadows.None;
                flash.intensity = 0.0f;

                Explosion burst = root.AddComponent<Explosion>();
                burst.Configure(ball.transform, flash, ExplosionDuration, 2.0f);

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPathFor(ExplosionAssetName));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Builds the debris prefab a structure throws when it changes state.
        /// </summary>
        /// <returns>The saved prefab asset.</returns>
        /// <remarks>
        /// Cubes, because a chunk of a building is a chunk of a building: every model in
        /// this project is a handful of boxes, and the pieces that come off one should look
        /// like they came off it. They are placed by <see cref="DebrisBurst"/> every frame,
        /// so where they sit in the prefab does not matter - only how many there are.
        /// </remarks>
        public static GameObject BuildDebris()
        {
            var root = new GameObject(DebrisAssetName);
            try
            {
                var chunks = new Transform[DebrisChunkCount];
                for (int index = 0; index < chunks.Length; index++)
                {
                    chunks[index] = CreateChunk($"Chunk{index}", root.transform).transform;
                }

                DebrisBurst burst = root.AddComponent<DebrisBurst>();
                burst.Configure(chunks, DebrisDuration, 3.0f);

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPathFor(DebrisAssetName));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Builds the flash prefab a gun draws at its barrel when it fires.
        /// </summary>
        /// <returns>The saved prefab asset.</returns>
        /// <remarks>
        /// <para>
        /// The same two objects an explosion is - a light on the root and a sphere below it,
        /// for the same reason: the sphere is scaled every frame, and a scaled light is a
        /// light with a scaled range on top of the range it was given. What makes it read as
        /// a muzzle flash rather than as a small explosion is entirely in
        /// <see cref="MuzzleFlash"/>: the flame is stretched along the barrel and collapses
        /// instead of swelling.
        /// </para>
        /// <para>
        /// One prefab for every gun in the game, sized per shot from the weapon's calibre.
        /// A tank's flash and a chaingun's differ by a number rather than by an asset,
        /// which is the same trade <see cref="Explosion"/> already makes with blast radius.
        /// </para>
        /// </remarks>
        public static GameObject BuildMuzzleFlash()
        {
            var root = new GameObject(MuzzleFlashAssetName);
            try
            {
                GameObject flame = CreateSphere(FlameNodeName, root.transform, GeneratedMaterials.Blast);
                flame.transform.localScale = Vector3.zero;

                Light glow = root.AddComponent<Light>();
                glow.type = LightType.Point;
                // Whiter than the explosion's light. Burning propellant at the barrel is
                // hotter than the fireball a shell makes when it arrives, and the pair read
                // as two different events partly because of it.
                glow.color = new Color(1.0f, 0.86f, 0.58f);
                glow.shadows = LightShadows.None;
                glow.intensity = 0.0f;

                MuzzleFlash flash = root.AddComponent<MuzzleFlash>();
                flash.Configure(flame.transform, glow, MuzzleFlashDuration, 0.9f);

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPathFor(MuzzleFlashAssetName));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Builds the spark prefab a round throws off armour it does not get through.
        /// </summary>
        /// <returns>The saved prefab asset.</returns>
        /// <remarks>
        /// Cubes wearing the tracer's material, because a spark is a piece of the round that
        /// came back: same colour as the tracer that made it, so a burst reads as belonging
        /// to the gun firing it. They are stretched into streaks and placed by
        /// <see cref="ImpactSparks"/> every frame, so their size and position in the prefab
        /// do not matter - only how many there are.
        /// </remarks>
        public static GameObject BuildSparks()
        {
            var root = new GameObject(SparksAssetName);
            try
            {
                var shards = new Transform[SparkShardCount];
                for (int index = 0; index < shards.Length; index++)
                {
                    GameObject shard = CreateShard($"Shard{index}", root.transform);
                    shard.transform.localScale = Vector3.zero;
                    shards[index] = shard.transform;
                }

                ImpactSparks burst = root.AddComponent<ImpactSparks>();
                burst.Configure(shards, SparksDuration, 1.0f);

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPathFor(SparksAssetName));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Builds one weapon's round.
        /// </summary>
        /// <param name="kind">Weapon whose round to build.</param>
        /// <returns>The saved prefab asset.</returns>
        /// <remarks>
        /// Lobbed rounds are drawn as balls and flat ones as darts, which is the only
        /// difference between the four: a grenade tumbling through an arc and a shell
        /// crossing eighty-five metres should not look like the same object, and the shape
        /// is the cheapest way to say which is which from above.
        /// </remarks>
        public static GameObject BuildProjectile(WeaponKind kind)
        {
            string assetName = ProjectileAssetNameFor(kind);
            var root = new GameObject(assetName);
            try
            {
                var body = new GameObject(BodyNodeName);
                body.transform.SetParent(root.transform, false);

                GameObject mesh = CreateSphere("Mesh", body.transform, GeneratedMaterials.Tracer);
                bool lobbed = Lobs(kind);
                mesh.transform.localScale = new Vector3(
                    TracerExaggeration,
                    TracerExaggeration,
                    lobbed ? TracerExaggeration : TracerExaggeration * TracerElongation);

                Projectile round = root.AddComponent<Projectile>();
                round.Configure(
                    body.transform,
                    LoadExplosion(),
                    LoadSparks(),
                    VfxPrefabBuilder.LoadSplash());

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPathFor(assetName));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Creates one flying piece of a building, with no collider on it.
        /// </summary>
        /// <param name="name">Object name.</param>
        /// <param name="parent">Object to hang it off.</param>
        /// <returns>The chunk.</returns>
        /// <remarks>
        /// No collider, deliberately: debris that could be driven into would be a second,
        /// invisible wall standing where a building used to be, which is the opposite of
        /// what knocking one down is for.
        /// </remarks>
        private static GameObject CreateChunk(string name, Transform parent)
        {
            GameObject chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chunk.name = name;
            chunk.transform.SetParent(parent, false);

            Object.DestroyImmediate(chunk.GetComponent<Collider>());
            chunk.GetComponent<MeshRenderer>().sharedMaterial =
                GeneratedMaterials.Load(GeneratedMaterials.Debris);

            return chunk;
        }

        /// <summary>
        /// Creates one flying fleck of hot metal, with no collider on it.
        /// </summary>
        /// <param name="name">Object name.</param>
        /// <param name="parent">Object to hang it off.</param>
        /// <returns>The shard.</returns>
        /// <remarks>
        /// A cube rather than a sphere, because it is drawn stretched into a streak and a
        /// stretched sphere is a lozenge with soft ends - which at this size is a smudge.
        /// No collider and no shadow, for the reasons a debris chunk and a tracer have
        /// none: nothing should be able to drive into a spark, and nothing this bright
        /// should darken the ground it is lighting.
        /// </remarks>
        private static GameObject CreateShard(string name, Transform parent)
        {
            GameObject shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shard.name = name;
            shard.transform.SetParent(parent, false);

            Object.DestroyImmediate(shard.GetComponent<Collider>());

            var renderer = shard.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = GeneratedMaterials.Load(GeneratedMaterials.Tracer);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return shard;
        }

        /// <summary>
        /// Reports whether a weapon throws its rounds along an arc.
        /// </summary>
        /// <param name="kind">Weapon to check.</param>
        /// <returns><c>true</c> for a weapon whose rounds fall.</returns>
        private static bool Lobs(WeaponKind kind) => kind == WeaponKind.Grenade;

        /// <summary>
        /// Creates an unlit-looking sphere with no collider on it.
        /// </summary>
        /// <param name="name">Object name.</param>
        /// <param name="parent">Object to hang it off.</param>
        /// <param name="material">Generated material asset name to wear.</param>
        /// <returns>The sphere.</returns>
        /// <remarks>
        /// The collider is removed rather than never added because a primitive is the only
        /// reliable source of a correctly set-up URP material and mesh - see
        /// <see cref="GeneratedMaterials.EnsureAssets"/>. Shadows are off: neither a tracer
        /// nor a flash should darken the ground it is lighting.
        /// </remarks>
        private static GameObject CreateSphere(string name, Transform parent, string material)
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = name;
            sphere.transform.SetParent(parent, false);

            Object.DestroyImmediate(sphere.GetComponent<Collider>());

            var renderer = sphere.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = GeneratedMaterials.Load(material);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return sphere;
        }
    }
}
