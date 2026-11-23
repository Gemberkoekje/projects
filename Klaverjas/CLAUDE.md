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
- **Nobody is deleted** (D16): "Gestopt" and "Uit de stand halen" keep every score and can be undone.
- One password for now (`Auth:Password`, Dad's), cookie auth for a year, fallback policy closes
  every page. The players' password and /beheer split come with slice 4.
- Dutch UI and domain nouns, English for technical names. Test methods are snake_case English sentences.
- Built for a 70-year-old on a PC: Verdana, big type, plain words, one obvious next button.
- StyleCop 1.1.118 predates some syntax: write `new Foo()` instead of `new()`, and avoid `required`.
- `<option>` is a tag helper in Razor: a local markup function that renders one must be `async Task`.
- A `<form method="post">` without `asp-page` posts to the current address, handler included; give
  forms an explicit `asp-page` when the page can be reached through a handler.
- Local PostgreSQL on **5437**, the web app on **8088**.

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

- Every slice ends with a written summary appended to `NOTES.md`, and its PLAN.md status updated.
- One slice per PR; stop after each for review.
