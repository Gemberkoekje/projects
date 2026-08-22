using UnityEngine;
using UnityEngine.UI;
using IronFlag.Core;

namespace IronFlag.UI
{
    /// <summary>
    /// The whole look of the HUD in one place: its colours, its typeface, and the three
    /// shapes everything on it is made of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The HUD is built from code rather than authored as a prefab, for the same reason the
    /// scenes and the vehicle prefabs are: what a player's panel contains follows
    /// mechanically from their roster, and a generated one picks up a fifth vehicle by
    /// being rebuilt. That only works if there is one file deciding what it all looks like,
    /// which is this one.
    /// </para>
    /// <para>
    /// The team colours here are not the ones the vehicles are painted with. Paint is a
    /// surface under a sunlit sky and a HUD accent is a flat colour on a dark panel, and the
    /// green that reads correctly on a hull is nearly black on glass. Same two hues,
    /// brightened, on purpose.
    /// </para>
    /// </remarks>
    public static class HudPalette
    {
        /// <summary>Backing plate behind a panel: dark, and dark enough to read against.</summary>
        /// <remarks>
        /// Not as transparent as it first was. The bunker screen is parked over a sunlit
        /// concrete roof, and at three-quarters the roof came through the panel and sat
        /// behind every row of the roster.
        /// </remarks>
        public static readonly Color Panel = new Color(0.05f, 0.06f, 0.07f, 0.88f);

        /// <summary>The row the player is currently on.</summary>
        public static readonly Color Highlight = new Color(0.16f, 0.19f, 0.22f, 0.92f);

        /// <summary>Anything readable: labels, names, numbers.</summary>
        public static readonly Color Ink = new Color(0.90f, 0.92f, 0.93f, 1.0f);

        /// <summary>Text that is present but not the point: units, hints, empty rows.</summary>
        public static readonly Color FadedInk = new Color(0.62f, 0.65f, 0.68f, 1.0f);

        /// <summary>Something is ready, full, or going well.</summary>
        public static readonly Color Good = new Color(0.44f, 0.78f, 0.40f, 1.0f);

        /// <summary>Something is running out or is not available yet.</summary>
        public static readonly Color Warning = new Color(0.93f, 0.70f, 0.24f, 1.0f);

        /// <summary>Something has run out.</summary>
        public static readonly Color Alarm = new Color(0.86f, 0.30f, 0.24f, 1.0f);

        /// <summary>The empty part of any bar.</summary>
        public static readonly Color Track = new Color(0.14f, 0.15f, 0.16f, 0.85f);

        /// <summary>Hit points remaining.</summary>
        public static readonly Color Armour = new Color(0.84f, 0.36f, 0.30f, 1.0f);

        /// <summary>Fuel remaining.</summary>
        public static readonly Color Fuel = new Color(0.94f, 0.74f, 0.30f, 1.0f);

        /// <summary>Ammunition remaining.</summary>
        public static readonly Color Ammunition = new Color(0.44f, 0.68f, 0.92f, 1.0f);

        /// <summary>The green side, brightened for a screen rather than for a hull.</summary>
        private static readonly Color GreenAccent = new Color(0.47f, 0.80f, 0.42f, 1.0f);

        /// <summary>The brown side, brightened for a screen rather than for a hull.</summary>
        private static readonly Color BrownAccent = new Color(0.87f, 0.62f, 0.33f, 1.0f);

        /// <summary>Built-in font the HUD is set in, found once and shared.</summary>
        private static Font face;

        /// <summary>
        /// The typeface the whole HUD is set in.
        /// </summary>
        /// <remarks>
        /// Unity's own built-in font, which needs no asset in the project and no import
        /// step. It was called <c>Arial.ttf</c> until 2022 and is called
        /// <c>LegacyRuntime.ttf</c> now, so both names are tried: a HUD with no font renders
        /// as empty boxes, which is a long way from where anybody would start looking.
        /// </remarks>
        public static Font Face
        {
            get
            {
                if (face == null)
                {
                    face = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                        ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
                }

                return face;
            }
        }

        /// <summary>
        /// Returns the accent colour of one side.
        /// </summary>
        /// <param name="side">Side to colour.</param>
        /// <returns>That side's accent, or plain ink for no side at all.</returns>
        public static Color For(Team side)
        {
            switch (side)
            {
                case Team.Green:
                    return GreenAccent;
                case Team.Brown:
                    return BrownAccent;
                default:
                    return Ink;
            }
        }

        /// <summary>
        /// Returns the colour a pool should be drawn in at a given level.
        /// </summary>
        /// <param name="full">The colour when there is plenty left.</param>
        /// <param name="fraction">How much is left, in 0..1.</param>
        /// <returns>The pool's own colour, or the alarm colour once it is nearly gone.</returns>
        /// <remarks>
        /// A bar that only gets shorter is a bar nobody looks at until it is empty. The
        /// switch is at a fifth, which for the helicopter's tank is about fifteen seconds of
        /// flying - roughly the trip home from the middle of the map.
        /// </remarks>
        public static Color Level(Color full, float fraction)
            => fraction <= 0.2f ? Alarm : full;

        /// <summary>
        /// Creates a coloured rectangle.
        /// </summary>
        /// <param name="name">Object name, which is what a hierarchy is read by.</param>
        /// <param name="parent">Object to hang it off.</param>
        /// <param name="color">Colour to fill it with.</param>
        /// <returns>The image, with its rect transform ready to be positioned.</returns>
        public static Image Box(string name, Transform parent, Color color)
        {
            var host = new GameObject(name, typeof(RectTransform), typeof(Image));
            host.transform.SetParent(parent, false);

            var image = host.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// Creates a line of text.
        /// </summary>
        /// <param name="name">Object name.</param>
        /// <param name="parent">Object to hang it off.</param>
        /// <param name="size">Type size in canvas units.</param>
        /// <param name="alignment">How the line sits in its own rectangle.</param>
        /// <returns>The text, with its rect transform ready to be positioned.</returns>
        /// <remarks>
        /// Text is not allowed to wrap or to shrink itself. Every string on this HUD is a
        /// word or a number that has been sized to fit, and a label that quietly reflows is
        /// one that has changed meaning without saying so.
        /// </remarks>
        public static Text Label(string name, Transform parent, int size, TextAnchor alignment)
        {
            var host = new GameObject(name, typeof(RectTransform), typeof(Text));
            host.transform.SetParent(parent, false);

            var text = host.GetComponent<Text>();
            text.font = Face;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Ink;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>
        /// Places a rectangle by its corners, in canvas units measured from its parent.
        /// </summary>
        /// <param name="rect">Rectangle to place.</param>
        /// <param name="left">Distance from the parent's left edge.</param>
        /// <param name="bottom">Distance from the parent's bottom edge.</param>
        /// <param name="width">Width in canvas units.</param>
        /// <param name="height">Height in canvas units.</param>
        /// <remarks>
        /// Everything on this HUD is anchored to the bottom left of its parent and given a
        /// size, which is the one layout rule here. Anchors that stretch would let a panel
        /// reflow between the two halves of a split screen, and a HUD that is a different
        /// shape for each player is one nobody can be taught once.
        /// </remarks>
        public static void Place(RectTransform rect, float left, float bottom, float width, float height)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(left, bottom);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
