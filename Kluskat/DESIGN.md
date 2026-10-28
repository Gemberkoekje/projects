# Kluskat — design

A phone-first website on gembernodes that hands you **one micro-chore at a time**. You never see
how many chores are waiting. A pixel cat cheers you on, levels up and collects hats.

_To understand this, read this file first, then [NOTES.md](NOTES.md) for what was built and what is next._

Status: design agreed 2026-10-28; chore list approved ("I'll tell you when a chore doesn't work and we refine"). Phase 1 built.

## 1. Decisions so far

| Topic | Decision |
|---|---|
| Users | Just me. One password, remembered on the device for a year. |
| Language | Dutch UI. |
| Task order | Most behind schedule: highest `(now − lastDone) ÷ interval` among due chores. |
| Skips | A skipped chore goes to the back of the queue. Skips wear off 3 hours after your last tap, so this morning's skip is first again tonight. |
| Big chores | Split into micro-steps (section 7) **and** each chore gets a size: S ≈ 5 min, M ≈ 15 min, L ≈ 30+ min. |
| Editing | A small "Beheer" page in the app, behind the same password. |
| Fresh start | Staggered: each chore's first due date is spread across its interval, so chores trickle in. |
| After done | A small win: XP, achievements, this week's count, then "Nog eentje" / "Klaar voor nu". |
| Game pieces | XP & levels, achievements, forgiving weekly streak. No loot drops, no reward shop. |
| Theme | Cat companion. |

For scale: the sheet adds up to about 20 chores a week (≈ 3 a day) to stay fully on schedule.
Doing fewer is fine: the "most behind" order stretches every chore out evenly, and nothing
piles up visibly.

## 2. Screens

**Klus (main screen).** The cat, one chore card (room, chore, size chip), and three buttons:

- **Gedaan!** (big, primary): marks it done and goes to the win screen.
- **Geen zin**: another chore, any size.
- **Geen tijd**: another chore that is *smaller* than this one. The size cap stays on until the
  skips wear off.

A tiny trophy icon opens the profile, and a tiny gear icon opens Beheer. There are no counters
for what's left.

**Win screen.** The cat does a happy animation with confetti, then shows:

- `+25 XP`, the XP bar filling, and a level-up banner when you level up;
- any new achievement as a toast;
- "Deze week: 6 klusjes" and the streak flame;
- **Nog eentje** / **Klaar voor nu**, plus a small "Oeps, niet gedaan" undo against mis-taps.

The next chore is never shown automatically.

**Niks te doen.** The cat naps: "Niks dat op je wacht." A quiet "Toch iets doen?" link offers
the chore that is due next. If everything due was skipped, the text says the skipped ones come
back later, with a link to see them now.

**Klaar voor nu.** A sleeping cat and "Goed gedaan. Tot later!"

**Profiel.** Level, XP, achievements (earned plus greyed-out ones), streak and freezes, and the
cat's wardrobe (pick an accessory). Totals count only what you did.

**Beheer.** A plain table: add, edit or retire a chore (room, name, interval, size), plus a
"mark done on date X" fix-up for when you did something without the app.

## 3. Chore picking

1. A chore is **due** when `now ≥ lastDone + interval`.
2. Remove chores skipped in the current skip window. With a "Geen tijd" cap active, also remove
   chores at or above the capped size.
3. Pick the highest `(now − lastDone) ÷ interval`. Ties go to the smaller size, then the
   oldest `lastDone`.
4. Skip window: skips and the size cap are cleared when 3 hours pass with no tap.
5. **Gedaan** sets `lastDone = now`. Completing early via "Toch iets doen?" works the same.

## 4. Gamification

- **XP:** S = 10, M = 25, L = 50.
  - **Dapperheidsbonus:** +50% for finishing a chore you skipped in the last 3 days. You beat
    the one you dodged.
  - **Eerste klus van vandaag:** +5.
- **Levels:** a gentle curve, roughly a level every few days early on, slowing down later.
  - The cat grows: kitten (level 1–4), cat (5–14), majestic cat (15+).
  - Every few levels unlocks an accessory: strikje, bril, sjaal, koksmuts, piratenhoed,
    heksenhoed, kroon…
