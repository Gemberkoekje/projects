using UnityEngine;
using UnityEngine.UI;

namespace IronFlag.UI
{
    /// <summary>
    /// One labelled bar on the HUD: a mark, a name, a track, the part of it that is filled,
    /// and the number behind it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three of these are the whole driving HUD - armour, fuel, ammunition - and a fourth
    /// doubles as the progress of a held button. They carry a number as well as a length
    /// because the two are read at different moments: a bar answers "am I in trouble" at a
    /// glance mid-fight, and the number answers "can I make it home" when there is a second
    /// to think. The mark is a third reading of the same thing, for the moment before either
    /// - a shape is recognised before a word is read.
    /// </para>
    /// <para>
    /// The fill is scaled rather than resized. A rect transform's width is laid out by the
    /// canvas and a scale is not, so scaling costs no layout pass per frame per bar per
    /// player - and the fill is a flat colour, so there is nothing in it to distort.
    /// </para>
    /// <para>
    /// <strong>A pool and a countdown are different bars.</strong> <see cref="Show"/> is for
    /// something the vehicle has a quantity of: it eases towards its new length, it goes red
    /// on its own when the pool is nearly gone, and it breathes while it stays there.
    /// <see cref="ShowProgress"/> is for a button being held down, and does none of those -
    /// a progress bar that eased would lag the finger pressing it, and one that pulsed at the
    /// start would be shouting about a bar that has only just begun to fill.
    /// </para>
    /// </remarks>
    public sealed class HudBar
    {
        /// <summary>How much of a row the mark takes, in canvas units.</summary>
        private const float GlyphWidth = 26.0f;

        /// <summary>Gap between the mark and the word beside it, in canvas units.</summary>
        private const float GlyphGutter = 4.0f;

        /// <summary>How much of a row the mark and the word take together.</summary>
        private const float CaptionWidth = 110.0f;

        /// <summary>How much of a row the number takes.</summary>
        private const float ReadingWidth = 130.0f;

        /// <summary>Gap between the track and the number behind it.</summary>
        private const float ReadingGutter = 16.0f;

        /// <summary>Type size as a fraction of the row's height.</summary>
        /// <remarks>
        /// A little larger than the two thirds this was set in a wider face, which is the
        /// first thing a condensed typeface buys: the same words in the same boxes, bigger.
        /// </remarks>
        private const float TypeScale = 0.66f;

        private readonly Text caption;
        private readonly HudGlyph mark;
        private readonly RectTransform fill;
        private readonly Image ink;
        private readonly Text reading;

        private float shown;
        private float target;
        private Color full;
        private bool eases;

        private HudBar(Text captionText, HudGlyph glyph, Image fillImage, Text readingText)
        {
            caption = captionText;
            mark = glyph;
            ink = fillImage;
            fill = fillImage.rectTransform;
            reading = readingText;
        }

        /// <summary>The objects this bar is drawn from, for the tests and the still.</summary>
        public RectTransform Fill => fill;

        /// <summary>How full the bar is drawn right now, which trails what it was told.</summary>
        public float Shown => shown;

        /// <summary>
        /// Builds a bar into a panel.
        /// </summary>
        /// <param name="parent">Panel to build it into.</param>
        /// <param name="name">What the bar measures, in words.</param>
        /// <param name="glyph">The mark that stands for it, or none.</param>
        /// <param name="color">Colour of the filled part.</param>
        /// <param name="left">Distance from the panel's left edge.</param>
        /// <param name="bottom">Distance from the panel's bottom edge.</param>
        /// <param name="width">Width of the whole row, caption included.</param>
        /// <param name="height">Height of the row.</param>
        /// <returns>The bar, already showing nothing.</returns>
        public static HudBar Build(
            Transform parent,
            string name,
            HudGlyphKind glyph,
            Color color,
            float left,
            float bottom,
            float width,
            float height)
        {
            float trackWidth = width - CaptionWidth - ReadingWidth - ReadingGutter;
            int type = Mathf.RoundToInt(height * TypeScale);

            HudGlyph mark = HudPalette.Glyph($"{name} Mark", parent, glyph, HudPalette.FadedInk);
            HudPalette.Place(mark.rectTransform, left, bottom, GlyphWidth, height);

            Text captionText = HudPalette.Label(
                $"{name} Caption", parent, type, TextAnchor.MiddleLeft);
            captionText.color = HudPalette.FadedInk;
            captionText.text = name.ToUpperInvariant();
            HudPalette.Place(
                captionText.rectTransform,
                left + GlyphWidth + GlyphGutter,
                bottom,
                CaptionWidth - GlyphWidth - GlyphGutter,
                height);

            Image track = HudPalette.Box($"{name} Track", parent, HudPalette.Track);
            HudPalette.Place(
                track.rectTransform, left + CaptionWidth, bottom + (height * 0.2f),
                trackWidth, height * 0.6f);

            Image fill = HudPalette.Box($"{name} Fill", track.transform, color);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = Vector2.zero;
            fill.rectTransform.pivot = Vector2.zero;
            fill.rectTransform.anchoredPosition = Vector2.zero;
            fill.rectTransform.sizeDelta = new Vector2(trackWidth, height * 0.6f);

            Text readingText = HudPalette.Label(
                $"{name} Reading", parent, type, TextAnchor.MiddleRight);
            HudPalette.Place(
                readingText.rectTransform, left + CaptionWidth + trackWidth + ReadingGutter, bottom,
                ReadingWidth, height);

            var bar = new HudBar(captionText, mark, fill, readingText);
            bar.ShowProgress(0.0f, string.Empty, color);
            return bar;
        }

