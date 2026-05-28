# The Curatool — Special Cases Implementation Plan

A focused, phase-style plan covering **only the special-case handling that is not yet implemented** in the current workspace.

Audit baseline (already in place — do **not** re-implement):

- `OutsiderDeltaSetupRule` — Baron (+2).
- `StoryTellerChoiceSetupRule` over `OutsiderDelta` — Godfather, Balloonist, Huntsman (used as ±/0..+1 outsider choice).
- `MinionSwapSetupRule` — Lil' Monsta.
- `ReplaceTownsfolkSetupRule` — Drunk.
- `RequiresCharacterSetupRule` — Huntsman → Damsel.
- `MarionetteSessionAdjustmentRule` — pre-draft toggle (+1 Townsfolk / −1 Minion).
- `LoricSetupRule` — Sentinel (±1 Outsider via storyteller choice).
- `AtheistFirstPickConstraint` — including Drunk-override bypass.
- `BlockedIfAnyChosenOfTypeConstraint` — Kazali, Summoner.
- Hidden flag math in `DraftEngine` / `DraftMath` / `SetupCalculator` — Drunk Townsfolk → Outsider slot, Lunatic Demon → Outsider slot, mandatory non-Lunatic Demon completion invariant.
- Script validation in `ScriptParser` — error on no Townsfolk; warnings on missing Demon/Minion/Outsider.

Everything below is either **missing entirely**, **partially modelled**, or **encoded incorrectly** versus the reference doc.

---

## High-Level Gap Summary

| Area | Gap |
|---|---|
| Setup math | Fang Gu +1 Outsider, Vigormortis −1 Outsider, Summoner ReplaceDemon, Hermit, Lord of Typhon unconstrained Outsider, Godfather true ±1 semantics |
| Required pairs | Choirboy → King (with out-of-script auto-add) |
| Hidden flags | Setup rules of a hidden-Drunk/Lunatic token are not suppressed for draft math |
| Marionette token semantics | Marionette token is fully excluded from the draft; pre-draft toggle remains the only setup path |
| Legion | Game mode + Legion-ratio distribution + "Evil" offer flow + ST-assigned evil resolution |
| Non-Legion "Evil" offer | Optional ST-driven evil offer for any script |
| Dynamic setup | `dynamic-setup` flag on Alchemist / Boffin + post-draft ST count confirmation prompt |
| Validation | Choirboy/King handshake; Legion-on-script + Legion=no silent exclusion |
| Summary UX | Unresolved Minion slots (Kazali / Lord of Typhon / Legion) shown as "ST-assigned night one" / "Evil (ST-assigned)" |

---

## Phase S1 — Setup Rule Coverage Fixes

**Goal:** Bring `characters.json` and the rule kinds into full alignment with the reference table.

**Deliverables:**

- New rule kind `ReplaceDemonSetupRule` (−1 Demon, +1 Townsfolk).
- **Marionette handling (replaces the previous `MinionReplacementSetupRule` plan):** Marionette is never a randomization/curation offer **and** never a chosen draft token. The only way to have a Marionette in a session is the pre-draft toggle (`MarionetteSessionAdjustmentRule`, already implemented). Therefore:
  - The Marionette character is **excluded** from `GetRemainingValidCharacters`, `SuggestThree`, and `CreateCuratedOffer` for all sessions.
  - No `MinionReplacementSetupRule` is introduced. The pre-draft toggle remains the single source of Marionette setup math.
  - This removes the duplicate-application concern entirely — the toggle and a chosen token can never both be active because the token can never be chosen.
- New rule kind `UnconstrainedOutsiderDeltaSetupRule` (Lord of Typhon: open-ended ST-choice range; emits a "defer to ST" marker in `DraftMath` rather than discrete outcomes).
- New rule kind `MinionDeltaSetupRule(+1)` for Lord of Typhon's extra Minion slot.
- Update Godfather encoding so its choice yields `{−1, +1}` (NOT `{0, +1}`). Either:
  - Extend `OutsiderDeltaSetupRule` with a `symmetric: true` flag, or
  - Express via `StoryTellerChoice` over two `OutsiderDelta` rules.
  - Preferred: explicit `StoryTellerChoice` composition for clarity.
- Update `CharacterDatabase` JSON loader to register the new rule kinds.

**characters.json changes:**

