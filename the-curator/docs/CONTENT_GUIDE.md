# The Curator — Content Guide

How to write the game's content — books, spells, ingredients, patrons, the schedule, newspaper and letters — for Claude Code and the designer alike. Rules that use this content are in `BUILD_BRIEF.md` §5.

## 1. Files and ownership

```
game/content/
├─ balance.json          numbers (BUILD_BRIEF §6)                         seeded
├─ tags.json             spine tags and effect tags                        seeded
├─ questions.json        standard questions, deflections, generic lines   seeded
├─ ingredients.json      every ingredient, known or not                    seeded; extend
├─ schedule.json         calendar and seven days of slots                  seeded
├─ books/<id>.json       one book per file                                 3 seeded, 12 to write
├─ patrons/<id>.json     one patron per file, all their visits             2 seeded, 13 to write
├─ outcomes_generic.json fallback outcomes by category and cause          to write
├─ newspaper.json        masthead, daily flavour, reputation tone lines   to write
└─ letters.json          tutorial notes, Board debt letters               to write
```

**Gold standards:** `books/common-wards.json`, `books/household-arts-3.json`, `books/hearth-and-kettle.json`, `patrons/board-clerk.json`, `patrons/elara-voss.json`. They set the expected depth and voice. Don't change them except to fix a schema error, and log that in `PROGRESS.md`. Their story hooks are suggestions the designer will keep or rewrite.

**Placeholders:** every content file carries `"placeholder"`. Claude Code writes `true`; the designer sets `false` once a file is rewritten or approved, and after that Claude Code leaves it alone unless asked. The gold standards and seeded files are `true` too — they were written in design chat, not by the designer.

**Conventions:** ids are kebab-case and match the file name. Page ids are `<bookId>-<n>`. Visit ids are `<patronId>-<step>`, with a suffix for variants (`elara-voss-2-candid`). Flags are `<patronId>:<flag>`. Days are integers: day 1 is the first day of play; history before the game uses 0 or negative days.

## 2. Page markup

Plain text is **translated** — the curator understands it. Two markers:

| Markup | Meaning | Rendered as | Use for |
|---|---|---|---|
| `{?Varre?}` | untranslated | grey cursive, letters readable | invented words, names, terms of art |
| `{~keeps what it touches~}` | illegible | faded scribble | passages the curator can't read |

Rules:
- Ordinary English words are never untranslated; they're either translated or illegible.
- A third to a half of a page's prose is illegible. What stays legible is chosen: enough to hint, never enough to be sure.
- An outlier's danger shows, if at all, as a legible fragment (*upon anything that still breathes*) or a telling ingredient — never as a plain statement.
- Spell names can be partly illegible.
- Ingredients sit in their own list (§4) and render by the ingredient, not by markup.

## 3. Tags

`tags.json` holds the vocabulary.

- **Spine tags** are a book's topic, and also a request's topic: domestic, warding, botanical, memory, fire, illusion, weather, preservation, healing.
- **Effect tags** say what a spell does, and drive outcomes: a patron's goal and temptations are effect tags. `unknown` is reserved for story pages and never appears in goals or temptations.

Add a tag only when nothing existing fits, with a one-line `about`.

## 4. Ingredients

`ingredients.json` entries have `id`, `name` and `known`. Known ingredients render as translated ink (*Eye of Newt*), unknown ones as untranslated cursive (*Hufeykrey*) — always, everywhere.

Two to five ingredients per spell, usually at least one unknown. Reuse ingredients across books on purpose. Overlap creates patterns players learn and ambiguities they have to resolve: eye of newt is in both the harmless stain-lifter and the dangerous fixative in *Household Arts*, so a fragment about eye of newt narrows things down without settling them.

## 5. Books

| Field | | Notes |
|---|---|---|
| `id`, `title` | string | |
| `spineTag` | spine tag | |
| `rarity` | `common`, `uncommon`, `rare`, `singular` | sets the fee |
| `fee` | int, optional | overrides the rarity fee |
| `summary` | string, optional | a previous owner's note: partial, biased, in their voice |
| `identifyResistance` | 0–1, optional | chance an Identify fails outright |
| `pages` | 3–10 | below |
| `placeholder` | bool | |

Each page: `id`, `spellName` (markup allowed), `tags` (effect tags), `power` (1–3; 0 for story pages), `danger` (0–3), `text` (markup), `ingredients` (ids), optionally `unidentifiable: true` (story pages) and `authorNote` (never shown).

Danger: **0** harmless · **1** mischief if misused · **2** someone could get hurt or lose something that matters · **3** lives or livelihoods.

