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
- The guardrails (§8.4) are enforced on the real content by `BalanceTests`, over 10 seeds. Five of the six hold on every seed. The sixth, careful causes at most 1 harm or mixed outcome, can't hold with the bot as specified (Q25). The test checks a stand-in instead: careful never causes more than lend-all in any seed, and at least a quarter fewer on average. The report still shows the original line as failing.
- `out/balance-report.md` gained a guardrail table and a silence section. About a third of outcomes should never surface (CONTENT_GUIDE §7): lend-all 37%, careful 43%.
- `RealContentTests`: no validation errors or warnings, the whole catalogue is present, and the content report covers every visit.

**Verified**: `dotnet test` is green (213 tests), validation and guardrails included.

**Differs from the brief**
- The careful-bot guardrail is relaxed (Q25).
- Jory's results visit has a third variant (Q26).

- **Review pass 2** (a fresh agent given only the docs and the content) found 37 issues. Fixed:
  - Three of Jory's branches couldn't play: his "Harrow forgot" results visit (no path left him Candid), the Sleep & Its Neighbours override and the "slept through" results visit (the book was still on loan to him). Lending at his brittle visit now raises trust, and Sleep & Its Neighbours comes back after 2 days ("I always bring things back early"). Each results variant was checked by playing its path.
  - Timeline slips: a day-4 greeting about a day-5 newspaper story, a post-exam line before the exam, "two days left" with one left, and an answer that contradicted the misuse outcome.
  - Lines that assumed the wrong book: Wren's "your linen charm works" after the Clerk's Companion, and Hennie's primer jokes on the Advanced Botanical path.
  - Wren's fragment gave the curator a gender. Four descriptions showed faces. Hennie's "Hollin road" read as Wren's "Lower Hollis" and is now the Bracken road.
  - The week's season was muddled. It's Seedmonth, so the harvest fair is now a spring fair, day 6 brings a drying wind, not relief from drought, and Bram's ovens go cold on damp nights, not in winter. The Ollery obituary came after the reading of her will; the obituary is now someone else's.
  - Books: Rust-Call stated its danger plainly; five pages were more legible than any gold page; a page and an ingredient name didn't match their ids or use; *Lanterns*' Fireball moved to page 2, between Linen-Dry and Warm Hands, as its note and the GDD's founding example have it.
  - Four unknown ingredients appeared only on dangerous pages, which gave the danger away. Each is now on ordinary pages too, and 38 of the 58 new pages have an unknown ingredient (was 27).
  - Tilly's cheerful gossip cost reputation (now 0), and she returns *Tricks for the Parlour* the next day so Jory can borrow it on day 7. Jory now wants it for paper moths, not the coin trick Tilly wants.
  - Generic outcomes named particular places; Ines's letter now matches the guide; long flavour items were trimmed.
- The stand-in guardrail asked careful to beat lend-all on every seed. That fails by chance whenever careful misses every outlier in some seed, and the content fixes made it fail. It now asks for never more than lend-all in any seed, and a quarter fewer on average (Q25).

