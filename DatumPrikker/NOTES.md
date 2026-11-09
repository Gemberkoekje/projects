# DatumPrikker: notes per slice

_To understand this, start with [PLAN.md](PLAN.md) (slices and decisions), then read
`DatumPrikker.core/Models/Poll.cs`, `Answer.cs` and `DatumPrikker.WebApp/Program.cs`._

## Slice 1: restart, the poll flow in one app (2026-11-09)

### What was done

- **The v1 code is gone** (Aspire AppHost/ApiService/Web/ServiceDefaults/Tests, EF Core, Redis).
  It is still in git history, last at `9b5372a8`. The new solution copies Kluskat's layout, build
  props, analyzer config, Dockerfile and compose file (D1, D2).
- **Domain** (`core`): `Poll` (+ `DateOption`), `Answer` (+ `OptionAnswer`), `PollResults`
  (+ `OptionTally`), `Account`, `Email`, `ShareToken`. All the rules live there as pure functions
  that return `Result`s: `Poll.Create`, `Poll.Close`, `Answer.Give` and `PollResults.Tally`.
- **Application:** `CreatePoll`, `GiveAnswer` and `ClosePoll`, plus `GetPoll` and `GetResults`,
  each with a handler. The ports are `IPollStore` and `IAnswerStore`.
- **Infrastructure:** `MartenPollStore`, `MartenAnswerStore`, and `MartenXmlRepository` (the key
  ring in Postgres, copied from Kluskat).
- **Web:** Razor Pages.
  - `/` is the landing page.
  - `/Aanmelden` shows the configured providers, plus the dev link in Development.
  - `/Dev/Aanmelden` is the dev login, and a 404 outside Development.
  - `/Afmelden` signs out.
  - `/Nieuw` makes a poll, with JS to add and remove date rows.
  - `/p/{token}` is where people answer.
  - `/p/{token}/uitslag` is for the owner: the share link with a copy button, the best dates, a
    who-said-what table, and a close button.
  - `/Fout` covers 404, 429 and exceptions.
- **CI:** `.github/workflows/datumprikker-build-and-push.yml` builds and tests PRs, and pushes the
  image to GHCR on main. It was planned for the cluster slice (now 4) but is cheap now.
- **Tests:** 51, all over in-memory stores. They cover the domain rules, last write wins (keeping
  `CreatedOn`), owner-only results and closing, malformed tokens, which providers get offered,
  account claims, parsing date rows, and a System.Text.Json round trip of the documents.

### Checked by hand (local Postgres, browser)

Every step below went as expected:

1. Signed in with the dev login (no provider configured, so the login page shows only the dev link).
2. Made a poll, after a deliberately half-filled date row was refused with "Datum 4: kies een dag."
3. Signed out and answered anonymously as Carla, then as Dirk.
4. Answered as Carla again with `CARLA@example.com`. The log says "changed", and the results show
   her second answer.
5. Signed in as someone else: the results page says owner-only, with a 403.
6. Signed in as the owner: the results show the correct tally and the best dates (a tie of two).
7. Closed the poll: the answer page says it's closed.
8. An unknown token gets the friendly 404.
9. After an app restart you stay signed in, because the key ring is in Postgres.

### Gotchas

- **A hand-written `<form action="/Afmelden">` gets no antiforgery token.** The form tag helper
  only adds one when the action comes from `asp-page`, `asp-action` and the like. Sign-out answered
  with an empty 400. Fixed with `asp-page="/Afmelden"`.
- **Status-code re-execution keeps the method.** A failed POST (an antiforgery 400, a rate-limit
  429) re-runs `/Fout` as a POST, so `/Fout` needs an `OnPost` and `[IgnoreAntiforgeryToken]`
  (suppressed S4502 with a justification). Without them the error page itself returned an empty 400.
- **NUnit shares one fixture instance across its tests.** Fields such as the in-memory stores leak
  between tests unless you set `[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]` (in
  `AssemblyInfo.cs`).
- **The analyzers here are strict:**
  - StyleCop 1.1.118 rejects `new()` (SA1000) and `required` (SA1206).
  - Qowaiv wants `[Mutable("…")]` on form-binding classes (QW0011/QW0012), and leading zeros in date
    literals (QW0021).
  - Positional records trip QW0016.
- **Empty form fields bind as `null`** even on non-nullable strings. The input classes use
  `[DisplayFormat(ConvertEmptyStringToNull = false)]`.
- Marten LINQ is only ever asked about plain strings and Guids (the email is stored normalised as a
  string), so no Qowaiv value type needs a Marten registration.

### Left for later slices

- Slice 2: "Mijn afspraken" and claimed answers (D4). The answer page's nudge is a single line for
  now. Returning respondents don't see their earlier answer yet: an anonymous form starts empty.
- Slice 3: admin and access (only approved people make polls).
- Slice 4: the cluster. The image already builds, and `/healthz` exists for the probes.
- Slice 5: the look. The CSS is deliberately plain.

### File map

- `DatumPrikker.core/Models/`: `Poll`, `PollDraft`, `DateOption`, `Answer`, `AnswerDraft`,
  `OptionAnswer`, `PollResults`, `OptionTally`, `Account`, `Email`, `ShareToken`.
