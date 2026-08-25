using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using IronFlag.Core;
using IronFlag.UI;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// Checks that a world camera runs post-processing and that the interface hanging off it
    /// does not.
    /// </summary>
    /// <remarks>
    /// Both halves fail silently and look like the other thing. A world camera that was never
    /// told to run post looks exactly like a volume profile tuned to do nothing - which is the
    /// state this project was in for nine milestones. An interface camera that was told to run
    /// it looks exactly like a HUD whose colours were picked badly.
    /// </remarks>
    public sealed class ViewStackTests
    {
        private GameObject host;

        [TearDown]
        public void TearDown()
        {
            if (host != null)
            {
                Object.DestroyImmediate(host);
                host = null;
            }
        }

        private Camera World()
        {
            host = new GameObject("Test Camera", typeof(Camera));
            return host.GetComponent<Camera>();
        }

        [Test]
        public void AWorldCameraRunsThePostProcessing()
        {
            Camera world = World();
            ViewStack.MakeWorldView(world);

            UniversalAdditionalCameraData data = world.GetUniversalAdditionalCameraData();
            Assert.That(data.renderPostProcessing, Is.True);
            Assert.That(data.renderType, Is.EqualTo(CameraRenderType.Base));
            Assert.That(data.antialiasing, Is.EqualTo(PostTuning.Antialiasing));
        }

        [Test]
        public void TheInterfaceCameraDoesNot()
        {
            Camera world = World();
            ViewStack.MakeWorldView(world);
            Camera view = ViewStack.AttachInterfaceView(world, 9);

            Assert.That(view, Is.Not.SameAs(world));

            UniversalAdditionalCameraData data = view.GetUniversalAdditionalCameraData();
            Assert.That(data.renderPostProcessing, Is.False);
            Assert.That(data.renderType, Is.EqualTo(CameraRenderType.Overlay));
        }

        [Test]
        public void TheInterfaceCameraIsStackedOnTheWorldCamera()
        {
            Camera world = World();
            ViewStack.MakeWorldView(world);
            Camera view = ViewStack.AttachInterfaceView(world, 9);

            Assert.That(
                world.GetUniversalAdditionalCameraData().cameraStack,
                Contains.Item(view),
                "an overlay camera outside the stack is never rendered at all");
        }

        /// <summary>
        /// The half that is easy to miss: a world camera still drawing the interface layer
        /// draws it a second time, graded, underneath the ungraded copy.
        /// </summary>
        [Test]
        public void TheWorldCameraStopsDrawingTheInterfaceLayer()
        {
            Camera world = World();
            world.cullingMask = ~0;
            ViewStack.AttachInterfaceView(world, 9);

            Assert.That(world.cullingMask & (1 << 9), Is.Zero);
            Assert.That(
                ViewStack.AttachInterfaceView(world, 9).cullingMask,
                Is.EqualTo(1 << 9),
                "the interface camera draws the interface and nothing else");
        }

        /// <summary>
        /// The scene builders are menu items somebody presses again every time a map changes.
        /// </summary>
        [Test]
        public void BuildingTwiceLeavesOneInterfaceCamera()
        {
            Camera world = World();
            ViewStack.MakeWorldView(world);

            Camera first = ViewStack.AttachInterfaceView(world, 9);
            Camera second = ViewStack.AttachInterfaceView(world, 9);

            Assert.That(second, Is.SameAs(first));
            Assert.That(world.transform.childCount, Is.EqualTo(1));
            Assert.That(world.GetUniversalAdditionalCameraData().cameraStack.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Renaming happens because the sandbox names a camera after the side in that seat,
        /// and rebuilding with the sides swapped must not leave a second one behind.
        /// </summary>
        [Test]
        public void RenamingTheWorldCameraDoesNotGrowASecondInterfaceCamera()
        {
            Camera world = World();
            ViewStack.MakeWorldView(world);
            ViewStack.AttachInterfaceView(world, 9);

            world.name = "Camera 1 (Brown)";
            ViewStack.AttachInterfaceView(world, 9);

            Assert.That(world.transform.childCount, Is.EqualTo(1));
        }

        /// <summary>
        /// A project missing the layer should draw a graded interface rather than none.
        /// </summary>
        [Test]
        public void AMissingLayerLeavesTheInterfaceOnTheWorldCamera()
        {
            Camera world = World();
            world.cullingMask = ~0;

            Assert.That(ViewStack.AttachInterfaceView(world, -1), Is.SameAs(world));
            Assert.That(world.cullingMask, Is.EqualTo(~0));
            Assert.That(ViewStack.InterfaceView(world), Is.SameAs(world));
        }

        /// <summary>
        /// A seat that changed size and left its instruments measuring the old one is a HUD
        /// laid out for half a screen on a quarter of one.
        /// </summary>
        [Test]
        public void ResizingASeatMovesItsInterfaceWithIt()
        {
            Camera world = World();
            ViewStack.MakeWorldView(world);
            Camera view = ViewStack.AttachInterfaceView(world, 9);

            var half = new Rect(0.0f, 0.5f, 1.0f, 0.5f);
            ViewStack.SetViewport(world, half);

            Assert.That(world.rect, Is.EqualTo(half));
            Assert.That(view.rect, Is.EqualTo(half));
        }

        [Test]
        public void TheInterfaceCameraCopiesTheWorldCameraProjection()
        {
            Camera world = World();
            world.fieldOfView = 50.0f;
            world.nearClipPlane = 0.3f;
            world.farClipPlane = 500.0f;

            Camera view = ViewStack.AttachInterfaceView(world, 9);

            Assert.That(view.fieldOfView, Is.EqualTo(50.0f).Within(0.0005f));
            Assert.That(view.nearClipPlane, Is.EqualTo(0.3f).Within(0.0005f));
            Assert.That(
                view.transform.localPosition,
                Is.EqualTo(Vector3.zero),
                "a canvas hangs at a fixed distance in front of its camera");
        }

        [Test]
        public void EverySeatHasAnInterfaceLayerOfItsOwn()
        {
            for (int slot = 0; slot < InterfaceLayers.Count; slot++)
            {
                Assert.That(
                    InterfaceLayers.LayerFor(slot),
                    Is.GreaterThanOrEqualTo(0),
                    $"the project has no {InterfaceLayers.NameFor(slot)} layer; "
                        + "run Tools > IronFlag > Build Vehicle Sandbox Scene");
            }

            Assert.That(
                InterfaceLayers.EditorLayer(),
                Is.GreaterThanOrEqualTo(0),
                $"the project has no {InterfaceLayers.EditorName} layer");
        }
    }
}