Writing books:
- Most pages are mundane and specific: charms someone really would need.
- One or two outliers per book that don't belong. Across the set, at least four books have an outlier of danger ≥ 2. Every book keeps at least one danger-0 page.
- A summary misleads by omission, never by lying.
- Book text is practical and slightly archaic, never jokey.

### The prototype catalogue

| id | Title | Spine | Rarity | Contents |
|---|---|---|---|---|
| `common-wards` | Common Wards & Small Mendings | warding | uncommon | **Gold.** Tutorial book. Outlier: Unbidden Door (opening, 2). Page 7 unidentifiable |
| `household-arts-3` | Household Arts, Vol. III | domestic | common | **Gold.** Outlier: Hold Fast (preservation, 2) |
| `hearth-and-kettle` | Hearth & Kettle | domestic | common | **Gold.** All safe: drying, stain-lifting, warmth, pest-warding |
| `advanced-botanical` | Advanced Botanical Compounds | botanical | rare | Plant-lore throughout. Outlier: Quickrot (decay, 2) |
| `primer-green` | A Primer of Green Things | botanical | common | Safe: growth (power 1), plant-lore |
| `field-and-furrow` | Field & Furrow | botanical | uncommon | Growth (power 2). Outlier: Rust-Call (blight, 2) |
| `patient-mind` | The Patient Mind | memory | uncommon | Memory-aid (power 2). Outlier: Unlearning (memory-erasure, 3) |
| `sleep-neighbours` | Sleep & Its Neighbours | memory | uncommon | Sleep; Waking Clear (memory-aid, 1). Outlier: Dreamless (sleep-deep, 2) |
| `clerks-companion` | The Clerk's Companion | domestic | common | Clerical charms. Outlier: False Hand (forgery, 2) |
| `lanterns` | Lanterns and Lesser Fires | fire | uncommon | Linen-Dry (drying, 1), Warm Hands (warmth), Kindle (fire-small). Outlier: Fireball (fire-destructive, 3) |
| `parlour-tricks` | Tricks for the Parlour | illusion | common | Illusions. Outlier: Second Face (glamour, 2) |
| `weatherwright` | The Weatherwright's Ledger | weather | rare | Weather-reading, rain. Outlier: Thunderhead (weather-storm, 3) |
| `gardeners-almanac` | A Gardener's Almanac of Small Weathers | weather | common | Weather-reading, rain (power 1). Outlier: Hailcall (weather-storm, 2) |
| `on-keeping` | On Keeping | preservation | rare | Preservation (power 2). Outlier: Stillness (stasis, 3) |
| `small-comforts` | Small Comforts | healing | common | All safe: healing-minor, warmth, sleep |

The set is built so every story arc has a tempting book and a safe alternative on the same topic — though some "safe" books hide a trap for a particular patron (the student and *The Clerk's Companion*), and some dangerous pages are harmless in hands that have no use for them (a sailor's wife reading the weather has no temptation for Hailcall).

## 6. Patrons and visits

### Patron
| Field | Notes |
|---|---|
| `id`, `name` | |
| `description` | a few words a curator would notice — coat, gloves, manner. Never a face. |
| `role` | `tutorial`, `story` or `filler` |
| `startTrust` | 0–3, default 0 |
| `warded` | optional; Read Thoughts is noticed (nice to have) |
| `fillerOrder` | fillers only: order of use |
| `preGameLoans` | `[{ bookId, borrowedDay, returnedDay }]`, days ≤ 0 — card history from before the game |
| `visits` | story patrons 2–3 steps, fillers 1 |
| `placeholder` | bool |

### Visit
| Field | Notes |
|---|---|
| `id` | |
| `step` | 1, 2, 3… Several visits can share a step as variants; the first that qualifies is used (BUILD_BRIEF §5.13) |
| `earliestDay`, `minDaysAfterPrevious` | scheduling |
| `when` | conditions (BUILD_BRIEF §5.12) |
| `forced` | only lending allowed |
| `patience` | default from balance |
| `greeting` | a string, or a list of `{ when, text }` — first match wins, the last entry has no `when` |
| `request` | `{ bookId?, topic, text }`. `topic` is the spine tag of *what they asked for*, not of the book they named. Omit `bookId` for "something about…" requests |
| `goal` | `{ tags, minPower }` — what they actually need |
| `temptations` | `[{ tag, use }]`, `use` = `misuse`, `accident` or `benign` |
| `alternatives` | optional `{ acceptSameTopic, minTrustForAny }` |
| `readThoughts` | `{ default, byTrust?, unlocks? }` |
| `answers` | per standard question id, answers keyed by trust: `{ "purpose": { "0": "…", "2": "…" } }` |
| `questions` | specific questions: `{ id, text, unlockedBy, answers, trust?, setFlags?, patienceCost? }` |
| `decisions` | `lent`, `alternative`, `offerRefused`, `declined`, `walkedOut`: `{ line, trust?, setFlags? }`. For requests without a `bookId`, any accepted offer uses `lent`. A missing line falls back to `questions.json` → `genericLines` |
| `loanDays` | default from balance |
| `outcomes` | keys `good`, `unhelpful`, `harm`, `harmMisuse`, `harmAccident`, `mixed`, `mixedMisuse`, `mixedAccident`, `declined`, `walkedOut` |
| `overrides` | `{ "<bookId>": outcome with its own category }` |