- **Weekly streak:** a week (Mon–Sun, Europe/Amsterdam) counts with at least 3 chores.
  - Every 4 streak weeks earns a freeze (max 2 held). A missed week uses one automatically.
  - "Longest streak" is kept forever.
- **Achievements:** about 20, for example:
  - Eerste klus, then 10 / 50 / 100 / 250 / 500 klussen.
  - Elke kamer gedaan.
  - Overwinnaar: finish a chore you'd skipped 3+ times.
  - Vroege vogel (before 08:00) / Nachtbraker (after 23:00).
  - Jaarklus: finish a 365-day chore.
  - 10× S, 10× L.
  - Streak 4 / 12 / 52 weeks.
  - **Comeback**: a chore after 14+ days away. Coming back is rewarded, never shamed.
- **Nothing ever goes down** except the streak, and freezes cushion that.

## 5. Tech

Follows the global C# defaults (four layers, Marten, Qowaiv `Result`, Central Package
Management, inline DI).

- **.NET 10, ASP.NET Core Razor Pages.** Plain HTML forms with a sprinkle of CSS/JS for the cat
  animations and confetti. No Blazor Server: a websocket circuit on a phone that keeps
  reconnecting is pointless pain for a one-button app.
- **PWA manifest + icon**, so it opens full-screen from the home screen.
- **Persistence: Marten on the shared cluster Postgres**, database `kluskat`, with the event
  store.
  - Each chore is a stream: `ChoreCreated`, `ChoreEdited`, `ChoreRetired`, `ChoreCompleted`,
    `ChoreCompletionUndone`, `ChoreSkipped`.
  - Inline projections build the current chore state and the player profile (XP, level,
    streak, achievements).
  - Because stats come from history, achievements can be added later and earned
    retroactively.
- **Auth:**
  - The password comes from config `Auth:Password` (a 1Password secret) and is compared with
    `CryptographicOperations.FixedTimeEquals`.
  - The cookie lasts 365 days and is sliding.
  - `/login` is rate-limited.
  - ★ Data Protection keys are persisted in Postgres, otherwise every redeploy would log you
    out.
- **Time zone:** Europe/Amsterdam for "today", "this week" and the early-bird/night-owl
  windows.
- **Seed:** the approved chore list (section 7) ships as a seed file and is applied, staggered,
  on first start when the database is empty.
- **Cat art:** hand-drawn SVG pixel cat with idle, cheer and sleep poses, and accessories as
  overlay layers.

Layout: `projects/Kluskat/`

- `Kluskat.core`, `.application`, `.infrastructure`, `.WebApp`, plus `Kluskat.test` (flat, as in
  werkplaats);
- `docker-compose.yml` with Postgres for local dev;
- `.env.example`.

Deploy:

- CI: `projects/.github/workflows/kluskat-build-and-push.yml` builds
  `ghcr.io/gemberkoekje/kluskat:<sha>`.
- gembernodes: namespace `kluskat`, `apps/kluskat/` (deployment, service, ghcr-login,
  OnePasswordItem holding `ConnectionStrings__Postgres` and `Auth__Password`), and
  `ingress/kluskat-ingress.yaml` for **kluskat.gemberkoekje.nl** on `nginx-public` +
  letsencrypt.
- One-time manual step: create the `kluskat` database and user on the shared Postgres, and
  the 1Password item.

## 6. Phases

1. **Core loop:** chores, picking, Gedaan / Geen zin / Geen tijd, login, seed + stagger, Beheer,
   a basic cat and win screen. Deploy, so you can start using it right away.
2. **Full gamification:** XP curve, levels, cat growth + wardrobe, achievements, weekly streak +
   freezes, profile page.

## 7. Proposed chore list (needs your OK)

Splits are marked ✂. `Opruimen` becomes a **timeboxed** "10 minuten opruimen" (set a timer,
stop when it rings), which turns an endless chore into a micro one.

