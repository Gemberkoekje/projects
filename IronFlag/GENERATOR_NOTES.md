# Random Map Generator — what shipped

**To understand this, start by reading
[`LevelGenerator.cs`](unity/Assets/RF/Scripts/Editing/LevelGenerator.cs), then
[`Dice.cs`](unity/Assets/RF/Scripts/Editing/Dice.cs), then the GENERATE button and modal in
[`EditorUi.cs`](unity/Assets/RF/Scripts/Editing/EditorUi.cs).** Everything else is small.

This is item 2 of [MASTER_PLAN.md](MASTER_PLAN.md#2-random-map-generator), built as planned
with the plan's three open questions answered: **archetypes** rather than jitter, the
**1-player option shipped now**, and **retry-then-report** rather than report-only.

---

## The feature in one paragraph

Press **GENERATE** in the level editor's top bar, pick a size, a kind of ground, whether the
halves match, and one or two players, and press GENERATE again. A whole map arrives — coast,
beaches, roads, bunkers, towers, depots, emplacements, trees — validated, framed and open for
editing, saved as `generated-map-N`. The same seed and the same settings always draw the same
map, so a seed worth keeping is worth writing down.

| | |
|---|---|
| Layouts | Island · Channel · Lagoon, or ANY to roll one |
| Sizes | Easy 200 m · Medium 280 m · Hard 360 m across, with turret and prop counts to match |
| Halves | Mirrored (rotated half a turn) or Asymmetrical |
| Players | 2, or 1 for the solo shape — a bunker against a field of enemy towers |
| Seed | shown, typeable, re-rollable; written into the level file |
| Validation | `LevelValidation`, unfiltered, in the editor's existing Problems panel |

---

## What was built

**`LevelGenerator` is `LevelEdits.Starter()` with the numbers rolled.** A static function
that takes `MapOptions` and hands back a `LevelDefinition` — no scene, no camera, no editor —
which is why 23 of the new tests build nothing and why the editor's button is four lines long.
`LevelEditorSession.GenerateLevel` is `NewLevel()`'s exact shape with `Generate` where
`Starter` was.

### Playable by construction, not by luck

The one topological rule a map owes is that the two bunkers are joined by land with no bridge
in it (`LevelValidation.IsConnected`). A from-scratch random layout is exactly where that
breaks by accident, and the failure is invisible in a level file — it is a flood fill that
stops. So each layout pays it deliberately:

- **Island** — each half is an ellipse *deeper than it is offset from the origin*, so it
  reaches past the centre line. The worst draw still leaves the two halves overlapping in a
  band 20 m deep. The coastline wobble is 3 m.
- **Channel** — the causeway is one unbroken asphalt rectangle, and a **headland** is drawn on
  each side centred on that causeway's own x. A shore ellipse is narrow at its flanks, which
  is exactly where a causeway wants to land; the headland is what turns a few metres of
  landfall into twenty.
- **Lagoon** — six blobs an arc, 30° apart, each about half the ring radius wide, so
  neighbours overlap by ~10 m even when both jitter the wrong way. The two flanks — where two
  arcs drawn from *different dice* meet — are closed by fixed **link blobs** drawn before
  anything is allowed to jitter.

### Everything placed is settled onto land before it is kept

`Settle` takes the spot a layout wants and rings outward until it finds one that is far enough
inside the *realised* coast (`LevelDefinition.IsOnLand`, which reads the noise-displaced
coastline, not the drawn rectangles) and far enough from everything already placed. One
function, so the shore margins, the tower-spacing rule and "do not put a tree inside the
bunker" are each written once.

**Required things are placed even when settling fails; optional things are not.** A map
missing its bunker fails validation with a sentence about a missing bunker, which helps
nobody; a map with its bunker in the sea fails with a sentence naming exactly what to drag.
Scenery is the opposite — nothing needs the twentieth tree, so one with nowhere to stand is
simply absent.

### Determinism

`Dice` is a hashed stream, never `UnityEngine.Random`, following `SurfaceNoise`'s discipline.
The seed goes into `LevelDefinition.Seed` so the coastline and the layout are one map rather
than two. `Dice.Branch` gives each half of an asymmetrical map its own stream, so adding a
roll to one half will not silently redraw the other for every seed ever noted down.

### Solo maps

One green bunker, no green towers, and a brown side that is several flag towers — one real,
rolled — each ringed with its own emplacements. Since the 1-player pass, `LevelValidation`
knows what a one-player map is (`LevelDefinition.IsSolo`) and judges one by its own rules, so
a clean solo map now comes out of the generator with an empty Problems panel — and
`LevelGenerator.SoloFaults`, the private copy of those rules the re-roll loop scored against,
is gone. See **Gotchas** below.

---

## Gotchas found while building this

**Paint order is the whole look, and getting it wrong costs a beach.** Land rectangles
overlap on purpose and the last one written wins. Drawing the map a landmass at a time put the
*second* landmass's sand over the *first* one's grass, so every place two of them met came out
with a beach through the middle — on an island map, straight across the waist, and the map
read as two islands touching. The fix is that `Ground` now works the whole map out into
`Patch` records **before writing a single rectangle**, then emits it one surface at a time:
every bank as sand, every bank again as grass inset by its own beach, then meadows, scuffs,
roads, and the causeways last of all. Only then does sand appear exclusively at a coastline,
which is what "a beach nobody drew" was always supposed to mean.

**The field is rebuilt whenever the land changes.** `LevelDefinition.Field` checks whether it
still describes the level and rebuilds if not, so a generator that interleaved "add a
rectangle" with "is this on land?" would rasterise the whole map once per rectangle. Hence the
two hard passes: **all land first, then everything that stands on it.**

**A fixed-length bridgehead is wrong at one end of its own range.** Measured against the shore
it lands on instead (`ShoreEdge`), because an ellipse falls away at its flanks: tuned against
the innermost bridge it stops in the water on the outermost, and tuned against the outermost
it is a runway on the innermost.

**Snapping every point to a lagoon's exact ring radius puts forty trees on one perfect
circle.** `Where` squeezes the distance instead of projecting it, so the band gets used.

**Twelve headings is a blind search when the ring is wide.** `Settle` steps outward at a
widening radius; at 70 m out, twelve headings are 40 m apart and can step over a headland
entirely. It now scales headings with circumference. Before that, one seed in ~200 put a tree
in the sea on a hard island map.

**The generator's first causeway was a runway.** Run to four tenths of the world "for safety",
it drew a grey strip half the height of the map down each flank. The headlands already
guarantee the landfall, so it is `CausewayReach = 0.24` now — and the roads (bunker road →
depot road → causeway spur → causeway) were joined into a network, which is the difference
between a map that looks generated and one that looks authored.

**Only the press that discards is guarded.** The top-bar GENERATE button opens the modal
unguarded — the modal has a CANCEL and throws nothing away — and the modal's own GENERATE
carries the two-press `Guard`. Guarding both, as the OPEN button does, would mean four presses
to draw a map. This is the open panel's own lesson applied: guard whatever actually discards,
not whatever leads to it.

**The version stamp `Guard` uses to catch "armed forever" does not see everything that changes
what would be discarded.** Found by an adversarial pass before this went into git, and real:
arm GENERATE with one press, close the panel with CANCEL without a second press, come back
later having changed SIZE, GROUND or the seed, and press GENERATE once — it fired immediately,
with no fresh warning, against settings that were never the ones the first press confirmed.
`Guard`'s stamp is `LevelEditorSession.EditVersion`, which only moves when the *live level*
changes; none of the modal's own option rows touch it, so reconfiguring what GENERATE would
draw was invisible to the thing meant to notice a change of mind. The fix does not touch
`Guard` — it disarms GENERATE specifically, from the one place every option row, the seed
field, and the panel's own closing already funnel through (`RefreshMakePanel`/
`HideMakePanel`), so a confirmation only ever fires against the exact settings it was shown
for.

