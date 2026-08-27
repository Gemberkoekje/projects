# Vehicle Reserves — what shipped

**To understand this, start by reading
[`TeamReserve.cs`](unity/Assets/RF/Scripts/Objective/TeamReserve.cs) — the whole feature is
that one component — then [`LevelReserve.cs`](unity/Assets/RF/Scripts/Levels/LevelReserve.cs)
for the four numbers a level gives it, then the `OnRunOut` half of
[`Match.cs`](unity/Assets/RF/Scripts/Objective/Match.cs).** Everything else in the diff is
those three facts being threaded through the panel, the level format and the editor.

Asked for directly, off the back of the doors pass; **not** a [MASTER_PLAN.md](MASTER_PLAN.md)
item. It is, however, the exact thing the design document deferred at M6:

> Secondary: destroy all enemy vehicles/base structures. *Deferred as of M6 — it needs either
> a finite vehicle roster or a destructible bunker, and v0.1 has neither.*

This is the finite vehicle roster.

---

## The feature in one paragraph

Each side gets a fixed number of each vehicle for the whole match — **8 jeeps, 3 tanks, 3 ASVs
and 3 helicopters** by default, and whatever the level file says otherwise. Every vehicle
destroyed comes off that stock and nothing puts one back: not the bunker, which repairs and
refuels everything else, and not time. A vehicle a side has none of left stays on the roster
panel, says **NONE LEFT**, and can never be deployed again. **Losing the last jeep loses the
match**, because only a jeep can carry a flag, so a side without one can no longer reach the
only ending that is a win.

| | |
|---|---|
| Default allotment | 8 jeeps · 3 tanks · 3 ASVs · 3 helicopters, per side |
| Where it is set | `"Reserve"` in the level file; the map panel in the level editor |
| What spends one | Anything that destroys a vehicle: shot, drowned, or scuttled by its own pilot |
| What spends nothing | Driving home and parking — the whole reason to make the drive |
| Loss condition | No jeeps left ⇒ the other side wins, `MatchOutcome.OutOfJeeps` |
| Schema | Level format version **3 → 4** |
| Tests | EditMode 438/438, PlayMode 160/160 |

## How to see it

- `reserve-sandbox.png` — the roster panel at the start of a match: `Jeep ×8`, `Tank ×3`,
  `ASV ×3`, `Helicopter ×3`.
- `reserve-roster.png` — a Green bunker part way through a bad afternoon: two jeeps and one
  ASV left, no tanks and no helicopters at all, and the prompt under the roster saying
  **NONE LEFT** instead of **F DEPLOY**.
- `reserve-defeat.png` — the same match one jeep later: **DEFEAT** / **VICTORY**, both halves
  reading `GREEN HAS NO JEEPS LEFT`.

The last two were shot with a one-off `-executeMethod` class that was deleted afterwards, the
way the doors still was. It staged the ending honestly — it spent the reserve through
`TeamReserve.Spend` and let the event end the match, rather than telling `Match` who had won —
which is why that still is evidence about the game and not about the staging.

---

## The decisions worth arguing about

### 1. The level file carries the numbers, and that breaks a stated rule

`LevelDefinition`'s own remarks said a level carries **no balance**: how much a building takes
is `StructureTuning`, how far a flag can be seen is `FlagRules`, and *"two levels that
disagreed about either would be two games wearing one name."* The reserve is the first
exception, and the argument for it is that it is not the same kind of fact:

> How many vehicles a side is given is a **quantity placed on a map**, like how many towers
> there are or how many crossings — not a rule about what one of them does.

A map with one crossing and four jeeps is the same game as a map with three crossings and
twelve, played at a different pitch, which is exactly what the original's missions varied. The
remark in `LevelDefinition` now says so out loud, with "there is exactly one exception" wording
that will read as a warning if somebody tries to make it two.

### 2. One block per level, not one per bunker

`"Reserve"` sits next to `"Bounds"` and applies to both sides. Per-bunker counts were the
obvious alternative and would have been no harder to build, but every other pairing on a map is
placed twice and checked for symmetry, and this way it **cannot** be asymmetric at all. The
cost is that a handicap is not expressible — which is what item 6 of the master plan
(1-Player Mode) would want, and the natural shape of that is a per-bunker *override* of a level
default rather than a change to this.

### 3. Losing is losing the jeeps, not losing everything

