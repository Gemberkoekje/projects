using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using IronFlag.Audio;
using IronFlag.Core;
using IronFlag.Destruction;
using IronFlag.Menu;
using IronFlag.Objective;

namespace IronFlag.Tests.PlayMode
{
    /// <summary>
    /// Whether the game is actually audible: that the events which should make a noise reach
    /// the director, that the ones which should not are silent, and that distance decides
    /// which.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The claim under test is "it was heard", not "the call was made", which is why
    /// <see cref="AudioDirector.LastSound"/> is only set once a clip has been found and its
    /// level is above zero. Every interesting failure lives in that gap: a catalog with a
    /// missing row, a sound played off the far end of the map, a state transition that fires
    /// while the map is still being built. All three look exactly like working code from the
    /// call site, and none of them shows up in a still.
    /// </para>
    /// <para>
    /// The catalog is built here out of one-sample clips rather than loaded off disk, for the
    /// reason <c>CombatTests</c> assembles its vehicles instead of loading the prefabs: a
    /// failure here should mean the wiring is wrong, not that somebody has not run the audio
    /// build. Whether the real catalog is complete is <c>AudioRosterTests</c>' question, and
    /// it is asked in edit mode where the asset database exists.
    /// </para>
    /// <para>
    /// Nothing here listens. A batch run has no audio device and no
    /// <see cref="AudioListener"/>, which is fine - the question is what the mix decided, not
    /// what came out of the speakers. The single-listener invariant this game is built around
    /// belongs to <c>SandboxWiringTests</c> and is not touched here.
    /// </para>
    /// </remarks>
    public sealed class AudioTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();
        private AudioDirector director;

        [SetUp]
        public void Listen()
        {
            GameSettings.Forget();

            var host = new GameObject("Audio");
            spawned.Add(host);

            director = host.AddComponent<AudioDirector>();
            director.Configure(FullCatalog());
        }

        /// <remarks>
        /// <see cref="Object.DestroyImmediate"/> rather than <see cref="Object.Destroy"/>,
        /// which matters more here than it looks. Both the director and the seats keep static
        /// state - <see cref="AudioDirector.Current"/> and
        /// <see cref="TopDownCameraRig.Seats"/> - that is only released in <c>OnDestroy</c>
        /// and <c>OnDisable</c>, and a deferred destroy leaves both of those pointing at this
        /// test's objects for the whole of the next one.
        /// </remarks>
        [TearDown]
        public void CleanUp()
        {
            foreach (GameObject item in spawned)
            {
                if (item != null)
                {
                    Object.DestroyImmediate(item);
                }
            }

            spawned.Clear();
            director = null;
            GameSettings.Forget();
        }

        /// <summary>
        /// The suite does not use the speakers of whoever ran it. Asserted rather than
        /// assumed, because the failure mode is somebody's afternoon being interrupted by
        /// the menu theme rather than a red test - see <see cref="TestSilence"/>, and note
        /// that muting the output deliberately does not change any decision below.
        /// </summary>
        [Test]
        public void TheSuiteIsMuted()
        {
            Assert.That(
                AudioListener.volume,
                Is.Zero,
                "the play-mode suite is about to play the game out loud");
        }

        /// <summary>
        /// The bones of it: a sound with a clip behind it is heard, and nothing is not.
        /// </summary>
        [Test]
        public void ASoundWithAClipIsHeardAndNothingIsNot()
        {
            Assert.That(Sfx.Play(SfxKind.UiClick), Is.True, "a click with a clip was not heard");
            Assert.That(director.LastSound, Is.EqualTo(SfxKind.UiClick));
            Assert.That(director.SoundsPlayed, Is.EqualTo(1));

            Assert.That(Sfx.Play(SfxKind.None), Is.False, "nothing was played as something");
            Assert.That(director.SoundsPlayed, Is.EqualTo(1), "silence reached the speakers");
        }

        /// <summary>
        /// A sound the catalog has no row for is silent rather than loud, and costs nothing.
        /// This is what a renamed recipe looks like from inside the game.
        /// </summary>
        [Test]
        public void ASoundTheCatalogHasNoRowForIsSimplySilent()
        {
            director.Configure(CatalogWithout(SfxKind.Explosion));

            Assert.That(Sfx.Play(SfxKind.Explosion), Is.False);
            Assert.That(director.SoundsPlayed, Is.Zero);
            Assert.That(Sfx.Play(SfxKind.UiClick), Is.True, "the rest of the catalog went with it");
        }

