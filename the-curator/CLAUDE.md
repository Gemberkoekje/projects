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
& $env:GODOT --path game                                          # play (run `dotnet build` first; the editor's Play button builds for you)
& $env:GODOT --path game -- --shots out/shots --seed 1            # screenshot run (windowed) — from M5
& $env:GODOT --path game --headless -- --autoplay careful --seed 1 # strategy bot — from M5
```

```bash
# Cloud / Linux: the same commands with `godot`. Screenshot runs need a display:
xvfb-run -a -s "-screen 0 1920x1080x24" godot --path game --audio-driver Dummy -- --shots out/shots --seed 1
```

- Godot version: **4.6.3 stable, .NET build** (the project's `Godot.NET.Sdk/4.6.3`). Keep to the 4.6 API.
- .NET SDK: **10.0** (verified with 10.0.401). Every project targets **`net10.0`**; `ProjectStructureTests` fails if the three ever differ.
- C# follows the workspace rules in `../instructions.md` (no implicit usings, warnings are errors, suppressions only in `.globalconfig` with a reason).

**Cloud sessions** start without .NET or Godot. Install them with `dot.net/v1/dotnet-install.sh --channel 10.0` and the Linux .NET build of Godot from `https://downloads.godotengine.org/?version=4.6.3&flavor=stable&slug=mono_linux_x86_64.zip&platform=linux.64`. GitHub release downloads are blocked there; that mirror isn't. `mesa-vulkan-drivers` gives Xvfb runs a software Vulkan renderer.

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
