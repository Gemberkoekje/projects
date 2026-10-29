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
- The seed: 64 klussen from the sheet (DESIGN.md §7 plus the user's answers: the three Diepe
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
  stored value is UTC anyway. (Since phase 2 nothing queries by date any more; the log is loaded
  whole and scored in memory.)
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

## Phase 2 — gamification (2026-10-28)

_Start with `Kluskat.application/Spel/Spelregels.cs` (every rule), then `Pages/Gedaan.cshtml` (the
celebration) and `Pages/Profiel.cshtml`._

### What shipped

- **XP**: klein 10 / middel 25 / groot 50; **+50% "dapper"** for a klus skipped in the 3 days
  before; **+5** for the first klus of a (local) day. Itemised on the win screen.
- **Levels**: level *n* takes `60 + 20·(n−1)` XP. Badge "Lv N" with a mini XP bar top-left on the
  klus screen (links to the profile), plus the streak flame top-right once there is a streak.
- **The cat grows**: kitten (lv 1–4, 3×3 eyes, 5 px/pixel) → cat (5–14, 6 px) → majesty (15+,
  lynx ear tufts, a cream ruff, twinkling sparkles, 7 px).
- **Wardrobe**: feestmuts (lv 2), strikje (4), bril (6), bloemetje (8), sjaal (10), koksmuts
  (12), kroon (15) — pixel layers on the sprite; chosen on the profile ("De kast"), worn on every
  screen.
- **22 achievements** (`Trofeeen.Alle`), incl. **Welkom terug** for coming back after 14 days.
- **Forgiving weekly streak**: a Monday–Sunday week counts with ≥ 3 klussen; every 4 counted weeks
  earn a freeze (max 2) that a short week spends instead of breaking the streak. The running week
  never breaks it. "Deze week telt mee voor je reeks!" on the win screen when it tips over.
- **Win screen**: +XP with bonus chips, an XP bar that animates from before to after, a level-up
  card (with the cat's growth and the unlocked accessory), a card per new trophy.
- **Profile** (`/Profiel`): level + XP, klussen / deze week / reeks / bevriezingen, the wardrobe,
  the trophy shelf (earned in colour, the rest greyed).
- **Dev gallery** `/Dev/Katten` (Development only, 404 elsewhere): every form × pose × accessory.
- 79 specs (35 new). Verified in the browser: XP + bonuses, Eerste klus/Dapper/Nachtbraker
  unlocks, streak tipping at the 3rd klus, level 2 + feestmuts unlock, dressing the cat, dark mode.

### Decisions

- **Everything is computed** from the `Voltooiing` log (`Spelregels.Bereken`) on each request —
  no player aggregate, no stored XP. A rule change re-scores history; undo just removes the entry.
  Only the *choice* of accessory is stored (`Profiel` document).
- `Voltooiing` now snapshots `IntervalDagen`, `Dapper` and `KeerOvergeslagen` at completion time,
  taken from the klus **before** the Gedaan (the aggregate counts skips since the last Gedaan).
- The win screen scores the log with and without the latest completion of that klus, so a refresh
  celebrates the same thing again instead of showing nothing.
- An accessory the cat can no longer wear (an undo dropped the level) is silently taken off.
- No loot drops, no reward shop, nothing that ever goes down except the streak (as agreed).

### Deployed (2026-10-29)

Live at https://kluskat.gemberkoekje.nl, image `kluskat:440705834b7fdf544d83fd238aa69e63d64a5112`
(gembernodes PR #99). First start seeded 64 klussen; Let's Encrypt certificate issued; the Dev
gallery answers 404 in production. Next deploys: push main, wait for the image, bump the tag in
gembernodes `apps/kluskat/deployment.yaml`.

### Gotchas

- Sprite layering order matters: base → form → pose face → accessory. Glass (`~`) only tints fur
  (`a b c s k`), so closed eyes and outlines show through the bril.
- Static assets are served from the build manifest: a CSS change needs a rebuild/restart of the
  dev server to show up.
- Old local `Voltooiing` docs (from before phase 2) lack the new fields; they read as 0/false.
  Production has no data yet, so no migration was needed.
- Positional public records trip Qowaiv `QW0016`; use `required` init properties.