- `DatumPrikker.core/Enums/Availability.cs`.
- `DatumPrikker.application/{Commands,Queries,Ports}/`: one record and one handler per file.
- `DatumPrikker.infrastructure/Adapters/`: the three Marten adapters.
- `DatumPrikker.WebApp/Program.cs`: the composition root, auth, rate limit and Marten schema.
- `DatumPrikker.WebApp/Auth/`: `LoginProviders` (D5) and `AccountClaims` (D6).
- `DatumPrikker.WebApp/Pages/`: the pages listed above. `Weergave.cs` holds the Dutch formatting.
- `DatumPrikker.test/`: specs per layer, plus `TestHelpers/` (in-memory stores, fixtures).

## Slice 2: accounts (2026-11-09)

### What was done

- **Claimed answers (D4).** `Answer.IsClaimed` (given while logged in) and
  `Answer.ReplaceableBy(account)`. `GiveAnswerHandler` refuses an anonymous replacement of a claimed
  answer with `Answer.ClaimedMessage`.
  - Logging in and answering again claims an anonymous answer.
  - Another account with the same email may replace a claimed answer: logged in, you can only answer
    as your own address.
- **Mijn afspraken** at `/Afspraken`, and on the home page when logged in. `ListAppointmentsHandler`
  merges the polls you made (by account id) with the polls answered under your email (D6).
  - The list puts open polls first, then the latest activity (made, or your last answer).
  - Each poll shows its state and date span.
  - The owner also sees the answer count and a results link.
  - A respondent sees their own answer summarised ("1× ja, 1× misschien"), with a link to change it.
- **Your earlier answer fills the form** on the answer page, with "Je antwoordde al op …" and an
  "Antwoord bijwerken" button.
  - Logged in, it's found by the account's email.
  - Anonymous, it's found by `RespondentMemory` (D19), a protected cookie with the name and email
    this browser last answered with. "Niet jij? Begin met een leeg formulier" forgets it.
- **Nudges, never walls:**
  - the answer page says "Log in, dan vullen we je gegevens in … Hoeft niet: antwoorden kan ook zo";
  - after an anonymous answer: "Bewaar deze afspraak in je overzicht: log in, met hetzelfde
    e-mailadres";
  - after a logged-in answer: a link to Mijn afspraken.
- **The header** shows "Mijn afspraken" and "Uitloggen" when logged in. "Nieuwe prikker" moved onto
  the home and afspraken pages, which keeps the header to two buttons on a phone.
- **Ports:** `IPollStore.ListOwnedByAsync` and `LoadManyAsync`; `IAnswerStore.ListByEmailAsync` and
  `CountForPollsAsync`. Marten serves these from the existing indexes on `OwnerAccountId`, `PollId`
  and `Email`.
- **Tests:** 69 (18 new). They cover the claim rule in the domain and the handler, merging,
  ordering and counting afspraken, finding an earlier answer by any spelling of the email, the
  cookie (round trip, unreadable, forged, other key ring), and the Dutch date span, summary and
  local time.

### Checked by hand (local Postgres, browser)

Every step below went as expected:

1. Arie made a poll.
2. Carla answered anonymously. The thank-you offered "log in" to save the poll in her overview.
3. On her next visit the page said "Je antwoordde al op 9 november om 21:05", with her choices and
   comment filled in.
4. "Niet jij?" emptied the form.
5. Bea answered logged in: her name was filled in and editable (she changed it), and her email was
   locked.
6. An anonymous answer with `bea@example.com` was refused with the claimed message, and Bea's answer
   stayed.
7. Carla logged in with the same email: her home page listed both polls she had answered
   anonymously, open first.
8. Arie's Mijn afspraken showed both his polls with their answer counts.
9. The results showed Bea's edited name.

### Gotchas

- **A line in a Razor code block that starts with `<br />` ends the markup right there.** The rest
  of the line is parsed as C#, which gave confusing CS0246 errors on Dutch words. Wrap the line in
  one element (`<span class="blok">`).
- **Sonar S2092 wants `Secure` on every cookie.** It is now always on. Browsers accept Secure
  cookies on `http://localhost`, so local runs still work, as checked above.
- `Europe/Amsterdam` resolves on Windows too (.NET uses ICU for IANA ids), so `Weergave.Moment`
  needs no Windows id fallback.

### Left for later slices

- Slice 3: admin and access (D14–D18).
- Slice 4: the cluster.
- Slice 5: the look. The afspraken list and the nudges are plain.

### File map (new or changed)

- `DatumPrikker.core/Models/Answer.cs`: `IsClaimed`, `ReplaceableBy`, `ClaimedMessage`.
- `DatumPrikker.application/ReadModels/Appointment.cs`.
- `DatumPrikker.application/Queries/`: `ListAppointments(+Handler)` and `GetAnswer(+Handler)`.
- `DatumPrikker.WebApp/Auth/RespondentMemory.cs` (D19).
- `DatumPrikker.WebApp/Pages/`: `Afspraken`, `Shared/_Afspraken`, a reworked `Index` and `P/Prikken`.
- `DatumPrikker.WebApp/Weergave.cs`: `Bereik`, `Moment`, `Samenvatting`.
