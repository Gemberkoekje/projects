# The Curator — Build Brief

For Claude Code. Read `CLAUDE.md` first, then this, then `GDD.md` for intent. Read `CONTENT_GUIDE.md` before writing or validating content.

## 1. Goal

Build the prototype in GDD §13: a playable first week, Windows desktop, Godot 4 with C#.

Done means:
1. The designer can play seven days, title screen to week summary, without errors.
2. The core tension is measurable: strategy bots show that lending everything, refusing everything and investigating carefully lead to clearly different results (§8.4).
3. Content lives in JSON. Changing a book, a patron or a number needs no code change.
4. A photographed painting dropped into `game/art/painted/` replaces its placeholder with no code change.

## 2. Stack

| | |
|---|---|
| Engine | Godot 4, .NET build — the version installed on the designer's PC (4.6 or later). Record the exact version in `CLAUDE.md` during M0. |
| Language | C# throughout. |
| Core | `Curator.Core`: a plain class library with no Godot reference, enforced by project references. Same `TargetFramework` as the Godot project; never raise it independently. |
| Tests | xUnit via `dotnet test`. No commercially licensed packages (e.g. FluentAssertions 8+). |
| Content | JSON, camelCase, `System.Text.Json`, strict: unknown properties are errors. |
| Platform | Windows desktop. Godot 4 had no C# web export as of mid-2026; don't plan on one. |

## 3. Repository layout

```
/
├─ CLAUDE.md
├─ TheCurator.sln
├─ docs/
│  ├─ GDD.md  BUILD_BRIEF.md  CONTENT_GUIDE.md
│  ├─ QUESTIONS.md          ← Claude Code creates: open design questions + what it chose
│  └─ PROGRESS.md           ← Claude Code creates: milestone log
├─ src/
│  ├─ Curator.Core/
│  │  ├─ Content/           ← records mirroring the JSON, loader, validation
│  │  ├─ Commands/  Events/
│  │  ├─ Rules/             ← mana, patience, trust, money, identify, outcomes, conditions, scheduling
│  │  ├─ Projections/       ← card, notebook, catalogue, book, visit, HUD, summaries, debug
│  │  ├─ Text/              ← page markup parser
│  │  └─ Game/              ← GameSession, PRNG
│  └─ Curator.Core.Tests/
├─ game/                    ← Godot project root: project.godot, Curator.Godot.csproj (references Core)
│  ├─ content/              ← JSON content; partly seeded (CONTENT_GUIDE §1)
│  ├─ art/
│  │  ├─ layout.json        ← art slots
│  │  └─ painted/           ← the designer's photographed paintings; never edited by Claude Code
│  ├─ fonts/                ← bundled fonts + licence files
│  ├─ scenes/
│  └─ scripts/              ← C# presentation code only
└─ out/                     ← gitignored: screenshots, reports, logs
```

## 4. Architecture

### 4.1 Principles
- **Core owns every rule and all state.** Godot renders projections and sends commands. No game rule in a Godot script; no `Godot` namespace in Core.
- **The event log is the truth.** Every state change is an appended event; state is a fold over events; every view is a projection. This is what later makes the theft scribbles (a projection of the true history) and forged cards (a different projection of the card) cheap.
- **Commands are validated, then produce events.** `GameSession.Handle(command)` returns events or a rejection with a reason (`NotEnoughMana`, `BookNotOnShelf`, …). Godot shows rejections as gentle in-world lines, never as errors.
- **Deterministic.** Same seed and same commands, same events. Randomness happens only while handling a command, and its result is stored in the event (which page Identify landed on), so replaying events never rolls dice.

### 4.2 Commands

| Command | Valid when | Notes |
|---|---|---|
| `NewGame(seed)` | at start | loads content, emits `GameStarted`, starts day 1 |
| `RingBell` | between visits, slots left today | starts the next slot (§5.13) |
| `AskQuestion(questionId)` | in a visit | standard or unlocked specific; each once per visit |
| `ReadThoughts` | in a visit, not yet cast this visit | |
| `Identify(bookId)` | book in the library, not locked | any time of day; patience cost only during a visit |
| `LendRequested` | in a visit, requested book on the shelf | |
| `OfferBook(bookId)` | in a visit, book on the shelf, not the requested one | accepted → lend; refused → patience cost |
| `Decline` | in a visit that isn't forced | |
| `NoteLoan(loanId)` | in the visit where the loan was made | writes a ledger entry |
| `NoteReturn(loanId)` | the day that book came back | fills in the return date |
| `NoteThought` | in a visit, after Read Thoughts landed | copies the fragment, dated, into free notes |
| `CloseForTheDay` | between visits | early closing allowed; unused slots are lost |
| `Debug…` | debug builds only | `DebugAddMana`, `DebugRevealBook`, `DebugEndDay`; logged |

