# Team Doors — what shipped

**To understand this, start by reading
[`blender/assets/structure_door.py`](blender/assets/structure_door.py) — a door is a wall
segment that opens, and that file is mostly `prop_wall.py`'s dimensions copied on purpose —
then [`AutoDoor.cs`](unity/Assets/RF/Scripts/Destruction/AutoDoor.cs), which is the whole of
the behaviour, then the `Door` case in
[`StructureTuning.cs`](unity/Assets/RF/Scripts/Destruction/StructureTuning.cs).** Read
`AutoDoor` against its sibling
[`AutoTurret.cs`](unity/Assets/RF/Scripts/Destruction/AutoTurret.cs): they are the two
destructibles that belong to a side, they are built the same way, and three of the door's
decisions are deliberately the *opposite* of the turret's.

This was **not in [MASTER_PLAN.md](MASTER_PLAN.md)**. It was asked for directly, off the back
of the walls pass, in four sentences: doors next to the destructible walls, part of a team, a
vehicle of that team makes them sink into the floor, and to the enemy they are practically a
wall. Everything below follows from those four sentences and from the one design decision that
is not in them.

---

## How to see it

`doors-shut.png` and `doors-open.png` are the same five-segment run — wall, wall, gate, wall,
wall — photographed with the gate up and with it down. They are the only pictures that make
the central claim, which is that **a gate is part of the run it stands in**: one pier at each
join, one unbroken cap line, and the team colour the only thing that says which segment is the
gate. `doors-states.png` is the three destruction states with a tank beside the rubble for
scale.

In the level editor it is a new **DOOR** row at the bottom of the PROPS palette. Pick a side
in the SIDE row, set the grid to 5 m, and click. Nothing else about the editor changed.

---

## The feature in one paragraph

A **door** is five metres of wall that belongs to somebody. Drive one of that side's vehicles
within sixteen metres and the steel leaf drops two metres into the ground in six tenths of a
second and stays down until you are through; drive an enemy vehicle at it and nothing happens
at all. It arrives in a level file as `"Kind": "Door"` with a `"Side"`, exactly as a turret
does, and it is knocked down exactly as a wall is — except that it is **softer** than the wall
it sits in, which is the one design decision the four sentences did not contain and the one
most worth arguing about. Four of the shipped map's eight wall segments are now gates.

| | |
|---|---|
| Size | 5.0 m long × 0.90 m at the footing × 2.0 m tall — the wall's, exactly |
| Opening | 4.00 m clear, the same bay a wall leaves between its own piers |
| Travel | 2.10 m at 3.5 m/s — 0.60 s, measured off the built mesh rather than written down |
| Reach | 16 m, which is what the jeep covers in 0.73 s |
| Hit points | 60 — two tank shells, three grenades, two rockets, 1.9 s of chaingun |
| Damaged at | half the pool; a damaged gate still blocks *and* still opens |
| Side | required. `LevelValidation` refuses a gate without one |
| States | `_Intact` / `_Damaged` / `_Destroyed`, 120 / 144 / 120 triangles |
| On the shipped map | 4 gates, the inland segment of each bridgehead run |

---

## The five decisions

### 1. A door is a wall segment, down to the pier spacing

Every dimension in `structure_door.py` that could have been chosen was copied from
`prop_wall.py` instead: the 5.00 m length, the 2.00 m height, the 0.90 m footing, the 0.76 m
coping, and above all the half-piers at x ±2.25. That last one is the load-bearing copy — it
means a gate butted against a wall makes **one** full pier at the join, exactly as two walls
do, so a run with a gate in it has the same rhythm as a run without one.

This is what makes the feature worth having at all. A gate that did not tile would be a
five-metre barrier with a hole in it that anybody can drive round; a gate that does tile is a
door in a wall. Everything else in this file is downstream of it.

The one number that is a judgement rather than a copy is the **4.00 m opening**, which leaves
0.40 m either side of the tank at 3.19 m. It is not generous. It is the same bay the wall
already leaves between its piers, and widening it would mean either thinner piers (which stop
matching the wall at a join) or a longer segment (which stops fitting the grid). If it turns
out to feel mean to drive through, it is one constant — but changing it breaks the tiling, so
the honest fix would be a wider *wall*.

