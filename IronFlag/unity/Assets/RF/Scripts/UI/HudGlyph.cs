using UnityEngine;
using UnityEngine.UI;

namespace IronFlag.UI
{
    /// <summary>
    /// One of the interface's four drawn marks - a shield, a drop, a round, a flag - built
    /// out of its own coordinates rather than sampled out of a picture.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>An icon set with no icons in it.</strong> This project has never shipped a
    /// texture, and an icon sheet would be the easiest possible place to start - which is
    /// exactly why it is worth not doing: a sheet is one binary file whose contents nobody
    /// can see in a diff, at one resolution, that has to be re-exported by whoever last had
    /// the source. Each mark here is instead a list of points in a unit square, in this file,
    /// where the shape of a shield is legible as numbers and a reviewer can see that somebody
    /// moved the tip of the drop rather than that a PNG changed.
    /// </para>
    /// <para>
    /// Every shape is convex, which is not decoration - it is what lets each one be drawn as
    /// a triangle fan from its first point with no triangulation step at all. A glyph that
    /// needed an ear-clipper would be a glyph that needed a library. The flag is the one mark
    /// with a concave silhouette and it is stored as two convex pieces, a pole and a pennant,
    /// for the same reason.
    /// </para>
    /// <para>
    /// The marks are drawn into the largest square that fits the rectangle, centred. A glyph
    /// beside a thirty-two unit gauge row and a glyph beside a twenty-four unit objective
    /// line are then the same shape at two sizes rather than the same shape squashed two
    /// ways, and no call site has to be told to keep its box square.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(CanvasRenderer))]
    [AddComponentMenu("IronFlag/Hud Glyph")]
    public sealed class HudGlyph : MaskableGraphic
    {
        /// <summary>Hit points: a shield, chamfered at the shoulders, pointed at the foot.</summary>
        private static readonly Vector2[] Shield =
        {
            new Vector2(0.12f, 0.96f),
            new Vector2(0.88f, 0.96f),
            new Vector2(0.88f, 0.52f),
            new Vector2(0.78f, 0.26f),
            new Vector2(0.50f, 0.06f),
            new Vector2(0.22f, 0.26f),
            new Vector2(0.12f, 0.52f),
        };

        /// <summary>Fuel: a drop, pointed at the top and round at the bottom.</summary>
        private static readonly Vector2[] Drop =
        {
            new Vector2(0.50f, 0.97f),
            new Vector2(0.72f, 0.62f),
            new Vector2(0.80f, 0.40f),
            new Vector2(0.74f, 0.20f),
            new Vector2(0.50f, 0.06f),
            new Vector2(0.26f, 0.20f),
            new Vector2(0.20f, 0.40f),
            new Vector2(0.28f, 0.62f),
        };

        /// <summary>Ammunition: a round, ogive on top of a straight case.</summary>
        private static readonly Vector2[] Round =
        {
            new Vector2(0.50f, 0.98f),
            new Vector2(0.68f, 0.74f),
            new Vector2(0.72f, 0.56f),
            new Vector2(0.72f, 0.04f),
            new Vector2(0.28f, 0.04f),
            new Vector2(0.28f, 0.56f),
            new Vector2(0.32f, 0.74f),
        };

        /// <summary>The staff half of the flag.</summary>
        private static readonly Vector2[] Staff =
        {
            new Vector2(0.16f, 0.02f),
            new Vector2(0.26f, 0.02f),
            new Vector2(0.26f, 0.98f),
            new Vector2(0.16f, 0.98f),
        };

        /// <summary>The cloth half of the flag.</summary>
        private static readonly Vector2[] Pennant =
        {
            new Vector2(0.26f, 0.98f),
            new Vector2(0.86f, 0.78f),
            new Vector2(0.26f, 0.58f),
        };

        [SerializeField]
        [Tooltip("Which mark this is.")]
        private HudGlyphKind kind = HudGlyphKind.None;

        /// <summary>Which mark this is.</summary>
        public HudGlyphKind Kind
        {
            get => kind;
            set
            {
                if (kind != value)
                {
                    kind = value;
                    SetVerticesDirty();
                }
            }
        }

        /// <summary>
        /// Returns the pieces one mark is drawn from.
        /// </summary>
        /// <param name="glyph">The mark to look up.</param>
        /// <returns>One convex outline per piece, in a unit square, or nothing at all.</returns>
        /// <remarks>
        /// Public and static so a test can check every kind has a shape without building a
        /// canvas - the failure this guards against is a fifth mark being added to
        /// <see cref="HudGlyphKind"/> and drawing nothing, which on a HUD looks exactly like a
        /// layout mistake.
        /// </remarks>
        public static Vector2[][] Outlines(HudGlyphKind glyph)
        {
            switch (glyph)
            {
                case HudGlyphKind.Armour:
                    return new[] { Shield };
                case HudGlyphKind.Fuel:
                    return new[] { Drop };
                case HudGlyphKind.Rounds:
                    return new[] { Round };
                case HudGlyphKind.Flag:
                    return new[] { Staff, Pennant };
                default:
                    return new Vector2[0][];
            }
        }

        /// <summary>
        /// Builds the mark: one fan per convex piece, inside the biggest square that fits.
        /// </summary>
        /// <param name="vh">The mesh being filled.</param>
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect area = GetPixelAdjustedRect();
            if (area.width <= 0.0f || area.height <= 0.0f)
            {
                return;
            }

            float side = Mathf.Min(area.width, area.height);
            var origin = new Vector2(
                area.xMin + ((area.width - side) * 0.5f),
                area.yMin + ((area.height - side) * 0.5f));

            Color32 ink = color;

            foreach (Vector2[] outline in Outlines(kind))
            {
                AddFan(vh, outline, origin, side, ink);
            }
        }

        /// <summary>
        /// Adds one convex outline to the mesh as a triangle fan from its first point.
        /// </summary>
        /// <param name="vh">The mesh being filled.</param>
        /// <param name="outline">The outline, in a unit square.</param>
        /// <param name="origin">Where the unit square's low corner lands.</param>
        /// <param name="side">How big the unit square is, in canvas units.</param>
        /// <param name="ink">Colour to draw it in.</param>
        private static void AddFan(
            VertexHelper vh, Vector2[] outline, Vector2 origin, float side, Color32 ink)
        {
            if (outline == null || outline.Length < 3)
            {
                return;
            }

            int first = vh.currentVertCount;

            foreach (Vector2 point in outline)
            {
                vh.AddVert(
                    new Vector3(origin.x + (point.x * side), origin.y + (point.y * side)),
                    ink,
                    Vector2.zero);
            }

            for (int corner = 1; corner < outline.Length - 1; corner++)
            {
                vh.AddTriangle(first, first + corner, first + corner + 1);
            }
        }
    }
}
