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

**S1 (groundwork) is done; S2 is next.** The engine still implements the earlier v0.2 slice: consumers
post haul contracts, AI haulers negotiate, carry the goods and settle through escrow, and transit
incidents make dangerous routes cost something. Most of that game logic changes shape in S3–S8; the
foundation it stands on (the event queue, the seeded random numbers, the money primitives, the
invariants and the soak harness) stays.

There is no client to play yet. The WPF client and the console game were retired in S1 (design D23);
the Blazor WebAssembly client arrives in S2.

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
```

Any `--filter` on the command line replaces the default one, so a filter that matches soak tests runs them.

## Layout

```
src/ByGalacticAccord.Engine/   the whole simulation: no UI, no I/O, deterministic
src/ByGalacticAccord.Cli/      the soak harness
tests/ByGalacticAccord.Tests/  xUnit: fast by default; soak-length tests carry [Trait("Category", "Soak")]
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
  (total = initial + faucet − sinks), escrow reconciles and is never negative, cargo picked up equals
  cargo delivered, lost or still aboard, and no contract is left active without a ship carrying it.
- **Money is `decimal`**, never floating point, so conservation is exact.

Balance values live in [`SimulationConfig`](src/ByGalacticAccord.Engine/Simulation/SimulationConfig.cs)
and in world generation. They are tuning, moved by reading soak telemetry.
