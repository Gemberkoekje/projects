# Plan — Documentation & Setup-Rule Corrections

## Goal

Correct five inaccuracies raised against `docs/Functional-Overview.md`, fixing the
underlying code first where the code is genuinely wrong, then updating the
documentation so it matches the implemented behaviour.

## Investigation findings (verified against the code)

| # | Topic | Code reality | Verdict |
|---|---|---|---|
| 1 | Balloonist | `characters.json` → `OutsiderDelta delta:1 isStorytellerChoice:true`. `OutsiderDeltaSetupRule` returns both `current` and `current+1 Outsider` when `IsStorytellerChoice` is true. | **Code correct → doc-only fix.** The +1 is optional, not guaranteed. |
| 2 | Huntsman / Damsel | `characters.json` → two rules: `OutsiderDelta delta:1 isStorytellerChoice:true` **and** `RequiresCharacter requiredId:"damsel"` with **no** `autoAddIfMissing`. `DraftEngine.ApplyAutoAddedRequiredCharacters` only injects required characters when `AutoAddIfMissing == true`, injecting them as `IsOutOfScript = true`. | **Damsel is NOT auto-injected** (unlike Choirboy/King). The two rules are **not redundant** — they operate on different axes (see resolved decision below). **Doc-only fix.** |
| 3 | Drunk | `ReplaceTownsfolkSetupRule` converts Townsfolk→Outsider, and when `Townsfolk == 0 && Outsiders > 0` flips to Outsider→Townsfolk (the forced-Drunk display case). | **Code correct → doc-only fix.** The current prose is confusing. |
| 4 | Lunatic | `ReplaceDemonOutsiderDisplayRule` converts Demon→Outsider, and when `Demons == 0 && Outsiders > 0` flips to Outsider→Demon (the forced-Lunatic display case). | **Code correct → doc-only fix.** The current prose is confusing. |
| 5 | Sentinel | `lorics.json` → `StoryTellerChoice` with **three** `OutsiderDelta` options (`-1`, `0`, `+1`). `StoryTellerChoiceSetupRule` unions all three distinct outcomes. | **Code already models three outcomes.** The doc is correct. Only the stale `plan-special-cases.md` shorthand (`±1`) is wrong. No production code change needed. |

## Resolved decision — Huntsman / Damsel (point 2)

**The `OutsiderDelta delta:1` rule and the `RequiresCharacter requiredId:"damsel"` rule
are NOT redundant. Keep both. No code change.** Verified against the code:

- **`SetupCalculator` builds the *target* distribution from setup *rules* only.** The
  Huntsman's `OutsiderDelta delta:1 isStorytellerChoice:true` shifts a *target* slot
  from Townsfolk→Outsider (offering both the +0 and +1 outcomes). The **Damsel
  character has no setup rules**, so the Damsel's presence contributes nothing to the
  target distribution.
- **`DraftMath.ComputeCurrentCounts` builds the *current* tally from actually-chosen
  characters by type.** When the Damsel is chosen, `outsiders++`. The Huntsman (a
  Townsfolk) does `townsfolk++`; its `OutsiderDelta` does **not** touch current counts.

So the two rules live on **different sides of the ledger**: `OutsiderDelta` *enlarges
the target* (the game now needs room for one more Outsider) and the Damsel *fills that
slot in the current tally*. There is **no double-counting**, and removing `OutsiderDelta`
would break feasibility — the target would never grow to accommodate the Damsel.

**Auto-injection:** the Damsel is **NOT** auto-added (unlike Choirboy/King, whose
`RequiresCharacter` sets `autoAddIfMissing: true`). The Damsel is a normal on-script
Outsider that the script author is expected to include alongside the Huntsman. Step 4's
prose must therefore describe **two semantically-linked but separate effects**, not a
single effect, and must state explicitly that the Damsel is not auto-injected.

## Files to touch

- `docs/Functional-Overview.md` — reword entries 1–5.
- `plan-special-cases.md` — correct the stale Sentinel `±1` shorthand to `-1 / 0 / +1`, and check for any other two-outcome Sentinel references.

## Steps

1. Rewrite the **Balloonist** doc row: "Storyteller choice of +0 or +1 Outsider — both outcomes are treated as legal."
2. Rewrite the **Huntsman** doc row to describe **two separate but linked effects**: (a) the `OutsiderDelta` enlarges the target Outsider count, and (b) the Damsel must be in play to fill it — stating explicitly that the Damsel is a normal on-script Outsider and is **not** auto-injected (contrast with Choirboy/King).
3. Rewrite the **Drunk** doc row to: occupies an Outsider slot while shown as a Townsfolk token; in states where all Townsfolk slots are filled but an Outsider slot remains, any Townsfolk offered is implicitly the Drunk and flagged automatically.
4. Rewrite the **Lunatic** doc row to the mirror wording: occupies an Outsider slot while shown as a Demon token; in states where the Demon slot is filled but an Outsider slot remains, the Lunatic is the only valid Demon-token offer and is flagged automatically.
5. Confirm the **Sentinel** doc row (`-1, 0, or +1 Outsider`) is correct and leave it; update the stale `±1` shorthand in `plan-special-cases.md` and scan that file for any other two-outcome Sentinel references.
6. No production code change is required (verified: Balloonist, Drunk, Lunatic, Sentinel code all correct; Huntsman rules are non-redundant and must both stay).
7. Re-read **both** `docs/Functional-Overview.md` and `plan-special-cases.md` end-to-end for consistency, confirming no remaining stale Sentinel shorthand and that all five reworded entries read cleanly.
