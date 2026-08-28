using NUnit.Framework;
using IronFlag.Audio;
using IronFlag.Menu;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// The mix: how a distance becomes a volume, how an engine's speed becomes a pitch, and
    /// what the two volume settings do.
    /// </summary>
    /// <remarks>
    /// This is the half of the audio work that can be checked without a speaker.
    /// <see cref="AudioMixdown"/> is static and side-effect free for exactly that reason -
    /// the same property <c>Explosion.Scale</c> and <c>SplitScreenLayout.ViewportFor</c>
    /// have - so the decision that a split screen attenuates but never pans is a decision
    /// with assertions under it rather than a rolloff curve drawn on a component.
    /// </remarks>
    public sealed class AudioMixdownTests
    {
        /// <summary>
        /// The shape of the falloff: flat across your own view, nothing past earshot, and
        /// never louder further away.
        /// </summary>
        [Test]
        public void ASoundIsFullVolumeAcrossYourOwnViewAndSilentOffTheMap()
        {
            Assert.That(AudioMixdown.Loudness(0.0f), Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(AudioMixdown.Loudness(AudioMixdown.FullWithin), Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(AudioMixdown.Loudness(AudioMixdown.SilentBeyond), Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(AudioMixdown.Loudness(500.0f), Is.EqualTo(0.0f).Within(0.0001f));

            float last = 1.0f;
            for (float metres = 0.0f; metres <= 200.0f; metres += 2.0f)
            {
                float now = AudioMixdown.Loudness(metres);
                Assert.That(now, Is.LessThanOrEqualTo(last + 0.0001f), $"louder at {metres} m");
                Assert.That(now, Is.InRange(0.0f, 1.0f));
                last = now;
            }
        }

        /// <summary>
        /// The reason the curve is squared rather than straight: a linear ramp leaves the far
        /// half of the map audible at a third of full volume, which is the mush the whole
        /// rule exists to prevent. Halfway to silence should be well under halfway in volume.
        /// </summary>
        [Test]
        public void TheFalloffIsSteepEnoughToKeepTheFarHalfOfTheMapOutOfTheMix()
        {
            float middle = (AudioMixdown.FullWithin + AudioMixdown.SilentBeyond) * 0.5f;
            Assert.That(AudioMixdown.Loudness(middle), Is.LessThan(0.3f));
        }

        /// <summary>
        /// A vehicle shoving against a wall is going nowhere and working hard, which is the
        /// case speed alone gets wrong - and the one moment a driver most needs to hear that
        /// the engine is doing something.
        /// </summary>
        [Test]
        public void AnEngineIsLoudWhenItIsWorkingEvenIfItIsNotMoving()
        {
            const float idle = AudioMixdown.EngineIdleShare;

            float stalled = AudioMixdown.EngineLoudness(0.0f, 10.0f, 1.0f, idle);
            Assert.That(stalled, Is.EqualTo(1.0f).Within(0.0001f), "full throttle is not full");

            float coasting = AudioMixdown.EngineLoudness(10.0f, 10.0f, 0.0f, idle);
            Assert.That(coasting, Is.EqualTo(1.0f).Within(0.0001f), "coasting flat out went quiet");

            float parked = AudioMixdown.EngineLoudness(0.0f, 10.0f, 0.0f, idle);
            Assert.That(parked, Is.EqualTo(idle).Within(0.0001f), "a stopped engine is not idling");
        }

        /// <summary>
        /// A rotor is at full song hovering, and that is the only difference between the
        /// helicopter's engine and everything else's - one number, not a branch.
        /// </summary>
        [Test]
        public void AHoveringRotorIsNearlyAsLoudAsAMovingOne()
        {
            float hovering = AudioMixdown.EngineLoudness(0.0f, 18.0f, 0.0f, 0.9f);
            float crossing = AudioMixdown.EngineLoudness(18.0f, 18.0f, 1.0f, 0.9f);

            Assert.That(hovering, Is.GreaterThan(0.85f), "a hovering helicopter went quiet");
            Assert.That(crossing - hovering, Is.LessThan(0.15f), "a rotor revved like an engine");
        }

        /// <summary>
        /// Pitch follows the wheels rather than the throttle, so a vehicle shoving against a
        /// wall is loud and low instead of loud and screaming.
        /// </summary>
        [Test]
        public void PitchRisesWithSpeedAndStaysInsideItsRange()
        {
            Assert.That(
                AudioMixdown.EnginePitch(0.0f, 10.0f),
                Is.EqualTo(AudioMixdown.EngineLowPitch).Within(0.0001f));

            Assert.That(
                AudioMixdown.EnginePitch(10.0f, 10.0f),
                Is.EqualTo(AudioMixdown.EngineHighPitch).Within(0.0001f));

            Assert.That(
                AudioMixdown.EnginePitch(40.0f, 10.0f),
                Is.EqualTo(AudioMixdown.EngineHighPitch).Within(0.0001f),
                "a vehicle past its own top speed pitched off the end of the range");
        }

        /// <summary>
        /// A vehicle with no top speed on its tuning - a rig assembled in a test - must not
        /// divide by it.
        /// </summary>
        [Test]
        public void AVehicleWithNoTopSpeedStillHasAnEngine()
        {
            Assert.That(AudioMixdown.EnginePitch(5.0f, 0.0f), Is.EqualTo(AudioMixdown.EngineLowPitch));
            Assert.That(
                AudioMixdown.EngineLoudness(5.0f, 0.0f, 0.0f, 0.25f),
                Is.EqualTo(0.25f).Within(0.0001f));
        }

        /// <summary>
        /// The two volumes are stored, clamped and remembered separately - and a volume of
        /// zero survives, because OFF is a choice rather than a missing value.
        /// </summary>
        [Test]
        public void TheTwoVolumesAreStoredSeparatelyAndClamped()
        {
            try
            {
                GameSettings.SetSoundVolume(0.4f);
                GameSettings.SetMusicVolume(0.9f);
                Assert.That(GameSettings.SoundVolume, Is.EqualTo(0.4f).Within(0.0001f));
                Assert.That(GameSettings.MusicVolume, Is.EqualTo(0.9f).Within(0.0001f));

                GameSettings.SetSoundVolume(-1.0f);
                Assert.That(GameSettings.SoundVolume, Is.EqualTo(0.0f), "stepping below OFF wrapped");
                Assert.That(GameSettings.MusicVolume, Is.EqualTo(0.9f).Within(0.0001f), "music followed sound");

                GameSettings.SetSoundVolume(4.0f);
                Assert.That(GameSettings.SoundVolume, Is.EqualTo(1.0f), "stepping past full wrapped");
            }
            finally
            {
                // Shared with the machine the tests run on: leaving a volume behind would
                // change what somebody hears the next time they press Play.
                GameSettings.Forget();
            }
        }

        /// <summary>
        /// A volume reads as a percentage, and the bottom of the range reads as a decision.
        /// </summary>
        [Test]
        public void TheBottomOfTheRangeReadsAsOffRatherThanAsZero()
        {
            Assert.That(GameSettings.NameOfVolume(0.0f), Is.EqualTo("OFF"));
            Assert.That(GameSettings.NameOfVolume(1.0f), Is.EqualTo("100%"));
            Assert.That(GameSettings.NameOfVolume(0.7f), Is.EqualTo("70%"));
        }
    }
}
