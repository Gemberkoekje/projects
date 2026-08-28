using UnityEditor;
using UnityEngine;
using IronFlag.Editor.ArtPipeline;
using IronFlag.Vfx;

namespace IronFlag.Editor.Gameplay
{
    /// <summary>
    /// Builds the particle effects: the two that stand on their own as prefabs, and the two
    /// that are bolted onto something else while it is being built.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This file is where the numbers live. <see cref="ParticleRig"/> knows how to turn a
    /// <see cref="ParticleRig.Look"/> into a Unity particle system and nothing else; every
    /// decision about what an effect actually looks like is a field of a
    /// <c>Look</c> below, so the whole visual design of the smoke, the dust and the spray is
    /// four objects on one screen that can be read against each other. That is the same
    /// trade <see cref="IronFlag.Levels.SurfaceTuning"/> and
    /// <see cref="IronFlag.Combat.WeaponTuning"/> make, and it is the answer to the standing
    /// objection that a particle system is an asset nobody can review in a diff.
    /// </para>
    /// <para>
    /// Two of the four are prefabs because they outlive what made them - a wreck's smoke
    /// column has to stay behind after the wreck has gone home, and a splash never had an
    /// owner in the first place. The other two are rigs added to a vehicle or a structure as
    /// it is built, because a plume that marks <em>this</em> hull as burning and a trail that
    /// comes off <em>these</em> wheels have nowhere else to live.
    /// </para>
    /// </remarks>
    public static class VfxPrefabBuilder
    {
        /// <summary>Folder the generated effect prefabs are written to.</summary>
        /// <remarks>
        /// Its own folder rather than <c>Prefabs/Combat</c>, so that the split the whole
        /// design stance turns on - closed-form and deterministic on one side, particles and
        /// cosmetic randomness on the other - is visible in the project window rather than
        /// only in the prose. A flash is combat; smoke is scenery that happens to be caused
        /// by combat.
        /// </remarks>
        public const string PrefabFolder = "Assets/RF/Prefabs/Vfx";

        /// <summary>Asset name of the smoke column a wreck throws up.</summary>
        public const string SmokeColumnAssetName = "RF_SmokeColumn";

        /// <summary>Asset name of the spray a shell puts up out of the water.</summary>
        public const string SplashAssetName = "RF_Splash";

        /// <summary>Asset name of the scorch a blast leaves on the ground.</summary>
        /// <remarks>
        /// A prefab like the smoke column and for the same reason: it outlives what made it.
        /// An explosion is gone in half a second and its mark is still there twenty seconds
        /// later, so the mark cannot be a child of the explosion.
        /// </remarks>
        public const string ScorchAssetName = "RF_Scorch";

        /// <summary>Name of the node a damaged thing's plume hangs off.</summary>
        public const string PlumeNodeName = "DamageSmoke";

        /// <summary>Name of the node a ground vehicle's dust trail hangs off.</summary>
        public const string DustNodeName = "DustTrail";

        /// <summary>Names of the nodes a ground vehicle's two wheel tracks hang off.</summary>
        public const string LeftTrackNodeName = "TrackLeft";
        public const string RightTrackNodeName = "TrackRight";

        /// <summary>Seconds of driving a wheel track stays on the ground for.</summary>
        /// <remarks>
        /// Long enough to still be behind you when you turn round and come back through your
        /// own approach, and short enough that ten minutes of a match does not end up drawn
        /// on the map. At the jeep's top speed it is a little over a hundred metres of
        /// track, which is about half the width of the island.
        /// </remarks>
        public const float TrackSeconds = 9.0f;

        /// <summary>Metres a wheel travels before its track puts down another point.</summary>
        /// <remarks>
        /// A trail renderer records a point whenever its transform has moved this far, so
        /// this is the resolution of a corner. Small enough that a jeep's turning circle is a
        /// curve rather than a polygon, large enough that a vehicle standing still and
        /// jostling against a wall does not fill the buffer.
        /// </remarks>
        public const float TrackStep = 0.35f;

        /// <summary>Metres across the standalone prefabs' own numbers are written for.</summary>
        /// <remarks>
        /// Both are authored at three metres and scaled at the point of use, exactly as one
        /// muzzle flash prefab serves five guns. Three is about a tank: big enough that the
        /// numbers below are readable as metres, and the size most of what dies on this map
        /// actually is.
        /// </remarks>
        public const float AuthoredSize = 3.0f;

