using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using IronFlag.Core;
using IronFlag.UI;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// The interface's own look: the two typefaces, the arithmetic everything on it moves by,
    /// and the shapes the drawn marks are made of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three of these test things that would otherwise fail silently and look like something
    /// else. A missing font asset does not throw - <see cref="HudPalette"/> falls back to the
    /// built-in face on purpose - so the whole game would quietly go back to being set in
    /// Arial, and the symptom is "it looks a bit worse than I remember." A glyph whose outline
    /// stopped being convex draws a fan of triangles across itself rather than a shield, which
    /// reads as a rendering bug. And a bar whose easing overshot would show more armour than
    /// the vehicle has, for a quarter of a second, which nobody will ever catch in a still.
    /// </para>
    /// <para>
    /// Everything here is either a pure function or a widget that needs no canvas, which is
    /// why this is an edit-mode suite. See <c>UI_NOTES.md</c>.
    /// </para>
    /// </remarks>
    public sealed class HudLookTests
    {
        [Test]
        public void TheInterfaceIsSetInItsOwnTypefaceAndNotInTheFallback()
        {
            Assert.That(HudPalette.Face, Is.Not.Null, "the interface has no font at all");
            Assert.That(
                HudPalette.Face.name,
                Is.EqualTo(HudPalette.BodyFaceName),
                "the body face fell back to the built-in one - is the Resources font missing?");

            Assert.That(HudPalette.DisplayFace, Is.Not.Null);
            Assert.That(
                HudPalette.DisplayFace.name,
                Is.EqualTo(HudPalette.DisplayFaceName),
                "the headline face fell back to the built-in one");
        }

        [Test]
        public void TheTwoFacesAreDifferentFaces()
        {
            // The fallback path returns the same built-in font for both, so two faces that
            // are the same object is exactly what a broken load looks like.
            Assert.That(HudPalette.Face, Is.Not.SameAs(HudPalette.DisplayFace));
        }

        [Test]
        public void AGlassPlateWearsTheSameVignetteAsTheWorld()
        {
            Assert.That(
                HudPlate.EdgeDarken,
                Is.EqualTo(1.0f - PostTuning.VignetteIntensity).Within(0.0001f),
                "the plate and the grade have drifted apart");
            Assert.That(
                HudPlate.EdgeReach,
                Is.EqualTo(PostTuning.VignetteSmoothness).Within(0.0001f));
        }

        [Test]
        public void EveryMarkHasAShapeAndEveryShapeIsInsideItsSquare()
        {
            foreach (HudGlyphKind kind in System.Enum.GetValues(typeof(HudGlyphKind)))
            {
                Vector2[][] outlines = HudGlyph.Outlines(kind);

                if (kind == HudGlyphKind.None)
                {
                    Assert.That(outlines, Is.Empty, "nothing should draw nothing");
                    continue;
                }

                Assert.That(outlines, Is.Not.Empty, $"{kind} draws nothing at all");

                foreach (Vector2[] outline in outlines)
                {
                    Assert.That(outline.Length, Is.GreaterThanOrEqualTo(3), $"{kind} is a line");

                    foreach (Vector2 point in outline)
                    {
                        Assert.That(point.x, Is.InRange(0.0f, 1.0f), $"{kind} leaves its square");
                        Assert.That(point.y, Is.InRange(0.0f, 1.0f), $"{kind} leaves its square");
                    }
                }
            }
        }

        [Test]
        public void EveryMarkIsConvexBecauseThatIsWhatLetsItBeDrawnAsAFan()
        {
            foreach (HudGlyphKind kind in System.Enum.GetValues(typeof(HudGlyphKind)))
            {
                foreach (Vector2[] outline in HudGlyph.Outlines(kind))
                {
                    Assert.That(IsConvex(outline), Is.True, $"{kind} cannot be fanned");
                }
            }
        }

        [Test]
        public void EveryDrawnPieceHasTheRendererItsMeshIsDrawnBy()
        {
            // The failure this catches has no symptom: Graphic.Rebuild returns silently when
            // the CanvasRenderer is missing, so the piece is enabled, dirtied and laid out
            // correctly and simply never draws. Nothing is logged and no test that only looks
            // at the object graph notices - see HudPlate for the whole story.
            var host = new GameObject("Piece Host", typeof(RectTransform));

            try
            {
                var made = new Graphic[]
                {
                    HudPalette.Plate("Plate", host.transform, HudPalette.Panel),
                    HudPalette.Bracket("Bracket", host.transform, HudPalette.Ink),
                    HudPalette.Glyph("Glyph", host.transform, HudGlyphKind.Fuel, HudPalette.Fuel),
                };

                foreach (Graphic piece in made)
                {
                    Assert.That(
                        piece.GetComponent<CanvasRenderer>(),
                        Is.Not.Null,
                        $"{piece.name} would never draw");
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void NothingMovesOnAFrameThatTookNoTime()
        {
            Assert.That(HudMotion.Ease(0.25f, 1.0f, HudMotion.BarRate, 0.0f), Is.EqualTo(0.25f));
            Assert.That(HudMotion.Ease(0.25f, 1.0f, HudMotion.BarRate, -1.0f), Is.EqualTo(0.25f));
        }

        [Test]
        public void EasingClosesTheGapWithoutEverPassingIt()
        {
            float rising = 0.0f;
            float falling = 1.0f;

            for (int frame = 0; frame < 600; frame++)
            {
                float wasRising = rising;
                float wasFalling = falling;

                rising = HudMotion.Ease(rising, 1.0f, HudMotion.BarRate, 1.0f / 60.0f);
                falling = HudMotion.Ease(falling, 0.0f, HudMotion.BarRate, 1.0f / 60.0f);

                Assert.That(rising, Is.GreaterThanOrEqualTo(wasRising), "a rising bar fell");
                Assert.That(rising, Is.LessThanOrEqualTo(1.0f), "a bar overshot its target");
                Assert.That(falling, Is.LessThanOrEqualTo(wasFalling), "a falling bar rose");
                Assert.That(falling, Is.GreaterThanOrEqualTo(0.0f), "a bar undershot its target");
            }

            Assert.That(rising, Is.EqualTo(1.0f), "a bar never arrived");
            Assert.That(falling, Is.EqualTo(0.0f), "a bar never arrived");
        }

        [Test]
        public void EasingLandsInTheSamePlaceWhateverTheFrameRate()
        {
            // The property that makes the motion a rule rather than an accident of hardware:
            // two half-steps have to reach where one whole step reaches.
            float once = HudMotion.Ease(0.0f, 1.0f, HudMotion.BarRate, 0.1f);

            float twice = HudMotion.Ease(0.0f, 1.0f, HudMotion.BarRate, 0.05f);
            twice = HudMotion.Ease(twice, 1.0f, HudMotion.BarRate, 0.05f);

            Assert.That(twice, Is.EqualTo(once).Within(0.0005f));
        }

        [Test]
        public void ThePulseBreathesBetweenNothingAndOne()
        {
            Assert.That(HudMotion.Pulse(0.0f, HudMotion.AlarmPeriod), Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(
                HudMotion.Pulse(HudMotion.AlarmPeriod * 0.5f, HudMotion.AlarmPeriod),
                Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(
                HudMotion.Pulse(HudMotion.AlarmPeriod, HudMotion.AlarmPeriod),
                Is.EqualTo(0.0f).Within(0.0001f),
                "the breath does not repeat");

            for (int step = 0; step <= 64; step++)
            {
                float at = HudMotion.Pulse(step * 0.05f, HudMotion.AlarmPeriod);
                Assert.That(at, Is.InRange(0.0f, 1.0f));
            }
        }

        [Test]
        public void TheAlarmFlashChangesBrightnessAndNothingElse()
        {
            Color alarm = HudPalette.Alarm;

            for (int step = 0; step <= 64; step++)
            {
                Color lit = HudMotion.Flash(alarm, step * 0.05f);

                Assert.That(lit.a, Is.EqualTo(alarm.a), "the flash faded the bar out");
                Assert.That(lit.r, Is.InRange(alarm.r * (1.0f - HudMotion.AlarmDepth), alarm.r));
                Assert.That(lit.g, Is.InRange(alarm.g * (1.0f - HudMotion.AlarmDepth), alarm.g));
                Assert.That(lit.b, Is.InRange(alarm.b * (1.0f - HudMotion.AlarmDepth), alarm.b));
            }
        }

        [Test]
        public void APoolGoesRedOnItsOwnAndAProgressBarNever()
        {
            Assert.That(HudPalette.IsAlarming(1.0f), Is.False);
            Assert.That(HudPalette.IsAlarming(HudPalette.AlarmFraction), Is.True);
            Assert.That(HudPalette.Level(HudPalette.Fuel, 0.05f), Is.EqualTo(HudPalette.Alarm));
            Assert.That(HudPalette.Level(HudPalette.Fuel, 0.5f), Is.EqualTo(HudPalette.Fuel));

            var host = new GameObject("Bar Host", typeof(RectTransform));

            try
            {
                HudBar bar = HudBar.Build(
                    host.transform, "Fuel", HudGlyphKind.Fuel, HudPalette.Fuel,
                    0.0f, 0.0f, 400.0f, 32.0f);

                // A pool eases towards what it was told and gets there.
                bar.Show(1.0f, "100", HudPalette.Fuel);
                Assert.That(bar.Shown, Is.EqualTo(0.0f), "a bar arrived before it moved");

                for (int frame = 0; frame < 120; frame++)
                {
                    bar.Advance(1.0f / 60.0f);
                }

                Assert.That(bar.Shown, Is.EqualTo(1.0f), "a bar never arrived");

                // A countdown is wherever it was put, immediately.
                bar.ShowProgress(0.4f, "40%", HudPalette.Alarm);
                Assert.That(bar.Shown, Is.EqualTo(0.4f), "a held button lagged the finger");

                // And changing vehicle stops the slide dead.
                bar.Show(0.1f, "10", HudPalette.Fuel);
                bar.Jump();
                Assert.That(bar.Shown, Is.EqualTo(0.1f), "a gauge swept between two vehicles");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// Says whether an outline turns the same way at every corner.
        /// </summary>
        /// <param name="outline">The outline to check.</param>
        /// <returns>Whether it is convex, and so safe to draw as one fan.</returns>
        private static bool IsConvex(Vector2[] outline)
        {
            int sign = 0;

            for (int corner = 0; corner < outline.Length; corner++)
            {
                Vector2 a = outline[corner];
                Vector2 b = outline[(corner + 1) % outline.Length];
                Vector2 c = outline[(corner + 2) % outline.Length];

                float turn = ((b.x - a.x) * (c.y - b.y)) - ((b.y - a.y) * (c.x - b.x));
                if (Mathf.Abs(turn) < 0.000001f)
                {
                    continue;
                }

                int way = turn > 0.0f ? 1 : -1;
                if (sign == 0)
                {
                    sign = way;
                }
                else if (sign != way)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