The user's rule is "if you lose all your jeeps before you get the enemy flag to your base, you
lose". The implementation says the same thing one level up:

```csharp
if (FlagRules.CanCarry(kind) && Remaining(kind) > 0) { return false; }   // not beaten
```

so what actually ends the match is *having nothing left that could carry a flag*. That reuses
the pillar rather than restating it, and the day a second vehicle may carry a flag this rule
already knows. Two things still say the word "jeep" and would then be wrong:
`MatchOutcome.OutOfJeeps` and the one sentence the HUD prints. Both are named in each other's
doc comments; a test asserts the enum name is still true (`FlagRules.CanCarry` admits only the
jeep).

The match ends **on the transition**, never on the state — a level that starts a side with no
jeeps at all is a broken level for `LevelValidation` to report, not a match that ends on its
own first frame.

### 4. It counts wrecks, not deploys

`TeamReserve` listens to a new static `VehicleHealth.AnyDestroyed` rather than being told by
the bunker the vehicle came out of. That matters in two directions:

- **Every** way of losing a vehicle costs the same one — shot, drowned, or blown up by its own
  pilot in the middle of nowhere. Scuttling used to be free, and the design document calls it
  *"a genuine tension mechanic worth keeping"*; it is a real decision again now that leaving
  the field the fast way costs a vehicle and driving home costs none.
- A vehicle that never came out of a bunker still counts, which is what anything placed on the
  map by something other than a player's roster will be.

The pair to that is that **parking is still free**: `VehicleBay.Stow` does not go through
`Destroyed`, so the swap-at-home loop is untouched.

### 5. `VehicleBayState` did not grow a fifth value

The tempting change was `VehicleBayState.Spent`. It is wrong: that enum answers *where the
vehicle is* — waiting, being repaired, riding out, on the field — and how many are left is a
fact about the side, not about the bay. So the bay is untouched by this whole feature, and the
one place that had to learn the rule is `PlayerVehicleDriver`, which is where a vehicle is
chosen. `CanDeploy` and `TakeTheField` both refuse; `HasOneLeft` and `RemainingOf` are what the
panel reads.

A row that is out of stock **stays on the panel and stays selectable**, for the same reason a
row being repaired does: the answer to "why can I not take the tank" has to be somewhere the
player can read it, and a row that vanished would take the answer with it.

### 6. A scene with no reserve has no limit

`TeamReserve.LeftFor(side, kind)` answers `int.MaxValue` when the scene keeps no reserve, so
every caller compares rather than special-casing. This is not a nicety: almost every test in
this project assembles a vehicle with no map behind it, and a missing reserve that answered
"none" would empty every bunker in the game the moment a scene was built without one. The HUD
shows no count at all in that case rather than a large number — a sandbox is not a side with a
million jeeps, it is a side nobody is counting.

### 7. `Match.FlagTaken` became `Match.Beaten` + `Match.Outcome`

There are two endings now and the panel has to tell them apart, so the winner is recorded with
*who it was won against* and *how*. `Win(side, stolen)` became `Win(side, loser, how)` and
refuses a win with no ending named. The HUD names the loser in both cases, which is the same
choice the old field made: the winner is already on the line above in letters an inch high.

---

## What was built