### 2. A gate is the weak point of its own wall — the decision nobody asked for

Sixty hit points against the wall's eighty. This is the only place a door departs from "same
as a wall", and it is deliberate in both directions:

- **Tougher than the wall** would make walls pointless. There is no build economy here — a
  level file places what it likes — so if a gate were both harder to breach *and* passable by
  its owner, the correct move would be to build every run entirely out of gates.
- **Equal to the wall** would make the gate a wall that happens to open, with no cost to the
  side that placed it and no decision for the side attacking it.
- **Softer** is a real trade, and it is the oldest rule there is about walls: the gate is where
  one gets attacked. The owner buys a way through their own line and pays for it with a
  weak point everyone can see, because it is the differently-coloured bit.

Read against the four guns, with the wall's numbers beside them:

| Vehicle | Gate (60) | Wall (80) | Share of a full load |
|---|---|---|---|
| Tank (cannon, 34) | **2 shells** | 3 shells | 10 % vs 15 % |
| Jeep (grenade, 22) | **3 grenades** | 4 grenades | 12 % vs 17 %, thrown from inside 14 m |
| ASV (rocket, 55) | 2 rockets | 2 rockets | 17 % — the one gun that sees no difference |
| Helicopter (chaingun, 4 @ 8/s) | **1.9 s** | 2.5 s | 6 % vs 8 %, and it can fly over anyway |

The ASV's row is the one to read hardest, and it is the most interesting number in the table
by accident: **one rocket leaves a gate standing on five hit points.** Sixty was picked to sit
just above the biggest single round in the game so that nothing opens a gate with one shot —
`AGateIsTheWeakPointOfTheWallItStandsIn` asserts exactly that, against the weapon table rather
than against a number written here — and the consequence is that the ASV gets the most
expensive near miss on the map.

The roster is now ordered Tree 40 < **Door 60** < Wall 80 < Depot 130 < Turret 170 <
BuildingA 220 < BuildingB 260 < Bridge 320 < FlagTower 340.

### 3. The leaf keeps its colliders and gets a kinematic body — the turret's rule, inverted

M9 wrote down the moving-collider problem and solved it by deletion:

> The head traverses every frame. A mesh collider that moves is a static collider Unity
> rebuilds each time it does, and the base underneath is already the thing a vehicle bumps
> into — so the moving part carries no collider at all.

A door cannot do that. There is no base underneath a gate; being solid *is* what the leaf is
for, and stripping its colliders would leave a gate that looks shut and is not. So the same
problem is answered the other way round: the leaf keeps its mesh colliders and gets a
**kinematic `Rigidbody`**, which tells the physics engine the collider is expected to move, so
sliding it re-places one body instead of dirtying the static scene every step. Kinematic
because nothing may push a gate — not gravity, not a tank leaning on it — and because a
non-convex mesh collider is only allowed on a body that is.

`TheDoorPrefabCarriesASolidLeafOnEveryStateThatStillOpens` and
`TheTurretPrefabCarriesAGunOnEveryStateThatStillHasABarrel` now assert opposite things about
their moving parts, on purpose. Losing either is silent: a leaf without colliders is a gate you
drive through, and a leaf without a body is a gate that works and quietly costs physics
something every step.

The same difference is why `AutoDoor` moves the leaf in `FixedUpdate` where `AutoTurret` turns
its head in `Update`. A gun barrel swinging is a picture and may move whenever a picture is
drawn; a leaf is something a six-tonne rigidbody is resting against.

### 4. A gate may not close on a vehicle — and so an enemy in the gateway holds it open

The rule exists for a physics reason: driving a solid leaf up through a vehicle standing on it
would throw that vehicle into the air. `AutoDoor.IsBlocked` measures the volume the **shut**
leaf fills, grown by a vehicle's half-width, and anything inside it pins the gate.

The tactic falls out rather than being designed: an enemy who gets a vehicle into the gateway
behind a defender holds that gate open, at the price of parking in a doorway, in the open,
doing nothing else. That is a good trade for both sides and it costs no code, so it stayed.

