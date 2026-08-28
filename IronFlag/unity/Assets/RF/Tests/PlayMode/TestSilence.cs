using NUnit.Framework;
using UnityEngine;

namespace IronFlag.Tests.PlayMode
{
    /// <summary>
    /// Turns the speakers off for the whole play-mode suite.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The suite got noisy the day the game did. Several classes here load
    /// <c>MainMenu</c>, <c>Sandbox</c> or <c>LevelEditor</c> for real, and those scenes now
    /// carry an <see cref="IronFlag.Audio.AudioDirector"/> and a
    /// <see cref="IronFlag.Audio.MusicPlayer"/> - so running the tests played the menu theme
    /// out loud and clicked its way through the map list, on the machine of whoever pressed
    /// run. A test suite should not be audible.
    /// </para>
    /// <para>
    /// <see cref="AudioListener.volume"/> rather than a flag on the sources, because the
    /// point is to silence everything a test can reach without any of it knowing it has been
    /// silenced: the mix still decides what would have played, and
    /// <c>AudioTests</c> still asserts on those decisions. Muting the output is the one
    /// change that cannot alter what is under test.
    /// </para>
    /// <para>
    /// A <c>SetUpFixture</c> with no namespace-mates to declare it applies to every fixture
    /// in <c>IronFlag.Tests.PlayMode</c>, which is exactly the scope wanted, and it runs once
    /// rather than per test. The previous volume is put back on the way out; a batch run is
    /// its own process, so there is nothing to leak into, and an interactive run gets it back
    /// at the next domain reload even if it is killed part-way.
    /// </para>
    /// </remarks>
    [SetUpFixture]
    public sealed class TestSilence
    {
        private float was = 1.0f;

        [OneTimeSetUp]
        public void Hush()
        {
            was = AudioListener.volume;
            AudioListener.volume = 0.0f;
        }

        [OneTimeTearDown]
        public void Restore() => AudioListener.volume = was;
    }
}
