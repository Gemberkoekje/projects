# M9 — Clearing the near-term backlog

**To understand this, start by reading** `unity/Assets/RF/Scripts/Vehicles/HelicopterMotion.cs`,
`unity/Assets/RF/Scripts/Destruction/AutoTurret.cs` and
`unity/Assets/RF/Scripts/Destruction/Destructible.cs`. Those three files are the whole of what
changed in kind; everything else in the diff is the consequences being threaded through.

This phase is not a design-document milestone. It is section 10 of
`return-fire-homage-design-doc.md` — the "near-term backlog" that had been accumulating since
M7 — done in one pass, and that section is now empty.

| Backlog item | What it turned into |
|---|---|
| Helicopter: lock to a fixed flight altitude; remove manual up/down | One `CruiseAltitude`, no `Lift` axis anywhere, and a new answer to what "home" means for an aircraft |
| Automated turrets that shoot at the enemy team | `StructureKind.Turret`, `AutoTurret`, a Blender asset, and four of them on the shipped map |
| A completely destroyed structure should lose its collider entirely | Six lines in `Destructible.Enter`, and a dropped bridge that finally means something |
| Audit that every `StructureKind` is actually destructible end-to-end | Two new tests that check the enum, the roster, the tuning table and the model inventory against each other |

---

## 1. The helicopter flies at one altitude

`FlightTuning` used to carry a floor, a ceiling and a deploy height — the two ends of a choice
and where the choice started. It now carries **one** number, `CruiseAltitude`, plus
`GroundedAltitude` for an aircraft that has run out of fuel. `HelicopterMotion.Step` takes a
`bool powered` where it took a collective, and the climb model survives only because the
aircraft still has to *get* to its altitude: off the pad on deploy, and back up after
something shoves it. A helicopter that snapped to its height would look like a teleport in
exactly those two places.

**`CruiseAltitude` is 10 m, which is the altitude the helicopter already deployed at.** Locking
the collective therefore changed where a helicopter actually sits by nothing at all — it only
took away the ability to move off it. That was deliberate: it keeps this change about the
control scheme rather than about the camera, the combat ceiling or how visible an aircraft is.

### The thread the design document warned about, and how it was pulled

The backlog entry said the hard part was not `HelicopterMotion` but everything that assumed a
helicopter *descends onto* something to be home. It was right. `SupplyPoint.Serves` had

```csharp
if (aircraft && (!servesAircraft || at.y > landingAltitude))
```

and with one altitude that gate means *no aircraft is ever served anywhere* — no refuelling,
no rearming, no swapping vehicles, and (through `HomeFor`) no winning. So the height half is
gone and **a helicopter is served hovering over its own bunker**.

What survives is `ServesAircraft`, which only a bunker has. That is the drawback the design
document's roster table actually gives the helicopter — it is the one vehicle that cannot
rearm at a field depot and has to fly all the way home — and it never needed the altitude
check to be true. The `landingAltitude` field is deleted rather than left at zero, because a
tuning number nothing reads is a promise the code is still making.

**One play-mode test was asserting the old rule and had to be rewritten rather than fixed.**
`SupplyTests.AHelicopterHasToComeDownOntoThePadToBeServed` said, in its own name, exactly the
thing that is no longer true, and it was invisible to a grep for `landingAltitude` because it
asserted through fuel rather than through the API. It is now
`AHelicopterIsServedHoveringOverItsOwnBunker`, and its remarks carry the reasoning above so
the next person to read it does not think the rule was quietly dropped. A second test was
added alongside it — `ABunkerServesAnAircraftAtEitherOfTheHeightsItCanBeAt` — because the two
heights an aircraft can now be at, cruising and grounded-out-of-fuel, must not become two
different rules.

### Where the descent went

A dry helicopter still comes down; leaving one hanging over the map as permanent cover was
never the intent. But it used to be expressed by `VehicleSupply.Stranded` writing `-1` into
the pilot's collective, and there is no collective to write to any more. `VehicleSupply` now
tells the aircraft straight out, through `Helicopter.SetPowered(bool)` — it already held a
`Helicopter` reference for `IsAircraft`, so this is a call it was one line away from making.
`Stranded` lost its `aircraft` parameter and is now one rule for all four vehicles.

**What a hover costs is now the idle draw and nothing else.** `VehicleSupply.DemandFrom` used
to be `max(throttle, collective)`, so a hovering pilot was charged for holding an axis. With
the axis gone the charge had to sit somewhere it cannot be avoided, and the roster table had
already put it there: the helicopter's `IdleFuelDraw` is 0.55 against about 0.15 for everything
that drives. No number changed; a test now says why that one is three times the others.

