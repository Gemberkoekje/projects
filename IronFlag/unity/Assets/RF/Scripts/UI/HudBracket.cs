using UnityEngine;
using UnityEngine.UI;

namespace IronFlag.UI
{
    /// <summary>
    /// The four corner marks around a panel: two short arms at each corner, in the colour of
    /// whoever the panel belongs to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Corners rather than a border, and that is the whole idea.</strong> A rectangle
    /// drawn all the way round a panel is a window frame - it says "this is a different
    /// surface from the one behind it," which is what an operating system wants to say and
    /// the opposite of what this game does. Four corner marks say "this region is being
    /// watched," which is what every gunsight, every reticle and every piece of military
    /// glass in the last fifty years has said, and it costs eight quads.
    /// </para>
    /// <para>
    /// This is also where a player's own colour lives on the interface. The plate underneath
    /// is the same dark glass on both halves of a split screen, and putting the accent on the
    /// frame rather than on the panel means the one thing that differs between the two halves
    /// is a thin line at the corners rather than the whole tint of the picture - which
    /// matters because both halves are on one television and the difference has to be
    /// legible without being loud.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(CanvasRenderer))]
    [AddComponentMenu("IronFlag/Hud Bracket")]
    public sealed class HudBracket : MaskableGraphic
    {
        /// <summary>How far each arm reaches along its edge, in canvas units.</summary>
        /// <remarks>
        /// Long enough to read as a corner rather than as a dot, short enough that the four
        /// of them never meet on the shortest panel this HUD builds. The reach is clamped
        /// against the panel anyway - see <see cref="OnPopulateMesh"/> - so a small panel
        /// gets smaller corners rather than a rectangle.
        /// </remarks>
        public const float ArmLength = 22.0f;

        /// <summary>How thick an arm is, in canvas units.</summary>
        public const float ArmThickness = 2.0f;

        [SerializeField]
        [Tooltip("How far each arm reaches along its edge, in canvas units.")]
        private float arm = ArmLength;

        [SerializeField]
        [Tooltip("How thick an arm is, in canvas units.")]
        private float thickness = ArmThickness;

        /// <summary>How far each arm reaches along its edge.</summary>
        public float Arm
        {
            get => arm;
            set
            {
                if (!Mathf.Approximately(arm, value))
                {
                    arm = value;
                    SetVerticesDirty();
                }
            }
        }

        /// <summary>How thick an arm is.</summary>
        public float Thickness
        {
            get => thickness;
            set
            {
                if (!Mathf.Approximately(thickness, value))
                {
                    thickness = value;
                    SetVerticesDirty();
                }
            }
        }

        /// <summary>
        /// Builds the four corners: two arms each, meeting but never overlapping.
        /// </summary>
        /// <param name="vh">The mesh being filled.</param>
        /// <remarks>
        /// The upright arm stops where the flat one starts rather than running under it. Two
        /// quads of a half-transparent colour laid on top of each other blend twice, and the
        /// result is a bright square at every corner of every panel - which looks like a
        /// deliberate stud until somebody changes the alpha and it does not.
        /// </remarks>
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect area = GetPixelAdjustedRect();
            if (area.width <= 0.0f || area.height <= 0.0f)
            {
                return;
            }

            float bar = Mathf.Max(0.0f, thickness);
            float reach = Mathf.Min(arm, area.width * 0.5f, area.height * 0.5f);
            if (reach <= 0.0f || bar <= 0.0f)
            {
                return;
            }

            Color32 ink = color;

            // Bottom left.
            AddArm(vh, area.xMin, area.yMin, reach, bar, ink);
            AddArm(vh, area.xMin, area.yMin + bar, bar, reach - bar, ink);

            // Bottom right.
            AddArm(vh, area.xMax - reach, area.yMin, reach, bar, ink);
            AddArm(vh, area.xMax - bar, area.yMin + bar, bar, reach - bar, ink);

            // Top left.
            AddArm(vh, area.xMin, area.yMax - bar, reach, bar, ink);
            AddArm(vh, area.xMin, area.yMax - reach, bar, reach - bar, ink);

            // Top right.
            AddArm(vh, area.xMax - reach, area.yMax - bar, reach, bar, ink);
            AddArm(vh, area.xMax - bar, area.yMax - reach, bar, reach - bar, ink);
        }

        /// <summary>
        /// Adds one arm of one corner.
        /// </summary>
        /// <param name="vh">The mesh being filled.</param>
        /// <param name="left">Low edge across.</param>
        /// <param name="bottom">Low edge up.</param>
        /// <param name="width">How far it reaches across.</param>
        /// <param name="height">How far it reaches up.</param>
        /// <param name="ink">Colour to draw it in.</param>
        private static void AddArm(
            VertexHelper vh, float left, float bottom, float width, float height, Color32 ink)
        {
            if (width <= 0.0f || height <= 0.0f)
            {
                return;
            }

            HudPlate.AddQuad(
                vh,
                new Vector2(left, bottom),
                new Vector2(left + width, bottom + height),
                ink,
                ink,
                ink,
                ink);
        }
    }
}
