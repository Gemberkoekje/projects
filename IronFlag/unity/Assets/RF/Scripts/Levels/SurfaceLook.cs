using System;
using UnityEngine;

namespace IronFlag.Levels
{
    /// <summary>
    /// How much detail one surface is drawn with: how uneven its grain is, and - for the two
    /// that are water - how it moves, catches the sun and breaks at the shore.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A second table beside <see cref="SurfaceTuning"/> rather than nine more columns in it,
    /// and the split is not filing: that table is <em>what a surface is</em> - what it is
    /// painted, what it does to a vehicle, where its coastline may wander - and every row of
    /// it is argued against a measured value on a map shot. This one is what the surface
    /// shaders are handed, none of it changes a colour or a rule, and all of it could be set
    /// to zero and leave a game that plays identically and looks like the one before this
    /// pass. Keeping them apart is what lets the value ramp go on being argued in one place
    /// by people who never have to read a shader.
    /// </para>
    /// <para>
    /// <strong>Nothing here touches albedo.</strong> Every number below moves a normal, adds
    /// a highlight or paints foam; the colour a surface is painted stays
    /// <see cref="SurfaceTuning.Colour"/> exactly. That is the constraint the whole item was
    /// given - it adds detail, it does not reopen the ramp - and it is why the ramp's
    /// measured figures survive this pass even though the surfaces do not look the same.
    /// </para>
    /// <para>
    /// The numbers that are the same for every surface - foam colour, how sharp a glint is,
    /// how fast the swell travels - are constants below rather than columns, because a number
    /// no row disagrees about is a constant and a constant in a table is a column somebody
    /// reads five times to learn nothing. They are <em>here</em> rather than left as defaults
    /// on the two shaders because a shader's default only ever reaches a material on the day
    /// that material is created: Unity writes every property into the <c>.mat</c> at that
    /// moment and never consults the shader's default again. A constant nobody can change is
    /// worse than a column.
    /// </para>
    /// <para>
    /// Which of the two shaders a surface gets is not a column either: it is
    /// <see cref="SurfaceTuning.Drowns"/>, for the same reason that field already answers
    /// "may a level paint land with this" - the two surfaces that drown you are exactly the
    /// two that are water, and a second way to say so is a second way to be wrong.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// SurfaceLook look = SurfaceLook.For(SurfaceKind.Sand);
    /// material.SetFloat("_DetailStrength", look.Grain);
    /// </code>
    /// </example>
    [Serializable]
    public sealed class SurfaceLook
    {
        /// <summary>What foam is painted.</summary>
        /// <remarks>
        /// Pale, and a long way short of white. The two waters are the darkest things on the
        /// map - the open sea measures 38 - so foam is the largest colour step anywhere on
        /// it, and the first pass ran this at 0.78 and got a coastline that read as pack ice.
        /// It also traced every tooth of the half-cell staircase a natural coastline is
        /// quantised to, which nobody had had to look at before something bright was drawn
        /// along it.
        /// </remarks>
        public static readonly Color FoamColour = new Color(0.62f, 0.69f, 0.72f);

        /// <summary>How much of the foam band is actually foam, in 0..1.</summary>
        /// <remarks>
        /// The main knob, and it is a threshold rather than an amount: the band is a
        /// wandering, breathing value and this is the height it has to clear. Low numbers
        /// give a solid ribbon of foam along the whole coast, which is what a coastline never
        /// looks like; high ones break it into surf that only shows here and there.
        /// </remarks>
        public const float FoamEdge = 0.80f;

        /// <summary>How fast the foam breathes, in surges a second.</summary>
        public const float FoamSpeed = 1.6f;

        /// <summary>How fast the swell travels, as a multiple of each wave's own speed.</summary>
        public const float SwellSpeed = 1.0f;

        /// <summary>How many metres one cell of chop covers.</summary>
        /// <remarks>
        /// Coarser than it would want to be from a metre away, because of where the water is
        /// actually looked at: the level overview draws the whole map from 200 m up, where a
        /// metre is six pixels, and chop finer than this aliases into static. What the
        /// aliasing shows up in is <see cref="Glint"/>.
        /// </remarks>
        public const float ChopScale = 1.8f;

        /// <summary>What a glint is tinted, before the sun's own colour.</summary>
        /// <remarks>
        /// Very nearly white, because it is multiplied by the sun and the sun already carries
        /// the warmth - <see cref="IronFlag.Core.LightingTuning"/> runs it at (1.0, 0.97,
        /// 0.9) times an intensity of 1.5. A warm tint here on top of that came out tan, and
        /// tan flecks on a dark blue sea read as debris rather than as light.
        /// </remarks>
        public static readonly Color GlintColour = new Color(1.0f, 0.98f, 0.94f);