What it must *not* become is a way to jam somebody's door from outside, so the check only
applies once the gate is already off its seat — an enemy nosed up against a **shut** gate does
nothing. Two play-mode tests hold the two halves apart:
`AGateWillNotShutOnAVehicleStandingInIt` and `AnEnemyLeaningOnAShutGateDoesNotPinItOpen`.

A helicopter falls out of the check on its own, without a check for one: the box is a little
over two metres tall and an aircraft holds ten.

### 5. How far open it is survives a hit — the other inversion

`AutoTurret.Mount` re-stows the barrel when the model swaps, because a gun's rest position is a
fact about the gun. `AutoDoor.Mount` does the opposite and re-applies the openness it already
had, because a gate's position is a fact about the traffic — and because the leaf is solid. A
gate that snapped shut the instant it was hit would put a two-metre barrier up through whoever
was driving through it, which is the worst possible moment to introduce one.

`AGateHitWhileOpenDoesNotSlamShut` asserts this in the same frame as the damage, with no step
in between, because one step of a full-height leaf appearing under a vehicle is already the bug.

---

## Two smaller decisions

**A door is a `Structure`, not a `Prop`.** The wall pass argued that split as generic repeated
cover (`Prop`) against purpose-built installations that *do* something (`Structure`), and a
gate opens. So it ships as `RF_Structure_Door_*` alongside the turret, and `CategoryOf` gained
a row — which is the switch M9's gotcha lives on. It did not fire; see Gotchas.

**Team trim goes in two places, doing two different jobs.** The collars round the piers never
move and say whose gate this is, on the turret's own reasoning about not putting a marker on a
part that moves. The cap along the top of the leaf is the *state*: from the top-down camera, a
shut gate is a coloured bar in a grey wall and an open one is a gap, so the thing a player
reads is the colour being there or not, rather than an animation they have to catch.

---

## The shipped map: four segments changed kind

The plan did not ask for this either, and it is the part to redirect if it is unwanted. It is
**four JSON edits and one paragraph of the map's description**, and reverting is a clean
undo — no structure was added or removed, and the count is still 62.

Each bridgehead already had a two-segment run across its inboard shoulder, at x ±19 running
from z ∓20 to z ∓25. The seaward segment stays a plain neutral wall. **The inland one is now a
gate belonging to the side whose bridgehead it is.** In one sentence: the shoulder is shut to a
raider and open to its owner, which is the whole of what a gate is for.

What actually changes in play is small and in both directions. The attacker's problem is
unchanged in shape — the run is still ten metres of barrier — but its inland half now costs
sixty rather than eighty, so the cheapest way through a bridgehead is now a specific,
visible, differently-coloured five metres. The defender gets a lane along their own shoulder
they did not have. Both bridgeheads of both sides get it, in the map's usual 180°-rotational
pairs, so a green gate's mirror is a *brown* gate and
`LevelDesignTests.EveryPropOnTheMapHasAMirrorImage` passes unchanged — that test compares kind,
side **and** yaw, so getting the pairing wrong would have failed rather than gone quiet.

---

## Gotchas found while building this

### Debris freezes in edit mode, and it looks exactly like geometry

The first `doors-states.png` appeared to show the destroyed gate's rubble standing about a
metre tall, which would have been a real defect — the whole claim of that state is that it is
low enough to drive over. It was not the door. `Destructible.TakeDamage` throws a
`DebrisBurst`, and **outside play mode nothing ever steps it**, so ten metre-wide chunks park
at the origin of every structure knocked down to build the still and read as a dark box.

Two things worth carrying: the preview had to destroy every `DebrisBurst` in the scene after
damaging anything, and — the more general one — **reading a render is eyeballing too.** The
walls pass learned to measure the built mesh instead of computing from size tuples; this pass
learned the same lesson one step further downstream. What settled it was logging every
renderer in the scene with its full path and world bounds, which named the culprit in one run
after three runs of staring at pictures.

### A rotated box's true height, again — and the collar that overhung the segment

Measuring the built mesh caught three things the size tuples hid, all of them the walls pass's
lesson repeating:

- The destroyed state's `LeafFallen` sat **0.029 m underground**. A three-degree tilt on a
  3.60 m box moves its bottom by 0.094 m, four times what the box's own thickness suggests.
  Every loose piece in the damaged and destroyed states is now centred on its measured
  half-range rather than on half its thickness.
- The destroyed state's **team collars stood at 0.49 m against a 0.45 m ceiling** — the one
  piece of a flattened gate above knee height, and the coloured one. Their wrecked height is
  now derived from `_DESTROYED_CEILING` rather than written beside it.
- The collars were **0.04 m wider than the segment at each end**, which would have pushed
  geometry into whatever was butted against the gate — and a door's entire reason for being
  five metres long is that something usually is. They are now proud across the thickness and
  inset along the run.

The script that found all three is five lines and worth re-running for any new asset:
`root = build_x(); [(o.matrix_world @ v.co).z for o in hierarchy(root) for v in o.data.vertices]`.

### The travel is measured, not written down

`AutoDoor.TravelFor` reads the leaf's renderers and computes the drop from the door's own
origin, plus a tenth of a metre so the cap tucks under the map's stack of surface sheets rather
than fighting the road for pixels from two hundred metres up. Nothing in C# knows how tall a
gate is. That means re-exporting a taller gate produces a longer drop with no code change, and
it means `AJeepNeverHasToWaitAtItsOwnGate` can check the *tuning* against the *asset* rather
than against a second copy of a number.

That test is the reach and speed's actual derivation: the jeep tops out at 22 m/s and the leaf
takes 0.60 s, so a gate must notice at 13.2 m or its own side drives into it. Sixteen is that
with a fifth again in margin.

### "Only a turret" was written down in eight places

The mechanism M9 built held perfectly — `StructureTuning.BelongsToASide` gained one clause and
the validator, the builder, the inspector, the mirror tool, the palette and the catalog all
needed nothing. What needed touching was every place that had spelled the rule out in *prose*:
two validation messages a player sees, one editor status line, and five doc comments. One of
the validation strings is asserted verbatim by a test, which is the only reason the sweep was
guaranteed to be complete rather than merely thorough.

Worth knowing for whoever adds the third: the code will not fight you, and the English will.

### The gotcha M9 warned about still did not fire

`DestructiblePrefabBuilder.CategoryOf` did gain a `Door` row this time, so the trap was live —
a kind exported by Blender as `RF_Structure_*` while `CategoryOf` says `Prop` builds no prefab,
logs one warning, and **exits 0**. The check is the count: `built 9 destructible prefabs`, and
`RF_Structure_Door` by name in that list. Grep the log for the count, never just the exit code.

### A personal copy of `iron-channel` still shadows the shipped one

