using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace IronFlag.Core
{
    /// <summary>
    /// What happens to the frame after it is drawn: the tone curve that fits it into a
    /// screen, the glow around the things that were authored to glow, the grade, and the
    /// corner falloff.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A table in one file for the same reason as <see cref="LightingTuning"/>: the numbers
    /// only mean anything against each other. The difference is where they end up - these are
    /// stamped onto <c>Assets/Settings/DefaultVolumeProfile.asset</c> by
    /// <c>Tools &gt; IronFlag &gt; Build Volume Profile</c> rather than applied to a scene, in
    /// the same generated-not-authored arrangement as
    /// <see cref="IronFlag.Levels.SurfaceTuning"/> and the materials it feeds. The asset is
    /// the thing URP reads; this is the thing a diff can argue with.
    /// </para>
    /// <para>
    /// <strong>Two things had to be true before any of this was visible at all.</strong> HDR
    /// has to be on for bloom to have anything above 1.0 to find, and it already was on both
    /// pipeline tiers. The other was not: <c>renderPostProcessing</c> defaults to <c>false</c>
    /// on a URP camera and nothing in the project had ever set it, so the profile was inert
    /// twice over - neutral values that no camera was asking for. <see cref="ViewStack"/> is
    /// the half that fixes the camera; this is the half that fixes the values.
    /// </para>
    /// <para>
    /// <strong>Neutral rather than ACES</strong>, which is the one choice here worth arguing
    /// about. ACES is the usual answer and gives a more filmic roll-off, but it also shifts
    /// hue and pulls saturation out of exactly the flat hand-picked palette that is this
    /// game's whole art direction - the palette is the homage, and a tone curve that quietly
    /// restyles it is not a neutral improvement. Neutral does the job that was actually
    /// needed, which is stopping a 3.4 emissive from clipping to white, and leaves the colours
    /// where <c>blender/rf/palette.py</c> put them.
    /// </para>
    /// </remarks>
    public static class PostTuning
    {
        /// <summary>
        /// The tone curve applied to the frame.
        /// </summary>
        /// <remarks>See the class remarks for why this is not ACES.</remarks>
        public const TonemappingMode Tonemapping = TonemappingMode.Neutral;

        /// <summary>
        /// How bright a pixel has to be before it blooms.
        /// </summary>
        /// <remarks>
        /// Sat just above 1.0 on purpose, which puts it above everything that is merely lit
        /// and below every emissive in the game: the head-lights start at 1.5 and the muzzle
        /// blast reaches 3.4, all of them authored with a bloom that did not exist yet. It is
        /// also what keeps a white HUD label from glowing, on top of the HUD being drawn by a
        /// camera that skips post entirely - see <see cref="ViewStack"/>.
        /// </remarks>
        public const float BloomThreshold = 1.05f;

        /// <summary>How much of that glow to add back.</summary>
        /// <remarks>
        /// Low. What is wanted is a tracer that reads as hot and a muzzle flash that throws
        /// light, not a soft filter over a game whose subject is small vehicles read at thirty
        /// four metres.
        /// </remarks>
        public const float BloomIntensity = 0.45f;

        /// <summary>How far the glow spreads.</summary>
        public const float BloomScatter = 0.62f;

        /// <summary>Overall exposure adjustment, in stops.</summary>
        /// <remarks>
        /// Zero: the lighting is already where it should be, and correcting it here would be
        /// correcting it in the wrong place. This exists so the next mood has a knob to reach
        /// for rather than because midday needs one.
        /// </remarks>
        public const float PostExposure = 0.0f;

        /// <summary>Contrast lift, as a percentage.</summary>
        /// <remarks>
        /// Small, and paying for the tone curve rather than restyling anything: a range
        /// remap flattens the midtones slightly, and this puts back roughly what it took.
        /// </remarks>
        public const float Contrast = 6.0f;

        /// <summary>Saturation lift, as a percentage.</summary>
        public const float Saturation = 4.0f;

        /// <summary>How dark the corners of the frame go.</summary>
        /// <remarks>
        /// Deliberately almost nothing. A vignette is drawn per camera, and this game can
        /// have four of them at once: at any real strength a four-way split stops reading as
        /// one screen divided and starts reading as four framed pictures. It earns its place
        /// at this strength by settling the eye towards the vehicle and no further.
        /// </remarks>
        public const float VignetteIntensity = 0.16f;

        /// <summary>How gradually the vignette arrives.</summary>
        public const float VignetteSmoothness = 0.45f;

        /// <summary>
        /// The anti-aliasing a world camera resolves with.
        /// </summary>
        /// <remarks>
        /// Every edge in this game is the hard silhouette of an untextured primitive against
        /// a flat colour, which is the worst case for aliasing and the best case for a
        /// morphological pass to fix. Chosen over turning MSAA on in the pipeline assets
        /// because it costs a fraction as much and this content - no alpha test, no foliage -
        /// is what SMAA is good at.
        /// </remarks>
        public const AntialiasingMode Antialiasing =
            AntialiasingMode.SubpixelMorphologicalAntiAliasing;

        /// <summary>Quality of that anti-aliasing pass.</summary>
        public const AntialiasingQuality AntialiasingLevel = AntialiasingQuality.High;

        /// <summary>Tint applied to the bloom, which is none.</summary>
        public static Color BloomTint => Color.white;

        /// <summary>Colour the corners of the frame fall off towards.</summary>
        public static Color VignetteColour => Color.black;
    }
}
