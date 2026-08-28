using UnityEngine;
using UnityEngine.UI;
using IronFlag.Core;

namespace IronFlag.UI
{
    /// <summary>
    /// The backing plate every panel in this game is drawn on: dark glass that falls off
    /// towards its edges, with the faint horizontal lines of something being displayed rather
    /// than printed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A generated mesh rather than an <see cref="Image"/> with a texture on it, which is the
    /// same answer this project has given every time the question has come up: a nine-slice
    /// sprite is an asset nobody can review in a diff, and everything a plate needs to look
    /// like glass is arithmetic. The whole thing is one draw call, it is resolution
    /// independent because there is nothing in it to sample, and the numbers that decide how
    /// it looks are four constants a reader can see from here.
    /// </para>
    /// <para>
    /// <strong>The falloff is the world's vignette, on the glass.</strong>
    /// <see cref="EdgeDarken"/> and <see cref="EdgeReach"/> are read out of
    /// <see cref="PostTuning"/> rather than written down again, so the corners of a panel go
    /// dark by exactly as much as the corners of the frame behind it do, and by exactly as
    /// gradually. That is the whole of what makes the interface and the world read as one
    /// picture: the HUD is drawn by a camera with the grade switched off - see
    /// <see cref="InterfaceLayers"/> for why it has to be - so the only way it can share a
    /// filter with the world is to be built out of the same numbers.
    /// </para>
    /// <para>
    /// The scanlines are the one thing here that is decoration rather than derivation. They
    /// are kept at <see cref="ScanInk"/>'s three per cent so they read as the texture of a
    /// surface at a glance and disappear entirely when anybody actually looks at a number -
    /// a HUD that announces it is a CRT is a HUD competing with the game for attention.
    /// </para>
    /// <para>
    /// <strong>The <see cref="CanvasRenderer"/> below is load-bearing.</strong>
    /// <see cref="Graphic"/> requires only a <see cref="RectTransform"/>; it is
    /// <see cref="Image"/> that asks for the renderer, so a custom graphic that does not ask
    /// for it too never gets one - and <c>Graphic.Rebuild</c> begins by returning when the
    /// renderer is null. The result is a component that is enabled, registered, dirtied and
    /// laid out correctly, whose mesh function is simply never called, and which logs nothing
    /// at all. See <c>UI_NOTES.md</c>; it cost most of a day.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(CanvasRenderer))]
    [AddComponentMenu("IronFlag/Hud Plate")]
    public sealed class HudPlate : MaskableGraphic
    {
        /// <summary>
        /// How much darker a plate is at its outer edge, as a multiplier of its own colour.
        /// </summary>
        /// <remarks>
        /// The world's vignette intensity, read as "how much of the picture the corners lose."
        /// Deriving it means a change to the grade carries onto the glass by itself, and means
        /// there is no second number for the two to drift apart by.
        /// </remarks>
        public static readonly float EdgeDarken = 1.0f - PostTuning.VignetteIntensity;

        /// <summary>
        /// How far in the falloff reaches, as a fraction of the plate's shorter half.
        /// </summary>
        /// <remarks>The world's vignette smoothness, read as "how gradually it arrives."</remarks>
        public static readonly float EdgeReach = PostTuning.VignetteSmoothness;

        /// <summary>Distance between one scanline and the next, in canvas units.</summary>
        /// <remarks>
        /// One line every four units, one unit thick. At the reference width, and in the
        /// two-player split - which is full width and half height, so the canvas still scales
        /// one to one - that is a one pixel line every four pixels, which is what a scanline
        /// is. Four players quarter the screen and halve the scale, and the lines go soft
        /// rather than wrong.
        /// </remarks>
        public const float ScanPitch = 4.0f;

        /// <summary>How thick one scanline is, in canvas units.</summary>
        public const float ScanThickness = 1.0f;

        /// <summary>Most scanlines a plate will ever be given.</summary>
        /// <remarks>
        /// A guard rather than a design decision. Nothing in this game builds a panel tall
        /// enough to reach it; a bug that built one a mile high should draw a plate with too
        /// few lines on it rather than lock the editor generating a mesh.
        /// </remarks>
        public const int ScanLimit = 256;

        /// <summary>Colour one scanline is drawn in, before the plate's own alpha.</summary>
        public static readonly Color ScanInk = new Color(1.0f, 1.0f, 1.0f, 0.03f);

        [SerializeField]
        [Tooltip("Whether the faint horizontal lines are drawn.")]
        private bool scanlines = true;

        /// <summary>Whether this plate carries scanlines.</summary>
        /// <remarks>
        /// On for anything a player looks at and off for the level editor's panels, which is
        /// the one place the two themes deliberately part company: the editor is a tool read
        /// while sitting still, and texture on a surface somebody is reading a list of map
        /// names off is texture in the way.
        /// </remarks>
        public bool Scanlines
        {
            get => scanlines;
            set
            {
                if (scanlines != value)
                {
                    scanlines = value;
                    SetVerticesDirty();
                }
            }
        }

        /// <summary>
        /// Builds the plate: nine quads of glass, then a line every few units up it.
        /// </summary>
        /// <param name="vh">The mesh being filled.</param>
        /// <remarks>
        /// Nine quads because a falloff needs somewhere to fall off across, and a three by
        /// three grid is the fewest that gives every edge and every corner one. The reach is
        /// taken from the shorter side, so the band is the same width all the way round a
        /// panel that is much wider than it is tall - which every strip on this HUD is.
        /// </remarks>
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect area = GetPixelAdjustedRect();
            if (area.width <= 0.0f || area.height <= 0.0f)
            {
                return;
            }

