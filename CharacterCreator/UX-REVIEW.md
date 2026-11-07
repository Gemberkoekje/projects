# Character Creator: a UX review of the player-facing UI

_A review of the build screen, the presets page and the FAQ against the Laws of UX
(lawsofux.com), with designed improvements. Beheer and the sign-in pages are out of scope.
2026-11-07, on the demo build (projects main `146c3076`)._

_To understand this, start with §0 (the short version), then §3 (the proposals), and read §2
(the laws) where a proposal's reasoning matters._

**Status:**
- **Batches 1 (P3, P4, P9, P11) and 2 (P5, P7) are built.** NOTES.md, "UX batch 1" and "UX batch
  2", has what was built and where it departs from the proposals.
- **Left from P4 and P11** until P1/P2 give them a place:
  - P4: the XP bar's motion and the new row's flash;
  - P11: the folding phone head and the short names (which need an SL/EC check).
- **Next:** batch 3 (P1, the sticky XP bar). The bar is already in the head, with Opslaan and
  Afdrukken.

---

## 0. At a glance

The screen does the hard part right: every skill says in words what one more purchase would do,
plans never refuse anything, and the look is Guild Hall's. What hurts is **where things are**:

- **What you have is far from where you add.** On desktop, with one category open, the chosen
  table starts 1,794 px down: two screens below the picker. On a phone the save and print buttons
  are four screens down, and the XP counter scrolls away while you pick.
- **Every row talks the same amount, so nothing stands out.** Six mage rows repeat the same
  91-character sentence. Rows you can buy and rows you can't look nearly the same.
- **Many small targets:** the add button is 34×32 px, the stepper and remove buttons 32×32, the
  tick box 18×18. All are under the 44 px a thumb needs.
- **The first visit is a blank form.** The onboarding hint ("begin met een voorbeeld") sits in
  the chosen table, below the fold. Above it are four empty "Geen." cards.
- **The presets page is 4,180 px for 15 presets.** Each card is taller than the screen (≈880 px),
  and its button is at the bottom.

**The six proposals that matter most**, in order:

| # | Proposal | Laws it answers | Effort |
|---|---|---|---|
| P1 | A sticky XP bar with budget, status and the two main actions | Goal-gradient, Doherty, Fitts, Proximity | M |
| P2 | "Jouw personage" beside the picker; tabs on a phone | Proximity, Serial position, Cognitive load | M |
| P3 | Quieter picker rows: one status pill, one main action, summarized choices | Hick, Miller, Von Restorff, Tesler | S–M |
| P4 | Instant feedback and Undo instead of most confirmations | Doherty, Peak-end, error forgiveness | S |
| P5 | A real first visit: three ways to start | Jakob, Pareto, Peak-end | S |
| P9 | Compact preset cards with the button on top | Hick, Fitts, Cognitive load | S |

---

## 1. What was measured

Desktop 1280×900 and phone 375×812, light theme. Two states: a first visit (empty plan) and
the Paladijn preset (11 skills).

| What | Desktop | Phone |
|---|---|---|
| Where the build head starts | 265 px | 294 px |
| Where the chosen table starts (empty plan) | 1,194 px, below the fold | 615 px |
| Where the chosen table starts (one category open) | 1,794 px | n/a (it comes first) |
| Where the picker starts | 389 px | 1,546 px |
| Where energy and properties start | 389 px | 2,381 px |
| Where save and print are | 1,329 px (empty plan) | 3,078 px |
| Page height, Paladijn plan, one category open | 2,806 px | 3,381 px |
| Add button / chain button | 34×32 px | same |
| Stepper / remove button | 32×32 px | same |
| Tick box | 18×18 px | same |
| Category head | 49 px high, full width | same |
| Picker row height | 76 px | taller |
| Longest status text (mage B rows) | 91 characters, repeated 6× | same |
| Action buttons in the bottom bar | 7 | 7 |
| Preset card height / button offset | ≈880 px / 822 px from the top | taller |

The first load of the WebAssembly client wasn't timed here: see §4.

---

## 2. The laws, one by one

