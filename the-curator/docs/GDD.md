# The Curator — Game Design Document

*v1.0 · working title · supersedes `magical-library-gdd.md` v0.8*

This document is design intent: what the game is and why. Companions:

- `BUILD_BRIEF.md` — how to build the prototype: architecture, exact rules, milestones.
- `CONTENT_GUIDE.md` — how to write books, patrons and outcomes, with gold-standard examples.
- `../CLAUDE.md` — working rules for Claude Code.

When documents disagree: `game/content/balance.json` wins on numbers, this document wins on intent, the build brief wins on implementation. Items marked **[A#]** are leans the designer hasn't confirmed yet (§14).

---

## 1. Concept

You are the curator of a magical library. Patrons come to the counter asking for books. You don't know what most of the books contain: spell books are accumulated, copied and inherited, so a household handbook can hold a laundry charm next to something that should never touch a living thing. You have two small spells for finding out more, a little mana each day to cast them, and a patron in front of you who won't wait forever.

**Pitch:** Papers Please meets My Little Bookstore. Gatekeeping as gameplay; cosy, with moral weight.

**In one sentence:** decide what to hand over, knowing too little, to people who may not trust you yet — and live with consequences that arrive late, or never.

### Tone

Cosy but morally serious. The library is warm; the dilemmas are real. The game never punishes you for information it didn't give you, and it never cheats: patrons don't secretly change their minds to make you wrong. You can misread them, though. The honest answer to "did I make the right call?" is often "I don't know — I didn't have enough information." That is the game.

Harm is reported, not shown. No gore.

## 2. The curator

A librarian, not a wizard. They've worked among these shelves long enough to absorb magic the way you absorb a language by living somewhere: imperfectly, practically, without ceremony. They know exactly two spells that help with the job. They read the morning paper. They know their shelves — mostly by spine, not by contents.

Not a hero. You don't go out and fix things. You hand people books and read about what happened.

## 3. Presentation

**One tall painted scene in a landscape window.** The camera looks up or down:

- **Looking up:** the patron, a silhouette framed in a warmly lit archway, bookshelves on either side. Conversation happens here.
- **Looking down:** the desk — library card, notebook, card-catalogue drawer, the open book, newspaper, candle. Investigation and decisions happen here.
- The counter shows in both views, so the halves read as one room.

Scroll, keys or a click pan the camera; a patron arriving pans it up. Patrons are always silhouettes. Faces are never shown.

**Art:** oil paintings by the designer, photographed and layered — the wall seen through the doorway, the doorway wall, shelves, patrons, counter, desk, candle, book covers. Until a painting exists a flat placeholder stands in, and a finished painting drops in without code changes.

**Light is the mood:** a flickering candle on the desk, warm light behind the patron, daylight from a window that turns to moonlight as the day goes on. Each patron moves the time of day forward [A15]; the last one arrives at dusk, and by evening the candle is the main light. Legibility beats mood: documents stay readable in every phase.

## 4. A day

**Morning.** The newspaper (outcomes surface here among ordinary news), letters on the desk, books returned overnight in the returns tray, and the day's mana. You can browse and identify books before opening — preparing for whoever might come.

**Patrons.** Ring the bell for the next one. There is no clock; you set the pace. Three patrons a day in the prototype [A7]. Each visit: greeting and request, investigation, a decision, an exit line.

**Evening.** A day summary — what went out, what came back, fees, upkeep, tomorrow's mana — then autosave.

**The prototype is one week:** seven days, then a week summary [A7].

## 5. Resources and hidden values

| | Scope | Visible? | Purpose |
|---|---|---|---|
| Mana | per day | yes | pays for the curator's two spells |
| Money | running total | yes | fees in, upkeep out |
| Patience | per visit | yes | how long a patron tolerates being kept |
| Trust | per patron | as the curator's read, in the notebook [A11] | how open a patron is with you |
| Reputation | global | no | how the town regards the library |

### Mana
- A fixed allowance each morning (default 6), plus up to 2 unspent mana rolled over. Hoarding doesn't work.
- Read Thoughts costs 2, Identify costs 1.
- **Bonus mana [A1]:** +1 next morning for each patron you investigated before deciding (asked a question or cast a spell; max +2), and +1 for each good outcome that surfaces; total bonus max +3. The library responds to you being present and to your judgement proving out — not to a morality meter.
- Mana can't be bought. Money and mana pull against each other: lending freely earns money but no bonus, and risks harm; refusing everything is safe and slowly bankrupting.

### Money [A2]
- Lending a book earns its fee; rarer books pay more — the dangerous grimoire is often the lucrative one [A13].
- Upkeep (candles, the Board's levy) is paid every evening, so fees matter.
- Debt is allowed in the prototype. The Board writes letters, increasingly pointed. No game over.

### Patience
- Each visit starts with patience (default 4). A question costs 1. Identify costs 1 while a patron waits — they watch you leaf through the book [A16]. A refused offer costs 1. Read Thoughts costs nothing; they can't tell.
- Past zero, each further question or Identify costs 1 trust instead. If their trust is already at its lowest, they walk out.
- Browsing the catalogue, reading the card or notebook, and rereading pages you've identified are always free.

### Trust
Per patron, four levels:

| Level | Behaviour |
|---|---|
| Guarded | Gives the safe story. Deflects. May return if treated well. |
| Cautious | Elaborates if you ask the right question. |
| Candid | Tells you what they actually need; the reasons may still be complicated. Takes your recommendations more readily. |
| Confiding | Rare. Asks for what they'd never ask a stranger. |

Trust rises when you take care with someone — a suitable alternative instead of a flat refusal — and falls when you refuse them or keep them past their patience. Trust is not morality: a guarded patron isn't a dangerous one. They just don't know you yet.

### Reputation (hidden)
Good outcomes that become public raise it; public harm lowers it sharply; refusing without looking into anything, and patrons walking out, lower it slightly. In the prototype it colours the newspaper and sets how warily strangers start. Never shown as a number.

## 6. The curator's spells

### Read Thoughts — 2 mana, once per visit, silent
Skims the surface of a patron's mind. It yields a **fragment of what they're thinking about** — practical, about the book or their errand, never a confession. *"I do not need to forget the eye of newt."* Now you know the book they want has a spell that uses eye of newt, and that it matters to them. Not which spell, not why.

A fragment can unlock a specific question ("What do you need eye of newt for?"). What surfaces can differ with trust and from one visit to the next. Later in the game, some patrons are warded: they notice the attempt and trust you less.

### Identify — 1 mana
Reads one random page of a book. Repeats happen — the cast is spent and nothing new is learned — so each further cast on the same book is worth less [A12]. Some books resist; some pages won't come into focus at all.

A page appears in **partial translation**, three ways:

- **Translated** — ordinary ink. You know what it says: *Eye of Newt*, *do not use near open water*.
- **Untranslated** — grey cursive. You can read the letters but not the meaning: *Hufeykrey*.
- **Illegible** — faded scribble. You can't read it at all.

Ingredients are the most legible part of a page, and each ingredient is always translated or always untranslated, wherever it appears. Players learn patterns with no stat tracking it: *hufeykrey turns up in both strong spells I've seen*.

### Personal spellcasting — post-prototype
Any identified spell can eventually be cast by the curator, given its ingredients: the librarian becoming a practitioner because they read so much.

## 7. Books, spells and ingredients

| Book property | Notes |
|---|---|
| Title | Often vague or misleading |
| Spine tag | Broad topic: domestic, warding, botanical, memory, fire, illusion, weather, preservation, healing |
| Rarity | Common, uncommon, rare, singular; sets the fee |
| Summary | Optional; a previous owner's note — partial, biased |
| Pages | One spell per page; 3–10 in the prototype |
| Cover | Painted per book, eventually |

**The mix problem.** Books aren't curated. Most pages are mundane; one or two outliers don't belong — a preservation charm that would bind living tissue, in a household handbook; a fireball between a linen-drying charm and warm hands. Patrons often know more about a book than you do.

**One copy each.** A lent book is gone until it comes back, and some never do.

Each spell has a name, what it does (effect tags), how strong it is, how dangerous it is, its text and its ingredients. The player only ever sees text and ingredients; the rest drives the rules and guides the writers.

## 8. Patrons

What drives a patron — all hidden:

- **Surface request** — what they say.
- **Actual goal** — what they really need; it may differ.
- **Temptations** — what they'd do with a dangerous spell if the book has one: *misuse* it (deliberate harm), have an *accident* with it (they'll try, and it goes wrong), or use it *benignly*.
- **Trust** and **patience**.

### Intent versus trust
A patron who lies to you isn't necessarily dangerous; they may just not trust you yet. The person asking for a laundry charm from a book that also holds a fireball may have a perfectly good reason for the fireball — and on their second visit, after you gave them the safer book without judgement, they might tell you. The game rewards relationships over one-shot reads: three visits of consistent, non-judgemental service can be worth more than a perfect Read Thoughts on the first.

### Arcs and one-off visitors
Story patrons return across days. Which visit comes next depends on their trust, what you decided, and what came of it. One-off visitors fill the rest of the days; they carry gossip and texture.

### Tells
Before any spell: nervousness in a greeting, oddly specific knowledge ("third shelf, green cover"), a farmer asking for a martial tome, a card full of the same book.

### The prototype cast
- **The Board's clerk** — day one, carrying a writ. You must lend (§12).
- **Elara Voss** — unhurried, always botanics, curious about the locked shelf. The gold-standard arc.
- **Wren Hale** — wants a linen-drying charm from a book that also holds a fireball.
- **Jory Fenn** — a nervous student who wants help memorising.
- **Hennie Dorrow** — a cheerful farmer who wants better yields, and has a neighbour.
- **One-off visitors** — among them a quiet regular who wants "the usual", a child after magic tricks, a grieving widower, and a councillor asking about the weather.

Every name and story is a placeholder until the designer rewrites it.

## 9. The desk

### The library card — the patron's
Issued by the library, carried by the patron: name, a short physical description, first visit, every loan with borrowed and returned dates. Purely factual. The curator can't write on it. In the prototype it is always accurate; later in the game, some are forged.

### The notebook — the curator's
One page per patron, three sections:

1. **Header (automatic)** — name, first visit, number of visits, and the curator's read of their trust [A11].
2. **Watched loans** — a ledger of the loans you chose to track. At the moment you lend a book, or a book comes back, one click writes the true entry in the curator's hand; skip it and it isn't there [A6]. Each row has a note column you can write in (*returned early, seemed nervous*).
3. **Free notes** — whatever you like. When Read Thoughts lands, one click copies the fragment in with the date [A6]. The game never reads this section; it's yours.

Patrons never see the notebook. Comparing card and notebook is how you notice something is off; the game never flags it for you.

### The catalogue drawer [A8]
An index card per book: title, spine tag, fee, summary if any, how many pages you've read, and whether it's on the shelf, out (with whom, since when) or back today. Pull a card and the book lands on the desk.

### The open book
The cover, the pages you've identified in partial translation, how many are unread. From here: Identify, offer this book instead, or stamp and lend it if it's the one requested.

### Newspaper, letters, returns tray
Outcomes surface among the ordinary news. Letters come from the Board and from patrons. Returned books wait in the tray to be shelved.

### The locked shelf
An iron-barred cabinet in the corner. Visible, never openable in the prototype. "The key isn't yours."

## 10. Decisions

During a visit, in any order:

- **Ask** [A5]. Four standard questions are always on offer — what it's for, what else is in the book, how they heard of it, whether they've done this before. Answers depend on trust. Investigation unlocks specific questions: a Read Thoughts fragment, a page you've identified, a past loan on their card.
- **Read Thoughts**, **Identify**, browse the catalogue and the notebook.

Then one decision ends the visit:

- **Stamp and lend** the requested book. Earns its fee.
- **Offer another book.** The patron takes it if it's on the topic they asked about, or if they trust you enough (Candid or more) to take your word [A14]. A refusal costs patience; you can try another. An accepted alternative earns its own fee and raises their trust.
- **Hand back the card** — decline. They leave a little less trusting; declining without having asked or looked into anything also costs reputation.

Some visits are forced: the clerk's writ allows only lending [A9].

## 11. Outcomes

**Settled when the book goes out, revealed later.** The rules compare the lent book's spells with the patron's actual goal and temptations:

| The book met their need | It held a spell they'd misuse or botch | Outcome |
|---|---|---|
| yes | no | good |
| no | no | unhelpful |
| no | yes | harm (misuse or accident) |
| yes | yes | mixed |

Hand-written overrides replace the rule for particular book-and-patron pairs and story beats. Declines and walk-outs have outcomes of their own. There are no dice: uncertainty comes from what you didn't read and what never surfaces.

**Surfacing** happens days later — in the newspaper, a letter, gossip from another patron, the patron's own return visit — or never. Silence is deliberate: you won't learn how every loan ended. When an outcome surfaces it can move trust, reputation and tomorrow's mana.

## 12. Story and progression

### The prototype: the first week
Seven days. Day one is a tutorial; for the first three days a short note from the Board on the desk introduces one set of mechanics each morning.

### The tutorial book
On day one a clerk of the Board arrives with a writ: *Common Wards & Small Mendings*, today. You have one mana and no choice but to lend [A9]. The book's last page won't come into focus.

*Placeholder hook:* on day six the book comes back with that page torn out [A17]. The prototype doesn't say why.

### Beyond the prototype
- **The world arc.** A slow accumulation: no single patron causes the climax; many individually defensible choices converge. The tutorial page resurfaces. Endings follow the pattern of your choices, not one decision. It should feel like a slow dawn.
- **Phases.** Finding your feet; established (more books, warded minds, patrons who ask for you by name); known quantity (the library as a faction — who do you serve?).
- **The theft.** Mid-game the notebook is stolen. Library cards remain; they belong to the institution. For a stretch of days you work from cards and memory. When a patron walks in, the curator scribbles whatever comes to mind on a scrap — generated from the true history with that patron (visits, loans, refusals, what Read Thoughts surfaced), never from what the player wrote. One scribble per patron per visit, gone the next day. Whoever took the notebook now knows whom you trusted and whom you watched; what's missing when it comes back is a clue.
- **Forged cards.** Inflated histories (loans that never happened) and scrubbed ones (real loans removed). Never flagged; the tell is the gap between card and notebook, and it's what justifies spending mana.
- **Book acquisition.** A travelling merchant (summaries cost extra), donations (possibly cursed), estate lots bought blind. New books wait on a pending shelf: identify first, or lend fast.
- **Personal spellcasting** (§6).

## 13. Prototype scope

**Must have**
- Tall scene with up/down camera, placeholder art, painted-art drop-in, candle flicker, archway and window light by time of day
- Library card, notebook (all three sections), catalogue drawer, open book in partial translation, newspaper, letters, returns tray
- Questions (standard and unlockable), Read Thoughts, Identify, lend, offer, decline, patience, trust
- Mana with bonuses, money with fees and upkeep, hidden reputation
- Hybrid outcome resolution; delayed surfacing on every channel, including silence
- Seven-day schedule: the clerk, four story arcs, one-off visitors; 15 books
- Day and week summaries, autosave, debug overlay

**Nice to have**
- Warded patrons, resistant books, an ingredient log, parallax on the camera pan

**Out of scope**
- The theft and scribbles, forged cards, book acquisition, personal spellcasting, the world arc, audio, normal-mapped lighting

The architecture must not block the out-of-scope items: the true history is kept in full, and the card is a view of it that a forgery could replace [A10].

## 14. Assumptions to confirm

Leans from design chat, used as defaults until the designer says otherwise.

| # | Assumption |
|---|---|
| A1 | Bonus mana from attentiveness (+1 per investigated patron, max 2) and good outcomes surfacing (+1 each); total bonus max 3 |
| A2 | Daily upkeep is the money sink; debt allowed, Board letters escalate, no game over |
| A3 | Gold-standard content written in design chat; Claude Code writes the rest as marked placeholders; the designer rewrites what matters |
| A4 | Claude Code runs on the designer's Windows PC, with Godot installed |
| A5 | Four standard questions, plus specific ones unlocked by investigation |
| A6 | One click at the moment (lending, return, Read Thoughts) writes a notebook entry; skip it and it's not there |
| A7 | Seven days, three patrons a day (one on day one) |
| A8 | Books are picked from a card-catalogue drawer on the desk |
| A9 | Day one: the clerk's writ makes lending the only option; mana is 1 |
| A10 | The true history is an append-only event log; the card, notebook header and later the scribbles are views of it |
| A11 | The notebook's trust read is accurate in the prototype |
| A12 | Identify picks uniformly from all pages, repeats included |
| A13 | The fee is always the lent book's own fee |
| A14 | Patrons accept an alternative on the topic they asked about, or any alternative at Candid trust or above |
| A15 | Time of day advances per patron, visually only |
| A16 | Identify costs patience only while a patron waits; Read Thoughts never does |
| A17 | Placeholder hook: the tutorial book returns on day six with its last page torn out |

## 15. Open for later
- What the tutorial book's last page holds, and who wanted it.
- The Board: who they are and what they want.
- Who steals the notebook, and why.
- What's on the locked shelf, and whether the curator ever gets the key.
- The title, the town, the calendar's month names.