**A field can want a sentinel and a domain with no free value at the same time.** The panel
used to treat a seed of `0` as "never rolled" and silently replaced it — but `MapOptions.Seed`
is documented as having no sentinel: zero is an ordinary seed a player can type on purpose. A
second, separate `bool` now carries "has this panel ever picked a seed", so `0` typed in
deliberately survives closing and reopening exactly as the field's own doc comment already
promised it would. The lesson generalises past this one field: reaching for an existing value's
edge case as an "unset" flag only works when that edge case is actually free, and confirming
that takes reading the type's own contract rather than assuming zero is always spare.

**Bash truncates very long commands in this environment.** Two attempts to write large C#
files through a heredoc failed with `unexpected EOF while looking for matching '` at a line in
the middle of the content. Write large files with the Write tool and splice via a short
script; the truncation is silent apart from the parse error.

---

## Numbers, and where they came from

The placement ratios are the **shipped map divided by 120** rather than invented. At Iron
Channel's 120 m half-extent its bunkers stand at 70 m, its towers at 56 m and 18 m either side
of the centre line, its depots at (38, 34), and its bridge emplacements 24 m back from the
water. Hence `BunkerOut = 0.58`, `TowerOut = 0.46`, `TowerAcross = 0.15`, `DepotOut = 0.28`,
`DepotAcross = 0.32`, `FrontOut = 0.20`. A generated map at the medium size therefore plays at
roughly the distances the game is tuned for.