✓ holds · ◐ partly · ✗ breaks.

| Law | | What we have | Proposal |
|---|---|---|---|
| **Jakob's law**: users expect what they know from elsewhere | ◐ | The categories follow the book (good). But point-buy character builders in games keep the budget visible all the time, and ours scrolls away. Thorfgin's builder showed the chosen table at the top. | P1, P2 |
| **Fitts's law**: the time to hit a target grows with distance and shrinks with size | ✗ | Targets of 32–34 px, and an 18 px tick box. On a phone, save and print are 3,000 px away. | P1, P3 |
| **Hick's law**: decision time grows with the number of choices | ◐ | Folding categories helps a lot. Inside one, every row offers the same buttons with the same weight. The bottom bar has 7 equal buttons. Presets: 15 tall cards. | P3, P7, P9 |
| **Miller's law / chunking**: working memory holds about 7 items | ◐ | Grouping by category is good chunking. The status sentences aren't: "vereist één van: Magiërspreuken A - Wit, Magiërspreuken A - Grijs, Magiërspreuken A - Zwart". | P3 |
| **Doherty threshold**: answer within 400 ms, or the user drifts | ◐ | The engine answers instantly. But adding a skill shows its result elsewhere: the XP counter and the chosen table are off screen. | P1, P4 |
| **Proximity / common region**: what belongs together sits together | ✗ | "What I have" and "what I can add" are a page apart. The left-over warning sits under the name field, away from the counter it explains. | P1, P2 |
| **Similarity**: what looks alike is read as alike | ◐ | A skill is a row in the picker and a table row in the chosen list, with different controls for the same action (+ vs. the stepper). | P2, P3 |
| **Von Restorff**: the item that differs is remembered | ✗ | A row you can buy differs from one you can't only by the colour of the status text and of a small button. | P3 |
| **Serial position**: people remember the first and last items | ◐ | First: a provisional banner every visit. Last: a bar of 7 buttons. The two things that matter (your character, its status) are in the middle. | P1, P5, P7 |
| **Goal-gradient**: effort rises as the goal comes closer | ✗ | XP is three bare numbers. Nothing shows "nearly there" or "3 more XP to spend". | P1, P6 |
| **Zeigarnik effect**: unfinished tasks stay on the mind | ◐ | Problems show per row. Nothing says "2 things don't fit yet" or where they are. | P6 |
| **Peak-end rule**: an experience is judged by its peak and its end | ✗ | Finishing a valid character goes unmarked; the end is a bar of equal buttons. | P6, P7 |
| **Tesler's law**: some complexity can't be removed, only moved, ideally to the system | ◐ | "+ met vereisten" moves it well. But the player still chooses between "+", "+ met vereisten" and "+ toch" on every blocked row. | P3 |
| **Postel's law**: be liberal in what you accept | ✓ | Comma or point in XP, three generations of old saves, unknown skills kept by name. Search could accept more: P8. | P8 |
| **Occam's razor**: no more than needed | ◐ | Seven equal actions; four empty summary cards on a first visit. | P5, P7 |
| **Aesthetic-usability effect**: a pleasing design is perceived as easier | ✓ | Guild Hall carries it; this is the screen's strongest asset. | — |
| **Selective attention / banner blindness** | ✗ | The provisional banner is permanent, so people learn to skip banners, including the one notice that matters ("2 vaardigheden vereisen iets"). | P1, P5 |
| **Cognitive load** | ◐ | Most screen space goes to lists. The question "is my character right?" has no single place. | P2, P6 |
| **Mental model** | ◐ | Players think in spells ("I want Heal Wounds") and in old names. Search knows only skill names and categories. | P8 |
| **Pareto principle**: 80% of use is one path | ◐ | Most visits are a new character of 15 XP; that path is the default (good). Its first screen is still a bare form. | P5 |

---

## 3. Proposals

### P1. A sticky XP bar

**What:** one bar that stays in view: under the crest bar on desktop, at the bottom of the
screen on a phone (thumb reach). It holds:

