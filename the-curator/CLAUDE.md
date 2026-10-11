# The Curator — notes for Claude Code

A Papers Please–like game about a librarian lending spell books they can't fully read. Godot 4 and C#. Current goal: the one-week prototype.

## Read first
1. `docs/BUILD_BRIEF.md` — what to build, the exact rules, the milestones. Start here.
2. `docs/GDD.md` — design intent. It wins on questions of intent; `game/content/balance.json` wins on numbers.
3. `docs/CONTENT_GUIDE.md` — before writing or validating any content.

## Commands
Run from this folder (`the-curator/`). Verified in M0 (the cloud runs used Linux and bash; the PowerShell lines are the same commands for Windows).

```powershell
dotnet build      # builds Core, the tests and the Godot project (into game/.godot/mono/temp/bin)
dotnet test

# Point GODOT at the installed Godot .NET editor once (new shells pick it up):
#   setx GODOT "C:\path\to\Godot_v4.6.3-stable_mono_win64.exe"
& $env:GODOT --headless --path game --import                      # once after cloning, and after new paintings arrive
& $env:GODOT --path game                                          # play: the title screen (run `dotnet build` first; the editor's Play button builds for you)
& $env:GODOT --path game -- --new --seed 7                        # straight into a new game, ignoring the save
& $env:GODOT --path game -- --shots out/shots --seed 1            # screenshot run (windowed): the full §7.11 sequence, 24 PNGs
& $env:GODOT --path game -- --clicktest --seed 1                  # plays days 1–2 by injected clicks and keys, then saves and continues; exit code 0 when every step works
& $env:GODOT --path game --headless -- --autoplay careful --seed 1 # a strategy bot (lend-all, decline-all, careful) plays the week through the UI (`--days n`: n whole days); exit code 0 when no errors were logged
```

```bash
# Cloud / Linux: the same commands with `godot`. Screenshot runs need a display:
xvfb-run -a -s "-screen 0 1920x1080x24" godot --path game --audio-driver Dummy -- --shots out/shots --seed 1

# The Windows beta zip, as CI builds it (needs the 4.6.3.stable.mono export templates; writes build/):
GODOT=godot tools/packaging/build-windows.sh 0.1.0-beta build
```

- `TheCurator.sln` (here) is the solution for building and testing. `game/Curator.Godot.sln` exists only because Godot's editor build and export look for a solution beside the project; `game/export_presets.cfg` holds the Windows export (not yet run on a Windows machine).
- Beta builds: every push to main exports the Windows build and publishes `TheCurator-0.1.<run>-beta-windows.zip` as a GitHub pre-release (tag `the-curator-v0.1.<run>-beta`); every run also keeps the unzipped build as a workflow artifact for 14 days (GitHub zips it for download) (`.github/workflows/ci-the-curator.yml`, `tools/packaging/`).
- Saves and settings live in Godot's `user://` folder (`save.json`, `settings.cfg`). F1–F4 (debug overlay and cheats) work in debug builds only.
- Godot version: **4.6.3 stable, .NET build** (the project's `Godot.NET.Sdk/4.6.3`). Keep to the 4.6 API.
- .NET SDK: **10.0** (verified with 10.0.401). Every project targets **`net10.0`**; `ProjectStructureTests` fails if the three ever differ.
- C# follows the workspace rules in `../instructions.md` (no implicit usings, warnings are errors, suppressions only in `.globalconfig` with a reason).

**Cloud sessions** start without .NET or Godot. Install them with `dot.net/v1/dotnet-install.sh --channel 10.0` and the Linux .NET build of Godot from `https://downloads.godotengine.org/?version=4.6.3&flavor=stable&slug=mono_linux_x86_64.zip&platform=linux.64`. If GitHub release downloads are blocked there, that mirror works. `mesa-vulkan-drivers` gives Xvfb runs a software Vulkan renderer.

## Golden rules
1. Rules and state live in `Curator.Core`, which never references Godot. Godot code sends commands and renders projections; no game rules in `game/scripts/`.
2. Every state change is an event. Nothing mutates state outside command handling.
3. Deterministic: randomness only through the Core PRNG, with results stored in events.
4. Numbers live in `balance.json` and story text in content JSON — never in code.
5. Content you write gets `"placeholder": true`. Don't edit files marked `"placeholder": false`, the gold-standard content (`docs/CONTENT_GUIDE.md` §1), `docs/GDD.md`, or anything in `game/art/painted/` — except to fix a schema error in gold content, which you log.
6. Never stall on a design question: choose the most reasonable reading, record it and your choice in `docs/QUESTIONS.md`, carry on.
7. After any change to what's on screen, run the screenshot mode and look at the images.

## Done means
- `dotnet test` is green, content validation and balance guardrails included.
- The milestone's acceptance checks (`docs/BUILD_BRIEF.md` §9) pass.
- `docs/PROGRESS.md` says what was built and what differs from the brief, and why; open questions are in `docs/QUESTIONS.md`.

## About the designer
Reviews every milestone. Paints the art in oils; photographs arrive in `game/art/painted/` over time, so placeholders must stay swappable.
