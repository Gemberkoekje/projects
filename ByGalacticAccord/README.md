# By Galactic Accord

> *A space economy where everything is a contract, and the player is just another party to them.*

Buying food, moving it, storing it, building a ship: each is a contract with parties, terms, and a
defined outcome for when it goes wrong. The world runs whether or not you act, driven by AI actors
using the same contracts you do. The skill is judging contracts: is this haul worth running, is this
yard's cheap bid a bargain or a trap, should you post your own contract instead?

## Where things are

| Document | What it holds |
|---|---|
| [`../by_galactic_accord_gdd_v0.3.md`](../by_galactic_accord_gdd_v0.3.md) | The design. Part II is the spec for **Slice 1, the contract web**: Sale, Haul, Build and Storage contracts, posted and taken by anyone; terms and failure; haggling over game time; ships built from graded parts; information that ages with distance; the browser client. |
| [`plan.md`](plan.md) | How the current build becomes Slice 1, in ten steps (S1–S10), each with a goal, a deliverable, a check and a status line. |
| [`log/`](log/README.md) | One log per finished step: what was built, checked and measured, and what the next step should know. |
| [`CODEMAP.md`](CODEMAP.md) | The code map: what is where, what calls what, and where money and goods move. Kept current with every code change. |

## The current build

**S1 (groundwork), S2 (the browser client) and S3 (possession, Sale and Haul) are done; S4 is next.** Goods
belong to someone and are somewhere: in an actor's stock, in storage it rents from a station, or in a ship's
hold. Two kinds of contract move them. A **Sale** is goods wanted or for sale, handed over at a place; a
**Haul** carries a shipper's goods from one place to another. Consumers post the Sales they need delivered,
producers sell lots and answer what is wanted, and traders fill Sales by buying and carrying, posting Hauls
when their ships are busy. Accord space locks escrow and bonds when a contract is signed; outside it the payer
pays at handover, or defaults. Transit incidents make dangerous routes cost something, and everyone, the
player included, pays their ships' running costs. Terms, haggling, information and ships change shape in
S4–S8; the foundation (the event queue, the seeded random numbers, the money primitives, the invariants and
the soak harness) stays.

The **Blazor WebAssembly client** runs that simulation in the browser: the schematic sector map with
routes styled by danger and ships moving along them, the time bar (pause, 1×, 4×, 16×), the board of open
Sales and Hauls with each one's take-home, the contract as a document, the ledger of what you own and where,
and the captain's log, in dark and light themes. You play an independent pilot at Meridian Station with a
Light Freighter. You can carry a haul, fill a wanted Sale by buying the goods and delivering them, buy a lot
for sale and collect it, and store goods at a station, all as posted; posting your own contracts and haggling
arrive in S5.

## Running it

Requires the **.NET 10 SDK**. Builds and runs on Linux, macOS and Windows.

```bash
dotnet build

# Fast tests (the default): unit tests plus short world runs. A few seconds.
dotnet test

# Soak-length tests: million-tick invariant runs, money supply, monopoly checks. Under a minute.
dotnet test --filter Category=Soak

# The soak harness: run the world headless, print economy telemetry, assert every invariant.
dotnet run --project src/ByGalacticAccord.Cli -- soak 1000000 --seed 42

# The client, at http://localhost:5180 (add ?seed=42 for another world).
dotnet run --project src/ByGalacticAccord.Client
```

The browser check needs Node 22 as well. It publishes the client, drives it in Chromium, screenshots it to
`artifacts/client-check/screenshots/`, and fails on any console error:

```bash
cd tools/client-check
npm ci
npx playwright install chromium   # once
node check.cjs
```

Any `--filter` on the command line replaces the default one, so a filter that matches soak tests runs them.

## Layout

```
src/ByGalacticAccord.Engine/        the whole simulation: no UI, no I/O, deterministic
src/ByGalacticAccord.Presentation/  what the interface shows, as data: text templates, view models, the clock
src/ByGalacticAccord.Client/        the Blazor WebAssembly client
src/ByGalacticAccord.Cli/           the soak harness
tests/ByGalacticAccord.Tests/       xUnit: fast by default; soak-length tests carry [Trait("Category", "Soak")]
tools/client-check/                 the browser check (Playwright)
```

[`CODEMAP.md`](CODEMAP.md) goes file by file.

## How it works

- **Discrete events** (design §19). The world is a priority queue of future events ordered by tick, with
  stable tie-breaking. Running means processing events as the clock advances; nothing happens between them.
- **One tick is one game hour** (§8.3). Text shows dates and durations, never ticks:
  `GameTime.FormatDate` gives "Day 4 · 14:00", `GameTime.FormatRelative` gives "in 1d 6h".
- **Determinism** (§20.1). Every random draw comes from the seeded PRNG, and everything that consumes
  randomness iterates in a stable order, so a seed reproduces a run exactly. The tests check it.
- **Invariants** (§20.2), checked by the soak harness and the tests: credits are conserved to the cent
  (total = initial + faucet − sinks), escrow reconciles and is never negative, goods are conserved per good
  (produced = consumed + lost + held in stock, storage and holds), nothing is set aside that isn't there, and
  every signed contract is being performed.
- **Money is `decimal`**, never floating point, so conservation is exact.
- **Text is data** (§11.5). Every sentence the client shows is a template in
  `src/ByGalacticAccord.Presentation/Text/Templates.resx`, filled from structured events and the engine's
  own figures; the journal records typed entries, not prose.

Balance values live in [`SimulationConfig`](src/ByGalacticAccord.Engine/Simulation/SimulationConfig.cs)
and in world generation. They are tuning, moved by reading soak telemetry.
