using System;
using UnityEngine;

namespace IronFlag.Core
{
    /// <summary>
    /// What one lighting condition is: where the sun is and what colour, what fills the
    /// shadows, what the air does with distance, and what is behind everything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A table in one file, for the same reason as
    /// <see cref="IronFlag.Levels.SurfaceTuning.For"/> and
    /// <see cref="IronFlag.Vehicles.VehicleTuning.For"/>: a look is balanced by reading its
    /// numbers against each other, and a diff can show that where a hand-edited lighting
    /// asset cannot. Everything that lights an IronFlag scene - the match, the level editor,
    /// the map overview, the art preview - goes through <see cref="LightingRig.Apply"/> with
    /// one of these, so there is exactly one answer to what the game looks like.
    /// </para>
    /// <para>
    /// <strong>Colours are gamma, the way URP takes them</strong> - the same convention
    /// <see cref="IronFlag.Levels.SurfaceTuning"/> documents, and for the same reason: these
    /// values are set on materials and on <see cref="RenderSettings"/>, both of which take
    /// the number a colour picker shows rather than its linear square.
    /// </para>
    /// <para>
    /// <strong>What the sky is actually for here.</strong> At the gameplay camera's 58 degree
    /// pitch and 50 degree field of view the top of the frame still points 33 degrees
    /// <em>below</em> the horizon, so a player never sees sky. That is not a reason to leave
    /// it alone - it is the reason the sky is tuned the way it is, because it shows up in two
    /// other places instead. The first is reflection: the scene's reflection source is the
    /// skybox, so <see cref="SkyTint"/> and <see cref="SkyExposure"/> are what the METAL
    /// palette on barrels, rails and gun metal has to reflect, and it read flat before this
    /// because what it was reflecting was Unity's untouched default. The second is the edge
    /// of the world: a level's sea is a slab exactly twice
    /// <see cref="IronFlag.Levels.LevelBounds.HalfExtent"/> across, and a camera looking down
    /// past its rim - which happens from any coast, well inside the visible range - sees the
    /// skybox's <em>lower</em> hemisphere through the gap. That is why
    /// <see cref="SkyGround"/> is a sea colour and not a ground colour: it is the paint on
    /// the hole, and it is the one sky value a player reliably sees.
    /// </para>
    /// <para>
    /// <strong>Fog is a gameplay-camera effect and says so.</strong> It is switched off by
    /// the two views that look at the world from 200 metres up - the map overview and the
    /// level editor - because at that range a haze tuned for a chase camera renders the whole
    /// map as one flat wash. Those callers turn <see cref="Fog"/> off on their own copy
    /// rather than getting a mood of their own, because the rest of the row is exactly what
    /// they want and a second near-identical row is a second thing to keep in step.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// LightingTuning lighting = LightingTuning.For(LightingMood.Daylight);
    /// LightingRig.Apply(lighting, sky);
    /// </code>
    /// </example>
    [Serializable]
    public sealed class LightingTuning
    {
        /// <summary>How far above the horizon the sun stands, in degrees.</summary>
        public float SunPitch = 52.0f;

        /// <summary>Which way the sun comes from, in degrees.</summary>
        public float SunYaw = -30.0f;

        /// <summary>Colour of the direct light.</summary>
        public Color SunColour = Color.white;

        /// <summary>Strength of the direct light.</summary>
        public float SunIntensity = 1.5f;

        /// <summary>Ambient light arriving from overhead.</summary>
        public Color AmbientSky = Color.grey;

        /// <summary>Ambient light arriving from the sides.</summary>
        public Color AmbientEquator = Color.grey;

        /// <summary>Ambient light bounced back off the ground.</summary>
        public Color AmbientGround = Color.grey;

        /// <summary>Whether distance haze is drawn at all.</summary>
        public bool Fog;

        /// <summary>Colour distant geometry fades towards.</summary>
        public Color FogColour = Color.grey;

        /// <summary>How far away the haze starts, in metres.</summary>
        public float FogStart = 55.0f;

        /// <summary>How far away the haze is complete, in metres.</summary>
        public float FogEnd = 200.0f;

        /// <summary>Tint of the sky's upper hemisphere.</summary>
        public Color SkyTint = Color.grey;

        /// <summary>
        /// Colour of the sky's lower hemisphere, which is what shows through the gap past
        /// the edge of a level's sea slab.
        /// </summary>
        public Color SkyGround = Color.grey;

