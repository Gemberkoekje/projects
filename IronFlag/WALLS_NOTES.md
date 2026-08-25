# Destructible Wall Sections — what shipped

**To understand this, start by reading
[`blender/assets/prop_wall.py`](blender/assets/prop_wall.py) — every decision that matters is
a dimension in that file — then the `Wall` case in
[`StructureTuning.cs`](unity/Assets/RF/Scripts/Destruction/StructureTuning.cs), then the eight
`"Kind": "Wall"` entries in
[`iron-channel.json`](unity/Assets/StreamingAssets/Levels/iron-channel.json).** There is no new
C# class anywhere in this pass; that is the point of it.

This is item 3 of [MASTER_PLAN.md](MASTER_PLAN.md#3-destructible-wall-sections), built to the
plan's seven steps with both of its open questions answered and three deliberate departures
from its letter — all four decisions are argued below.

---

## How to see it

`walls-bridgehead.png` is the green west bridgehead of the shipped map: two segments butted into
one wall, the emplacement behind them, the bridge beyond. It was shot with a one-off camera
rather than a menu item, because the two commands that photograph this map —
**Render Level Overview** and **Build Vehicle Sandbox Scene** — both go through
`LevelLibrary.PathFor` and will show you a *personal* copy of `iron-channel` if one exists. See
the gotcha below; that is the first thing to check if the walls are not where this says they
are.

In the level editor they are simply a new **WALL** row at the bottom of the PROPS palette. Set
the grid to 5 m and click along a line.

---

## The feature in one paragraph

A **wall** is five metres of capped concrete, two metres tall, neutral, worth eighty hit
points, and it is the only thing in the game designed to be placed touching copies of itself.
It arrives in the level editor's palette as a new PROP row and in a level file as
`"Kind": "Wall"`, and it is knocked down exactly the way a building is. Eight of them stand on
the shipped map, two behind each bridgehead, closing the shoulder of beach a raider used to
slip along to get clear of the emplacement covering the crossing.

| | |
|---|---|
| Size | 5.0 m long × 0.90 m at the footing × 2.0 m tall |
| Hit points | 80 — three tank shells, four grenades, two rockets, 2.5 s of chaingun |
| Damaged at | half the pool, and the damaged state is still a barrier |
| Debris radius | 2.2 m |
| Side | none; a wall belongs to nobody |
| States | `_Intact` / `_Damaged` / `_Destroyed`, 60 / 96 / 96 triangles |
| On the shipped map | 8 segments, at x ±19, z ±20 and ±25, yaw 90 / 270 |

---

## What was built

Almost nothing, which was the plan's whole thesis and it held. The pipeline really is
self-registering: `blender/assets/__init__.py` discovered `prop_wall.py` with no edit,
`StructureTuning.Roster()` gaining one line made the wall buildable *and* placeable *and* a
catalog row, and `LevelBuilder.BuildStructures` needed no change at all. The C# diff is one
enum value, one tuning case, one roster line and one click-radius case.

### Five metres, because that is the editor's coarsest grid

`LevelEditorSession.GridSteps` is `{ 0, 0.5, 1, 2, 5 }`. A five-metre segment tiles exactly on
both the 1 m and the 5 m grid, so building a wall is click, click, click along the grid and the
run comes out continuous. Any other length would mean a row with gaps somebody has to nudge
shut by hand — which, on a format where a structure is a point and a heading, is the difference
between a usable feature and a fiddly one.

### The piers stand at the joins, not in the middle

Each segment carries a half-pier at either end (0.50 m wide, at x ±2.25 of a 5.0 m segment), so
two neighbours make **one** full pier where they meet and a lone segment gets end posts. This
is the single decision that makes repetition read as construction rather than as tiling, and it
is why the coping is exactly 5.00 m long rather than overhanging: an overhang would put two
coplanar faces in the same place at every join, which is a flickering seam every five metres.

### The damaged state is still a barrier

The middle state loses its cap and its top course over the +X half of the run and keeps an
unbroken 1.15 m course from end to end. A damaged wall stops exactly what an intact one stops.
Modelling the obvious thing — a notch through to the ground — would have made `DamagedAt = 0.5`
the *breach* point and left the destroyed state meaning nothing, because the colliders come
from the mesh.

### The destroyed state is knee-high on purpose

Nothing in the rubble stands above `_DESTROYED_CEILING` (0.45 m), checked empirically off the
built mesh rather than eyeballed from the size tuples — a rotated box's true top is not its
unrotated height, and the first pass of this asset shipped with a docstring claiming knee
height while its own stub and west pier actually reached 0.85 m and 0.72 m; see
[Gotchas](#a-rotated-boxs-true-height-is-not-its-size-tuple) below. `Destructible` switches the
rubble's colliders off the moment it is shown (M9's rule), so a heap a player could read as
still blocking would be a wall they drive straight through — worse than no wall at all. The
footing survives in all three states, scorched in the last, so a wrecked run still reads as a
line across the ground from the top-down camera.

---

## The four decisions

### 1. Rubble is not solid — no exception (plan's open question 1)

The plan asked whether a wall should be the first destructible whose rubble still obstructs.
It should not, and the reason is one line: **a breach that still blocks is not a breach.** The
entire point of shooting a wall is to make a hole; if the hole is not a hole, the eighty hit
points bought nothing and the only remaining use of a gun on a wall is decoration. This also
costs zero code — `Destructible.Solid(destroyed, false)` already does it — and keeps one rule
for the whole game instead of one rule and an exception. The art carries the promise instead:
see "knee-high on purpose" above.

### 2. A wall belongs to nobody — no `Side` (plan's open question 2)

Neutral, matching everything except the turret. `LevelValidation` already refuses a side on
anything but a turret, so this is the answer that needed no plumbing, and it is also the more
interesting one: **the wall a side built is cover for whoever reaches it first.** A team-tinted
wall would say "this is theirs" about a thing whose whole function is that both sides can hide
behind it.

### 3. The wall is a `Prop`, not a `Structure` — a departure

The plan called for `blender/assets/structure_wall.py` and a new case in
`DestructiblePrefabBuilder.CategoryOf`, and flagged that switch as the known gotcha that bit
the turret in M9. It is `blender/assets/prop_wall.py` instead, so the wall is `RF_Prop_Wall_*`
and lands in `Prefabs/Props` — **and step 5 disappears entirely**, because `Prop` is
`CategoryOf`'s default. The gotcha cannot fire.

That is a happy side effect rather than the reason. The reason is that the two categories split
generic-repeated-cover (tree, buildings, bridge → `Prop`) from purpose-built installations that
*do* something (depots, tower, turret → `Structure`), and a wall is neutral repeated cover that
does nothing. The asset spec's own heading for that group is "Generic cover (buildings, trees)",
and the wall's entry now sits directly under it.

### 4. Eighty hit points, tougher than a tree, not thinner — a departure

The plan said "balanced thinner than a Tree (40hp) or Building (220-260hp)". Thinner than a
*tree* is wrong twice over. Concrete that came down faster than a sapling is scenery pretending
to be a defence; and `StructureRosterTests.EveryStructureFallsToOneFullLoadAndNoneToASingleRound`
requires every kind to survive one tank shell (34), so anything under 40 would have had a
six-point margin against a test that exists to stop exactly that.

Eighty is **the price of one hole, not the price of a wall**: a barrier is breached a segment at
a time, so what a raider pays is this number and not the run's. Read against the four guns:

| Vehicle | Cost of one breach | Share of a full load |
|---|---|---|
| Tank (cannon, 34) | 3 shells | 15 % |
| Jeep (grenade, 22) | 4 grenades | 17 %, thrown from inside 14 m |
| ASV (rocket, 55) | 2 rockets | 17 %, and its 4.5 m splash catches the neighbours |
| Helicopter (chaingun, 4 @ 8/s) | 20 rounds ≈ 2.5 s | 8 %, and it can just fly over |

That leaves the roster ordered Tree 40 < **Wall 80** < Depot 130 < Turret 170 < BuildingA 220 <
BuildingB 260 < Bridge 320 < FlagTower 340. The jeep's line is the one to read hardest: it can
let itself in, but only by parking fourteen metres from a wall that is there because something
behind it is worth covering. And the helicopter's line is the asymmetry worth knowing — a wall
is the one piece of cover on the map that only exists for ground vehicles.

---

## The shipped map got eight of them — also a departure

The plan did not ask for this. It is here because a placeable thing that is placed nowhere is a
palette row nobody will ever click, and because M9 set the precedent by putting its four
turrets onto the shipped map rather than leaving the feature latent.

**This is a deliberate balance change to `iron-channel`, and it is the thing to redirect if it
is unwanted** — it is eight JSON entries and one paragraph of the map's description, so
reverting is a clean deletion.

What it does: each bridgehead has a shoulder of beach on its inboard side — between the
bridgehead building and the water, on the side facing the middle of the map. A raider coming
off a bridge could turn along it and be past the emplacement before the emplacement had
finished traversing. Two segments at x ±19, running from z ±20 to z ±25, close it: the gap left
at the shore end is about 1.5 m, narrower than a jeep, so nothing gets round it. The turn
inboard is now ten metres further inland, which is ten metres more of the turret's twenty; or
eighty hit points and the seconds to spend them, sixteen metres from the gun. Both bridges get
it, all four bridgeheads, in the map's usual 180°-rotational pairs — so
`LevelDesignTests.EveryPropOnTheMapHasAMirrorImage` passes unchanged.

---

## Gotchas found while building this

### A rotated box's true height is not its size tuple

An adversarial second pass (see [How this was checked](#how-this-was-checked)) caught the
destroyed state's own docstring lying: it claimed "nothing here stands higher than the pier
stumps at knee height," and the built mesh's `PierWest` was 0.72 m and its `Stub` reached
0.85 m — both eyeballed from their size/position tuples, neither ever measured. The tuples
looked right in isolation; nobody had asked Blender what they actually built.

Worse, fixing the obvious offenders (the piers, the stub) was not enough on its own: four of
the scattered debris boxes are long and thin and rotated to look "toppled," and a long box
tilted even a few degrees pushes its true top and bottom well past `at.z ± half-thickness` -
the naive number a flat, unrotated box would use. `SlabFallen` at 2.20 m long, tilted 9° about
its short axis, reached 0.37 m *above* its own center on one corner and −0.09 m *below* the
ground plane on the other, from a center height that looked conservative on paper.

The fix that generalises, and the reason the first pass got this wrong: **measure the built
mesh, do not compute from the size tuple.** A five-line headless script -
`root = build_destroyed(); [(root.matrix_world @ v.co).z for v in root.data.vertices]` - gives
the real min/max instantly, the same way `level.Field.ToTheCoast(...)` gave the real coastline
below. `_DESTROYED_CEILING = 0.45` is now a named constant the geometry is measured against
rather than a number in a comment, and every rotated debris piece is centered on its own
measured half-range rather than a round number, so its low corner rests on the ground instead
of hanging under it.

### The coastline is not where the level file says it is

The first placement put the shore-side segments at z ±17.5, which is 3.5 m inside the nominal
waterline at z ±14 and comfortably past `LevelValidation.ShoreMargin` (2.5). It failed:
*"A Wall at (-22.00, 0.00, -17.50) stands in the sea."*

The surfaces pass made every natural coast wander by up to 3 m of seeded noise, and at x ±22 it
had wandered **inland**: the real water's edge there is z ≈ ∓16.5, not ∓14. Arithmetic off the
rectangles in the level file is arithmetic about a coastline that does not exist any more.

The fix that generalises: **ask the field.** A throwaway editor method printing
`level.Field.ToTheCoast(...)` down each candidate line, run with `-executeMethod`, gave the real
signed distance in one two-minute batch run and turned two rounds of guess-and-test into one
measurement. Worth doing again for anything placed near a coast by hand.

### Two bridgeheads on this map are not mirror images of each other

The map is 180°-rotationally symmetric, not mirror-symmetric, so the *green west* bridgehead
pairs with the *brown east* one — and green west has a `BuildingA` at x −24 while green east has
a `BuildingB` at x +22. A wall line that clears the building at one green bridgehead does not
automatically clear it at the other. x ±19 is the value that clears all four (0.30 m at the
tightest, against `BuildingB`'s 4.5 m roof footprint at yaw 90).

### `BuildingB` is 4.5 m across, not 4.0

Its walls are a 4.00 box but the two roof wedges are 4.50, so the footprint that matters for
clearance is the roof's. Reading the wall box and forgetting the roof is a 0.25 m error per
side, which was the difference between clearing that building and intersecting it.

### A personal copy of `iron-channel` shadows the shipped one — everywhere except the tests

The tests passed and the walls were on the map, and **every render still showed the map without
them**. `LevelLibrary.PathFor` prefers the user folder over `StreamingAssets` — M8's rule, so
that editing a map cannot damage the shipped one — and there is a saved
`iron-channel.json` in `%USERPROFILE%\AppData\LocalLow\Gemberkoekje\IronFlag\Levels\` from an
earlier editor session. `LevelLoader`, `LevelPreview` (**Render Level Overview**),
`LevelEditorScene` and `VehicleSandboxScene` all go through `PathFor`; only `LevelDesignTests`
reads `ShippedPathFor` directly. So the tests were judging the file I had edited and everything
that draws a picture was judging a copy from the day before.

**This is still true.** Nothing here deletes that file — it is a save, and deleting somebody's
save to make a screenshot come out right is the wrong trade. Anyone who wants the walls in the
running game has to delete it (or open `iron-channel` in the editor and save over it). Worth
knowing whenever a change to the shipped map appears to have had no effect.

It is also why `Sandbox.unity` and `LevelEditor.unity` were **not** rebuilt in this pass. Both
carry a baked copy of the map so that opening the scene shows something, and both bake through
`PathFor` — so regenerating them today would bake the shadow copy and produce two large scene
diffs that still contain no walls. The baked copy is thrown away and rebuilt from the file on
the first frame of play, so nothing about the game is wrong; only the scene-view preview is
stale, and it was already stale before this pass.

### The plan's known gotcha never fired, because the category changed

`DestructiblePrefabBuilder.CategoryOf` is still untouched. Worth noting that the audit test the
plan pointed at — `StructureRosterTests.EveryStructureKindIsDestructibleEndToEnd` — would not
have caught a wrong category anyway on its own; the test that *does* is
`EveryStateModelTheSpecPromisesIsOnDisk`, which is where a `Prop`-named `.glb` looked up under
`Structure` comes back null.

---

## File map

| File | What changed |
|---|---|
| [`blender/assets/prop_wall.py`](blender/assets/prop_wall.py) | **New.** Three states, primitives only, 60/96/96 triangles. Every dimension is a named constant with the reason next to it. |
| [`unity/Assets/RF/Art/Models/RF_Prop_Wall_*.glb`](unity/Assets/RF/Art/Models) | **New**, three files, built by the Blender pipeline. |
| [`unity/Assets/RF/Prefabs/Props/RF_Prop_Wall.prefab`](unity/Assets/RF/Prefabs/Props) | **New**, assembled by **Tools > IronFlag > Build Destructible Prefabs**. |
| [`StructureKind.cs`](unity/Assets/RF/Scripts/Destruction/StructureKind.cs) | `Wall = 9`. |
| [`StructureTuning.cs`](unity/Assets/RF/Scripts/Destruction/StructureTuning.cs) | One tuning case (80 / 0.5 / 2.2) and one roster line. |
| [`LevelPick.cs`](unity/Assets/RF/Scripts/Editing/LevelPick.cs) | Click radius 2.5 m — half a segment, so the boundary between two neighbours falls exactly on the join a player can see. |
| [`LevelCatalog.asset`](unity/Assets/RF/Levels/LevelCatalog.asset) | Row for `Kind: 9`, written by **Build Level Catalog**. |
| [`iron-channel.json`](unity/Assets/StreamingAssets/Levels/iron-channel.json) | Eight wall entries and a paragraph of description. Schema stays at 3. |
| [`return-fire-homage-asset-spec.md`](return-fire-homage-asset-spec.md) | Wall entry under "Generic cover". |
| [`README.md`](README.md) | A paragraph on what a wall is and what the eight on the map do. |
| [`MASTER_PLAN.md`](MASTER_PLAN.md) | Item 3 struck through, Completed entry added. |

**No schema bump.** A wall is a plain scenery kind with no new semantics; an older build reading
this map fails loudly (`"'Wall' is not a kind of structure this game has."`) rather than
misreading it quietly, which is the criterion the M9 turret set.

---

## Tests

EditMode **405/405**, PlayMode **137/137** — no new tests, and that is the honest result rather
than a gap. The suite already contained the audit the plan named plus four more that a new
structure kind has to satisfy, and every one of them is a loop over
`StructureTuning.Roster()`:

- `EveryStructureKindIsDestructibleEndToEnd` — the enum, the roster and the tuning table agree.
- `EveryStructureFallsToOneFullLoadAndNoneToASingleRound` — 34 < 80 < 960.
- `EveryDestructibleHasAPrefabWithItsStatesAndItsNumbers` — three states, and `HasDamagedState`
  true for everything except the bridge.
- `EveryStateHasSomethingToBumpInto` — every state carries mesh colliders.
- `EveryStateModelTheSpecPromisesIsOnDisk` — the `.glb`s exist under the name the category
  implies.
- `OnlyTheTurretBelongsToASide` — the wall does not.
- `LevelDesignTests.*` — the shipped map's eight segments are on land, mirrored, and not parked
  on an objective. This is the one that failed first, and correctly (see gotchas).

**Looked at, not only asserted.** Two renders, both against the shipped file rather than the
shadow copy: **Build Art Preview Scene** for the three states side by side, and a throwaway
perspective camera on the green west bridgehead for the thing no single-asset shot can show —
that two segments five metres apart butt into one wall with a single pier at the join and
half-piers at the ends. They do.

Commands used:

```bash
"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" --background --factory-startup --python blender/build.py -- --asset Wall
```

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -quit -projectPath unity -executeMethod IronFlag.Editor.Gameplay.DestructiblePrefabBuilder.BuildAll -logFile -
```

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -quit -projectPath unity -executeMethod IronFlag.Editor.Gameplay.LevelCatalogBuilder.BuildAndSave -logFile -
```

---

## How this was checked

Before this went into git, it went through an independent adversarial pass by an agent with no
memory of writing it - told to distrust every numeric claim in this file and re-derive them
against the live code instead. It found one real defect: the destroyed state's own docstring
claim about knee height was checked by eye against the size tuples rather than against the
built mesh, and was wrong (see
[A rotated box's true height is not its size tuple](#a-rotated-boxs-true-height-is-not-its-size-tuple)
above). Everything else it re-checked came back confirmed rather than refuted - the 80 hp
arithmetic against the live `WeaponTuning.cs` numbers, the geometric consistency of the eight
placements in `iron-channel.json`, and the piers-at-the-joins math. Re-verification is worth
doing on a first draft's own numeric claims even when the draft already looked internally
consistent; internal consistency is not the same question as "is it actually true of the built
thing."

---

## What this does not do

**The generator still places no walls.** [GENERATOR_NOTES.md](GENERATOR_NOTES.md) predicted they
would "slot straight into `Scenery`/`Garrison` — one list and one loop away", and that estimate
is optimistic. Every scenery loop in `LevelGenerator` places one thing through `Settle`, whose
entire job is to keep placements a `Room` radius **apart** — and a wall only reads as a wall when
its segments **touch**. A ring of individually-settled segments around a solo map's tower is a
scatter of concrete stubs, not a fortress. Wall runs need a different placement path: settle the
run's two ends, then fill between them at fixed 5 m spacing, checking each fill against the
coast but not against `taken`. That is a real piece of design work, and it is the obvious next
thing to do with this feature.

**Fixed-length segments only**, as the plan recommended and for its reason: `LevelStructure`
carries a point and a heading, and a wall spanning two chosen ends would need a placement
primitive closer to `LevelLand`'s rectangles. Worth revisiting only if placing four segments in
a row turns out to feel like work — and with 5 m snapping it does not.

**Nobody has played against one.** Every claim here about eighty hit points is arithmetic off
the weapon table, which is the same footing every other number in `StructureTuning` stands on,
and the same caveat applies: what a wall costs to breach *feels* like is not something a test
can answer.

**The turret is still missing from the asset spec.** M9 added `RF_Structure_Turret_*` to the
game without adding a row to `return-fire-homage-asset-spec.md`'s Structures & Props section.
The wall's row is there now; the turret's is still absent, and that section is now the only
place in the repo that disagrees about what the game contains.
