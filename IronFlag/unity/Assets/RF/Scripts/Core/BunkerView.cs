using UnityEngine;
using IronFlag.UI;

namespace IronFlag.Core
{
    /// <summary>
    /// How a player is shown their own base while they are choosing what to drive out of it:
    /// where the camera stands, and how bright each bay is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The framing has one job and it is not "look at the bunker". It is to fill the picture
    /// with the hall - four bays and a shaft - in a viewport whose shape is not known until
    /// the match starts, because two players share the screen and one does not. So the
    /// distance is solved rather than written down, and the picture is hung from the
    /// <em>top</em> of the hall's cutaway face rather than centred on the bays: above that
    /// line there is no world, only the underside of the sea slab and then sky, and a shot of
    /// an underground room with sky in it is the one framing error this view can make.
    /// </para>
    /// <para>
    /// The heading is the bunker's own, which is a deliberate break from the rule
    /// <see cref="TopDownCameraRig"/> keeps for the battlefield. That rule exists so two
    /// players sharing a screen agree about which way north is; nobody is navigating while
    /// they are indoors choosing, and a base whose front wall is missing has exactly one side
    /// it can be looked at from.
    /// </para>
    /// <para>
    /// All the maths is static and takes numbers, so the framing can be checked without a
    /// scene, a camera or a hall.
    /// </para>
    /// </remarks>
    public static class BunkerView
    {
        /// <summary>Downward tilt of the select camera, in degrees.</summary>
        /// <remarks>
        /// Nearly level, because a cutaway is an elevation: pitch this view like the
        /// battlefield's 58 degrees and the four bays become four foreshortened slots. The
        /// few degrees it does have are what stop the decks reading as lines.
        /// </remarks>
        public const float PitchDegrees = 10.0f;

        /// <summary>How far the view is turned from the bunker's own heading, in degrees.</summary>
        /// <remarks>
        /// Half a turn, and exactly half a turn: the bunker faces the field and the camera
        /// stands out on the field looking back at it. Anything off that axis looks straight
        /// through a side wall the cutaway does not build, into an empty room.
        /// </remarks>
        public const float YawOffsetDegrees = 180.0f;

        /// <summary>How much wider than the hall the picture is drawn.</summary>
        /// <remarks>
        /// Small: the facade is far bigger than the frame in every direction, so this is
        /// about not cropping the outer bays rather than about leaving room around them.
        /// </remarks>
        public const float FramingMargin = 1.08f;

        /// <summary>Closest the camera is ever allowed to stand, in metres.</summary>
        public const float MinimumDistance = 5.0f;

        /// <summary>How much of the bottom of the picture the console strip takes.</summary>
        /// <remarks>
        /// A fixed share rather than the console's real height, and the two are only the same
        /// number on a full screen. The console is laid out in canvas units against a fixed
        /// reference width, so on the letterbox half of a split screen it covers twice the
        /// fraction it covers on a whole one - and framing for that would push the camera so
        /// far back that the base sat in the middle of a field of rock. This is the share the
        /// framing promises to keep clear; on a shared screen the strip sits in front of the
        /// bottom of the lower bays, which is where the console in the game this is a homage
        /// to sat anyway.
        /// </remarks>
        public const float ConsoleShare = 0.22f;

        /// <summary>Emission property on URP's Lit shader.</summary>
        public static readonly int EmissionProperty = Shader.PropertyToID("_EmissionColor");

        /// <summary>What a bay's lamp emits while nobody has chosen it.</summary>
        /// <remarks>
        /// The same numbers <c>GeneratedMaterials.BayLightEmission</c> bakes into the shared
        /// asset, so a bay that has never been written to and one that has just been dimmed
        /// look identical. They are written out again here rather than shared because that
        /// class is editor-only and this one runs in a built player.
        /// </remarks>
        private static readonly Color RestingEmission = new Color(1.90f, 1.42f, 0.86f);