        /// <summary>Overall brightness of the sky, which is also its brightness as a reflection.</summary>
        public float SkyExposure = 1.15f;

        /// <summary>How much air the sun is seen through; higher is hazier and warmer.</summary>
        public float SkyAtmosphere = 1.0f;

        /// <summary>Angular size of the sun's disc, as the procedural sky measures it.</summary>
        public float SunDiscSize = 0.035f;

        /// <summary>
        /// Returns the lighting for one condition.
        /// </summary>
        /// <param name="mood">Which condition to light for.</param>
        /// <returns>
        /// A fresh copy, so callers can stamp and edit it - which is how the two overhead
        /// views drop the fog. An unrecognised mood - including
        /// <see cref="LightingMood.None"/> - answers with the
        /// <see cref="LightingMood.Daylight"/> row, because the fallback for a scene's
        /// lighting has to be one you can play under.
        /// </returns>
        /// <example>
        /// <code>
        /// LightingTuning lighting = LightingTuning.For(LightingMood.Daylight);
        /// lighting.Fog = false;
        /// </code>
        /// </example>
        public static LightingTuning For(LightingMood mood)
        {
            switch (mood)
            {
                case LightingMood.Studio:
                    return new LightingTuning
                    {
                        // The art preview's own numbers, carried over unchanged from the
                        // private copy of this it used to keep: the sun four degrees lower and
                        // round from the other side, slightly weaker, and a slightly darker
                        // fill. A model being photographed wants even light across it rather
                        // than a read at a glance, and a stand is not a map.
                        SunPitch = 48.0f,
                        SunYaw = -35.0f,
                        SunColour = new Color(1.0f, 0.97f, 0.9f),
                        SunIntensity = 1.4f,

                        AmbientSky = new Color(0.42f, 0.48f, 0.58f),
                        AmbientEquator = new Color(0.32f, 0.34f, 0.35f),
                        AmbientGround = new Color(0.18f, 0.17f, 0.15f),

                        // No haze: a backdrop has no far edge to lose, and a model measured
                        // through fog is a model measured wrong.
                        Fog = false,

                        SkyTint = new Color(0.46f, 0.52f, 0.62f),

                        // The one row where this is not a sea colour, because an art preview
                        // has no sea and no hole to paint - just a neutral floor to the
                        // reflection a model is photographed wearing.
                        SkyGround = new Color(0.22f, 0.22f, 0.22f),

                        SkyExposure = 1.15f,
                        SkyAtmosphere = 1.0f,
                        SunDiscSize = 0.035f,
                    };

                default:
                    return new LightingTuning
                    {
                        // Unchanged from what the sandbox has always used. The brief for this
                        // pass was depth, not a restyle: the win comes from tone mapping,
                        // occlusion, bloom on emissives that were authored expecting it, and
                        // haze - none of which needs the key light moved. A low warm sun is
                        // the obvious next mood rather than a change to this one.
                        SunPitch = 52.0f,
                        SunYaw = -30.0f,
                        SunColour = new Color(1.0f, 0.97f, 0.9f),
                        SunIntensity = 1.5f,

                        AmbientSky = new Color(0.45f, 0.51f, 0.60f),
                        AmbientEquator = new Color(0.34f, 0.36f, 0.36f),
                        AmbientGround = new Color(0.19f, 0.18f, 0.16f),

                        // Starts past the far edge of the middle of the frame and is never
                        // complete inside the view, so it reads as air rather than as
                        // weather. Deliberately weak: M7 established that a player reads this
                        // map by value contrast, so a haze that lifted the far sea towards
                        // the land would cost more than the depth it bought.
                        Fog = true,
                        FogColour = new Color(0.32f, 0.38f, 0.46f),
                        FogStart = 55.0f,
                        FogEnd = 200.0f,

                        // Matched to AmbientSky, because the two describe the same sky to two
                        // different systems and disagreeing would show up on metal.
                        SkyTint = new Color(0.46f, 0.52f, 0.62f),

                        // A sea colour, not a ground colour - see the class remarks. Deep
                        // water's albedo is 0.035, 0.075, 0.135, but the sea is a lit surface
                        // and comes out brighter than its albedo while this is painted
                        // straight on. This is that difference, and it is a number to measure
                        // off a capture of a coast rather than one to trust.
                        SkyGround = new Color(0.06f, 0.11f, 0.18f),

                        SkyExposure = 1.15f,
                        SkyAtmosphere = 1.0f,
                        SunDiscSize = 0.035f,
                    };
            }
        }
    }
}