        /// <summary>
        /// A scene with no director is silent rather than broken. Every art preview, prefab
        /// builder and test rig in this project assembles a vehicle and fires it without ever
        /// building a scene to hear it in, and all of them have to keep working.
        /// </summary>
        [Test]
        public void ASceneWithNoDirectorIsSilentRatherThanBroken()
        {
            Object.DestroyImmediate(director.gameObject);
            spawned.Clear();
            director = null;

            Assert.That(AudioDirector.Current, Is.Null);
            Assert.That(Sfx.Play(SfxKind.UiClick), Is.False);
            Assert.That(Sfx.PlayAt(SfxKind.Explosion, Vector3.zero), Is.False);
        }

        /// <summary>
        /// Turning the sound off turns the sound off - including for anything that was going
        /// to be played at a distance, which is scaled by the setting rather than against it.
        /// </summary>
        [Test]
        public void TurningTheVolumeOffSilencesEverything()
        {
            GameSettings.SetSoundVolume(0.0f);

            Assert.That(Sfx.Play(SfxKind.UiClick), Is.False);
            Assert.That(Sfx.PlayAt(SfxKind.Explosion, Vector3.zero), Is.False);
            Assert.That(director.SoundsPlayed, Is.Zero);
        }

        /// <summary>
        /// The split-screen compromise at the point it actually applies: an event next to a
        /// seat is heard, the same event at the far end of the map is not, and neither of them
        /// needed a second listener to work that out.
        /// </summary>
        [Test]
        public void DistanceDecidesWhetherAnEventIsHeardAtAll()
        {
            Seat(Vector3.zero);
            Assert.That(TopDownCameraRig.Seats.Count, Is.EqualTo(1), "the seat did not register");

            Assert.That(
                Sfx.PlayAt(SfxKind.Explosion, Vector3.zero),
                Is.True,
                "a blast in front of the player was not heard");

            int heard = director.SoundsPlayed;

            Assert.That(
                Sfx.PlayAt(SfxKind.Explosion, new Vector3(0.0f, 0.0f, 400.0f)),
                Is.False,
                "a blast off the end of the map was heard");

            Assert.That(director.SoundsPlayed, Is.EqualTo(heard), "the far blast still cost a voice");
        }

        /// <summary>
        /// Both seats are ears. A shell landing in front of either player is loud for both,
        /// because measuring to the nearest is the only answer that does not make half the
        /// events on screen quieter than they look.
        /// </summary>
        [Test]
        public void EitherSeatIsCloseEnoughToBeHeardBy()
        {
            Seat(Vector3.zero);
            Seat(new Vector3(0.0f, 0.0f, 300.0f));

            Assert.That(TopDownCameraRig.Seats.Count, Is.EqualTo(2));

            Assert.That(
                AudioDirector.LoudnessAt(new Vector3(0.0f, 0.0f, 300.0f)),
                Is.EqualTo(1.0f).Within(0.0001f),
                "an event in front of the second seat was quiet");

            // Halfway between them, which is past earshot of both. The point of the pair is
            // that neither seat's distance is the answer on its own - the nearer one is.
            Assert.That(
                AudioDirector.LoudnessAt(new Vector3(0.0f, 0.0f, 150.0f)),
                Is.Zero,
                "an event far from both seats was still audible");
        }

        /// <summary>
        /// A scene with no seats at all - the menu, the level editor, a still being
        /// photographed - hears everything. There is no view to be far from.
        /// </summary>
        [Test]
        public void ASceneWithNoSeatsHearsEverythingAtFullVolume()
        {
            Assert.That(TopDownCameraRig.Seats.Count, Is.Zero, "a seat leaked in from another test");
            Assert.That(
                AudioDirector.LoudnessAt(new Vector3(0.0f, 0.0f, 900.0f)),
                Is.EqualTo(1.0f).Within(0.0001f));
        }

