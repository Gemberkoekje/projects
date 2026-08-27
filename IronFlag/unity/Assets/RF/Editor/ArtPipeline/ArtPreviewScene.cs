using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using IronFlag.Core;

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
        /// <remarks>
        /// These numbers used to be written out here, a few degrees and a shade away from a
        /// near-identical block in the sandbox builder. They are the
        /// <see cref="LightingMood.Studio"/> row now - still the preview's own lighting and
        /// still free to differ from the game's, but differing somewhere the difference can
        /// be read rather than in a second copy nobody diffs against the first.
        /// </remarks>
        private static void ConfigureLighting() => SceneLighting.Apply(LightingMood.Studio);

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
            camera.nearClipPlane = 0.3f;
            // A flat backdrop rather than the default skybox: this is a product shot of
            // the models, and a gradient sky just competes with them. The sky is still worth
            // hanging, because it is what the METAL palette reflects.
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.09f, 0.10f, 0.12f);

            // The one view in the project that deliberately runs no post-processing, and the
            // reason is what this sheet is for. It is a contact sheet: twenty-five assets in a
            // grid, laid out to be compared against each other and measured off. A vignette
            // makes the same model read darker in the corner than in the middle, and a tone
            // curve moves a colour away from the palette value somebody is checking it
            // against - so the two effects that are worth having everywhere else are exactly
            // the two that would break this. It keeps the shared lighting; it skips the grade.
            // The sandbox still is where an emissive is judged with its bloom on.

            // Four per cent of slack: this is a contact sheet, so the grid should fill the
            // sheet, and the margin is only there to keep the outermost silhouette off the
            // edge. The fit itself is shared with the other generated previews.
            CameraCapture.FrameOrthographic(camera, contents, aspect, 1.04f);
            return camera;
        }
    }
}