### Files this touched that are not about flying

- `VehicleInput` lost `Lift` entirely, and with it one constructor overload. Every call site
  is a compile error rather than a silent behaviour change, which is why removing the field
  was safer than leaving it at zero.
- `IronFlagControls.inputactions` lost the `Lift` action and its seven bindings, freeing
  <kbd>Space</kbd>, <kbd>Ctrl</kbd>, <kbd>C</kbd>, and gamepad South/East.
  `ControlAssetTests` gained a `Retired` list so the action cannot come back through the
  inspector window, where the asset is actually edited.
- `CombatPlane.Ceiling` and `WaterLine`'s remarks both explained themselves in terms of
  `MinAltitude`/`MaxAltitude`. Both now read against `CruiseAltitude`/`GroundedAltitude`,
  which is also the check that both altitudes a helicopter can be at are above the drowning
  line.

---

## 2. Rubble is not solid

M5 wrote this down as a known wrong and gave its reason:

> **Rubble stops being cover for fire, but not for driving.** […] Making it drivable would
> mean either throwing away the colliders — a vehicle driving through visible geometry — or
> giving the ground vehicles the ability to climb, which is a terrain question and belongs to
> M7.

The first horn of that dilemma turns out to be fine. Every destroyed model in the asset spec
is a knee-high pile a tank would ride over anyway, so "driving through visible geometry" reads
as driving *over* it. `Destructible.Enter` now switches off the destroyed model's colliders
when it shows it, and back on if `Restore` puts the structure up again.

**The bridge is where this matters most.** A dropped bridge used to leave a solid wreck you
could still drive across, which quietly undid the one thing dropping it was for. Now the deck
is gone and driving at it puts you in the sea on the usual terms.

Two things deliberately did *not* change:

- **Fire already passed through rubble.** `Projectile.CanHit` has refused destroyed targets
  since M5. This closes the gap from the other side, so movement and fire finally agree.
- **`FlagTower.TryFootprint` still measures the rubble.** `GetComponentsInChildren<Collider>`
  returns disabled colliders on active objects, so a flattened tower still reports the
  footprint the pickup radius is measured from. Its remarks used to claim "only the colliders
  that are switched on"; that sentence was true when written and is now the opposite of the
  reason it works.

---

## 3. Automated turrets

A new `StructureKind.Turret`: the only destructible that belongs to a side and the only one
that shoots back.

### It reuses the combat pipeline rather than getting its own

The design document asked for this by name, and the cost of honouring it was one
generalisation: `VehicleWeapon` used to read its team and its is-it-wrecked off a
`VehicleHealth`. It now reads both off an `IDamageable`, which a hull and a wall both answer.
That single change buys the whole thing — a wrecked turret stops firing without `AutoTurret`
checking, because `IsLoaded` asks its mount whether it is still standing and rubble says no.
The class keeps its name; a turret is a muzzle on a part that traverses with an owner that is
not a pilot, which is a mount `VehicleWeapon` already described.

`Destructible.Team` went from a hardcoded `Team.None` to a serialized field with a `SetTeam`
setter, and that same answer does three jobs: it points the gun, it paints the emplacement,
and it makes the turret immune to its own side's fire (`Teams.IsHostile`, unchanged).

### The gun lives inside the state model, not on the root

This is the one structural thing worth knowing. The intact turret and the shot-up one are
different meshes with the barrel in different places, so a muzzle bolted to the prefab root
would fire out of thin air once the emplacement was half wrecked. `AutoTurret` re-finds its
`Turret` node and `MuzzlePoint` whenever `Destructible.StateChanged` fires — the same trick
`FlagTower.Mount()` plays for the flag.

**The destroyed model has no `Turret` node at all.** That is an art decision doing a code
job: the rubble is silent by construction rather than by a check somebody could forget to
write. `VehicleWeapon.SetMuzzle` exists rather than reusing `Configure` so that swapping
models does not reset the cooldown — a turret that could dodge its own rate of fire by being
shot would fire *faster* the more damage it took.

### The numbers, and why they are what they are

