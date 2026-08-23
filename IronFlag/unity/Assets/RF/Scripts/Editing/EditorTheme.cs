using System;
using UnityEngine;
using UnityEngine.UI;
using IronFlag.Core;
using IronFlag.UI;

namespace IronFlag.Editing
{
    /// <summary>
    /// The whole look of the level editor's panels, and the four widgets they are made of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same arrangement as <see cref="HudPalette"/>, whose colours and typeface this
    /// borrows: one file decides what the thing looks like, and everything else asks for a
    /// button rather than for an image, a text and a component. The HUD and the editor are
    /// deliberately the same dark plates and the same type - they are two panels of one game
    /// - but they are not the same file, because a HUD is read at a glance mid-corner and an
    /// editor is read while sitting still, and the two will drift.
    /// </para>
    /// <para>
    /// The one thing here the HUD has no use for is <em>raycasting</em>.
    /// <see cref="HudPalette.Box"/> turns it off on everything it makes, which is right for a
    /// HUD nobody clicks and exactly wrong here: an editor panel has to swallow the click
    /// that lands on it, or pressing Save also places a tree behind the button. So every
    /// plate this file makes is a raycast target, and
    /// <see cref="LevelEditorSession"/> asks the event system whether the pointer is over one
    /// before it touches the map.
    /// </para>
    /// </remarks>
    public static class EditorTheme
    {
        /// <summary>Backing plate behind a panel.</summary>
        /// <remarks>
        /// Nearly opaque, unlike the HUD's. A HUD panel is over a game somebody is trying to
        /// watch; an editor panel is over a map, and a coastline showing faintly through a
        /// list of level names helps nobody read either.
        /// </remarks>
        public static readonly Color Chrome = new Color(0.07f, 0.08f, 0.09f, 0.96f);

        /// <summary>A heading strip inside a panel.</summary>
        public static readonly Color Header = new Color(0.13f, 0.15f, 0.17f, 1.0f);

        /// <summary>A button nobody has chosen.</summary>
        public static readonly Color ButtonFace = new Color(0.18f, 0.20f, 0.23f, 1.0f);

        /// <summary>The button that is the current choice.</summary>
        public static readonly Color ButtonChosen = new Color(0.62f, 0.72f, 0.82f, 1.0f);

        /// <summary>A text field's trough.</summary>
        public static readonly Color FieldFace = new Color(0.11f, 0.12f, 0.14f, 1.0f);

        /// <summary>Anything readable.</summary>
        public static readonly Color Ink = HudPalette.Ink;

        /// <summary>Text on the button that is the current choice.</summary>
        public static readonly Color ChosenInk = new Color(0.06f, 0.07f, 0.08f, 1.0f);

        /// <summary>Text on a button that cannot be pressed.</summary>
        public static readonly Color DisabledInk = new Color(0.42f, 0.44f, 0.46f, 1.0f);

        /// <summary>Text that is present but not the point.</summary>
        public static readonly Color FadedInk = HudPalette.FadedInk;

        /// <summary>Something is wrong with the map.</summary>
        public static readonly Color Problem = HudPalette.Warning;

        /// <summary>Nothing is wrong with the map.</summary>
        public static readonly Color Clean = HudPalette.Good;

        /// <summary>There are changes that have not been saved.</summary>
        public static readonly Color Unsaved = HudPalette.Alarm;

        /// <summary>What is selected on the map, drawn on the overlay.</summary>
        public static readonly Color Selected = new Color(1.0f, 0.85f, 0.30f, 1.0f);

        /// <summary>The rectangle a drag is describing, drawn on the overlay.</summary>
        public static readonly Color Drawn = new Color(0.55f, 0.85f, 1.0f, 0.95f);

        /// <summary>The middle of the map, drawn on the overlay.</summary>
        /// <remarks>
        /// Faint on purpose. Every map in this game is built in pairs rotated half a turn
        /// about the origin, so where the origin is matters constantly - and a bright cross
        /// through the middle of the picture would be the loudest thing on a map shot nobody
        /// wants it in.
        /// </remarks>
        public static readonly Color Origin = new Color(0.75f, 0.78f, 0.82f, 0.32f);

        /// <summary>Where a selected thing's opposite number would stand.</summary>
        public static readonly Color Twin = new Color(0.55f, 0.85f, 1.0f, 0.45f);

