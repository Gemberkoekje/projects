# Klaverjas — plan

_To understand this, start with this file, then open [mock/index.html](mock/index.html) in a browser
(a clickable mock: nothing is saved, printed or sent). The mock uses invented member names with the
week-47 standings' real numbers, because this repository is public. What Dad's two workbooks do,
sheet by sheet, is in `explainer/index.html` (git-ignored: personal reference)._

**Status (2026-11-24):** **slices 1 to 6 are done**: sign-in, players, entering an evening, the
standings, reading Dad's "Standen" workbook (every points total matches it), downloading every score,
preparing an evening (cards, slapers, table plan and score sheets), the players' page with "Zet de
stand online", the slips and both passwords (Dad's admin now lives at /beheer), and the optional email
with the PDF. What they do, the decisions taken while building and the gotchas are in
[NOTES.md](NOTES.md). **It is live** since 2026-11-24 (slice 6, [DEPLOY.md](DEPLOY.md)), at
https://samenspelers.gemberkoekje.nl since 2026-11-25 (the first address redirects). Slice 7, the
hand-over, is under way: Dad tries it in December, and his first feedback is in (NOTES.md). Before that: the plan
and the mock, which **Dad approved** (2026-11-23), and his answers to the question sheet
[vragenblad/vragenblad.pdf](vragenblad/vragenblad.pdf) (see "Dad's answers" below), which changed
the slapers (he picks them beforehand and they get the numbers 1, 2, 3 …, D12) and added R16–R19.
Q17 (does the Eindstand drop the quarters' lowest evenings?) only matters for the Eindstand.

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
  fairly over the evenings (Q10 and his note). **They get the numbers 1, 2, 3 …**, as many as that
  evening needs (3, 6 or 9), and **the other numbers are drawn from a pot** (Dad, Q18). Their card
  has **their number printed** in the number box, and in the round they sit out **1600** under
  Punten and **"Slaper 1"**, **"Slaper 2"** … (in red) under Pitten, so they know it beforehand and the
  card total includes the 1600 by itself (Dad's own card sheet, 2026-11-23). The
  table plan puts those lowest numbers in the sit-out places and says which round each sits out.
- **R13** Some Mondays have no play (2026: Easter Monday, 4 May, Whit Monday). The season starts in
  week 3 (12 January 2026).
- **R14** Substitutes are known people kept in the same list as the members (status, not ranking).
- **R15** After the cards are handed in, nothing can be changed by the players. Dad can still
  correct his own typing.
- **R16** **The lowest evening per quarter** (Dad, Q8 and Q17). The workbook finds each member's
  lowest evening per quarter (the "vervalt" column, shown as a minus and coloured), but the weekly
  Totaal is the plain sum of every evening: checked for all 20 members in "Standen week 45". Dad: "it
  worked correctly in the sheet". So **the weekly standings count every evening**, and the quarter's
  lowest is only marked. An absence (4200) can be the lowest. His tick "per kwartaal vervalt de
  laagste avond" can then only take effect in the Eindstand (R19): Q17.
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
  first, replies going to Dad. _(Your call on email, 2026-11-20; online first, 2026-11-23.)_ As built
  (slice 5): the mail always carries the standings that are online, so it is refused while a
  correction waits to go online; it is sent from your Gmail (Q1) with Dad's address as reply-to.
- **D3** The screens are designed for a **PC or laptop** with a mouse and keyboard. _(Your call.)_
- **D4** The 2026 weeks are **imported from "Standen"** (`Stand` + `pit`). Holiday text becomes
  evenings without play, and the "invallers" row becomes substitutes. Names are matched across the
  two sheets despite the spelling differences, with a check that the totals equal the workbook's.
  _(Your call; layout now known.)_
- **D5** **The players' page is the main output.** It lives at **samenspelers.gemberkoekje.nl** (Q2;
  first klaverjas.gemberkoekje.nl, which redirects),
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
  As built (slice 4): the players' page stays closed until Dad chooses the players' password; capitals
  and spaces at the ends do not matter for it. Dad's own password is stored as a hash; until he chooses
  one, the configured `Auth:Password` is his, and it keeps working afterwards as the site keeper's way
  back in. Changing either password signs every other phone or computer out of that door.
- **D8** Data: `Speler` (member or substitute), `Avond` (date, per person status/slaper
  round/points/pits, who got a card), `Instellingen`, `Verzending`. Standings are **computed, never
  stored**, and each save of an evening keeps the previous version. The one exception is
  `OnlineStand` (slice 4): the copy of the standings Dad put online, names and numbers only, so a
  correction reaches the players only when he puts it online again (D2).
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
  - **Wie slaapt er?** (Dad's answers Q10 and Q18 and his note, 2026-11-23.) Before printing, Dad
    picks the slapers. The expected number of players (by default the ticked members) gives how many:
    one row per slaper, "Slaper 1" to "Slaper k", each with its number and the round it sits out
    (from the table plan). Each name shows how often that member sat out this season (this replaces
    the `slapers` sheet). **"Eerlijk voorstel"** fills the empty places with those who sat out least,
    and he can change any of it. Their cards print the number and "Slaper n" (R12). Fewer picked than needed is
    allowed (a note says so); he can write it on a card himself.
  - **Tafelindeling op nummer:** pick the number of players and print that plan (seat numbers per
    round and table, and which numbers sit out), or a booklet for every number from 4 to 28.
  - **Deze tafelindeling printen** gives the overview plus the table score sheets for that number of
    players (D18).
  - Absence is set while entering the result (D1). The slapers come pre-set from here, and Dad can
    still change them there if the evening went differently.
- **D13** The table plans are **Dad's own**, from the score-sheet tabs of his evening workbook (`16p4ta`
  and so on): the names there are formulas into the draw list, and those references give the seat
  numbers. (Changed back 2026-11-25, Dad: "hou die tafelindelingen zo veel mogelijk aan"; from
  2026-11-23 they were computed with the mock's search.) The workbook's slips (a seat twice, `#REF!`,
  a tab copied from another) are repaired with the one seat missing in that round. The numbers without
  a tab (5, 6, 9–11, 13–15, 19, 23) follow his own rule: with s extra players, round r's slapers are
  numbers (r−1)s+1 to rs and the extras take their places, plus the fewest swaps so nobody gets the same
  partner twice (from 16 players up, nobody sits with the same people twice either). Seven players has
  no plan. Below 16 players some people meet twice, as in his own 12-player plan.
- **D14** Spelers holds members and substitutes (type Lid / Invaller) with email, phone, address
  and birthday, plus a printable member list (replacing the "Leden" sheet).
- **D15** The app prints **the personal score cards in Dad's own layout**, 12 per A4 (3 across,
  4 down). The card is one bordered table of 8 rows of equal height: the club name with an **empty
  cell top right for the seat number** (a slaper's number is printed there, centred, with 1600 and a
  red "Slaper n" in the round sat out, R12), the **first name**
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
  Wij, second Zij): the sheet's pairs read row by row. **The first writes, the second deals hand 1, the
  third plays it** (Dad, 2026-11-25; hand 1 used to start with the first Wij), then clockwise. The Speler is
  the one to the dealer's left (Dad, Q15). The dealer and player numbers are plain, not bold (Dad). After every 4 hands there's a subtotal row to fill in (a running total), with the
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