| | Turret | Read against |
|---|---|---|
| Hit points | 170 | Softer than a building's 220, harder than a depot's 130 — five cannon shells |
| Reach | 20 m | Inside the tank's 36 m, so an emplacement can be shelled from outside its own range |
| Damage / interval | 12 @ 0.33 s | 36 dps — a full tank in under 3 s, a jeep in under 1.5 s |
| Splash | 0 | It cannot clear the cover a raider needs to reach it |
| Traverse | 80 °/s | Faster than the tank's 65; a jeep at 22 m/s out-turns it inside 16 m |

That traverse rate is the interesting one. It puts the ring where circling beats the barrel
*inside* the turret's own reach, so there are two real answers to an emplacement — get close
and circle it, or stand off with the tank — rather than one.

**The damage row is deliberately brutal, not "expensive" as an earlier draft of this table and
of `WeaponTuning.Emplacement()`'s own comment claimed** — the adversarial review caught the
arithmetic mistake (36 dps against a 100 hp tank is under three seconds, not the twenty-two
the prose asserted, and no single fixed-dps weapon can hit two different numbers like
twenty-two and four against two different pools in the first place). Nothing in the roster is
meant to sit inside twenty metres of one and trade shots; that is the whole reason the tank's
own reach clears it by sixteen metres. Both places are corrected to the real number now. The
tuning itself — `Damage = 12.0f`, `ShotInterval = 0.33f` — was never touched; only the two
descriptions of what it does were wrong.

Ammunition is deliberately unlimited (no `VehicleSupply`). An emplacement that could be
emptied by driving past it out of range would be a puzzle with one answer; the thing that
stops a turret is shooting it.

### The level format grew a `Side`

`LevelStructure.Side` is the second exception to "a level places props, it does not rebalance
them" (the supply rates were the first), and like them it is placement rather than balance:
the same emplacement thirty metres further north is the other side's. `LevelValidation`
refuses **both** mistakes — a turret with no side, and a side on anything else — and
`StructureTuning.BelongsToASide` is the one place that answers which kinds care, so the
validator, the builder, the inspector and the mirror tool cannot drift apart.

**The schema went to 3.** A build that has never heard of `Turret` drops every emplacement
with a warning and builds the same battlefield with its defences missing, which is not the map
anybody authored — closer to v2's "an ellipse comes out square" than to v2's "lost two
colours", which explicitly did not earn a bump.

**`LevelEdits.Mirror` flips the side.** A mirrored turret is the *other* side's turret, as a
mirrored tower is. Copying it across would give one player both emplacements on a map that
still looked symmetrical, which is the worst kind of asymmetry: the invisible kind.
`LevelDesignTests.HasMirror` now checks the side too, so the shipped map cannot regress into
it silently.

### On the shipped map

Four turrets, one behind each side's two bridgeheads, at (±35, ∓24), mirrored 180°. At 20 m
they cover their own bridgehead pad and the near depot but **not** the far bank: crossing is
still a decision made under fire from the far side rather than one refused before it starts,
and the turret only speaks once a raider is across. The map's `Description` — the level file's
only room for comments — says so.

---

## 4. The audit

`StructureRosterTests.EveryStructureKindIsDestructibleEndToEnd` walks the enum itself and
checks each member is either scattered by `StructureTuning.Roster()` or is the flag tower,
that it has a real hit-point pool and debris radius, and that an empty pool means rubble.
`EveryStateHasSomethingToBumpInto` was widened from one building to every kind on the roster.
Together with the two tests that were already there — every kind has a prefab with its states
and its numbers, every state model the spec promises is on disk — the four separate lists
(enum, roster, tuning table, model inventory) are now checked against each other rather than
kept in step by hand.

**The audit found one real gap while this was being written**, and it was in the new code:
`CategoryOf` decides whether an asset is named `RF_Prop_*` or `RF_Structure_*`, and the turret
was falling through to `Prop` while Blender exported it as `Structure`. The prefab silently
did not build, `BuildAll` reported "built 6" and exited 0, and only the roster test would have
caught it. Worth knowing that `DestructiblePrefabBuilder.BuildAll` warns and carries on rather
than failing — grep the log for the count, not just the exit code.

---

## Gotchas for whoever is next

- **`Destructible.Team` is now a field, and `Projectile.CanHit` reads it.** A structure given
  a side is one its own team cannot shoot. That is right for a turret and would be a bug for
  anything else, which is why validation refuses it in both directions rather than only the
  one that looked likely.
- **`AutoTurret` walks `VehicleController.OnTheField` every frame** and calls
  `GetComponent<VehicleTeamPaint>()` on each candidate — the same idiom `Flag` uses. That is
  four entries and four `GetComponent` calls per turret per frame; with four turrets on the
  map it is not worth caching yet, but it is the first place to look if a future map places
  twenty.
