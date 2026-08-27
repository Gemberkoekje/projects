using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Editor.ArtPipeline;
using IronFlag.Vehicles;

namespace IronFlag.Editor.Gameplay
{
    /// <summary>
    /// Builds a scene that lays the combat effects out as filmstrips - the same flash and
    /// the same spark burst, frozen at four points of its life side by side - so what they
    /// look like can be reviewed from a still instead of from a video.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other generated preview in this project photographs something that holds still.
    /// These two do not: a muzzle flash is over in 65 milliseconds and a spark burst in 220,
    /// which is short enough that a single frame grabbed at random says nothing about the
    /// shape of either. Laying the frames out in a row is the only way a still can show a
    /// curve, and the curve is the whole design - see <see cref="MuzzleFlash.Flare"/>
    /// against <see cref="Explosion.Scale"/>.
    /// </para>
    /// <para>
    /// It poses them through <see cref="MuzzleFlash.PoseAt"/> and
    /// <see cref="ImpactSparks.PoseAt"/> - the same methods <c>Update</c> calls, spawned
    /// through the same <c>Spawn</c> methods the game fires them with, off the same prefabs
    /// bound to the same tank. Nothing here reimplements an effect, so a picture that looks
    /// right is evidence about the game rather than about this file.
    /// </para>
    /// <para>
    /// Unlike <see cref="IronFlag.Editor.ArtPipeline.ArtPreviewScene"/>, this one runs
    /// post-processing. That sheet is a contact sheet and a tone curve would move the colours
    /// somebody is measuring; these are emissive effects whose whole job is to bloom, and
    /// judging one with the bloom off is judging something the player never sees.
    /// </para>
    /// </remarks>
    public static class CombatVfxPreviewScene
    {
        /// <summary>Where the generated scene is saved.</summary>
        public const string ScenePath = "Assets/RF/Scenes/CombatVfxPreview.unity";

        /// <summary>Command-line switch naming the file <see cref="RenderToFile"/> writes.</summary>
        private const string OutputArgument = "-vfxOutput";

        /// <summary>File <see cref="RenderToFile"/> writes when the switch is absent.</summary>
        private const string DefaultOutputFile = "combat-vfx.png";

        /// <summary>Metres between one frame of a filmstrip and the next.</summary>
        private const float ColumnPitch = 11.0f;

        /// <summary>Metres from the middle of the sheet to the middle of each strip.</summary>
        private const float RowOffset = 4.5f;

        /// <summary>
        /// How far through its life each frame of the muzzle flash strip is drawn.
        /// </summary>
        /// <remarks>
        /// Not evenly spaced, and not starting at zero. The flash spends most of itself in
        /// its first third, so four evenly spaced samples would be one bright frame and
        /// three dark ones; these are picked to show the fall rather than to sample it.
        /// </remarks>
        private static readonly float[] FlashFrames = { 0.0f, 0.2f, 0.45f, 0.75f };

        /// <summary>Seconds into its life each frame of the spark strip is drawn.</summary>
        private static readonly float[] SparkFrames = { 0.02f, 0.07f, 0.13f, 0.19f };

        /// <summary>
        /// Rebuilds the preview scene from the prefabs currently on disk and saves it.
        /// </summary>
        [MenuItem("Tools/IronFlag/Build Combat VFX Preview Scene", false, 153)]
        public static void BuildAndSave()
        {
            // Building replaces whatever scene is open, so give the usual save prompt first
            // rather than silently discarding someone's work.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Build(16.0f / 9.0f);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ScenePath)));
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.Refresh();
            Debug.Log($"IronFlag: combat VFX preview scene saved to {ScenePath}");
        }

