# Klaverjas: notes per slice

_To understand the code, start with `Klaverjas.core/Standen/StandBerekening.cs` (every scoring rule
in one pure function), then `Klaverjas.application/Commands/UitslagOpslaanHandler.cs` (what Dad types)
and `Klaverjas.WebApp/Pages/Beheer/Uitslag.cshtml` (where he types it). For who may see what, read
`Klaverjas.WebApp/Toegang.cs`. The why is in [PLAN.md](PLAN.md). (Slices 1–3 below name the admin pages
by their old root paths; since slice 4 they live under `Pages/Beheer` and `/beheer/...`.)_

## Slice 1: the core app (2026-11-23)

Dad can sign in, keep the players, enter an evening and see the standings exactly as the players
will see them. Built from the approved mock; nothing is online yet and nothing is imported yet.

**What is there**

- **Avonden**: the next evening (first club day after the latest entered one, skipping evenings
  without play) with "Uitslag invullen", the season's evenings newest first with "Wijzigen", the
  evenings without play among them, and a date box to open any other evening.
- **Uitslag invullen**: every active member as "Speelt"; status Speelt / Slaper / Afwezig as radio
  buttons drawn as the mock's segmented control (works without script), the slaper's round, card
  total and pits. Live hints ("Klopt dit?", "Precies 4200 gespeeld: wordt niet groen"), Enter moves
  to the next box, substitutes added from the list or by a new name, removable. A refused save keeps
  everything as typed and names the players concerned.
- **Stand**: "Stand na week N", the evening's winners, the points table with the month's club days
  as columns (evenings without play show their reason), the substitutes under it, the pits table
  with "Week totalen", the legend, "Uitslag wijzigen".
- **Spelers**: members and substitutes, "Wijzigen" in place (name, kind, email, phone, address,
  birthday, Doet mee / Gestopt, In de stand), "Nieuwe speler".
- **Instellingen**: club name, absence score, play day, first evening of the season, evenings without
  play (add / remove), the four colours.
- Sign-in with Dad's password, a year-long sliding cookie, `noindex` everywhere, `/healthz`.
- CI: `.github/workflows/klaverjas-build-and-push.yml` builds and tests every PR; on main it also
  pushes `ghcr.io/gemberkoekje/klaverjas` (the cluster deploy is slice 6).

**Decisions taken while building** (none changes the approved screens)

- **R17 "joins"** is a date on the player, `LidVanaf`: the day Dad adds a member. Evenings before it
  without a result count as absent. Slice 2's import must set it to `DateOnly.MinValue` for members
  who were there from the start, or they would get 4200 for every imported evening they miss.
- A **new evening lists every active member**, whatever their `LidVanaf`, so a member added the day
  after their first evening still appears on it.
- **Stopping (R18)**: a save that turns "Doet mee" into "Gestopt" also takes a member out of the
  stand, unless the stand was changed in the same save. Keeping a stopped member in is a second save
  ("Blijft in de stand"); the page's script mirrors this.
- The **absence score is the current setting for every evening**: changing it re-scores the past.
  Fine for a fixed 4200; worth knowing before anyone changes it mid-season.
- **R16**: the quarter's lowest evening is computed per member (`StandRegel.Laagste`) but not shown
  yet; the Eindstand question (Q17) decides what to do with it.
- An evening without play cannot be entered (refused) and a played evening cannot be made one.
- Not built yet, on purpose: the password change and the players' password (slice 4), "Rondes per
  avond" (only the table plans use it, slice 3), the mail settings (slice 5), "Ledenlijst printen".

**Gotchas**

- `<option>` is a tag helper in Razor. A local markup function that renders one must be `async Task`
  (MVC1006), or the build fails with errors in the generated file.
- A `<form method="post">` without `asp-page` posts to the current address, handler and all. After
  "Invaller toevoegen" the address is `?handler=Invaller`, so "Opslaan" added a second substitute
  instead of saving. Every form that can be reached through a handler now has `asp-page`.