- **The turret's head carries no collider.** A mesh collider that moves is a static collider
  physics rebuilds every frame it swings, so `AddGun` strips them from the traversing part and
  the base underneath is what a vehicle bumps into and a round's column crosses.
- **`VehicleWeapon.owner` is an interface reference**, so `owner != null` is a plain reference
  comparison rather than Unity's overloaded one. It is safe here because the mount lives on
  the same GameObject as the weapon and they die together; a weapon that ever gains a mount
  on a *different* object would need `owner is Object o && o != null` instead.
- **`SupplyPoint` no longer looks at height at all.** If anything ever needs to distinguish a
  landed aircraft from a hovering one again, it has to come back as a real concept rather than
  as a resurrected `landingAltitude` — because there is now no altitude a helicopter can be at
  that is not its cruising one.

## File map

| Path | Change |
|---|---|
| `unity/Assets/RF/Scripts/Vehicles/FlightTuning.cs` | One `CruiseAltitude` + `GroundedAltitude`; floor, ceiling and deploy height gone |
| `unity/Assets/RF/Scripts/Vehicles/HelicopterMotion.cs` | `Step` takes `bool powered`; eases onto the altitude rather than clamping to a band |
| `unity/Assets/RF/Scripts/Vehicles/Helicopter.cs` | `IsPowered` / `SetPowered`; deploys at cruise |
| `unity/Assets/RF/Scripts/Vehicles/VehicleInput.cs` | `Lift` removed, with one constructor overload |
| `unity/Assets/RF/Input/IronFlagControls.inputactions` | `Lift` action and its seven bindings deleted |
| `unity/Assets/RF/Scripts/Players/PlayerControls.cs`, `PlayerVehicleDriver.cs` | Stopped reading a collective |
| `unity/Assets/RF/Scripts/Supply/SupplyPoint.cs` | `landingAltitude` gate deleted; `ServesAircraft` left doing the work |
| `unity/Assets/RF/Scripts/Supply/VehicleSupply.cs` | Tells the aircraft its engine died; `DemandFrom` is the throttle alone |
| `unity/Assets/RF/Scripts/Core/VehicleBay.cs` | Rides out to `CruiseAltitude` |
| `unity/Assets/RF/Scripts/Destruction/Destructible.cs` | Rubble's colliders switched off; `Team` is now a field with `SetTeam` |
| `unity/Assets/RF/Scripts/Destruction/AutoTurret.cs` | **New** — targeting, traverse and trigger for the emplacement |
| `unity/Assets/RF/Scripts/Destruction/StructureKind.cs`, `StructureTuning.cs` | `Turret` row, `Roster()` entry, `BelongsToASide` |
| `unity/Assets/RF/Scripts/Combat/VehicleWeapon.cs` | Owner is an `IDamageable`, not a `VehicleHealth`; `SetMuzzle` added |
| `unity/Assets/RF/Scripts/Combat/WeaponKind.cs`, `WeaponTuning.cs` | `Autocannon` and `WeaponTuning.Emplacement()` |
| `unity/Assets/RF/Scripts/Levels/LevelStructure.cs`, `LevelDefinition.cs` | `Side` field; schema 3 |
| `unity/Assets/RF/Scripts/Levels/LevelValidation.cs`, `LevelBuilder.cs` | Both side rules; turret gets its team and its paint |
| `unity/Assets/RF/Scripts/Editing/LevelEdits.cs`, `LevelEditorSession.cs`, `EditorInspector.cs` | Palette places a turret on the palette's side; mirror flips it; inspector shows it |
| `unity/Assets/RF/Editor/Gameplay/DestructiblePrefabBuilder.cs` | `AddGun`, `TurretTraverseRate`, turret categorised as a Structure |
| `unity/Assets/RF/Editor/Gameplay/CombatPrefabBuilder.cs` | `Autocannon` joins the arsenal |
| `blender/assets/structure_turret.py` | **New** — three states; the destroyed one has no `Turret` node |
| `unity/Assets/StreamingAssets/Levels/iron-channel.json` | Four turrets, `Side` on every structure, schema 3 |
| `unity/Assets/RF/Tests/PlayMode/TurretTests.cs` | **New** — nine tests on who a turret shoots |
| `return-fire-homage-design-doc.md` | Section 10 emptied |

## Verified

