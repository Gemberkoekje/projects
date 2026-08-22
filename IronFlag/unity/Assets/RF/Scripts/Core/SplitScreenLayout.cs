using UnityEngine;

namespace IronFlag.Core
{
    /// <summary>
    /// Works out which slice of the screen each local player gets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two players split horizontally - one band above the other - because the map is wider
    /// than it is deep and a top-down view of it reads better in a wide letterbox than in a
    /// tall column. The first player is always on top, which is also the order the design
    /// doc's HUD panels are listed in.
    /// </para>
    /// <para>
    /// Static and side-effect free, like the camera rig's placement maths, so the layout can
    /// be checked without a screen. Nothing here touches a <see cref="Camera"/>;
    /// <see cref="TopDownCameraRig.Viewport"/> is where the result lands.
    /// </para>
    /// </remarks>
    public static class SplitScreenLayout
    {
        /// <summary>The largest number of local players a layout exists for.</summary>
        public const int MaxPlayers = 4;

        /// <summary>
        /// Returns the viewport rectangle for one player, in normalised screen coordinates.
        /// </summary>
        /// <param name="playerIndex">Zero-based player slot.</param>
        /// <param name="playerCount">How many players are sharing the screen.</param>
        /// <returns>
        /// The slice of the screen that player draws into. One player fills the screen, two
        /// split it into an upper and a lower band, and three or four take a quadrant each.
        /// </returns>
        /// <remarks>
        /// An index outside the player count, or a count outside 1..<see cref="MaxPlayers"/>,
        /// gives the full screen: a camera that is misconfigured should be plainly visible
        /// rather than invisible.
        /// </remarks>
        public static Rect ViewportFor(int playerIndex, int playerCount)
        {
            if (playerCount < 2 || playerCount > MaxPlayers || playerIndex < 0 || playerIndex >= playerCount)
            {
                return FullScreen;
            }

            if (playerCount == 2)
            {
                // Player one on top, so the split reads in the same order the players do.
                return new Rect(0.0f, playerIndex == 0 ? 0.5f : 0.0f, 1.0f, 0.5f);
            }

            float left = playerIndex % 2 == 0 ? 0.0f : 0.5f;
            float bottom = playerIndex < 2 ? 0.5f : 0.0f;
            return new Rect(left, bottom, 0.5f, 0.5f);
        }

        /// <summary>The whole screen, which is what one player on their own gets.</summary>
        public static Rect FullScreen => new Rect(0.0f, 0.0f, 1.0f, 1.0f);

        /// <summary>
        /// Reports whether a point on the screen falls inside a viewport.
        /// </summary>
        /// <param name="viewport">Viewport in normalised coordinates.</param>
        /// <param name="screenPoint">Point in pixels, with the origin at the bottom left.</param>
        /// <param name="screenSize">Size of the whole screen in pixels.</param>
        /// <returns><c>true</c> when the point is inside the viewport.</returns>
        public static bool Contains(Rect viewport, Vector2 screenPoint, Vector2 screenSize)
            => PixelRect(viewport, screenSize).Contains(screenPoint);

        /// <summary>
        /// Pulls a point on the screen back inside a viewport.
        /// </summary>
        /// <param name="viewport">Viewport in normalised coordinates.</param>
        /// <param name="screenPoint">Point in pixels, with the origin at the bottom left.</param>
        /// <param name="screenSize">Size of the whole screen in pixels.</param>
        /// <returns>The nearest point inside the viewport, in pixels.</returns>
        /// <remarks>
        /// The mouse belongs to one player but roams the whole window, and a pointer sitting
        /// in the other player's half would aim the turret at somewhere its owner cannot
        /// see. Clamping keeps the aim at the edge of what the player is actually looking
        /// at, which is what a turret pushed against its limits should do.
        /// </remarks>
        public static Vector2 ClampToViewport(Rect viewport, Vector2 screenPoint, Vector2 screenSize)
        {
            Rect pixels = PixelRect(viewport, screenSize);
            return new Vector2(
                Mathf.Clamp(screenPoint.x, pixels.xMin, pixels.xMax),
                Mathf.Clamp(screenPoint.y, pixels.yMin, pixels.yMax));
        }

        /// <summary>
        /// Converts a normalised viewport into pixels.
        /// </summary>
        /// <param name="viewport">Viewport in normalised coordinates.</param>
        /// <param name="screenSize">Size of the whole screen in pixels.</param>
        /// <returns>The same rectangle in pixels, with the origin at the bottom left.</returns>
        public static Rect PixelRect(Rect viewport, Vector2 screenSize)
            => new Rect(
                viewport.x * screenSize.x,
                viewport.y * screenSize.y,
                viewport.width * screenSize.x,
                viewport.height * screenSize.y);
    }
}
