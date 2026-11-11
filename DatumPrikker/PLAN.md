# DatumPrikker: plan

_To understand this, read this file first, then [NOTES.md](NOTES.md) for where the last slice stopped,
then [CLAUDE.md](CLAUDE.md) for how to build and run it._

DatumPrikker lets someone propose a few dates and lets others say Ja, Misschien or Nee to each.
Only making a poll needs an account, and from slice 3 the admin's approval as well. Answering needs
only a name and an email address.

## Status

| Slice | What | State |
|---|---|---|
| 1 | Restart: the poll flow in one app | merged (projects#240) |
| 2 | Accounts: "Mijn afspraken", prefilled email, gentle login nudges | merged (projects#241) |
| 3 | Admin and access: only approved people make polls | merged (projects#242) |
| 4 | On gembernodes | live since 2026-11-09 (gembernodes#155, projects#243) |
| 5 | The look: bold and playful | live (projects#244, gembernodes#156) |
| – | The maker chooses their name (not a numbered slice; NOTES.md) | live (projects#246, gembernodes#157) |
| 6 | Respondents see the most likely date (D21) | live (projects#247, gembernodes#158) |
| 7 | Deadline and final pick (D22) | built 2026-11-11, waiting for review (branch `ccr-0ac3368a-xnhnhs`) |

Each slice is one PR. Work stops after each one for review.

## Decisions

- **D1 Restart, one app** (2026-11-09). The first version (April 2026, last at commit `9b5372a8`) was
  an Aspire solution: a Blazor front end and a separate API service, plus EF Core and Redis. Its
  API trusted identity headers from anyone who could reach it. Its rate limit only saw the front
  end's IP, so every respondent shared one bucket. Its Redis cache was more than the app needed.
  The new requirements rewrite identity, every page and the deploy anyway. So slice 1 replaces it.
  The old code only served as the spec: its data model, ranking and share tokens carry over.
- **D2 Layout: the Kluskat template.** `DatumPrikker.core` ← `.application` ← `.infrastructure` ←
  `.WebApp` (+ `DatumPrikker.test`). One record + one handler per command or query. Marten sits
  behind Marten-free ports, DI is inline in `Program.cs`, and expected failures are Qowaiv `Result`s.
  Warnings are errors.
  - **Razor Pages + vanilla JS, not Blazor Server.** The question on 2026-11-09 said "Blazor
    Server". Kluskat turned out to be the better template: plain pages need no websocket per
    respondent, survive a phone's flaky connection, and work before any script loads. Say so if you
    want Blazor after all.
  - **Marten as a document store only.** Nothing here needs history, so there is no event store.
- **D3 A respondent is a name plus an email address.** The email (trimmed, lower case) identifies
  the respondent within a poll. Answering again with the same email replaces the earlier answer
  (last write wins), so the email doubles as the "edit" key. Results show the name. Only the poll's
  owner sees the email.
  - **When you're logged in** (confirmed 2026-11-09): your name is filled in and stays editable.
    Your email is your account's and can't be edited.
  - The domain enforces this too: `Answer.Give` uses the account's email whatever was posted.
  - Only an account without an email claim gets an editable email field.
- **D4 Answers given while logged in are claimed** (slice 2). Anonymously, nobody can overwrite an
  answer that was given while logged in. They get "Log in om dit antwoord te wijzigen" instead.
- **D5 Login: Google and Microsoft, each shown only when it's configured.** A provider is
  registered and offered only when both its `ClientId` and `ClientSecret` are set. With neither
  configured, nobody can make a poll, and the login page says so. In `Development` only, a dev
  login (name + email, no password) stands in for both, so the app runs locally without OAuth apps.
- **D6 An account is `{provider}:{subject}`** together with the provider's name and email claims.
  - The polls you **made** are found by account. They're not found by email, because an email
    claim from a Microsoft work account isn't necessarily verified.
  - The polls you **answered** (slice 2) are found by your account's email. That includes answers
    given anonymously with that email, which is harmless for a list.
- **D7 A date option is a date with an optional time range.** Without times, it's the whole day.
  At most 30 options per poll.
- **D8 One comment per answer, not one per date.** v1 asked for a reason per date. Nobody fills
  in five reasons.
- **D9 Results are visible only to the owner**, as in v1. The owner can close the poll, after which
  it stops taking answers. Slice 7 added a deadline and a final pick (D22).
- **D10 Dutch UI, English code.** Test methods are snake_case English sentences, as in Kluskat.
- **D11 No cache, no Redis.** One pod and one shared PostgreSQL. Anonymous answering is rate limited
  per client IP (30 per 5 minutes), behind the ingress's forwarded headers.
- **D12 Local ports:** PostgreSQL on **5436**, the web app on **8086** (Kluskat uses 5434/8082,
  CharacterCreator 5435/8084).
- **D13 Hosting** (slice 4): `https://datumprikker.gemberkoekje.nl` on the public ingress, against
  the shared PostgreSQL. The image is `ghcr.io/gemberkoekje/datumprikker:<sha>`, and the key ring
  lives in the database (as in Kluskat).
- **D14 Making polls needs the admin's approval: request, then approve** (2026-11-09, slice 3).
  - **Asking:** anyone can log in. A logged-in account without access that opens Nieuwe prikker gets
    a "Toegang vragen" button, with an optional note ("Wie ben je? Bijvoorbeeld: Bea, buurvrouw van
    nr. 12") so you know who's asking.
  - **Deciding:** you approve or decline on the admin page. The requester gets no email; on their
    next visit, Nieuwe prikker shows the form or the outcome.
  - **States:** Aangevraagd (requested), Toegestaan (approved), Afgewezen (declined), Ingetrokken
    (revoked). After a decline or a revoke there's no "ask again" button, so nobody can nag; you can
    still approve them later from the list.
  - **The rule lives in `CreatePollHandler`**, not only on the page, so posting the form directly
    can't get around it.
  - **Answering stays open to everyone,** and logging in stays worthwhile without access: Mijn
    afspraken shows the polls you answered.
  - **Revoking only stops new polls.** Polls the person already made keep working.
- **D15 Pre-approve by email.** On the admin page you can add an email address, so that person is
  approved the moment they first ask, with no wait. It covers someone who logs in with Google one
  day and Microsoft the next (two accounts, same email).
  - Accepted risk: a Microsoft work account's email claim isn't necessarily verified (D6), so
    somebody with their own tenant could pose as a pre-approved address. The worst they get is
    making polls, which shows up on your admin page.
- **D16 Admins are account ids in configuration, not emails.** `Admin:Accounts` holds a
  comma-separated list such as `google:1234…`. Admins are always allowed to make polls.
  - Ids rather than emails because an admin email could be spoofed the same way, and admin is worth
    more than making polls.
  - To find your id: log in once and open Nieuwe prikker, which shows "Je account-id: …". Put it in
    the 1Password item (slice 4). In Development, `appsettings.Development.json` makes the dev
    account `dev:arie@example.com` an admin.
- **D17 Admins see a badge, no email.** The header shows "Beheer (2)" while requests are waiting.
  There's no mail set-up; an SMTP account can come later if the badge isn't enough.
- **D18 The admin page lists access and polls; it doesn't edit polls.** You can approve, decline,
  revoke and pre-approve, and see every poll with its maker, number of answers and state.
  - From that list, an admin may open any poll's results. This is an exception to D9 (owner only):
    the operator could read the database anyway.
  - Closing and deleting stay with the owner.
- **D19 This browser remembers an anonymous respondent** (slice 2). After an anonymous answer, a
  cookie keeps the name and email for a year, so the next visit starts from the earlier answer.
  - The cookie is protected with data protection, not merely encoded. Otherwise anybody could plant
    a neighbour's address in it and read the neighbour's answer.
  - "Niet jij? Begin met een leeg formulier" forgets it, which matters on a shared computer.
  - Logged in, the account's email is used instead and no cookie is written.
- **D20 The answer page keeps one row per date** (2026-11-09, slice 5). A grid of calendar tiles,
  with tap-to-cycle, was built and then turned down: rows with Ja/Misschien/Nee beside the date are
  easier to fill in. Calendar tiles stay only as decoration on the home page. The results follow the
  same idea: one row per date, ranked from best to least favourite. From now on, every
  front-end change starts with a mock that the user approves before it's built.
- **D21 Respondents see the most likely date, not each other's answers** (decided 2026-11-09, slice
  6, later). The answer page shows which date is leading. Individual answers (names, choices,
  comments) stay owner-only, so D9 still holds for them.
  - **Only after answering** (2026-11-11), so a first answer isn't swayed. The card shows on the
    thank-you and whenever the page finds your earlier answer (logged in, or this browser's
    memory, D19).
  - **With the counts** (2026-11-11): "5× ja, 1× misschien" under each leading date, as on the
    owner's winner card. Still no names and no comments.
  - Accepted: anyone can see it early by answering with a made-up address. The likely date isn't
    a secret; the rule is only there not to sway honest answers.
- **D22 A deadline, and a final pick that tells everyone** (decided 2026-11-09, slice 7, later).
  - The owner can set an optional deadline; past it, the poll closes by itself.
  - The owner can pick "this is the date". The poll page, the results and Mijn afspraken then show
    it to every respondent.
  - **On the site only** (2026-11-11). No email for now; mail may come later, together with the
    SMTP account D17 already mentions.
  - **The deadline is set when making the poll** (2026-11-11). Nothing changes or removes it
    afterwards; closing by hand still works before it.
  - **The owner can change or undo the pick** (2026-11-11). Undoing leaves the poll closed.
  - **No reopening** (2026-11-11). A closed poll stays closed; to ask again, the owner makes a new
    poll.
  - **"Jij zei: …" on the pick card** (2026-11-11), for respondents who answered.
  - **"In je agenda zetten"** (2026-11-11): the pick card offers an .ics download of the picked
    date.

## Slices

### 1. Restart: the poll flow in one app

- Remove the v1 projects. Add the four-layer solution, docker-compose, Dockerfile and analyzer
  stack, copied from Kluskat.
- Sign-in: cookie auth, plus Google and Microsoft when configured (D5), plus the dev login in
  Development.
- **Nieuwe prikker** (logged in only): title, description, location, and date options added and
  removed in place.
- **Prikken** at `/p/{token}` (no login needed): name, email, Ja/Misschien/Nee per date, and a
  comment. When logged in, the name and email are filled in from the account.
- **Uitslag** at `/p/{token}/uitslag` (owner only): dates ranked by Ja then Misschien, who said
  what, the share link with a copy button, and a button to close the poll.
- Tests: poll validation, last write wins, closed polls, foreign dates, owner-only results and
  closing, ranking, and which providers are offered.
- Styling stays plain and tidy. The real look is slice 5.

### 2. Accounts

- **Mijn afspraken** (logged in): the polls you made and the polls you answered (D6), with each
  one's state (open/closed), your own answer, and how many people answered.
- Claimed answers (D4).
- Nudges, never walls:
  - a friendly strip on the answer page ("Log in, dan vullen we je gegevens in en zie je al je
    afspraken op één plek");
  - after answering anonymously, "Bewaar deze afspraak in je overzicht: log in";
  - the home page shows logged-in users their afspraken.
- Answering again shows your earlier answer, when logged in or when the email matches what this
  browser last used.

### 3. Admin and access

This slice comes before going live, so the public site never runs without the gate. Nothing is
live yet, so there are no existing poll makers to approve after the fact.

- **Domain:** an `Access` document per account (id = account id). It records state, name, email,
  provider, note, requested on, and decided on and by. A `PreApproval` document per email.
  - The state changes are pure functions returning `Result`s: request, approve, decline, revoke.
  - Revoke applies only to approved accounts. Approve works from any state.
- **Application:**
  - commands `RequestAccess`, `DecideAccess` (approve/decline/revoke), `PreApprove` and
    `RemovePreApproval`;
  - queries `GetAccess`, `ListAccess`, `ListAllPolls` and `CountPendingRequests`;
  - `CreatePollHandler` refuses accounts that are neither approved nor admin (D14);
  - `GetResultsHandler` lets admins in (D18).
  - The admin-only commands and queries check the admin rule themselves, as well as the page
    checking it.
- **Nieuwe prikker** depends on where you stand:
  - approved or admin: the form;
  - no request yet: an explanation, the optional note and "Toegang vragen";
  - requested: "Je aanvraag is verstuurd";
  - declined or revoked: "Je kunt (nu) geen prikkers maken".
  - Every state except approved also shows "Je account-id: …" (D16).
  - A matching pre-approval turns a request into an approval on the spot (D15).
- **Beheer** at `/Beheer`, for admins only (an authorization policy over `Admin:Accounts`):
  - **Aanvragen:** pending requests with name, email, provider, note and date, each with Toestaan
    and Afwijzen.
  - **Toegang:** approved accounts with Intrekken; declined and revoked accounts with Toestaan.
  - **Vooraf goedgekeurd:** add and remove email addresses.
  - **Alle prikkers:** every poll with its maker, created date, number of answers and open/closed,
    each linking to its results.
- **The header badge** "Beheer (n)" for admins (D17). A "Beheer" link is always there for admins.
- **Config:** `Admin:Accounts` stays empty in `appsettings.json`, and docker-compose and
  `.env.example` get `ADMIN_ACCOUNTS`. Slice 4 adds `Admin__Accounts` to the 1Password item.
- **Tests:**
  - every state change, and the ones that aren't allowed;
  - pre-approval by normalised email;
  - the gate in `CreatePollHandler` (anonymous, requested, declined, revoked, approved, admin);
  - admin-only commands and queries refused to non-admins;
  - admins opening results;
  - the badge count;
  - parsing `Admin:Accounts`.
- **Checked by hand:**
  1. A second dev account asks for access.
  2. The badge appears.
  3. You approve; they make a poll.
  4. You revoke; they can't make another, and their poll still takes answers.

### 4. On gembernodes

- The CI workflow `datumprikker-build-and-push.yml` already arrived in slice 1. It builds and tests
  every PR, and pushes `ghcr.io/gemberkoekje/datumprikker:<sha>` on every merge to main.
- gembernodes PR: namespace, deployment, service, OnePasswordItem, ingress, `create-database.sql`,
  README. A copy of Kluskat's setup.
- `DEPLOY.md` with the steps that are yours:
  - the database and login;
  - the 1Password item, including `Admin__Accounts` with your account id (D16);
  - the Google and Microsoft OAuth apps, with redirect URIs `/signin-google` and
    `/signin-microsoft`.

### 5. The look: bold and playful

As built (projects#244):
- Calendar paper, chunky outlines, and a violet/coral/sun palette, with green, amber and coral for
  Ja, Misschien and Nee.
- The answer page keeps one row per date (D20).
- The results have a winner card and a ranking from best to least favourite.
- Confetti after answering or making a poll.
- Mobile first, with light and dark themes.

### 6. The most likely date for respondents (later)

D21. The mock is in `mocks/` (open `slice6-*.html` in a browser; it loads the real `site.css`, and
the new CSS sits in `mocks/mock.css` under its slice heading). Its open points were settled on
2026-11-11.
- On the answer page, after you've answered, a "Zo te zien wordt het…" card under the thank-you
  or the "Je antwoordde al" note. It shows the leading date or dates (`PollResults.Best`: most Ja,
  then most Misschien), as white tickets with their counts on a dashed soft-violet card. That
  keeps it apart from the owner's sun-yellow winner card and the solid violet of a final pick.
- Under the tickets: "Meeste ja, dan meeste misschien, uit 7 antwoorden. Nog niet definitief: dat
  beslist Arie." A tie says "Deze 2 staan gelijk".
- Before anyone has said Ja: "Nog niemand zei ja op een datum. Alles kan nog."
- No names and no comments. A respondent's page model gets only the leading tallies, never the
  answers.
- Tests: the card only after an answer; ties; nobody said Ja yet; and that the page model holds no
  answers.
- **As built** (2026-11-11): `GetLikelyDateHandler` checks "only after answering" itself, as
  `CreatePollHandler` holds its own gate, and returns a `LikelyDate` (the leading `OptionTally`s and
  the answer count). On a closed poll, someone who answered still sees the card, under "Deze prikker
  is gesloten" (see the open question below).

### 7. Deadline and final pick (later)

D22. The mock is in `mocks/` (`slice7-*.html`, as for slice 6). Its open points were settled on
2026-11-11.
- **Deadline:**
  - Nieuwe prikker gets "Wanneer sluit hij? (optioneel)": a day plus an optional time. Without a
    time it closes at the end of that day (Amsterdam time).
  - The answer page says "⏳ Antwoorden kan tot vrijdag 4 december om 18:00". The results say
    "Sluit vanzelf op …", and after the deadline "Vanzelf gesloten op …". Mijn afspraken adds
    "sluit … om …".
  - Past the deadline the poll is closed by the same rule as closing by hand: `IsOpen` becomes a
    question about now, and `Answer.Give` refuses.
  - It can't be changed or removed after making the poll.
- **Final pick:**
  - In the results' ranking, every row gets a "Dit wordt het" button for the owner. Picking closes
    the poll (after a confirm) and puts a solid violet "📌 Dit wordt het" card where the winner
    card was. The card has a big calendar tile, the date, the time, the place, "📅 In je agenda
    zetten" and "Keuze ongedaan maken". Undoing leaves the poll closed; the owner can pick again.
  - After a pick, the other rows say "Kies deze" (change the pick) and the picked row says
    "✓ gekozen". The poll's label reads "gekozen".
  - The ranking shows even with no answers yet, so the owner can pick a date agreed elsewhere.
  - Everyone opening the poll link sees the pick card instead of the form, with "📅 In je agenda
    zetten". Someone who answered also sees "Jij zei: ja" (or misschien, nee).
  - In Mijn afspraken the item shows the picked date in its mini calendar, a "gekozen" label and
    "Dit wordt het: …".
  - Only the owner picks, changes or undoes, and only a date of this poll. Admins looking at
    someone else's results don't get the buttons (D18).
- **Closed without a pick** (by hand or by the deadline): the ranking keeps its "Dit wordt het"
  buttons, and under it "Toch opnieuw vragen? Maak een nieuwe prikker." There's no reopening.
- **The agenda file:** `/p/{token}/agenda.ics`, open to anyone with the link, and a 404 until a
  date is picked.
  - One `VEVENT`: the title as summary, the place as location, the description plus the poll's
    link.
  - A whole day is a `VALUE=DATE` event (the end is the next day). A time range is converted from
    Amsterdam time to UTC.
  - The `UID` is the poll's id, so a changed pick imported again replaces the old event instead of
    adding a second one. A `SEQUENCE` from the pick's time helps calendars that check it.
- Tests: the deadline passing (and a time-less deadline closing at the end of the day), the pick
  (owner only, a date of this poll, changing, undoing), what respondents see after the pick, and
  the .ics (whole day, time range across a DST change, escaping of commas and newlines, 404
  without a pick).
- **As built** (2026-11-11):
  - `Poll` got `Deadline` (a UTC moment) with `DeadlineHasTime`, and `PickedOptionId` with
    `PickedOn`. `IsOpen` is now `IsOpenAt(now)`. Read models that lists show (`Appointment`,
    `PollOverview`) carry an `IsOpen` worked out by their handler.
  - Picking is `Poll.Pick` / `Poll.UndoPick`, behind one `PickDateHandler` for the `PickDate` and
    `UndoPick` commands (as `PreApprovalHandler` does for its two). The .ics text comes from
    `AgendaFile` in core, served by `GetAgendaHandler` and a minimal endpoint (`AgendaEndpoint`).
  - The wording the mocks didn't show, for a deadline without a time: "Antwoorden kan tot en met
    vrijdag 4 december", "Sluit vanzelf na …" / "Vanzelf gesloten na …", and "sluit na 4 december".
    Closed by hand or by a pick, the results say "Gesloten op 1 december om 21:12" (as in
    `slice7-4`). Right after undoing, a plain note says the poll stays closed.

## Open questions

- **Slice 6: the likely-date card on a closed poll.** D21 doesn't say. As built, someone who
  answered still sees "Zo te zien wordt het…" under "Deze prikker is gesloten", since the date
  isn't picked yet. That now includes a poll closed by its deadline. The alternative is to hide it
  once the poll closes. Since slice 7, the pick card replaces it once a date is picked.

Everything else was answered on 2026-11-11 and sits in D21 and D22. Mail for the final pick stays a
possible later addition (D17, D22).
