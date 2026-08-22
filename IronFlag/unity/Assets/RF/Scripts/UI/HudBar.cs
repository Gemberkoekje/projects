using UnityEngine;
using UnityEngine.UI;

namespace IronFlag.UI
{
    /// <summary>
    /// One labelled bar on the HUD: a name, a track, the part of it that is filled, and the
    /// number behind it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three of these are the whole driving HUD - armour, fuel, ammunition - and a fourth
    /// doubles as the progress of a held button. They carry a number as well as a length
    /// because the two are read at different moments: a bar answers "am I in trouble" at a
    /// glance mid-fight, and the number answers "can I make it home" when there is a second
    /// to think.
    /// </para>
    /// <para>
    /// The fill is scaled rather than resized. A rect transform's width is laid out by the
    /// canvas and a scale is not, so scaling costs no layout pass per frame per bar per
    /// player - and the fill is a flat colour, so there is nothing in it to distort.
    /// </para>
    /// </remarks>
    public sealed class HudBar
    {
        private readonly Text caption;
        private readonly RectTransform fill;
        private readonly Image ink;
        private readonly Text reading;

        private HudBar(Text captionText, Image fillImage, Text readingText)
        {
            caption = captionText;
            ink = fillImage;
            fill = fillImage.rectTransform;
            reading = readingText;
        }

        /// <summary>The objects this bar is drawn from, for the tests and the still.</summary>
        public RectTransform Fill => fill;

        /// <summary>
        /// Builds a bar into a panel.
        /// </summary>
        /// <param name="parent">Panel to build it into.</param>
        /// <param name="name">What the bar measures, in words.</param>
        /// <param name="color">Colour of the filled part.</param>
        /// <param name="left">Distance from the panel's left edge.</param>
        /// <param name="bottom">Distance from the panel's bottom edge.</param>
        /// <param name="width">Width of the whole row, caption included.</param>
        /// <param name="height">Height of the row.</param>
        /// <returns>The bar, already showing nothing.</returns>
        public static HudBar Build(
            Transform parent,
            string name,
            Color color,
            float left,
            float bottom,
            float width,
            float height)
        {
            const float captionWidth = 110.0f;
            const float readingWidth = 130.0f;
            float trackWidth = width - captionWidth - readingWidth - 16.0f;

            Text captionText = HudPalette.Label(
                $"{name} Caption", parent, Mathf.RoundToInt(height * 0.62f), TextAnchor.MiddleLeft);
            captionText.color = HudPalette.FadedInk;
            captionText.text = name.ToUpperInvariant();
            HudPalette.Place(captionText.rectTransform, left, bottom, captionWidth, height);

            Image track = HudPalette.Box($"{name} Track", parent, HudPalette.Track);
            HudPalette.Place(
                track.rectTransform, left + captionWidth, bottom + (height * 0.2f),
                trackWidth, height * 0.6f);

            Image fill = HudPalette.Box($"{name} Fill", track.transform, color);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = Vector2.zero;
            fill.rectTransform.pivot = Vector2.zero;
            fill.rectTransform.anchoredPosition = Vector2.zero;
            fill.rectTransform.sizeDelta = new Vector2(trackWidth, height * 0.6f);

            Text readingText = HudPalette.Label(
                $"{name} Reading", parent, Mathf.RoundToInt(height * 0.62f), TextAnchor.MiddleRight);
            HudPalette.Place(
                readingText.rectTransform, left + captionWidth + trackWidth + 16.0f, bottom,
                readingWidth, height);

            var bar = new HudBar(captionText, fill, readingText);
            bar.Show(0.0f, string.Empty, color);
            return bar;
        }

        /// <summary>
        /// Sets how full this bar is and what it says.
        /// </summary>
        /// <param name="fraction">How full, in 0..1.</param>
        /// <param name="text">The number behind it, already formatted.</param>
        /// <param name="color">Colour of the filled part.</param>
        public void Show(float fraction, string text, Color color)
        {
            Vector3 scale = fill.localScale;
            scale.x = Mathf.Clamp01(fraction);
            fill.localScale = scale;

            ink.color = color;
            reading.text = text;
            reading.color = color;
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
            reading.enabled = visible;
            ink.enabled = visible;

            Transform track = fill.parent;
            var backing = track == null ? null : track.GetComponent<Image>();
            if (backing != null)
            {
                backing.enabled = visible;
            }
        }
    }
}
