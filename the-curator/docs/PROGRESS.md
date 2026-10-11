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

## M2 — Content

**Built**
- `game/content/` is complete per `CONTENT_GUIDE.md`. Everything new is a placeholder (`"placeholder": true`):
  - 12 books, 15 with the three gold ones. Twelve of the 15 have at least one outlier page (danger 2 or more).
  - 13 patrons, 15 with the gold clerk and Elara: four story patrons (Elara, Wren, Jory, Hennie), ten one-visit fillers and the tutorial clerk.
  - `outcomes_generic.json`, `newspaper.json` (masthead, daily flavour, reputation tone lines) and `letters.json` (tutorial notes, the three Board debt letters).
  - `ingredients.json` now holds the new books' ingredients; `questions.json` has the two generic lines from Q17.
- The gold files are untouched. Validation found no schema errors in them.
- `out/content-report.md`, written by the tests, is the designer's reading copy. For each patron it lists every visit with its greeting, request, needs and temptations, Read Thoughts fragments, questions and what they unlock, outcomes and overrides, then a lend matrix: for each relevant book, the outcome category, channel and outcome it leads to.
- The guardrails (§8.4) are enforced on the real content by `BalanceTests`, over 10 seeds. Five of the six hold on every seed. The sixth, careful causes at most 1 harm or mixed outcome, can't hold with the bot as specified (Q25). The test checks a stand-in instead: careful causes fewer than lend-all on every seed, and at least 2 fewer on average. The report still shows the original line as failing.
- `out/balance-report.md` gained a guardrail table and a silence section. About a third of outcomes should never surface (CONTENT_GUIDE §7): lend-all 37%, careful 43%.
- `RealContentTests`: no validation errors or warnings, the whole catalogue is present, and the content report covers every visit.

**Verified**: `dotnet test` is green (213 tests), validation and guardrails included.

**Differs from the brief**
- The careful-bot guardrail is relaxed (Q25).
- Jory's results visit has a third variant (Q26).

**Open**: Q25 and Q26. Review pass 2 is running; its fixes come in the next commit.

## M3 — Scene, camera, light

**Built**
- `game/art/layout.json` holds the §7.3 slots (rect, z, light mask, placeholder style, fallback colour), the candle flame's position and frames, fallback colours for covers and paper, and the lighting for each phase.
- The scene tree of §7.1: `World` (Layers built from the layout, Patron, Desk, Lights, Camera2D), `Ui` and `Debug`.
- Each slot shows `art/painted/<id>.png` if it exists, scaled to its rect, then `art/easel/<id>.png` (Q6), else a flat procedural placeholder: wall with an open arch, stone beyond it, shelves of books (spine colours and heights drawn from a hash of the slot id, so they're the same every run), the barred cabinet, counter and desk planks, the candle and a hooded silhouette. A PNG dropped in shows up on the next run, even before the editor has imported it.
- The camera pans between the up view (y 0–1080) and the down view (y 720–1800) in 0.45 s with sine easing, on W/S, ↑/↓ and the mouse wheel.
- Lighting:
  - A `CanvasModulate` tints each phase.
  - The candle is a warm `PointLight2D` over the counter and desk. It flickers by `FastNoiseLite` sampled over time, with ±1 px of jitter.
  - The flame has four frames on irregular timing. It sits on its own canvas layer so the night tint doesn't darken it.
  - The archway light sits behind the patron; the window light falls from the left across the far layers.
  - Light masks keep each light on its own layers. Phases blend over 1.5 s.
- Screenshot mode, first part: `--shots <dir>` saves the up and down views at morning, dusk and night, then prints where each slot's art came from. The full §7.11 sequence comes in M5.
- Command-line parsing for `--seed`, `--new`, `--shots`, `--autoplay` and `--days`.

**Verified**
- The screenshots (1920×1080) pass §8.6 for what's on screen so far. The silhouette reads against the lit archway in every phase. The candle is visibly warm, most of all at night. Both views are framed as in §7.2, with the counter band in both.
- The painted override: a dummy `art/painted/counter.png` replaced the counter's placeholder, scaled to the slot's rect and lit by the candle, and the run reported `counter=painted`. Then it was deleted.

**Differs from the brief**
- The screenshots also cover dusk.
- The pan tabs at the top and bottom edge are UI, so they come with the HUD in M4.
- No Easel paintings yet. The playable week (M4–M5) comes first, then the interim Easel art, so the art can't hold up the game. Until then every slot shows its flat placeholder.

**Open**: nothing new.
