using UnityEngine;
using IronFlag.Core;

namespace IronFlag.UI
{
    /// <summary>
    /// Which layers hold interface rather than world: one per seat for the players'
    /// instruments, and one for the level editor's panels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A screen-space canvas is geometry in the world - see
    /// <see cref="IronFlag.Core.ViewStack"/> for why this project cannot use the overlay
    /// canvas that would not be - so without a layer per player, one player's instruments
    /// turn up in the other player's view the moment their cameras get close. The layers are
    /// created by the scene builder; when they are missing this degrades to every canvas on
    /// the default layer, which is visible rather than silent.
    /// </para>
    /// <para>
    /// Since post-processing arrived these layers carry a second job, which is why this class
    /// no longer says "HUD". Interface is drawn by a camera stacked on the world camera with
    /// the grade switched off, so what separates interface from world is no longer only whose
    /// screen it belongs on - it is whether a tone curve is allowed to touch it. The level
    /// editor's panels want the second thing and have no use for the first, which is why they
    /// are here too, on <see cref="EditorName"/>.
    /// </para>
    /// <para>
    /// The editor's layer is Unity's own built-in <c>UI</c> rather than a fifth generated one,
    /// because it already exists in every project, it already means this, and a layer that
    /// does not have to be created is a layer that cannot fail to be.
    /// </para>
    /// </remarks>
    public static class InterfaceLayers
    {
        /// <summary>How many per-player interface layers exist, which is the seat limit.</summary>
        public const int Count = SplitScreenLayout.MaxPlayers;

        /// <summary>Name of the layer the level editor's panels draw on.</summary>
        public const string EditorName = "UI";

        /// <summary>
        /// Returns the name of one player's interface layer.
        /// </summary>
        /// <param name="slot">Zero-based player slot.</param>
        /// <returns>The layer name, as it appears in the tag manager.</returns>
        public static string NameFor(int slot) => $"Hud{slot + 1}";

        /// <summary>
        /// Returns one player's interface layer.
        /// </summary>
        /// <param name="slot">Zero-based player slot.</param>
        /// <returns>The layer index, or -1 when the project has no such layer.</returns>
        public static int LayerFor(int slot) => LayerMask.NameToLayer(NameFor(slot));

        /// <summary>
        /// Returns the layer the level editor's panels draw on.
        /// </summary>
        /// <returns>The layer index, or -1 when the project has no such layer.</returns>
        public static int EditorLayer() => LayerMask.NameToLayer(EditorName);

        /// <summary>
        /// Returns the culling mask for a camera that draws the world.
        /// </summary>
        /// <returns>Everything, less every interface layer.</returns>
        /// <remarks>
        /// <para>
        /// Subtractive rather than additive on purpose: a camera that had to be told about
        /// each new kind of scenery would eventually stop drawing some of it, and the only
        /// thing a world camera genuinely must not see is an interface.
        /// </para>
        /// <para>
        /// Every interface layer rather than only the other players', which is the one thing
        /// that changed when the interface moved onto its own camera. A world camera that
        /// still drew its own player's HUD would draw it a second time, through the grade,
        /// underneath the ungraded copy - which reads as a HUD with a coloured ghost behind
        /// it rather than as anything obviously wrong.
        /// </para>
        /// </remarks>
        public static int WorldMask()
        {
            int mask = ~0;

            for (int slot = 0; slot < Count; slot++)
            {
                mask &= Without(LayerFor(slot));
            }

            return mask & Without(EditorLayer());
        }

        /// <summary>
        /// Puts everything hanging off a canvas onto the canvas's own layer.
        /// </summary>
        /// <param name="root">The canvas whose subtree to paint.</param>
        /// <remarks>
        /// Generated objects arrive on the default layer, and one label left behind is one
        /// label that the world camera draws and the interface camera does not - so it is
        /// graded, in the other player's half of the screen, or both. Every canvas here
        /// rebuilds its children at runtime, so this is called after each rebuild rather than
        /// once at construction.
        /// </remarks>
        public static void Paint(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
            {
                part.gameObject.layer = root.layer;
            }
        }

        private static int Without(int layer) => layer < 0 ? ~0 : ~(1 << layer);
    }
}
