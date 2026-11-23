# Klaverjas — plan

_To understand this, start with this file, then open [mock/index.html](mock/index.html) in a browser
(a clickable mock: nothing is saved, printed or sent). The mock uses invented member names with the
week-47 standings' real numbers, because this repository is public. What Dad's two workbooks do,
sheet by sheet, is in `explainer/index.html` (git-ignored: personal reference)._

**Status (2026-11-23):** phase 0, the plan and the mock. The mock follows Dad's two workbooks
(received 2026-11-21) and, since 2026-11-23, puts the online standings first. **Dad has seen all the
pages and approved them** (2026-11-23), and **answered the question sheet**
[vragenblad/vragenblad.pdf](vragenblad/vragenblad.pdf) the same day (see "Dad's answers" below).
His answers changed the slapers (he picks them beforehand, D12) and a few rules (R16–R19); the mock
follows them. Nothing is built yet. One question is left before slice 1: **Q17**, when the lowest
evening of a quarter is dropped.

## What it is

Dad runs the klaverjas competition of **"De Samenspelers"** (he wrote it on both proof prints,
2026-11-23; his workbook also has "Ons Genoegen", which stays out): about 20 members, an evening every Monday,
a year-long ranking on points, and a separate ranking on pits. Today he uses two Excel workbooks
and mails a PDF of the standings. The app takes over both workbooks:

- **Before the evening:** the score cards with names (the numbers are drawn on the evening itself),
  and the table plan and table score sheets by seat number.
- **After the evening:** he types each player's card total and pits, checks the standings and
  presses one button. The standings are then online for every player at klaverjas.gemberkoekje.nl,
  behind the club's password. Email is an optional extra.

Goals:

- Easy for a 70-year-old on a PC: big text, plain words, one obvious next button, no hidden menus.
- Everything changeable by Dad himself: members, substitutes, old evenings, the scores for
  absence and sitting out, the evenings without play, the mail text, the colours.
- **The main result is the standings online.** That's Dad's main wish: emails disappear, land in spam
  or go to wrong addresses. Email (with the PDF) stays possible as an extra, and the PDF can be printed.
- Hosted on gembernodes, like the other apps.

## What Dad's workbooks do today (studied 2026-11-21)

**"invul blad totaal.xlsm"** (the evening). Its macros are empty; the buttons are links and
everything else is formulas.

- **Invulblad:** one row per person (members and known substitutes) with a status: Speler,
  Slaper1–9 or Afwezig. Random numbers are drawn, and slapers get the lowest numbers.
- **Table plans** for 4 to 28 players (sheets like `16.1`, `20.1`, `Tafelindeling`). There are
  3 rounds, and seat numbers decide who sits where.
- **Score cards** printed per player (`PK bl-1`, "leeg kaartje"): club, name, date, slaper round,
  and points plus pits per round with a total.
- **`slapers`:** how often each member has sat out (so it stays fair).
- **Member lists** for De Samenspelers and for **"Ons Genoegen"** (a second club, Bennekom), with
  addresses, phone numbers, birthdays and email addresses.
- **Spelregels:** the club's rules, including the scoring rules below.
- **Other sheets:** club money (`Rekening`, `uitgave zieken`, `Kerst uitgave`).

**"Standen week 45.xlsx"** (the standings).

- **`Stand`:** points per member per week for the whole year. It has quarterly subtotals, a
  "Laagste waarde vervalt" column per quarter, an "invallers" row, and text in the weeks without
  play ("Pasen", "vier mei", "Pinksteren").
- **`pit`:** the same for pits, with a "Week totalen" row.
- **One sheet per month** (`Januarie` … `December`): the month's weeks next to the year totals.
  This sheet is the PDF that is mailed.

## The rules (confirmed by you, 2026-11-20; R10–R15 from the workbooks; R16–R19 Dad's answers, 2026-11-23)

- **R1** A season is a calendar year ("Punten 2026"). The ranking is on the total of all evenings' points.
- **R2** Per evening, each player has a points total and a number of pits, taken from the score card.
- **R3** A member who skips an evening gets a fixed **4200** points and no pits. The cell is green.
  Green comes from the status, not the number: someone who really scores exactly 4200 is **not**
  green (Dad, 2026-11-23). The workbook turns any 4200 green, so Dad typed a played 4200 as 4199 or
  4201. Those values are imported as typed: a real 4199 is possible too, and Dad can correct any himself.
