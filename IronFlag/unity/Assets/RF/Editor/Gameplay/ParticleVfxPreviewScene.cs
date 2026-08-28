using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using IronFlag.Core;
using IronFlag.Editor.ArtPipeline;
using IronFlag.Levels;
using IronFlag.Vehicles;
using IronFlag.Vfx;

namespace IronFlag.Editor.Gameplay
{
    /// <summary>
    /// Builds a contact sheet of the four particle effects, each frozen at four points of
    /// its life, so what they look like can be reviewed from a still.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The particle counterpart of <see cref="CombatVfxPreviewScene"/> and built for the same
    /// reason: none of these effects holds still, so a single frame grabbed at random says
    /// nothing about the shape of any of them. It is a second scene rather than four more
    /// rows on the first because the two sheets answer different questions - that one is
    /// about two curves, this one is about four clouds - and sixteen cells is already as many
    /// as one image can carry.
    /// </para>
    /// <para>
    /// It poses everything through <c>PoseAt</c>, which for a particle system means
    /// <c>ParticleSystem.Simulate</c>: particles do not tick outside play mode, so an effect
    /// dropped into a generated scene is an empty emitter until something winds it forward.
    /// Everything is spawned off the real prefabs bound to a real tank, so nothing here
    /// reimplements an effect.
    /// </para>
    /// <para>
    /// The dust row is the odd one out, and deliberately: instead of one trail at four
    /// moments it is four trails on four different <em>surfaces</em>, each tank standing on a
    /// slab of the ground it is kicking up. That is the row's actual claim - that the colour
    /// and the amount both fall out of the surface table - and it is a claim about four
    /// things beside each other rather than about one thing over time.
    /// </para>
    /// </remarks>
    public static class ParticleVfxPreviewScene
    {
        /// <summary>Where the generated scene is saved.</summary>
        public const string ScenePath = "Assets/RF/Scenes/ParticleVfxPreview.unity";

        /// <summary>Command-line switch naming the file <see cref="RenderToFile"/> writes.</summary>
        private const string OutputArgument = "-particleOutput";

        /// <summary>File <see cref="RenderToFile"/> writes when the switch is absent.</summary>
        private const string DefaultOutputFile = "particle-vfx.png";

        /// <summary>Metres between one frame of a strip and the next.</summary>
        private const float ColumnPitch = 12.0f;

        /// <summary>Metres between one strip and the next.</summary>
        private const float RowPitch = 11.0f;

        /// <summary>How many frames each strip has.</summary>
        private const int Frames = 4;

        /// <summary>How many strips the sheet has.</summary>
        /// <remarks>
        /// Named rather than written into <see cref="CellAt"/> twice, which is what it was
        /// until a fifth strip arrived and the sheet came out photographed off-centre.
        /// </remarks>
        private const int Rows = 5;

        /// <summary>Seconds into its life each frame of the damage plume is drawn.</summary>
        private static readonly float[] PlumeFrames = { 0.35f, 0.9f, 1.7f, 2.6f };

        /// <summary>Seconds into its life each frame of the wreck column is drawn.</summary>
        private static readonly float[] ColumnFrames = { 0.25f, 0.8f, 1.6f, 2.3f };

        /// <summary>Seconds into its life each frame of the water splash is drawn.</summary>
        private static readonly float[] SplashFrames = { 0.06f, 0.18f, 0.35f, 0.55f };

        /// <summary>The four grounds the dust row is driven over, softest first.</summary>
        /// <remarks>
        /// Read left to right it is the whole rule: sand throws a cloud, open country puffs,
        /// a road barely marks, and water gives nothing at all. The last is not padding - it
        /// is the one surface that has to produce <em>no</em> dust, and a row without it
        /// cannot show that.
        /// </remarks>
        private static readonly SurfaceKind[] DustGrounds =
        {
            SurfaceKind.Sand, SurfaceKind.Grass, SurfaceKind.Asphalt, SurfaceKind.ShallowWater,
        };