        /// <summary>
        /// Builds the preview scene and writes a render of it to a PNG file.
        /// </summary>
        /// <remarks>
        /// Intended for <c>-executeMethod</c>. Pass <c>-vfxOutput &lt;path&gt;</c> to choose
        /// the file; without it the image lands next to the Unity project. Run Unity in
        /// <c>-batchmode</c> but <em>not</em> <c>-nographics</c>, since this needs a real
        /// graphics device.
        /// </remarks>
        public static void RenderToFile()
        {
            const int width = 2400;
            const int height = 1350;

            Camera camera = Build((float)width / height);
            CameraCapture.RenderToPng(
                camera,
                width,
                height,
                CameraCapture.OutputPathFromCommandLine(OutputArgument, DefaultOutputFile));
        }

        /// <summary>
        /// Creates the scene contents: ground, lighting, both filmstrips and a camera.
        /// </summary>
        /// <param name="aspect">Width divided by height of the intended output.</param>
        /// <returns>The scene camera, already framed on the strips.</returns>
        public static Camera Build(float aspect)
        {
            // Materials before the scene, for the reason ArtPreviewScene gives: creating
            // them while a scene is being populated leaves renderers on the wrong material.
            GeneratedMaterials.EnsureAssets();
            CombatPrefabBuilder.EnsureAssets();

            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            SceneLighting.Apply(LightingMood.Studio);
            CreateGround();

            Bounds contents = LayOutFlashes();
            contents.Encapsulate(LayOutSparks());

            return FrameCamera(contents, aspect);
        }

        /// <summary>
        /// Lays out the muzzle flash strip: one tank per frame, each mid-shot.
        /// </summary>
        /// <remarks>
        /// A real tank rather than a bare marker, because the only question worth asking of
        /// a muzzle flash is whether it is the right size for the gun it is coming out of -
        /// and that is a question about a barrel, which a marker does not have.
        /// </remarks>
        /// <returns>World-space bounds enclosing the strip.</returns>
        private static Bounds LayOutFlashes()
        {
            var strip = new Bounds();
            bool started = false;

            for (int frame = 0; frame < FlashFrames.Length; frame++)
            {
                GameObject tank = PlaceTank($"Flash{frame}", frame, RowOffset);
                var gun = tank.GetComponent<VehicleWeapon>();
                if (gun == null || gun.Flash == null || gun.Muzzle == null)
                {
                    Debug.LogWarning("IronFlag: the tank prefab has no gun to draw a flash on.");
                    continue;
                }

                MuzzleFlash flash = MuzzleFlash.Spawn(gun.Flash, gun.Muzzle, gun.Tuning.Radius);
                if (flash != null)
                {
                    flash.PoseAt(FlashFrames[frame]);
                }

                Swallow(ref strip, ref started, tank);
            }

            return started ? strip : new Bounds(Vector3.zero, Vector3.one * 10.0f);
        }

        /// <summary>
        /// Lays out the spark strip: one tank per frame, each taking a cannon round on the
        /// nose from the right and surviving it.
        /// </summary>
        /// <remarks>
        /// The calibre is read off the struck tank's own stamped gun rather than off the
        /// round prefab, which carries the default tuning until something fires it - a
        /// prefab has never been shot out of anything, so its <see cref="Projectile.Weapon"/>
        /// is a blank row. The tanks face the same way as the row above them, which puts the
        /// shooter off the right-hand edge of the sheet in both strips - one row firing, one
        /// row being fired at.
        /// </remarks>
        /// <returns>World-space bounds enclosing the strip.</returns>
        private static Bounds LayOutSparks()
        {
            var strip = new Bounds();
            bool started = false;

            ImpactSparks prefab = CombatPrefabBuilder.LoadSparks();
            if (prefab == null)
            {
                Debug.LogWarning("IronFlag: there is no spark prefab to draw.");
                return new Bounds(Vector3.zero, Vector3.one * 10.0f);
            }

            for (int frame = 0; frame < SparkFrames.Length; frame++)
            {
                GameObject tank = PlaceTank($"Sparks{frame}", frame, -RowOffset);
                var hull = tank.GetComponent<BoxCollider>();
                var gun = tank.GetComponent<VehicleWeapon>();

                // The nose of the hull, measured off the collider rather than guessed, and
                // raised to about turret height - which is where a flat-flying cannon round
                // aimed down onto the CombatPlane actually arrives.
                float nose = hull == null ? 2.7f : hull.center.z + (hull.size.z * 0.5f);
                Vector3 at = tank.transform.TransformPoint(new Vector3(0.0f, 1.3f, nose));

                ImpactSparks burst = ImpactSparks.Spawn(
                    prefab,
                    at,
                    tank.transform.forward,
                    gun == null ? new WeaponTuning().Radius : gun.Tuning.Radius);
                if (burst != null)
                {
                    burst.PoseAt(SparkFrames[frame]);
                    Swallow(ref strip, ref started, burst.gameObject);
                }

                Swallow(ref strip, ref started, tank);
            }

            return started ? strip : new Bounds(Vector3.zero, Vector3.one * 10.0f);
        }