`CausewayWidth = 12` and `Narrows = 13` are Iron Channel's own numbers — the second was
measured off the jeep's ballistic jump, so a bridge over less than it is a ramp anybody can
skip. Tower spacing is read live off `WeaponTuning`, exactly as `LevelValidation` reads it, so
retuning the rocket moves the generator with it.

**Difficulty can only mean size and count.** A turret's health, rate of fire and reach are
global constants (`StructureTuning.For`, `WeaponTuning.Emplacement`), not fields of a level
file, and a level carries placement rather than balance. Extending the format for per-turret
overrides is possible later; nothing here needs it.

---

## File map

| File | What changed |
|---|---|
| `unity/Assets/RF/Scripts/Editing/LevelGenerator.cs` | **new** — the whole generator |
| `unity/Assets/RF/Scripts/Editing/Dice.cs` | **new** — hashed, seeded draws |
| `unity/Assets/RF/Scripts/Editing/MapOptions.cs` | **new** — what to generate |
| `unity/Assets/RF/Scripts/Editing/MapDifficulty.cs` | **new** — Easy / Medium / Hard |
| `unity/Assets/RF/Scripts/Editing/MapLayout.cs` | **new** — Island / Channel / Lagoon |
| `unity/Assets/RF/Scripts/Editing/MapSymmetry.cs` | **new** — Mirrored / Asymmetrical |
| `unity/Assets/RF/Scripts/Editing/LevelEditorSession.cs` | `GenerateLevel(MapOptions)`; `UnusedName(stem)` |
| `unity/Assets/RF/Scripts/Editing/EditorUi.cs` | GENERATE button, the modal, `MakePanel` |
| `unity/Assets/RF/Scripts/Editing/LevelEdits.cs` | `Turned` promoted to public, so the rotation is written once |
| `unity/Assets/RF/Tests/EditMode/LevelGeneratorTests.cs` | **new** — 23 tests |
| `unity/Assets/RF/Tests/EditMode/LevelEditorWiringTests.cs` | asserts the modal is built and starts hidden |
| `unity/Assets/RF/Scenes/LevelEditor.unity` | regenerated for the new top-bar button and modal |

## Tests

**405/405 EditMode**, 23 of them new. The sweep that matters is
`EveryGeneratedMapIsPlayable`: every layout × every size × either symmetry × 12 seeds — 216
maps — each asked `LevelValidation.Problems` and required to answer nothing. It is the
expensive suite in the project, because each map costs one rasterisation of itself.

