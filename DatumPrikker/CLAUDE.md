# DatumPrikker: repo conventions

Propose dates, share a link, see which day works. Read [PLAN.md](PLAN.md) first (the slices and the
decisions D1–D13 with their reasons), then [NOTES.md](NOTES.md) for where the last slice stopped.

## Template

Kluskat (`../Kluskat`) is the template: `DatumPrikker.core` ← `.application` ← `.infrastructure` ←
`.WebApp` (+ `DatumPrikker.test`), one record + one handler per command or query, Marten behind
Marten-free ports (`IPollStore`, `IAnswerStore`), inline DI in `Program.cs`, configuration through the
colon-path indexer, Razor Pages + vanilla JS, the analyzer stack in `Directory.Build.props` and the
severities in the single `.globalconfig`.

## Deltas

- **Warnings are errors** (projects/ house style). Tune a rule only in `.globalconfig`, with a
  `[Justification: …]`.
- **Making needs an account, answering never does.** The fallback policy closes every page;
  public pages say `[AllowAnonymous]`. Results and closing are owner-only (D9).
- **A respondent is a name plus an email address (D3).** An answer's id is `{pollId:N}:{email}`, so
  answering again with the same address replaces the answer. Signed in, you answer as your
  account's address.
- **Providers appear only when configured (D5).** Google and Microsoft need both
  `Authentication:{Scheme}:ClientId` and `ClientSecret`. In Development, `/Dev/Aanmelden` signs you
  in as anyone; elsewhere it is a 404.
- Dutch UI, English code. Test methods are snake_case English sentences.
- StyleCop 1.1.118 predates some syntax: write `new Foo()` instead of `new()`, and avoid `required`
  (use `init` with a default).
- Local PostgreSQL on **5436**, the web app on **8086**.

## Working here

```bash
dotnet build DatumPrikker.slnx
```

```bash
dotnet test DatumPrikker.slnx
```

To run it: put `POSTGRES_PASSWORD=datumprikker-dev` in `.env` (copy `.env.example`), then
`docker compose up -d postgres` and run the WebApp (http://localhost:8086). Sign in through
"inloggen zonder provider" on the sign-in page.

- Every slice ends with a written summary appended to `NOTES.md`, and its PLAN.md status row
  updated.
