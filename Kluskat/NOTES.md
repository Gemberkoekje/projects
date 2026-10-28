# Kluskat — phase notes

_To understand this, start by reading [DESIGN.md](DESIGN.md) (what and why), then
`Kluskat.application/Kiezen/KlusKiezer.cs` + `Sessie.cs` (the whole "one klus at a time" rule set),
then `Kluskat.WebApp/Pages/Index.cshtml(.cs)`. [DEPLOY.md](DEPLOY.md) has the cluster steps._

## Phase 1 — core loop (2026-10-28)

### What shipped

- The klus screen: one klus, **Gedaan! / Geen zin / Geen tijd**. Nothing ever counts what is left.
- The win screen: happy hopping cat, square confetti, "Deze week: N klusjes", **Nog eentje** /
  **Klaar voor nu**, and an "Oeps, niet gedaan" undo.
- "Niks te doen" (sleeping cat + "Toch iets doen?" to get ahead) and "Dat was het voor nu"
  (everything due was skipped; "Laat ze nu toch zien" ends the skip window).
- Beheer: list, add, edit, "gedaan op" fix-up, schrappen. No due dates shown, on purpose.
- One-password sign-in, 365-day sliding cookie, rate-limited form, key ring in Postgres.
- The seed: 65 klussen from the sheet (DESIGN.md §7 plus the user's answers: the three Diepe
  closets are split into planken 1–3 / 4–6 / bovenkast; windows in keuken/kattenkamer/hobbykamer
  are middel, woonkamer/slaapkamer groot). Staggered on first start.
- The cat is redrawn as a 26×22 pixel sprite (the first mock-up was "a bit MS Paint"), rendered as
  inline SVG with three poses and a CSS blink. Icons are generated from the same sprite.
- 44 specs (picker, sessie, stagger, aggregate, handlers end to end, sprite, password, calendar,
  event schema guard). Verified in the browser: sign-in, skip, Geen tijd cap + hint, Gedaan, win
  screen, undo, alles-overgeslagen, vrijgeven, Klaar, Beheer, light and dark. Container
  smoke-tested on Linux.

### Decisions

- **Template = werkplaats/plotboek**, with the projects/ house style on top (warnings as errors,
  `Proj0039` silenced for that reason in `.globalconfig`).
- **Skips are server-side and device-independent**: one `Sessie` document, worn off 3 hours after
  the last *tap* (looking is not a tap). A skip also lands on the klus stream (`KlusOvergeslagen`)
  so phase 2 can give a bravery bonus.
- **Geen tijd caps to one size below** the klus on screen (klein stays klein); the cap only
  tightens and survives a Gedaan until the window wears off.
- **Deterministic order** (most behind relative to own interval, then smaller, then older). That is
  what makes "a skipped klus comes back first" free: nothing is reshuffled.
- **Completions entered on Beheer don't count as Voltooiing** (they were not "played"); phase 2
  can still read them from the stream (`KlusGedaan.ViaBeheer`).
- **Data-protection keys in Postgres** (`MartenXmlRepository`), unencrypted — accepted for a
  one-user app; the pod has no volume.
- **Razor Pages, no Blazor**: a one-button phone app should not hold a websocket circuit.

### Gotchas

- **Qowaiv.DomainModel 3 aggregates are immutable**: `klus.Gedaan(...)` returns a *new* `Klus`;
  the instance you called it on is unchanged. Always continue with `result.Value`.
- **Marten compares JSON `DateTime` as `timestamp without time zone`**; Npgsql refuses a UTC-kind
  parameter for it (500 on the win screen). Pass `DateTime.SpecifyKind(x, Unspecified)` — every
  stored value is UTC anyway (`MartenVoltooiingStore.AantalSindsAsync`).
- **Marten 9 has no sync `SaveChanges`**; `IXmlRepository` is sync by contract, so the key ring
  blocks on the async call with a justified `S4462` suppression.
- **StyleCop 1.1.118 doesn't know target-typed `new()`** (SA1000) — write the type out.
- **NUnit reuses one fixture instance** across tests; specs with in-memory state carry
  `[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]`.
- **The preview tool reads `C:\git\.claude\launch.json`**, not one inside the repo; the `kluskat`
  entry lives there.
- .NET 10's generated `Program` is already public — don't add `public partial class Program`
  (it trips QW0005/S1118).

### File map

| Where | What |
|---|---|
| `Kluskat.core/Models/Klus.cs` | The aggregate: Toevoegen, Aanpassen, Gedaan, Terugdraaien, Overslaan, Schrappen |
| `Kluskat.core/Events/KlusEvent.cs` | All six events (frozen by `KlusEvent_schema_guard_specs`) |
| `Kluskat.core/Models/Kalender.cs` | Europe/Amsterdam conversions: today, week start, midnight |
| `Kluskat.application/Kiezen/` | `KlusKiezer` (the picker), `Sessie` (skip window), `Keuze` |
| `Kluskat.application/Startvulling/Spreiding.cs` | The staggered fresh start |
| `Kluskat.application/Commands/`, `Queries/` | One record + one handler each |
| `Kluskat.application/ReadModels/` | `KlusOverzicht`, `Voltooiing` (completion log), `Overwinning` |
| `Kluskat.infrastructure/Adapters/` | Marten stores + the data-protection key ring |
| `Kluskat.infrastructure/Services/startvulling.json` | The seed list |
| `Kluskat.WebApp/Kat/KatSprite.cs` | The pixel cat (grid + pose overrides → SVG) |
| `Kluskat.WebApp/Pages/` | Index (klus), Gedaan (win), Klaar, Aanmelden, Fout, Beheer/* |
| `Kluskat.WebApp/wwwroot/` | `site.css` (tokens + dark mode), `feest.js` (confetti), manifest, icons |
| `../.github/workflows/kluskat-build-and-push.yml` | CI: build + test, then publish + image to GHCR |

### Next (phase 2)

XP & levels (S 10 / M 25 / L 50, +50% bravery bonus for a klus skipped in the last 3 days, +5 for
the first of the day), the cat growing and unlocking accessories, ~20 achievements (incl.
**Comeback**), the forgiving weekly streak with freezes, and a profile page. Everything can be
computed from the streams + `Voltooiing` log already being written.