- the XP budget as a bar, with the "at most 3 left over" zone marked;
- the plan's status in one phrase, which jumps to what is wrong;
- the two actions that end a session: **Opslaan** and **Afdrukken**;
- a small "voorlopige regels" badge instead of the full-width banner.

```
┌──────────────────────────────────────────────────────────────────────────────────────┐
│ Paladijn · nieuw personage   ███████████████████████░░░  13 / 15 XP · nog 2 over      │
│                              └────── besteed ───────┘└≤3┘                             │
│ ✓ Alles klopt                                    [voorlopig ⓘ]  [Opslaan] [Afdrukken] │
└──────────────────────────────────────────────────────────────────────────────────────┘
          with problems:  ⚠ 2 dingen kloppen nog niet ›     (jumps to the first)
```

**Phone:** a 56 px bar at the bottom: `13/15 XP ▮▮▮▮▮▮▯ · ✓` on the left, and a "Mijn personage"
button on the right that opens P2's tab. Save and print live in that tab.

**Why:**
- Goal-gradient: the remaining distance is visible.
- Doherty: every add moves the bar at once, where you're looking.
- Fitts: the main actions are always one tap away.
- Proximity: the left-over warning sits on the bar it explains.

**Notes:**
- It replaces the counters in the build head. The head keeps the name and the kind of plan.
- Its status words come from `FindingWords`, as today.

### P2. "Jouw personage" beside the picker; tabs on a phone

**Desktop:** two columns. The **picker** stays on the left. On the right is a **sticky "Jouw
personage" column** that scrolls on its own:

```
┌─ Vaardigheden ──────────────────────────────┐ ┌─ Jouw personage ─────────────────────┐
│ [Zoek…]   Basis | Extra | Alle   ☐ Alleen   │ │ Klopt je personage?  ✓ ✓ ⚠ (P6)       │
│                                    wat kan  │ ├──────────────────────────────────────┤
│ ▾ Pantservaardigheden                    7  │ │ Lezen & schrijven        0   1×   ✕   │
│   Harnas I        0 XP  ✓ gekozen           │ │ Harnas I                 0   1×   ✕   │
│   Harnas III      2 XP  ⚠ vereist Harnas II │ │ Harnas II   ⚠ vereist…   2   1×   ✕   │
│                       [+ met Harnas II 4 XP]│ │ …                                     │
│ ▸ Wapenvaardigheden                      6  │ ├─ Energie ──── ● Spiritueel 10         │
│ ▸ Magiërvaardigheden                     9  │ ├─ Eigenschappen ── LP 2 · PP 4         │
│   …                                         │ ├─ Spreuken en technieken (8)  ▸        │
└─────────────────────────────────────────────┘ └─ Recepten (0) ───────────────────────┘
```

- The chosen list is compact (name, XP, times, remove); details open in place, as in the picker.
- Energy and properties shrink to one line each. Spells and recipes fold, with their counts.
- **Phone:** three tabs under the build head: **Kiezen** · **Mijn personage (11)** ·
  **Overzicht**. The XP bar (P1) stays at the bottom across all three. This is what DESIGN.md §5
  already asked for ("the summary as a tab").

**Why:**
- Proximity and uniform connectedness: what you have sits next to where you add.
- Serial position: your character is always first or last in view, never in the middle of a
  long page.
- Cognitive load: one place answers "what do I have, and is it right?".

### P3. Quieter picker rows

**Before**, one blocked row:

```
☐  Harnas III                      2 XP   [+ met vereisten (4 XP)]  [+ toch]
   vereist Harnas II
```

**After:**

```
   Harnas III     2 XP   ⚠ vereist Harnas II               [+ met Harnas II · 4 XP]
   Magiërspreuken B - Vuur   3 XP   ⚠ vereist een Magiërspreuken A (Wit, Grijs of Zwart)   [⋯]
   Harnas I       0 XP   ✓ gekozen                                     [− 1 +]
   Lezen          0 XP                                                   [ + ]
```

- **One status pill** with an icon and few words: ✓ gekozen, ⚠ vereist …, ✗ te duur, ⓘ leraar.
  Nothing for "kan": the add button says that already, which takes noise out of 80% of rows.