- `fang_gu` — add `OutsiderDelta delta=+1`.
- `vigormortis` — add `OutsiderDelta delta=-1`.
- `summoner` — add `ReplaceDemon` rule (in addition to existing Demon-blocked availability).
- `godfather` — replace current `OutsiderDelta delta=1 isStorytellerChoice=true` with `StoryTellerChoice` of `[OutsiderDelta(-1), OutsiderDelta(+1)]`.
- New entry `hermit` — type `Outsider`, `StoryTellerChoice` of `[NoOp, SwapOutsiderForTownsfolk]` where the swap atomically applies `−1 Outsider` and `+1 Townsfolk`.
- New entry `lord_of_typhon` — type `Demon`, rules `UnconstrainedOutsiderDelta` and `MinionDelta(+1)`, availability `BlockedIfAnyChosenOfType=Minion`.
- `marionette` — keep the entry for script-presence/script-validation purposes only. Add no setup rules. Mark it `IsDraftExcluded = true` (new boolean on `CharacterDefinition`, default `false`) so the draft engine skips it everywhere.
  - Add an engine-level regression test that asserts any character flagged `IsDraftExcluded = true` is filtered out of `GetRemainingValidCharacters`, `SuggestThree`, and `CreateCuratedOffer` before any character-specific special-case logic runs.

**Acceptance:**

- `SetupCalculator` produces correct expanded outcomes for each new/updated character.
- New unit tests in `SetupCalculatorTests`:
  - Fang Gu adds an Outsider.
  - Vigormortis subtracts an Outsider.
  - Summoner script produces a valid distribution with 0 Demons.
  - Godfather choice yields exactly two distinct outcomes (−1 and +1).
  - Hermit choice yields two outcomes (no change vs −1 Outsider +1 Townsfolk).
  - Lord of Typhon flagged as "ST-defer Outsider" in `DraftMath`.
  - Marionette is never returned by `GetRemainingValidCharacters`, `SuggestThree`, or `CreateCuratedOffer`, regardless of script content.

---

## Phase S2 — Required-Pair: Choirboy ↔ King

**Status:** ✅ Completed

**Goal:** Implement the Choirboy / King handshake including the **out-of-script auto-add** case.

**Deliverables:**

- New `RequiresCharacterSetupRule(requiredId="king", autoAddIfMissing=true)` flag on the existing rule (or a sibling `RequiresAndAutoAddCharacterSetupRule`).
- `DraftEngine` behavior: when Choirboy is chosen and `king` is not in the script, inject King as an **out-of-script** `CharacterDefinition` (mark with `IsOutOfScript = true`) into the active session's available pool and into the makeup summary.
- Add `IsOutOfScript` (default `false`) on `CharacterDefinition` (or a session-level override list) — model only what is needed; avoid nullable.
- `Script` model: expose a derived "effective script" view that includes any out-of-script additions.

**characters.json changes:**

- New entry `choirboy` — type `Townsfolk`, rule `RequiresCharacter requiredId="king" autoAddIfMissing=true`.
- New entry `king` — type `Townsfolk`, no setup rule.

**Acceptance:**

- Unit tests:
  - Choirboy + King both on script + neither chosen → both remain available normally.
  - Choirboy chosen + King already on script → no change.
  - Choirboy chosen + King NOT on script → King is added as out-of-script and becomes available / mandatory per existing required-pair priority.
- Summary clearly distinguishes out-of-script additions.

---

## Phase S3 — Hidden Flag Setup Suppression

**Status:** ✅ Completed

**Goal:** Honor "When a token is flagged Drunk or Lunatic, its setup rules are ignored for draft math purposes."

**Deliverables:**

- In `DraftMath` / `SetupCalculator`, when iterating chosen tokens to apply their `ISetupRule`s, skip the rule application for any `PlayerChoice` whose `HiddenFlags.IsDrunk` or `HiddenFlags.IsLunatic` is set.
- Keep the existing type-counting reassignment (Drunk → Outsider slot, Lunatic → Outsider slot) intact.

**Acceptance:**

- Unit tests:
  - A Baron flagged Drunk does NOT add +2 Outsiders.
  - A Fang Gu flagged Lunatic does NOT add +1 Outsider, and an additional real Demon is still mandatory (existing invariant).
  - A Godfather flagged Drunk does NOT produce ±1 Outsider outcomes.
  - Atheist flagged Drunk continues to pass the first-pick constraint (already covered) — regression check only.

---

