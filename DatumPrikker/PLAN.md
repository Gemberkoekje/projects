# DatumPrikker: plan

_To understand this, read this file first, then [NOTES.md](NOTES.md) for where the last slice stopped,
then [CLAUDE.md](CLAUDE.md) for how to build and run it._

DatumPrikker lets someone propose a few dates and lets others say Ja, Misschien or Nee to each.
Only making a poll needs an account. Answering needs only a name and an email address.

## Status

| Slice | What | State |
|---|---|---|
| 1 | Restart: the poll flow in one app | built, PR open |
| 2 | Accounts: "Mijn afspraken", prefilled email, gentle login nudges | planned |
| 3 | On gembernodes | planned |
| 4 | The look: bold and playful | planned |

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
  it stops taking answers. There's no deadline yet.
- **D10 Dutch UI, English code.** Test methods are snake_case English sentences, as in Kluskat.
- **D11 No cache, no Redis.** One pod and one shared PostgreSQL. Anonymous answering is rate limited
  per client IP (30 per 5 minutes), behind the ingress's forwarded headers.
- **D12 Local ports:** PostgreSQL on **5436**, the web app on **8086** (Kluskat uses 5434/8082,
  CharacterCreator 5435/8084).
- **D13 Hosting** (slice 3): `https://datumprikker.gemberkoekje.nl` on the public ingress, against
  the shared PostgreSQL. The image is `ghcr.io/gemberkoekje/datumprikker:<sha>`, and the key ring
  lives in the database (as in Kluskat).

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
- Styling stays plain and tidy. The real look is slice 4.

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

### 3. On gembernodes

- The CI workflow `datumprikker-build-and-push.yml` already arrived in slice 1. It builds and tests
  every PR, and pushes `ghcr.io/gemberkoekje/datumprikker:<sha>` on every merge to main.
- gembernodes PR: namespace, deployment, service, OnePasswordItem, ingress, `create-database.sql`,
  README. A copy of Kluskat's setup.
- `DEPLOY.md` with the steps that are yours: the database and login, the 1Password item, and the
  Google and Microsoft OAuth apps with redirect URIs `/signin-google` and `/signin-microsoft`.

### 4. The look: bold and playful

- Bright accent colours, big rounded cards, and a calendar-style grid of date options. Tap a date
  to cycle Ja → Misschien → Nee, colour-coded.
- Results as a heat strip, with the leading date celebrated.
- A small celebration on submitting an answer.
- Mobile first, with light and dark themes.

## Open questions

- Should respondents see each other's answers, as on Doodle? v1 and slice 1 say no (D9).
- Do you want a deadline, and a "this is the date" pick that tells everyone?
