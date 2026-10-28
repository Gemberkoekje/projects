# Kluskat — repo conventions

One-chore-at-a-time household app with a pixel cat. Read [DESIGN.md](DESIGN.md) first (the
agreed design and its reasons), then [NOTES.md](NOTES.md) for where the last phase stopped.
[DEPLOY.md](DEPLOY.md) covers the cluster.

## Template

`C:\git\gitlabs\werkplaats` (itself from `plotboek-generator`) is the template: layering
`Kluskat.core` ← `.application` ← `.infrastructure` ← `.WebApp` (+ `Kluskat.test`), hand-rolled
CQRS (one record + one handler per file), Marten behind Marten-free ports, inline DI in
`Program.cs`, config via the colon-path indexer, Razor Pages + vanilla JS, the analyzer stack in
`Directory.Build.props`, severities in the single `.globalconfig`.

## Deltas

- **Warnings are errors** (projects/ house style). Keep the build clean; tune a rule only in
  `.globalconfig` with a `[Justification: …]`.
- **One user, one password** (`Auth:Password`), cookie auth, fallback policy closes every page.
  No Identity, no roles.
- **The product rule above all**: nothing user-facing may count or list what is *left* to do. The
  klus screen shows one klus; Beheer lists chores but never due dates or overdue markers.
- Dutch domain nouns and Dutch UI (`Klus`, `Ruimte`, `Grootte`, `Overslaan`, `Voltooiing`);
  English for technical names. Test methods are snake_case English sentences.
- Local Postgres on **5434**, web on **8082** (`docker compose up -d postgres`, then run the
  WebApp; dev password in `appsettings.Development.json`).
- CI is GitHub Actions in the projects repo (`kluskat-build-and-push.yml`), image on GHCR.

## Working here

```bash
dotnet build Kluskat.slnx
```

```bash
dotnet test Kluskat.slnx
```

- Qowaiv.DomainModel aggregates are immutable: continue with `result.Value`, never with the
  instance you called the mutation on.
- Events are frozen by `KlusEvent_schema_guard_specs`: add a new event, never change one.
- The cat is `Kluskat.WebApp/Kat/KatSprite.cs` (base grid + form/pose/accessory layers). Draw
  against `/Dev/Katten` (Development only) — it renders every combination. After changing the
  *base* sprite, regenerate the icons in `wwwroot/img` from it (any SVG-to-PNG with
  nearest-neighbour scaling, e.g. ImageMagick `-filter point`).
- The game is a pure function of the completion log (`Spelregels.Bereken`): change a rule there,
  and history re-scores itself. Never store XP or levels.
- Every phase ends with a written summary appended to `NOTES.md`.
