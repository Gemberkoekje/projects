# Klaverjas: repo conventions

Dad's klaverjas competition: the evening's results in, the standings out. Read [PLAN.md](PLAN.md)
first (the rules R1–R19, the decisions D1–D18 and the slices), then [NOTES.md](NOTES.md) for where
the last slice stopped. [mock/index.html](mock/index.html) is the approved design: build what it shows.

## Template

Kluskat (`../Kluskat`) is the template: `Klaverjas.core` ← `.application` ← `.infrastructure` ←
`.WebApp` (+ `Klaverjas.test`), one record + one handler per command or query, Marten behind
Marten-free ports (`ISpelerStore`, `IAvondStore`, `IInstellingenStore`), inline DI in `Program.cs`,
configuration through the colon-path indexer, Razor Pages + vanilla JS, the analyzer stack in
`Directory.Build.props` and the severities in the single `.globalconfig`.

## Deltas

- **Warnings are errors** (projects/ house style). Tune a rule only in `.globalconfig`, with a
  `[Justification: …]`.
- **The standings are a pure function** (`StandBerekening.Bereken`) of the players, the evenings and
  the settings. Change a rule there and every week re-scores itself; never store a total or a place.
- **Documents, not events**: `Speler`, `Avond` (one per date; saving again keeps an `AvondVersie`),
  `Instellingen` (one document).
- **The repository is public. Real club data never goes into git** (D9): specs, the mock and any
  example use invented names. Dad's workbooks hold addresses, phones and birthdays.
- **Dad's workbooks stay off git and off screenshots.** Specs build an invented workbook in code
  (`StandenWerkboek`). Try a real file only locally, through the upload, then wipe the local
  database (`docker compose down -v`); the check screen shows real names.
- **Nobody is deleted** (D16): "Gestopt" and "Uit de stand halen" keep every score and can be undone.
- **Two doors, one cookie** (D7, `Toegang.cs`): `/` is the players' page (players' password, set in
  Instellingen; closed while empty), `/beheer/...` is Dad's (his own hashed password, or
  `Auth:Password`). The cookie carries a role and a key from the password it was opened with;
  `Toegang.ControleerAsync` drops it when that password changes. Admin pages live in
  `Pages/Beheer` and are locked by folder convention; anything new for Dad goes there.
- **The players see only `OnlineStand`**, the copy made by "Zet de stand online": never compute the
  standings for the players' page, and keep personal details (email, phone, address) out of it.
- Dutch UI and domain nouns, English for technical names. Test methods are snake_case English sentences.
- Built for a 70-year-old on a PC: Verdana, big type, plain words, one obvious next button.
- StyleCop 1.1.118 predates some syntax: write `new Foo()` instead of `new()`, and avoid `required`.
- `<option>` is a tag helper in Razor: a local markup function that renders one must be `async Task`.
- A `<form method="post">` without `asp-page` posts to the current address, handler included; give
  forms an explicit `asp-page` when the page can be reached through a handler.
- Local PostgreSQL on **5437**, the web app on **8088**.
- Start it with plain `dotnet run` (the launch profile sets Development). `dotnet run --environment X`
  is eaten by `dotnet run` itself, so the app boots as Production and stops on a missing `Auth:Password`.
- Sign-in posts are rate limited (Dad's 10, the players' 40 per 5 minutes per address): a script
  that signs in over and over gets 429 locally too.
- NUnit makes one instance per fixture, not per test: build stores in a `[SetUp]`, never in field
  initialisers, or one test's data leaks into the next.
- **Mail is optional** (`Mail:*` settings; on the cluster the site keeper's Gmail). Development points
  at `localhost:2525` without TLS: run any local SMTP catcher there to see the mails, or the screens
  record each mail as failed. Never send to real addresses from a local run.
- **The mail and the PDF show the standings online** (`OnlineStand`), never newer ones. The mail
  HTML (`StandMail`) uses inline styles only, and encodes everything typed.
- The PDF is **QuestPDF** under its Community licence (individual use). It brings its own font
  (Lato) and its Linux native libraries need nothing beyond the aspnet image.

## Working here

```bash
dotnet build Klaverjas.slnx
```

```bash
dotnet test Klaverjas.slnx
```

To run it: copy `.env.example` to `.env` and fill in `POSTGRES_PASSWORD=klaverjas-dev` and
`KLAVERJAS_PASSWORD=klaverjas-dev` (compose checks both even for the database alone), then
`docker compose up -d postgres` and run the WebApp (http://localhost:8088). The development password
is in `appsettings.Development.json`.

- Production runs on gembernodes: [DEPLOY.md](DEPLOY.md) here, `apps/klaverjas/README.md` there.
  A release is an image tag bump in that repo.
- Every slice ends with a written summary appended to `NOTES.md`, and its PLAN.md status updated.
- One slice per PR; stop after each for review.