- **R4** Substitutes are listed under the table. A substitute can take an evening's 1st–3rd place
  but is not in the year ranking or the pits ranking.
- **R5** Colours: in each week column the evening's top 3 points are yellow, pink and blue. In the
  Totaal column, places 1–3 get the same colours and everyone else light green. Ties share a place
  (1, 2, 2, 4).
- **R6** "Verschil met vorige" is the gap to the player directly above, and 0 for the leader.
- **R7** The pits ranking uses the same tie rule. "Week totalen" sums the members' pits per week;
  substitutes' pits don't count (Dad, Q3).
- **R8** Each mail shows the weeks (Mondays) of the current month next to the year totals.
- **R9** There is one member list, so one spelling. Today the two sheets spell some names differently.
- **R10** An evening is **3 rounds ("partijen") of 16 hands**. You never sit with the same people
  twice in one evening.
- **R11** When the number of players isn't a multiple of 4, some sit out a round ("slapen"):
  `players mod 4` per round, so 3, 6 or 9 in an evening. Each sits out exactly one round and gets
  **1600 points** for it. The slaper writes the 1600 on the card, so **the card total already
  includes it** and the app adds nothing (Dad, Q9).
- **R12** **The numbers are drawn on the evening itself**, at the door, so nobody can get the
  impression of a fixed draw (Dad, 2026-11-23). Each player writes the drawn number on their card. The
  table plan for that number of players says which seat number sits where in each round.
  **Only the slapers know beforehand.** Dad picks them himself, by turns, so sitting out is shared
  fairly over the evenings (Q10 and his note). Their card says **"Slaper 1"**, **"Slaper 2"** or
  **"Slaper 3"** in the number box: the round they sit out, as on his `PK bl-1` sheet. They don't
  draw; they get the lowest numbers, the ones the plan has sitting out (with one per round, Slaper 1
  is number 1; see Q18 for two or three per round).
- **R13** Some Mondays have no play (2026: Easter Monday, 4 May, Whit Monday). The season starts in
  week 3 (12 January 2026).
- **R14** Substitutes are known people kept in the same list as the members (status, not ranking).
- **R15** After the cards are handed in, nothing can be changed by the players. Dad can still
  correct his own typing.
- **R16** **Each quarter, a member's lowest evening doesn't count** (Dad, Q8). The workbook finds it
  per quarter ("Laagste waarde vervalt") and colours it, but never subtracted it. An absence (4200)
  can be the lowest. _When_ it drops, every week or only in the Eindstand, is Q17.
- **R17** A member who **joins** during the year gets 4200 for every evening of the season before
  they joined, as if absent (Dad, Q5).
- **R18** A member who **stops** during the year goes out of the standings (Dad, Q4). The scores stay
  stored and it can be undone (D16).
- **R19** In December there's a separate **Eindstand**, and January starts a new season at 0 (Dad, Q6).

**What the workbooks get wrong today** (the app removes these by computing everything from one source):

- In the November sheet, two members' week-47 pits are typed as blanks instead of linked. So the
  PDF showed no pits for them, though the `pit` sheet has them.
- The "Week totalen" formulas add up different rows from week to week (some skip the first member,
  some the last two). Week 46 shows 27 where the members' pits add up to 28. Week 47 shows 35, but it is 36.
- The pits ranking formulas use different ranges per row.

## Decisions

