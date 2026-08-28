# Sound and Music

**To understand this, start by reading [`AudioMixdown.cs`](unity/Assets/RF/Scripts/Audio/AudioMixdown.cs)
— it is short, and it holds the one decision everything else follows from. Then
[`Sfx.cs`](unity/Assets/RF/Scripts/Audio/Sfx.cs) for how the rest of the game makes a
noise, [`MatchMusic.cs`](unity/Assets/RF/Scripts/Audio/MatchMusic.cs) for what a match
sounds like, and [`audio/README.md`](audio/README.md) for where the clips come from.**
Everything else is wiring.

This is all of [MASTER_PLAN.md § 8](MASTER_PLAN.md#8-sounds-and-music). It was built in
two halves, and this file is written in two halves to match:

- **The pipeline** (2026-08-27) — thirty clips synthesised from committed SuperCollider
  source. Answered the plan's open question 1, *license / commission / synthesise*, in
  favour of **synthesise**.
- **The wiring** (2026-08-28) — the clips actually playing. Answers open questions 2 and
  3: **non-positional but attenuated**, and **all of it, engine loops included**.

| Category | Count | Covers |
|---|---|---|
| Weapons | 5 | One per `WeaponKind` — grenade, cannon, rocket, chaingun, autocannon |
| Impacts | 4 | Explosion, armour hit, and both `Destructible` state transitions |
| Objective | 6 | Flag pickup / dropped / returned / captured, match won / lost |
| UI | 4 | Click, select, back, denied |
| Vehicles | 4 | One looping engine per `VehicleKind` |
| Music | 7 | Menu theme, one theme per vehicle, victory and defeat cues |

**Hear one**: `./audio/build.ps1 -Listen -Sound Cannon`. **Build them**:
`./audio/build.ps1`, or `Tools > IronFlag > Rebuild All Audio from SuperCollider`, then
`Tools > IronFlag > Build Audio Catalog`.

---

# Part two: the wiring

## The one decision: distance decides loudness, nothing decides direction

This game has exactly **one `AudioListener`** for the whole split-screen rig, on seat
one's camera, enforced by a test whose comment reads *"a split screen still only has one
set of speakers."* § 8 raised that as a real architectural wrinkle and recommended
non-positional sound everywhere as the way round it.

That recommendation is taken, but only half of it was ever the problem. Positional audio
does two things, and they fail differently under one listener:

- **Panning breaks.** A shot to seat one's left is to seat two's right, and one pair of
  speakers cannot say both. Whichever seat holds the listener is right and the other is
  wrong.
- **Attenuation does not.** A shell landing in the far corner of the map is quiet for
  everybody, whoever is looking at it. That is true no matter where the listener sits.

So every clip plays **flat** — `spatialBlend` at zero, no panning, no doppler — and the
volume is computed in [`AudioMixdown`](unity/Assets/RF/Scripts/Audio/AudioMixdown.cs)
from the distance to the **nearest seat's focus point**. Full volume inside 24 m (about
what a seat's camera takes in), squared down to nothing at 110 m (under half the width of
a 240 m map). Nearest rather than averaged or seat-one's, so a shell in front of either
player is loud for both.

The point of doing it arithmetically instead of with an `AudioSource` rolloff curve is
that **the mix becomes a rule the project owns and can test**. `AudioMixdown` is static
and side-effect free — the same property `Explosion.Scale` and
`SplitScreenLayout.ViewportFor` have — so "a firefight at the other end of the map is not
in the mix" is an assertion rather than a curve drawn on a component nobody can check.

## Decisions worth knowing

- **The catalog's names are computed, not listed.** A clip is its enum value behind a
  prefix: `SfxKind.WeaponCannon` is `RF_Sfx_WeaponCannon.wav`. That is why the audio
  build validates the `RF_Sfx_` / `RF_Music_` naming rule *before* it renders — a recipe
  named wrongly fails the build instead of surfacing as a catalog row that is quietly
  empty. A hand-written mapping table would have been a third place for the same name to
  be spelled, and two is already the limit.
- **One `AudioSource` serves every one-shot in the game.** `PlayOneShot` mixes rather
  than interrupts, so a chaingun firing over a collapsing wall needs no pool and no voice
  stealing. What a pool would have added is a way for two sounds to end up at different
  volumes by accident.
