using NUnit.Framework;
using UnityEngine;
using IronFlag.Core;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// The framing and the lighting of the select view, checked as arithmetic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every number here is one that decides what a player sees when they are choosing a
    /// vehicle, and every one of them fails silently: a camera half a metre too close crops
    /// a bay, one two metres too far back puts sky above an underground room, and a lamp one
    /// step too bright is a white rectangle rather than a lit ceiling. None of those throw,
    /// and all of them are two multiplications.
    /// </para>
    /// <para>
    /// The shapes fed in are the real hall's, written out rather than read off the model, so
    /// these still mean something when the <c>.glb</c> is missing - and so a change to the
    /// hall that breaks the framing shows up here as two numbers that no longer agree rather
    /// than as a still nobody looked at.
    /// </para>
    /// </remarks>
    public sealed class BunkerViewTests
    {
        /// <summary>Half the width of the shipped hall's bays, in metres.</summary>
        private const float HallHalfWidth = 9.4f;

        /// <summary>Top of the shipped hall's cutaway face, in metres.</summary>
        private const float HallTop = -4.2f;

        /// <summary>Deck of its lowest bay, in metres.</summary>
        private const float HallFloor = -13.78f;

        /// <summary>Bottom of its cutaway face: how far the picture may run before it is sky.</summary>
        private const float FaceBottom = -24.0f;

        /// <summary>The vertical field of view every camera in this game uses.</summary>
        private const float Fov = 60.0f;

        /// <summary>
        /// The shapes of viewport this game actually produces: a whole screen, and the
        /// letterbox half of a shared one.
        /// </summary>
        /// <remarks>
        /// Three and a half to one is not a hypothetical - it is what 1920x1080 split between
        /// two players is, and it is the shape that decides the framing, because the hall is
        /// about twice as wide as it is tall and that viewport is three and a half times.
        /// </remarks>
        private static readonly float[] Viewports = { 16.0f / 9.0f, 1920.0f / 540.0f, 4.0f / 3.0f };

        /// <summary>
        /// Whatever the screen is shared out as, the whole hall is in the picture.
        /// </summary>
        [Test]
        public void EveryBayIsInThePictureWhateverShapeTheViewportIs()
        {
            foreach (float aspect in Viewports)
            {
                (float top, float bottom, float halfWidth) = Frame(aspect);

                Assert.That(
                    halfWidth,
                    Is.GreaterThanOrEqualTo(HallHalfWidth),
                    $"an outer bay is cropped at {aspect:0.00}:1");
                Assert.That(
                    top,
                    Is.GreaterThanOrEqualTo(HallTop - 0.001f),
                    $"the top of the hall is cut off at {aspect:0.00}:1");
                Assert.That(
                    bottom,
                    Is.LessThanOrEqualTo(HallFloor),
                    $"the lowest bay's deck is below the picture at {aspect:0.00}:1");
            }
        }

        /// <summary>
        /// Nothing above the hall is ever in shot, because there is nothing up there to draw
        /// but the underside of the sea and then sky.
        /// </summary>
        /// <remarks>
        /// This is the one framing mistake this view can make that looks like a bug in the
        /// world rather than in the camera. The hall hangs below the sea slab, and a level's
        /// sea is a box as wide as the whole map: a picture whose top edge clears the hall
        /// gets a sheet of water drawn across it.
        /// </remarks>
        [Test]
        public void TheTopOfThePictureIsTheTopOfTheHall()
        {
            foreach (float aspect in Viewports)
            {
                (float top, float bottom, float _) = Frame(aspect);

                Assert.That(
                    top,
                    Is.EqualTo(HallTop).Within(0.001f),
                    $"the picture is not hung from the top of the hall at {aspect:0.00}:1");
                Assert.That(
                    bottom,
                    Is.GreaterThanOrEqualTo(FaceBottom),
                    $"the picture runs off the bottom of the cutaway face at {aspect:0.00}:1");
            }
        }

        /// <summary>
        /// The console gets its share of the bottom of the picture, and on a whole screen the
        /// bays stay clear of it.
        /// </summary>
        /// <remarks>
        /// Only on a whole screen, and that is deliberate rather than a weaker assertion: the
        /// console is laid out in canvas units against a fixed reference width, so on a
        /// shared screen it covers twice the fraction of the picture and framing round it
        /// would push the camera far enough back to lose the hall in a field of rock. What
        /// the framing promises is a fixed share; what a shared screen gets is a console
        /// standing in front of the bottom of the lower bays.
        /// </remarks>
        [Test]
        public void TheConsoleGetsTheBottomOfThePictureToItself()
        {
            (float top, float bottom, float _) = Frame(16.0f / 9.0f);
            float console = bottom + ((top - bottom) * BunkerView.ConsoleShare);

            Assert.That(
                console,
                Is.LessThanOrEqualTo(HallFloor),
                "the console strip covers the deck the lower bays' vehicles stand on");
        }

        /// <summary>
        /// The select camera looks back along the bunker's own heading, and nearly level.
        /// </summary>
        /// <remarks>
        /// Both halves are a deliberate break from the rule the battlefield camera keeps. The
        /// fixed heading exists so two players sharing a screen agree about which way north
        /// is, and nobody is navigating while they are indoors; the 58-degree tilt exists to
        /// show silhouettes from above, and a cutaway is an elevation.
        /// </remarks>
        [Test]
        public void TheSelectViewIsAnElevationTakenFromTheBunkersOwnSide()
        {
            Assert.That(
                BunkerView.YawOffsetDegrees,
                Is.EqualTo(180.0f),
                "the select camera is not standing in front of the bunker it is looking into");
            Assert.That(
                BunkerView.PitchDegrees,
                Is.InRange(4.0f, 20.0f),
                "the select view is tilted like the battlefield, which flattens the bays");
        }

        /// <summary>
        /// The chosen bay reads brighter than the rest without going to flat white.
        /// </summary>
        /// <remarks>
        /// Which bay is chosen is said by lighting it, so the gap between the two states has
        /// to be big enough to see. The ceiling is M3's: emission much past four clips to a
        /// white shape with no colour left in it, which is a worse answer than a dim one
        /// because it is the same shape whatever the number.
        /// </remarks>
        [Test]
        public void ChoosingABayLightsItWithoutBlowingItOut()
        {
            Color resting = BunkerView.LampEmission(false);
            Color chosen = BunkerView.LampEmission(true);

            Assert.That(
                chosen.maxColorComponent,
                Is.GreaterThan(resting.maxColorComponent * 1.4f),
                "the chosen bay is not visibly brighter than the others");
            Assert.That(
                chosen.maxColorComponent,
                Is.LessThan(4.0f),
                "the chosen bay's lamp clips to a flat white rectangle");
            Assert.That(
                BunkerView.LampIntensity(true),
                Is.GreaterThan(BunkerView.LampIntensity(false)),
                "the chosen bay throws no more light than a resting one");
        }

        /// <summary>
        /// A resting bay is warm white and a chosen one is only washed towards its side.
        /// </summary>
        /// <remarks>
        /// This is the only place in the game where a side's colour is a light rather than
        /// paint. At full strength it stops reading as a room with the lights on and starts
        /// reading as a coloured filter over one, so the chosen bay has to stay closer to
        /// white than to the accent.
        /// </remarks>
        [Test]
        public void ABayIsLitWarmWhiteAndOnlyWashedTowardsItsSide()
        {
            Color resting = BunkerView.LampColour(false, Team.Green);
            Color green = BunkerView.LampColour(true, Team.Green);
            Color brown = BunkerView.LampColour(true, Team.Brown);

            Assert.That(resting.r, Is.GreaterThan(resting.b), "a bay lamp is not a warm light");
            Assert.That(
                green, Is.Not.EqualTo(brown), "both sides' bays are lit exactly the same colour");
            Assert.That(
                Distance(green, resting),
                Is.LessThan(Distance(green, HudColourOf(Team.Green))),
                "the chosen bay is closer to the team colour than to a working light");
        }

        /// <summary>
        /// A bunker with nothing underneath it frames from its own ground, rather than
        /// solving for a hall that is not there.
        /// </summary>
        [Test]
        public void ABunkerWithNoHallStillHasAPose()
        {
            var host = new GameObject("Bunker");
            try
            {
                host.transform.position = new Vector3(3.0f, 0.0f, -70.0f);
                TeamBunker bunker = host.AddComponent<TeamBunker>();
                bunker.Configure(Team.Green, null, null);

                CutawayPose pose = BunkerView.Solve(bunker, Fov, 16.0f / 9.0f);

                Assert.That(bunker.HasHall, Is.False, "a bunker built from nothing claims a hall");
                Assert.That(
                    pose.Distance,
                    Is.GreaterThanOrEqualTo(BunkerView.MinimumDistance),
                    "the camera was put inside the building");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// Returns the top, bottom and half-width of the picture the select view takes.
        /// </summary>
        /// <param name="aspect">Width over height of the viewport.</param>
        /// <returns>Top and bottom in world heights, and half the visible width in metres.</returns>
        private static (float Top, float Bottom, float HalfWidth) Frame(float aspect)
        {
            float halfHeight = ((HallTop - HallFloor) * 0.5f) / (1.0f - BunkerView.ConsoleShare);
            float distance = BunkerView.SolveDistance(HallHalfWidth, halfHeight, Fov, aspect);
            float halfFrame = BunkerView.SolveHalfFrame(distance, Fov);
            float focus = BunkerView.SolveFocusHeight(HallTop, halfFrame);

            return (focus + halfFrame, focus - halfFrame, halfFrame * aspect);
        }

        private static float Distance(Color one, Color other)
            => Mathf.Abs(one.r - other.r) + Mathf.Abs(one.g - other.g) + Mathf.Abs(one.b - other.b);

        private static Color HudColourOf(Team side) => IronFlag.UI.HudPalette.For(side);
    }
}