- **Choices are summarized** when the skills share a name: "één van Magiërspreuken A - Wit,
  Magiërspreuken A - Grijs, Magiërspreuken A - Zwart" becomes "een Magiërspreuken A (Wit, Grijs of
  Zwart)". A small rule in `FindingWords`: a common prefix up to " - " or " (".
- **One main action per row.**
  - If the chain decides everything, the button is the chain, named: "+ met Harnas II · 4 XP".
  - If a choice is left, the row offers "⋯", which opens the details with the choices and "toch
    toevoegen".
  - "+ toch" moves into that menu. It remains possible (plans refuse nothing) but is no longer
    offered on every row (Tesler).
- **Rows you have** show the stepper in place, so the picker and the list use one control
  (Similarity).
- **Rows you can't buy** are dimmed to 70% (text, not the pill), so the buyable ones stand out
  (Von Restorff). Book order is kept (Jakob: the book is the map players know).
- **"Alleen wat kan"** is a tick next to Basis/Extra/Alle, for "what can I still take with 2 XP?"
  (Hick).
- **44 px targets:**
  - the whole row opens the details;
  - the add button is at least 44×44, and so are the stepper buttons;
  - multi-select keeps its tick box, inside a 44 px label;
  - on a phone, multi-select moves behind a "Meerdere kiezen" toggle, which few people need.

### P4. Feedback where you look, and Undo

- **After each add or remove**, a toast for 4 seconds, above the XP bar: "Harnas III toegevoegd,
  met Harnas II · 4 XP · **Ongedaan maken**". For a chain it names what came along. That fixes
  today's silent chain.
- **Undo replaces the confirmation** for "Nieuw plan" and for plain removes. People act faster
  when a mistake costs nothing, and confirmation dialogs train people to click "Ja" without
  reading.
- **Removing what others need** keeps its question: that is a real choice (remove them too, or
  keep them flagged), not a safety net.
- **Motion:** the XP bar animates its change (150 ms) and a new row in "Jouw personage" flashes
  once. Both respect `prefers-reduced-motion`.

### P5. A real first visit

Today a first visit shows a blank form with four empty "Geen." cards. Instead, while the plan is
empty, the right column (P2) shows **three ways to start**:

```
┌─ Hoe wil je beginnen? ──────────────────────────────────────────────┐
│ ▸ Met een voorbeeld        [Vechter] [Genezer] [Magiër] [Alle 15 ›] │
│ ▸ Uit de oude builder      Je .dat-bestand van builder.qequoia.nl   │
│ ▸ Zelf kiezen              Begin links met je eerste vaardigheid    │
└─────────────────────────────────────────────────────────────────────┘
```

- **The provisional banner** becomes P1's badge, with the explanation behind ⓘ. Banners stay for
  what's new in this visit: a saved plan opened, an import, rules that changed.
- **Why:**
  - Pareto: the common path, a new character from an example, is one click.
  - Jakob: people coming from Thorfgin's builder see their old save is welcome.
  - Occam: no empty cards.

### P6. "Klopt je personage?", a three-line checklist

At the top of "Jouw personage":

```
Klopt je personage?
 ✓ 15 van 15 XP besteed (hoogstens 3 over)
 ✓ Alle vereisten in orde
 ⚠ Harnas II vereist Harnas I            ›
```

- Each ⚠ jumps to its row. When all three are ✓, the block turns into the peak moment: **"Je
  personage is klaar"**, with Afdrukken and Opslaan right there.
- **Why:**
  - Zeigarnik: the unfinished items stay visible.
  - Goal-gradient: progress shows as ticks.
  - Peak-end: the end is "done", not a row of equal buttons.
- **The lines come from the engine as it stands:** no findings means ✓. Each finding kind maps to
  one of the three lines.

### P7. Two main actions, the rest under "Meer"

Today the bar holds seven equal buttons: Opslaan, Afdrukken, Exporteren, Importeren, Opgeslagen,
Voorbeelden, Nieuw plan. Instead:

- **Opslaan** and **Afdrukken** in the XP bar (P1).
- **"Mijn plannen ▾"** next to the name field. It switches between saved plans and lists them,
  as a document app does (mental model: a plan is a document).
- **"⋯ Meer"**: Exporteren, Importeren, Nieuw plan (with Undo, P4).
- **Voorbeelden** stays in the crest bar's menu, where it already is, and in P5.

### P8. Search that knows what players think in

- **Spells and techniques**: "heal wounds" finds Genezingsspreuken A (both), with the hit shown:
  "via Heal Wounds".
- **Thorfgin's old names**: "Runen Gebruiken" finds Rune use. The legacy map is already in the
  ruleset.
- **English and Dutch at once**, as today, and without accents: "magier" finds "Magiër".
- **Why:** mental model (players come with a spell in mind), and Postel (accept every name the
  player knows).

### P9. Compact preset cards

Today each card is ≈880 px, with 300–650 characters of text, a full skill list, and its button
822 px from the top. Instead:

```
┌─ Paladijn ──────────────────────────── [Begin hiermee] ┐
│ Nieuw personage · 15 XP          ● Spiritueel 10  LP 2  │
│ De klassieke ridder: harnas, schild, en genezing in…    │
│ Lees meer ▾                                             │
│ Harnas II · Schilden · Paladijn · Genezingsspreuken A   │
│ + 7 meer                                                │
└─────────────────────────────────────────────────────────┘
```

- **The button sits at the top** (Fitts, serial position); the whole card is clickable too.
- **Two lines of text**, folded open on demand. The **four defining skills** show, the rest is
  counted.
- **Energy and hit points** at a glance: what a player compares.
- **Filter chips** above the grid: Vechten · Magie · Genezing · Ambacht · Ritueel (Hick).
- About 260 px per card: the 15 presets fit in two screens instead of five.

### P10. Spells and recipes: grouped, with details beside them

- **Grouped under their skill** ("Genezingsspreuken A · 5"), folded when more than 12, with the
  energy cost on the tile.
- **The details** open beside the tile on desktop (a popover anchored to it), and as a bottom
  sheet on a phone. Today they open below all the tiles, away from the one you clicked
  (Proximity).

### P11. Small things

- **FAQ** as an accordion: the questions folded, so the page can be scanned (Hick).
- **Skill names**: "Genezingsspreuken A (spiritueel)" is long. The list could show "Genezing A
  (SP)", with the full book name in the details. That needs an SL/EC check first.
- **Focus**: after an add, focus stays on the row, not lost to the top of the page (keyboard and
  screen reader users).
- **Phone**: the build head's name and kind of plan fold into one line once a name is set.

---

## 4. Priorities, and how to check them

| Order | What | Size | Why first |
|---|---|---|---|
| 1 | P3 (rows), P4 (toast and Undo), P9 (presets), P11 (FAQ, focus) | S, each a day or less | Mostly CSS and a little component work; immediate relief |
| 2 | P5 (first visit), P7 (actions) | S | Small, and they make room for P1 and P2 |
| 3 | P1 (XP bar) | M | The biggest single gain; the layout change builds on it |
| 4 | P2 (two columns, phone tabs), P6 (checklist) | M | The layout change; do it once P1 is in |
| 5 | P8 (search), P10 (spells) | M | Valuable, less urgent |

**How to check:**

- **Five players, one task each** (Nielsen's five users find most problems):
  - "Make a paladin with 15 XP and print it";
  - "Bring your old builder character and add Harnas III".
  - Measure time, clicks, and where they hesitate. Do it before and after P1–P3.
- **Thorfgin's eye:** he knows what his players asked for (DESIGN.md §1, the README wishes). Several
  of these proposals answer them directly:
  - "say why", by P3;
  - "add several at once", which is kept;
  - "add the chain automatically", by P3;
  - "a bigger presets button", by P5 and P9.
- **The Doherty threshold for the first load:** time the WebAssembly download and start on a phone
  over 4G. If it's over 2–3 s, the prerender should show the build head and the folded categories
  (static, from the server) instead of a loading line.