        /// <summary>
        /// Grows a running bounds to take in everything one object draws.
        /// </summary>
        /// <param name="running">Bounds being accumulated.</param>
        /// <param name="started">Whether anything has been taken in yet.</param>
        /// <param name="what">Object whose renderers to swallow.</param>
        /// <remarks>
        /// The started flag is what stops a default <c>Bounds</c> - which sits at the origin
        /// with no size - being counted as a real corner and dragging the frame back to the
        /// middle of the sheet.
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
        /// Drops one tank into a filmstrip cell, side-on to the camera.
        /// </summary>
        /// <param name="name">Object name, so a frame can be found in the hierarchy.</param>
        /// <param name="frame">Which frame of the strip, zero-based and left to right.</param>
        /// <param name="depth">Where the strip sits along Z.</param>
        /// <returns>The instantiated tank.</returns>
        /// <remarks>
        /// Turned side-on, which is the one orientation that shows a muzzle flash for what
        /// it is: pointed at the camera it is a bright dot, and pointed away it is behind
        /// the vehicle. Yaw 90 degrees puts the barrel along +X, so both strips read left to
        /// right in the same direction the frames advance.
        /// </remarks>
        private static GameObject PlaceTank(string name, int frame, float depth)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                VehiclePrefabBuilder.PrefabPathFor(VehicleKind.Tank));
            var tank = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            tank.name = name;
            float span = (FlashFrames.Length - 1) * ColumnPitch;
            tank.transform.SetPositionAndRotation(
                new Vector3((frame * ColumnPitch) - (span * 0.5f), 0.0f, depth),
                Quaternion.Euler(0.0f, 90.0f, 0.0f));

            return tank;
        }

        /// <summary>Creates the ground plane the tanks stand on.</summary>
        private static void CreateGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(20.0f, 1.0f, 20.0f);

            ground.GetComponent<Renderer>().sharedMaterial =
                GeneratedMaterials.Load(GeneratedMaterials.Ground);
        }

        /// <summary>
        /// Positions the scene camera so both strips fill the frame.
        /// </summary>
        /// <param name="contents">World-space bounds of both strips.</param>
        /// <param name="aspect">Width divided by height of the intended output.</param>
        /// <returns>The configured camera.</returns>
        /// <remarks>
        /// The rake is shallower than the game's 58 degrees on purpose: a muzzle flash is a
        /// horizontal shape at barrel height, and straight down is the one angle that shows
        /// none of it. The fit is measured off the contents rather than computed from the
        /// pitch - the first version of this file did the latter and cropped the fourth
        /// frame off both strips, because a tank's origin is not the middle of a tank.
        /// </remarks>
        private static Camera FrameCamera(Bounds contents, float aspect)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                camera = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
                camera.tag = "MainCamera";
            }

            camera.transform.rotation = Quaternion.Euler(34.0f, 0.0f, 0.0f);
            camera.nearClipPlane = 0.3f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.09f, 0.10f, 0.12f);

            CameraCapture.FrameOrthographic(camera, contents, aspect, 1.06f);

            // The bloom is the point. Every emissive in this game was authored assuming it,
            // and these two effects are the brightest things in the project.
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;

            return camera;
        }
    }
}
