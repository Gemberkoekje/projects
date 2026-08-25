using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace IronFlag.Core
{
    /// <summary>
    /// Turns post-processing on for a camera that draws the world, and hangs a second camera
    /// off it that draws the interface after the post-processing has happened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The reason any of this exists.</strong> A URP camera does not run the volume
    /// profile unless it is told to - <c>renderPostProcessing</c> is <c>false</c> by default -
    /// and nothing in this project had ever told one, which is why a fully authored set of
    /// emissive materials had never once glowed. Switching it on is one line. The rest of
    /// this file is what that one line breaks.
    /// </para>
    /// <para>
    /// What it breaks is the interface. Both canvases here - the player's HUD and the level
    /// editor's panels - are <see cref="RenderMode.ScreenSpaceCamera"/>, which makes them
    /// geometry inside the camera's frame and therefore something post-processing lands on:
    /// the tone curve would pull the saturation out of hand-picked HUD colours and bright
    /// labels would bloom. The obvious fix - a screen-space <em>overlay</em> canvas, which the
    /// engine draws after every camera and post-processing never touches - is the one fix this
    /// project cannot take, and <see cref="IronFlag.Editing.EditorUi"/> says why: an overlay
    /// canvas does not appear in anything rendered to a texture, and every screenshot this
    /// project validates itself with is rendered to a texture. A command-line still of the
    /// level editor would be a picture of a map with no editor around it.
    /// </para>
    /// <para>
    /// A camera stack satisfies both. The interface goes on its own layer, drawn by an overlay
    /// camera with post-processing switched off, stacked on the world camera: it renders
    /// through a camera, so it lands in a capture, and it renders after the world camera's
    /// post-processing pass, so nothing is graded but the world. The world camera stops
    /// drawing that layer, which is the half that is easy to forget and shows up as an
    /// interface drawn twice, once graded.
    /// </para>
    /// <para>
    /// Every function here is safe to run twice, because the scene builders that call them are
    /// menu items somebody presses again whenever a map changes.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// ViewStack.MakeWorldView(camera);
    /// Camera interfaceView = ViewStack.AttachInterfaceView(camera, InterfaceLayers.LayerFor(slot));
    /// hud.Configure(driver, interfaceView, slot);
    /// </code>
    /// </example>
    public static class ViewStack
    {
        /// <summary>Appended to a world camera's name to name the camera drawing its interface.</summary>
        public const string InterfaceSuffix = " Interface";

        /// <summary>
        /// Makes a camera the one that draws the world: post-processing on, anti-aliased,
        /// and the base of its own stack.
        /// </summary>
        /// <param name="world">The camera that draws the world.</param>
        public static void MakeWorldView(Camera world)
        {
            if (world == null)
            {
                return;
            }

            UniversalAdditionalCameraData data = world.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = true;
            data.antialiasing = PostTuning.Antialiasing;
            data.antialiasingQuality = PostTuning.AntialiasingLevel;
        }

        /// <summary>
        /// Hangs a camera off a world camera to draw one interface layer over it, unprocessed.
        /// </summary>
        /// <param name="world">
        /// The camera drawing the world, which is also the one that stops drawing
        /// <paramref name="layer"/>.
        /// </param>
        /// <param name="layer">
        /// The layer the interface lives on. A negative layer - which is what
        /// <see cref="IronFlag.UI.InterfaceLayers.LayerFor"/> answers when the project has no such layer -
        /// leaves the world camera alone and answers with it, so an interface still gets
        /// drawn rather than disappearing.
        /// </param>
        /// <returns>The camera the interface should be drawn by.</returns>
        /// <remarks>
        /// A child of the world camera at no offset, so it always agrees with it about where
        /// the frame is. It copies the projection rather than sharing one, which is what lets
        /// the level editor's interface stay put while the world camera underneath it zooms
        /// from 12 metres to 220.
        /// </remarks>
        public static Camera AttachInterfaceView(Camera world, int layer)
        {
            if (world == null)
            {
                return null;
            }

            if (layer < 0)
            {
                return world;
            }

            world.cullingMask &= ~(1 << layer);

            Camera view = Existing(world);
            if (view == null)
            {
                var host = new GameObject(world.name + InterfaceSuffix, typeof(Camera));
                host.transform.SetParent(world.transform, false);
                view = host.GetComponent<Camera>();
            }

            view.name = world.name + InterfaceSuffix;
            view.transform.localPosition = Vector3.zero;
            view.transform.localRotation = Quaternion.identity;
            view.cullingMask = 1 << layer;
            view.rect = world.rect;
            CopyProjection(world, view);

            UniversalAdditionalCameraData data = view.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Overlay;
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;

            Stack(world, view);
            return view;
        }

        /// <summary>
        /// Returns the camera drawing one world camera's interface.
        /// </summary>
        /// <param name="world">The camera drawing the world.</param>
        /// <returns>
        /// Its interface camera, or <c>null</c> when
        /// <see cref="AttachInterfaceView"/> has not run for it.
        /// </returns>
        /// <remarks>
        /// Found by being the camera parented to this one rather than by name, because the
        /// scene builder names a camera after the side sitting in that seat and rebuilding a
        /// map with the sides the other way round would otherwise leave two of these behind.
        /// Nothing else is ever parented to a camera here.
        /// </remarks>
        public static Camera Existing(Camera world)
        {
            if (world == null)
            {
                return null;
            }

            foreach (Transform child in world.transform)
            {
                Camera view = child.GetComponent<Camera>();
                if (view != null)
                {
                    return view;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the camera an interface hanging off this world camera should draw through.
        /// </summary>
        /// <param name="world">The camera drawing the world.</param>
        /// <returns>
        /// Its interface camera, or the world camera itself when it has none - which is the
        /// same thing <see cref="AttachInterfaceView"/> answers when the project is missing
        /// the layer, and degrades to an interface that is drawn and graded rather than to
        /// one that is not drawn.
        /// </returns>
        public static Camera InterfaceView(Camera world)
        {
            Camera view = Existing(world);
            return view == null ? world : view;
        }

        /// <summary>
        /// Puts an interface camera in the same slice of the screen as the world camera it
        /// belongs to.
        /// </summary>
        /// <param name="world">The camera drawing the world.</param>
        /// <param name="viewport">The slice of the screen, in fractions.</param>
        /// <remarks>
        /// URP renders a stacked camera into the base camera's viewport whatever the overlay
        /// camera says, so this is not what puts the pixels in the right place. It is what
        /// keeps <see cref="Camera.pixelRect"/> honest, which is what a canvas sizes itself
        /// from and what a graphic raycaster decides a click landed on it by.
        /// </remarks>
        public static void SetViewport(Camera world, Rect viewport)
        {
            if (world == null)
            {
                return;
            }

            world.rect = viewport;

            Camera view = Existing(world);
            if (view != null)
            {
                view.rect = viewport;
            }
        }

        private static void CopyProjection(Camera from, Camera to)
        {
            to.orthographic = from.orthographic;
            to.orthographicSize = from.orthographicSize;
            to.fieldOfView = from.fieldOfView;
            to.nearClipPlane = from.nearClipPlane;
            to.farClipPlane = from.farClipPlane;
        }

        private static void Stack(Camera world, Camera view)
        {
            UniversalAdditionalCameraData data = world.GetUniversalAdditionalCameraData();
            if (data.cameraStack.Contains(view))
            {
                return;
            }

            data.cameraStack.Add(view);
        }
    }
}