        /// <summary>The charcoal everything charred in this game already wears.</summary>
        /// <remarks>
        /// Two steps up from <c>GeneratedMaterials</c>'s debris colour rather than equal to
        /// it. Smoke coming off a dark hull has to be lighter than the hull or there is
        /// nothing to see; debris has the opposite problem, because it lands on open ground.
        /// </remarks>
        private static readonly Color Soot = new Color(0.24f, 0.23f, 0.22f);

        /// <summary>The pale blue-white of water thrown into the air.</summary>
        /// <remarks>
        /// Nowhere near the sea's own colour, which is the darkest thing on the map. Spray is
        /// water full of air, and it reads as water precisely because it is the one bright
        /// thing that ever comes out of somewhere that dark.
        /// </remarks>
        private static readonly Color Spray = new Color(0.82f, 0.90f, 0.96f);

        /// <summary>
        /// Rebuilds both standalone effect prefabs.
        /// </summary>
        [MenuItem("Tools/IronFlag/Build VFX Prefabs", false, 154)]
        public static void BuildAll()
        {
            GeneratedMaterials.EnsureAssets();
            GeneratedMaterials.EnsureAssetFolder(PrefabFolder);

            string[] built =
            {
                BuildSmokeColumn().name, BuildSplash().name, BuildScorch().name,
            };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"IronFlag: built {built.Length} VFX prefabs - {string.Join(", ", built)}.");
        }

        /// <summary>
        /// Builds any effect prefab that is missing, leaving any that already exist alone.
        /// </summary>
        /// <remarks>
        /// Called by the vehicle and destructible builders, which cannot bind a smoke column
        /// that does not exist yet.
        /// </remarks>
        public static void EnsureAssets()
        {
            if (LoadSmokeColumn() == null || LoadSplash() == null || LoadScorch() == null)
            {
                BuildAll();
            }
        }

        /// <summary>
        /// Returns where one generated effect prefab is written to.
        /// </summary>
        /// <param name="assetName">Asset name, without the extension.</param>
        /// <returns>The project-relative path of the prefab.</returns>
        public static string PrefabPathFor(string assetName) => $"{PrefabFolder}/{assetName}.prefab";

        /// <summary>
        /// Loads the smoke column prefab.
        /// </summary>
        /// <returns>The burst, or <c>null</c> when it has not been built yet.</returns>
        public static ParticleBurst LoadSmokeColumn() => LoadBurst(SmokeColumnAssetName);

        /// <summary>
        /// Loads the water splash prefab.
        /// </summary>
        /// <returns>The burst, or <c>null</c> when it has not been built yet.</returns>
        public static ParticleBurst LoadSplash() => LoadBurst(SplashAssetName);