        /// <summary>
        /// Creates a panel: a plate that swallows the clicks that land on it.
        /// </summary>
        /// <param name="name">Object name, which is what a hierarchy is read by.</param>
        /// <param name="parent">Object to hang it off.</param>
        /// <param name="color">Colour to fill it with.</param>
        /// <returns>The image, with its rect transform ready to be positioned.</returns>
        public static Image Plate(string name, Transform parent, Color color)
        {
            var host = new GameObject(name, typeof(RectTransform), typeof(Image));
            host.transform.SetParent(parent, false);

            var image = host.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = true;
            return image;
        }

        /// <summary>
        /// Creates a line of text, which clicks pass straight through.
        /// </summary>
        /// <param name="name">Object name.</param>
        /// <param name="parent">Object to hang it off.</param>
        /// <param name="size">Type size in canvas units.</param>
        /// <param name="alignment">How the line sits in its own rectangle.</param>
        /// <returns>The text.</returns>
        public static Text Label(string name, Transform parent, int size, TextAnchor alignment)
            => HudPalette.Label(name, parent, size, alignment);

        /// <summary>
        /// Creates a line of text that wraps rather than overflowing.
        /// </summary>
        /// <param name="name">Object name.</param>
        /// <param name="parent">Object to hang it off.</param>
        /// <param name="size">Type size in canvas units.</param>
        /// <returns>The text.</returns>
        /// <remarks>
        /// For the problem list, which is the one thing in this editor whose text is written
        /// by <see cref="IronFlag.Levels.LevelValidation"/> in whole sentences rather than
        /// sized to fit a box. A rule that ran off the edge of the panel would be a rule
        /// nobody could act on.
        /// </remarks>
        public static Text Paragraph(string name, Transform parent, int size)
        {
            Text text = HudPalette.Label(name, parent, size, TextAnchor.UpperLeft);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        /// <summary>
        /// Creates a button.
        /// </summary>
        /// <param name="name">Object name.</param>
        /// <param name="parent">Object to hang it off.</param>
        /// <param name="words">What it says.</param>
        /// <param name="size">Type size in canvas units.</param>
        /// <param name="does">What pressing it does.</param>
        /// <returns>The button, its plate and its caption.</returns>
        public static EditorButton Button(
            string name, Transform parent, string words, int size, Action does)
        {
            Image plate = Plate(name, parent, ButtonFace);
            var control = plate.gameObject.AddComponent<Button>();
            control.targetGraphic = plate;

            ColorBlock colors = control.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1.0f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1.0f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 1.0f);
            colors.fadeDuration = 0.05f;
            control.colors = colors;

            Text caption = Label($"{name} Caption", plate.transform, size, TextAnchor.MiddleCenter);
            caption.text = words == null ? string.Empty : words;
            Fill(caption.rectTransform, 6.0f);

            var button = new EditorButton(control, plate, caption);
            button.OnPress(does);
            button.SetChosen(false);
            return button;
        }

        /// <summary>
        /// Creates a text field.
        /// </summary>
        /// <param name="name">Object name.</param>
        /// <param name="parent">Object to hang it off.</param>
        /// <param name="size">Type size in canvas units.</param>
        /// <param name="done">What to do with the text once the player leaves the field.</param>
        /// <returns>The field.</returns>
        /// <remarks>
        /// <para>
        /// Wired on <c>onEndEdit</c> rather than <c>onValueChanged</c>, which matters more
        /// here than it looks. Every change to a level rebuilds the map and adds an undo step,
        /// so a field that reported each keystroke would rebuild the world eleven times while
        /// somebody typed "Iron Channel" and leave eleven steps in the history to get back
        /// through.
        /// </para>
        /// <para>
        /// Unity's legacy <see cref="InputField"/> rather than TextMeshPro's, because this
        /// project has no font assets and no TMP essentials imported - and the HUD is already
        /// set in the built-in font for the same reason.
        /// </para>
        /// </remarks>
        public static InputField Field(string name, Transform parent, int size, Action<string> done)
        {
            Image plate = Plate(name, parent, FieldFace);

            Text text = Label($"{name} Text", plate.transform, size, TextAnchor.MiddleLeft);
            text.supportRichText = false;
            Fill(text.rectTransform, 8.0f);

            Text hint = Label($"{name} Hint", plate.transform, size, TextAnchor.MiddleLeft);
            hint.color = DisabledInk;
            hint.supportRichText = false;
            Fill(hint.rectTransform, 8.0f);

            var field = plate.gameObject.AddComponent<InputField>();
            field.targetGraphic = plate;
            field.textComponent = text;
            field.placeholder = hint;
            field.lineType = InputField.LineType.SingleLine;
            field.caretColor = Ink;
            field.customCaretColor = true;
            field.selectionColor = new Color(0.45f, 0.60f, 0.75f, 0.55f);

            if (done != null)
            {
                field.onEndEdit.AddListener(value => done(value));
            }

            return field;
        }