            // Never more than half the shorter side, so the middle band can be flat but never
            // inside out.
            float reach = Mathf.Min(area.width, area.height) * 0.5f * Mathf.Clamp01(EdgeReach);

            var xs = new float[] { area.xMin, area.xMin + reach, area.xMax - reach, area.xMax };
            var ys = new float[] { area.yMin, area.yMin + reach, area.yMax - reach, area.yMax };

            Color32 lit = color;
            Color32 dim = Dimmed(color);

            for (int column = 0; column < 3; column++)
            {
                for (int row = 0; row < 3; row++)
                {
                    AddQuad(
                        vh,
                        new Vector2(xs[column], ys[row]),
                        new Vector2(xs[column + 1], ys[row + 1]),
                        Shade(column, row, lit, dim),
                        Shade(column + 1, row, lit, dim),
                        Shade(column + 1, row + 1, lit, dim),
                        Shade(column, row + 1, lit, dim));
                }
            }

            if (scanlines)
            {
                AddScanlines(vh, area);
            }
        }

        /// <summary>
        /// Draws a line every <see cref="ScanPitch"/> units up the plate.
        /// </summary>
        /// <param name="vh">The mesh being filled.</param>
        /// <param name="area">The plate's rectangle.</param>
        /// <remarks>
        /// Phased from the plate's own bottom edge rather than from the canvas, so two panels
        /// at different heights are not in step with each other. They are never adjacent on
        /// this HUD, and aligning them would mean a plate whose look depended on where it had
        /// been put.
        /// </remarks>
        private void AddScanlines(VertexHelper vh, Rect area)
        {
            Color32 ink = Scanline(color);
            int lines = Mathf.Min(Mathf.FloorToInt(area.height / ScanPitch), ScanLimit);

            for (int line = 0; line < lines; line++)
            {
                float bottom = area.yMin + (line * ScanPitch);
                float top = Mathf.Min(bottom + ScanThickness, area.yMax);

                AddQuad(
                    vh,
                    new Vector2(area.xMin, bottom),
                    new Vector2(area.xMax, top),
                    ink,
                    ink,
                    ink,
                    ink);
            }
        }

        /// <summary>
        /// Returns the colour of one corner of the falloff grid.
        /// </summary>
        /// <param name="column">Column of the grid, 0..3.</param>
        /// <param name="row">Row of the grid, 0..3.</param>
        /// <param name="lit">Colour away from the edges.</param>
        /// <param name="dim">Colour at them.</param>
        /// <returns>Whichever of the two that corner is.</returns>
        private static Color32 Shade(int column, int row, Color32 lit, Color32 dim)
            => column == 0 || column == 3 || row == 0 || row == 3 ? dim : lit;

        /// <summary>
        /// Returns a colour darkened by the world's own vignette.
        /// </summary>
        /// <param name="full">The colour away from the edge.</param>
        /// <returns>The same colour, dimmer, and no more transparent.</returns>
        /// <remarks>
        /// Brightness only. A plate that also thinned towards its edges would let the world
        /// through exactly where the falloff is meant to be holding it back, which is the
        /// opposite of what a vignette does.
        /// </remarks>
        private static Color Dimmed(Color full)
            => new Color(full.r * EdgeDarken, full.g * EdgeDarken, full.b * EdgeDarken, full.a);

        /// <summary>
        /// Returns the colour a scanline is drawn in on a plate of a given opacity.
        /// </summary>
        /// <param name="plate">The plate's own colour.</param>
        /// <returns>The scanline ink, no more opaque than the glass it is on.</returns>
        /// <remarks>
        /// Scaled by the plate's alpha, so lines never show up where the plate itself has
        /// faded out - a panel on its way off screen would otherwise leave its texture behind
        /// for a moment after the glass had gone.
        /// </remarks>
        private static Color Scanline(Color plate)
            => new Color(ScanInk.r, ScanInk.g, ScanInk.b, ScanInk.a * plate.a);

        /// <summary>
        /// Adds one rectangle to the mesh, with a colour at each corner.
        /// </summary>
        /// <param name="vh">The mesh being filled.</param>
        /// <param name="bottomLeft">The rectangle's low corner.</param>
        /// <param name="topRight">The rectangle's high corner.</param>
        /// <param name="lowLeft">Colour at the bottom left.</param>
        /// <param name="lowRight">Colour at the bottom right.</param>
        /// <param name="highRight">Colour at the top right.</param>
        /// <param name="highLeft">Colour at the top left.</param>
        internal static void AddQuad(
            VertexHelper vh,
            Vector2 bottomLeft,
            Vector2 topRight,
            Color32 lowLeft,
            Color32 lowRight,
            Color32 highRight,
            Color32 highLeft)
        {
            int first = vh.currentVertCount;

            vh.AddVert(new Vector3(bottomLeft.x, bottomLeft.y), lowLeft, Vector2.zero);
            vh.AddVert(new Vector3(topRight.x, bottomLeft.y), lowRight, Vector2.zero);
            vh.AddVert(new Vector3(topRight.x, topRight.y), highRight, Vector2.zero);
            vh.AddVert(new Vector3(bottomLeft.x, topRight.y), highLeft, Vector2.zero);

            vh.AddTriangle(first, first + 1, first + 2);
            vh.AddTriangle(first + 2, first + 3, first);
        }
    }
}
