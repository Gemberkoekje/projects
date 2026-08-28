# SuperCollider audio pipeline

Every IronFlag sound is **synthesised from code, not recorded or licensed**. There are
no source sessions to maintain: `audio/sounds/*.scd` is the source of truth, and the
`.wav` files in `unity/Assets/RF/Audio/` are build output that happens to be committed.

This is the same bargain the Blender pipeline makes for models, for the same reasons.
`MASTER_PLAN.md` §8 called sourcing "genuinely the user's call" between licensing
clips, commissioning work, and synthesising them — synthesis is the option that keeps
audio the same kind of thing as everything else here: something you can diff, review
and re-derive.

---

## Running a build

```bash
./build.ps1                      # render every sound into the Unity project
```

```bash
./build.ps1 -Sound Cannon        # render just the sounds matching "Cannon"
```

```bash
./build.ps1 -List                # list known sounds with length and channels
```

`build.sh` is the same wrapper for POSIX shells (`--sound`, `--list`). Both find
SuperCollider automatically; set `IRONFLAG_SUPERCOLLIDER` to override. From inside
Unity, **Tools > IronFlag > Rebuild All Audio from SuperCollider** runs the identical
command and re-imports the result, and right-clicking a clip gives you
**Rebuild This Sound from SuperCollider**.

A full render of all 30 sounds takes well under a minute — non-realtime synthesis is
far faster than the audio it produces.

## Listening while you work

Rendering, importing and entering play mode is far too slow a loop to tune a sound in.
`-Listen` plays the selection out loud and writes nothing:

```bash
./build.ps1 -Listen -Sound Cannon -Repeat 3
```

This is the main reason the pipeline is SuperCollider rather than a script that writes
wav files: the recipe you hear is the recipe that renders. Edit, listen, repeat, and
only run a real build once it sounds right.

---

## Adding a sound

1. Open the module in `sounds/` that the sound belongs to, or start a new one.
2. Add an entry keyed by asset name, built out of `~rfv` voices.
3. Run `./build.ps1 -Listen -Sound <YourName>` until it sounds right.
4. Run `./build.ps1 -Sound <YourName>` and check the peak it reports.

`build.scd` discovers modules automatically — there is no registry to update. It
rejects names that break the `RF_Sfx_<Name>` / `RF_Music_<Name>` rule before rendering,
so a typo fails the build instead of surfacing later as a missing clip in Unity.

### The recipe format

A sound is an `Event` with a `graph` and not much else:

```supercollider
'RF_Sfx_WeaponCannon': (
    dur: 1.10,           // seconds of audio to render
    channels: 1,         // 1 = mono for SFX, 2 = stereo for music
    note: "loop",        // optional, shown by -List
    graph: {             // returns a signal; the engine adds output and gain
        var snap = ~rfv[\crack].(freq: 1800, dur: 0.09, res: 0.25);
        var body = ~rfv[\boom].(from: 220, to: 34, dur: 0.75);
        (snap + body) * 0.42;
    }
)
```

A `graph` returns a signal and nothing else — the engine wraps it with `Out.ar`, the
amplitude control, a mono/stereo fold, and a watchdog that frees the synth even if the
graph forgets its own `doneAction`.

For anything with more than one event in it — all the music — add `notes`, a list of
`[time, [\key, value, ...]]` pairs. Each entry spawns the same graph again with those
arguments, so a theme is one instrument played many times:

```supercollider
notes: melody.(96, [[0.0, 0, 1.5, 0.20], [2.0, 2, 0.5, 0.16]], 220)
```

Arguments declared on the `graph` function become settable per note, courtesy of
`SynthDef.wrap`. Avoid naming one `out`, `amp` or `lifetime`, which the engine already
uses.

### The voices

`rf/voices.scd` is the shared DSP vocabulary — the audio equivalent of
`blender/rf/primitives.py`. Build sounds out of these rather than raw UGens wherever
you can, so the whole game keeps one character: `crack`, `tick`, `boom`, `shell`,
`air`, `debris`, `motor`, `rotor`, `brass`, `bass`, `snare`, `ensemble`.

---

## How it works

`build.scd` loads the engine and voices, discovers `sounds/*.scd`, and for each sound
builds a `Score` — a timestamped list of OSC commands — which `scsynth` renders offline
with `-N`. Nothing is played in real time and no audio device is involved, which is why
this runs fine in Unity's batch mode and on a build agent.