## Phase S4 — Legion Game Mode

**Goal:** Add Legion as a first-class binary game mode with its own distribution math and an "Evil" offer flow that behaves like a hidden borrowed-ability choice until Legion mode is committed.

**Deliverables:**

- `SessionSetupOptions.IsLegionGame` (`bool`, default `false`).
- `SessionSetupOptions.LegionCount` (`int`, default `0` when Legion mode is off).
- **Default `LegionCount`** (used when Legion mode is enabled and the ST hasn't overridden it):
  - Start from the normal setup's good-player total for the selected `playerCount` (that is, the normal Townsfolk + Outsider count).
  - Use that as the default Legion count, because Legion reverses the normal balance.
  - Example: if the normal setup for 10 players would have 7 good and 3 evil, then the default LegionCount is 7 (roughly 7 Legion and 3 good).
  - The storyteller may freely raise or lower this number if they want more or fewer Legion; the UI seeds the input with the default and accepts manual edits.
  - There is no static `LegionDistributionTable`; the count is derived from the normal good-count baseline + ST override.
- `Setup.razor` exposes the **"Legion game?"** checkbox only when `legion` is on the script, plus the `LegionCount` numeric input when the checkbox is on.
- `SetupCalculator` Legion branch:
  - When `IsLegionGame = true`, replace the standard 5–15 distribution with: `Legion = LegionCount` evil slots (all Legion tokens), and the remaining `playerCount − LegionCount` good slots distributed as Townsfolk/Outsider using the normal Outsider count expectations for that good-slot subtotal.
  - When `IsLegionGame = false` and `legion` is on the script, silently exclude `legion` from the draft pool.
- `DraftEngine`:
  - For evil slots when Legion is available, `SuggestThree` / `CreateCuratedOffer` may return the sentinel choice `"evil"` instead of a specific demon/minion.
  - The "evil" sentinel behaves like a hidden choice: the engine resolves it only to demons/minions that are valid for the current board state, and it is presented to the player simply as "Evil".
  - If `IsLegionGame = true`, the hidden choice is no longer relevant because evil assignments resolve directly to Legion and the offer no longer branches into demon/minion sub-choices.
  - `SessionSetupOptions.LegionCount` (or equivalent explicit setup input) records how many Legion tokens the storyteller wants when Legion mode is enabled; default is a majority of players (for example 9 players = 5 Legion, 12 players = 7 Legion).
  - `RecordChoice` for an evil slot stores `chosenCharacterId = "evil"`, `hiddenFlags = empty`.
  - `ResolveEvilSlot(sessionId, draftOrder, actualCharacterId, hiddenFlags)` is idempotent by `(SessionId, DraftOrder)` and ST-only; retries overwrite with the same resolved state and do not duplicate slots.
  - `MakeupSummary` flags evil slots with `chosenCharacterId == "evil"` as `"Evil (ST-assigned)"`.
- New entry `legion` in `characters.json` — type `Demon`, no standard setup rule (handled by the Legion branch), no availability constraint.

**Acceptance:**

- Unit tests:
  - Legion on script + Legion=no → `legion` excluded from `GetRemainingValidCharacters`.
  - Legion on script + Legion=yes → distribution uses `LegionCount` for evil slots and the formula default; evil slots are offered only "Evil".
  - Default `LegionCount` formula: 9 players → 6, 10 players → 7, 11 players → 7, 12 players → 8, 15 players → 9.
  - ST override of `LegionCount` is honored by the distribution.
  - `ResolveEvilSlot` updates the slot's stored character and is reflected in summary.
  - Summary shows unresolved evil slots distinctly.

---

## Phase S5 — Non-Legion "Evil" Offer

**Status:** ✅ Completed

**Goal:** Allow the ST to deliberately include "Evil" as one of the offered options for any slot on any script.

**Deliverables:**

- Extend `CreateCuratedOffer` so the offered set may include the sentinel `"evil"` alongside real character ids.
- Extend `RecordChoice` to accept `"evil"` as a valid chosen id (stored as-is, hiddenFlags empty) and to bypass character-database validation for that sentinel only.
- UI: Curate panel on `Draft.razor` gains an "Add Evil option" toggle.
- Reuse the `ResolveEvilSlot` action from Phase S4.

**Acceptance:**

- Unit tests:
  - Curated offer `["fortune_teller", "goon", "evil"]` is accepted.
  - Choosing `"evil"` stores it and is later resolvable via `ResolveEvilSlot`.

---

## Phase S6 — Unresolved Minion Slots (Kazali / Lord of Typhon)

**Goal:** When Kazali or Lord of Typhon is chosen, mark all remaining Minion slots as ST-assigned and remove them from the active draft queue.

**Deliverables:**

- `DraftEngine`: on `RecordChoice` of Kazali or Lord of Typhon:
  - Remove all not-yet-drafted Minion slots from the active queue.
  - Add corresponding entries in `MakeupSummary` as `"ST-assigned night one"`.
- For Lord of Typhon, also add **one extra** ST-assigned Minion slot to reflect the +1 Minion rule.
- `ResolveMinionSlot(sessionId, slotKey, characterId)` is idempotent by `(SessionId, SlotKey)` and ST-only; retries overwrite with the same resolved state and do not duplicate slots. Same semantics as `ResolveEvilSlot`.
- The resolved character id from `ResolveMinionSlot` is stored on the corresponding `PlayerSlotEntity` in the same column used for borrowed/resolved assignments — see Phase S10 (reuses `BorrowedAbilityCharacterId` as the single "ST-resolved-character" column for both dynamic-setup slots and ST-assigned Minion/Evil slots).

**Acceptance:**

- Unit tests:
  - Picking Kazali mid-draft drops all remaining Minion slots into unresolved state.
  - Picking Lord of Typhon adds +1 unresolved Minion slot beyond the base count.
  - `ResolveMinionSlot` fills them in and they appear in the final summary.

---

## Phase S7 — Dynamic-Setup Flag (Alchemist / Boffin) + Inline Ability Assignment

**Goal:** Surface ST-runtime setup uncertainty AND let the ST resolve it immediately at the moment of choice by picking the actual borrowed ability. Once picked, that ability's `ISetupRule` is applied as if it had been on the character from the start.

### S7.1 — Data model

- Add `IsDynamicSetup` boolean on `CharacterDefinition`, default `false`.
- JSON: new field `"dynamicSetup": true` parsed by `CharacterDatabase`.
- New entries:
  - `alchemist` — Townsfolk, `dynamicSetup: true`, borrows from **not-in-play Minion** abilities on the script.
  - `boffin` — Minion, `dynamicSetup: true`, borrows from **not-in-play Townsfolk or Outsider** abilities on the script.
- Add `DynamicAbilityScope` enum on `CharacterDefinition`:
  - `Unknown = 0`
  - `NotInPlayMinion` (Alchemist)
  - `NotInPlayTownsfolkOrOutsider` (Boffin)
- `PlayerChoice` (or a sibling state record) gains `BorrowedAbilityCharacterId` (string, default `""`) representing the assigned ability. Empty string = unassigned.

### S7.2 — Engine API

- `AbilityOption` record:
  ```
  AbilityOption(
      string AbilityCharacterId,
      string DisplayName,
      bool IsAvailable,
      string UnavailableReason)
  ```
- `IReadOnlyList<AbilityOption> DraftEngine.GetAlchemistAbilityOptions(Guid sessionId, int playerSlotIndex)`.
- `IReadOnlyList<AbilityOption> DraftEngine.GetBoffinAbilityOptions(Guid sessionId, int playerSlotIndex)`.
- Both methods:
  1. Resolve the candidate set from the **current script** filtered by scope (Minion vs Townsfolk/Outsider) AND filtered to **not-already-chosen** characters.
  2. For each candidate, run `SetupCalculator` speculatively: clone the session's current counts, apply the candidate's `ISetupRule` (if any), then check feasibility against the remaining unchosen pool and remaining seats. Abilities with no setup rule short-circuit as always `IsAvailable = true`.
  3. Populate `UnavailableReason` with a concise human string (examples below). Empty string when available.
- `GameSession DraftEngine.AssignDynamicAbility(Guid sessionId, int playerSlotIndex, string abilityCharacterId)`:
  - Validates the slot's chosen character has `IsDynamicSetup = true`.
  - Validates `abilityCharacterId` matches scope, is not already chosen as a real draft token by another player, and is currently `IsAvailable = true` (re-runs the speculative check to avoid stale-state acceptance).
  - Stores `BorrowedAbilityCharacterId` on the slot.
  - Marks the borrowed ability as consumed for future draft availability so it can no longer be offered as a real pick to another player.
  - Applies the borrowed character's `ISetupRule` to the session's effective counts via the same path that normal chosen characters use, so all downstream math (`DraftMath`, `MakeupSummary`, availability checks for later slots) sees it.
- `DraftMath` exposes `RequiresStorytellerSetupConfirmation = true` while any dynamic-setup slot still has `BorrowedAbilityCharacterId == ""`. Clears once all are assigned.

### S7.3 — Feasibility examples (greying-out reasons)

These are the canonical `UnavailableReason` strings the UI will surface:

- Alchemist + Baron — "Not enough Outsiders remaining on the script to satisfy +2 Outsider count."
- Alchemist + Baron — "Not enough remaining seats to add 2 Outsiders."
- Alchemist + Godfather — "No Outsider can be added or removed to satisfy ±1."
- Boffin + Huntsman — "Damsel is not on the script."
- Boffin + Huntsman — "Damsel is already chosen, cannot satisfy required-pair."
- Boffin + Fang Gu-style +1 Outsider ability — "Not enough Outsiders remaining on the script."
- (Catch-all) "Resulting counts cannot be satisfied by the remaining script."

### S7.4 — UI flow

- `Draft.razor`: when `RecordChoice` returns a slot with `IsDynamicSetup = true`, surface an inline panel (NOT a blocking modal) listing all `AbilityOption`s for that slot:
  - Available options are clickable.
  - Unavailable options are rendered disabled with a tooltip showing `UnavailableReason`.
  - Confirming an option calls `AssignDynamicAbility` and refreshes the live makeup summary.
- The next slot's draft is **not blocked** by an unresolved dynamic ability — the ST may continue and resolve it later from the summary, but `RequiresStorytellerSetupConfirmation` will keep nagging.
- `Summary.razor`:
  - For each Alchemist/Boffin slot, show "Alchemist (as Baron)" / "Boffin (as Huntsman)" formatting.
  - If still unassigned, show the same inline ability picker for late resolution.
  - Non-blocking banner: "Confirm effective counts — N borrowed abilities still unassigned."

### S7.5 — Persistence

- `PlayerSlotEntity.BorrowedAbilityCharacterId` (string, default `""`).
- Round-trip in `GameSessionRepository` mapping (both directions).
- Migration: `AddBorrowedAbility` (can be folded into the Phase S10 `AddSpecialCaseFields` migration).

### S7.6 — Acceptance

Unit tests in `DraftEngineTests`:

- Choosing Alchemist sets `RequiresStorytellerSetupConfirmation = true` until `AssignDynamicAbility` is called.
- `GetAlchemistAbilityOptions` returns only Minions present on the script, not already chosen as real draft tokens, and not already consumed as a borrowed ability.
- `GetBoffinAbilityOptions` returns only Townsfolk/Outsiders present on the script, not already chosen as real draft tokens, and not already consumed as a borrowed ability.
- `GetAlchemistAbilityOptions` greys out Baron when remaining Outsiders on the script are < 2, with the correct reason string.
- `GetAlchemistAbilityOptions` greys out Godfather when neither +1 nor −1 Outsider can be satisfied.
- `GetBoffinAbilityOptions` greys out Huntsman when Damsel is not on the script.
- `GetBoffinAbilityOptions` greys out Huntsman when Damsel is already chosen.
- `AssignDynamicAbility` rejects an unavailable ability.
- `AssignDynamicAbility(Alchemist, Baron)` causes the session's effective Outsider target to increase by 2 (and Townsfolk by −2), reflected in `DraftMath` and `MakeupSummary`.
- `AssignDynamicAbility(Alchemist, <ability with no setup rule>)` leaves counts unchanged and clears the confirmation flag for that slot.
- Round-trip persistence: a session with an Alchemist + assigned Baron ability rehydrates with both fields intact.
- `DraftSessionStateTests` extended for the inline picker flow and the summary-late-resolution flow.

---

## Phase S8 — Script Validation Updates

**Goal:** Round out `ScriptParser` validation per the reference doc.

**Deliverables:**

- When Choirboy is on the script but King is not: emit an informational diagnostic (not error/warning) that King will be auto-added if Choirboy is drafted.
- When Legion is on the script: emit informational diagnostic noting the Legion-game checkbox will appear in Setup.
- No new errors beyond the existing "no Townsfolk" error.

**Acceptance:**

- `ScriptParserTests` extended with fixtures for Choirboy-without-King and Legion-on-script cases.

---

## Phase S9 — Web UI Surfacing

**Goal:** Make all of the above visible and operable in the Blazor UI.

**Deliverables:**

- `Setup.razor`: Legion checkbox (gated by script content).
- `Draft.razor`:
  - "Add Evil option" toggle on the Curate panel.
  - Visual marker on unresolved ST-assigned slots.
- `Summary.razor`:
  - Unresolved evil/Minion slot resolution UI (dropdown + Save → `ResolveEvilSlot` / `ResolveMinionSlot`).
  - Dynamic-setup confirmation banner for Alchemist / Boffin.
- `DraftSessionState`: pass-through methods for the new engine actions.

**Acceptance:**

- `DraftSessionStateTests` extended for: Legion start, evil-slot resolution, Minion-slot resolution, dynamic-setup banner state.
- Manual smoke test path: Legion script → Setup → Draft → Summary resolves cleanly.

---

## Phase S10 — Persistence Round-Trip

**Goal:** Persist all new fields/flags so sessions resume correctly.

**Deliverables:**

- `GameSessionEntity`: add `IsLegionGame` and `LegionCount`.
- `PlayerSlotEntity`:
  - Ensure `chosenCharacterId = "evil"` round-trips.
  - Add `IsStAssigned` flag for ST-assigned-but-unresolved slots (Kazali/Lord of Typhon Minion slots and Legion/Evil-offer evil slots prior to resolution).
  - Add `IsOutOfScript` marker if needed for Choirboy/King handshake.
  - Reuse a neutral ST-assigned character column on the entity — rename `BorrowedAbilityCharacterId` to `ResolvedCharacterId` if the model is being revised, otherwise add a code comment that the existing column intentionally stores three concepts:
    1. Alchemist/Boffin borrowed ability id.
    2. `ResolveMinionSlot` resolved Minion id (Kazali / Lord of Typhon).
    3. `ResolveEvilSlot` resolved evil character id (Legion mode or ST-offered "evil").
  - Empty string (`""`) means unassigned/unresolved; combined with `IsStAssigned` it disambiguates from a normal drafted slot.
- Migration: `AddSpecialCaseFields` (folds in `AddBorrowedAbility`).
- Update `GameSessionRepository` mapping in both directions.

**Acceptance:**

- Existing persistence tests pass.
- New tests:
  - Legion session round-trips with evil sentinels and `LegionCount`.
  - Unresolved ST-assigned slot (`IsStAssigned = true`, `BorrowedAbilityCharacterId = ""`) round-trips.
  - Resolved ST-assigned Minion slot (e.g. `ResolveMinionSlot(..., "poisoner")`) round-trips with `BorrowedAbilityCharacterId = "poisoner"`.
  - Resolved evil slot (e.g. `ResolveEvilSlot(..., "imp")`) round-trips with `BorrowedAbilityCharacterId = "imp"`.
  - Out-of-script King addition round-trips.

---

## Phase S11 — Documentation

**Goal:** Update `README.md` and `plan.md` "Implementation Status" with a new section listing each S-phase as it lands.

**Deliverables:**

- README "Special Cases" section linking to this plan.
- `plan.md` `## Implementation Status` gains `Phase S1`…`Phase S11` entries when completed.

---

## Cross-Cutting Notes

- **No nullables**: follow workspace rule. Use sentinels (e.g. `"evil"`, `IsOutOfScript`, `IsStAssigned`, `IsDynamicSetup`) instead of nullable references.
- **Enums**: any new enum (e.g. `EvilOfferMode`) must include an explicit `Unknown = 0`.
- **Explicit usings**: add to `GlobalUsings.cs` or local files; do not rely on implicit usings.
- **Suppression**: resolve any new warnings via real fixes; if unavoidable, suppress in `.globalconfig` with a comment.
- **Priority rule** (already noted in reference doc): required-pair constraints take precedence over type-count ceilings. Confirm Phase S2 honors this for Choirboy/King.
- **Marionette exclusivity rule:** Marionette is setup-only via the pre-draft toggle. The character is never offered, never chosen, never carries a setup rule of its own. This guarantees `MarionetteSessionAdjustmentRule` can never be applied twice in one session.
- **Suggested implementation order:** S1 → S3 → S2 → S8 → S6 → S4 → S5 → S7 → S9 → S10 → S11.