        /// <summary>How tight a glint is: higher is smaller and sparser.</summary>
        public const float GlintSharpness = 220.0f;

        /// <summary>What the water brightens towards where it is seen edge-on.</summary>
        /// <remarks>
        /// A pale sky blue rather than the sky material's own colour, which it is standing in
        /// for. Reading the real one would mean a level's sky and its sea could not be tuned
        /// apart, and this project's sky is barely seen at all.
        /// </remarks>
        public static readonly Color FresnelColour = new Color(0.42f, 0.58f, 0.72f);

        /// <summary>How sharply the edge-on brightening falls off with angle.</summary>
        public const float FresnelPower = 2.0f;

        /// <summary>How far off square the grain tilts the surface, in 0..1.</summary>
        /// <remarks>
        /// <para>
        /// Small on purpose. The number is a slope, not an angle: 0.10 leans the normal
        /// about six degrees at the steepest part of the noise, which under a sun at 52
        /// degrees is a few percent of brightness either way. That is the whole intended
        /// effect - a flat sheet of one colour stops reading as a sheet of one colour - and
        /// anything above about 0.25 stops being detail and starts being terrain the
        /// vehicles are visibly not driving on, because the ground really is flat: every
        /// round in the game resolves on <see cref="IronFlag.Combat.CombatPlane"/>.
        /// </para>
        /// <para>
        /// Read against each other rather than absolutely. Sand is the strongest because it
        /// is the lightest thing on the map by half again and therefore the flattest-reading;
        /// asphalt is the weakest because a road is the one surface somebody built, and the
        /// surfaces table already spends its <see cref="SurfaceTuning.NaturalEdge"/> row
        /// saying so.
        /// </para>
        /// </remarks>
        [Range(0.0f, 1.0f)]
        [Tooltip("How far off square the grain tilts the surface. A slope, not an angle.")]
        public float Grain;

        /// <summary>How many metres one cell of the grain covers.</summary>
        /// <remarks>
        /// Metres because the shader's coordinates are metres - there is no texture and so
        /// no tiling to get wrong. Coarse for sand, which is dunes at two or three metres,
        /// and fine for a road, which is chipping.
        /// </remarks>
        [Tooltip("How many metres one cell of the grain covers.")]
        public float GrainScale = 1.0f;

        /// <summary>How hard the swell tilts the water, in 0..1, or zero on land.</summary>
        /// <remarks>
        /// Nothing is displaced: the water is a flat sheet two centimetres above another
        /// flat sheet, and moving its vertices would open a crack along every coastline. All
        /// a swell does is change which way the surface faces, which is where the light is.
        /// </remarks>
        [Range(0.0f, 1.0f)]
        [Tooltip("How hard the swell tilts the water. Zero on land.")]
        public float Swell;

        /// <summary>How many metres from one wave crest to the next.</summary>
        /// <remarks>
        /// The shelf's is half the open sea's, which is the one thing about these two rows
        /// that is a real observation rather than a preference: waves shorten as the water
        /// shallows. It also keeps the shelf from sharing a beat with the sea it sits in.
        /// </remarks>
        [Tooltip("How many metres from one wave crest to the next.")]
        public float SwellScale = 1.0f;

        /// <summary>How much fine chop rides on top of the swell, in 0..1.</summary>
        [Range(0.0f, 1.0f)]
        [Tooltip("How much fine chop rides on top of the swell.")]
        public float Chop;

        /// <summary>How hard the sun glints off the water, in 0..2.</summary>
        /// <remarks>
        /// The specular the surfaces table deliberately refused. Its
        /// <see cref="SurfaceTuning.Smoothness"/> is still zero for both waters and still
        /// means it - a gloss there is a broad lobe over one enormous flat sheet, which is
        /// exactly what made M7's first sea read <em>lighter</em> than the land it has to
        /// contrast with. This is a different thing: a highlight only a few pixels wide,
        /// taken off the wave normal rather than the plane, which cannot lift the measured
        /// value of the sea because it is not on most of the sea.
        /// </remarks>
        [Range(0.0f, 2.0f)]
        [Tooltip("How hard the sun glints off the water.")]
        public float Glint;

