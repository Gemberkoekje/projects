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

        /// <summary>Folder inside <c>Resources</c> the two typefaces are loaded from.</summary>
        /// <remarks>
        /// <c>Resources</c> rather than a reference on a component, because the two classes
        /// that need a font - this one and <see cref="IronFlag.Editing.EditorTheme"/> - are
        /// static, and a static class cannot be handed an asset by a scene. It is the only
        /// <c>Resources</c> folder in the project and it holds nothing but the fonts and the
        /// licence they arrived under, which ships with them because the licence says it must.
        /// </remarks>
        private const string TypeFolder = "Fonts/";

        /// <summary>File name of the face everything readable is set in.</summary>
        public const string BodyFaceName = "SairaCondensed-SemiBold";

        /// <summary>File name of the face the game's few headlines are set in.</summary>
        public const string DisplayFaceName = "SairaStencilOne-Regular";

        /// <summary>The face everything readable is set in, found once and shared.</summary>
        private static Font face;

        /// <summary>The face the headlines are set in, found once and shared.</summary>
        private static Font displayFace;

        /// <summary>
        /// The typeface everything readable on this interface is set in.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Saira Condensed SemiBold. Condensed because nearly every string here is a word in
        /// capitals or a pair of numbers with a slash between them, and a condensed face fits
        /// those in a column narrow enough to leave the game visible behind it. SemiBold
        /// because this is white type on dark glass over a sunlit map, where a regular weight
        /// goes thin and grey; the interface's secondary hierarchy is carried by
        /// <see cref="FadedInk"/> rather than by a second weight, which is one asset fewer and
        /// one decision fewer.
        /// </para>
        /// <para>
        /// Its numerals are the reason it was chosen over the alternatives. Three of the four
        /// gauges on this HUD are a number over another number, and Saira's figures are
        /// lining, even in width, and have a slashed zero - which is what a reading glanced at
        /// mid-corner needs and what most condensed display faces do worst.
        /// </para>
        /// </remarks>
        public static Font Face
        {
            get
            {
                if (face == null)
                {
                    face = Load(BodyFaceName);
                }

                return face;
            }
        }

        /// <summary>
        /// The typeface the handful of headlines are set in.
        /// </summary>
        /// <remarks>
        /// Saira Stencil One - the same superfamily as <see cref="Face"/>, so the two agree
        /// by construction rather than by somebody's eye. It is used in exactly three places:
        /// VICTORY and DEFEAT, the game's name on the main menu, and the word at the top of
        /// the pause panel. A stencil face is a sign painted on a vehicle, which is the right
        /// voice for a title and completely the wrong one for a fuel gauge, so it is
        /// deliberately never available to anything that carries a number.
        /// </remarks>
        public static Font DisplayFace
        {
            get
            {
                if (displayFace == null)
                {
                    displayFace = Load(DisplayFaceName);
                }

                return displayFace;
            }
        }

        /// <summary>
        /// Finds one of the typefaces, or something legible if it is missing.
        /// </summary>
        /// <param name="name">File name of the face, without its extension.</param>
        /// <returns>The face, or Unity's built-in one when the project has no such asset.</returns>
        /// <remarks>
        /// The fallback is the font this interface was set in before it had one of its own.
        /// It was called <c>Arial.ttf</c> until 2022 and is called <c>LegacyRuntime.ttf</c>
        /// now, so both names are tried. Falling back rather than failing is deliberate: the
        /// wrong typeface is a thing anybody can see and diagnose, and a HUD with no font at
        /// all renders as empty boxes, which is a long way from where anybody would start
        /// looking.
        /// </remarks>
        private static Font Load(string name)
        {
            var found = Resources.Load<Font>(TypeFolder + name);
            if (found != null)
            {
                return found;
            }

            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
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

        /// <summary>How little of a pool has to be left before it is an alarm.</summary>
        /// <remarks>
        /// A fifth, which for the helicopter's tank is about fifteen seconds of flying -
        /// roughly the trip home from the middle of the map.
        /// </remarks>
        public const float AlarmFraction = 0.2f;

        /// <summary>
        /// Says whether a pool is low enough to be shouting about.
        /// </summary>
        /// <param name="fraction">How much is left, in 0..1.</param>
        /// <returns>Whether it is nearly gone.</returns>
        public static bool IsAlarming(float fraction) => fraction <= AlarmFraction;

        /// <summary>
        /// Returns the colour a pool should be drawn in at a given level.
        /// </summary>
        /// <param name="full">The colour when there is plenty left.</param>
        /// <param name="fraction">How much is left, in 0..1.</param>
        /// <returns>The pool's own colour, or the alarm colour once it is nearly gone.</returns>
        /// <remarks>
        /// A bar that only gets shorter is a bar nobody looks at until it is empty. Turning it
        /// red is half of the answer and <see cref="HudMotion.Flash"/> is the other half: a
        /// colour is only a warning to somebody already looking at it, and the whole problem
        /// with a fuel gauge is that nobody is.
        /// </remarks>
        public static Color Level(Color full, float fraction)
            => IsAlarming(fraction) ? Alarm : full;

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
        /// Creates a panel's backing plate: dark glass, vignetted, faintly scanlined.
        /// </summary>
        /// <param name="name">Object name, which is what a hierarchy is read by.</param>
        /// <param name="parent">Object to hang it off.</param>
        /// <param name="color">Colour of the glass.</param>
        /// <returns>The plate, with its rect transform ready to be positioned.</returns>
        /// <remarks>
        /// What <see cref="Box"/> used to be used for and no longer should be. A box is a
        /// flat rectangle and is still the right thing for the small solid parts of a panel -
        /// a bar's track, a selected row, a rule - while a plate is the surface a panel is
        /// <em>made of</em>. See <see cref="HudPlate"/> for what the difference buys.
        /// </remarks>
        public static HudPlate Plate(string name, Transform parent, Color color)
        {
            var host = new GameObject(name, typeof(RectTransform), typeof(HudPlate));
            host.transform.SetParent(parent, false);

            var plate = host.GetComponent<HudPlate>();
            plate.color = color;
            plate.raycastTarget = false;
            return plate;
        }

        /// <summary>
        /// Creates the four corner marks around a panel, stretched to fit it.
        /// </summary>
        /// <param name="name">Object name.</param>
        /// <param name="parent">The panel to frame.</param>
        /// <param name="color">Colour of the marks, which is usually a side's accent.</param>
        /// <returns>The bracket, already filling its parent.</returns>
        /// <remarks>
        /// Placed here rather than by the caller, because a bracket that is not exactly the
        /// size of the thing it frames is not a frame - it is four marks near a panel. It is
        /// the one thing on this interface that is anchored to stretch rather than given a
        /// size, and the exception is what makes it impossible to get wrong.
        /// </remarks>
        public static HudBracket Bracket(string name, Transform parent, Color color)
        {
            var host = new GameObject(name, typeof(RectTransform), typeof(HudBracket));
            host.transform.SetParent(parent, false);

            var bracket = host.GetComponent<HudBracket>();
            bracket.color = color;
            bracket.raycastTarget = false;

            RectTransform rect = bracket.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return bracket;
        }

        /// <summary>
        /// Creates one of the interface's drawn marks.
        /// </summary>
        /// <param name="name">Object name.</param>
        /// <param name="parent">Object to hang it off.</param>
        /// <param name="kind">Which mark to draw.</param>
        /// <param name="color">Colour to draw it in.</param>
        /// <returns>The glyph, with its rect transform ready to be positioned.</returns>
        public static HudGlyph Glyph(string name, Transform parent, HudGlyphKind kind, Color color)
        {
            var host = new GameObject(name, typeof(RectTransform), typeof(HudGlyph));
            host.transform.SetParent(parent, false);

            var glyph = host.GetComponent<HudGlyph>();
            glyph.Kind = kind;
            glyph.color = color;
            glyph.raycastTarget = false;
            return glyph;
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
        /// Creates a line of text in the headline face.
        /// </summary>
        /// <param name="name">Object name.</param>
        /// <param name="parent">Object to hang it off.</param>
        /// <param name="size">Type size in canvas units.</param>
        /// <param name="alignment">How the line sits in its own rectangle.</param>
        /// <returns>The text, with its rect transform ready to be positioned.</returns>
        /// <remarks>
        /// A separate call rather than an argument on <see cref="Label"/>, so that the three
        /// places in the game entitled to a stencil are three call sites somebody can find,
        /// rather than a flag anything could pass. See <see cref="DisplayFace"/>.
        /// </remarks>
        public static Text Headline(string name, Transform parent, int size, TextAnchor alignment)
        {
            Text text = Label(name, parent, size, alignment);
            text.font = DisplayFace;
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
