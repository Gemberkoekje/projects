using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using IronFlag.Editor.ArtPipeline;
using IronFlag.Levels;

namespace IronFlag.Editor.Gameplay
{
    /// <summary>
    /// Renders a whole level from directly above, the way a level is designed rather than
    /// the way it is played.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The split-screen still is a picture of a moment: two cameras thirty-four metres up,
    /// each following one vehicle, each showing about sixty metres of a map that is nearly
    /// two hundred across. It is the right picture of the game and it is useless for judging
    /// a map - you cannot see the channel, the crossings, or whether the two halves are the
    /// same shape.
    /// </para>
    /// <para>
    /// So a level gets its own shot: orthographic, straight down, framed on the land. It is
    /// the first thing to look at after editing a level file, and it is the view the in-game
    /// editor will want when it arrives.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// Unity.exe -batchmode -quit -projectPath unity
    ///   -executeMethod IronFlag.Editor.Gameplay.LevelPreview.RenderToFile
    ///   -levelOutput ../m7-map.png -level iron-channel
    /// </code>
    /// </example>
    public static class LevelPreview
    {
        /// <summary>Command-line switch naming the file <see cref="RenderToFile"/> writes.</summary>
        private const string OutputArgument = "-levelOutput";

        /// <summary>Command-line switch naming the level to render.</summary>
        private const string LevelArgument = "-level";

        /// <summary>File <see cref="RenderToFile"/> writes when the switch is absent.</summary>
        private const string DefaultOutputFile = "level-map.png";

        /// <summary>Metres of sea left showing around the land.</summary>
        /// <remarks>
        /// Enough that the island reads as an island rather than as a picture cropped to its
        /// coastline, which is the one thing a map shot has to get across before anything
        /// else.
        /// </remarks>
        private const float Margin = 14.0f;

        /// <summary>Side of the square image, in pixels.</summary>
        private const int ImageSize = 1400;

        /// <summary>
        /// Builds the default level in a scene of its own and photographs it.
        /// </summary>
        [MenuItem("Tools/IronFlag/Render Level Overview", false, 156)]
        public static void RenderToFile()
        {
            string name = CameraCapture.ValueFromCommandLine(LevelArgument, LevelLibrary.DefaultLevel);

            LevelDefinition level = LevelFile.Read(LevelLibrary.PathFor(name));
            if (level == null)
            {
                return;
            }

            foreach (string problem in LevelValidation.Problems(level))
            {
                Debug.LogWarning($"IronFlag: {name} - {problem}");
            }

            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            VehicleSandboxScene.ConfigureLighting();

            LevelBuilder.Build(level, LevelCatalogBuilder.Load(), Instantiate);

            CameraCapture.RenderToPng(
                Overhead(level),
                ImageSize,
                ImageSize,
                CameraCapture.OutputPathFromCommandLine(OutputArgument, DefaultOutputFile));
        }

        /// <summary>
        /// Puts a camera above the middle of the land, looking straight down at all of it.
        /// </summary>
        /// <param name="level">The level being photographed.</param>
        /// <returns>The camera.</returns>
        /// <remarks>
        /// Orthographic, because a map read at a perspective is a map whose far end is a
        /// different scale from its near end - and the whole point of this shot is comparing
        /// one half with the other. Framed on the land rather than on the bounds, so a level
        /// with a small island in a big sea is still a picture of the island.
        /// </remarks>
        private static Camera Overhead(LevelDefinition level)
        {
            Bounds land = level.LandBounds();
            float half = Mathf.Max(land.extents.x, land.extents.z) + Margin;

            var host = new GameObject("Overhead");
            host.transform.SetPositionAndRotation(
                new Vector3(land.center.x, 200.0f, land.center.z),
                Quaternion.Euler(90.0f, 0.0f, 0.0f));

            Camera view = host.AddComponent<Camera>();
            view.orthographic = true;
            view.orthographicSize = half;
            view.nearClipPlane = 1.0f;
            view.farClipPlane = 400.0f;
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = Color.black;
            return view;
        }

        private static GameObject Instantiate(GameObject prefab)
            => (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    }
}