Free-text edits (notes, the ledger note column) aren't commands; the save stores them beside the log (§7.12).

### 4.3 Events — minimum set
`GameStarted`, `DayStarted`, `BookReturned`, `PageRemoved`, `OutcomeSurfaced`, `NewspaperDelivered`, `LetterDelivered`, `ManaGranted`, `VisitStarted`, `GossipShared`, `QuestionAsked`, `ThoughtsRead`, `PageIdentified(pageId, repeat)`, `PageResisted`, `IdentifyFailed`, `ManaSpent`, `PatienceSpent`, `TrustChanged`, `OfferRefused`, `BookLent(loanId, bookId, fee, asAlternative)`, `VisitDeclined`, `PatronWalkedOut`, `OutcomeResolved(category, cause, outcomeKey, channel, surfaceDay)`, `FlagSet`, `ReputationChanged`, `MoneyChanged`, `LedgerEntryNoted`, `ThoughtNoted`, `PhaseAdvanced`, `UpkeepPaid`, `DayEnded`, `WeekEnded`, `DebugCheatUsed`.

Add events as needed; never fold several facts into one.

### 4.4 Randomness
A small PRNG in Core (e.g. SplitMix64); don't rely on `System.Random`. Derive each draw from `(gameSeed, eventSequenceNumber)` so draws don't depend on call order elsewhere.

### 4.5 Content loading
`IContentSource` abstracts file access: tests read from disk, Godot reads `res://content` with `FileAccess`. Loading validates everything (§8.3) and reports problems with file path and JSON path. Content is immutable at runtime.

### 4.6 Projections Godot needs
`LibraryCardView(patronId)`, `NotebookView(patronId)` (header, ledger, note slots), `CatalogueView`, `BookView(bookId)` (identified pages as parsed spans, unread count, resisted and removed pages, status), `VisitView` (greeting, request, questions with unlock state, last answer, fragment, patience, allowed decisions, pending "note it" moments), `HudView` (mana, money, day, date, phase), `MorningView` (newspaper, letters, returns), `DaySummary`, `WeekSummary`, `DebugView` (hidden state).

## 5. Rules

Numbers here are defaults from `game/content/balance.json`; code reads them from there.

### 5.1 Day flow
`Morning → (BetweenVisits ⇄ InVisit) → Evening → next Morning`. After day 7's evening comes `WeekEnded`.

