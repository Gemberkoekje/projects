using UnityEngine;
using UnityEngine.UI;

namespace IronFlag.Editing
{
    /// <summary>
    /// A hollow rectangle drawn over the map: four thin bars, reused rather than rebuilt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything the editor needs to draw on top of the map is a box - what is selected, what
    /// a drag is describing, where a thing's opposite number would stand - so there is one
    /// shape here and it is made of four images. No line renderer, because a line renderer is
    /// world geometry that needs a material and a shader this project does not have, and would
    /// be occluded by the very map it is annotating.
    /// </para>
    /// <para>
    /// <strong>Placed by anchors rather than by coordinates.</strong> A bar is told which
    /// <em>fraction</em> of its parent each of its edges sits at, which is exactly what
    /// <c>WorldToViewportPoint</c> answers - so nothing here ever has to know how many pixels
    /// wide anything is. That matters because the canvas is one size while the game runs and
    /// another while the camera is rendering a still into a texture, and a marker measured in
    /// pixels lands somewhere else in the second case. Only the <em>thickness</em> is in
    /// canvas units, because a bar should be the same weight at every zoom.
    /// </para>
    /// <para>
    /// The bars are created once and moved, never created per frame. A selection outline that
    /// allocated four objects a frame would allocate a quarter of a million of them over a
    /// session spent drawing a coastline.
    /// </para>
    /// </remarks>
    public sealed class EditorOutline
    {
        private readonly Image[] bars;

        private EditorOutline(Image[] sides)
        {
            bars = sides;
        }

        /// <summary>
        /// Builds an outline into a canvas.
        /// </summary>
        /// <param name="parent">Object to hang it off.</param>
        /// <param name="name">What to call it in the hierarchy.</param>
        /// <returns>The outline, hidden.</returns>
        public static EditorOutline Build(Transform parent, string name)
        {
            var host = new GameObject(name, typeof(RectTransform));
            host.transform.SetParent(parent, false);

            var rect = host.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var sides = new Image[4];
            for (int side = 0; side < sides.Length; side++)
            {
                var bar = new GameObject($"Bar {side}", typeof(RectTransform), typeof(Image));
                bar.transform.SetParent(host.transform, false);

                Image image = bar.GetComponent<Image>();
                image.raycastTarget = false;
                image.rectTransform.pivot = new Vector2(0.5f, 0.5f);

                sides[side] = image;
            }

            var outline = new EditorOutline(sides);
            outline.Hide();
            return outline;
        }

        /// <summary>
        /// Draws the outline around a rectangle of the view.
        /// </summary>
        /// <param name="viewport">
        /// The rectangle, in fractions of the view from its bottom left.
        /// </param>
        /// <param name="color">What to draw it in.</param>
        /// <param name="thickness">How thick the bars are, in canvas units.</param>
        public void Show(Rect viewport, Color color, float thickness)
        {
            float lowX = viewport.xMin;
            float highX = viewport.xMax;
            float lowY = viewport.yMin;
            float highY = viewport.yMax;

            Lay(bars[0], new Vector2(lowX, lowY), new Vector2(highX, lowY), 0.0f, thickness);
            Lay(bars[1], new Vector2(lowX, highY), new Vector2(highX, highY), 0.0f, thickness);
            Lay(bars[2], new Vector2(lowX, lowY), new Vector2(lowX, highY), thickness, 0.0f);
            Lay(bars[3], new Vector2(highX, lowY), new Vector2(highX, highY), thickness, 0.0f);

            foreach (Image side in bars)
            {
                side.color = color;
                side.enabled = true;
            }
        }

        /// <summary>
        /// Draws a small cross at one point of the view.
        /// </summary>
        /// <param name="at">The point, in fractions of the view from its bottom left.</param>
        /// <param name="reach">How far each arm reaches, in canvas units.</param>
        /// <param name="color">What to draw it in.</param>
        /// <param name="thickness">How thick the arms are, in canvas units.</param>
        /// <remarks>
        /// Sized in canvas units rather than in fractions, unlike everything else here: a
        /// cross marking the origin is a symbol rather than a measurement of anything, so it
        /// should stay the same size as the map is zoomed rather than grow into a pair of
        /// stripes across the whole picture.
        /// </remarks>
        public void ShowCross(Vector2 at, float reach, Color color, float thickness)
        {
            Lay(bars[0], at, at, reach * 2.0f, thickness);
            Lay(bars[1], at, at, thickness, reach * 2.0f);
            bars[0].color = color;
            bars[1].color = color;
            bars[0].enabled = true;
            bars[1].enabled = true;

            bars[2].enabled = false;
            bars[3].enabled = false;
        }

        /// <summary>
        /// Takes the outline off the screen.
        /// </summary>
        public void Hide()
        {
            foreach (Image side in bars)
            {
                side.enabled = false;
            }
        }

        /// <summary>
        /// Anchors one bar between two fractions of its parent, with a thickness in units.
        /// </summary>
        /// <param name="bar">The bar.</param>
        /// <param name="from">One end, in fractions of the parent.</param>
        /// <param name="to">The other end.</param>
        /// <param name="width">Extra width in canvas units, over what the anchors give it.</param>
        /// <param name="height">Extra height in canvas units.</param>
        private static void Lay(Image bar, Vector2 from, Vector2 to, float width, float height)
        {
            RectTransform rect = bar.rectTransform;
            rect.anchorMin = new Vector2(Mathf.Min(from.x, to.x), Mathf.Min(from.y, to.y));
            rect.anchorMax = new Vector2(Mathf.Max(from.x, to.x), Mathf.Max(from.y, to.y));
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