| File | What changed |
|---|---|
| `Scripts/Objective/TeamReserve.cs` | **New.** One per side, on that side's bunker. Counts wrecks, answers how many are left, raises `AnyBeaten`. |
| `Scripts/Objective/MatchOutcome.cs` | **New.** The two ways a match can end. |
| `Scripts/Levels/LevelReserve.cs` | **New.** The four counts as they appear in a level file, plus the standard allotment. |
| `Scripts/Vehicles/VehicleRoster.cs` | **New.** The four vehicles, in roster order, in the *runtime* assembly — `VehiclePrefabBuilder.Roster()` now delegates to it instead of keeping a second copy the game could not see. |
| `Scripts/Combat/VehicleHealth.cs` | Static `AnyDestroyed`, raised alongside the instance event. |
| `Scripts/Objective/Match.cs` | `Beaten` + `Outcome`; wins by attrition through `TeamReserve.AnyBeaten`. |
| `Scripts/Core/Teams.cs` | `OpponentOf`, which answers `Team.None` rather than guessing when a match is not two-sided. |
| `Scripts/Levels/LevelDefinition.cs` | Schema **4**; the `Reserve` field, and the "exactly one exception" wording. |
| `Scripts/Levels/LevelBuilder.cs` | Builds a stocked `TeamReserve` onto each bunker. |
| `Scripts/Levels/LevelValidation.cs` | A level that gives nobody a jeep, or a negative stock of anything, is refused. |
| `Scripts/Players/PlayerVehicleDriver.cs` | `RemainingOf` / `HasOneLeft`; `CanDeploy` and `TakeTheField` refuse a vehicle the side has none of. |
| `Scripts/UI/PlayerHud.cs` | `×N` beside each roster row, `NONE LEFT`, the prompt that distinguishes "wait" from "never", `YOUR LAST JEEP` while driving, and the result line for both endings. |
| `Scripts/Editing/EditorInspector.cs` | Four more rows on the map panel, as a loop over the roster; `RowCount` 8 → 10. |
| `StreamingAssets/Levels/iron-channel.json` | Version 4, an explicit standard allotment, and a paragraph in the description saying what eight jeeps means on this map. |
| `Tests/PlayMode/ReserveTests.cs` | **New.** Seven tests: what a wreck costs, what parking costs, what scuttling costs, a row that has run out, the last jeep, the last tank, and a scene with no reserve at all. |
| `Tests/PlayMode/MainMenuTests.cs` | Unloads the scenes it opens — see the gotcha below. |
| `Tests/EditMode/*` | The format round trip, the older-file default, roster completeness, the two new validation rules, the shipped map's allotment, the sandbox's two reserves, and the inspector's row budget. |

---

## Gotchas

**A test class was leaving a whole match loaded, and this feature is what noticed.**
`MainMenuTests` opens `MainMenu`, `Sandbox` and `LevelEditor` with `LoadSceneMode.Single` and
never unloaded them, so every class that ran after it inherited an entire map — two bunkers,
both flags, and now two reserves. `TeamReserve.For(Team.Green)` then answered with *that map's*
Green reserve, and two of the new tests failed against numbers they never wrote. Three sibling
classes already paid this debt (`LevelLoadingTests`, `SplitScreenTests`, `LevelEditorTests`);
`MainMenuTests` now does too. Anything that looks things up statically by team — `Flag.Of`,
`TeamBunker.For`, `SupplyPoint.HomeFor` — has the same exposure, so it is worth knowing that
the failure looks like a bug in whichever class runs alphabetically next.

**A level file that says nothing about vehicles gets the standard allotment, and that rests on
`JsonUtility` keeping field initializers for absent fields.** It does — a field the JSON does
not mention keeps whatever the constructor put there — and there is now a test that says so by
parsing a two-key version 3 file, because the failure mode is the quiet one: a side that starts
a match with nothing to drive and no message anywhere saying why.

**The seventh trap from the batch-testing notes applies to every still here.** This machine has
a personal save of `iron-channel` in `AppData/LocalLow`, which `LevelLibrary.PathFor` prefers,
and it is a version 3 file with no reserve in it. Every still above therefore shows the
*defaults* (8/3/3/3) rather than the shipped file's block — the same numbers, arrived at the
other way. Render against `ShippedPathFor` to check the shipped map's own values.

**`RowCount` was raised to 10 and the inspector has about 48 canvas units of headroom left**
before the rows run into the Problems panel. A test asserts the panel is long enough for the
map's own rows (five, plus one per vehicle), because a row past the end is dropped in silence.

---

## Deliberately not done

- **No per-side allotment.** See decision 2. The natural home for a handicap is 1-Player Mode.
- **The generator does not vary the allotment.** `LevelGenerator` produces maps with the
  standard reserve whatever the difficulty. Fewer jeeps is one of the cheapest difficulty
  levers this game has, and it is still available.
- **You cannot see how many the enemy has left.** Each player sees only their own counts. The
  original showed both, and there is a real argument that attrition is only tense when you can
  watch it happening to somebody else — the objective strip at the top of each half is where
  that line would go.
- **A vehicle nobody can ever deploy still spends four seconds being repaired.** Harmless
  (the panel says `NONE LEFT` over the top of it) and not worth a branch in `VehicleBay`.
- **Nothing warns you at two jeeps.** The driving HUD says `YOUR LAST JEEP` at one, and the
  roster panel counts. Between those there is no escalation.
