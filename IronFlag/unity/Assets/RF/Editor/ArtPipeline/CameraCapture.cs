using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace IronFlag.Editor.ArtPipeline
{
    /// <summary>
    /// Renders a camera straight to a PNG, for the tools that are driven from the command
    /// line rather than from the editor window.
    /// </summary>
    /// <remarks>
    /// Every generated scene in this project has a matching <c>-executeMethod</c> entry
    /// point that produces an image, so a change can be reviewed without opening Unity.
    /// Run the editor in <c>-batchmode</c> but <em>not</em> <c>-nographics</c>: this needs a
    /// real graphics device.
    /// </remarks>
    public static class CameraCapture
    {
        /// <summary>
        /// Renders a camera to a PNG file.
        /// </summary>
        /// <param name="camera">Camera to render. Its target texture is restored afterwards.</param>
        /// <param name="width">Image width in pixels.</param>
        /// <param name="height">Image height in pixels.</param>
        /// <param name="outputPath">Absolute path of the file to write.</param>
        /// <exception cref="ArgumentNullException"><paramref name="camera"/> is null.</exception>
        public static void RenderToPng(Camera camera, int width, int height, string outputPath)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            RenderToPng(new[] { camera }, width, height, outputPath);
        }

        /// <summary>
        /// Renders several cameras into one PNG file, each into its own viewport.
        /// </summary>
        /// <param name="cameras">
        /// Cameras to render, in the order they should be drawn. Each one keeps its
        /// <see cref="Camera.rect"/>, so a split screen comes out split.
        /// </param>
        /// <param name="width">Image width in pixels.</param>
        /// <param name="height">Image height in pixels.</param>
        /// <param name="outputPath">Absolute path of the file to write.</param>
        /// <exception cref="ArgumentNullException"><paramref name="cameras"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="cameras"/> is empty.</exception>
        /// <remarks>
        /// Each camera clears and draws only inside its own viewport, so rendering them one
        /// after another into the same texture composites the halves without any blitting
        /// here. Anything no camera covers stays at the texture's cleared black.
        /// </remarks>
        public static void RenderToPng(Camera[] cameras, int width, int height, string outputPath)
        {
            if (cameras == null)
            {
                throw new ArgumentNullException(nameof(cameras));
            }

            if (cameras.Length == 0)
            {
                throw new ArgumentException("There is nothing to render.", nameof(cameras));
            }

            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4,
            };
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previouslyActive = RenderTexture.active;

            bool batching = SetScriptableRenderPipelineBatching(false);

            try
            {
                foreach (Camera camera in cameras)
                {
                    camera.targetTexture = target;
                    camera.Render();
                }

                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                File.WriteAllBytes(outputPath, image.EncodeToPNG());
                Debug.Log($"IronFlag: rendered to {outputPath}");
            }
            finally
            {
                SetScriptableRenderPipelineBatching(batching);
                RenderTexture.active = previouslyActive;
                foreach (Camera camera in cameras)
                {
                    camera.targetTexture = null;
                }

                UnityEngine.Object.DestroyImmediate(image);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        /// <summary>
        /// Reads an output path from the command line, falling back to a default.
        /// </summary>
        /// <param name="argument">Switch to look for, including its leading dash.</param>
        /// <param name="defaultFileName">File name to use when the switch is absent.</param>
        /// <returns>An absolute path for the rendered PNG.</returns>
        public static string OutputPathFromCommandLine(string argument, string defaultFileName)
            => Path.GetFullPath(ValueFromCommandLine(argument, defaultFileName));

        /// <summary>
        /// Reads the value following a command-line switch.
        /// </summary>
        /// <param name="argument">The switch, including its leading dash.</param>
        /// <param name="fallback">What to return when the switch is not there.</param>
        /// <returns>The value after the switch, or <paramref name="fallback"/>.</returns>
        public static string ValueFromCommandLine(string argument, string fallback)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (arguments[index] == argument)
                {
                    return arguments[index + 1];
                }
            }

            return fallback;
        }

        /// <summary>
        /// Turns the SRP Batcher on or off, returning what it was set to before.
        /// </summary>
        /// <param name="enabled">Whether the batcher should be enabled.</param>
        /// <returns>The previous setting, so the caller can restore it.</returns>
        /// <remarks>
        /// The batcher groups objects by shader and feeds the whole batch one material's
        /// constant buffer. Materials created and rendered in the same frame - exactly what
        /// a build-and-render-immediately path does - all end up drawing with whichever
        /// material the batch bound, so every URP Lit object comes out the same colour. A
        /// scene opened normally in the editor references materials that already exist and
        /// batches correctly, so only these command-line paths are affected.
        ///
        /// The flag lives on the URP asset - setting
        /// <c>GraphicsSettings.useScriptableRenderPipelineBatching</c> does not stick,
        /// because the pipeline reasserts its own value every frame. It is written through
        /// SerializedObject to avoid taking an assembly reference on URP's runtime just for
        /// one boolean, and is always restored by the caller.
        /// </remarks>
        private static bool SetScriptableRenderPipelineBatching(bool enabled)
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null)
            {
                return enabled;
            }

            var serialized = new SerializedObject(pipeline);
            SerializedProperty property = serialized.FindProperty("m_UseSRPBatcher");
            if (property == null)
            {
                return enabled;
            }

            bool previous = property.boolValue;
            property.boolValue = enabled;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return previous;
        }
    }
}