        /// <summary>
        /// Loads the scorch prefab.
        /// </summary>
        /// <returns>The mark, or <c>null</c> when it has not been built yet.</returns>
        public static GroundMark LoadScorch()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabPathFor(ScorchAssetName));
            return prefab == null ? null : prefab.GetComponent<GroundMark>();
        }

        /// <summary>
        /// Builds the scorch a blast leaves on the ground.
        /// </summary>
        /// <returns>The saved prefab asset.</returns>
        /// <remarks>
        /// <para>
        /// The only piece of geometry in the project generated here rather than in Blender,
        /// and it is a square metre of nothing: four corners, two triangles, lying flat and
        /// facing up. Putting that through the art pipeline would be a .glb with two
        /// triangles in it and a step in the build for something completely described by the
        /// eight numbers below.
        /// </para>
        /// <para>
        /// Not <c>PrimitiveType.Quad</c>, which is nearly this and wrong in two ways: it
        /// stands up in the x-y plane rather than lying in x-z, and it has no vertex colours,
        /// which <c>RF_Mark.shader</c> reads as how much of the mark is left. A mesh with no
        /// colour channel hands the shader whatever happens to be in that register.
        /// </para>
        /// </remarks>
        public static GameObject BuildScorch()
        {
            var root = new GameObject(ScorchAssetName);
            try
            {
                var mesh = new Mesh { name = $"{ScorchAssetName}Quad" };
                mesh.SetVertices(new[]
                {
                    new Vector3(-0.5f, 0.0f, -0.5f),
                    new Vector3(-0.5f, 0.0f, 0.5f),
                    new Vector3(0.5f, 0.0f, 0.5f),
                    new Vector3(0.5f, 0.0f, -0.5f),
                });
                mesh.SetUVs(0, new[]
                {
                    new Vector2(0.0f, 0.0f),
                    new Vector2(0.0f, 1.0f),
                    new Vector2(1.0f, 1.0f),
                    new Vector2(1.0f, 0.0f),
                });
                mesh.SetColors(new[] { Color.white, Color.white, Color.white, Color.white });

                // Clockwise seen from above is the way up in a left-handed world, which is
                // the same winding SurfaceMesh uses and for the same reason.
                mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                root.AddComponent<MeshFilter>().sharedMesh = Save(mesh);
                MeshRenderer face = root.AddComponent<MeshRenderer>();
                face.sharedMaterial = GeneratedMaterials.Load(GeneratedMaterials.Scorch);
                face.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                face.receiveShadows = false;

                root.AddComponent<GroundMark>().Wire(face, 1.0f);

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPathFor(ScorchAssetName));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Writes the scorch's quad out as an asset of its own, keeping the one that is
        /// already there.
        /// </summary>
        /// <param name="made">The freshly built mesh.</param>
        /// <returns>The mesh asset, which is what the prefab may refer to.</returns>
        /// <remarks>
        /// <para>
        /// A prefab cannot carry a mesh that is not an asset. Saving the prefab first and
        /// adding the mesh to it afterwards looks like it would do and does not: the
        /// <c>MeshFilter</c> is serialised before the mesh exists to be pointed at, so the
        /// prefab on disk gets a null mesh and a spare mesh hanging off the side of it. That
        /// is what the first build did, and it cost a test rather than an afternoon only
        /// because the test asked whether the scorch had any geometry.
        /// </para>
        /// <para>
        /// Copied into the existing asset rather than replacing it, for the reason
        /// <c>GeneratedMaterials.EnsureAssets</c> updates materials in place:
        /// <c>CreateAsset</c> over an existing path deletes the file and its meta, which
        /// hands out a fresh GUID and unbinds everything that referred to the old one.
        /// </para>
        /// </remarks>
        private static Mesh Save(Mesh made)
        {
            string path = $"{PrefabFolder}/{ScorchAssetName}Quad.asset";
            Mesh already = AssetDatabase.LoadAssetAtPath<Mesh>(path);

            if (already == null)
            {
                AssetDatabase.CreateAsset(made, path);
                return made;
            }

            EditorUtility.CopySerialized(made, already);
            EditorUtility.SetDirty(already);
            Object.DestroyImmediate(made);
            return already;
        }

        /// <summary>
        /// Gives a vehicle two wheel tracks to leave behind it.
        /// </summary>
        /// <param name="root">Prefab root the component goes on.</param>
        /// <param name="halfWidth">Metres from the middle of the vehicle to its wheels.</param>
        /// <param name="width">How wide one track is, in metres.</param>
        /// <returns>The component.</returns>
        /// <remarks>
        /// A rig rather than a prefab, like the dust trail and for the same reason: a track
        /// that comes off <em>these</em> wheels has nowhere else to live. The two trails are
        /// children rather than components on the root, because a trail renderer emits at its
        /// own transform and a vehicle has two sets of wheels a metre or so apart.
        /// </remarks>
        public static TyreTracks AddTyreTracks(GameObject root, float halfWidth, float width)
        {
            TrailRenderer left = Track(LeftTrackNodeName, root, -halfWidth, width);
            TrailRenderer right = Track(RightTrackNodeName, root, halfWidth, width);

            TyreTracks tracks = root.AddComponent<TyreTracks>();
            tracks.Configure(left, right, halfWidth);
            return tracks;
        }

        /// <summary>
        /// Builds one wheel track.
        /// </summary>
        /// <param name="name">Object name.</param>
        /// <param name="root">Object to hang it off.</param>
        /// <param name="across">Where it sits across the vehicle, in metres.</param>
        /// <param name="width">How wide the track is, in metres.</param>
        /// <returns>The trail renderer.</returns>
        /// <remarks>
        /// <c>autodestruct</c> is deliberately off: a trail that removes its own GameObject
        /// when it empties is a trail that takes a wheel off the vehicle the first time it
        /// stops moving.
        /// </remarks>
        private static TrailRenderer Track(string name, GameObject root, float across, float width)
        {
            GameObject node = Node(name, root);
            node.transform.localPosition = new Vector3(across, TyreTracks.Height, 0.0f);

            TrailRenderer trail = node.AddComponent<TrailRenderer>();
            trail.sharedMaterial = GeneratedMaterials.Load(GeneratedMaterials.Track);
            trail.time = TrackSeconds;
            trail.minVertexDistance = TrackStep;
            trail.widthMultiplier = width;
            trail.numCapVertices = 0;
            trail.numCornerVertices = 2;
            trail.alignment = LineAlignment.TransformZ;
            trail.textureMode = LineTextureMode.Stretch;
            trail.autodestruct = false;
            trail.emitting = false;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.startColor = new Color(1.0f, 1.0f, 1.0f, TyreTracks.Darkest);
            trail.endColor = new Color(1.0f, 1.0f, 1.0f, 0.0f);
            return trail;
        }

        /// <summary>
        /// Builds the column of smoke a wreck throws up and leaves hanging.
        /// </summary>
        /// <returns>The saved prefab asset.</returns>
        /// <remarks>
        /// Two systems, because a column is two things. The <c>Column</c> burst is the
        /// mushroom that goes up in the first instant and hangs for three seconds; the
        /// <c>Feed</c> is a second and a bit of smaller smoke behind it, which is what stops
        /// the whole thing being one puff that fades all at once. Neither loops - a wreck
        /// smokes for a few seconds and then it is a wreck, not a chimney.
        /// </remarks>
        public static GameObject BuildSmokeColumn()
        {
            var root = new GameObject(SmokeColumnAssetName);
            try
            {
                ParticleRig.Create(Node("Column", root), new ParticleRig.Look
                {
                    Tint = Soot,
                    Opacity = 0.7f,
                    Lifetime = 3.0f,
                    StartSize = 0.85f,
                    Growth = 3.0f,
                    Burst = 16,
                    Speed = 2.0f,
                    Fall = -0.03f,
                    Radius = 0.5f,
                    ConeAngle = 22.0f,
                    MaxParticles = 24,
                });

                ParticleRig.Create(Node("Feed", root), new ParticleRig.Look
                {
                    Tint = Soot,
                    Opacity = 0.55f,
                    Lifetime = 2.2f,
                    StartSize = 0.5f,
                    Growth = 2.6f,
                    Rate = 11.0f,
                    Duration = 1.2f,
                    Speed = 1.6f,
                    Fall = -0.03f,
                    Radius = 0.35f,
                    ConeAngle = 16.0f,
                    MaxParticles = 20,
                });

                // Three seconds of particle life plus the second the feed keeps emitting for,
                // and a beat so the last puff fades out rather than being switched off.
                root.AddComponent<ParticleBurst>().Configure(4.4f, AuthoredSize);

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPathFor(SmokeColumnAssetName));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Builds the spray a shell puts up when it goes off in the water.
        /// </summary>
        /// <returns>The saved prefab asset.</returns>
        /// <remarks>
        /// A crown and a ring, which is the plan's "particle system plus a hand-coded ring"
        /// with the hand-coding taken out. The ring is a circle emitter firing along its own
        /// plane, so it is the same twenty lines as the crown with one field changed - and
        /// unlike an expanding disc it needs no transparent shader of its own, no
        /// per-instance material and no second fade curve to keep in step with the first.
        /// The crown falls back down at over one gravity, because water does.
        /// </remarks>
        public static GameObject BuildSplash()
        {
            var root = new GameObject(SplashAssetName);
            try
            {
                ParticleRig.Create(Node("Crown", root), new ParticleRig.Look
                {
                    Tint = Spray,
                    Opacity = 0.9f,
                    Lifetime = 0.95f,
                    StartSize = 0.3f,
                    Growth = 1.4f,
                    Burst = 14,
                    Speed = 5.2f,
                    Fall = 1.6f,
                    Radius = 0.35f,
                    ConeAngle = 26.0f,
                    MaxParticles = 20,
                });

                ParticleRig.Create(Node("Ring", root), new ParticleRig.Look
                {
                    Tint = Spray,
                    Opacity = 0.7f,
                    Lifetime = 0.75f,
                    StartSize = 0.22f,
                    Growth = 2.4f,
                    Burst = 18,
                    Speed = 4.2f,
                    Fall = 0.3f,
                    Radius = 0.5f,
                    Flat = true,
                    MaxParticles = 24,
                });

                root.AddComponent<ParticleBurst>().Configure(1.3f, AuthoredSize);

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPathFor(SplashAssetName));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Bolts a damage plume and a death column onto something that can be hurt.
        /// </summary>
        /// <param name="root">Prefab root, which must carry an <c>IDamageable</c>.</param>
        /// <param name="at">Where the smoke comes from, in the root's own space.</param>
        /// <param name="size">Roughly how big the thing is, in metres across.</param>
        /// <returns>The component that drives both.</returns>
        /// <remarks>
        /// The plume is sized by scaling its node rather than by writing different numbers
        /// per vehicle: every system built here scales with its own transform, so one set of
        /// numbers covers a jeep and a flag tower - see <see cref="ParticleBurst.Resize"/>,
        /// which does the same thing for the standalone prefabs.
        /// </remarks>
        public static DamageSmoke AddDamageSmoke(GameObject root, Vector3 at, float size)
        {
            EnsureAssets();

            GameObject node = Node(PlumeNodeName, root);
            node.transform.localPosition = at;
            node.transform.localScale = Vector3.one * (Mathf.Max(0.3f, size) / AuthoredSize);

            ParticleSystem plume = ParticleRig.Create(node, new ParticleRig.Look
            {
                Tint = Soot,
                Opacity = 0.55f,
                Lifetime = 1.8f,
                StartSize = 0.45f,
                Growth = 2.4f,
                Rate = 7.0f,
                Loops = true,
                PlayOnAwake = false,
                Speed = 1.1f,
                Fall = -0.04f,
                Radius = 0.35f,
                ConeAngle = 18.0f,
                MaxParticles = 24,
            });

            DamageSmoke smoke = root.AddComponent<DamageSmoke>();
            smoke.Configure(plume, LoadSmokeColumn(), Mathf.Max(1.0f, size));
            return smoke;
        }

        /// <summary>
        /// Bolts a dust trail onto a ground vehicle.
        /// </summary>
        /// <param name="root">Prefab root, which must carry a <c>GroundVehicle</c>.</param>
        /// <param name="at">Where the dust comes off, in the root's own space.</param>
        /// <param name="width">Roughly how wide the vehicle is, in metres.</param>
        /// <returns>The component that drives it.</returns>
        /// <remarks>
        /// Emitted with a wide, low cone and almost no gravity, so it spreads sideways and
        /// hangs rather than fountaining. It is left playing at a rate of nothing:
        /// <see cref="DustTrail"/> writes the real rate every frame, and a system that had to
        /// be started and stopped as well would have two things to keep in step where one
        /// number will do.
        /// </remarks>
        public static DustTrail AddDustTrail(GameObject root, Vector3 at, float width)
        {
            GameObject node = Node(DustNodeName, root);
            node.transform.localPosition = at;
            node.transform.localScale = Vector3.one * Mathf.Clamp(width / 2.4f, 0.6f, 1.8f);

            ParticleSystem trail = ParticleRig.Create(node, new ParticleRig.Look
            {
                // Grey, and immediately overwritten: the real colour is whatever the vehicle
                // is standing on, written by DustTrail the first time it reads the ground.
                Tint = Color.grey,
                Opacity = 0.5f,
                Lifetime = 0.85f,
                StartSize = 0.32f,
                Growth = 2.6f,
                Rate = 0.0f,
                Loops = true,
                Speed = 0.9f,
                Fall = -0.05f,
                Radius = 0.5f,
                ConeAngle = 55.0f,
                MaxParticles = 40,
            });

            DustTrail dust = root.AddComponent<DustTrail>();
            dust.Configure(trail, 30.0f);
            return dust;
        }

        /// <summary>
        /// Loads one generated burst prefab.
        /// </summary>
        /// <param name="assetName">Asset name, without the extension.</param>
        /// <returns>The burst, or <c>null</c> when it has not been built yet.</returns>
        private static ParticleBurst LoadBurst(string assetName)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPathFor(assetName));
            return prefab == null ? null : prefab.GetComponent<ParticleBurst>();
        }

        /// <summary>
        /// Creates an empty child to hang one particle system off.
        /// </summary>
        /// <param name="name">Object name.</param>
        /// <param name="parent">Object to hang it off.</param>
        /// <returns>The node.</returns>
        private static GameObject Node(string name, GameObject parent)
        {
            var node = new GameObject(name);
            node.transform.SetParent(parent.transform, false);
            return node;
        }
    }
}