        /// <summary>
        /// Returns the accent colour of one side.
        /// </summary>
        /// <param name="side">Side to colour.</param>
        /// <returns>That side's accent, or plain ink for no side at all.</returns>
        public static Color For(Team side) => HudPalette.For(side);

        /// <summary>
        /// Places a rectangle by its corners, in canvas units measured from its parent.
        /// </summary>
        /// <param name="rect">Rectangle to place.</param>
        /// <param name="left">Distance from the parent's left edge.</param>
        /// <param name="bottom">Distance from the parent's bottom edge.</param>
        /// <param name="width">Width in canvas units.</param>
        /// <param name="height">Height in canvas units.</param>
        public static void Place(RectTransform rect, float left, float bottom, float width, float height)
            => HudPalette.Place(rect, left, bottom, width, height);

        /// <summary>
        /// Makes a rectangle fill its parent, inset by a margin on every side.
        /// </summary>
        /// <param name="rect">Rectangle to stretch.</param>
        /// <param name="margin">Metres of inset - canvas units - on every side.</param>
        public static void Fill(RectTransform rect, float margin)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(margin, margin);
            rect.offsetMax = new Vector2(-margin, -margin);
        }

        /// <summary>
        /// Anchors a rectangle to one edge of its parent and gives it a thickness.
        /// </summary>
        /// <param name="rect">Rectangle to place.</param>
        /// <param name="edge">Which edge of the parent it clings to.</param>
        /// <param name="thickness">How far it reaches in from that edge, in canvas units.</param>
        /// <param name="from">Inset at the start of the edge it runs along.</param>
        /// <param name="to">Inset at the end of that edge.</param>
        /// <remarks>
        /// The one layout rule the editor's outer panels follow, because unlike the HUD they
        /// have to survive being a different shape: an editor is used at whatever size the
        /// window happens to be, and a column of tools that stopped halfway down a tall screen
        /// would be a column with a hole under it.
        /// </remarks>
        public static void Cling(
            RectTransform rect, RectTransform.Edge edge, float thickness, float from, float to)
        {
            switch (edge)
            {
                case RectTransform.Edge.Left:
                    rect.anchorMin = new Vector2(0.0f, 0.0f);
                    rect.anchorMax = new Vector2(0.0f, 1.0f);
                    rect.pivot = new Vector2(0.0f, 0.5f);
                    rect.offsetMin = new Vector2(0.0f, from);
                    rect.offsetMax = new Vector2(thickness, -to);
                    return;

                case RectTransform.Edge.Right:
                    rect.anchorMin = new Vector2(1.0f, 0.0f);
                    rect.anchorMax = new Vector2(1.0f, 1.0f);
                    rect.pivot = new Vector2(1.0f, 0.5f);
                    rect.offsetMin = new Vector2(-thickness, from);
                    rect.offsetMax = new Vector2(0.0f, -to);
                    return;

                case RectTransform.Edge.Top:
                    rect.anchorMin = new Vector2(0.0f, 1.0f);
                    rect.anchorMax = new Vector2(1.0f, 1.0f);
                    rect.pivot = new Vector2(0.5f, 1.0f);
                    rect.offsetMin = new Vector2(from, -thickness);
                    rect.offsetMax = new Vector2(-to, 0.0f);
                    return;

                default:
                    rect.anchorMin = new Vector2(0.0f, 0.0f);
                    rect.anchorMax = new Vector2(1.0f, 0.0f);
                    rect.pivot = new Vector2(0.5f, 0.0f);
                    rect.offsetMin = new Vector2(from, 0.0f);
                    rect.offsetMax = new Vector2(-to, thickness);
                    return;
            }
        }
    }
}
