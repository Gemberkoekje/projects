using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using IronFlag.Core;
using IronFlag.Editor.ArtPipeline;
using IronFlag.Levels;

namespace IronFlag.Editor.Gameplay
{
    /// <summary>
    /// Renders a stretch of coastline from where a player would be looking at it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two shots this project already had between them cannot show the water. The
    /// split-screen still frames whichever vehicle the staged match put on the field, and
    /// both of them are inland; the level overview is orthographic from two hundred metres
    /// up with the fog turned off, which is a picture of a map rather than of a sea. So the
    /// swell, the glint, the shore wash and the foam - everything the ground and water pass
    /// added - were all invisible in both of the pictures anybody reviews.
    /// </para>
    /// <para>
    /// This is the third shot: the game's own camera - the same 58 degrees, the same
    /// thirty-four metres, the same fog and grade - pointed at the coast instead of at a
    /// vehicle. It is the only view in which the water can be judged, and it is what a
    /// change to <see cref="SurfaceLook"/> should be looked at in.
    /// </para>
    /// <para>
    /// Nothing plays, so <see cref="WaterClock"/> never runs and the sea is a flat calm at
    /// time zero. That is the point: two renders of an unchanged map are the same image, and
    /// a still that showed a different wave every time would be a still nobody could diff.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// Unity.exe -batchmode -quit -projectPath unity
    ///   -executeMethod IronFlag.Editor.Gameplay.CoastPreview.RenderToFile
    ///   -coastOutput ../coast.png -level iron-channel
    /// </code>
    /// </example>
    public static class CoastPreview
    {
        /// <summary>Command-line switch naming the file <see cref="RenderToFile"/> writes.</summary>
        private const string OutputArgument = "-coastOutput";

        /// <summary>Command-line switch naming the level to render.</summary>
        private const string LevelArgument = "-level";

        /// <summary>File <see cref="RenderToFile"/> writes when the switch is absent.</summary>
        private const string DefaultOutputFile = "level-coast.png";

        /// <summary>The gameplay camera's own framing, which this shot borrows exactly.</summary>
        /// <remarks>
        /// Written out rather than read off a <see cref="TopDownCameraRig"/>, because there
        /// is no rig in this scene and building one to ask it three numbers would be a rig
        /// that had to be kept out of the picture. If the rig's defaults move, these move
        /// with them - and a shot framed differently from the game is a shot that proves
        /// nothing about the game.
        /// </remarks>
        private const float Pitch = 58.0f;
        private const float Yaw = 0.0f;
        private const float Distance = 34.0f;

        /// <summary>Size of the image, in pixels.</summary>
        /// <remarks>
        /// The shape of one seat of the split screen rather than a square: the water is
        /// judged in a viewport this wide, and a square crop of it would show a band of sea
        /// no player ever sees at once.
        /// </remarks>
        private const int ImageWidth = 1600;
        private const int ImageHeight = 900;

        /// <summary>How far apart the points that look for the coast are, in metres.</summary>
        /// <remarks>
        /// A fifth of a cell. <see cref="SurfaceField"/> measures the map in metres and
        /// <see cref="SurfaceField.Shore"/> reads between the cells, so a step much finer
        /// than this buys nothing; a step much coarser can straddle a narrow channel and
        /// find a coast on the far side of it.
        /// </remarks>
        private const float Step = 0.2f;

        /// <summary>
        /// Builds the default level in a scene of its own and photographs its coast.
        /// </summary>
        [MenuItem("Tools/IronFlag/Render Coast Preview", false, 157)]
        public static void RenderToFile()
        {
            string name = CameraCapture.ValueFromCommandLine(LevelArgument, LevelLibrary.DefaultLevel);

            LevelDefinition level = LevelFile.Read(LevelLibrary.PathFor(name));
            if (level == null)
            {
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // The game's lighting in full, haze included, unlike the level overview: this
            // camera is where the player's is, so the fog it renders through is the fog they
            // would be looking at the same water through.
            SceneLighting.Apply(LightingMood.Daylight, LightingTuning.For(LightingMood.Daylight));

            LevelBuilder.Build(level, LevelCatalogBuilder.Load(), Instantiate);

            CameraCapture.RenderToPng(
                Ashore(level),
                ImageWidth,
                ImageHeight,
                CameraCapture.OutputPathFromCommandLine(OutputArgument, DefaultOutputFile));
        }

        /// <summary>
        /// Puts the game's camera on the coast, looking out to sea.
        /// </summary>
        /// <param name="level">The level being photographed.</param>
        /// <returns>The camera.</returns>
        /// <remarks>
        /// Focused a few metres out from the waterline rather than on it, so the frame is
        /// roughly half land and half water: the shore wash and the foam are both about the
        /// boundary, and a shot of nothing but sea says as little as a shot of nothing but
        /// beach.
        /// </remarks>
        private static Camera Ashore(LevelDefinition level)
        {
            Vector3 focus = Waterline(level);

            var host = new GameObject("Coast");
            host.transform.SetPositionAndRotation(
                TopDownCameraRig.SolveCameraPosition(focus, Pitch, Yaw, Distance),
                TopDownCameraRig.SolveRotation(Pitch, Yaw));

            Camera view = host.AddComponent<Camera>();
            view.nearClipPlane = 0.3f;
            view.farClipPlane = 400.0f;

            ViewStack.MakeWorldView(view);
            return view;
        }

        /// <summary>
        /// Finds a point on the level's southern shore.
        /// </summary>
        /// <param name="level">The level being photographed.</param>
        /// <returns>A point a little out to sea from where the land ends.</returns>
        /// <remarks>
        /// <para>
        /// Walked <em>north</em> from below the map until the first land turns up, rather
        /// than south from the middle of it until the land runs out. The two find different
        /// coasts and only one of them is worth photographing: the shipped map is two
        /// islands with a channel between them, so walking out from the middle finds the
        /// inside of that channel - a stretch of water with a bridge over it and land on
        /// both sides. Coming in from outside finds the island's own outer shore, which has
        /// open sea in front of it and a beach behind it.
        /// </para>
        /// <para>
        /// Found rather than written into a level file, so the shot stays honest on a map
        /// nobody has seen: a generated island has an outer shore wherever its shape put
        /// one, and this photographs that one instead of open water.
        /// </para>
        /// </remarks>
        private static Vector3 Waterline(LevelDefinition level)
        {
            SurfaceField field = level.Field;
            Bounds land = level.LandBounds();
            var fallback = new Vector3(land.center.x, 0.0f, land.min.z);

            if (field == null)
            {
                return fallback;
            }

            float from = land.min.z - Distance;
            for (float gone = 0.0f; gone < land.size.z + Distance; gone += Step)
            {
                var here = new Vector3(land.center.x, 0.0f, from + gone);
                if (field.Shore(here) > 0.0f)
                {
                    // Backed off a little into the water, so the waterline sits above the
                    // middle of the frame and the sea fills the bottom of it.
                    return new Vector3(here.x, 0.0f, here.z - (Distance * 0.12f));
                }
            }

            return fallback;
        }

        private static GameObject Instantiate(GameObject prefab)
            => (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    }
}