`unlockedBy`: `readThoughts`, `identifiedPage:<pageId>`, `cardBook:<bookId>`, `flag:<flag>`.

Answers fall back to the nearest lower trust, then to a deflection from `questions.json`, so write the levels where the answer actually changes. Omitted trust deltas use the balance defaults.

### Outcome
| Field | Notes |
|---|---|
| `channel` | `newspaper`, `letter`, `gossip`, `return`, `none` |
| `delayDays` | default by category |
| `headline` | newspaper only, at most 8 words |
| `from` | letter only |
| `text` | what the player reads or hears |
| `trust`, `reputation` | applied when it surfaces; public channels have reputation defaults |
| `setFlags` | applied when it's resolved, at lending |
| `bookReturns`, `returnInDays`, `onReturn` | `onReturn: { removePage }` |
| `category` | overrides only |

Lookup: `overrides[bookId]` → cause-specific key → category key → `outcomes_generic.json`.

## 7. Voice and style

**Patrons** speak briefly, one or two sentences at a time, each with a recognisable rhythm (Elara: polite, precise, a beat of hesitation). Answers grow more specific as trust rises. At Guarded they deflect or give the safe story — never a lie the game later contradicts for no reason.

**Read Thoughts fragments:** 4–14 words, first person or bare images, present tense. Practical and oblique, about the errand or the book: a negation (*I do not need to forget the eye of newt*), a fixation (*Twelve paces of bare earth. Twelve at least.*), an image, a name, a number. Never a statement of intent (*I want to hurt him*), never a verdict.

**Specific questions** follow naturally from what was found, phrased the way the curator would ask — curious, never accusing.

**Outcomes** are reported, not shown.
- Newspaper: a dry small-town paper. Headline of at most 8 words, one or two sentences. Never names the curator; often only implies the connection.
- Letter: personal and short.
- Gossip: one overheard sentence beginning "Did you hear…".
- Return: what the patron says on coming back.
- Silence (`none`): about a third of the week's outcomes, on purpose.

**Restraint.** No gore, no cruelty for effect. Grief, fear and anger are fine; harm is told by its consequences (*a hayrick burned; nobody was hurt*). Children are never harmed.

**Names** are short, a little old-fashioned, varied in sound. Avoid famous fictional names.

## 8. The gold standards, annotated

**`board-clerk` with `common-wards` — the tutorial.** A forced visit, one mana, and a book whose seventh page is `unidentifiable`. A single override covers the only book that can be lent: the book comes back on day 6 with page 7 removed, and that morning a one-line letter from the Board thanks you. The clerk's answers are courteous walls. Unbidden Door — a page about getting past wards — is the outlier, and it's readable: a player who lands on it has something to think about.

