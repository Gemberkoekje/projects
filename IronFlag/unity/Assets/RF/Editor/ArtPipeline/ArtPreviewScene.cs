using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace IronFlag.Editor.ArtPipeline
{
    /// <summary>
    /// Builds a scene that lays out every model in <c>Assets/RF/Art/Models</c> side by
    /// side, in both team colors, so the output of the Blender pipeline can be checked
    /// at a glance without wiring anything into gameplay.
    /// </summary>
    /// <remarks>
    /// The scene is generated, never hand-edited: rebuilding it after an art change is
    /// one menu item, and it picks up new assets automatically. <see cref="RenderToFile"/>
    /// is the same thing driven from the command line, which is how the preview image
    /// gets produced without opening the editor.
    /// </remarks>
    public static class ArtPreviewScene
    {
        /// <summary>Folder scanned for models to lay out.</summary>
        public const string ModelFolder = "Assets/RF/Art/Models";

        /// <summary>Where the generated scene is saved.</summary>
        public const string ScenePath = "Assets/RF/Scenes/ArtPreview.unity";

        /// <summary>Command-line switch naming the file <see cref="RenderToFile"/> writes.</summary>
        private const string OutputArgument = "-previewOutput";

        /// <summary>File <see cref="RenderToFile"/> writes when the switch is absent.</summary>
        private const string DefaultOutputFile = "art-preview.png";

        /// <summary>Metres of clear space between adjacent grid cells.</summary>
        private const float CellGap = 4.0f;

        /// <summary>Metres between the two team variants of one asset.</summary>
        private const float PairGap = 2.0f;

        /// <summary>
        /// Rebuilds the preview scene from the models currently on disk and saves it.
        /// </summary>
        [MenuItem("Tools/IronFlag/Build Art Preview Scene", false, 140)]
        public static void BuildAndSave()
        {
            // Building replaces whatever scene is open, so give the usual save prompt
            // first rather than silently discarding someone's work.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Build(16.0f / 9.0f);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ScenePath)));
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.Refresh();
            Debug.Log($"IronFlag: art preview scene saved to {ScenePath}");
        }

        /// <summary>
        /// Builds the preview scene and writes a render of it to a PNG file.
        /// </summary>
        /// <remarks>
        /// Intended for <c>-executeMethod</c>. Pass <c>-previewOutput &lt;path&gt;</c> to
        /// choose the file; without it the image lands next to the Unity project. Run
        /// Unity in <c>-batchmode</c> but <em>not</em> <c>-nographics</c>, since this
        /// needs a real graphics device.
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
        /// Creates the scene contents: ground, lighting, every model laid out in a grid,
        /// and a camera framing all of it.
        /// </summary>
        /// <param name="aspect">Width divided by height of the intended output.</param>
        /// <returns>The scene camera, already framed on the models.</returns>
        public static Camera Build(float aspect)
        {
            // Materials are created as assets before the scene exists, and only looked up
            // afterwards. Creating them while the scene is being populated left renderers
            // resolving to the wrong material - the ground came out wearing a team color.
            GeneratedMaterials.EnsureAssets();
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            ConfigureLighting();
            CreateGround();

            List<GameObject> models = LoadModels();
            if (models.Count == 0)
            {
                Debug.LogWarning($"IronFlag: no models found in {ModelFolder}. "
                    + "Run blender/build.ps1 first.");
            }

            return FrameCamera(LayOutGrid(models), aspect);
        }

        /// <summary>
        /// Lays every model out in a grid, pairing the two team variants side by side.
        /// </summary>
        /// <param name="models">Imported model assets, in display order.</param>
        /// <returns>World-space bounds enclosing everything placed.</returns>
        /// <remarks>
        /// Cells are sized from the models actually present rather than on a fixed pitch,
        /// because the set spans a 3m tree and a 16m bridge; a uniform grid either overlaps
        /// the big assets or strands the small ones in whitespace. Only assets that carry
        /// team trim get a second instance - a neutral prop would just be drawn twice
        /// identically.
        /// </remarks>
        private static Bounds LayOutGrid(List<GameObject> models)
        {
            if (models.Count == 0)
            {
                return new Bounds(Vector3.zero, Vector3.one * 10.0f);
            }

            int columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(models.Count)));
            int rows = Mathf.CeilToInt(models.Count / (float)columns);

            var green = new GameObject[models.Count];
            var brown = new GameObject[models.Count];
            var size = new Vector3[models.Count];

            for (int index = 0; index < models.Count; index++)
            {
                green[index] = Place(models[index], Vector3.zero, GeneratedMaterials.Green);
                size[index] = MeasuredSize(green[index]);
                if (HasTeamTrim(models[index]))
                {
                    brown[index] = Place(models[index], Vector3.zero, GeneratedMaterials.Brown);
                }
            }

            var columnWidth = new float[columns];
            var rowDepth = new float[rows];
            for (int index = 0; index < models.Count; index++)
            {
                int column = index % columns;
                int row = index / columns;
                float width = brown[index] == null
                    ? size[index].x
                    : (2.0f * size[index].x) + PairGap;
                columnWidth[column] = Mathf.Max(columnWidth[column], width);
                rowDepth[row] = Mathf.Max(rowDepth[row], size[index].z);
            }

            float[] columnCentre = Centres(columnWidth);
            float[] rowCentre = Centres(rowDepth);

            var contents = new Bounds(Vector3.zero, Vector3.zero);
            bool started = false;

            for (int index = 0; index < models.Count; index++)
            {
                float x = columnCentre[index % columns];
                float z = rowCentre[index / columns];
                float offset = brown[index] == null ? 0.0f : 0.5f * (size[index].x + PairGap);

                green[index].transform.position = new Vector3(x - offset, 0.0f, z);
                if (brown[index] != null)
                {
                    brown[index].transform.position = new Vector3(x + offset, 0.0f, z);
                }

                foreach (GameObject instance in new[] { green[index], brown[index] })
                {
                    if (instance == null)
                    {
                        continue;
                    }

                    foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
                    {
                        if (started)
                        {
                            contents.Encapsulate(renderer.bounds);
                        }
                        else
                        {
                            contents = renderer.bounds;
                            started = true;
                        }
                    }
                }
            }

            return started ? contents : new Bounds(Vector3.zero, Vector3.one * 10.0f);
        }

        /// <summary>
        /// Converts a run of cell extents into centre positions, centred on the origin.
        /// </summary>
        /// <param name="extents">Extent of each cell along the axis.</param>
        /// <returns>The centre coordinate of each cell.</returns>
        private static float[] Centres(float[] extents)
        {
            float total = 0.0f;
            foreach (float extent in extents)
            {
                total += extent + CellGap;
            }

            total -= CellGap;

            var centres = new float[extents.Length];
            float cursor = -0.5f * total;
            for (int index = 0; index < extents.Length; index++)
            {
                centres[index] = cursor + (0.5f * extents[index]);
                cursor += extents[index] + CellGap;
            }

            return centres;
        }

        /// <summary>
        /// Measures the world-space size of everything an instance renders.
        /// </summary>
        /// <param name="instance">Scene object to measure.</param>
        /// <returns>The size of its combined renderer bounds.</returns>
        private static Vector3 MeasuredSize(GameObject instance)
        {
            var bounds = new Bounds(instance.transform.position, Vector3.zero);
            bool started = false;
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
            {
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

            return started ? bounds.size : Vector3.one;
        }

        /// <summary>
        /// Reports whether a model carries any team-accent geometry.
        /// </summary>
        /// <param name="model">Imported model asset.</param>
        /// <returns><c>true</c> when one of its renderers wears the accent material.</returns>
        private static bool HasTeamTrim(GameObject model)
        {
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if (GeneratedMaterials.IsTeamTrim(renderer))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Instantiates one model, names it for its team, and applies the team material.
        /// </summary>
        /// <param name="model">Imported model asset to instantiate.</param>
        /// <param name="position">Where to place it.</param>
        /// <param name="team">Asset name of the team material to apply.</param>
        /// <returns>The instantiated scene object.</returns>
        private static GameObject Place(GameObject model, Vector3 position, string team)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name = $"{model.name}_{team}";
            instance.transform.position = position;
            // Turned to face the camera. Assets are authored facing +Z and the camera
            // looks along +Z, so without this every review shot is of their backs - and
            // the front is where the weapons, lights and entrances are.
            instance.transform.rotation = Quaternion.Euler(0.0f, 180.0f, 0.0f);

            GeneratedMaterials.Apply(instance, team);
            return instance;
        }

        /// <summary>
        /// Loads every model asset in <see cref="ModelFolder"/>, sorted by name.
        /// </summary>
        /// <returns>The imported model root objects.</returns>
        private static List<GameObject> LoadModels()
        {
            var models = new List<GameObject>();
            if (!Directory.Exists(Path.GetFullPath(ModelFolder)))
            {
                return models;
            }

            var paths = new List<string>(Directory.GetFiles(Path.GetFullPath(ModelFolder), "*.glb"));
            paths.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (string absolutePath in paths)
            {
                string assetPath = $"{ModelFolder}/{Path.GetFileName(absolutePath)}";
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (model == null)
                {
                    Debug.LogWarning($"IronFlag: {assetPath} did not import as a model.");
                    continue;
                }

                models.Add(model);
            }

            return models;
        }

        /// <summary>Sets up sun angle and ambient light for a readable preview.</summary>
        private static void ConfigureLighting()
        {
            Light sun = UnityEngine.Object.FindAnyObjectByType<Light>();
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(48.0f, -35.0f, 0.0f);
                sun.intensity = 1.4f;
                sun.shadows = LightShadows.Soft;
                sun.color = new Color(1.0f, 0.97f, 0.9f);
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.48f, 0.58f);
            RenderSettings.ambientEquatorColor = new Color(0.32f, 0.34f, 0.35f);
            RenderSettings.ambientGroundColor = new Color(0.18f, 0.17f, 0.15f);

            // Ambient changes made from script only take effect once the environment is
            // rebuilt; without this the scene keeps the ambient probe it was created with.
            DynamicGI.UpdateEnvironment();
        }

        /// <summary>Creates the ground plane the models stand on.</summary>
        private static void CreateGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(30.0f, 1.0f, 30.0f);

            ground.GetComponent<Renderer>().sharedMaterial =
                GeneratedMaterials.Load(GeneratedMaterials.Ground);
        }

        /// <summary>
        /// Positions the scene camera so the given bounds fill the frame.
        /// </summary>
        /// <param name="contents">World-space bounds to frame.</param>
        /// <param name="aspect">Width divided by height of the intended output.</param>
        /// <returns>The configured camera.</returns>
        private static Camera FrameCamera(Bounds contents, float aspect)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                camera = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
                camera.tag = "MainCamera";
            }

            // A raked three-quarter view rather than the game's straight-down camera:
            // silhouettes and vertex colors both need to be visible here. Orthographic,
            // because this is a contact sheet - under perspective the back row of a
            // 25-asset grid renders at a fraction of the size of the front row, and the
            // whole point is comparing assets against each other.
            camera.transform.rotation = Quaternion.Euler(38.0f, 0.0f, 0.0f);
            camera.orthographic = true;
            camera.nearClipPlane = 0.3f;
            // A flat backdrop rather than the default skybox: this is a product shot of
            // the models, and a gradient sky just competes with them.
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.09f, 0.10f, 0.12f);

            // Fit by measuring every corner of the bounds in camera space. Half-height is
            // what orthographicSize means; half-width has to be divided by the aspect
            // before the two are compared.
            Quaternion inverseRotation = Quaternion.Inverse(camera.transform.rotation);
            float halfWidth = 0.0f;
            float halfHeight = 0.0f;
            float halfDepth = 0.0f;

            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 offset = new Vector3(
                    (corner & 1) == 0 ? -contents.extents.x : contents.extents.x,
                    (corner & 2) == 0 ? -contents.extents.y : contents.extents.y,
                    (corner & 4) == 0 ? -contents.extents.z : contents.extents.z);
                Vector3 local = inverseRotation * offset;

                halfWidth = Mathf.Max(halfWidth, Mathf.Abs(local.x));
                halfHeight = Mathf.Max(halfHeight, Mathf.Abs(local.y));
                halfDepth = Mathf.Max(halfDepth, Mathf.Abs(local.z));
            }

            camera.orthographicSize = Mathf.Max(halfHeight, halfWidth / aspect) * 1.04f;
            camera.farClipPlane = (2.0f * halfDepth) + 400.0f;
            camera.transform.position = contents.center - (camera.transform.forward * (halfDepth + 200.0f));
            return camera;
        }
    }
}
