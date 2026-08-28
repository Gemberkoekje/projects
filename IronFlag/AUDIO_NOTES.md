# Sound and Music: the SuperCollider pipeline

**To understand this, start by reading [`audio/README.md`](audio/README.md), then
[`audio/rf/engine.scd`](audio/rf/engine.scd) for how a sound becomes a file and
[`audio/sounds/weapons.scd`](audio/sounds/weapons.scd) for what authoring one looks
like. Then [`AudioImportSettings.cs`](unity/Assets/RF/Editor/ArtPipeline/AudioImportSettings.cs)
for what Unity does with the result.** Everything else is wiring.

This is the **pipeline** half of
[MASTER_PLAN.md § 8](MASTER_PLAN.md#8-sounds-and-music). The plan's open question 1
("license / commission / synthesise — blocks everything else in this item") is now
answered: **synthesise**. Nothing is wired into gameplay yet — see
[What this does not do](#what-this-does-not-do), which is the whole of the rest of § 8.

| Category | Count | Covers |
|---|---|---|
| Weapons | 5 | One per `WeaponKind` — grenade, cannon, rocket, chaingun, autocannon |
| Impacts | 4 | Explosion, armour hit, and both `Destructible` state transitions |
| Objective | 6 | Flag pickup / dropped / returned / captured, match won / lost |
| UI | 4 | Click, select, back, denied |
| Vehicles | 4 | One looping engine per `VehicleKind` |
| Music | 7 | Menu theme, one theme per vehicle, victory and defeat cues |

**Hear it**: `./audio/build.ps1 -Listen -Sound Cannon`. **Build it**:
`./audio/build.ps1`, or `Tools > IronFlag > Rebuild All Audio from SuperCollider`.

---

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
- **Nothing is positional yet.** § 8's split-screen wrinkle is real — one
  `AudioListener` for the whole rig, test-enforced — and the clips are rendered mono so
  that 2D or 3D stays a decision for the wiring pass, not something the assets foreclose.
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
audio/                                          new: the whole pipeline
  README.md                                     how to use it, and every trap above
  build.ps1 / build.sh                          wrappers: find sclang, flags, timeout
  build.scd                                     discover, render, measure, report
  audition.scd                                  play live instead of rendering
  rf/engine.scd                                 recipe -> Score -> scsynth -> .wav
  rf/voices.scd                                 shared DSP vocabulary (13 voices)
  sounds/*.scd                                  6 modules, 30 sounds
unity/Assets/RF/Editor/ArtPipeline/
  SuperColliderAudioPipeline.cs                 new: Tools > IronFlag menu items
  AudioImportSettings.cs                        new: import rules as code
unity/Assets/RF/Audio/SFX/                      23 rendered .wav (was: one .gitkeep)
unity/Assets/RF/Audio/Music/                     7 rendered .wav (was: one .gitkeep)
```

`AudioListener` placement is untouched, so `SandboxWiringTests`' "a split screen still
only has one set of speakers" invariant still holds.

## Tests

There are no new automated tests, and that is a gap rather than a decision. The build
self-checks (name validation, silence and clipping detection, byte-identical
re-renders) and the Unity side is verified only by a batch-mode compile plus reading
back the generated `.meta` files. A play-mode test would have nothing to assert against
until the wiring below exists.

## What this does not do

**No sound is wired into gameplay.** Rendering 30 clips into the project does not make
the game audible; every call site in § 8 is still untouched. Specifically, still to do:

- `AudioCatalog` (the `ScriptableObject` mapping `SfxKind`/`MusicKind` → `AudioClip`,
  mirroring `LevelCatalog` — a built player has no asset database, so nothing can look
  these up by path), plus its builder.
- `Sfx.cs` and `MusicPlayer.cs` under `unity/Assets/RF/Scripts/Audio/`.
- The call sites: `VehicleWeapon.TryFire`, `Explosion.Spawn`, `Destructible`'s state
  transitions, `Flag`'s transitions, `Match.Win`, and per-vehicle engine loops on
  `VehicleController` — the last being the only *continuous* hook and the one § 8
  rightly says to budget more time for.
- The 2D-versus-3D decision (§ 8 open question 2), which the mono clips leave open.
- A volume setting in the options menu, which `MASTER_PLAN.md` § 3 already lists.

**Nobody has listened to these yet.** They were designed from the synthesis up and
verified by measurement — every clip is confirmed non-silent, un-clipped, the right
length and the right channel count — but measurement cannot tell you whether the
cannon sounds like a cannon. Audition them before trusting any of it:

```
./audio/build.ps1 -Listen
```

Expect to re-tune. That is what `-Listen` and a one-file diff are for, and the levels
in particular (a click at 0.27 against a cannon at 0.68) are a starting guess at a mix,
not a measured one.