- **D1** After the evening Dad types **each player's card total plus the pits**. Players are listed
  alphabetically. Each row has a **status: Speelt / Slaper / Afwezig**, in the workbook's colours
  (green, yellow, light red). Everyone starts as Speelt, except the slapers Dad picked when printing
  the cards (D12), who start as Slaper in their round; he clicks the absentees (and any change) as he
  goes. For a slaper he types the card total like for everyone, since it already includes the 1600
  (R11). For someone absent, the app fills in 4200. A played total of exactly 4200 gets the note
  "wordt niet groen" (R3). Enter or Tab moves to the next box. Unusual numbers get a "Klopt dit?"
  note, but they are never blocked. _(Your call on the total, 2026-11-20; Dad's answer Q9, 2026-11-23.)_
- **D2** **Online first.** After checking, Dad presses "Zet de stand online" and the players' page shows
  the new standings at once. After a correction the button reads "Verbeterde stand online zetten".
  **Email is an optional extra**: one mail per member with the standings and the PDF, a test mail
  first, replies going to Dad. _(Your call on email, 2026-11-20; online first, 2026-11-23.)_
- **D3** The screens are designed for a **PC or laptop** with a mouse and keyboard. _(Your call.)_
- **D4** The 2026 weeks are **imported from "Standen"** (`Stand` + `pit`). Holiday text becomes
  evenings without play, and the "invallers" row becomes substitutes. Names are matched across the
  two sheets despite the spelling differences, with a check that the totals equal the workbook's.
  _(Your call; layout now known.)_
- **D5** **The players' page is the main output.** It lives at **klaverjas.gemberkoekje.nl**,
  read-only, behind **one players' password**. Dad hands out **printed slips** with the address and
  the password (Instellingen → "Briefjes printen", 10 per A4; Dad, Q13). The phone or computer
  remembers it. The page is `noindex` and uses full names, as on the PDF. It shows Dad's note, the
  evening's winners and the points and pits rankings, plus the whole month on a computer. A small
  "Beheer" link at the bottom leads to **/beheer**. _(Your calls, 2026-11-23.)_
- **D6** Stack: one ASP.NET Core Razor Pages app on the Kluskat/DatumPrikker template, with Marten
  documents on the cluster's Postgres, Qowaiv `Result`, MailKit for SMTP and QuestPDF for the PDF,
  the score cards and the table plan.
- **D7** **Two passwords**, each with a 365-day sliding cookie like Kluskat: **Dad's** for /beheer and
  the **players'** password for the standings. Both can be changed in Instellingen. _(Your call, 2026-11-23.)_
- **D8** Data: `Speler` (member or substitute), `Avond` (date, per person status/slaper
  round/points/pits, who got a card), `Instellingen`, `Verzending`. Standings are **computed, never
  stored**, and each save of an evening keeps the previous version.
- **D9** **Real club data never goes into git.** The repository is public and the workbook holds
  addresses, phone numbers and birthdays. The import runs against the database, and the tests and
  the mock use invented names.
- **D10** Settings: club name, absence score, rounds per evening, play day, first evening of the season, evenings without play, sender name, reply-to, mail subject,
  the colours, the players' password, Dad's password. (No score for a round sat out: the app never
  adds the 1600, R11.)
- **D11** A player who stops is set to "Gestopt": kept in old weeks, off the preparation and entry
  screens, and by default out of the standings (R18, D16).
- **D12** **"Kaartjes en tafelindeling"** replaces the Invulblad and the table sheets. **The app
  does not draw**: the numbers are drawn on the evening itself (R12, Dad's wish against any
  impression of false play). _(2026-11-23.)_
  - **Kaartjes printen:** a card for every active member (Dad unticks anyone he knows won't come),
    plus a few blank ones for substitutes, with names and date but **no number**.
  - **Wie slaapt er?** (Dad's answer Q10 and his note, 2026-11-23.) Before printing, Dad picks the
    slapers. The expected number of players (by default the ticked members) gives how many per round.
    Per round he picks the names; each name shows how often that member sat out this season (this
    replaces the `slapers` sheet). **"Eerlijk voorstel"** fills the empty places with those who sat
    out least, and he can change any of it. Their cards print "Slaper 1/2/3" (R12). Fewer picked
    than needed is allowed (a note says so); he can write it on a card himself.
  - **Tafelindeling op nummer:** pick the number of players and print that plan (seat numbers per
    round and table, and which numbers sit out), or a booklet for every number from 4 to 28.
  - **Deze tafelindeling printen** gives the overview plus the table score sheets for that number of
    players (D18).
  - Absence is set while entering the result (D1). The slapers come pre-set from here, and Dad can
    still change them there if the evening went differently.
- **D13** The table plans come from **Dad's own schemes** in the workbook (4–28 players), imported
  once, so the evening works exactly as the club is used to. The mock computes a plan instead. With
  16 or more players nobody sits with the same people twice. With fewer (only 3 tables a round)
  that's impossible, so then nobody gets the same partner twice. Dad's 12-player scheme also repeats.
- **D14** Spelers holds members and substitutes (type Lid / Invaller) with email, phone, address
  and birthday, plus a printable member list (replacing the "Leden" sheet).
- **D15** The app prints **the personal score cards in Dad's own layout**, 12 per A4 (3 across,
  4 down). The card is one bordered table of 8 rows of equal height: the club name with an **empty
  cell top right for the seat number** (for a slaper it says "Slaper 1/2/3", R12), the **first name**
  large across the whole width (Dad, Q16; the full name when two active members share a first name),
  Datum, a header row with "Ronde" (written upwards), "Punten:" and "Pitten", rounds 1–3, and Totaal.
  Name and date are printed; the number is written in after the draw (R12). **No address or QR code** on the cards
  (Dad crossed them out). _(Dad, from his own card and his pen notes, 2026-11-23.)_
- **D18** It also prints **the table score sheets**, like Dad's `16p4ta`-style sheets: one per table per
  round for the chosen number of players, **two A5 forms per A4, cut in half**, printed together with the overview by **"Deze
  tafelindeling printen"** (your call, 2026-11-23). Each sheet has a header (round, club, date,
  week, number of tables; no table number) and the pairs: Wij on the left, Zij on the right, **each player on their own row** with the seat
  number printed and a line behind it for the name, which players always write in themselves. A **big
  arrow** links each pair, because some players otherwise think they play with the person across the
  sheet. _(Dad, 2026-11-23.)_ Down the left is the
  0 – 162 split strip. The grid has hands 1–16 with points and roem for both pairs, the **Gever** (dealer) and
  **Speler** (who must play) per hand, filled in by seat number. No suit symbols, and the Spel column
  is narrow: Punten wider, Roem narrower (Dad's pen notes, 2026-11-23). Dealing goes clockwise with partners opposite each other (first Wij, first Zij, second
  Wij, second Zij), starting with the first Wij number. The Speler is the one to the dealer's left. _(Your call,
  2026-11-23; Dad confirmed it, Q15.)_ After every 4 hands there's a subtotal row to fill in (a running total), with the
  **control number** across the Gever and Speler columns: what both pairs' points (without roem) must
  add up to so far, 648, 1296, 1944 and 2592, as on Dad's sheets and placemat. At
  the bottom, per pair: totaal punten, totaal roem, eindtotaal and pitten. _(Dad's wish, 2026-11-23.)_

## Lessons from samenspelers.nl (your answer, 2026-11-23)

You built samenspelers.nl in 2017 with season files, a players' page and an admin area. It fell out of
use for two reasons. Dad stopped being the club's chair and someone else took the site over. And every
change Dad wanted went through you, above all removing players who had died: that meant deleting their
scores, and there was never a proper fix for it. So:

- **D16** **Dad takes players out himself, and nothing is ever deleted.** On Spelers he sets
  someone to "Gestopt": off the preparation and entry screens and, by default, **out of the stand**
  (R18). That takes them out of this season's points and pits rankings, while their scores stay
  stored in the evenings. He can pick "Blijft in de stand" instead, and it can always be undone. Wording on screen stays gentle ("bijvoorbeeld als
  iemand is overleden").
- **D17** **Nothing routine needs you.** Players, names, statuses, old evenings, seasons, evenings
  without play, settings and both passwords are all in /beheer. There is an **export** of all scores
  (Excel/CSV) so the data is never trapped, and the passwords can be handed over if someone else
  takes over the competition. The code needs you only for real changes to the app.

## Open questions

- **Q1 (yours)** Sender address for the optional email: your Gmail via the cluster's mail secret, or a
  separate club address? (Less urgent now that the standings are online first.)
- **Q2 (yours)** The web address: `klaverjas.gemberkoekje.nl` for now, or one with the club's name (De Samenspelers)?
- **Q17 (Dad, blocks slice 1)** **When does the lowest evening of a quarter drop out (R16)?** Every
  week already (the lowest so far in each quarter, so the standings during the year are lower than
  today's), or only in the Eindstand in December (the weekly standings stay as today)? And only for
  the points, or for the pits too?
- **Q18 (Dad, slice 3)** With two or three slapers per round, two or three cards say "Slaper 1". Do
  they simply share the lowest numbers at the door (with 18 players: numbers 1 and 2), or should each
  card carry its own number?

## Dad's answers (question sheet, 2026-11-23)

Sheet number → question → answer:

1. Q8 "Laagste waarde vervalt": **per quarter the lowest evening drops** (R16; when: Q17).
2. Q5 New member: **4200 per evening missed** (R17).
3. Q4 Member stops: **out of the stand** (R18, D16).
4. Q9 Slaper: **the card total already includes the 1600** (R11). His note: nothing needs to be
   computed; the slapers get their "number" beforehand ("Slaper 1", "Slaper 2" … in the box), so they
   know which round they sit out, and he shares sitting out fairly over the evenings (R12, D12).
5. Q3 Substitutes' pits in Week totalen: **no, as now** (R7).
6. Q6 **Eindstand in December: yes; January starts at 0: yes** (R19).
7. Q16 Name on the card: **first name only** (D15).
8. Q10 Who sits out: **Dad decides** (by turns) (R12, D12).
9. Q15 Gever/Speler on the table sheet: **correct** (D18).
10. Q13 Address and password: **a slip to hand out** (D5).
11. Q11 4199/4201: not ticked; his note says absence is green and a real 4200 is not, so they were
    played 4200s typed around the green rule (R3).
12. Q12 Club money: **stays in Excel**.

## Slices (one PR each; I stop after each for review)

0. **Plan + mock** (this). The mock is approved (Dad, 2026-11-23).
1. **Core app.** Players (members and substitutes), uitslag invullen with statuses, standings,
   Instellingen (incl. evenings without play), sign-in and local `docker-compose`. Specs cover
   R1–R19: ties, absence (green by status, not by value), slapers, substitutes in the week podium
   only, the gap column, the month columns, the pits totals, the lowest evening per quarter (as Q17
   decides), joining and stopping mid-season, and the season boundary.
2. **Import 2026 from "Standen", and an export of all scores (D17).** The app's totals after the latest week must match the workbook's,
   and any differences are listed (like the week-46/47 pits totals).
3. **Kaartjes en tafelindeling.** "Wie slaapt er?" with the fair proposal (D12), printing the name
   cards without numbers but with "Slaper 1/2/3" (D15), and Dad's table schemes by seat number (D13),
   one or all numbers of players, with the table score sheets (D18).
4. **Players' page** at klaverjas.gemberkoekje.nl with the players' password, the handout slips (D5),
   "Zet de stand online", and the /beheer split. This is Dad's main wish, so it comes
   before email.
5. **Email + PDF (optional extra).** One HTML mail per member, the PDF like today's month sheet, the
   test mail, the send log and the list of members without email.
6. **gembernodes.** Manifests, 1Password items, `DEPLOY.md` and a live check.
7. **Hand-over.** For two Mondays the app runs alongside the workbooks and the results are compared.
   Dad gets a one-page printed guide, and I fix whatever he trips over.

## Screens (see the mock)

- **Avonden:** the next evening with "Kaartjes en tafels printen" before and "Uitslag invullen" after,
  then every evening with its mail status, including evenings without play.
- **Kaartjes en tafelindeling:** who gets a card (all active members ticked), blank cards for
  substitutes, "Wie slaapt er?" (expected number of players, names per round, "Eerlijk voorstel"),
  "Kaartjes printen", then the plan by seat number for a chosen number of players with
  "Deze tafelindeling printen" (overview plus table score sheets) / "Alle aantallen printen".
- **Uitslag invullen:** status (slapers pre-set), points and pits per player, the substitutes, and
  the counts with "Opslaan en stand bekijken".
- **Stand online:** the standings as players will see them, the note, "Zet de stand online",
  optionally "Ook e-mailen", and the PDF.
- **Spelers:** members and substitutes, with more details under "Wijzigen", and "Ledenlijst printen".
  Adding a member mid-season says how many evenings get 4200 (R17).
- **Instellingen:** every setting from D10, and "Briefjes printen" for the players' slips (D5).

Also in the mock: the players' page on a phone (club password first), the email, and the /beheer sign-in.

## Risks

- Gmail's sending limit (500 a day) is far above 20 mails a week. A club address (Q1) looks more trustworthy.
- If Dad forgets the password, you reset it through 1Password. There is no email reset flow on purpose.
- Dad's table schemes are in odd layouts (one sheet per player count). Importing them needs care
  and a check that nobody meets twice. The mock's computed plan is the fallback.