        /// <summary>How much harder the chosen bay's lamp burns.</summary>
        /// <remarks>
        /// 1.8, which lands the chosen lamp at 3.4 - the same ceiling M3 settled on for a
        /// blast, and for the same reason: past about four, emission clips to a flat white
        /// shape with no colour left in it.
        /// </remarks>
        private const float ChosenGain = 1.8f;

        /// <summary>Colour of the light a bay throws while nobody has chosen it.</summary>
        private static readonly Color RestingGlow = new Color(1.00f, 0.88f, 0.68f);

        /// <summary>How much of the side's own colour the chosen bay's light takes.</summary>
        private const float ChosenTint = 0.35f;

        /// <summary>Point-light intensity in a bay nobody has chosen.</summary>
        /// <remarks>
        /// Read against <c>LightingTuning.SunIntensity</c>, which is 1.5: these are three to
        /// six times the sun because they are the only light in a room the sun cannot reach,
        /// and they fall off over eleven metres rather than not at all.
        /// </remarks>
        private const float RestingIntensity = 4.6f;

        /// <summary>Point-light intensity in the chosen bay.</summary>
        private const float ChosenIntensity = 9.5f;

        /// <summary>How far a bay's light reaches, in metres.</summary>
        public const float LampRange = 11.0f;

        /// <summary>
        /// Returns what one bay's lamp emits.
        /// </summary>
        /// <param name="chosen">Whether this is the bay being chosen.</param>
        /// <returns>An emission colour for the lamp's material property block.</returns>
        public static Color LampEmission(bool chosen)
            => chosen ? RestingEmission * ChosenGain : RestingEmission;

        /// <summary>
        /// Returns how hard one bay's point light burns.
        /// </summary>
        /// <param name="chosen">Whether this is the bay being chosen.</param>
        /// <returns>An intensity for the light.</returns>
        public static float LampIntensity(bool chosen)
            => chosen ? ChosenIntensity : RestingIntensity;

        /// <summary>
        /// Returns the colour of the light one bay throws.
        /// </summary>
        /// <param name="chosen">Whether this is the bay being chosen.</param>
        /// <param name="side">Side the bunker belongs to.</param>
        /// <returns>A light colour, warm white for a resting bay.</returns>
        /// <remarks>
        /// The chosen bay is washed a third of the way towards its own side's colour rather
        /// than all of it. This is the one place in the game where a side's colour is a light
        /// rather than paint, and at full strength it stops reading as a room with the lights
        /// on and starts reading as a coloured filter over one.
        /// </remarks>
        public static Color LampColour(bool chosen, Team side)
            => chosen ? Color.Lerp(RestingGlow, HudPalette.For(side), ChosenTint) : RestingGlow;

        /// <summary>
        /// Returns how far back the camera has to stand to fit the hall in the picture.
        /// </summary>
        /// <param name="halfWidth">Half the width of what has to be visible, in metres.</param>
        /// <param name="halfHeight">Half its height, in metres.</param>
        /// <param name="verticalFovDegrees">The camera's vertical field of view.</param>
        /// <param name="aspect">The viewport's width over its height.</param>
        /// <returns>Metres back along the view direction.</returns>
        /// <remarks>
        /// Whichever of the two fits worse wins, which is what makes one number right for a
        /// full screen and for the letterbox half of a split one - the second is three times
        /// as wide as it is tall, so it is the height that decides, and the first is not.
        /// </remarks>
        public static float SolveDistance(
            float halfWidth, float halfHeight, float verticalFovDegrees, float aspect)
        {
            float rise = Mathf.Tan(Mathf.Deg2Rad * Mathf.Clamp(verticalFovDegrees, 1.0f, 179.0f) * 0.5f);
            float forHeight = Mathf.Max(0.0f, halfHeight) / rise;
            float forWidth = Mathf.Max(0.0f, halfWidth) / (rise * Mathf.Max(0.01f, aspect));
            return Mathf.Max(MinimumDistance, Mathf.Max(forHeight, forWidth) * FramingMargin);
        }

