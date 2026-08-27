using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using IronFlag.Core;

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
        /// <para>
        /// Each camera clears and draws only inside its own viewport, so rendering them one
        /// after another into the same texture composites the halves without any blitting
        /// here. Anything no camera covers stays at the texture's cleared black.
        /// </para>
        /// <para>
        /// <strong>Interface is a second pass, and it has to be.</strong> The HUD and the
        /// level editor's panels are drawn by cameras stacked on these ones so that
        /// post-processing leaves them alone - see <see cref="IronFlag.Core.ViewStack"/> -
        /// and URP does not render a camera stack from an offline one-shot render like this
        /// one. Neither <see cref="Camera.Render"/> nor a standard render request draws the
        /// overlays: both were tried, both came back as a correctly graded picture of a game
        /// with no interface in it, and neither logged anything. So the stack is walked here
        /// instead - the world is rendered, then the interface cameras are rendered on their
        /// own over nothing, then the two are composited.
        /// </para>
        /// <para>
        /// Compositing happens on the pixels rather than by letting the second pass draw
        /// straight over the first, because a URP base camera has no way to leave the colour
        /// it finds alone. <see cref="CameraClearFlags.Depth"/> - which is exactly that in
        /// the built-in pipeline - counts as "uninitialized" in URP, and what it actually
        /// produced was a perfect HUD floating on a sheet of flat blue.
        /// </para>
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
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Texture2D drawn = null;
            RenderTexture previouslyActive = RenderTexture.active;

            bool batching = SetScriptableRenderPipelineBatching(false);

            try
            {
                foreach (Camera camera in cameras)
                {
                    camera.targetTexture = target;
                    camera.Render();
                }

                ReadInto(image, target, width, height);

                Camera[] interfaces = InterfacesOf(cameras);
                if (interfaces.Length > 0)
                {
                    drawn = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    RenderInterfaces(interfaces, target);
                    ReadInto(drawn, target, width, height);
                    Composite(image, drawn);
                }

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
                if (drawn != null)
                {
                    UnityEngine.Object.DestroyImmediate(drawn);
                }

                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        /// <summary>
        /// Points an orthographic camera at some bounds and sizes it so they fill the frame.
        /// </summary>
        /// <param name="camera">Camera to place. Its rotation is used and not changed.</param>
        /// <param name="contents">World-space bounds to frame.</param>
        /// <param name="aspect">Width divided by height of the intended output.</param>
        /// <param name="margin">How much slack to leave, as a multiplier; 1.0 is tight.</param>
        /// <exception cref="ArgumentNullException"><paramref name="camera"/> is null.</exception>
        /// <remarks>
        /// <para>
        /// Every generated preview in this project frames a pile of objects it just laid
        /// out, and each of them was getting this wrong in its own way. It is fiddlier than
        /// it looks: <see cref="Camera.orthographicSize"/> is a half-<em>height</em>, so the
        /// half-width has to be divided by the aspect before the two can be compared, and
        /// the bounds have to be measured in the camera's own space rather than the world's
        /// or a raked view crops its own corners.
        /// </para>
        /// <para>
        /// The camera is pulled a long way back and given a far plane to match rather than
        /// fitted to the depth of the contents, because an orthographic camera loses nothing
        /// by standing further off and a near plane that clips the front row of a preview is
        /// the failure this avoids.
        /// </para>
        /// </remarks>
        public static void FrameOrthographic(
            Camera camera, Bounds contents, float aspect, float margin)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            Quaternion inverseRotation = Quaternion.Inverse(camera.transform.rotation);
            float halfWidth = 0.0f;
            float halfHeight = 0.0f;
            float halfDepth = 0.0f;

            for (int corner = 0; corner < 8; corner++)
            {
                var offset = new Vector3(
                    (corner & 1) == 0 ? -contents.extents.x : contents.extents.x,
                    (corner & 2) == 0 ? -contents.extents.y : contents.extents.y,
                    (corner & 4) == 0 ? -contents.extents.z : contents.extents.z);
                Vector3 local = inverseRotation * offset;

                halfWidth = Mathf.Max(halfWidth, Mathf.Abs(local.x));
                halfHeight = Mathf.Max(halfHeight, Mathf.Abs(local.y));
                halfDepth = Mathf.Max(halfDepth, Mathf.Abs(local.z));
            }

            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(halfHeight, halfWidth / aspect) * margin;
            camera.farClipPlane = (2.0f * halfDepth) + 400.0f;
            camera.transform.position = contents.center - (camera.transform.forward * (halfDepth + 200.0f));
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
        /// Returns the interface camera stacked on each of these cameras, where there is one.
        /// </summary>
        /// <param name="cameras">The world cameras being rendered.</param>
        /// <returns>Their interface cameras, in the same order.</returns>
        private static Camera[] InterfacesOf(Camera[] cameras)
        {
            var found = new List<Camera>();

            foreach (Camera camera in cameras)
            {
                Camera view = ViewStack.Existing(camera);
                if (view != null)
                {
                    found.Add(view);
                }
            }

            return found.ToArray();
        }

        /// <summary>
        /// Draws the interface cameras on their own, over nothing.
        /// </summary>
        /// <param name="interfaces">The cameras drawing the interface.</param>
        /// <param name="target">The texture to draw into, which is wiped first.</param>
        /// <remarks>
        /// Each is promoted to a base camera for the duration, because an overlay camera is
        /// only ever drawn as part of a stack and a stack is the thing that does not happen
        /// out here. They are put back afterwards: the live game does render them as a stack,
        /// and that is what they are configured for.
        /// </remarks>
        private static void RenderInterfaces(Camera[] interfaces, RenderTexture target)
        {
            RenderTexture previouslyActive = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = previouslyActive;

            foreach (Camera view in interfaces)
            {
                UniversalAdditionalCameraData data = view.GetUniversalAdditionalCameraData();
                CameraClearFlags clearing = view.clearFlags;
                Color background = view.backgroundColor;

                data.renderType = CameraRenderType.Base;
                view.clearFlags = CameraClearFlags.SolidColor;
                view.backgroundColor = Color.clear;
                view.targetTexture = target;

                try
                {
                    view.Render();
                }
                finally
                {
                    view.targetTexture = null;
                    view.clearFlags = clearing;
                    view.backgroundColor = background;
                    data.renderType = CameraRenderType.Overlay;
                }
            }
        }

        /// <summary>
        /// Lays one image over another, using the top one's alpha.
        /// </summary>
        /// <param name="under">The image drawn onto, which is changed in place.</param>
        /// <param name="over">The image laid over it.</param>
        private static void Composite(Texture2D under, Texture2D over)
        {
            Color32[] below = under.GetPixels32();
            Color32[] above = over.GetPixels32();

            for (int index = 0; index < below.Length && index < above.Length; index++)
            {
                int alpha = above[index].a;
                if (alpha == 0)
                {
                    continue;
                }

                if (alpha == 255)
                {
                    below[index] = above[index];
                    continue;
                }

                below[index] = new Color32(
                    Mix(below[index].r, above[index].r, alpha),
                    Mix(below[index].g, above[index].g, alpha),
                    Mix(below[index].b, above[index].b, alpha),
                    255);
            }

            under.SetPixels32(below);
            under.Apply();
        }

        private static byte Mix(byte under, byte over, int alpha)
            => (byte)(((over * alpha) + (under * (255 - alpha))) / 255);

        /// <summary>
        /// Copies what a texture currently holds back into an image.
        /// </summary>
        /// <param name="image">The image to fill.</param>
        /// <param name="target">The texture to read.</param>
        /// <param name="width">Image width in pixels.</param>
        /// <param name="height">Image height in pixels.</param>
        private static void ReadInto(Texture2D image, RenderTexture target, int width, int height)
        {
            RenderTexture previouslyActive = RenderTexture.active;
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = previouslyActive;
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
        /// SerializedObject rather than through the typed property because that property is
        /// not settable, and is always restored by the caller.
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