- `docker compose up -d postgres` still checks `KLAVERJAS_PASSWORD` (the web app's variable): put
  both variables in `.env`.
- Port 5436 is taken on the dev machine (kasboek); Klaverjas uses 5437.

**Checked**

- 53 specs: R1–R7 and R16–R19 on the standings (ties, gap, absence by status, slaper totals as
  typed, substitutes in the podium only, pits ties, week totals without substitutes, joining,
  stopping, quarter's lowest marked not subtracted, calendar-year seasons), the calendar, the entry
  checks, the player rules, the queries, the password compare.
- In the browser against a local Postgres, with the mock's invented names and week 45–47 numbers:
  the totals match the mock, the week totals come out as 28 and 36 (the sheet showed 27 and 35, its
  formula error), a re-saved evening left one `AvondVersie`, an evening without play shows in the
  month column and is skipped as the next evening, "Gestopt" took the member out of the stand.

**Files**

| Where | What |
|---|---|
| `Klaverjas.core/Models` | `Speler`, `Uitslag`, `Avond`, `Instellingen` (+ `GeenSpeelavond`, `Kleuren`), `Kalender` |
| `Klaverjas.core/Standen` | `StandBerekening` (the rules) and the `Stand` it returns |
| `Klaverjas.application/Commands` | add/change a player, save an evening, settings, evenings without play |
| `Klaverjas.application/Queries` | Avonden, the entry screen, the standings, players, settings |
| `Klaverjas.application/Ports` | `ISpelerStore`, `IAvondStore`, `IInstellingenStore` |
| `Klaverjas.infrastructure/Adapters` | the Marten stores (`AvondVersie` lives with the evening store), the key ring |
| `Klaverjas.WebApp/Pages` | Index (Avonden), Uitslag, Stand, Spelers, Instellingen, Aanmelden, Fout |
| `Klaverjas.WebApp/wwwroot` | `site.css` (from the mock), `js/uitslag.js` |
| `Klaverjas.test` | specs per layer, in-memory stores, invented fixtures |

**Next: slice 2**, importing 2026 from "Standen" plus an export of all scores. Set `LidVanaf` to
"since always" for imported members, turn the holiday texts into evenings without play, and list
every difference with the workbook's totals.

## Slice 2: Excel inlezen and alle scores downloaden (2026-11-23)

Dad (or you) can read his "Standen week NN.xlsx" into the app and download every score again. Reading
writes nothing: a check screen first shows what comes in and every place where the app's sums differ
from Excel's; only "Inlezen" writes. Built from the approved mock `mock/inlezen.html`.

**What is there**

- **Instellingen → Excel**: "Bestand lezen" (upload) and "Downloaden (Excel)".
- **Excel inlezen** (`/Inlezen?id=…`): what comes in (evenings, members, evenings without play,
  substitute scores), the names the two sheets spell differently with a choice to re-couple, "Klopt
  het met de Excel?" (points, pits, pits on an evening without play, week totals), "Inlezen" /
  "Annuleren". After "Inlezen", Avonden says what was written.
- **Download**: one .xlsx with "Punten {jaar}" and "Pitten {jaar}" laid out like Dad's sheet (with his
  colours), "Alle uitslagen" (one row per result, every season) and "Spelers".

**How the import reads the workbook** (`ClosedXmlStandenLezer`, `ImportPlanner`)

- It finds its way by labels, not addresses: the "Naam" header, the row with the most dates, the
  totals right of "Naam", the "invallers" row (points sheet) and "Week totalen" row (pits sheet).
  Totals are read as Excel last saved them, never recalculated.
- A date column with text instead of scores ("Pasen") is an evening without play. Next January's
  column on the sheet is left out.
- Names are coupled across the two sheets: the same name first (capitals, accents and spacing don't
  count), then one at least 85% alike and clearly the best ("Lijkt op: klopt dit?"). Dad can re-couple.
- Exactly 4200 is Afwezig, **unless that player has pits that evening**: then it was a played 4200.
- The unnamed "invallers" row becomes one substitute, "Invaller (uit Excel)".
- Members get `LidVanaf = since always`; players already in the app are reused by name.
- An evening already in the app is replaced (the old one kept as an `AvondVersie`); the season's first
  evening and the evenings without play go into Instellingen.
- The differences are computed with the same `StandBerekening` as the live standings, over the
  workbook's evenings only.

**What the real workbook gave** (checked locally on "Standen week 45"; the data was wiped afterwards)

- 42 evenings, 20 members, 3 evenings without play, 4 substitute scores; 18 names equal, 2 a letter off.
- **All 20 points totals equal the workbook's**, also on the Stand page after "Inlezen".
- Pits: 699 against Excel's 708. The 9 are pits typed on 4 May, which the points sheet marks "vier
  mei" (no play): not imported, and said so on the check screen (Q19 for Dad).
- One 4200 with 2 pits (7 September) was a played 4200: that rule came from it.
- 14 "Week totalen" cells differ: the sheet's formula skips members (always lower in Excel).

**Gotchas**

- ClosedXML: read `CachedValue` for formula cells; `Value` would recalculate.
- curl on Windows (Git Bash): `-F "file=@path;type=…"` fails with error 26 when the path has spaces;
  copy the file first.
- The in-app browser can't pick a local file for an upload; test uploads with curl and the auth cookie.
- Sonar S5693 caps upload limits at 8 MB; the page allows 5 MB (the workbook is about 200 kB).
- Never commit the workbook, a real name, or a screenshot of the check screen: it shows real names.

**Also on this branch**: slaper cards print 1600 under Punten and a red "Slaper n" under Pitten in
the round sat out, as on Dad's own card sheet (mock and card proof print updated; the live mock on
gembernodes still shows the old card).