        /// <summary>How much the water brightens where it is seen edge-on, in 0..1.</summary>
        /// <remarks>
        /// Worth having on this map because of where the camera is: it looks down at 58
        /// degrees, so the far half of the frame is near grazing and the near half is not,
        /// and that gradient across one flat sea is most of what stops it reading as paint.
        /// </remarks>
        [Range(0.0f, 1.0f)]
        [Tooltip("How much the water brightens where it is seen edge-on.")]
        public float Fresnel;

        /// <summary>How many metres out from the coast foam is drawn, or zero.</summary>
        /// <remarks>
        /// <para>
        /// Zero everywhere but the shelf, and zero is load-bearing rather than tidy: the
        /// open sea is a slab rather than one of <see cref="SurfaceMesh"/>'s sheets, so it
        /// carries no distance-to-coast at all and every vertex of it reads as being exactly
        /// on the coastline. A sea whose row said 1.6 here would be foam from one horizon to
        /// the other. See <c>RF_Water.shader</c>.
        /// </para>
        /// <para>
        /// Narrower than the shelf is wide - the shelf is <see cref="SurfaceTuning.RimWidth"/>
        /// at five metres - so there is pale shallow water outside the foam rather than foam
        /// running to the edge of the shelf and stopping at a straight line.
        /// </para>
        /// </remarks>
        [Tooltip("How many metres out from the coast foam is drawn, or zero.")]
        public float Foam;

        /// <summary>How much the water pales towards the shore under the foam, in 0..1.</summary>
        /// <remarks>
        /// The soft shore edge, and the reason this pass needs no camera depth texture. It
        /// is a gradient across the foam's own width, so the step from the shelf to the
        /// beach stops being the one hard colour boundary left on the map.
        /// </remarks>
        [Range(0.0f, 1.0f)]
        [Tooltip("How much the water pales towards the shore under the foam.")]
        public float Wash;

        /// <summary>
        /// Returns how one surface is drawn.
        /// </summary>
        /// <param name="kind">Surface to look up.</param>
        /// <returns>
        /// A fresh copy, so callers can stamp and edit it. An unrecognised surface -
        /// including <see cref="SurfaceKind.None"/> - answers with the
        /// <see cref="SurfaceKind.Grass"/> row, matching <see cref="SurfaceTuning.For"/>.
        /// </returns>
        public static SurfaceLook For(SurfaceKind kind)
        {
            switch (kind)
            {
                case SurfaceKind.Sand:
                    return new SurfaceLook
                    {
                        // The strongest grain on the map, at the coarsest scale: a beach is
                        // dunes and scuff at two or three metres, and sand is the surface
                        // with the most to gain because it is the brightest and therefore
                        // the one whose flatness shows.
                        Grain = 0.16f,
                        GrainScale = 2.4f,
                    };

                case SurfaceKind.Asphalt:
                    return new SurfaceLook
                    {
                        // Nearly nothing, at chipping scale. A road reads as built because
                        // it is dead straight and hard-edged; giving it a surface that
                        // wanders would spend the same argument NaturalEdge already won.
                        Grain = 0.05f,
                        GrainScale = 0.8f,
                    };

                case SurfaceKind.ShallowWater:
                    return new SurfaceLook
                    {
                        // Shorter waves than the open sea, and less of them: this is two
                        // vehicle-lengths of water over a shelf, and it is also the band
                        // the foam has to be legible against.
                        Swell = 0.22f,
                        SwellScale = 5.0f,
                        Chop = 0.08f,
                        Glint = 0.14f,
                        Fresnel = 0.13f,

                        // The only row with foam, and the reason SurfaceMesh measures the
                        // coastline into UV1 at all.
                        Foam = 1.4f,
                        Wash = 0.30f,
                    };

                case SurfaceKind.DeepWater:
                    return new SurfaceLook
                    {
                        // Eleven metres crest to crest, which at a 240 m map is about
                        // twenty waves across the sea - enough to read as motion from
                        // thirty-four metres up and far too few to read as noise.
                        Swell = 0.38f,
                        SwellScale = 11.0f,
                        Chop = 0.10f,
                        Glint = 0.12f,
                        Fresnel = 0.18f,

                        // No foam, and not because the open sea has none: it has no
                        // distance-to-coast to draw it from. See Foam.
                        Foam = 0.0f,
                        Wash = 0.0f,
                    };

                default:
                    return new SurfaceLook
                    {
                        // Open country, and the row the other two grains are argued
                        // against. Finer than sand because grass is grass rather than
                        // dunes, and stronger than asphalt because nobody laid it.
                        Grain = 0.10f,
                        GrainScale = 1.2f,
                    };
            }
        }
    }
}