- **Q19 (Dad, not blocking)** **4 May**: the pits sheet has 9 pits that evening, but the points sheet
  says "vier mei" (no play). Was there play? The import leaves those pits out; if there was play, Dad
  can enter the evening himself.
- **Q17 (Dad, before the Eindstand is built; not blocking)** The weekly standings count every evening,
  as the sheet does (R16). **In the Eindstand in December, are the four lowest evenings (one per
  quarter) taken off?** Or is "vervalt" only a marking? (Points only; the pits sheet has no such column.)

## Dad's answers (question sheet, 2026-11-23)

Sheet number → question → answer:

1. Q8 "Laagste waarde vervalt": **per quarter the lowest evening drops** (R16; when: Q17).
2. Q5 New member: **4200 per evening missed** (R17).
3. Q4 Member stops: **out of the stand** (R18, D16).
4. Q9 Slaper: **the card total already includes the 1600** (R11). His note: nothing needs to be
   computed; the slapers get their number beforehand ("Slaper 1", "Slaper 2" … in the Pitten box), so they
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

Follow-ups, relayed by you the same day:

- Q17 When the quarter's lowest drops: "**it worked correctly in the sheet**". The sheet never
  subtracts it from the weekly total (R16); what the Eindstand does is the narrowed Q17.
- Q18 Slaper numbers: **the slapers get the numbers 1, 2, 3 …** (as many as needed that evening), and
  the rest of the numbers are drawn from a pot (R12).