        /// <summary>
        /// Sets how full a pool is and what it says.
        /// </summary>
        /// <param name="fraction">How much is left, in 0..1.</param>
        /// <param name="text">The number behind it, already formatted.</param>
        /// <param name="color">Colour of the pool when there is plenty of it.</param>
        /// <remarks>
        /// The colour handed in is the pool's own, not the colour it will be drawn in. Whether
        /// something is low enough to go red is a fact about the reading rather than about the
        /// caller, so <see cref="HudPalette.Level"/> is applied here - which is also what lets
        /// this bar know it should be breathing without being told twice.
        /// </remarks>
        public void Show(float fraction, string text, Color color)
        {
            target = Mathf.Clamp01(fraction);
            full = color;
            eases = true;
            reading.text = text;
        }

        /// <summary>
        /// Sets how far through something is and what it says.
        /// </summary>
        /// <param name="fraction">How far through, in 0..1.</param>
        /// <param name="text">The number behind it, already formatted.</param>
        /// <param name="color">Colour to draw it in, exactly.</param>
        public void ShowProgress(float fraction, string text, Color color)
        {
            target = Mathf.Clamp01(fraction);
            shown = target;
            full = color;
            eases = false;
            reading.text = text;
            Paint(color);
        }

        /// <summary>
        /// Moves the bar towards what it was last told, and breathes it if it is nearly out.
        /// </summary>
        /// <param name="deltaTime">Seconds since this was last called.</param>
        /// <remarks>
        /// Called once a frame by whatever owns the bar rather than from an <c>Update</c> of
        /// its own, because a <see cref="HudBar"/> is a plain object holding four widgets and
        /// not a component - see the class summary of
        /// <see cref="IronFlag.Editing.EditorButton"/>, which is the same shape for the same
        /// reason.
        /// </remarks>
        public void Advance(float deltaTime)
        {
            if (!eases)
            {
                return;
            }

            shown = HudMotion.Ease(shown, target, HudMotion.BarRate, deltaTime);
            Paint(Colour());
        }

        /// <summary>
        /// Puts the bar where it was told to be, with no slide.
        /// </summary>
        /// <remarks>
        /// For the moment a player changes vehicle. Everything on this strip is about the
        /// thing being driven, so a tank's armour easing down into a jeep's would be one
        /// vehicle's reading drawn on another's gauge - which is a lie for a quarter of a
        /// second rather than an animation.
        /// </remarks>
        public void Jump()
        {
            shown = target;
            Paint(Colour());
        }

        /// <summary>
        /// Returns the colour the bar should be drawn in right now.
        /// </summary>
        /// <returns>The pool's colour, the alarm colour, or that colour breathing.</returns>
        /// <remarks>
        /// Read off the target rather than off the length being drawn. A gauge easing down
        /// past a fifth would otherwise turn red partway through the slide, which reads as the
        /// bar changing its mind rather than as the tank running dry.
        /// </remarks>
        private Color Colour()
        {
            if (!eases)
            {
                return full;
            }

            Color level = HudPalette.Level(full, target);
            return HudPalette.IsAlarming(target) ? HudMotion.Flash(level, Time.time) : level;
        }

        /// <summary>
        /// Changes what this bar is called, which the fourth one does as its meaning changes.
        /// </summary>
        /// <param name="name">What the bar now measures.</param>
        public void Rename(string name) => caption.text = name.ToUpperInvariant();

        /// <summary>
        /// Shows or hides the whole row.
        /// </summary>
        /// <param name="visible">Whether the row should be drawn.</param>
        public void SetVisible(bool visible)
        {
            caption.enabled = visible;
            mark.enabled = visible;
            reading.enabled = visible;
            ink.enabled = visible;

            Transform track = fill.parent;
            var backing = track == null ? null : track.GetComponent<Image>();
            if (backing != null)
            {
                backing.enabled = visible;
            }
        }

        /// <summary>
        /// Draws the bar at its current length in a given colour.
        /// </summary>
        /// <param name="color">Colour of the filled part and of the number behind it.</param>
        private void Paint(Color color)
        {
            Vector3 scale = fill.localScale;
            scale.x = Mathf.Clamp01(shown);
            fill.localScale = scale;

            ink.color = color;
            reading.color = color;
        }
    }
}