- **Every button in the game gained a voice in one line.** Every button here — the
  editor's palettes, the main menu, the pause panel — is an `EditorButton` and reaches
  its action through `OnPress`, so the click lives there. Four call sites override it
  (`UiBack` on the panels you leave, `UiSelect` on the settings steppers and the editor's
  side buttons); the other forty say nothing and are audible anyway. The alternative was
  thirty-nine chances to forget.
- **A blast is heard if and only if it leaves a scorch.** `GroundMark` already had a
  floor — *"the chaingun and the autocannon fire several rounds a second each… without a
  floor a firefight would carpet the ground in scorch marks inside ten seconds"* — and
  the sound wants exactly the same line. `Explosion.HeardAbove` is derived from
  `GroundMark.SmallestBlast` rather than written down again, and a test asserts the two
  agree. Two thresholds for one question eventually disagree, and the version where they
  disagree is a bang with no mark under it.
- **The flag of "is this an event" is reused, not reinvented.** `Destructible.Enter`
  already took a `throwDebris` flag that separates a wall being knocked down from a wall
  being *built* — the sound hangs off that same flag. `Flag.Enter` needed the same
  distinction and did not have it, so it gained a `heard` parameter of the same shape:
  a flag returned after a raid is the loudest thing that can happen to a defending side,
  and a flag raised while the map loads is nothing at all.
- **What decides whether an engine runs is its bay.** `VehicleBay.IsOnField` already
  means "out, with somebody at the controls", so the twenty-odd vehicles parked in the
  two bunkers are silent for a reason `EngineAudio` did not have to invent — and a test
  rig with no bay runs, which is what the bay's own `None` state already means.
- **The helicopter needs no special case, only a different number.** A rotor is at full
  song the moment it leaves the ground; an engine follows the throttle. That is the
  difference between an idle share of 0.9 and one of 0.25, stamped on by the prefab
  builder like every other per-vehicle figure. Both go through the same arithmetic.
- **Seat one owns the soundtrack, because it already owns the ear.** Four match themes
  and one pair of speakers is the same shape of problem as four cameras and one listener,
  and it gets the same answer. When seat one is back at its bunker, seat two's ride picks
  the theme; when neither is out, the last *theme* keeps playing — being blown up should
  not also change the music.
- **The end cue is about the room, not the side.** Two players sharing a screen cannot be
  played a fanfare and a lament at once, and one of them did just win. So a result with
  any local player on the winning side is a victory — always true of a two-player match,
  and correctly false of the one-player game, where the only human can genuinely lose.
- **Two volumes, not one.** `GameSettings` used to give a master volume as its example of
  a setting that would fail its own rule — *"a slider over a game with no sounds in it"* —
  and it was right until there were sounds. There are two because the mix has two halves
  people want at different levels: a soundtrack somebody has heard forty times, and the
  gunfire telling them where they are being shot from.
- **The test suite is muted.** Several play-mode classes load `MainMenu`, `Sandbox` and
  `LevelEditor` for real, and those scenes now carry a music player — so running the
  tests played the menu theme out loud on the machine of whoever pressed run.
  `TestSilence` turns `AudioListener.volume` down for the whole suite; muting the
  *output* is the one change that cannot alter what is under test, and `AudioTests` still
  asserts on every decision the mix makes.

## Gotchas

- **Every round in the game goes off through `Explosion.Spawn`.** Hanging the bang there
  is right — it is one line instead of six — but the ASV fires eight rounds a second and
  each one detonates at about 0.4 m. Without `HeardAbove` a single ASV sounds like an
  artillery barrage. Those rounds *are* heard, as an impact off the armour they struck,
  which is the right sound for a bullet.
- **"Hold the last theme" held the victory fanfare into the rematch.** The fallback for
  "nobody is deployed" was *keep playing whatever is playing*, which is correct for a
  theme and wrong for an end cue. Caught by a test written for `Match.Restart`, which
  nothing in the game calls yet. Fixed with `AudioRoster.IsMatchTheme`.