        /// <summary>
        /// A structure knocked into a state during a match is an event; the same structure
        /// arriving in that state because a map said so is not. Both go through one method,
        /// and only the flag that throws the debris tells them apart - so a map carrying nine
        /// pre-damaged walls must not open with nine collapses.
        /// </summary>
        [Test]
        public void AStructureIsHeardBreakingButNotBeingBuiltOrRepaired()
        {
            Destructible wall = Structure(StructureKind.Wall);

            Assert.That(
                director.SoundsPlayed,
                Is.Zero,
                "the wall announced itself while the map was being built");

            wall.TakeDamage(wall.Tuning.HitPoints * 10.0f, Team.Green);

            Assert.That(wall.State, Is.EqualTo(DestructionState.Destroyed));
            Assert.That(
                director.LastSound,
                Is.EqualTo(SfxKind.StructureDestroyed),
                "a wall collapsed in silence");

            int heard = director.SoundsPlayed;
            wall.Restore();

            Assert.That(
                director.SoundsPlayed,
                Is.EqualTo(heard),
                "putting a wall back up in the editor made a noise");
        }

        /// <summary>
        /// The music fades rather than cutting, and both themes are audible while it does.
        /// A change of vehicle happens mid-match at a moment the player chose, and a hard cut
        /// there sounds like a fault in the game.
        /// </summary>
        [UnityTest]
        public IEnumerator ChangingThemeIsACrossfadeRatherThanACut()
        {
            MusicPlayer music = Music();

            music.Play(MusicKind.MatchJeep);
            Assert.That(music.Playing, Is.EqualTo(MusicKind.MatchJeep));
            Assert.That(music.IsFading, Is.True, "the first theme arrived fully formed");

            music.Play(MusicKind.MatchTank);
            Assert.That(music.Playing, Is.EqualTo(MusicKind.MatchTank));

            music.Play(MusicKind.MatchTank);
            Assert.That(music.IsFading, Is.True, "asking again for what is playing restarted it");

            float waited = 0.0f;
            while (music.IsFading && waited < MusicPlayer.FadeSeconds * 4.0f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.That(music.IsFading, Is.False, "the crossfade never finished");
        }

        /// <summary>
        /// The fade runs on unscaled time, so pausing does not freeze it halfway and leave
        /// two themes playing at half volume for as long as the panel is up.
        /// </summary>
        [UnityTest]
        public IEnumerator TheCrossfadeKeepsRunningWhileTheMatchIsPaused()
        {
            MusicPlayer music = Music();
            float was = Time.timeScale;

            try
            {
                music.Play(MusicKind.MenuTheme);
                Time.timeScale = 0.0f;

                float waited = 0.0f;
                while (music.IsFading && waited < MusicPlayer.FadeSeconds * 4.0f)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }

                Assert.That(music.IsFading, Is.False, "the fade froze with the match");
            }
            finally
            {
                Time.timeScale = was;
            }
        }

        /// <summary>
        /// A match opens on the jeep's theme - the ride the game is about, and the one every
        /// side has eight of - and ends on a cue rather than on whatever happened to be
        /// playing when somebody won.
        /// </summary>
        [UnityTest]
        public IEnumerator AMatchOpensOnATuneAndEndsOnACue()
        {
            Music();

            var host = new GameObject("Session");
            spawned.Add(host);

            Match match = host.AddComponent<Match>();
            MatchMusic policy = host.AddComponent<MatchMusic>();
            yield return null;

            Assert.That(
                policy.Wanted(),
                Is.EqualTo(MatchMusic.Opening),
                "a match with nobody deployed opened in silence");

            Assert.That(match.Win(Team.Green, Team.Brown, MatchOutcome.FlagCaptured), Is.True);
            yield return null;

            Assert.That(
                policy.Wanted(),
                Is.EqualTo(MusicKind.Victory),
                "a won match kept playing the match theme");

            Assert.That(
                director.LastSound,
                Is.EqualTo(SfxKind.MatchWon),
                "nothing marked the end of the match");

            int heard = director.SoundsPlayed;
            yield return null;
            yield return null;

            Assert.That(
                director.SoundsPlayed,
                Is.EqualTo(heard),
                "the end of the match was announced once per frame");
        }