**Files**

| Where | What |
|---|---|
| `Klaverjas.application/Import` | `StandenBestand` (as read), `ImportPlanner` (pure), `ImportVoorstel`, `NaamVergelijker` |
| `Klaverjas.application/Commands` | `ImportVoorbereiden`, `ImportKoppelingWijzigen`, `ImportUitvoeren`, `ImportAnnuleren` |
| `Klaverjas.application/Queries` | `ImportVoorstelOphalen`, `ExportMaken` |
| `Klaverjas.application/Ports` | `IStandenLezer`, `IImportVoorstelStore`, `IExportSchrijver` |
| `Klaverjas.infrastructure/Adapters/Excel` | `ClosedXmlStandenLezer`, `ClosedXmlExportSchrijver` |
| `Klaverjas.WebApp/Pages/Inlezen.*` | the check screen, upload and download handlers |
| `Klaverjas.test/Import` | reader (on an invented workbook built in code), planner, export specs |

**Next: slice 3**, "Kaartjes en tafelindeling" (D12, D13, D15, D18).

## Slice 3: kaartjes en tafelindeling (2026-11-23)

Dad can prepare an evening: tick who gets a card, pick the slapers (with a fair proposal), print the
score cards, and print the table plan with its score sheets. Built from the approved mock; the app
never draws numbers (R12).

**What is there**

- **Avonden**: "Kaartjes en tafels printen" is the big button before the evening; once the evening is
  prepared, "Uitslag invullen" becomes the big one.
- **Kaartjes en tafelindeling** (`/Voorbereiden?datum=…`): who gets a card (all active members
  ticked), blank cards, "Wie slaapt er?" (the expected number of players, one row per slaper number
  with its round and each member's "N× geslapen", "Eerlijk voorstel"), "Kaartjes printen", and the
  plan by seat number for any number of players with "Deze tafelindeling printen" / "Alle aantallen".
- **Kaartjes printen** (`/Afdrukken/Kaartjes`): 12 per A4 in Dad's own layout. First name (full name
  when two active members share it); a slaper's card has the number top right, and 1600 plus a red
  "Slaper n" in the round sat out.
- **Deze tafelindeling printen** (`/Afdrukken/Tafels?aantal=…`): the overview for the wall (A4
  portrait), then one score sheet per table per round, two A5 per A4 landscape, cut down the middle.
  **Alle aantallen**: an overview for every number of players, 24 A4.
- **Uitslag invullen** starts the picked slapers as Slaper in their round.

**Decisions taken while building**

