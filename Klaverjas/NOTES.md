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

## Slice 5: email and the PDF, an optional extra (2026-11-24)

Below "Online zetten", Dad can now mail the standings that are online to every member with an
address, with the PDF attached, after a test mail to himself if he likes. Built from the approved
mock; sent from your Gmail through the cluster's mail secret (Q1).

**What is there**

- **Stand → "Ook e-mailen"**: how many members get it (each their own mail), who has no address ("Print
  de PDF voor hen"), what went out this week, and the buttons "E-mailen…", "Proefmail naar mij",
  "Zo ziet de e-mail eruit" and "PDF bekijken". "E-mailen…" asks first ("Stand van week 47 mailen naar
  18 leden, met de PDF erbij?"); if exactly this stand was mailed already it warns that everyone gets
  it twice and the button reads "Toch nog een keer versturen". Afterwards: "✓ De e-mail is verstuurd
  aan 17 leden", or which members failed and why to check their address.
- **The mail** (as in the mock): "Beste {first name}," Dad's note, "Je staat 3e met … punten en …
  pitten.", the evening's winners, the month's points and pits tables in the sheet's colours, the
  link to the players' page, "Groeten, {sender}". Subject "Standen week 47", "(verbeterd)" when that
  week went out before with other numbers; the PDF is "Standen week 47.pdf". Replies go to Dad.
- **The test mail** shows Dad the number 1's mail, sent to his own address, subject "Proef: …".
- **"Zo ziet de e-mail eruit"** (`/beheer/mailvoorbeeld`): the mock's mail view with the real mail in a frame.
- **The PDF** (`?handler=Pdf`, opened in the browser): one A4 landscape like Dad's month sheet: club,
  "Stand na week N" and the date, the note, the winners, the points and pits tables side by side,
  the legend. Scaled to fit one page (26 members with long names still fit).
- **Instellingen → "De e-mail"**: sender name, "Antwoorden gaan naar" (Dad's own address; required
  before mailing, the test mail goes there too) and the subject. Members' addresses are now checked.
- **Avonden** marks a mailed evening "✉ Gemaild"; every mailing is logged (`Verzending`) per member.

**Decisions taken while building**

- **The mail carries the standings that are online, never newer ones**, so the mail and the
  players' page always agree. While a correction waits to go online, mailing is refused with "Zet
  eerst de verbeterde stand online".
- **The confirm step is a page state** (`?stap=mailen`), not a script dialog: it works without
  JavaScript and reads like the mock's dialog. The buttons then show "Bezig met versturen…".
- **One connection, one mail per member, failures recorded per member**: one bad address never
  stops the rest. The mails go out in the request (20 members take a few seconds).
- **An address needs a dot in its domain**: Qowaiv accepts `naam@voorbeeld`, which for club members
  is a typo.
- Mailing is **switched off** while the server has no sender address (or a user without its
  password); the screens say "Mailen staat nog niet aan op de server".
- The online copy's rows now carry the member's id (no personal detail), so each mail finds that
  member's place even with two similar names.
- The mail's tables are the full month, as in the mock; on a phone the mail program scrolls them.
  The PDF is the printable copy.

**Checked**

- 257 specs (28 new): the mail text (first name, place line, note encoded with its line breaks,
  names encoded, winners, tables, link, subject and "(verbeterd)", attachment names); mailing (one
  per playing member with an address, not to stopped members or substitutes, PDF attached, reply-to
  Dad, logged per member with failures; the test mail to Dad only; refused when nothing is online,
  after an unpublished correction, without Dad's address or without a mail account); the mail card
  (counts, members without address, already mailed, verbeterd); Avonden's "✉ Gemaild" (not for a
  test); the PDF fitting one page; the address checks.
- Locally with invented members and a small SMTP catcher on port 2525 (scratch script): the test
  mail and a real mailing went out (17 of 18; the one address the catcher refused was reported by
  name); every caught mail had the right From, To, Reply-To, subject, HTML, text and PDF; the
  confirm step warned before a second identical mailing; the preview and the PDF looked like the
  mock and Dad's month sheet.
- QuestPDF's Linux libraries resolve inside `mcr.microsoft.com/dotnet/aspnet:10.0` with only libc,
  libstdc++ and zlib (checked with `ldd` in the image).
- **Not checked: a real Gmail delivery.** That needs the cluster's mail secret, so it is part of
  slice 6's live check (first a test mail to Dad's address).

**Gotchas**

- A Razor `srcdoc="@html"` encodes the mail correctly for the attribute; a sandboxed frame with
  `allow-same-origin` lets the page size it to the mail.
- The browser pane treats a PDF as a download: check PDFs by fetching and rendering them (`pdftoppm`).
- Gmail rewrites From to the signed-in account anyway: `Mail:From` must be the account's own address.

**Files**

| Where | What |
|---|---|
| `Klaverjas.core/Models` | `Verzending` (+ `Ontvanger`), `Emailadres`; `Instellingen` sender, reply address, subject |
| `Klaverjas.application/Mail` | `StandMail` (subject, HTML, text, attachment name), `Ontvangers` (who gets it, what went out), `MailBericht` |
| `Klaverjas.application` | `StandMailen` (+ handler), `MailOphalenHandler` (mail card, preview, PDF), ports `IMailVerzender`, `IStandPdfMaker`, `IVerzendingStore` |
| `Klaverjas.infrastructure/Adapters` | `Mail/MailKitMailVerzender` (+ `SmtpInstellingen`), `Pdf/QuestPdfStandMaker`, `Application/MartenVerzendingStore` |
| `Klaverjas.WebApp` | `Pages/Beheer/Stand` (mail card), `Pages/Beheer/MailVoorbeeld`, Instellingen "De e-mail", `js/mailvoorbeeld.js`; `Mail:*` in appsettings, compose and `.env.example` |

**Next: slice 6**, gembernodes: the app replaces the static mock at klaverjas.gemberkoekje.nl, with
the mail secret, and a live check that starts with a test mail.

## Slice 6: live on gembernodes (2026-11-24)

The app takes over klaverjas.gemberkoekje.nl from the static mock. This slice is two pull requests:
the gembernodes manifests and [DEPLOY.md](DEPLOY.md) here. It goes live after your two steps
(database, 1Password item) and the merge.

**What is there**

- **gembernodes `apps/klaverjas`**, set up like datumprikker: one pod on the image of projects#281's merge
  (RollingUpdate with `maxSurge: 0`, so the old pod stops first; `Recreate` can't be switched to on the
  mock's existing Deployment under Flux's server-side apply), `/healthz` probes, 160Mi requested and 512Mi limit (the Excel import and the PDF
  take more than a page), `runAsNonRoot`, the `ghcr-login` pull secret Reflector already copies in.
  The Caddyfile and the mock page are gone; the Service keeps its name, so the routes still fit.
- **Two `OnePasswordItem`s**: `klaverjas-secrets` (`ConnectionStrings__Postgres`, `Auth__Password`,
  optional `Mail__User`) and the existing shared `mail-secret` (`SMTP_PASSWORD`, optional).
- **`gemberkoekje.nl/klaverjas`** now redirects to the hostname (it showed the mock): the app has
  no path base, like plotboek's short link.
- `create-database.sql` and a README with the steps, the deploy commands and the live check.

**Decisions**

- **Your Gmail address stays out of the public gembernodes repository**: it is the `Mail__User`
  field of `klaverjas-secrets` (cov-website writes it in its manifest; this app doesn't need to).
- **`Auth__Password` is Dad's first password, then yours:** once Dad has chosen his own, change it
  to one only you keep, since it keeps opening `/beheer` as the way back in.

**Live check (2026-11-24, after gembernodes#275)**

- Flux applied the merge (`flux reconcile` of `infrastructure`, `ingress` and `apps`, which were
  waiting on each other): the pod runs the slice 5 image, the Caddy mock is gone, both items came
  through with their fields, and the app started in Production without errors and with mail on.
- Public: `/healthz` ok; `/` and `/beheer` go to their own sign-in pages over https; `http` goes to
  `https`; `gemberkoekje.nl/klaverjas` (and `/klaverjas/beheer`) lands on the app; `noindex` and HSTS set.
- You, signed in: read Dad's workbook in, set the players' password and the reply address, put the
  stand after week 47 online, and sent the test mail. The log: "gemaild (proef): 1 van 1 verstuurd",
  so Gmail took it. Then you handed it to Dad: his device signed in as a player and then into
  `/beheer` (his first try there with the players' password was refused, as meant). No errors, no restarts; 141 MiB after the
  import and the PDF, well inside the 512 MiB limit.
- The data-protection warning "No XML encryptor configured" is expected: the key ring sits in the
  app's own database, as for the other apps.

**Still open**

- Change `Auth__Password` to one only you keep once Dad has chosen his own (DEPLOY.md, step 5).
- Q2 (a web address with the club's name); the slips and links follow the hostname by themselves.
- Slice 7, the hand-over. Dad has it (2026-11-24): he tries it out on December's last evenings,
  next to his workbooks, and uses it fully from the 2027 season in January. Before January he sets
  the season's first evening and the evenings without play in Instellingen; the standings start at
  0 by themselves with the first January evening (R1). His one-page guide still to write.

## Stand printen: a button of its own (2026-11-24)

Dad asked, with his week-48 sheet, whether "dit blad" can be printed with a button. The PDF already
is that sheet (slice 5), but its only button, "PDF bekijken", sat in the "Ook e-mailen" card, between
the mail buttons. Your call: give him an obvious button outside the mail.

**What changed**

- The Stand page has a **"Stand printen"** card between "Online zetten" and "Ook e-mailen", with one
  big button, "🖨 Stand printen". It opens the same PDF (`?handler=Pdf`) in a new tab, and one line
  says to click the printer at the top right or press Ctrl+P. It doesn't depend on mail being on.
- It prints **the standings online**, like the mail and the PDF always have. Before anything is
  online the card says to put it online first; after a correction, to put the corrected standings
  online first. In both cases there's no button, so the sheet never shows old figures.
- After "Zet de stand online" the green banner adds "Ook printen? Dat kan onderaan, bij Stand
  printen" with a link down to the card. Once online, the online button is greyed out ("Staat online
  ✓"), so "Stand printen" is the one obvious next button.
- In the mail card, "Print de PDF voor hen" (members without an address, failed mails) now reads
  "Print de stand voor hen (bij Stand printen)". "PDF bekijken" stays there, next to "Zo ziet de
  e-mail eruit".

**Not changed**

- The PDF itself. Next to Dad's sheet it differs only in small things: the line on top (club, week,
  note, winners), no orange bar between the two tables, the legend in a row instead of stacked,
  "Oscar (invaller)" instead of "Invaller 1 / Oscar", and "2 nov" instead of "2-11-2026". Match those
  only if he asks for it.
- The mock still shows "PDF bekijken" in the online card. The page now has its own print card.

**Checked**

- 257 specs still pass (the PDF handler is unchanged; the card is markup only).
- Locally, with the invented workbook (`StandenWerkboek`) imported through Instellingen: the card
  before going online (text only), after going online (the button and the banner link, which jumps to
  the card), after a correction (the warning, no button) and after putting it online again; the
  button's address returns `application/pdf` inline, named "Standen week 46.pdf".

**Files**

| Where | What |
|---|---|
| `Klaverjas.WebApp/Pages/Beheer/Stand.cshtml` | the "Stand printen" card, the banner link, the mail card's wording |
| `Klaverjas.WebApp/Pages/Beheer/Stand.cshtml.cs` | doc comments; the failed-mail summary points at "Stand printen" |

## Dad's first feedback, and samenspelers.gemberkoekje.nl (2026-11-25)

Dad tried it and sent eight points (with his club rules), and you moved the site to the club's name.
To understand this round, read `Klaverjas.core/Tafels/Tafelschemas.cs` (the plans as data) and
`Klaverjas.core/Tafels/Tafelschema.cs` (`Gever`, `Speler`).

**What changed, by Dad's point**

1. **Players get "Stand printen"** under the date on their page: it opens the PDF of the standings
   online (the sheet Dad mails), to print. Dad's own Stand page has had one since projects#284.
2. **No "Invaller (uit Excel)"**: the import no longer reads the workbook's nameless "invallers" row,
   and the check screen says so. Substitutes on the Spelers page get **"Weghalen"** (with a
   confirmation): the person and their results go from every evening. Dad removes the existing
   "Invaller (uit Excel)" that way. Members are still never removed (D16).
3. **A substitute shows only on evenings played**: an absence never counts for a substitute, so a
   member changed to substitute ("Wijzigen" → Invaller) leaves the ranking and drops their 4200s; the
   entry screen leaves the absence out too.
4. **The first on the score sheet writes, the second deals, the third plays**: the dealer and player
   numbers move one seat on (hand 1: dealer Zij 1, player Wij 2).
5. **The dealer and player numbers are plain**, not bold.
6. **No "Je staat …e met … punten en … pitten"** in the mail.
7. **A legend of the colours** under the points table in the mail, and right under the points table
   in the PDF (it was a small row at the bottom).
8. **The table plans are Dad's own** (D13 changed back): read from the score-sheet tabs of his
   evening workbook, repaired where the workbook slips, the missing counts made by his own rule.

Also: **samenspelers.gemberkoekje.nl** (Q2, your call): the gembernodes PR adds a Gateway listener and
redirects `klaverjas.gemberkoekje.nl` (308, path kept) and `gemberkoekje.nl/klaverjas` there. The slips
take the address from the request; its size on the slip is 15pt now, so the longer name keeps one line.

**How the plans were read** (scratch scripts, the workbook stays private)

- Each tab has one block per table per round; its two name rows are formulas `=Invulblad!H{r}`, and
  `Invulblad` row r holds seat r−1. Read row by row, a block is the order around the table, the
  writer first: [Wij 1, Zij 1, Wij 2, Zij 2].
- Clean: 4, 12, 16, 17, 20 (seat 1 typed as a name), 21, 24, 25, 26. Repaired: 8 (its tab is a copy
  of 16's; the `Tafelindeling` sheet has the plan, with seat 1 twice in round 2), 18 (round 1 points at
  the wrong column; his rule and the seats left fill it), 22 (15 twice, 9 missing), 27 (26 twice,
  `#REF!`), 28 (`#REF!` for 27 and 28).
- His extended plans all follow one rule: with s extra players, round r's slapers are (r−1)s+1..rs and
  the extras take their places; in 17, 18, 21 and 22 he also moved a player so nobody meets twice. The
  same rule makes 5, 6, 9–11, 13–15, 19 and 23, with the fewest swaps (a small beam search) so nobody
  gets the same partner twice, and from 16 up nobody sits with the same people twice. 19 is his
  16-player plan with two swaps, 23 his 20-player plan with two.
- `Tafelindeling` (the sheet) disagrees with the tabs on who partners whom (its 20-player plan), so the
  tabs win: they are what Dad prints.

**Checked**

- 267 specs: the plans for every count (everyone once a round, the slapers by his rule, no partner
  twice, from 16 up no table-mates twice), pins to his tabs (16, 17, 20, 24, 12) and to the repairs
  (8, 22, 27) and to the rule (19); writer/dealer/player; substitutes (absence never counts, the entry
  screen, removing one and their evenings, a member never removed); the import without the nameless
  row; the mail without the place line and with the legend.
- Locally with invented members: the 16-player sheet reads Wij 1/3 against Zij 2/4, hand 1 dealt by 2
  and played by 3, plain numbers, round 2 "1 & 5 tegen 9 & 13" as in his tab; a member changed to
  substitute left the ranking and kept only the evening he played; "Weghalen" asked first and removed
  the substitute; the players' "Stand printen" returned the PDF; the mail preview has the legend and no
  place line. Proofs printed through Chrome: the 16-player plan with its sheets, and the standings PDF.

**Gotchas**

- The plans are data now: never "improve" them in code. A change to a count's line changes what Dad
  prints; the pins in `Tafelschemas_specs` guard his own plans.
- In Dad's sheets the writer is the first name, not the first dealer: a sheet read row by row is the
  order around the table.

**Files**

| Where | What |
|---|---|
| `Klaverjas.core/Tafels/Tafelschemas.cs` | the plans as data, read into `Tafelschema`; no search any more |
| `Klaverjas.core/Tafels/Tafelschema.cs` | `Gever`/`Speler` one seat on; who writes |
| `Klaverjas.core/Standen/StandBerekening.cs` | a substitute's absence is no cell |
| `Klaverjas.application` | `SpelerWeghalen` (+ handler), `ISpelerStore.WeghalenAsync`; `ImportPlanner` without the nameless row; `UitslagOphalen` leaves a substitute's absence out; `StandMail` without the place line, with the legend |
| `Klaverjas.infrastructure` | `MartenSpelerStore.WeghalenAsync`; the PDF's legend under the points table |
| `Klaverjas.WebApp` | the players' "Stand printen" (`/?handler=Pdf`); Spelers "Weghalen"; Inlezen's text; plain dealer numbers and the slip's address size in `afdruk.css` |

**Live check of the new address (2026-11-25, after gembernodes#278)**

- Flux applied the merge; the pod runs `32684dcf` (this round), no errors. `samenspelers.gemberkoekje.nl`: `/healthz`
  ok, a Let's Encrypt certificate (`samenspelers-tls`), the players' sign-in, `noindex` and HSTS.
  `klaverjas.gemberkoekje.nl` redirects 308 on http and https with the path and `ReturnUrl` kept;
  `gemberkoekje.nl/klaverjas` 301, `/klaverjas/beheer` to `/beheer`.

## Dad's second feedback: the card list and the slips (2026-11-26)

To understand this, read `Klaverjas.core/Models/Voorbereiding.cs` (`HeeftKaartje`) and
`Klaverjas.application/Queries/VoorbereidingOphalenHandler.cs`.

**What Dad found, and why**

- **The slips broke the address**: "samenspelers.gemberkoekje" with ".nl" on the next line, confusing
  for older players. The longer name no longer fitted at 15pt and the line was allowed to break
  anywhere. Now it never breaks (`white-space: nowrap`), at 14pt with a little less side padding;
  measured in the printed PDF it ends some 40pt before the slip's edge.
- **"Wie krijgt een kaartje?" showed 19 of the 20 members.** The live data (read with ids and counts
  only) had all 20 members active, but the saved preparation of 30 November (saved 25 November
  13:08) held 19 of today's members plus one player who has been a substitute since (the players
  were changed at 22:16). The screen started from the saved ticks, which listed only who got a
  card: so one current member showed unticked, and the card of the new substitute was invisible but
  still counted ("20 kaartjes met naam"). Not a browser or server cache: the saved choice did not
  follow later changes to who is a member.
- **Substitutes were not in the list**: only blank cards for them.

**What changed**

- A preparation also keeps the members Dad **unticked** (`ZonderKaartje`). Who has a card is worked out
  from the players as they are now: ticked, or a member Dad never unticked (so a new member, or one
  who became a member after saving, gets a card); a substitute only when ticked. For the saved 30
  November preparation that means the missing member gets a card again, and the player who became a
  substitute keeps the card Dad gave him (now shown, ticked, among the substitutes).
- The list has the members, then **"Invallers"**, unticked: a ticked substitute gets **the same card**
  as a member (first name, nothing that says invaller), counts towards the expected players, and is
  already on that evening's entry screen among the substitutes. "Lege kaartjes voor andere invallers"
  stays for the ones he doesn't know beforehand.
- **Slapers stay members**: a substitute with a card is never a slaper, and "Eerlijk voorstel" only
  proposes members (it works from this season's sit-outs, which substitutes don't have).

**Checked**

- 274 specs (7 new): substitutes listed but unticked; a member since saving gets a card; an unticked
  member stays unticked; someone who became a substitute keeps the card; a substitute's card equals a
  member's; a ticked substitute is on the entry screen; a substitute is never a slaper.
- Locally with invented players: 30 November's list showed the 20 members ticked and the two
  substitutes unticked under "Invallers" (counter 20); with Bert ticked, 21 cards, his reading
  "Bert"; Bert on that evening's entry screen. The slips printed through Chrome with the real
  address: one line, ten on one A4.

**Files**

| Where | What |
|---|---|
| `Klaverjas.core/Models/Voorbereiding.cs` | `ZonderKaartje`, `HeeftKaartje` |
| `Klaverjas.application` | `VoorbereidingOphalen` (cards as the players are now; `Invallers`), `VoorbereidingOpslaan` (substitutes may have a card; stores who was unticked; slapers members only), `KaartjesOphalen` (substitutes' cards), `UitslagOphalen` (ticked substitutes on a new evening) |
| `Klaverjas.WebApp` | `Beheer/Voorbereiden` (the "Invallers" part of the list), `afdruk.css` (the slip's address) |