        /// <summary>
        /// A rematch is a new match. The result is latched so it is not recomputed every
        /// frame, so the latch has to let go when the match does - or the second game plays
        /// the first one's fanfare.
        /// </summary>
        [UnityTest]
        public IEnumerator ARestartedMatchStopsPlayingTheLastOnesFanfare()
        {
            Music();

            var host = new GameObject("Session");
            spawned.Add(host);

            Match match = host.AddComponent<Match>();
            MatchMusic policy = host.AddComponent<MatchMusic>();

            match.Win(Team.Green, Team.Brown, MatchOutcome.FlagCaptured);
            yield return null;
            Assert.That(policy.Wanted(), Is.EqualTo(MusicKind.Victory));

            match.Restart();
            yield return null;

            Assert.That(
                policy.Wanted(),
                Is.Not.EqualTo(MusicKind.Victory),
                "the rematch is still playing the last match's cue");
        }

        /// <summary>
        /// Creates the thing that plays music, pointed at the same catalog as the director.
        /// </summary>
        private MusicPlayer Music()
        {
            var host = new GameObject("Music");
            spawned.Add(host);

            MusicPlayer music = host.AddComponent<MusicPlayer>();
            music.Configure(FullCatalog());
            return music;
        }

        /// <summary>
        /// Creates a seat whose view is centred on a point.
        /// </summary>
        private TopDownCameraRig Seat(Vector3 at)
        {
            var host = new GameObject("Seat", typeof(Camera));
            spawned.Add(host);

            TopDownCameraRig rig = host.AddComponent<TopDownCameraRig>();
            rig.Park(at);
            return rig;
        }

        /// <summary>
        /// Assembles a structure with the real numbers for its kind and cubes for models.
        /// </summary>
        /// <remarks>
        /// The same rig <c>DestructionTests</c> builds, and for the same reason: what is
        /// under test is the transition, not the imported model.
        /// </remarks>
        private Destructible Structure(StructureKind kind)
        {
            var host = new GameObject(kind.ToString());
            host.SetActive(false);
            spawned.Add(host);

            GameObject intact = State(host, Destructible.IntactNodeName);
            GameObject damaged = State(host, Destructible.DamagedNodeName);
            GameObject destroyed = State(host, Destructible.DestroyedNodeName);

            Destructible built = host.AddComponent<Destructible>();
            built.Configure(kind, StructureTuning.For(kind), intact, damaged, destroyed, null);

            host.SetActive(true);
            return built;
        }

        private static GameObject State(GameObject host, string name)
        {
            var state = new GameObject(name);
            state.transform.SetParent(host.transform, false);
            return state;
        }

        /// <summary>
        /// Builds a catalog with a clip for every sound there is.
        /// </summary>
        private static AudioCatalog FullCatalog() => CatalogWithout(SfxKind.None);

        /// <summary>
        /// Builds a catalog with a clip for every sound but one.
        /// </summary>
        /// <param name="missing">The sound to leave a hole for.</param>
        /// <returns>The catalog.</returns>
        private static AudioCatalog CatalogWithout(SfxKind missing)
        {
            var sounds = new List<AudioSfxClip>();
            foreach (SfxKind kind in AudioRoster.Sounds())
            {
                sounds.Add(new AudioSfxClip
                {
                    Kind = kind,
                    Clip = kind == missing ? null : Tone(AudioRoster.AssetNameOf(kind)),
                });
            }

            var music = new List<AudioMusicClip>();
            foreach (MusicKind kind in AudioRoster.Themes())
            {
                music.Add(new AudioMusicClip
                {
                    Kind = kind,
                    Clip = Tone(AudioRoster.AssetNameOf(kind)),
                });
            }

            AudioCatalog catalog = ScriptableObject.CreateInstance<AudioCatalog>();
            catalog.Configure(sounds, music);
            return catalog;
        }

        /// <summary>
        /// Creates the shortest clip Unity will make, to stand in for a rendered one.
        /// </summary>
        /// <param name="name">What to call it, so a failure names the sound.</param>
        /// <returns>The clip.</returns>
        /// <remarks>
        /// One sample of silence. Nothing here is listening, and what the director cares
        /// about is whether a row has a clip in it at all.
        /// </remarks>
        private static AudioClip Tone(string name)
            => AudioClip.Create(name, 1, 1, 44100, false);
    }
}
