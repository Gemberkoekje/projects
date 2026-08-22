using NUnit.Framework;
using UnityEngine;
using IronFlag.Core;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// Checks how the screen is divided up, without a screen.
    /// </summary>
    /// <remarks>
    /// A viewport that is wrong by a half is obvious the moment anyone looks at it, but one
    /// that overlaps by a pixel or leaves a gap is not - and neither is a pointer clamped to
    /// the wrong half. All of it is arithmetic, so all of it is checked here.
    /// </remarks>
    public sealed class SplitScreenLayoutTests
    {
        [Test]
        public void OnePlayerGetsTheWholeScreen()
        {
            Assert.That(SplitScreenLayout.ViewportFor(0, 1), Is.EqualTo(SplitScreenLayout.FullScreen));
        }

        [Test]
        public void TwoPlayersSplitTheScreenHorizontally()
        {
            Rect first = SplitScreenLayout.ViewportFor(0, 2);
            Rect second = SplitScreenLayout.ViewportFor(1, 2);

            Assert.That(first, Is.EqualTo(new Rect(0.0f, 0.5f, 1.0f, 0.5f)), "player one is not on top");
            Assert.That(second, Is.EqualTo(new Rect(0.0f, 0.0f, 1.0f, 0.5f)), "player two is not below");
        }

        [Test]
        public void TheViewportsCoverTheScreenExactlyOnce()
        {
            for (int players = 1; players <= SplitScreenLayout.MaxPlayers; players++)
            {
                float covered = 0.0f;
                for (int slot = 0; slot < players; slot++)
                {
                    Rect viewport = SplitScreenLayout.ViewportFor(slot, players);
                    covered += viewport.width * viewport.height;

                    Assert.That(viewport.xMin, Is.GreaterThanOrEqualTo(0.0f), $"{players}p, slot {slot}");
                    Assert.That(viewport.xMax, Is.LessThanOrEqualTo(1.0f), $"{players}p, slot {slot}");
                    Assert.That(viewport.yMin, Is.GreaterThanOrEqualTo(0.0f), $"{players}p, slot {slot}");
                    Assert.That(viewport.yMax, Is.LessThanOrEqualTo(1.0f), $"{players}p, slot {slot}");
                }

                // Three players leave one quadrant empty; everything else fills the screen.
                float expected = players == 3 ? 0.75f : 1.0f;
                Assert.That(covered, Is.EqualTo(expected).Within(0.0001f), $"{players} players");
            }
        }

        [Test]
        public void TwoPlayersViewportsDoNotOverlap()
        {
            Rect first = SplitScreenLayout.ViewportFor(0, 2);
            Rect second = SplitScreenLayout.ViewportFor(1, 2);

            Assert.That(first.Overlaps(second), Is.False);
        }

        [Test]
        public void AnImpossibleSlotFallsBackToTheWholeScreen()
        {
            Assert.That(SplitScreenLayout.ViewportFor(2, 2), Is.EqualTo(SplitScreenLayout.FullScreen));
            Assert.That(SplitScreenLayout.ViewportFor(-1, 2), Is.EqualTo(SplitScreenLayout.FullScreen));
            Assert.That(SplitScreenLayout.ViewportFor(0, 0), Is.EqualTo(SplitScreenLayout.FullScreen));
            Assert.That(SplitScreenLayout.ViewportFor(0, 9), Is.EqualTo(SplitScreenLayout.FullScreen));
        }

        [Test]
        public void APointerInsideAViewportIsLeftWhereItIs()
        {
            Rect lower = SplitScreenLayout.ViewportFor(1, 2);
            var screen = new Vector2(1920.0f, 1080.0f);
            var pointer = new Vector2(400.0f, 200.0f);

            Assert.That(SplitScreenLayout.Contains(lower, pointer, screen), Is.True);
            Assert.That(SplitScreenLayout.ClampToViewport(lower, pointer, screen), Is.EqualTo(pointer));
        }

        [Test]
        public void APointerInTheOtherPlayersHalfIsPulledBackToTheEdge()
        {
            Rect lower = SplitScreenLayout.ViewportFor(1, 2);
            var screen = new Vector2(1920.0f, 1080.0f);
            var pointer = new Vector2(400.0f, 900.0f);

            Assert.That(SplitScreenLayout.Contains(lower, pointer, screen), Is.False);
            Vector2 clamped = SplitScreenLayout.ClampToViewport(lower, pointer, screen);
            Assert.That(clamped.x, Is.EqualTo(400.0f), "it moved sideways");
            Assert.That(clamped.y, Is.EqualTo(540.0f), "it is not on the dividing line");
        }

        [Test]
        public void APointerOffTheWindowEntirelyStillLandsInTheViewport()
        {
            Rect upper = SplitScreenLayout.ViewportFor(0, 2);
            var screen = new Vector2(1920.0f, 1080.0f);

            Vector2 clamped = SplitScreenLayout.ClampToViewport(
                upper, new Vector2(-4000.0f, -4000.0f), screen);

            Assert.That(SplitScreenLayout.Contains(upper, clamped, screen), Is.True);
        }
    }
}