        /// <summary>
        /// Returns half the height the picture covers at the distance it is taken from.
        /// </summary>
        /// <param name="distance">Metres back along the view direction.</param>
        /// <param name="verticalFovDegrees">The camera's vertical field of view.</param>
        /// <returns>Half the visible height at the focus plane, in metres.</returns>
        public static float SolveHalfFrame(float distance, float verticalFovDegrees)
            => distance * Mathf.Tan(Mathf.Deg2Rad * Mathf.Clamp(verticalFovDegrees, 1.0f, 179.0f) * 0.5f);

        /// <summary>
        /// Returns the height to centre the picture on.
        /// </summary>
        /// <param name="skyline">Height of the top of the hall's cutaway face.</param>
        /// <param name="halfFrame">Half the height the picture covers.</param>
        /// <returns>The height of the focus point.</returns>
        /// <remarks>
        /// The top edge of the picture is put on the top edge of the hall, not the middle on
        /// the middle. Everything the hall does not cover is below it, where the facade
        /// carries on for ten metres past the lowest bay; above it there is nothing to draw
        /// but sea and sky.
        /// </remarks>
        public static float SolveFocusHeight(float skyline, float halfFrame)
            => skyline - halfFrame;

        /// <summary>
        /// Works out where a player's camera stands to look into their own base.
        /// </summary>
        /// <param name="bunker">The bunker being looked into.</param>
        /// <param name="verticalFovDegrees">The camera's vertical field of view.</param>
        /// <param name="aspect">The viewport's width over its height.</param>
        /// <returns>The pose to park the camera at.</returns>
        public static CutawayPose Solve(TeamBunker bunker, float verticalFovDegrees, float aspect)
        {
            if (bunker == null)
            {
                return new CutawayPose(Vector3.zero, PitchDegrees, 0.0f, MinimumDistance);
            }

            Transform stand = bunker.transform;
            float halfWidth = 1.0f;
            float lowest = bunker.SkylineHeight;
            Vector3 middle = Vector3.zero;
            int counted = 0;

            for (int slot = 0; slot < bunker.BayCount; slot++)
            {
                Transform deck = bunker.BayNode(slot);
                if (deck == null)
                {
                    continue;
                }

                Vector3 local = stand.InverseTransformPoint(deck.position);
                halfWidth = Mathf.Max(halfWidth, Mathf.Abs(local.x) + HalfDeck(deck));
                lowest = Mathf.Min(lowest, deck.position.y);
                middle += new Vector3(local.x, 0.0f, local.z);
                counted++;
            }

            if (counted > 0)
            {
                middle /= counted;
            }

            float skyline = bunker.SkylineHeight;

            // Divided by what the console leaves, so the hall lands in the part of the
            // picture nothing is sitting in front of rather than in the whole of it.
            float halfHeight = Mathf.Max(0.5f, (skyline - lowest) * 0.5f) / (1.0f - ConsoleShare);
            float distance = SolveDistance(halfWidth, halfHeight, verticalFovDegrees, aspect);
            float focusHeight = SolveFocusHeight(skyline, SolveHalfFrame(distance, verticalFovDegrees));

            Vector3 planar = stand.TransformPoint(middle);
            return new CutawayPose(
                new Vector3(planar.x, focusHeight, planar.z),
                PitchDegrees,
                bunker.FacingYawDegrees + YawOffsetDegrees,
                distance);
        }

        /// <summary>
        /// Returns half the width of one bay deck, measured off the mesh it was exported as.
        /// </summary>
        /// <param name="deck">The bay deck.</param>
        /// <returns>Half its width across the bunker's own x axis, in metres.</returns>
        /// <remarks>
        /// Measured rather than written down, so a hall whose bays are widened in Blender is
        /// framed correctly without anybody editing this file. A deck with no mesh - which is
        /// what a marker built by a test is - contributes nothing but its own position.
        /// </remarks>
        private static float HalfDeck(Transform deck)
        {
            var filter = deck.GetComponent<MeshFilter>();
            return filter == null || filter.sharedMesh == null
                ? 0.0f
                : filter.sharedMesh.bounds.extents.x;
        }
    }
}