| Waar | Klus | Dagen | Grootte |
|---|---|---|---|
| Keuken | Stofzuigen | 7 | S |
| Keuken | Vloer dweilen | 14 | M |
| Keuken | 10 minuten opruimen | 14 | M |
| Keuken | Oven schoonmaken | 14 | M |
| Keuken | Kastjes nat afnemen | 14 | S |
| Keuken | Afwasmachine reinigen | 28 | M |
| Keuken | ✂ Koelkast: leeghalen en over-de-datum weggooien | 84 | S |
| Keuken | ✂ Koelkast: planken en lades afnemen | 84 | M |
| Keuken | ✂ Koelkast: deur en rubbers afnemen | 84 | S |
| Keuken | Afzuigkapfilter schoonmaken | 180 | M |
| Keuken | Waterkoker ontkalken | 365 | S |
| Keuken | ✂ Bovenkastjes binnenkant schoonmaken | 365 | M |
| Keuken | ✂ Onderkastjes binnenkant schoonmaken | 365 | M |
| Keuken | ✂ Lades binnenkant schoonmaken | 365 | M |
| Planten | Water geven | 7 | S |
| Kattenkamer | Stofzuigen | 7 | S |
| Kattenkamer | 10 minuten opruimen | 14 | M |
| Gang | Stofzuigen | 7 | S |
| Gang | 10 minuten opruimen | 14 | M |
| Gang | Deurmat uitkloppen | 14 | S |
| Diepe | Kasten met water afnemen | 14 | M |
| Diepe | ✂ Kast 1–3: planken 1–3 schoonmaken (×3) | 365 | M |
| Diepe | ✂ Kast 1–3: planken 4–6 schoonmaken (×3) | 365 | M |
| Diepe | ✂ Kast 1–3: bovenkast schoonmaken (×3) | 365 | M |
| Slaapkamer | Stofzuigen | 7 | S |
| Slaapkamer | 10 minuten opruimen | 14 | M |
| Slaapkamer | Beddengoed verschonen | 28 | M |
| Slaapkamer | Matrassen omkeren | 84 | S |
| Douche/Badkamer | Wastafeltje schoonmaken | 28 | S |
| Douche/Badkamer | Deurknop schoonmaken | 28 | S |
| Douche/Badkamer | Vloer dweilen | 28 | S |
| Douche/Badkamer | Douche schoonmaken | 28 | M |
| Toiletruimte | Wastafeltje schoonmaken | 14 | S |
| Toiletruimte | Deurknop schoonmaken | 14 | S |
| Toiletruimte | Vloer dweilen | 14 | S |
| Toiletruimte | WC schoonmaken | 14 | S |
| Toiletruimte | Tegels schoonmaken | 28 | M |
| Hobbykamer | Stofzuigen | 7 | S |
| Hobbykamer | 10 minuten opruimen | 14 | M |
| Woonkamer | Stofzuigen | 7 | S |
| Woonkamer | 10 minuten opruimen | 14 | M |
| Afval | GFT afval weggooien | 7 | S |
| Afval | Restafval weggooien | 7 | S |
| Afval | Papier weggooien | 7 | S |
| Huisdieren | Kattenbak helemaal verschonen | 28 | M |
| Chemisch afval | ✂ Verzamelen in een tas | 84 | S |
| Chemisch afval | ✂ Wegbrengen | 84 | L |
| Ramen | ✂ Zemen: woonkamer | 84 | L |
| Ramen | ✂ Zemen: keuken | 84 | M |
| Ramen | ✂ Zemen: slaapkamer | 84 | L |
| Ramen | ✂ Zemen: hobbykamer | 84 | M |
| Ramen | ✂ Zemen: kattenkamer | 84 | M |
| Berging | ✂ Eén hoek opruimen (deel 1/4 … 4/4) | 365 | M |
| Gordijnen | ✂ Wassen: woonkamer | 365 | L |
| Gordijnen | ✂ Wassen: slaapkamer | 365 | L |

Answered on 2026-10-28:

- "Diepe" are three deep wall closets, each a high closet with six shelves and a separate cupboard
  on top: split per closet into shelves 1–3, shelves 4–6 and the top cupboard.
- Small windows in the keuken, kattenkamer and hobbykamer (M); big ones in the woonkamer and
  slaapkamer (L). Curtains stay a guess (woonkamer, slaapkamer) until a chore says otherwise.
- The name **Kluskat** / `kluskat.gemberkoekje.nl` is fine.
- The first cat mock-up felt "a bit MS Paint"; phase 1 redrew it as proper pixel art.