Unchanged since the walls pass and still true on this machine: `LevelLibrary.PathFor` prefers
`%USERPROFILE%\AppData\LocalLow\Gemberkoekje\IronFlag\Levels\` over `StreamingAssets`, and
there is a save there from an earlier editor session. Every test that judges the shipped map
reads `ShippedPathFor` and sees the gates; **the running game and every level render will not**
until that file is deleted or overwritten from the editor. Only the person whose save it is
should delete it, which is why nothing here does.

It is also why `Sandbox.unity` and `LevelEditor.unity` were not rebaked: both bake through
`PathFor`, so regenerating them today would bake the shadow copy and produce two large scene
diffs with no gates in them. The structure count is unchanged at 62, so the wiring tests that
compare the bake against the file still pass; only the scene-view preview is stale, and it was
already stale before this pass.

---

## File map

| File | What changed |
|---|---|
| [`blender/assets/structure_door.py`](blender/assets/structure_door.py) | **New.** Three states, primitives only, 120/144/120 triangles. Every dimension is either copied from `prop_wall.py` with the reason, or argued. |
| [`unity/Assets/RF/Art/Models/RF_Structure_Door_*.glb`](unity/Assets/RF/Art/Models) | **New**, three files, built by the Blender pipeline. |
| [`unity/Assets/RF/Prefabs/Structures/RF_Structure_Door.prefab`](unity/Assets/RF/Prefabs/Structures) | **New**, assembled by **Tools > IronFlag > Build Destructible Prefabs**. |
| [`AutoDoor.cs`](unity/Assets/RF/Scripts/Destruction/AutoDoor.cs) | **New.** The whole behaviour: who opens it, what stops it closing, where the leaf goes. |
| [`StructureKind.cs`](unity/Assets/RF/Scripts/Destruction/StructureKind.cs) | `Door = 10`. |
| [`StructureTuning.cs`](unity/Assets/RF/Scripts/Destruction/StructureTuning.cs) | One tuning case (60 / 0.5 / 2.2), one roster line, and `BelongsToASide`'s second clause. |
| [`Destructible.cs`](unity/Assets/RF/Scripts/Destruction/Destructible.cs) | Remarks only — `Team` now has two exceptions, not one. |
| [`DestructiblePrefabBuilder.cs`](unity/Assets/RF/Editor/Gameplay/DestructiblePrefabBuilder.cs) | `AddLeaf`, `DoorReach`, `DoorLeafSpeed`, and `Door` added to `CategoryOf`. |
| [`LevelValidation.cs`](unity/Assets/RF/Scripts/Levels/LevelValidation.cs) | Both side-rule messages reworded off the kind rather than off the word "turret". |
| [`LevelPick.cs`](unity/Assets/RF/Scripts/Editing/LevelPick.cs) | Click radius 2.5 m, sharing the wall's case and its reason. |
| [`EditorInspector.cs`](unity/Assets/RF/Scripts/Editing/EditorInspector.cs) | The Side row's status line names the kind. |
| [`LevelBuilder.cs`](unity/Assets/RF/Scripts/Levels/LevelBuilder.cs), [`LevelStructure.cs`](unity/Assets/RF/Scripts/Levels/LevelStructure.cs), [`LevelEditorSession.cs`](unity/Assets/RF/Scripts/Editing/LevelEditorSession.cs) | Comments and tooltips only. |
| [`LevelCatalog.asset`](unity/Assets/RF/Levels/LevelCatalog.asset) | Row for `Kind: 10`, written by **Build Level Catalog**. |
| [`iron-channel.json`](unity/Assets/StreamingAssets/Levels/iron-channel.json) | Four segments changed from `Wall`/`None` to `Door`/`Green`\|`Brown`, and a rewritten paragraph. Schema stays at 3. |
| [`DoorTests.cs`](unity/Assets/RF/Tests/PlayMode/DoorTests.cs) | **New**, ten tests on who a gate opens for. |
| [`StructureRosterTests.cs`](unity/Assets/RF/Tests/EditMode/StructureRosterTests.cs) | `OnlyTheTurretBelongsToASide` widened and renamed; three new tests. |
| [`LevelValidationTests.cs`](unity/Assets/RF/Tests/EditMode/LevelValidationTests.cs) | One assertion reworded, one test added. |
| [`return-fire-homage-asset-spec.md`](return-fire-homage-asset-spec.md) | Door entry, the door rule — and the **turret entry the walls pass flagged as missing**. |
| [`README.md`](README.md), [`MASTER_PLAN.md`](MASTER_PLAN.md) | A paragraph on what a gate is; a Completed entry. |

**No schema bump.** A door adds no field to the level format — it reuses the `Side` that schema
3 already has for turrets — and an older build refuses this map loudly
(`"'Door' is not a kind of structure this game has."`) rather than misreading it quietly. That
is the criterion the wall set and the same one applies.

---

## Tests

EditMode **409/409** (was 405), PlayMode **147/147** (was 137). Fourteen new, one renamed and
widened, one reworded.

New in edit mode:

- `TheDoorPrefabCarriesASolidLeafOnEveryStateThatStillOpens` — a leaf in the two states that
  open and none in the rubble, with colliders and a kinematic body on each. The deliberate
  inverse of the turret's.
- `AJeepNeverHasToWaitAtItsOwnGate` — the reach and speed checked against the drop measured off
  the built prefab.
- `AGateIsTheWeakPointOfTheWallItStandsIn` — softer than the wall, harder than every single
  round in the game, both read off the live tables.
- `LevelValidationTests.ADoorOnNoSideIsRejectedAndOneOnASideIsNot` — the side rule applied to
  the second kind that takes one, through `BelongsToASide` rather than a second comparison.
- `OnlyTheTurretAndTheDoorBelongToASide` — rewritten as an explicit set rather than a repeat of
  the production expression, so widening the rule stays a deliberate edit in two places.

Ten new in play mode (`DoorTests`), built out of cubes like `TurretTests`: it opens for its own
side, is a wall to the enemy, ignores its own side out of reach, shuts again once its owner has
gone, will not shut on a vehicle standing in it, is not pinned open by an enemy leaning on it
shut, does not slam shut when hit while open, has nothing left to close once wrecked, opens for
nobody when it belongs to nobody, and cannot be shot down by its owners.

**Looked at, not only asserted.** Three renders through a throwaway preview scene, since deleted:
the run shut, the run open, and the three states with a tank for scale. The first of those is
the only check there is on the claim that a gate tiles with a wall, and the third is what caught
the frozen-debris artefact described above.

Commands used:

```bash
"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" --background --factory-startup --python blender/build.py -- --asset Door
```

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -quit -projectPath unity -executeMethod IronFlag.Editor.Gameplay.DestructiblePrefabBuilder.BuildAll -logFile -
```

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -quit -projectPath unity -executeMethod IronFlag.Editor.Gameplay.LevelCatalogBuilder.BuildAndSave -logFile -
```

---

## What this does not do

**No sound, and no signal that a gate is opening except the gate opening.** A leaf that moved
silently is the one part of this that will feel wrong first, and it is blocked on
[MASTER_PLAN.md](MASTER_PLAN.md) item 8 having no audio system at all to hook into.

~~**The generator still places neither walls nor gates.**~~ **It does now** — added
immediately after this pass; see
[GENERATOR_NOTES.md](GENERATOR_NOTES.md#wall-runs-added-after-the-doors-pass). Two-sided maps
get one gated run per crossing per side. Solo maps still get none, deliberately.

**A gate opens for any friendly vehicle, including a helicopter.** That is the absence of a
special case rather than a decision made, and it is defensible — an aircraft has no use for a
gate itself, so all it can do by hovering over one is hold it open for a team-mate, and two
players on a side helping each other is worth having. Worth revisiting only if it turns out
that a side can field two vehicles at once often enough for it to matter.

**A vehicle can be caught by a closing gate in one specific way:** approach at speed, stop
inside the doorway *exactly* as the last friendly leaves reach, and the leaf will already be
rising before `IsBlocked` sees the new position. The check is per fixed step and the leaf moves
3.5 m/s, so the worst case is about seven centimetres of lift — a nudge rather than a launch.
It has not been seen; it is written down because it is the shape of the bug that check exists
to prevent, and a faster leaf would make it real.

**Nobody has played against one.** Every number here — sixty hit points, sixteen metres, six
tenths of a second — is arithmetic off the weapon and vehicle tables, which is the footing every
other number in `StructureTuning` stands on. What a gate *feels* like to arrive at is not
something a test can answer, and the one about the 4.00 m opening is the number most likely to
be wrong.

---

# The gun tower, raised afterwards

Walls arriving at 2.0 m left the emplacement — built at **1.68 m** in M9 — shorter than the
fence beside it, so a "gun tower" read as a bollard behind its own wall.
`blender/assets/structure_turret.py` is rebuilt as a hexagonal shaft **4.0 m** tall with a
gallery at the top and the same head on it. Nothing else changed: same `Turret` node, same
`Muzzle` group, same hit points, same reach.

## Height in this game is silhouette and nothing else

Worth stating plainly, because the obvious reading of "make the gun tower taller so it can
shoot over the wall" is wrong. **A round here is a column, not a point.** `Projectile.Sweep`
capsule-casts `CombatPlane.Column`, which stands from 0.5 m to 30 m at the round's horizontal
position, whatever the muzzle's height. So:

- Raising the tower does not let it shoot over anything. Nothing shoots over anything.
- A wall in front of a turret blocked its fire before this change and blocks it exactly as much
  now — which is why the generator puts wall runs *behind* the emplacement line, not in front.
- The turret's base was always cover, so M9's stated reason for building it low — "well under a
  building's height so it never becomes cover in its own right" — was never true.

That last point is why the change is safe rather than a balance edit: the thing the low profile
was protecting was not being protected.

## What changed in the shape

| | Was | Now |
|---|---|---|
| Total height | 1.68 m | **4.00 m** — exactly twice a wall |
| Shaft | 0.75 m plinth | 2.76 m tapered shaft, 1.05 → 0.74 m radius |
| Head sits on | the plinth | a 1.12 m **gallery** that oversails the shaft |
| Team trim | one ring on the apron | apron ring **plus a band under the head** |
| Destroyed state | 0.86 m tall | **0.44 m**, under the wall's 0.45 m ceiling |
| Triangles | 196 / 196 / 124 | 236 / 248 / 160 |

**The gallery band is the one addition that is not decoration.** The camera looks down at 58°,
and on a four-metre shaft the apron ring is the part the tower itself stands in front of — the
old trim would have been mostly hidden by the thing carrying it. A collar directly under the
head is the only team-coloured surface nothing can occlude, and it does not rotate, so it stays
put while the gun tracks. In the stills it is the strongest team marker on the map.

**The destroyed state came down from 0.86 m to 0.44 m**, which was not part of the request. The
walls pass set 0.45 m as the ceiling every wreck keeps, on the grounds that `Destructible`
switches a destroyed structure's colliders off and a heap that reads as blocking is a heap
players get shot through. The turret predates that rule and had never been measured against it.
It matters more for a tower than for a wall segment: the taller the thing that fell, the more
tempting it is to draw the wreck as a pile.

## Gotchas

### The head floated, and nothing would have said so

The head is a separate object parented to the root, so the first build put the housing's
underside at 3.38 m over a gallery whose top was at 3.22 m — a 16 cm gap with a gun hanging in
it. No test could catch it and the numbers each looked right on their own. `_SHAFT_TOP` is now
chosen so the gallery's *top* lands on the housing's underside rather than near it.

### The same rotated-box arithmetic, a third time

Five of the seven loose pieces in the destroyed state sat between 6 and 23 millimetres
underground, because each was centred on half its own thickness rather than on its measured
half-range. A four-degree tilt on a 1.70 m course moves its bottom by 6 cm, four times what the
box's thickness suggests. This is the third asset in a row to make that mistake; the fix each
time has been to measure the built mesh instead of computing from the size tuple.

## Tests

One new: `StructureRosterTests.TheBuiltStructuresReadAsAHeightSequence`, which measures the
wall, the gate, the gun tower and the flag tower off their built prefabs and asserts a gate
matches a wall exactly, a gun tower clears a wall by half again, and a flag tower clears the
gun tower. **This is the only thing in the project that reads a structure's height at all** —
the game never does, which is precisely why a silhouette rule needed asserting rather than
describing. EditMode **415/415**, PlayMode **147/147**.

**Looked at, not only asserted.** `tower-lineup.png` is the tower behind a gated wall run with
a tank for scale, shot at the game's own 58° camera pitch; `tower-states.png` is the three
states against a wall.

## What this does not do

**It is a cosmetic change, and the balance is untouched.** Same 170 hit points, same 20 m
reach, same 12 damage at 0.33 s, same footprint — so the same things block it and the same
things kill it. If gun towers should actually be able to fire over walls, that is a change to
`CombatPlane`'s central rule rather than to an asset, and it would be a large one: the column
is what lets a ground vehicle shoot down a helicopter and what stops the tank's shells sailing
over a jeep.

**The tracers now visibly angle down**, which is a free consequence rather than a decision:
`VehicleWeapon.Elevation` reads `CombatPlane.DepressionFrom(muzzle.position.y, range)` off the
live muzzle, so a gun that moved from 1.4 m to 3.7 m went from about 1° of depression to about
8° without anything being retuned. Nobody has looked at whether that reads well in motion.