## Slices (one PR each; I stop after each for review)

0. **Plan + mock.** The mock is approved (Dad, 2026-11-23). _Done (merged)._
1. **Core app.** Players (members and substitutes), uitslag invullen with statuses, standings,
   Instellingen (incl. evenings without play), sign-in and local `docker-compose`. Specs cover
   R1–R19: ties, absence (green by status, not by value), slapers, substitutes in the week podium
   only, the gap column, the month columns, the pits totals, the weekly total counting every evening
   with each quarter's lowest marked (R16), joining and stopping mid-season, and the season boundary.
   _Done (merged, projects#276). Dad's password comes from configuration for now; changing it in
   Instellingen comes with the players' password in slice 4._
2. **Import 2026 from "Standen", and an export of all scores (D17).** _Done (merged, projects#277)._ The app's totals after the latest week must match the workbook's,
   and any differences are listed (like the week-46/47 pits totals).
3. **Kaartjes en tafelindeling.** "Wie slaapt er?" with the fair proposal (D12), printing the name
   cards without numbers, except the slapers' (D15), and Dad's table schemes by seat number (D13),
   one or all numbers of players, with the table score sheets (D18). _Built 2026-11-23 (see NOTES.md)._
4. **Players' page** at klaverjas.gemberkoekje.nl with the players' password, the handout slips (D5),
   "Zet de stand online", and the /beheer split. This is Dad's main wish, so it comes
   before email. _Built 2026-11-23 (see NOTES.md)._
5. **Email + PDF (optional extra).** One HTML mail per member, the PDF like today's month sheet, the
   test mail, the send log and the list of members without email. Sent from your Gmail through the
   cluster's mail secret (Q1, your answer 2026-11-24). _Built 2026-11-24 (see NOTES.md)._
6. **gembernodes.** Manifests, 1Password items, `DEPLOY.md` and a live check. _Live since
   2026-11-24 (gembernodes#275, projects#282; see [DEPLOY.md](DEPLOY.md)); the test mail went out
   through Gmail._
7. **Hand-over.** For two Mondays the app runs alongside the workbooks and the results are compared.
   Dad gets a one-page printed guide, and I fix whatever he trips over. _Started 2026-11-24: Dad has
   it and tries it on December's last evenings next to his workbooks, then uses it fully from the
   2027 season in January._

## Screens (see the mock)

- **Avonden:** the next evening with "Kaartjes en tafels printen" before and "Uitslag invullen" after,
  then every evening with its mail status, including evenings without play.
- **Kaartjes en tafelindeling:** who gets a card (all active members ticked), blank cards for
  substitutes, "Wie slaapt er?" (expected number of players, a name per slaper number, "Eerlijk voorstel"),
  "Kaartjes printen", then the plan by seat number for a chosen number of players with
  "Deze tafelindeling printen" (overview plus table score sheets) / "Alle aantallen printen".
- **Uitslag invullen:** status (slapers pre-set), points and pits per player, the substitutes, and
  the counts with "Opslaan en stand bekijken".
- **Stand online:** the standings as players will see them, the note, "Zet de stand online",
  "Stand printen" (the PDF, whether or not mail is on), and optionally "Ook e-mailen".
- **Spelers:** members and substitutes, with more details under "Wijzigen", and "Ledenlijst printen".
  Adding a member mid-season says how many evenings get 4200 (R17).
- **Instellingen:** every setting from D10, and "Briefjes printen" for the players' slips (D5).

Also in the mock: the players' page on a phone (club password first), the email, and the /beheer sign-in.

## Risks

- Gmail's sending limit (500 a day) is far above 20 mails a week. A club address (Q1) looks more trustworthy.
- If Dad forgets the password, you reset it through 1Password. There is no email reset flow on purpose.
- Dad's table schemes are in odd layouts (one sheet per player count). Importing them needs care
  and a check that nobody meets twice. The mock's computed plan is the fallback.