- **496 tests pass**: 359 edit-mode and 137 play-mode, against 343 and 124 before this phase.
  Twenty-nine new tests and one rewritten — see the note in section 1 about the play-mode test
  that asserted the rule this phase removed, and the "Adversarial review" section below for
  the two tests the review added on top of that.
- **Every generated asset was rebuilt from scratch and re-run against**: seven destructible
  prefabs (the turret is the seventh), four vehicle prefabs restamped with the new
  `FlightTuning`, the level catalog, and both baked scenes. The suites above were run after
  that, not before it — a map change that is not baked into `Sandbox.unity` and
  `LevelEditor.unity` fails the level-loading tests, and a map change that *is* baked but not
  re-tested proves nothing.
- **The Blender build is clean**: three `.glb`s at 196, 196 and 124 triangles.

## Adversarial review

Six independent angles read the diff (turret correctness, helicopter correctness, level-data
integrity, test quality, docs-and-comments, and a regression sweep over the four pre-existing
vehicles), each finding checked by three skeptical passes that tried to refute it by reading
the live source. Six candidate findings came back; five survived unanimously and one was
refuted unanimously.

**Fixed:**

- **The turret's own damage numbers were wrong in two places.** `WeaponTuning.Emplacement()`'s
  own doc comment and this file's numbers table both said "twenty-two seconds to strip a full
  tank, about four to strip a jeep." Twelve damage at three shots a second is thirty-six
  damage per second, which is a tank's hundred hit points in under three seconds and a jeep's
  fifty in under a second and a half — the comment's own two numbers were not even reachable
  by a single fixed-dps weapon in the first place (22 s against 100 hp needs ~4.5 dps; 4 s
  against 50 hp needs 12.5 dps; one weapon cannot be both). The tuning itself was never wrong,
  only its two descriptions — see the corrected remarks on `Emplacement()` for what actually
  justifies twenty metres of reach against the tank's thirty-six.
- **`LevelDesignTests.HasMirror(LevelStructure)` never compared facing**, only kind, side and
  position — a turret mirrored onto the wrong side with the *same* yaw as its source, instead
  of opposite, would still read as correctly mirrored to every one of the 494 tests. Fixed to
  compare yaw too, with one deliberate exception: a bridge is straight and reads the same from
  either end, so both of a pair are drawn at the same yaw on purpose rather than opposing —
  turrets and every other kind still require the opposite. Verified by hand against the
  shipped file (all 54 structures pass; hand-corrupting one turret's yaw to match its source
  makes the check correctly fail) before spending a Unity run on it.
- **No test proved a turret withholds fire while its barrel is still traversing.** The one
  test that exercises mid-swing behaviour (`TheGunTraversesOntoWhatItIsShootingAt`) discards
  the target's `VehicleHealth` and only ever checks yaw. Added
  `ATurretWithholdsFireWhileTheBarrelIsStillSwinging`, which does keep the health reference and
  asserts no damage lands before the traverse can plausibly have finished, then that it has by
  the time it certainly has.
- **`HelicopterMotion`'s altitude clamp was only step-checked in one direction.**
  `ItNeverClimbsPastTheAltitudeItIsHolding` starts below `CruiseAltitude` and only ever
  exercises the ascending half of the clamp in `Step`; the only test that starts above it
  checks the state after a full run, not each step, so a broken descending branch could
  transiently overshoot and still self-correct by the time the test looks. Added the
  symmetric `ItNeverSinksPastTheAltitudeItIsHoldingWhenComingDownFromAbove`.
- **This file undercounted its own new test file by one** (said "seven tests," the file had
  eight before the fix above and has nine after it).

**Refuted, no action taken:** a claim that nothing bounds the turret's rate of fire survived
one look — `AutoTurret` fires through the same `VehicleWeapon.TryFire()`/cooldown every other
gun uses, and `CombatTests.AGunWillNotFireFasterThanItsInterval` already exercises that exact
shared mechanism directly. All three verifiers found this before I did.

## What the next phase inherits

The design document's section 10 is empty and M8 (the polish pass — music, minimap, HUD
readability, screen shake) is still the only unstarted milestone. `M4_BUNKER_VIEW_PLAN.md`
remains a second unstarted plan doc with an unanswered "Open questions for you" section.

Still true, and still the largest gap in the project: **nobody has played a match.** The
turrets are the first thing on the map that acts on its own, which makes that gap slightly
more expensive than it was — a balance number that is wrong is now a number that shoots at
you while you find out.