**`elara-voss` — the story arc.** Shows everything a story patron needs:
- A half-true surface request (a stain on fabric — the cloth bindings of her grandmother's herbarium) and a real goal the requested book does meet (preservation).
- A fragment about an ingredient shared by two spells in that book, so it narrows without settling.
- Two specific questions with different unlocks: the fragment, and identifying the dangerous page.
- Step-2 variants by decision: candid if you gave her a safer book, returning with a harder request if you lent the one she asked for, guarded if you refused.
- Outcomes across channels — return, newspaper, letter, gossip, silence — and greeting variants that react to what happened.
- A hook planted with no payoff in the prototype: the locked shelf, her grandmother's book, the Board's locks.

Story visits should match Elara's depth; one-off visits are about a quarter of it.

## 9. Story arcs to write

The bones of each arc; write the full files in Elara's shape, all `"placeholder": true`.

### Wren Hale — the linen charm and the fireball (`wren-hale`)
The designer's founding example. Practical, terse, weather-worn. `startTrust` 0.
- **Step 1, day 3.** Asks for `lanterns`: "Something to dry linen quick — my washing never dries in this damp." Topic domestic. Goal `fire-destructive`: she needs to burn a firebreak around Lower Hollis before the dry season, and the council won't pay for diggers. Temptation `fire-destructive` → accident: she's never cast anything bigger than a candle. Fragment: *Twelve paces of bare earth. Twelve at least.*, unlocking "What needs twelve paces of bare earth?". Lanterns → mixedAccident, channel `return`: a hayrick burned and her hands are singed (flag `wren-hale:hayrick`). Hearth & Kettle → unhelpful, trust +1. Declined → `gossip` on day 6: the Hales dug a firebreak round Lower Hollis by hand; it took the whole family four days.
- **Step 2, day 5 — candid** (`previousDecisionIn: alternative`). "Your linen charm works. I lied about what I wanted." Explains the firebreak, asks for Lanterns outright (topic fire). She's practised small fires all week, so the temptation is now `benign`. Lanterns → good, newspaper: *Lower Hollis firebreak holds*. Write her candid answers at trust 1 — she starts this visit Cautious.
- **Step 2, day 5 — subdued** (`previousDecisionIn: lent`). Opens with the hayrick, told by her (the `return` outcome). Asks for `small-comforts` for her hands (topic healing). Benign; good → silent.

### Jory Fenn — the student (`jory-fenn`)
Young, quick, nervous; talks too much when anxious. `startTrust` 1.
- **Step 1, day 2.** Asks for `patient-mind`: "My examination's in three days." Topic memory. Goal `memory-aid`, power 1. Temptations `memory-erasure` → misuse (he failed the spring paper and has imagined the examiner forgetting it), `forgery` → misuse. Fragment: *Spring paper. Red ink. Gone.*, unlocking "What happened with the spring paper?" — at trust 1 "Nothing. Why?" is itself a tell. Patient Mind → mixedMisuse, newspaper on day 5: Examiner Harrow cannot recall the spring results (flag `jory-fenn:examiner-forgot`). `sleep-neighbours` → good, `return`. `clerks-companion` (only taken at Candid) → harmMisuse, gossip: forged marks at the Academy.
- **Step 2, day 4 — brittle** (`previousDecisionIn: lent, alternative`). Over-prepared, not sleeping. "Something to help me sleep. I can't stop going over it." Topic healing, goal `sleep`. Temptation `sleep-deep` → accident (he'd sleep through the exam). Small Comforts → good. Sleep & Its Neighbours (taken only at Candid; it may still be on loan to him) → harmAccident, `return`: he slept through the exam.
- **Step 2, day 4 — sullen** (`previousDecisionIn: declined`). "Anything on memory." No `bookId`; step 1's goal and temptations.
- **Step 3, day 7 — results.** Two variants, both `minTrust: 2`: with `flagsAll: [jory-fenn:examiner-forgot]` — "I passed. I think I passed because Harrow forgot. I don't know if that was me." Otherwise — "I passed. On my own." Either way he asks for something fun for once (topic illusion, `parlour-tricks`; no temptations). Mostly silent outcomes: it's a character beat.

### Hennie Dorrow — the farmer (`hennie-dorrow`)
Loud, warm, a joker; the boundary with his neighbour Kessel is the one thing that sours him. `startTrust` 1.
- **Step 1, day 3.** Asks for `field-and-furrow`: "Better yields, that's all. My father swore by it." Topic botanical. Goal `growth`, power 1. Temptations `blight` and `decay` → misuse. Fragment: *Kessel's fence. A foot onto my land. A whole foot.*, unlocking "Who's Kessel?". Field & Furrow → mixedMisuse, newspaper on day 4: rust strikes a single barley field (flag `hennie-dorrow:kessel-rust`). `primer-green` → good, letter: best beans in years. `advanced-botanical` → harmMisuse, gossip: Kessel's hedge rotted to black overnight.
- **Step 2, day 5 — candid** (`minTrust: 2`). Laughs about the beans, then asks plainly for "the rust spell in Field & Furrow — just to give Kessel a fright." Goal and temptation both `blight`. Declining costs nothing (`decisions.declined.trust: 0`): "Aye. You're right. I'd have regretted it." Lending → mixedMisuse, newspaper.
- **Step 2, day 5 — jovial** (`previousDecisionIn: lent`). Cheerful as ever; asks for `primer-green` "for the beans this time". Benign. The player may wonder what he knows about Kessel's barley.

## 10. One-off visitors (write ten)

`role: filler`, one visit, `step` 1: a greeting, request, goal, temptations (mostly none), two or three answers, a fragment, decision lines, two or three outcomes (generic fallback covers the rest). Use this `fillerOrder`:

1. **Ada Marsh, the quiet regular** — "The usual": `hearth-and-kettle`. `preGameLoans` show it four times. `startTrust` 2. Benign; silent. Her card is the first lesson in reading a history.
2. **Nell Hart, a seamstress** — asks for `common-wards` for Stitch-Fast; topic domestic. It's out (the clerk has it), which demonstrates the "requested book is out" flow. Goal `mending` — no domestic book has it, so any alternative is unhelpful.
3. **Ines Carrow, a sailor's wife** — `gardeners-almanac` to read the weather before her husband sails. Hailcall is in it, but she has no temptation for it: danger needs a temptation. Good → a letter: he came home on the calm day.
4. **Bram Tolley, a baker** — warmth for his ovens in winter; topic domestic, no `bookId`. Benign. Chatty: a good carrier for gossip.
5. **Tilly Brook, a child** — magic tricks for her friends: `parlour-tricks`. `startTrust` 2 — children are. Temptation `glamour` → accident → gossip: her brother wore a cat's face for an afternoon and loved it.
6. **Osric Penn, a widower** — "a preservation spell"; topic preservation, no `bookId`; to keep his late wife's roses. Goal `preservation`; temptation `stasis` → accident. On Keeping → mixedAccident, letter: the cut roses keep beautifully; he tried the other charm on her rosebush, and it's very still now. Write him with care.
7. **Councillor Abel Wyke, a town official** — weather prediction: `weatherwright`. Temptation `weather-storm` → misuse. Every outcome `none`: you'll never know.
8. **Hester Gale, a new mother** — something soothing for herself, she hasn't slept in weeks: `small-comforts`. Benign; good → a short thank-you letter.
9. **Lysander Quill, a scholar** — `on-keeping` for research; `warded: true`. Benign; silent.
10. **Fenwick Stray, a peddler** — `clerks-companion` "for copying receipts"; topic domestic. Temptation `forgery` → misuse → newspaper: forged receipts at the market.

## 11. Schedule

Seeded in `schedule.json`. Day 1 holds only the clerk; days 2–7 have three slots each, with each story patron's visits two or three days apart. The calendar's month names are placeholders.

## 12. Newspaper, letters and generic outcomes (to write)

**`newspaper.json`**
```json
{
  "masthead": "The Lantern Street Gazette",
  "flavour": {
    "1": ["Bridge toll to rise by a penny", "Lost: one grey goose, answers to nothing"],
    "2": ["…"]
  },
  "toneLines": {
    "wary": ["Some say the library lends too freely these days."],
    "neutral": ["…"],
    "warm": ["…"]
  },
  "placeholder": true
}
```
Two or three flavour items a day: small-town, specific, now and then quietly ominous in a way that may pay off later (or never). A few tone lines per band, about the library.

**`letters.json`**
```json
{
  "tutorial": [
    { "day": 1, "from": "The Board", "title": "Rules of the desk", "text": "…" }
  ],
  "debt": {
    "1": { "from": "The Board", "text": "…" },
    "2": { "from": "The Board", "text": "…" },
    "3": { "from": "The Board", "text": "…" }
  },
  "placeholder": true
}
```
Tutorial notes, dry and in-world, the Board writing to its curator. Day 1: the card, the catalogue, Identify, the stamp. Day 2: questions, Read Thoughts, patience, the notebook. Day 3: offering another book, declining, the fact that consequences arrive later.

**`outcomes_generic.json`** — for each category (`good`, `unhelpful`, `harm`, `mixed`) and, for harm and mixed, each cause (`misuse`, `accident`): two or three outcomes using `{name}` and `{book}` placeholders, across channels, at least one silent each. Used whenever a visit doesn't define its own.

## 13. Checklist before committing content

- [ ] Validation passes (BUILD_BRIEF §8.3) and `placeholder` is set
- [ ] Every story visit: a surface request that isn't the whole truth, at least one specific question, a fragment that narrows without settling, outcomes for good, unhelpful and harm
- [ ] Every arc: one tempting book, a reachable same-topic safe alternative, and a step 2 that differs by what the player did
- [ ] About a third of the week's outcomes are silent
- [ ] Fragments follow §7; harm is reported, never shown; no child is harmed
- [ ] Each tempting book's danger can be found by Identify, not read off its summary
- [ ] `out/content-report.md` regenerated
