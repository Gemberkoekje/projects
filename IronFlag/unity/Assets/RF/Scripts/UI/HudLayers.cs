using UnityEngine;
using IronFlag.Core;

namespace IronFlag.UI
{
    /// <summary>
    /// One layer per player, so that one player's HUD is drawn into one player's half of
    /// the screen and nowhere else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A screen-space canvas bound to a camera is not really flat: it is a sheet of geometry
    /// hanging a metre in front of that camera, and any other camera close enough to see it
    /// draws it too. In split screen that is not a corner case - it is what happens the
    /// moment both players drive to the same place, which in a game about capturing a flag
    /// is most of the match. The first version of this had no layers and the bug looked like
    /// the other player's fuel gauge sliding across your windscreen.
    /// </para>
    /// <para>
    /// So each player's HUD gets a layer of its own and each player's camera is told not to
    /// draw anybody else's. The layers are created by the scene builder; when they are
    /// missing this degrades to every HUD on the default layer, which is visible rather than
    /// silent.
    /// </para>
    /// </remarks>
    public static class HudLayers
    {
        /// <summary>How many per-player HUD layers exist, which is the seat limit.</summary>
        public const int Count = SplitScreenLayout.MaxPlayers;

        /// <summary>
        /// Returns the name of one player's HUD layer.
        /// </summary>
        /// <param name="slot">Zero-based player slot.</param>
        /// <returns>The layer name, as it appears in the tag manager.</returns>
        public static string NameFor(int slot) => $"Hud{slot + 1}";

        /// <summary>
        /// Returns one player's HUD layer.
        /// </summary>
        /// <param name="slot">Zero-based player slot.</param>
        /// <returns>The layer index, or -1 when the project has no such layer.</returns>
        public static int LayerFor(int slot) => LayerMask.NameToLayer(NameFor(slot));

        /// <summary>
        /// Returns the culling mask for one player's camera.
        /// </summary>
        /// <param name="slot">Zero-based player slot.</param>
        /// <returns>Everything, less every other player's HUD layer.</returns>
        /// <remarks>
        /// Subtractive rather than additive on purpose: a camera that had to be told about
        /// each new kind of scenery would eventually stop drawing some of it, and the only
        /// thing a camera here genuinely must not see is another player's instruments.
        /// </remarks>
        public static int CullingMaskFor(int slot)
        {
            int mask = ~0;

            for (int other = 0; other < Count; other++)
            {
                if (other == slot)
                {
                    continue;
                }

                int layer = LayerFor(other);
                if (layer >= 0)
                {
                    mask &= ~(1 << layer);
                }
            }

            return mask;
        }
    }
}
