# Progress

Milestone log (BUILD_BRIEF §9). Per milestone: what was built, what differs from the brief and why, what's open. Design questions and the choices made for them are in `QUESTIONS.md`.

## M0 — Setup

**Built**
- `TheCurator.sln` with `src/Curator.Core` (no references at all), `src/Curator.Core.Tests` (xUnit) and the Godot project `game/Curator.Godot.csproj` (`Godot.NET.Sdk/4.6.3`, references Core).
- `game/project.godot` from the designer's skeleton, moved into `game/`. Its main scene is `scenes/Main.tscn`, an empty node whose script prints a line that proves Core is reachable. The viewport is 1920×1080 with `canvas_items` stretch and `keep` aspect.
- The SplitMix64 PRNG (`Curator.Core.Game.Prng`), keyed by seed, event sequence number and salt, with tests.
- `ProjectStructureTests`: Core references nothing Godot; all three projects share one `TargetFramework`.
- Shared build settings in `Directory.Build.props` (nullable on, implicit usings off, warnings as errors, code style enforced in the build), suppressions in `.globalconfig`.
- CI: `.github/workflows/ci-the-curator.yml` restores, builds and tests the solution on Ubuntu for pushes and PRs that touch `the-curator/`.
- Content and docs from the hand-off zip: `docs/GDD.md`, `BUILD_BRIEF.md` and `CONTENT_GUIDE.md` are unchanged. `game/content/` holds the seeded files and the gold standards, also unchanged.

**Verified**: `dotnet build` and `dotnet test` are green (9 tests). Godot 4.6.3 (Linux .NET build) imports the project, builds it with `--build-solutions`, and runs the main scene with `--headless` and in a window under Xvfb. All of that ran with only the .NET 10 runtime installed.

**Differs from the brief**
- The project lives in `the-curator/` inside the multi-project repo, not at a repo root. Paths in the docs are relative to `the-curator/`.
- `net10.0`, not Godot 4.6's template default `net8.0` (Q3).
- The window opens at 1600×900 (`window_*_override`) so it fits a 1080p screen with a taskbar. The viewport is still 1920×1080, as the brief asks (Q4).
- The work ran in a Linux cloud container, not on the designer's Windows PC (A4). Windows-only settings, such as the `d3d12` driver and the export preset (§7.13), haven't been tried (Q5).

**Open**: whether the designer's PC has the .NET 10 SDK (Q3).

## M1 — Core rules

**Built**
- `Curator.Core`, Godot-free:
  - **Content**: records mirroring every JSON file, a strict loader (camelCase; unknown properties, nulls and missing required fields are errors) and the §8.3 validator. Problems are reported with file and JSON path.
  - **Commands and events**: the §4.2 commands and 40 events. A refused command emits nothing.
  - **State**: `GameState` is a pure fold over the event log and needs no content to replay.
  - **Rules** (`Rules/`): mana, trust, conditions, outcomes, scheduling, questions, reputation, calendar.
  - **Projections**: the §4.6 views (HUD, visit, morning, catalogue, book, card, notebook, day and week summaries, debug).
  - **Other**: the page markup parser, save serialisation, and three strategy bots with the guardrail check and the balance report.
- `Curator.Core.Tests`: about 200 xUnit tests on a small test-content world in the test project. That world is the three gold books, a test *Lanterns* and *Advanced Botanical*, the gold clerk and Elara, a short Wren arc and four one-off visitors. The tests cover the §5 rules and the §8.1 edges, every surfacing channel including the `return` fallbacks, upkeep and all three debt letters, the replay test (§8.2: all projections rebuilt from the saved log, for each bot), determinism, and content validation with deliberate faults.
- **Review pass 1** (a fresh agent given only the docs and the code) found these bugs, all now fixed with tests:
  - A forced visit could end in a walk-out or take an offer.
  - Closing early skipped the `return`→letter fallback.
  - Generic letter senders kept `{name}`.
  - `removePage` could hit the wrong book.
  - One mutable "empty visit" object was shared by every session.
  - Content parsing allowed repeated keys and loose enum casing, and a missing `channel` meant silence instead of an error.
  - Saves didn't check their schema or content.

  The views now ask the command checks whether each button applies, so they can't disagree with the rules. The rulings it raised are Q19–Q24.

**Verified**: `dotnet test` is green. All three bots play the test week end to end. The guardrails are report-only for now (`out/balance-report-testcontent.md`); the test world is too small to meet them, which is expected until M2.

**Differs from the brief**
- `CloseForTheDay` also starts the next morning (Q8). The brief's command list has no command that starts a day.
- Added events the brief invited: `OutcomeRedirected`, `RequestMade`, `SlotSkipped`, `VisitEnded`, `ManaAdded`, `LedgerReturnNoted`.
- Added `bookOut` and `noThoughts` generic lines (Q17).
- Rulings on gaps in the brief are Q9–Q16.

**Open**: Q7–Q24 for the designer.