- **A volume stepper cannot click before it steps.** The arrow's own `UiSelect` fires
  *before* the action, so it would play at the level you just left — and stepping up from
  OFF would play at a volume of nothing, giving no feedback at the one moment feedback
  matters most. The sound row's arrows are silenced and `StepSound` plays the sample
  itself, after applying. Getting this wrong the first time produced a double click.
- **`Time.timeScale = 0` does not silence anything.** Unity audio is not time-scaled, so
  pausing left every engine on the field idling under the pause panel. `EngineAudio`
  checks the time scale explicitly. (The one-shots want the opposite and get it for free:
  the pause menu's own buttons still click.)
- **`PlayerVehicleDriver.Team` is a roster scan, not a field.** It reads the paint off up
  to eleven vehicles. `MatchMusic` asked it from `Update` for both seats on every frame of
  a finished match until the result was latched.
- **A stopped engine has to be `Stop()`ped, not turned down.** Twenty-two vehicles each
  holding a voice open at zero volume for a whole match is twenty-two voices.
- **Scene-scoped audio means a scene-changing click is cut short.** The director belongs
  to its scene, so PLAY and MAIN MENU lose the tail of their click to the load. The sound
  is played before the action rather than after, so what survives is the front of it.
  Making audio survive scene loads would fix it and cost a persistent-singleton lifecycle;
  not worth it for 120 ms.

## File map

```
unity/Assets/RF/Scripts/Audio/            new: the whole runtime side
  SfxKind.cs / MusicKind.cs               one value per clip; the value IS the file name
  AudioRoster.cs                          names, and which sound an event asks for
  AudioMixdown.cs                         the mix: distance -> volume, speed -> pitch
  AudioCatalog.cs                         ScriptableObject; mirrors LevelCatalog
  AudioSfxClip.cs / AudioMusicClip.cs     its two row types
  AudioDirector.cs                        scene singleton: catalog + the one-shot source
  Sfx.cs                                  Play / PlayAt - how everything else asks
  MusicPlayer.cs                          two sources, crossfaded (mechanism)
  MatchMusic.cs                           which theme, and the end cue (policy)
  EngineAudio.cs                          the only continuous sound in the game
unity/Assets/RF/Editor/Gameplay/
  AudioCatalogBuilder.cs                  new: builds the catalog, seeds the three scenes
  VehiclePrefabBuilder.cs                 + EngineAudio and its source, per vehicle
  VehicleSandboxScene.cs                  + Audio object, + MatchMusic on the session
  MainMenuScene.cs / LevelEditorScene.cs  + Audio object (the editor gets no music)
unity/Assets/RF/Audio/AudioCatalog.asset  new: 30 rows, built not hand-assigned
unity/Assets/RF/Scripts/
  Combat/VehicleWeapon.cs                 the shot, beside the muzzle flash
  Combat/Explosion.cs                     the bang, above HeardAbove
  Combat/Projectile.cs                    the impact, beside the sparks
  Destruction/Destructible.cs             damage and collapse, behind throwDebris
  Objective/Flag.cs                       four transitions, behind a new `heard` flag
  Core/TopDownCameraRig.cs                a live list of seats, and Focus - the ears
  Menu/GameSettings.cs                    two volumes, cached; the panel rows
  Menu/MainMenuController.cs              menu theme; SOUND and MUSIC steppers
  Menu/PauseMenu.cs                       CONTINUE is a back, not a click
  Editing/EditorButton.cs                 every button in the game, in one method
  Editing/EditorTheme.cs                  the overload that picks a different noise
  Editing/EditorInspector.cs              side buttons select rather than click
  Players/PlayerVehicleDriver.cs          the bunker roster: select, deploy, denied
unity/Assets/RF/Tests/
  EditMode/AudioRosterTests.cs            new: both directions of the roster
  EditMode/AudioMixdownTests.cs           new: the mix, without a speaker
  EditMode/SandboxWiringTests.cs          + the scene can make a noise, still one ear
  PlayMode/AudioTests.cs                  new: what was actually heard
  PlayMode/TestSilence.cs                 new: the suite does not use your speakers
```

## Tests

34 new, and the suites at 519 EditMode and 189 PlayMode, all passing.

The roster tests are checked **in both directions**, which is the half that matters: one
direction catches a sound the game asks for and has not got (a weapon added without a
recipe fires silently, and nothing else in the project would ever notice), the other
catches a clip that was rendered, committed and then never used by anything — twenty
megabytes of audio is committed, so an orphan is worth naming.

One of them is `TheSuiteIsMuted`, which exists because the interesting failure there is not
a red test but somebody's afternoon interrupted by the menu theme — so the mute is asserted
rather than assumed.

`AudioTests` asserts on **"was it heard"**, never on "was the call made".
`AudioDirector.LastSound` is only set once a clip has been found and its level is above
zero, so a missing catalog row, a shot off the end of the map and a transition that fires
at load time are all distinguishable from working code — which they are not from the call
site, and never in a still.

## What this does not do

- **Nobody has listened to the game yet.** The clips were designed from the synthesis up
  and verified by measurement; the wiring is verified by tests. Neither can tell you
  whether the cannon sounds like a cannon *in a firefight*, whether 24 m is the right
  radius for "on my screen", or whether the four themes read as four themes. Expect to
  re-tune: that is what `-Listen` and byte-identical re-renders are for.
- **Some things in the game still make no noise**, because the thirty rendered clips do
  not include one: a team door sinking, a supply point refuelling, a vehicle rolling out
  of its bunker on the lift. Each needs a recipe first, then one line.
- **Audio does not survive a scene change** — see the gotcha above.
- **There is no audio mixer.** Two volumes multiply straight into the sources. A mixer
  would buy ducking (dropping the music under an explosion) and is the obvious next step
  if the mix feels crowded, but it is a Unity asset rather than generated source, which is
  a decision this project should make deliberately rather than by reaching for it.

---

# Part one: the pipeline

## Why synthesis, and what it cost

The plan recommended licensing CC0 clips for a first pass and called full synthesis
"a research project in itself". That estimate was fair for *realistic* audio and wrong
for this game: everything here is flat-shaded primitives and vertex colours, and the
matching sound is arcade-synthetic, which is the kind synthesis is actually good at.
Choosing it keeps the § 8 "precedent worth deciding explicitly" consistent with
Blender-Python for models and C# for levels — **every asset in this project is
generated from committed source**, with no exception carved out for audio.

The real cost was not DSP. It was that SuperCollider is a live-performance tool being
used as a build step, and most of a day's traps (below) come from that mismatch rather
than from making noises.

## Decisions worth knowing

- **Renders are byte-identical.** Re-rendering an unchanged sound produces the same
  file, so touching one sound is a one-file diff. This needed work: noise UGens draw
  from the server RNG, which is seeded from the clock, so every build would otherwise
  rewrite every noise-based clip. `~rf[\seedFor]` seeds it from a hash of the sound's
  name. Without this the pipeline would have been unusable in review.
- **The build measures what it wrote.** Every render is read back and its peak and RMS
  reported, because a render that "succeeds" into silence is the most common failure
  mode here — and it is invisible until someone enters play mode. Three under-level
  sounds were caught this way that ear-checking a 30-file batch would have missed.
- **One instrument per sound, played many times.** Music is not a special case in the
  engine: a theme is one `SynthDef` with a `notes` list, and the three musical voices
  (brass, bass, snare) are one graph behind a `Select.ar`. This is why a 20-second
  march and a 0.12-second menu click go through exactly the same code path.
- **Per-vehicle music, because the design doc asked for it.** M8's line is
  "per-vehicle music/SFX hook", so there are four match themes rather than one bed:
  what is playing tells you what you are driving. They share a key and a motif so that
  switching vehicle is a change of character, not of track.
- **The themes are original.** The obvious homage would be to arrange the public-domain
  classical pieces the genre is known for. Writing them instead keeps the soundtrack
  the same kind of thing as the rest of the repository: source that can be diffed.
- **Levels are relative and deliberate.** A menu click peaks around 0.27 and a cannon
  around 0.68. That mix is the point, and it is why the importer does *not* use Unity's
  `forceToMono` — that setting drags a Normalize flag along with it that would rescale
  every clip to full scale and throw the mix away. The renderer guarantees mono
  instead, and the importer verifies it.
- **Import settings are code, not committed YAML.** Unlike the `.glb` files, the
  `.wav.meta` files are Unity-generated. An `AudioImporter` has far more settings than
  a model importer, and hand-authoring that YAML is a good way to ship clips that are
  subtly wrong in a built player. `AudioImportSettings.cs` applies the rules on import,
  so a re-render can never silently revert them.
- **This adds ~20 MB to the repository**, three quarters of it the seven stereo music
  beds. That is the price of committing build output as uncompressed PCM, and it is
  paid deliberately: Unity re-encodes to Vorbis at build time anyway, so what is
  committed is *source* and should stay lossless. If it ever becomes a problem, the
  lever is music length or sample rate in the recipes — not compressing what is stored,
  which would make every re-render a lossy round trip.

## Gotchas

SuperCollider ones, in rough order of time lost. All are also recorded in
`audio/README.md`, where someone editing a sound will actually look.

- **A silent file is a successful render.** Adding a synth to the tail of a *synth*
  rather than a group is accepted by the server, creates no node, and writes a valid
  file full of zeroes. Cost an hour before the measurement step existed; that step now
  exists because of it.
- **`sclang` never exits on error.** A script that raises leaves the interpreter idling
  in its event loop forever. Both wrappers impose a hard timeout, and the Unity bridge
  does too. If a build "hangs", look for an `ERROR:` line, not a deadlock.
- **Killing `sclang` on timeout does not kill `scsynth`.** Caught by adversarial review
  rather than by hitting it: each sound renders through `engine.scd`'s
  `cmd.unixCmdGetStdOut`, which runs `scsynth` synchronously as a separate process. If
  `scsynth` itself is what hangs, the Unity bridge's `process.Kill()` only terminates
  `sclang` — `scsynth` is left running and can hold the output `.wav` open into the next
  build. `SuperColliderAudioPipeline.KillOrphanedScsynth` now sweeps for any `scsynth`
  started at or after the render began and kills those too, deliberately leaving alone
  one a developer already has open via `-Listen`. The same timeout path was also
  discarding the output it had already captured — the very thing its own error message
  tells you to go read — so the timeout log now includes it instead of only the failed
  exit-code path doing so.
- **`exit` does not return.** `0.exit` schedules the quit but lets the current
  expression run to completion — an early `0.exit` printed a listing and *then*
  rendered all 30 sounds anyway. Real early returns need `block { |done| ... }`.
- **`sclang` eats `--flags`.** It parses any leading dash as one of its own options and
  exits with "unrecognised option"; a lone `--` drops it into an interactive REPL that
  never returns. The build script takes bare words, and the wrappers translate.
- **`arg` is a reserved word.** `{ |arg, i| ... }` is a syntax error, not a shadowing
  warning, and the message ("unexpected ARG, expecting ELLIPSIS") does not say so.
- **`matchRegexp` is backwards.** The *pattern* is the receiver, the string under test
  is the argument. Getting this wrong fails every name silently-plausibly.
- **`isAbsolutePath` is Unix-only.** It tests for a leading slash, so it calls every
  `C:/...` path relative — which produced the memorable
  `C:\...\audio\C:/git/.../engine.scd`.
- **Loops are arithmetic, not vibes.** An engine loop is seamless only when
  `rate * dur` is a whole number; a music loop needs its final note to finish before
  `dur` or the tail is cut and it clicks. Both constraints are commented in the modules
  that depend on them, because both are silent when broken.

## File map

```
audio/                                          the whole pipeline
  README.md                                     how to use it, and every trap above
  build.ps1 / build.sh                          wrappers: find sclang, flags, timeout
  build.scd                                     discover, render, measure, report
  audition.scd                                  play live instead of rendering
  rf/engine.scd                                 recipe -> Score -> scsynth -> .wav
  rf/voices.scd                                 shared DSP vocabulary (13 voices)
  sounds/*.scd                                  6 modules, 30 sounds
unity/Assets/RF/Editor/ArtPipeline/
  SuperColliderAudioPipeline.cs                 Tools > IronFlag menu items
  AudioImportSettings.cs                        import rules as code
unity/Assets/RF/Audio/SFX/                      23 rendered .wav
unity/Assets/RF/Audio/Music/                     7 rendered .wav
```
