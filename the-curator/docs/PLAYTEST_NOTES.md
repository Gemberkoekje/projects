# Playtest notes

For the designer: how to run the prototype week, what to watch for while you play, what's known to be rough, and the questions waiting on you.

## How to run it

**A beta build is the easiest.** Each change merged to main publishes a Windows zip on the repository's Releases page, as a pre-release named "The Curator 0.1.N-beta". Download the newest one, unzip it anywhere and run `TheCurator.exe`; nothing needs installing. The same link works for playtesters, and the README inside tells them how to play. The title screen shows the version, so feedback can name its build.

**To run it from the project**, you need the **.NET 10 SDK** and **Godot 4.6.3, .NET build** (`Godot_v4.6.3-stable_mono_win64`). The projects target .NET 10 (Q3).

**From the Godot editor.** Import `the-curator/game/project.godot` and press Play (F5). The editor builds the C# itself.

**From a terminal**, in `the-curator/`:

```powershell
setx GODOT "C:\path\to\Godot_v4.6.3-stable_mono_win64.exe"   # once; open a new terminal after
dotnet build
& $env:GODOT --headless --path game --import    # once after cloning, and after new paintings arrive
& $env:GODOT --path game                        # the title screen
& $env:GODOT --path game -- --new --seed 7      # straight into a new game; the seed changes which pages Identify turns up
```

Saves and settings live in `%APPDATA%\Godot\app_userdata\The Curator\` (`save.json`, `settings.cfg`). Delete `save.json` to start clean. The game saves each evening, and Continue resumes the next morning.

**Controls**: the mouse does everything. Space rings the bell. W/S or the arrow keys look up at the counter or down at the desk. N opens the notebook, C the catalogue, Esc pauses. When run from Godot (a debug build), F1 shows the debug overlay with the visit's hidden goal, temptations and trust; F2 gives +5 mana, F3 ends the day, F4 reveals every page of the open book.

**Your paintings**: drop a photograph into `game/art/painted/<slot>.png`, then run `--import` once. It replaces the Easel stand-in for that slot. Slots and their rectangles are in `game/art/layout.json`; `tools/easel/README.md` lists which slots have stand-ins.

## A week, briefly

Day 1 is the tutorial: the Board's clerk asks for *Common Wards*. Days 2–7 bring three patrons each, from morning to dusk. You read their cards, ask questions, cast Read Thoughts (2 mana) or Identify a page (1 mana and some patience), then lend, offer something else, or hand back the card. What came of it arrives later, or never: in the morning paper, in a letter, as gossip from the next patron, or from the borrower in person. Each evening shows a summary. After day 7, the week summary shows what you heard and what you never did.

The notebook keeps a page per patron and, on its first tab, an ingredient log: every ingredient on the pages you've identified, and where it turned up.

## What to look for

- **Does the tension land?** You should want to lend: fees pay the upkeep, and patrons are pleasant. You should also want to look first. Does a mana budget of 6 a day, plus up to 3 for attentive days and good news, make each Identify feel like a choice?
- **Identify's half-reading.** A page shows its name and ingredients, with words smudged or lost. Is it readable enough to judge danger, and does it still leave you guessing?
- **Patrons' tells.** Greetings, answers, impatience and Read Thoughts fragments. Do they give a careful reader enough to catch the patrons who mean trouble, without giving it away?
- **The ingredient log.** The GDD wants players to learn ingredient patterns without the game tracking them. The log lists only what the pages showed. Does it help you notice the patterns, or does it do the noticing for you?
- **Consequences.** About a third of outcomes are never heard of. Does that feel like the world going on without you, or like the game withholding? Does the notebook help you link the news back to your loans?
- **Pacing.** Three patrons a day, seven days. Is a day too short, too long, about right?
- **Legibility.** The documents at dusk and night, and with the reading lamp setting on. Settings also has three text sizes.
- **The interim art.** It's there to be replaced. Do the slots sit where your paintings will go?

## Known issues

- **Windows is untested.** All the work ran on Linux. The Windows build is exported in CI, and the same pipeline's Linux build plays the week, but nobody has run the Windows build on Windows yet, and the `d3d12` driver is untried (Q5).
- **Careful play still causes about 4 mixed outcomes a week**, against the brief's "at most 1" (Q25). See `docs/BALANCE_REPORT.md` for why and what would change it.
- **Outright harm never happens if you lend what's asked for.** Every tempting request is for a book that also does what the patron wants, so the worst result is mixed (Q33).
- **Some written branches can't be reached**: Jory's Clerk's Companion branch (Q27), answers at trust levels no visit reaches, and Lysander's fragment (Q30).
- **Placeholders**: the candle's flame is still drawn in code, documents use flat paper colours, and every patron shares one silhouette. There's no sound; the GDD leaves audio out of the prototype.
- **Leaving mid-day loses that day**: the game saves only in the evening (§7.12).
- **Resistant books** are built but unused: a book can resist Identify (`identifyResistance`) and a page can refuse to come into focus, but no book resists yet. Giving one resistance would make careful play miss even more (Q25).

## Questions waiting on you

`docs/QUESTIONS.md` has all of them; these are the ones most likely to change how the week plays:

- **Q25** The careful-play guardrail: accept the stand-in, or change Identify or the books.
- **Q33** Should a careless player be able to cause outright harm?
- **Q7** Phases advance after each decision (the brief), so the third patron arrives in the afternoon and leaves at dusk (the GDD has them arrive at dusk).
- **Q9** Returned books can be lent again before they're shelved.
- **Q10** How long a "Note it" moment lasts.
- **Q19** A good outcome heard in the morning adds to that morning's mana.
- **Q21** Forced visits can't be declined and never walk out.
- **Q29, Q30** Step-2 visits drop step-1 temptations, and some text is never seen.
- **Q31** The speech panel sits beside the archway rather than just above the counter.
- **Q32** Real weekday names beside invented months.
- **Q3, Q4** .NET 10, and a 1600×900 window that scales the 1080p picture.