Also covered: land connectivity on its own, seed determinism both ways, mirrored halves being
*rotated* rather than reflected, mirrored emplacements changing hands, asymmetrical halves
genuinely differing, nothing standing in the sea, tower spacing, no land off the edge of the
world, bridges over water, the solo shape, the solo flag not always hiding in the same place,
solo tower reachability, and round-tripping through the file format.

**Not covered: the panel's own arm/disarm state.** The two `EditorUi` bugs an adversarial
review caught (see [Gotchas](#gotchas-found-while-building-this)) are both about *when* the
GENERATE button's confirmation and the seed field's first roll fire, and nothing in the suite
above exercises them - `LevelGeneratorTests.cs` calls `LevelGenerator.Generate` directly,
never through the panel, and `LevelEditorWiringTests.cs` checks the modal exists rather than
clicking anything in it. `EditorUi` exposes no public seam a test could drive a button through
or read `armed`/`seedRolled` back from, and this codebase's own testing pattern for that class
is wiring-level rather than simulated-click-and-observe - adding one would mean designing a
new kind of test for it, which is a real piece of work and not a side effect of a two-finding
bug fix. Worth doing before either code path is touched again.

**PlayMode 137/137.** One run of it came back 136/137 on
`SurfaceDrivingTests.AParkedVehiclePaysNothingForTheGroundUnderIt`, which compares fuel burned
across two real-time idles at 5 % tolerance and is therefore sensitive to machine load; it
passed on a clean re-run, and nothing in this work touches fuel, driving or physics. Worth
knowing it can flake rather than chasing it as a regression next time.

## What this does not do

- ~~**Solo maps are not playable yet.**~~ **Done** — see [SOLO_NOTES.md](SOLO_NOTES.md). The
  `Sandbox` scene still builds two seats; `SessionSeating` empties the one the loaded map has
  no side for. As predicted here, `LevelGenerator.SoloFaults` moved into `LevelValidation`
  when it landed, and this generator is where the shipped one-player map comes from.
- ~~**No walls.**~~ **Done, and the estimate below was wrong** — see
  [Wall runs, added after the doors pass](#wall-runs-added-after-the-doors-pass). It said
  `Scenery` and `Garrison` were "one list and one loop from here"; they were not, because
  every loop in this file places things through `Settle`, whose whole job is keeping them
  *apart*, and a wall only reads as a wall when its segments *touch*. Wall runs needed a
  placement path of their own. Solo maps still get none, and that part is deliberate.
- **Difficulty does not make anything tougher**, only bigger and more numerous. See above.

---

# Wall runs, added after the doors pass

**To understand this, read `LevelGenerator.Rampart` and `LevelGenerator.Ramparts` together.**
The first lays one run; the second decides where runs go.

A two-sided generated map now comes out with **one gated wall run per crossing per side**,
sized by difficulty: one run each at easy, two at medium, three at hard, capped at one per
crossing. Each run is an odd 3, 5 or 7 segments with a **gate in the middle belonging to that
side** and plain neutral walls either side of it.

## Why this could not be one more loop in `Scenery`

Every other placement in this file goes through `Settle`, and `Settle` exists to keep things
**apart** — it walks outward from the wanted spot until it finds one that is not `Crowded`. A
wall run needs the exact opposite: its segments must be exactly `LevelEdits.SegmentLength`
apart and touching, or two neighbours do not butt into one wall with a single pier at the join
and the run reads as a row of boxes.

So `Rampart` places rather than settles, and it grows **outward from the gate** rather than
laying end to end. A run that meets the water halfway comes out short at that end with its
gate still in it; laid from one end, the arm that ran out of land would be the one carrying
the gate — and the side that built the wall would have walled itself in.

This file's own estimate that walls were "one list and one loop from here" was wrong, and
[WALLS_NOTES.md](WALLS_NOTES.md) said so at the time. It was right to.

## The three decisions

### The gate is dead centre on the crossing, with no jitter across at all

The opposite of what every other placement here does, and the single thing that makes the
feature work rather than backfire. The gate is the middle segment, so centring the run on the
road puts the gate on the road. **The first version jittered `across` by ±0.07 of the world —
±10 m — and the result was a gate sitting on the grass beside the causeway with a wall segment
across the road.** Green would have had to shoot its own rampart to use its own causeway. It
was invisible in the level file, obvious in the first render, and the fix was deleting one
`dice.Spread`.

The variety that gives up comes back as the depth jitter and the rolled segment count.

### Ramparts are laid before the depots and the guns

`SideProps` now runs bunker, towers, **ramparts**, depots, emplacements, scenery, and the
order is a claim about priority. A run is not something a map owes — it is skipped when there
is nowhere for it — but it is the one thing here whose *shape* is fixed. A settled depot or a
settled emplacement can be nudged aside; a run cannot, because nudging one segment breaks the
join. Laid first, everything after it settles around the finished run. Laid last, it would be
the only thing on the map that could not get out of its own way.

### Behind the guns, never in front of them

`RampartOut` is 0.24 against the emplacements' `FrontOut` of 0.20, and the gap is not taste.
A round stops on the first thing it hits and a wall is two metres tall, so a run laid between
a turret and the enemy is a turret firing into its own defences.

## Gotchas

### `Settle` reserves the spot it finds, and the gate then asks whether that spot is free

`Ramparts` settles the middle and `Rampart` then tries to lay a gate there — against a `taken`
list that now holds a reservation at exactly that point, at distance zero. Every gate failed to
place. `Ramparts` takes the reservation back before calling `Rampart`, which is safe because
`Settle` appends exactly one entry and nothing runs in between. Worth knowing before using
`Settle` as a "find me somewhere" helper rather than as a "put this here" one.

`Rampart`'s own abandon-the-run path (below) takes a reservation back the identical way, for
the identical reason - safe for as long as nothing runs between the gate going down and the
run coming out again. Both sites are trusting `taken`'s tail rather than checking it, which is
worth a real release operation on `Settle` if a third one ever turns up.

### A run of one is worse than no run

If both arms fail immediately — the middle was the only dry, clear ground there was — the gate
comes back out again. A lone gate is five metres of kerb with a door in it that anybody drives
round, and it costs its owner sixty hit points of their own line for no barrier at all.

### A relocated gate can drift off the line it is meant to gate — found during review, not fixed

`Settle` looks outward in every direction, because it has no notion of an axis to prefer and
that generality is the whole point of sharing it with bunkers, towers and trees. So when the
exact crossing point is blocked, the spot it settles for instead can be shifted sideways along
the run's own line rather than only nearer or further from the shore — and `Rampart` lays the
whole run there regardless, gate and all, beside the crossing instead of on it. That is the
identical failure the fixed `across` draw above exists to prevent, reached by a path that fix
never touched.

A first attempt at a fix rejected any run whose settled gate drifted more than half a segment
off the crossing's own axis, treating it the same as `Settle` finding nowhere at all. That
broke `EveryGeneratedMapIsWalledAndGated`: Lagoon/Easy alone failed on five of six sampled
seeds, because a lagoon's crossing points sit close enough to open water that the exact spot
is blocked more often than not, and easy difficulty gets exactly one attempt at a run — reject
that one attempt and the map gets none at all. Settle's omnidirectional relocation is not the
rare edge case that fix assumed; on a lagoon it is closer to the common case.

The real fix wants a search that stays on the crossing's own axis rather than one that looks in
every direction and gets rejected after the fact — nudging `outward` while holding `across`
fixed, the same shape `Settle` already has but run along one line instead of a full ring. That
is more machinery than a reject and wants writing and looking at, the way the original
gate-beside-the-road bug was only caught by rendering it, not deciding blind under review-time
pressure — so it is reverted here rather than shipped half right. Left as a known gap for the
same reason the tower-spacing bug below is: it moves what a map looks like on affected seeds,
which wants deciding rather than doing.

### The pitch is the model's, not a number

`LevelEdits.SegmentLength` is new, and it lives there for the same reason `BridgeSink` does:
it is a fact about `prop_wall.py` and `structure_door.py` that nothing in a level file can
catch and nobody laying a run should have to know.
`StructureRosterTests.TheSegmentLengthIsWhatTheModelsWereBuiltTo` measures both prefabs against
it rather than comparing it with a second copy of the number, so a wall re-exported at 4.8 m is
caught there instead of as a row of boxes with gaps in it.

## A pre-existing bug this pass found and did **not** fix

**`Garrison` does not ring a solo map's towers at 13–19 m. It rings them at 40–53 m.**

`SoloProps` settles each fortress tower with `FortressRoom` (40 m), which goes into `taken` as
that tower's room. `Crowded` compares against `Mathf.Max(room, spot.Room)`, so a turret asking
for a spot 13–19 m out — with its own `TurretRoom` of 9 — is measured against 40 and shoved
outside it. Measured over three seeds at the medium setting, every emplacement on the map came
out between 40.0 m and 53.2 m from its nearest tower.

What that costs: an emplacement reaches 20 m, so **not one of them covers the tower it was
placed to guard.** The solo mode's whole shape — "a field of small fortresses to crack open one
at a time, each with its own ring of emplacements" — is not what the generator draws.

Untouched here because fixing it moves every solo map for every seed, which is a bigger change
than adding a feature and wants deciding rather than doing. The fix is probably that a tower's
*reservation* room and its *keep-away-from-me* room are two different numbers that `Placed`
currently conflates.

## Tests

Five new, all in edit mode, and the sweep already here covers ramparts for free —
`EveryGeneratedMapIsPlayable` draws every layout, size and symmetry across twelve seeds and
still passes with runs on every map.

- `EveryGeneratedMapIsWalledAndGated` — the feature is on: every two-sided map, on every
  layout and size, comes out with at least one wall and one gate.
- `EveryGeneratedGateStandsInARunOfWall` — the rule that made this a separate placement path:
  every gate has a wall exactly one segment away, lying the same way, to within a centimetre.
- `AGeneratedGateStandsOnItsOwnSidesGround` — a side's gate is on its own half.
- `AMirroredMapGivesEachSideItsOwnGates` — equal counts per side, and the mirrored one belongs
  to the *other* side.
- `StructureRosterTests.TheSegmentLengthIsWhatTheModelsWereBuiltTo` — the pitch, against the
  built prefabs.

**Looked at, not only asserted.** An overhead render of seed 4242 and a close shot of one run,
through a throwaway preview since deleted. The overhead is what showed the gate-beside-the-road
bug above; no test would have caught it, because a gate on the grass is a perfectly valid map.

EditMode **414/414**, PlayMode **147/147**.

## What this still does not do

- **Solo maps get no ramparts**, and that is a decision rather than an omission. Green's front
  is the one piece of ground on a solo map that nothing ever attacks, so a gated run across it
  would be a defence against nobody — and the place a wall genuinely belongs there is ringing
  each of brown's fortresses, which wants a run laid *against* a tower rather than settled away
  from one. `FortressRoom` currently makes that impossible; see the bug above.
- **Nothing gates a bridge on purpose.** Runs are dealt round `Crossings`, which lists causeways
  first, so on a medium map the two runs land on the two causeways and the bridges get none.
  Whether a bridge landing wants one is a play question nobody can answer yet.
- **A run does not know how wide the thing it crosses is.** It is 3, 5 or 7 segments because
  those are odd, not because a 12 m causeway wants 25 m of wall. A run that measured its
  crossing and closed it exactly would be better, and is a real piece of work.