- **The table plans are computed, not imported (D13 changed).** Dad's evening workbook has no clean
  scheme: its score sheets mix seat numbers with names pulled in from the last draw, the formulas behind
  those names point at different columns from sheet to sheet, some blocks are empty or `#REF!`, and
  there are no sheets for 19, 23 or the small odd numbers. The plans use the approved mock's seeded
  search (ported exactly; specs pin a few rounds to the mock's values), so Dad gets the plans he saw.
  Properties, checked for every number by the specs: everyone plays once a round; the slapers are
  the lowest numbers (round 1 sits out 1…s, round 2 the next s, round 3 the next s, so slaper n's
  round follows from n); nobody gets the same partner twice; with 16+ players nobody sits with the
  same people twice. **Seven players has no plan** (nine sit-outs, seven players).
- The **expected number of players follows the ticked cards** until Dad picks one himself (stored as
  `VerwachtZelfGekozen`); a moment after the last tick the page saves itself and shows the slaper rows.
- **Eerlijk voorstel** keeps every pick and fills the empty places with who sat out least, then
  longest ago, then by name. A slaper must have a card.
- The **"N× geslapen" count starts from the evenings entered in the app**: neither the Excel import
  nor the app knows the old `slapers` sheet's history.
- **Printing is HTML** with named pages (`@page staand` / `@page liggend`), so one print job holds
  the portrait overview and the landscape sheets. The designs are in rem with the proof prints' root
  sizes (3.3 mm for cards, 4.95 mm for sheets). QuestPDF (D6) is not needed for this.

**Checked**

- 187 specs, including the plan properties for all 24 numbers of players, the fair proposal, the
  expected-number rule, the cards and the pre-set slapers.
- In the browser with invented players: unticking three gave 17 expected and three slaper places;
  "Eerlijk voorstel" skipped the three who sat out before; 19 cards on 2 A4; the entry screen started the
  slapers in rounds 1–3; Avonden switched its big button.
- Printed to PDF through Chrome: the cards on 2 portrait A4; the 17-player plan as 1 portrait overview
  plus 6 landscape pages of score sheets, matching the proof print; every overview keeps at least
  33 mm to the paper edge (25–28 players got tighter rows).

**Gotchas**

- Chrome's `printToPDF` honours named pages only with `preferCSSPageSize: true`.
- A Razor `RedirectToPage(null, null, values, "slapers")` keeps the scroll place with a fragment.
- The table plan must stay stable: printed overviews and score sheets of one evening must agree. The
  pinned specs guard the seeded search; never "improve" it without accepting that plans change.

**Files**

| Where | What |
|---|---|
| `Klaverjas.core/Tafels` | `Tafelschema` (+ `Tafel`, `Ronde`), `Tafelschemas` (the seeded search), `SlaperKeuze` |
| `Klaverjas.core/Models/Voorbereiding.cs` | an evening's preparation |
| `Klaverjas.application` | `VoorbereidingOpslaan`, `VoorbereidingOphalen`, `KaartjesOphalen`, `IVoorbereidingStore`; `UitslagOphalen` pre-sets slapers |
| `Klaverjas.WebApp/Pages` | `Voorbereiden`, `Afdrukken/Kaartjes`, `Afdrukken/Tafels`, `Shared/_Afdruk`, `Shared/_Tafelindeling` |
| `Klaverjas.WebApp/wwwroot/css/afdruk.css` | the print pages |

**Next: slice 4**, the players' page at klaverjas.gemberkoekje.nl with the players' password, the
handout slips, "Zet de stand online" and the /beheer split.

## Slice 4: the players' page, online and two passwords (2026-11-23)

The standings are online for the players, behind the club's password, the moment Dad presses "Zet de
stand online". His own screens moved to /beheer. Built from the approved mock.

**What is there**

- **The players' page** (`/`): "Stand na week N", the date, Dad's note, "Winnaars van de avond", then
  on a phone the points and pits rankings with only the latest week (`#`, Naam, Totaal, wk N), and
  on a computer (60rem and wider) the whole month as on Dad's sheet. "Online gezet op …" and a small
  "Beheer" link at the bottom. Before anything is online it says so.
- **The players' sign-in** (`/aanmelden`): the mock's "De stand bekijken" screen. While no players'
  password is set it says the page is not open yet.
- **Dad's screens under /beheer** (`/beheer`, `/beheer/stand`, …) with his own sign-in at
  `/beheer/aanmelden`. A player who follows the "Beheer" link is sent there too.
- **Stand → "Online zetten"**: the note ("Bericht erbij"), and one button that reads "Zet de stand
  online", then "Staat online ✓" (resting), or "Verbeterde stand online zetten" with a warning banner
  once anything the players would see has changed. Editing the note wakes the resting button.
  "Bekijk de spelerspagina" opens `/` in a new tab.
- **Avonden** has the mock's Online column: "✓ Online" (with the time on the latest), "Gewijzigd, nog
  niet opnieuw online", or "Nog niet online".
- **Instellingen**: "Spelerspagina" with the players' password and "Briefjes printen", and "Uw
  wachtwoord" (new, twice). **Briefjes** (`/beheer/afdrukken/briefjes`): 10 per A4 portrait, cut lines,
  club, address, password and "Eén keer intypen, daarna onthoudt uw telefoon of computer het."

**Decisions taken while building**

- **What goes online is a frozen copy** (`OnlineStand` holding a `StandWeergave`): names and numbers
  only, never email, phone or address (a spec checks it). Dad's own Stand screen draws the same
  `StandWeergave` live, through the same partial (`_StandTabellen`), so "Zo zien de spelers de stand"
  is literally what goes online. Whether it changed is a SHA-256 of the copy's JSON against the live
  one, so new colours do not count as a change and a new name or score does.
- **One cookie, two roles.** Dad's role opens everything, the players' page included. The cookie's key
  is derived from the password it was opened with, checked on every request, so a password change
  signs the other phones or computers out; Dad's own computer is signed in again on the spot when he
  changes his password.
- **The players' password ignores capitals and spaces at the ends**: phones capitalise the first
  letter and older players do not notice. It is kept readable (Dad prints it on the slips); Dad's
  own password is a salted PBKDF2-SHA256 hash (600,000 rounds).
- **The players' password never opens /beheer**, even if someone once made them equal; saving
  refuses to make them equal in the first place (both ways).
- **`Auth:Password` stays valid** after Dad chooses his own: it is the site keeper's way back in
  ("Wachtwoord vergeten? Bel de beheerder van de site").
- **No confirm dialog** before going online (the mock had one): Dad has just checked the standings
  above the button, and a mistake is fixed by correcting and pressing again. Fewer clicks for him.
- **Sign-in posts are rate limited per address**, opening the form is not: Dad's form 10 posts per
  5 minutes, the players' 40, since a whole club evening may sign in over one wifi.
- **Addresses are lower case** (`LowercaseUrls`), so links read `/beheer/stand` like what Dad types.
- The slips and the online texts take the address from the request, so they follow whatever web
  address the site gets (Q2).
- The month columns now also include an evening entered on another weekday, so the latest evening
  always has its column (the phone's "wk N").

**Checked**

- 229 specs (42 new): password hashing; the players' password rules; the cookie keys changing with
  each password and only that door's; the copy online (refused before the first evening, the note,
  the time, too long a note); "Verbeterde stand" after a correction and not after new colours; the
  players seeing the copy, not the correction; the Avonden column; `StandWeergave` columns, cells,
  substitutes, winners and that no personal details go online.
- Against the running app with invented players: every redirect (`/` → `/aanmelden`, `/beheer/…` →
  `/beheer/aanmelden` with the way back); a player's password with a capital and spaces signs in; a
  player gets nowhere in /beheer; the players' password at /beheer is refused with its own message;
  an outside return address is ignored; changing the players' password signed the player out and
  kept Dad in; Dad choosing his own password kept his computer in and signed a second computer out,
  and both his own and the configured password sign in afterwards; the rate limit answered 429.
- In the browser: Dad's Stand with the note and "Zet de stand online" → banner and "Staat online ✓";
  a correction → warning banner and "Verbeterde stand online zetten" while the players still saw the
  old total; after pressing, the new total. The players' page at phone width (375px, no sideways
  scroll; matches the mock) and computer width (the month tables); the sign-in on a phone; the
  Instellingen cards; the slips (10 on one A4, the real address `klaverjas.gemberkoekje.nl` on one
  line); every admin page and print page answering 200 with no link left to an old path.

