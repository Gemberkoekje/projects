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