Each render is then **read back and measured**. A render that "succeeds" into silence
is by far the most common failure here, so the build never trusts an exit code alone;
it reports peak and RMS per sound and warns when a clip is clipping, silent, or quiet
enough to have probably been a mistake.

### Renders are deterministic

Re-rendering an unchanged sound produces a **byte-identical** file, so a change to one
sound is a one-file diff. That is not free: noise UGens draw from the server's RNG,
which is seeded from the clock unless told otherwise, so every build would otherwise
rewrite every noise-based clip. The engine seeds the RNG per sound from a hash of its
name — see `~rf[\seedFor]`.

---

## Traps

Things that cost time here, recorded so they only cost it once.

- **A silent file is a successful render.** Adding a synth to the tail of a *synth*
  rather than a group is accepted by the server, creates no node, and produces a
  perfectly valid file full of zeroes. This is why the build measures what it wrote.
- **`sclang` never exits on error.** A script that raises leaves the interpreter
  sitting in its event loop indefinitely, so both wrappers impose a hard timeout. If a
  build "hangs", look for an `ERROR:` line in the output, not a deadlock.
- **`sclang` eats `--flags`.** It parses anything leading with a dash as one of its own
  options and exits with "unrecognised option"; a bare `--` drops it into an
  interactive REPL that never returns. Hence the bare-word arguments the wrappers
  translate into.
- **`arg` is a reserved word.** Naming a block argument `arg` is a syntax error, not a
  shadowing warning.
- **`matchRegexp` is backwards.** The *pattern* is the receiver and the string under
  test is the argument.
- **`exit` does not return.** `0.exit` schedules the process to quit but lets the
  current expression run to completion, so it cannot be used as an early return — hence
  the `block { |done| ... }` in `build.scd`.
- **`isAbsolutePath` is Unix-only.** It tests for a leading slash, so it calls every
  `C:/...` path relative.
- **Loops need arithmetic.** An engine loop is only seamless when
  `rate * dur` is a whole number, so the file ends exactly on a cycle boundary; a
  music loop needs its last note to finish before `dur`, or the tail is cut off and the
  loop clicks. Both are noted in the modules that depend on them.

---

## What Unity does with these

Import settings are **not** committed as `.meta` YAML — they are applied in code by
`AudioImportSettings.cs`, so a re-render can never silently revert them:

| | SFX | Music |
|---|---|---|
| Format | PCM, decompress on load | Vorbis, streaming |
| Channels | mono (checked, not forced) | stereo |
| Resident | preloaded | loaded in background |

SFX are mono because Unity will not pan a stereo clip in 3D at all. They are
uncompressed because they are all under three seconds and the chaingun fires several
times a second — there should be no decode cost when a weapon fires. Music streams
because twenty seconds of stereo is far too much to hold in memory uncompressed.

Note "checked, not forced". Unity's `forceToMono` drags a Normalize flag along with it
that rescales the clip, which would flatten a mix where a menu click is quiet and a
cannon is loud *on purpose*. The renderer already guarantees mono, so the importer
verifies that and warns instead of converting.

Looping is deliberately not an import setting, because Unity has no per-clip loop
flag — it is a property of the `AudioSource`. The engine loops and music beds are
*rendered* to loop cleanly, but whatever plays them still has to set
`AudioSource.loop`.

---

## File map

```
audio/
  build.ps1 / build.sh      wrappers: find sclang, translate flags, impose a timeout
  build.scd                 discovers sounds, renders them, measures the results
  audition.scd              plays sounds live instead of rendering (-Listen)
  rf/engine.scd             recipe -> Score -> scsynth -> measured .wav
  rf/voices.scd             the shared DSP vocabulary every sound is built from
  sounds/weapons.scd        one per WeaponKind
  sounds/impacts.scd        explosion, impact, structure damage states
  sounds/objective.scd      flag transitions, match won/lost
  sounds/ui.scd             menu clicks
  sounds/vehicles.scd       one looping engine per VehicleKind
  sounds/music.scd          menu theme, per-vehicle themes, end cues
```

Output goes to `unity/Assets/RF/Audio/SFX/` and `unity/Assets/RF/Audio/Music/`.