**Gotchas**

- In a Razor *page* the request is `HttpContext`, not `Context` (that is the MVC view property).
- `dotnet run --environment Development` does not set the environment: `dotnet run` takes
  `--environment` for itself. Use the launch profile.
- NUnit shares one fixture instance across its tests: stores built in field initialisers leak.
- Bash heredocs choked on some of the C# in this slice; scripts went into files instead.

**Files**

| Where | What |
|---|---|
| `Klaverjas.core/Standen/StandWeergave.cs` | the standings as the players see them (+ `Kolom`, `WeergaveRegel`, `WeergaveCel`, `WeergaveWinnaar`) |
| `Klaverjas.core/Models/OnlineStand.cs`, `Wachtwoord.cs` | the copy online; Dad's password hash and the players' comparison form |
| `Klaverjas.core/Models/Instellingen.cs` | `Spelerswachtwoord`, `BeheerWachtwoord` |
| `Klaverjas.application` | `StandOnlineZetten`, `OnlineStandOphalen`, `IOnlineStandStore`, `Online/HuidigeStand` (live copy + fingerprint); `StandOphalen` and `AvondenOphalen` compare with what is online; `InstellingenOpslaan` takes the passwords |
| `Klaverjas.infrastructure/Adapters/Application/MartenOnlineStandStore.cs` | the copy online |
| `Klaverjas.WebApp/Toegang.cs` | roles, keys, sign-in, the cookie check, the right sign-in page |
| `Klaverjas.WebApp/Pages` | `Index` + `Aanmelden` (players), `Shared/_Spelers` (their layout), `Shared/_StandTabellen`; `Beheer/*` (all of Dad's pages, moved), `Beheer/Aanmelden`, `Beheer/Afdrukken/Briefjes` |
| `Klaverjas.WebApp/wwwroot` | players' page and online styles in `site.css`, slips in `afdruk.css`, `js/stand.js` |

**Next: slice 5**, email and the PDF as an optional extra (D2), then slice 6 (gembernodes: the
players' page replaces the static mock at klaverjas.gemberkoekje.nl).