**Open**: Q25–Q30. The validator's trust check can't see dead ends that depend on the path taken (it adds each step's best trust gain across all variants), which is how Jory's dead branches got past it.

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

## M4 — Desk and speech

**Built**
- `GameController` owns the `GameSession`. It sends commands and, after each one, pushes a fresh snapshot of Core's projections to every view. The views only call back into it.
  - It holds presentation state only: which document is open, which returned books were shelved (Q9), and the notebook page showing.
  - A refused command shows its gentle line from `desk.json`, never an error. Disabled buttons show the same line as their tooltip.
- **Up view** (§7.6):
  - The silhouette fades in when a patron arrives and out after their exit line.
  - Beside the archway, a panel holds the visit's transcript (a new Core projection, so its order comes from the event log), with the patron's name and description.
  - Beneath it are patience pips, Read Thoughts with its cost, and the questions: standard first, then unlocked ones marked new.
  - The Read Thoughts fragment floats near the patron's head, in italics with a soft glow, with "Note it" beneath.
- **Down view** (§7.5):
  - The library card sits with "Stamp and lend" and "Hand back the card" beneath it. After a lend, the stamp's slip offers "Note it".
  - The newspaper, letters and returns tray lie beneath the card. In the tray, "Note it" appears while a return can be noted, and a click puts the book back on the shelf.
  - The notebook and catalogue drawer lie by the candle. One document at a time opens in the middle of the desk.
  - The open book is a two-page spread:
    - Left page: the cover (painting or tinted fallback), facts, the previous owner's note, the unread count, and Identify (with its cost and whether it costs patience), Offer this instead, and Stamp and lend for the requested book.
    - Right page: identified pages in partial translation, with notes for resisted pages and torn stubs.
  - The notebook has the automatic header, watched loans with an editable note column, the thoughts noted, and free notes in the handwriting font. Notes aren't events; they're saved beside the log (§7.12).
  - N opens the notebook and C the catalogue.
- **HUD** (§7.7):
  - Mana pips and money sit top left; day, date and phase top centre.
  - During a visit, the top right shows who is waiting, their patience, what they asked for, and the impatience tell.
  - Tabs at the top and bottom edge pan the camera. While looking down, the patron's lines appear at the top of the screen, so nothing they say is missed.
- The bell and the closing sign stand on the counter, seen from both views. Space rings the bell too.
- Closing with patrons still to come asks first, with the line from `desk.json`. Closing shows the day summary over the night scene.
- **Text** (§7.8):
  - Page markup renders as BBCode: translated text in IM Fell English ink, untranslated in grey Pinyon Script, illegible as faded Redacted Script.
  - Known ingredients are in ink, unknown ones in script. The notebook is in Caveat.
  - The four OFL fonts are bundled with their licences (`game/fonts/README.md`).
  - Body text is 19–22 px, the notebook 24–26 px. `UiTheme.TextScale` is ready for the text-size setting (M5).
- **Core additions**:
  - `desk.json` (Q24), validated: every key must be a refusal reason, plus a `default`.
  - `Projector.RefusalLine` and `Projector.IdentifyNote`.
  - Desk notes on `BookView` and the close-early question on `HudView`.
  - The visit transcript (`SpeechLine`).
  - A fix: a requested book counted as "out" once it had been lent to the patron who asked for it.
- `--clicktest` plays days 1 and 2 by injected mouse clicks at each button's place on screen. The day-2 run asks each of three patrons a question, then lends, declines and lends. Each step is checked and the exit code is 0 when all pass.
- `--shots` now plays day 1 (letters, the clerk's visit, a question, the catalogue, the book after an Identify, the lend, the notebook, the summary) and day 2 (the newspaper, the third visit with Read Thoughts, the dusk farewell, the desk at dusk, the summary).

**Verified**
- `dotnet test` is green (222 tests).
- `--clicktest` passes: days 1 and 2 by mouse, 20 checks.
- The `--shots` images pass §8.6. Text is legible in every phase, the night summary included. The silhouette reads against the lit archway. The candle is warm. Nothing overlaps once the gentle lines fade.
- No rule logic outside Core: every button reads its `Can…` flag from a projection.
- The game quits cleanly, with no leaked objects or engine errors at exit.

**Differs from the brief**
- The desk's documents and the speech panel sit on canvas layers that follow the camera, rather than under `World` and `Ui` as plain nodes. They pan with the room but stay outside the night tint (§7.4 legibility). A milder per-phase document tint (in `layout.json`) keeps them in the scene, and the "reading lamp" setting (M5) will turn it off.
- Lines a patron says while the curator looks down also appear at the top of the screen.
- `--clicktest` is a command-line mode the brief doesn't list. It's a smoke test of the mouse path.

**Open**: nothing new.

## M5 — Playable week

**Built**
- **Title** over the library at morning: New game, Continue, Settings, Quit. Continue is enabled when `user://save.json` restores cleanly. If the content changed since the save, a note says so; if the save can't be read, the note says why and Continue stays off.
- **Saves** (§7.12):
  - The game autosaves each evening, written to a temporary file and then swapped in.
  - A save holds the schema version, seed, content version, events, free notes by patron and ledger notes by loan.
  - Continue resumes the next morning; leaving mid-day loses that day. A game saved after the week ended continues at the week summary.
- **Esc** opens the pause menu: Resume, Settings, Back to the title, Quit.
- **Settings**, kept in `user://settings.cfg`: fullscreen; text size (Normal, Large, Larger), which rebuilds the views around the same game; and the reading lamp, which keeps documents at full brightness at dusk and night.
- **The week**: after day 7's summary comes the week summary (§5.15). It shows money, loans, declines and walk-outs, what was heard and when and how, late word, how many loans were never heard of, each story patron's trust (or "never came"), and the tutorial book's state. Then it returns to the title.
- **Debug overlay** (F1, debug builds): the visit's goal, temptations, trust and patience; reputation and band; every resolved outcome with its surface day, channel and key; flags; the seed; and the last 20 events, each on one line (Core's `Describe`). Cheats go through `Debug…` commands, so they're logged: F2 +5 mana, F3 ends the day, F4 reveals every page of the open book.
- **Command line** (§7.11):
  - `--seed`; `--new` starts a game straight away, ignoring the save.
  - `--shots`: title; day 1 morning up and down; letters; the clerk's visit with a question, the catalogue and the book open after an Identify; the lend; the notebook; the day summary; day 2's newspaper, third visit with Read Thoughts, dusk farewell and desk; day 2's summary; the tray, the debug overlay, the pause menu and the settings; larger text; then days 3–7 fast-forwarded by the careful bot and the week summary.
  - `--autoplay <bot> [--days n]` drives a bot through the presentation layer: every command goes through the controller, which redraws every view. It runs one bell or one closing per frame, dismissing summaries as a player would, and exits 0 when the engine logged no errors (counted by a Godot `Logger`).
- **Core**: `ICommandSender`, which the session and the Godot controller both implement, so bots can send through either. A compact one-line description of each event for the overlay.

**Verified**
- All three bots exit 0 headless, each finishing the week with the same numbers as Core's balance tests on seed 1:
  - lend-all: money 30, 16 lent.
  - decline-all: money −21, 1 lent (the forced visit).
  - careful: money 20, 12 lent.
- An unknown bot logs an error and exits 1, so the error count works.
- `--clicktest` passes 23 checks. Days 1 and 2 are played by mouse. Then a visit starts on day 3, Esc leads back to the title, and Continue resumes day 3's morning with that visit gone. The test uses its own save file and deletes it.
- The full `--shots` set (22 images) passes §8.6, the night summaries and week summary included. The run exits 0.
- `dotnet test` is green (222 tests).

**Fixed along the way**: after a game's UI was torn down (back to the title, or a text-size rebuild), the old controller still listened to the camera, and the next pan reached a freed object.

**Differs from the brief**
- `--new` skips the title as well as ignoring the save. That's the reading that makes it useful from the command line.
- `--shots` and `--autoplay` never write saves, and `--clicktest` writes only its own test save. None of them touch a player's save or settings.

**Review pass 3** (a reviewer played the build; fixed in a separate commit):
- **Esc pauses.** The game's nodes stop while the pause menu or settings are open; the menus carry on. Space can't ring the bell behind the menu.
- **Note fields let go of the keyboard.** A click on bare wood, a pan, opening or closing a document, the bell, closing and Esc all take focus from a note, so the shortcuts work again.
- **The reading lamp** takes effect without waiting for the next change of phase. *(It still waited for the menus to close and then blended in; M6 made it immediate.)*
- **The mouse wheel** at the end of a scrolling document no longer pans the camera. *(This first fix didn't work; the final review caught it, and M6 fixed it.)*
- **Paper art slots** (`paper_card`, `paper_notebook`, `paper_page`, `newspaper`) are used: a painted texture when one arrives, else the layout's paper colour.
- **A Windows export preset** (`game/export_presets.cfg`, "Windows Desktop", JSON included) and `game/Curator.Godot.sln`, which Godot's export and editor build look for. A pack export works on Linux (all 39 content files and `layout.json` are in it). **A real Windows export is untested**: it needs Godot's export templates on the designer's PC.
- **The generic walk-out line** ("They leave without another word.") is narration, in italics without quotes, not the patron's speech.
- **A text-size change** keeps the camera, the open document, the book, the notebook page, shelved returns and the tray.
- **Disabled counter buttons** are readable: darker ink on a more opaque face.
- **The debug overlay** lists the newest events first and scrolls with the wheel.
- **The HUD** hides behind the day and week summaries, so it no longer shows tomorrow's numbers early.
- Smaller: a noted thought is also written into the patron's free notes, with its date; the catalogue summary is 18 px (the minimum); test runs use default settings, not the player's; the deflection "It's for something at home" became "Nothing worth the telling, really.", which fits any question.
- Matters of taste taken up: the text size now scales button captions, the question list included; a warded patron who notices Read Thoughts shows a tell (`genericLines.noticed`; M6 rewrote it without a face); patience and mana show one empty pip at 0 instead of nothing; N and C pan down to the desk before opening; the letters badge clears once the day's letters are read; `--days n` plays n whole days, evenings included (it stopped before day n's evening, so `--days 7` never reached the week summary); the HUD's visit summary shows only while looking down, as §7.6 has it.
- Left as they are, with questions: the speech panel's place (Q31) and real weekday names beside invented months (Q32). The `--shots` set still has no plain night view; night shows behind the summaries.
- After the fixes: `dotnet test` 226 green; the three bots unchanged (lend-all 30, decline-all −21, careful 20); `--clicktest` passes 33 checks, adding Esc-pauses-the-bell and the note-focus round trip; `--shots` 22 images, all looked at.

## Interim art — painted in Easel

**Built**
- Eight art slots painted stroke by stroke with Easel (`easel-paint` 0.8.0), as the designer asked: `beyond_doorway`, `doorway_wall`, `shelf_left`, `shelf_right`, `locked_shelf`, `counter`, `desk` and `patron_default`. Together they make the whole up view and the desk; only the candle is still drawn in code. *(M6 painted the candle too; only its flame is drawn in code.)*
- Each painting started from a written plan of values and a light, was checked against Easel's value readouts and checklist as it went, and is signed small in a shaded corner.
- The wall's arch is see-through and the patron's surround transparent. Both are cut out after painting, along the same outlines the paint was laid in (`tools/easel/cut_arch.py`, `cut_patron.py`).
- The scripts are in `tools/easel/`, one per painting, with a README. They're seeded, and each was re-run to confirm it writes the shipped PNG pixel for pixel.

**Not painted**: the candle and its flame; the paper textures (documents use the layout's paper colours); per-patron silhouettes (everyone uses `patron_default`).

**Verified**: the 22 `--shots` images with the paintings in place, all looked at; the click test and the three bots are unaffected. Slot lookup is unchanged, so a photographed painting in `game/art/painted/` still wins over its Easel stand-in, and deleting an Easel file brings back the placeholder (the M5 clean-checkout run had none of them).

**Differs from the brief**: `tools/` is a new folder. The brief's layout has no home for the paintings' sources.

## M6 — Hand-off

**Built**
- `docs/BALANCE_REPORT.md`: the final bot numbers over 10 seeds, with `balance.json` still at the brief's defaults. It says what the numbers mean and which levers would move them. One finding is new: no bot ever causes outright harm, only mixed outcomes (Q33).
- `docs/PLAYTEST_NOTES.md`: how to run it on Windows, the controls, what to look for, known issues and the open questions.
- **Nice-to-haves**, each its own commit:
  - **Ingredient log.** It's the notebook's first tab. It lists every ingredient on a page the curator has identified, as the page writes it, with the book, page and spell where it turned up. Ingredients are filed by name, ignoring a leading article. Core's `Projector.IngredientLog()` has tests.
  - **Parallax on the pan** (§7.2). The layers behind the counter travel a little less than the camera: 0.9, and 0.85 for the room beyond the arch. It's set per slot by `"parallax"` in `layout.json`. The up view is exactly as laid out.
  - **Warded patrons** were already in the rules, with Lysander warded. M5 added the tell.
- **The candle is painted** in Easel as well (`tools/easel/candle.py`). It keeps the placeholder's geometry, so the flame, still drawn in code, sits on its wick. `cut_patron.py` became `cut_outline.py`, which cuts out any painting from its outline; the patron re-runs pixel for pixel through it.
  - **Resistant books** are built and tested (`identifyResistance`, unidentifiable pages) but no book resists. Giving one resistance would make careful play miss even more (Q25).

**Final review** (a fresh agent with the docs and the repository). Fixed:
- **The transcript followed one step behind**, so the newest answer stayed hidden. It now follows the scroll range as new lines are measured.
- **The wheel still panned the camera at a document's end**: review pass 3's fix didn't work. A scroll area that has something to scroll now stops the wheel (Stop, not Pass); one whose content fits lets it through, so the wheel still pans over the room. The rule applies to every scroll area and text field as it's created, rebuilt pages included. Behind the title the camera doesn't pan at all.
- **Catalogue cards clipped their text** at Large and Larger. Cards now grow to fit.
- **A text-size change closed the pause menu.** It now comes back to it, and the menu rebuilds at the new size.
- **"No one at the counter."** was faint on the painted desk. It's near-opaque now, with a soft halo.
- **Export**: `build/` is ignored, and the fonts' licences ship with the export.
- **The noticed tell described a face** ("Their eyes narrow"), against the content guide. It's now "They go very still. They felt that."
- Matters of taste taken up:
  - The room beyond the arch no longer takes the archway light, which had blown it out.
  - The paintings' signatures are fainter, and the desk's knots lost their square-ended smears.
  - The notebook has the GDD's three sections: noted thoughts live in the free notes and aren't listed twice.
  - The impatience tell has its own line, so the row no longer overflows at Larger.
  - Esc closes Settings on the title and answers the close-early question with "Not yet".
  - Turning the reading lamp on or off applies at once.

**Verified**
- `dotnet test` is green (230 tests).
- The three bots are unchanged: lend-all 30, decline-all −21, careful 20.
- `--clicktest` passes 43 checks. New since M5: the wheel at the drawer's top stays put while the wheel over bare wood pans; the transcript runs past its panel with the newest answer in view; a text-size change keeps the pause menu; Esc answers the close-early question. With the wheel and transcript fixes removed, those checks fail.
- `--shots` gives 24 images, all looked at. The ingredient log and the catalogue at Larger are new.

**Still open**:
- The careful-bot guardrail (Q25).
- No outright harm (Q33).
- The speech panel's place (Q31).
- The shot set has no plain night view; night shows behind the summaries.
- Windows is untested (Q5).

## After M6 — Beta builds

**Built**
- **A Windows beta zip from CI.** The Curator's workflow gains two jobs after build and test:
  - `windows-build` installs Godot 4.6.3 .NET and its Windows export templates, checked against pinned SHA-512 hashes and cached. It runs `tools/packaging/build-windows.sh` and keeps the build as a workflow artifact, so a pull request's build can be tried before merging. The artifact is the unzipped folder, which the release job zips: GitHub zips every artifact for download, so the first version, which uploaded the zip itself, handed out a zip inside a zip.
  - `beta-release` runs on pushes to main, or a manual run on main. It publishes the zip as a pre-release, "The Curator 0.1.N-beta" (tag `the-curator-v0.1.N-beta`, N being the workflow's run number). Only this job may write to the repository.
- **`tools/packaging/build-windows.sh <version>`** exports the Windows build and packs it, the same in CI and locally:
  - It stamps the version into the game, for the title screen, and into the exe's file properties. Both files are put back afterwards.
  - The zip holds `TheCurator.exe`, its pack, the bundled .NET runtime, a playtester's README and the licences for Godot (read from the engine itself) and every font.
  - The folder isn't called `release/` because the repository's root `.gitignore` ignores every folder by that name.
- **The title screen shows the version** of a release build. Development runs show none.

**Verified**
- The Windows export builds from a clean checkout: 9 seconds, with `TheCurator.exe`, a 3.8 MB pack and the .NET runtime. The exe carries the stamped file and product version.
- The script's zip is 77 MB, and the stamped files come back unchanged.
- A Linux export through the same pipeline played the whole week headless: careful and lend-all, with the same numbers as the editor. It also rendered the screenshot set, fonts and paintings included. That shows the exported pack, content and .NET runtime work outside the editor.

**Not verified**: the Windows build running on Windows (Q5).

## After M6 — Morning paper, spells tried, kept books

From the designer's playtest: the paper should open by itself each morning, and Jory was lent the sleep book for his exam, brought it back, then asked for a sleep book.

**Built**
- **The morning paper opens by itself** (Q34). Once each morning, before the first patron, the camera looks down and the paper lies open on the desk. Putting it away keeps it away for the day; a text-size rebuild doesn't reopen it (`DeskState.PaperDay`). Presentation only: `GameController.OpenMorningPaper`.
- **Spells tried** (Q35). At each lend the patron tries one spell drawn among the pages that meet their goal, plus one they'd misuse or botch if the book holds one. A book with neither gets one page drawn from it all. The draw is seeded and stored in a new event, `SpellsTried`, so replays roll no dice. The outcome category still follows the §5.11 table, so every written mixed outcome is reached as before.
- **`triedTagsAny`**, a new `when` field: holds when a spell the patron tried from their last lend carries one of the tags. Validated against the effect tags and described in the content report.
- **Kept books** (Q35). On the morning a loan is due, if the patron's next visit wants something the book already holds, they keep it (`LoanKept`). That visit is skipped and counts as lent the book: spells tried, outcome resolved (overrides included), the `lent` line's flags set, a new due date. For Jory, the sleep book lent for his exam now stays with him; his sleep visit is skipped and the book's Dreamless page has him sleep through the exam (the existing override), which leads to his "slept" results visit on day 7.
- **Jory's content** follows (Q37): the brittle visit greets by what he tried or by an unhelpful loan, and his step-1 good outcome no longer says he brought the book back.

**Differs from the brief**: §5.11 gains the spells tried and kept books, and §5.12 gains `triedTagsAny`. The brief and the content guide weren't edited (Q35, Q37).

**Verified**
- `dotnet test` is green (239 tests). New: `SpellsTriedTests` covers the draw rules, the spells recorded at a lend, the reported Jory case (kept, brittle visit skipped, `slept-through` set, no return) and a book that doesn't cover the next visit coming back. `ConditionTests` covers `triedTagsAny`.
- `--clicktest` passes 45 checks, two new: the paper is open on day 1 and day 2.
- The three bots play the week with no errors. The careful bot hits the reported case itself: on day 2 it lends Jory the sleep book. Careful's money falls to 18.9 on average (10 to 26, was 17 to 28), because Jory's day-4 visit and its fee are skipped and the one-off visitors shift. It now causes one harm in 4 seeds of 10 (`docs/BALANCE_REPORT.md`, Q33). Lend-all and decline-all are unchanged.
- `--shots`: 24 images; the day-1 desk now shows the paper open.

**Open**: a kept book gives no sign on the desk (Q36).
