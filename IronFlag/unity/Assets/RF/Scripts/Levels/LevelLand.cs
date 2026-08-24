using System;
using UnityEngine;

namespace IronFlag.Levels
{
    /// <summary>
    /// One rectangle of dry ground. Everything the land rectangles do not cover is sea.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The map is described by what is <em>land</em> rather than by what is water, because
    /// the design document's map is an island: the water is the default and the land is the
    /// thing somebody drew. It also means a level cannot accidentally describe a hole in the
    /// sea, and that the shape of a coastline costs exactly as many rectangles as it is
    /// worth.
    /// </para>
    /// <para>
    /// Rectangles are axis-aligned and stated as their edges rather than as a centre and a
    /// size. Read out loud, "the green shore runs from z -92 to z -13" is a sentence about
    /// the map; a centre and a half-size is arithmetic about it. Overlap is fine and
    /// expected - a headland reaching into the channel is a second rectangle over the first.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class LevelLand
    {
        /// <summary>What this piece of land is called, for the hierarchy and the editor.</summary>
        [Tooltip("What this piece of land is called.")]
        public string Name = "Land";

        /// <summary>
        /// What it is made of, by name - a member of <see cref="SurfaceKind"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Carried as a name rather than a number, exactly as
        /// <see cref="LevelStructure.Kind"/> is and for the same reason - see
        /// <see cref="LevelNames"/>. A level says <em>where</em> a surface is; what it does
        /// is <see cref="SurfaceTuning.For"/>.
        /// </para>
        /// <para>
        /// Overlap is paint order. Rectangles already overlap on purpose - a headland
        /// reaching into the channel is a second rectangle over the first - so the last one
        /// in the file wins, and a road is a thin rectangle laid over a shore.
        /// </para>
        /// </remarks>
        [Tooltip("What it is made of: Grass, Sand or Asphalt.")]
        public string Surface = nameof(SurfaceKind.Grass);

        /// <summary>
        /// What outline it is cut to inside its box, by name - a member of
        /// <see cref="LandShape"/>.
        /// </summary>
        /// <remarks>
        /// Optional, and a rectangle when it is missing, so every map written before shapes
        /// existed means what it always did. The four edges are the box either way: an
        /// ellipse is the one inscribed in it. See <see cref="LandShape"/> for why there is
        /// one alternative rather than a polygon list.
        /// </remarks>
        [Tooltip("What outline it is cut to inside its box: Rectangle or Ellipse.")]
        public string Shape = nameof(LandShape.Rectangle);

        /// <summary>West edge, in metres.</summary>
        [Tooltip("West edge, in metres.")]
        public float MinX;

        /// <summary>East edge, in metres.</summary>
        [Tooltip("East edge, in metres.")]
        public float MaxX;

        /// <summary>South edge, in metres.</summary>
        [Tooltip("South edge, in metres.")]
        public float MinZ;

        /// <summary>North edge, in metres.</summary>
        [Tooltip("North edge, in metres.")]
        public float MaxZ;

        /// <summary>Width of the rectangle, in metres.</summary>
        public float Width => MaxX - MinX;

        /// <summary>Depth of the rectangle, in metres.</summary>
        public float Depth => MaxZ - MinZ;

        /// <summary>Middle of the rectangle, on the ground plane.</summary>
        public Vector3 Centre => new Vector3((MinX + MaxX) * 0.5f, 0.0f, (MinZ + MaxZ) * 0.5f);

        /// <summary>What it is made of.</summary>
        /// <remarks>
        /// A missing or unrecognised name is <see cref="SurfaceKind.Grass"/> rather than
        /// nothing, which is the one place this format departs from the rule that an unknown
        /// name resolves to the empty member. A prop nobody can build is a prop the map does
        /// without; a piece of land nobody can paint would be a hole in the world. Every
        /// rectangle written before surfaces existed therefore keeps working untouched -
        /// and a name that was meant to be something else is caught and named by
        /// <see cref="LevelValidation"/> rather than swallowed here.
        /// </remarks>
        public SurfaceKind Ground
        {
            get
            {
                SurfaceKind named = LevelNames.ToSurface(Surface);
                return named == SurfaceKind.None ? SurfaceKind.Grass : named;
            }
        }

        /// <summary>What outline it is cut to.</summary>
        /// <remarks>
        /// A missing or unrecognised name is <see cref="LandShape.Rectangle"/> for exactly
        /// the reason <see cref="Ground"/> is grass: a piece of land has to be some shape,
        /// and the shape every map before this one meant is the box. The typo is caught and
        /// quoted by <see cref="LevelValidation"/> rather than swallowed here.
        /// </remarks>
        public LandShape Form
        {
            get
            {
                LandShape named = LevelNames.ToShape(Shape);
                return named == LandShape.None ? LandShape.Rectangle : named;
            }
        }

        /// <summary>Whether the rectangle has any area at all.</summary>
        public bool IsDrawn => Width > 0.0f && Depth > 0.0f;

        /// <summary>
        /// Reports whether a point on the map stands on this piece of land.
        /// </summary>
        /// <param name="at">Point to test; its height is ignored.</param>
        /// <returns><c>true</c> when the point is inside the outline.</returns>
        public bool Contains(Vector3 at) => Signed(at) >= 0.0f;

        /// <summary>
        /// Measures how far a point is inside this piece of land.
        /// </summary>
        /// <param name="at">Point to measure; its height is ignored.</param>
        /// <returns>Metres to its nearest edge: positive inside, negative outside.</returns>
        /// <remarks>
        /// <para>
        /// What a coastline is actually cut from. <see cref="SurfaceField"/> rasterises the
        /// map to decide what each square metre is <em>made of</em>, but where the water's
        /// edge <em>runs</em> comes from this - measured, not rounded to a cell - which is
        /// what keeps a built edge exactly where the file put it even when it falls between
        /// two cells. It is also what the coastline wobble is added to, so a displaced coast
        /// is this shape moved rather than this shape redrawn.
        /// </para>
        /// <para>
        /// Exact for a rectangle. For an ellipse it is the first-order approximation - how
        /// far you are along the gradient of the ellipse equation - which is exact for a
        /// circle and errs by a fraction of a metre on the flanks of a long one. That is
        /// well inside the metre the field is measured in, and the alternative is an
        /// iterative root-find per cell.
        /// </para>
        /// </remarks>
        public float Signed(Vector3 at) => Signed(at, Form);

        /// <summary>
        /// Measures how far a point is inside a given outline of this piece of land.
        /// </summary>
        /// <param name="at">Point to measure; its height is ignored.</param>
        /// <param name="form">Which outline, already read out of <see cref="Shape"/>.</param>
        /// <returns>Metres to that outline: positive inside, negative outside.</returns>
        /// <remarks>
        /// For the one caller that asks this half a million times. <see cref="Form"/> reads
        /// a word out of a file, and reading a word is nothing at all until it happens once
        /// per shape per cell - which is the same reason <see cref="SurfaceField"/> reads
        /// the surface table into an array before it walks a grid. Everything else calls
        /// <see cref="Signed(Vector3)"/> and lets this class say what shape it is.
        /// </remarks>
        public float Signed(Vector3 at, LandShape form)
        {
            if (form == LandShape.Ellipse)
            {
                return SignedInEllipse(at);
            }

            float outsideX = Mathf.Max(MinX - at.x, at.x - MaxX);
            float outsideZ = Mathf.Max(MinZ - at.z, at.z - MaxZ);
            float beyond = new Vector2(Mathf.Max(outsideX, 0.0f), Mathf.Max(outsideZ, 0.0f)).magnitude;
            float within = Mathf.Min(Mathf.Max(outsideX, outsideZ), 0.0f);
            return -(beyond + within);
        }

        /// <summary>
        /// Reports whether a point is on this land with room to spare on every side.
        /// </summary>
        /// <param name="at">Point to test; its height is ignored.</param>
        /// <param name="margin">Metres of clearance demanded from every edge.</param>
        /// <returns><c>true</c> when the point is that far inside the outline.</returns>
        /// <remarks>
        /// What a placement check wants against <em>one rectangle</em>. Anything asking
        /// whether a thing stands on the map wants <see cref="LevelDefinition.IsOnLand"/>
        /// instead, which measures against the realised coast and so has no seam where two
        /// pieces meet.
        /// </remarks>
        public bool Contains(Vector3 at, float margin) => Signed(at) >= margin;

        /// <summary>
        /// Measures how far a point is inside the ellipse inscribed in this box.
        /// </summary>
        /// <param name="at">Point to measure; its height is ignored.</param>
        /// <returns>Metres to the outline: positive inside, negative outside.</returns>
        /// <remarks>
        /// The ellipse equation is <c>k = 1</c> on the outline, and how quickly <c>k</c>
        /// changes underfoot turns "how wrong is k here" into "how many metres away is it".
        /// Exactly right for a circle, where the gradient is the same everywhere.
        /// </remarks>
        private float SignedInEllipse(Vector3 at)
        {
            float acrossHalf = Width * 0.5f;
            float upHalf = Depth * 0.5f;
            if (acrossHalf <= 0.0f || upHalf <= 0.0f)
            {
                return -1.0f;
            }

            Vector3 middle = Centre;
            float across = (at.x - middle.x) / acrossHalf;
            float up = (at.z - middle.z) / upHalf;
            float outline = Mathf.Sqrt((across * across) + (up * up));

            // Dead centre: every direction is the same distance out, and the gradient below
            // is zero there, so answer with the nearer semi-axis rather than dividing by it.
            if (outline < 0.0001f)
            {
                return Mathf.Min(acrossHalf, upHalf);
            }

            var slope = new Vector2(across / acrossHalf, up / upHalf);
            return outline * (1.0f - outline) / slope.magnitude;
        }
    }
}