        /// <summary>
        /// Rebuilds the preview scene from the prefabs currently on disk and saves it.
        /// </summary>
        [MenuItem("Tools/IronFlag/Build Particle VFX Preview Scene", false, 155)]
        public static void BuildAndSave()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Build(4.0f / 3.0f);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ScenePath)));
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.Refresh();
            Debug.Log($"IronFlag: particle VFX preview scene saved to {ScenePath}");
        }

        /// <summary>
        /// Builds the preview scene and writes a render of it to a PNG file.
        /// </summary>
        /// <remarks>
        /// Intended for <c>-executeMethod</c>. Pass <c>-particleOutput &lt;path&gt;</c> to
        /// choose the file. Run Unity in <c>-batchmode</c> but <em>not</em>
        /// <c>-nographics</c>, since this needs a real graphics device.
        /// </remarks>
        public static void RenderToFile()
        {
            const int width = 2400;
            const int height = 1800;

            Camera camera = Build((float)width / height);
            CameraCapture.RenderToPng(
                camera,
                width,
                height,
                CameraCapture.OutputPathFromCommandLine(OutputArgument, DefaultOutputFile));
        }

        /// <summary>
        /// Creates the scene contents: ground, lighting, four strips and a camera.
        /// </summary>
        /// <param name="aspect">Width divided by height of the intended output.</param>
        /// <returns>The scene camera, already framed on the strips.</returns>
        public static Camera Build(float aspect)
        {
            GeneratedMaterials.EnsureAssets();
            VfxPrefabBuilder.EnsureAssets();

            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            SceneLighting.Apply(LightingMood.Studio);
            CreateGround();

            var contents = new Bounds(Vector3.zero, Vector3.one);
            bool started = false;

            LayOutPlumes(ref contents, ref started);
            LayOutColumns(ref contents, ref started);
            LayOutSplashes(ref contents, ref started);
            LayOutDust(ref contents, ref started);
            LayOutMarks(ref contents, ref started);

            return FrameCamera(started ? contents : new Bounds(Vector3.zero, Vector3.one * 20.0f), aspect);
        }

        /// <summary>
        /// Lays out the damage plume: a tank smoking a little more in each frame.
        /// </summary>
        private static void LayOutPlumes(ref Bounds contents, ref bool started)
        {
            for (int frame = 0; frame < Frames; frame++)
            {
                GameObject tank = PlaceTank($"Plume{frame}", frame, 0);
                var smoke = tank.GetComponent<DamageSmoke>();
                if (smoke == null || smoke.Plume == null)
                {
                    Debug.LogWarning("IronFlag: the tank prefab has no damage plume on it.");
                    continue;
                }

                smoke.Plume.Simulate(PlumeFrames[frame], false, true);
                Swallow(ref contents, ref started, tank);
            }
        }

        /// <summary>
        /// Lays out the wreck column, with a tank beside it for scale.
        /// </summary>
        /// <remarks>
        /// The tank is there because a column of smoke has no size of its own in a
        /// photograph - it is the one effect in the game big enough that "is it too big" is a
        /// real question, and the answer only means anything next to something you know.
        /// </remarks>
        private static void LayOutColumns(ref Bounds contents, ref bool started)
        {
            ParticleBurst prefab = VfxPrefabBuilder.LoadSmokeColumn();
            if (prefab == null)
            {
                Debug.LogWarning("IronFlag: there is no smoke column prefab to draw.");
                return;
            }

            for (int frame = 0; frame < Frames; frame++)
            {
                GameObject tank = PlaceTank($"ColumnScale{frame}", frame, 1);
                Swallow(ref contents, ref started, tank);

                ParticleBurst burst = ParticleBurst.Spawn(
                    prefab, tank.transform.position + (Vector3.up * 1.2f), 3.5f);
                if (burst != null)
                {
                    burst.name = $"Column{frame}";
                    burst.PoseAt(ColumnFrames[frame]);
                    Swallow(ref contents, ref started, burst.gameObject);
                }
            }
        }

        /// <summary>
        /// Lays out the water splash, standing on a slab of sea.
        /// </summary>
        private static void LayOutSplashes(ref Bounds contents, ref bool started)
        {
            ParticleBurst prefab = VfxPrefabBuilder.LoadSplash();
            if (prefab == null)
            {
                Debug.LogWarning("IronFlag: there is no splash prefab to draw.");
                return;
            }

            for (int frame = 0; frame < Frames; frame++)
            {
                Vector3 at = CellAt(frame, 2);
                // The shelf rather than the open sea, which is the darkest thing in the
                // game and comes out invisible against this sheet's backdrop. Both drown you
                // and both splash; this one can be seen underneath the spray.
                Swallow(ref contents, ref started, Slab($"Sea{frame}", at, SurfaceKind.ShallowWater));

                ParticleBurst burst = ParticleBurst.Spawn(prefab, at, 3.0f);
                if (burst != null)
                {
                    burst.name = $"Splash{frame}";
                    burst.PoseAt(SplashFrames[frame]);
                    Swallow(ref contents, ref started, burst.gameObject);
                }
            }
        }

        /// <summary>
        /// Lays out the dust row: one tank per surface, each standing on that surface.
        /// </summary>
        private static void LayOutDust(ref Bounds contents, ref bool started)
        {
            for (int frame = 0; frame < DustGrounds.Length; frame++)
            {
                SurfaceKind ground = DustGrounds[frame];
                Vector3 at = CellAt(frame, 3);
                Swallow(ref contents, ref started, Slab($"Ground{ground}", at, ground));

                GameObject tank = PlaceTank($"Dust{ground}", frame, 3);
                var trail = tank.GetComponent<DustTrail>();
                if (trail == null)
                {
                    Debug.LogWarning("IronFlag: the tank prefab has no dust trail on it.");
                    continue;
                }

                // Flat out on each surface, a second and a half in - long enough for the
                // trail to have reached the length it holds while driving.
                trail.PoseAt(SurfaceTuning.For(ground), 100.0f, 1.5f);
                Swallow(ref contents, ref started, tank);
            }
        }

        /// <summary>
        /// Lays out the marks row: two scorches and two sets of wheel tracks, each on a
        /// different surface.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The row exists to make one claim checkable: <c>RF_Mark.shader</c> multiplies
        /// rather than blends, so the <em>same</em> stain is the right colour on every
        /// ground. The first three cells are one burn on sand, on grass and on a road, and
        /// they should read as one mark on three surfaces rather than as three marks.
        /// </para>
        /// <para>
        /// The fourth is the shader's other half: the same material a wheel track wears,
        /// laid as a strip. It is <strong>not</strong> a wheel track - it is that material's
        /// ribbon falloff on a piece of ground, and it is here because the real thing cannot
        /// be photographed. A <see cref="TrailRenderer"/> builds its ribbon during the game
        /// loop and a headless one-shot render never runs one, so a posed track has all its
        /// points, bakes to a correctly placed flat mesh, and draws nothing. See
        /// <c>GROUND_WATER_NOTES.md</c>.
        /// </para>
        /// <para>
        /// Posed rather than made: a mark is built straight from
        /// <see cref="GroundMark.Configure"/>, never from <c>Spawn</c>, which refuses outside
        /// play mode on purpose. That refusal is the doors pass's frozen-debris lesson, and
        /// posing round it is the same move the dust row already makes.
        /// </para>
        /// </remarks>
        private static void LayOutMarks(ref Bounds contents, ref bool started)
        {
            // How high the marks sit: on top of the cell's slab rather than on the ground
            // plane the slab is laid over. Slab returns a cube whose top face is four
            // centimetres up - see the lip in Slab.
            const float onTheSlab = 0.04f;
            const float burnAcross = 5.0f;
            const float ribbonLong = 9.0f;
            const float ribbonWide = 0.7f;

            SurfaceKind[] burns = { SurfaceKind.Sand, SurfaceKind.Grass, SurfaceKind.Asphalt };

            GroundMark scorch = VfxPrefabBuilder.LoadScorch();
            if (scorch == null)
            {
                Debug.LogWarning("IronFlag: there is no scorch prefab to photograph.");
                return;
            }

            for (int frame = 0; frame < burns.Length; frame++)
            {
                SurfaceKind ground = burns[frame];
                Vector3 at = CellAt(frame, 4);
                Swallow(ref contents, ref started, Slab($"Burn{ground}", at, ground));

                var mark = (GroundMark)PrefabUtility.InstantiatePrefab(scorch);
                mark.name = $"Scorch{ground}";
                mark.transform.position = at + (Vector3.up * (onTheSlab + GroundMark.Height));
                mark.Configure(burnAcross, 0.0f);
                Swallow(ref contents, ref started, mark.gameObject);
            }

            Vector3 last = CellAt(burns.Length, 4);
            Swallow(ref contents, ref started, Slab("RibbonGrass", last, SurfaceKind.Grass));

            var strip = (GroundMark)PrefabUtility.InstantiatePrefab(scorch);
            strip.name = "TrackRibbon";
            strip.transform.position = last + (Vector3.up * (onTheSlab + TyreTracks.Height));
            strip.Configure(1.0f, 0.0f);
            strip.transform.localScale = new Vector3(ribbonLong, 1.0f, ribbonWide);
            strip.GetComponent<Renderer>().sharedMaterial =
                GeneratedMaterials.Load(GeneratedMaterials.Track);
            Swallow(ref contents, ref started, strip.gameObject);
        }

        /// <summary>
        /// Returns where one cell of the sheet is.
        /// </summary>
        /// <param name="column">Which frame of the strip, zero-based and left to right.</param>
        /// <param name="row">Which strip, zero-based and top to bottom on the sheet.</param>
        /// <returns>The middle of that cell, on the ground.</returns>
        private static Vector3 CellAt(int column, int row)
            => new Vector3(
                (column * ColumnPitch) - ((Frames - 1) * ColumnPitch * 0.5f),
                0.0f,
                ((Rows - 1 - row) * RowPitch) - ((Rows - 1) * RowPitch * 0.5f));

        /// <summary>
        /// Drops one tank into a cell, side-on to the camera.
        /// </summary>
        /// <param name="name">Object name, so a frame can be found in the hierarchy.</param>
        /// <param name="column">Which frame of the strip.</param>
        /// <param name="row">Which strip.</param>
        /// <returns>The instantiated tank.</returns>
        private static GameObject PlaceTank(string name, int column, int row)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                VehiclePrefabBuilder.PrefabPathFor(VehicleKind.Tank));
            var tank = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            tank.name = name;
            tank.transform.SetPositionAndRotation(
                CellAt(column, row), Quaternion.Euler(0.0f, 90.0f, 0.0f));

            return tank;
        }

        /// <summary>
        /// Lays a square of one surface's own material down in a cell.
        /// </summary>
        /// <param name="name">Object name.</param>
        /// <param name="at">Middle of the cell.</param>
        /// <param name="kind">Surface to paint it.</param>
        /// <remarks>
        /// Barely off the ground, so it reads as that cell's patch of map rather than as a
        /// plinth. It is what makes the dust row's claim checkable: the puff and the slab
        /// under it should obviously be the same colour, one lighter.
        /// <para>
        /// The four centimetres matter. Laid flush with the ground plane the two surfaces are
        /// coplanar, and which one the depth buffer picks depends on how far off the camera
        /// is - so the far rows drew their slabs and the near ones drew the ground straight
        /// through them, which reads as a missing slab rather than as z-fighting.
        /// </para>
        /// </remarks>
        /// <returns>The slab, so the frame can be sized to include it.</returns>
        private static GameObject Slab(string name, Vector3 at, SurfaceKind kind)
        {
            const float lip = 0.04f;

            GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = name;
            slab.transform.position = at + (Vector3.down * (0.1f - lip));
            slab.transform.localScale = new Vector3(ColumnPitch * 0.82f, 0.2f, RowPitch * 0.75f);

            slab.GetComponent<Renderer>().sharedMaterial =
                GeneratedMaterials.Load(GeneratedMaterials.SurfaceMaterial(kind));

            return slab;
        }

        /// <summary>Creates the ground plane everything stands on.</summary>
        private static void CreateGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(20.0f, 1.0f, 20.0f);

            ground.GetComponent<Renderer>().sharedMaterial =
                GeneratedMaterials.Load(GeneratedMaterials.Ground);
        }

        /// <summary>
        /// Grows a running bounds to take in everything one object draws.
        /// </summary>
        /// <param name="running">Bounds being accumulated.</param>
        /// <param name="started">Whether anything has been taken in yet.</param>
        /// <param name="what">Object whose renderers to swallow.</param>
        /// <remarks>
        /// A particle system's renderer bounds are the bounds of the particles alive right
        /// now, which is why everything is posed before it is measured: an emitter that has
        /// not been wound forward yet occupies a point, and framing off that crops the smoke.
        /// </remarks>
        private static void Swallow(ref Bounds running, ref bool started, GameObject what)
        {
            foreach (Renderer renderer in what.GetComponentsInChildren<Renderer>())
            {
                if (started)
                {
                    running.Encapsulate(renderer.bounds);
                }
                else
                {
                    running = renderer.bounds;
                    started = true;
                }
            }
        }

        /// <summary>
        /// Positions the scene camera so the whole sheet fills the frame.
        /// </summary>
        /// <param name="contents">World-space bounds of the four strips.</param>
        /// <param name="aspect">Width divided by height of the intended output.</param>
        /// <returns>The configured camera.</returns>
        /// <remarks>
        /// A steeper rake than the flash sheet's and a shallower one than the game's. Smoke
        /// is a vertical shape and wants a low angle; four rows laid out on the ground want a
        /// high one; forty-two degrees is where the two stop arguing.
        /// </remarks>
        private static Camera FrameCamera(Bounds contents, float aspect)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                camera = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
                camera.tag = "MainCamera";
            }

            camera.transform.rotation = Quaternion.Euler(42.0f, 0.0f, 0.0f);
            camera.nearClipPlane = 0.3f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.09f, 0.10f, 0.12f);

            CameraCapture.FrameOrthographic(camera, contents, aspect, 1.04f);

            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;

            return camera;
        }
    }
}