**Morning, in order:**
1. `DayStarted`.
2. Loans due today return (`BookReturned`), applying any `onReturn` effect (`PageRemoved`).
3. Outcomes due today on the `newspaper` or `letter` channel surface and apply their effects (§5.11).
4. The newspaper (outcome headlines, the day's flavour items, one line toned by reputation band) and letters (tutorial notes, Board debt letters, outcome letters).
5. Mana is granted (§5.2).

**Visit:** `RingBell` resolves the next slot (§5.13). Due gossip is told first, then any `return` outcome, then the greeting and request. A decision ends the visit with the patron's exit line, and the phase advances (morning → midday → afternoon → dusk).

**Evening:** upkeep (§5.3), tomorrow's attentiveness bonus is fixed, `DayEnded`, phase night, autosave.

### 5.2 Mana
- Morning grant = `basePerDay` + bonus + rollover.
- Rollover = min(mana unspent at the end of yesterday, `rolloverCap`).
- Bonus = min(attentive + good, `totalBonusCap`), where
  - attentive = min(yesterday's visits in which a question, Read Thoughts or Identify happened before the decision, `attentiveBonusCap`);
  - good = good outcomes surfaced since the previous grant × `goodOutcomeBonus`.
- Day 1 ignores all of that: the grant is the schedule's `startMana` (1).
- Read Thoughts costs `readThoughtsCost`, Identify `identifyCost`. Mana never goes below zero; a cast you can't afford is rejected.

### 5.3 Money
- Start with `money.start`. A lend adds the book's `fee`, or `feeByRarity` if it has none.
- Every evening pay `upkeepPerDay`. Money may go negative.
- A morning after an evening that ended in debt brings a Board letter: level 1 the first time, level 2 the second consecutive time, level 3 after that. Ending a day out of debt resets the count.

### 5.4 Patience
- A visit starts with its `patience`, else `patience.default`.
- Costs: question `questionCost` (a question can override it), Identify during a visit `identifyCost`, refused offer `refusedOfferCost`, Read Thoughts `readThoughtsCost` (0).
- At zero, a patience-costing action costs trust instead (`trust.pastPatience`). If trust is already 0, the action doesn't happen: the patron walks out (`PatronWalkedOut`), with the visit's `walkedOut` line and outcome.
- At patience ≤ 1 the visit shows a tell ("She glances at the door.").

### 5.5 Trust
- Integer 0–3 (Guarded, Cautious, Candid, Confiding), clamped.
- Starts at the patron's `startTrust`, default `strangerStart`. One-off visitors start at `warmStrangerTrust` instead if reputation ≥ `warmThreshold` and that's higher.
- Defaults, each overridable per visit decision: alternative accepted `alternativeAccepted` (only when a specific book was requested and a different one was taken); decline `declined`; patience overrun `pastPatience`; a warded patron noticing Read Thoughts `wardedNoticed`. Questions and surfaced outcomes can add their own deltas.

### 5.6 Reputation (hidden)
Starts at `reputation.start`. An outcome surfacing on a public channel (newspaper, gossip) adds `goodPublic` if good, `harmPublic` if harm or mixed, unless the outcome sets its own `reputation`. A decline in a visit with no question, Read Thoughts or Identify: `unexplainedDecline`. A walk-out: `walkedOut`. Bands: ≤ `waryThreshold` wary, ≥ `warmThreshold` warm, else neutral. Effects in the prototype: the newspaper's tone line and one-off visitors' starting trust.

### 5.7 Identify
1. The book must be in the library and not locked. Pay mana; pay patience if in a visit.
2. If the book has `identifyResistance` r > 0, roll; on failure `IdentifyFailed` ("The letters slide away from you.").
3. Pick a page uniformly from the book's pages, removed pages excluded.
4. An `unidentifiable` page gives `PageResisted`: the player now knows there's a page that won't come into focus.
5. Otherwise `PageIdentified(pageId, repeat)`; a repeat teaches nothing new.

### 5.8 Read Thoughts
Once per visit; pay mana, no patience. The fragment is the visit's `readThoughts.byTrust` entry for current trust, else the nearest lower entry, else `default`. It unlocks the listed questions. Warded patrons give no fragment: trust changes by `wardedNoticed` and the flag `<patronId>:noticed` is set.

### 5.9 Questions
- Standard questions (`questions.json`) are always available; for requests without a `bookId`, use their `textTopic` wording.
- Specific questions appear once unlocked by `readThoughts` (this visit), `identifiedPage:<pageId>` (ever), `cardBook:<bookId>` (this patron borrowed it before) or `flag:<flag>`.
- Each question once per visit. The answer is the visit's entry for current trust, else the nearest lower entry, else a deflection for current trust from `questions.json`.
- Asking can change trust and set flags.

### 5.10 Offers and lending
- `LendRequested` needs the requested book on the shelf.
- `OfferBook` is accepted if the book's spine tag equals the request's `topic` and `acceptSameTopic` holds, or trust ≥ `minTrustForAny`; a visit's `alternatives` overrides both. Refused: `OfferRefused`, patience cost, the visit's `offerRefused` line.
- If the requested book is out, the patron is told so; `LendRequested` is unavailable and offers follow the same rule.
- For a request without a `bookId`, an accepted offer counts as the decision `lent` (no alternative bonus).
- A lend: fee, `BookLent` with a new loan id, due back after the visit's `loanDays` (else `loans.defaultDays`) unless the outcome changes that, then outcome resolution (§5.11).
- `Decline` isn't allowed in forced visits.

### 5.11 Outcomes
At lending:
```
pages     = the lent book's pages, removed pages excluded
satisfied = any page whose tags meet goal.tags with power ≥ goal.minPower
risky     = pages whose tags match a temptation with use misuse or accident
cause     = misuse if any risky match is misuse, else accident
category  = risky ? (satisfied ? mixed : harm) : (satisfied ? good : unhelpful)
outcome   = visit.overrides[bookId]                 (sets its own category)
         ?? visit.outcomes[category + Cause]        (harmMisuse, mixedAccident, …)
         ?? visit.outcomes[category]
         ?? outcomes_generic.json[category][cause]
```
`OutcomeResolved` records category, cause, the outcome's key, channel and surface day (today + `delayDays`, else the default for its category). The outcome's `setFlags` apply now, so later visits can branch on what really happened before the player hears of it. Decline and walk-out resolve to the visit's `declined` and `walkedOut` outcomes (default: silent).

Surfacing:
- `newspaper`, `letter`: the morning of the surface day.
- `gossip`: told at the start of the first visit on or after the surface day, by whoever comes ("Did you hear…").
- `return`: opens this patron's next visit on or after the surface day. If their next scheduled slot passes without a visit from them, or they have no slot left, it arrives as a letter the following morning — or appears in the week summary if the week is over.
- `none`: never shown. The week summary counts it under "never heard".

When an outcome surfaces, apply its `trust`, its `reputation` (defaults per §5.6 on public channels), and count good outcomes for tomorrow's mana. `bookReturns: false` keeps the book away; `returnInDays` replaces the due date.

### 5.12 Conditions
Visits and greeting variants can carry `when`; every field present must hold. `minTrust`, `maxTrust`, `flagsAll`, `flagsNone`, `previousDecisionIn` (`lent`, `alternative`, `declined`, `walkedOut` — this patron's last decision), `previousOutcomeIn` (`good`, `unhelpful`, `harm`, `mixed` — the outcome of this patron's last lend), `hasBorrowed` (a book id). Flags are global strings named `<patronId>:<flag>`.

### 5.13 Scheduling
`schedule.json` lists each day's slots in order. When a slot comes up:
- **A patron id:** take that patron's lowest `step` that has no used visit. Among that step's visits, in file order, use the first whose `earliestDay` ≤ today, whose `minDaysAfterPrevious` has passed since their last visit, and whose `when` holds. If none qualifies, or all steps are used, the slot goes to the next unused one-off visitor.
- **`"filler"`:** the next unused one-off visitor, by `fillerOrder`.
- Nobody left: the slot is skipped.

A step with no qualifying variant stays open; a patron who walked out typically never qualifies again, which is intended.

### 5.14 Tutorial
Day 1: `startMana` 1 and one slot, the Board's clerk. The visit is `forced`. The tutorial book's last page is `unidentifiable`; its outcome brings the book back on day 6 with that page removed. Tutorial notes arrive as letters on days 1–3.

### 5.15 Week end
After day 7's evening, `WeekEnded` and the week summary: money, loans and declines, outcomes heard and never heard, each story patron's trust, the tutorial book's state.

## 6. Balance defaults

A mirror of `game/content/balance.json` at hand-off. The JSON is authoritative; update this table only when a change sticks.

| Key | Default |
|---|---|
| `mana.basePerDay` | 6 |
| `mana.readThoughtsCost`, `identifyCost` | 2, 1 |
| `mana.rolloverCap` | 2 |
| `mana.attentiveBonusCap`, `goodOutcomeBonus`, `totalBonusCap` | 2, 1, 3 |
| `money.start`, `upkeepPerDay` | 10, 5 |
| `money.feeByRarity` | common 2, uncommon 4, rare 7, singular 12 |
| `patience.default` | 4 |
| `patience.questionCost`, `identifyCost`, `refusedOfferCost`, `readThoughtsCost` | 1, 1, 1, 0 |
| `trust.strangerStart`, `alternativeAccepted`, `declined`, `pastPatience`, `wardedNoticed` | 0, +1, −1, −1, −2 |
| `reputation.start`, `goodPublic`, `harmPublic`, `unexplainedDecline`, `walkedOut` | 0, +1, −2, −1, −1 |
| `reputation.warmThreshold`, `waryThreshold`, `warmStrangerTrust` | 3, −3, 1 |
| `alternatives.acceptSameTopic`, `minTrustForAny` | true, 2 |
| `loans.defaultDays` | 5 |
| `outcomes.defaultDelayDays` | good 2, unhelpful 2, harm 1, mixed 2, declined 0, walkedOut 0 |
| `week.days` | 7 |

## 7. Presentation (Godot)

### 7.1 Scene tree
```
Main (Node)
├─ World (Node2D)          ← the tall painted canvas
│  ├─ Layers               ← one node per art slot, built from art/layout.json
│  ├─ Patron               ← silhouette slot + archway backlight
│  ├─ Desk                 ← card, notebook, catalogue drawer, open book, newspaper, letters, returns tray
│  ├─ Lights               ← CanvasModulate, candle, archway, window
│  └─ Camera2D
├─ Ui (CanvasLayer)        ← HUD, speech + question list, pan tabs, summaries, menus
└─ Debug (CanvasLayer)     ← F1 overlay
```
A `GameController` node owns the `GameSession`, sends commands, and pushes fresh projections to the views after each one. Views never read each other.

### 7.2 Canvas and camera
- Window 1920×1080, stretch mode `canvas_items`, aspect `keep`.
- World canvas 1920×1800. The **up** view shows y 0–1080, the **down** view y 720–1800; the counter band (y 720–1080) is in both.
- Pan: tween of about 0.45 s, sine ease in and out. Inputs: mouse wheel, W/S, ↑/↓, a small tab at the top or bottom edge. A patron arriving pans up; nothing else moves the camera by itself.
- Nice to have: slight parallax during the pan (far layers ~0.9×).

### 7.3 Art slots
`game/art/layout.json` lists the slots: id, rect in canvas coordinates, z, light mask, placeholder style. At startup each slot uses `res://art/painted/<id>.png` if it exists (scaled to fit the rect), otherwise a procedural placeholder — flat shapes and colours, no generated image files. Newly dropped paintings need one opening of the editor so Godot imports them.

| Slot | Rect x, y, w, h | z | Notes |
|---|---|---|---|
| `beyond_doorway` | 680, 120, 560, 600 | −40 | Seen through the arch: grey stone, window light from the left |
| `doorway_wall` | 0, 0, 1920, 720 | −30 | Painting needs a transparent arch |
| `patron_default`, `patron_<id>` | 760, 240, 400, 620 | −20 | Hooded silhouette with transparency; the counter hides the lower body |
| `shelf_left`, `shelf_right` | 0, 0, 640, 720 · 1280, 0, 640, 720 | −10 | Oak, mostly covered by books |
| `locked_shelf` | 1500, 260, 300, 460 | −9 | Iron-barred cabinet; optional painting |
| `counter` | 0, 720, 1920, 360 | 0 | Dark oak |
| `desk` | 0, 1080, 1920, 720 | 10 | Lighter oak |
| `candle` | 1700, 1180, 120, 300 | 20 | Body; flame frames `candle_flame_0`–`3` |
| `book_cover_<bookId>` | — | — | Open-book view; fallback cover tinted by spine tag |
| `paper_card`, `paper_notebook`, `paper_page`, `newspaper` | — | — | Optional paper textures; fallback parchment colours |

The rects are starting points: tune them in `layout.json`, not in code. The designer crops photos to roughly each slot's proportions.

### 7.4 Lighting
- `CanvasModulate` sets the ambient tint for each phase.
- **Candle:** `PointLight2D`, warm amber, covering the desk. Flicker = base energy + `FastNoiseLite` sampled over time (amplitude about 0.1, a few hertz) plus ±1 px of positional jitter — noise, not per-frame random, so it breathes rather than strobes. The flame sprite has 4 frames on slightly irregular timing.
- **Archway:** warm light behind the patron so the silhouette reads against it.
- **Window:** a large soft light from the left across the far layers: warm white by day, low orange at dusk, cool pale blue at night.
- **Phases:** morning, midday, afternoon, dusk, night; each finished visit advances one, the evening is night. Blend tints over about 1.5 s.
- **Legibility:** documents keep readable contrast in every phase. A "reading lamp" setting brightens documents. Check night screenshots specifically.
- Out of scope: normal maps, occluders, shadows.

### 7.5 Desk views
- **Library card:** name, description, first visit, loans table. During a visit, "Stamp and lend" and "Hand back the card" sit beside it.
- **Notebook:** a page per patron, opening on the current patron. Header (automatic), watched loans (rows with an editable note column), free notes (`TextEdit` in the handwriting font). "Note it" buttons appear only at their moments: on the lend confirmation, on a returned book in the tray, under a fresh fragment.
- **Catalogue drawer:** scrollable index cards with status, fee, pages read ("2 of 7"), spine tag. Clicking one opens the book.
- **Open book:** cover; identified pages in partial translation; unread count; a "won't come into focus" note for resisted pages and a torn stub for removed ones; buttons Identify (showing its cost), Offer this instead, and Stamp and lend (requested book only).
- **Newspaper, letters, returns tray:** on the desk each morning; click to read; returned books go back on the shelf with a click.

### 7.6 Patron and speech (up view)
The silhouette in the archway; speech in a panel just above the counter; the question list beneath it (standard questions, then unlocked ones marked new); patience pips; the Read Thoughts fragment in italics with a soft glow. While looking down, the HUD repeats patience and a one-line request summary, so the player never loses track of who is waiting.

### 7.7 HUD
Top left: mana pips and money. Top centre: day, date and phase. During a visit: patience and the request summary. Everything else is on the desk.

### 7.8 Text and fonts
- Core parses page markup into spans: `Translated`, `Untranslated` (`{?…?}`), `Illegible` (`{~…~}`). Godot renders them as BBCode in a `RichTextLabel`: ink-coloured print serif, grey italic script, faded scribble.
- Bundle fonts with their licence files in `game/fonts/`. Candidates — verify the OFL licence and availability first: IM Fell English (print), Caveat (handwriting), Pinyon Script (untranslated), Redacted Script (illegible).
- Body text at least 18 px at 1080p; notebook 22 px. A text-size setting scales all document text.

### 7.9 Input
Mouse first. Keys: W/S or ↑/↓ to pan, Space for the bell, N notebook, C catalogue, Esc menu. No drag and drop in the prototype.

### 7.10 Debug overlay (F1, debug builds)
The current visit's hidden state (goal, temptations, trust, patience), reputation, pending outcomes with surface days and channels, flags, seed, the last 20 events. Cheats: F2 +5 mana, F3 end the day, F4 reveal every page of the open book. Cheats go through `Debug…` commands and are logged.

### 7.11 Command-line modes
User arguments after `--`:
- `--seed <n>`, `--new` (ignore the save).
- `--shots <dir>`: play a scripted sequence, save PNGs, quit. Title; day 1 morning, up and down; the clerk's visit with the book open after an Identify; the notebook; a day-2 visit at dusk; a day summary; the week summary (fast-forwarded). Needs a renderer: run windowed, not headless.
- `--autoplay <lend-all|decline-all|careful> [--days n]`: drive a strategy bot (§8.4) through the presentation layer at speed; exit code 0 if no errors were logged.

### 7.12 Menus, settings, saving
- Title (New game, Continue, Settings, Quit); Esc pauses. Settings: fullscreen, text size, reading lamp.
- Autosave each evening to `user://save.json`: schema version, seed, content version, events, free notes by patron, ledger notes by entry. Continue resumes the next morning; quitting mid-day loses that day.

### 7.13 Export
Windows preset. Add `*.json` to the export's non-resource include filter, or the content won't ship.

## 8. Verification

### 8.1 Core unit tests
Every rule in §5 has tests, edges included: mana never negative; patience overrun becomes trust loss; walk-out at trust 0; Identify on a lent book rejected; resisted, removed and repeat pages; condition evaluation; scheduling steps and fallbacks; outcome lookup order; every surfacing channel including the `return` fallback; upkeep and the three debt letters; the day-1 mana override.

### 8.2 Replay test
Play a week with a bot, rebuild the state from the event log alone, and compare: every projection must be identical.

### 8.3 Content validation
Runs on load and as tests:
- Strict JSON parsing; unique ids; file names match ids.
- References resolve: books, pages, ingredients, patrons, question ids, unlock targets, schedule slots, `hasBorrowed`, `onReturn.removePage`.
- Effect tags and spine tags exist in `tags.json`; every `request.topic` is a spine tag.
- Markup is balanced, with no empty spans.
- Every book has 3–10 pages and at least one danger-0 page; at least four books have an outlier of danger ≥ 2.
- Every story visit defines `good`, `unhelpful` and `harm` outcomes (or a cause-specific variant), and `outcomes_generic.json` covers every category and cause.
- Every patron visit has a `step`; story patrons' `earliestDay`s fit the schedule.
- Flags used in `when` are set somewhere (warning).
- Every story visit is reachable by some sequence of decisions within the seven days (warning if not).
- Files written by Claude Code have `"placeholder": true`.

### 8.4 Strategy bots and balance guardrails
Bots play the real content for seven days through commands only:
- **lend-all** — lends every request; never investigates.
- **decline-all** — declines everything not forced.
- **careful** — asks the purpose question; casts Read Thoughts when mana allows; identifies the requested book up to twice; if it saw a page of danger ≥ 2, offers the best same-topic book with no danger seen; otherwise lends.

Guardrails, as tests (tune `balance.json` until they pass, and record changes in `PROGRESS.md`):
- money: lend-all > careful > decline-all, and decline-all ends in debt;
- harm and mixed outcomes: lend-all ≥ 3, careful ≤ 1;
- total bonus mana: careful > lend-all.

Write `out/balance-report.md`: each bot's money, mana, outcomes by category, and every story patron's final trust.

### 8.5 Godot smoke test
`dotnet build`, then run each `--autoplay` bot headless; all exit 0.

### 8.6 Screenshot review
After any change to what's on screen, run `--shots` and look at every image: text legible (at night too), nothing overlapping or clipped, the silhouette readable against the archway, the candlelight visibly warm, both views framed as in §7.2.

### 8.7 Review passes
After M1, M2 and M5, start a fresh review agent with only the docs and the repository — not your reasoning. Ask for deviations from the GDD and this brief, rule bugs, and content that breaks the content guide's voice and style rules. Fix what's clear; put design questions in `docs/QUESTIONS.md`.

## 9. Milestones

Each milestone ends with its acceptance checks passing, `PROGRESS.md` updated, and open questions logged.

**M0 — Setup.** Solution with Core, Core.Tests and the Godot project (made with the installed Godot .NET version, referencing Core); `.gitignore`. *Accept:* `dotnet build` and `dotnet test` pass; the Godot project runs an empty main scene; `CLAUDE.md` holds the verified commands, the Godot path and both versions.

**M1 — Core rules.** Commands, events, rules (§5), projections (§4.6), PRNG, content loading and validation. *Accept:* unit and replay tests pass on a small test-content set inside the test project; the bots run end to end, guardrails report-only for now; review pass 1 done.

**M2 — Content.** Complete `game/content/` per `CONTENT_GUIDE.md`: gold files untouched, everything else written as placeholders. *Accept:* validation (§8.3) passes; guardrails (§8.4) pass on the real content; `out/content-report.md` lists every patron's visits, branches, unlocks and outcomes for the designer to read; review pass 2 done.

**M3 — Scene, camera, light.** Slots from `layout.json`, procedural placeholders, the painted override (test with a dummy PNG, then delete it), panning, candle flicker, archway and window light, phases. *Accept:* `--shots` gives up and down views at morning and at night that pass §8.6.

**M4 — Desk and speech.** Every view in §7.5–7.8 wired to projections and commands, plus the day summary. *Accept:* a whole day can be played by mouse; screenshots pass §8.6; no rule logic outside Core.

**M5 — Playable week.** Title, tutorial day, seven days, week summary, save and continue, debug overlay, command-line modes. *Accept:* all three `--autoplay` bots exit 0; a full `--shots` set passes §8.6; review pass 3 done.

**M6 — Hand-off.** Fix or log review findings; final balance report; `docs/PLAYTEST_NOTES.md` for the designer — how to run it, what to look for, known issues, open questions. Nice-to-haves (parallax, warded patron, ingredient log) only once everything else is done.

## 10. Working agreements
- Never stall on a design question: take the most reasonable reading, record it in `docs/QUESTIONS.md` with what you chose, carry on.
- The GDD is the designer's. Propose changes in `QUESTIONS.md`; don't edit `GDD.md`.
- Don't edit `game/art/painted/`, files with `"placeholder": false`, or the gold-standard content (`CONTENT_GUIDE.md` §1) — except to fix a schema error, which you log.
- Numbers live in `balance.json`; story text lives in content. Neither belongs in code.
- Keep `PROGRESS.md` short: per milestone, what was built, what differs from this brief and why, what's open.
