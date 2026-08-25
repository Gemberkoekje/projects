# Surfaces — notes

**The pass is finished: phases A, B, C, D and E are all done.**
[SURFACES_PLAN.md](SURFACES_PLAN.md) is still the plan; this is what shipped against it.

**To understand this, start by reading
[SurfaceKind.cs](unity/Assets/RF/Scripts/Levels/SurfaceKind.cs),
[SurfaceTuning.cs](unity/Assets/RF/Scripts/Levels/SurfaceTuning.cs) and
[SurfaceField.cs](unity/Assets/RF/Scripts/Levels/SurfaceField.cs)** — an enum, the table it
indexes, and the thing that turns a level file into a map made of them. Then
[SurfaceMesh.cs](unity/Assets/RF/Scripts/Levels/SurfaceMesh.cs), which is the only thing that
turns any of it into geometry, and
[iron-channel.json](unity/Assets/StreamingAssets/Levels/iron-channel.json), which names rows
of that table, draws two islands out of ellipses, and does not mention the shelf or a single
coordinate of the coastline it actually has. Everything else in this pass is plumbing
between those five. The
arguments are in [SurfaceTests.cs](unity/Assets/RF/Tests/EditMode/SurfaceTests.cs),
[SurfaceFieldTests.cs](unity/Assets/RF/Tests/EditMode/SurfaceFieldTests.cs) and
[SurfaceMeshTests.cs](unity/Assets/RF/Tests/EditMode/SurfaceMeshTests.cs).

For the half of it a player *feels* rather than sees, read
[GroundVehicleMotion.Traction](unity/Assets/RF/Scripts/Vehicles/GroundVehicleMotion.cs) with
[VehicleTuning.SurfaceSensitivity](unity/Assets/RF/Scripts/Vehicles/VehicleTuning.cs) beside
it: two numbers multiplied together are the whole of Phase D, and
[GroundVehicle.ReadTheGround](unity/Assets/RF/Scripts/Vehicles/GroundVehicle.cs) is the only
place in the game that asks the map what is underneath something.

---

## Phase A — surfaces exist, and the map gets painted

### What Phase A is

The map is made of more than one thing now. A level file's land rectangles each name a
surface, a table says what that surface is, and `LevelBuilder` paints each slab with it. The
result is the one in `surfaces-a-map.png`: green shores with grey crossings over them. The
causeways and bridgeheads — the pieces of ground the whole match is decided on — are no longer
the same colour as the open country either side of them.

Nothing about how the game *plays* changed. Grip, fuel draw and drowning are all written into
the table and none of them is read by anything yet; that is Phase D. No coastline moved; that
is Phase C. There is no beach and no shelf; that is Phase B. Both waters have rows and
materials because Phase B needs them, and they are unused on any map today.

### Decisions made, and why

**The surface a level names resolves to grass when nobody recognises it, but `LevelNames`
still answers `None`.** The plan asked for "a missing or unrecognised name resolves to Grass",
which every rectangle written before this phase depends on. But `LevelNames` has one stated
rule — an unknown name is the empty member — and folding a second rule into it would have made
a typo indistinguishable from the word "Grass". So the fallback lives on
`LevelLand.Ground` instead, one property, documented as the one place the format departs from
that rule, and `LevelValidation` catches the typo and quotes it back. A misspelled prop costs
you one prop; a misspelled surface has nothing to cost you, so it costs a warning.

**The schema version did not move.** The plan recommends bumping it to 2, and it is right, but
its reason is a map "authored against displaced coastlines and a road network", which is
Phase C. A Phase-A file read by a Phase-A-less build loses its colours and nothing else — the
map still builds, still validates and still plays. Bump it in C, where a version-1 build would
genuinely mis-place a bridgehead. Bumping it now would refuse today's file on yesterday's
build for a difference nobody could die of.

**`DeepWater`'s material is `RF_Water`, not `RF_Surface_DeepWater`.** The sea already had a
material, its colour is a measured result M7 fought for, and generating a second dark blue
beside it would be one more thing that could drift apart. So the surface table now owns that
colour and `GeneratedMaterials` reads it from there, but the asset keeps its name and its
GUID, the sea slab keeps wearing it, and Phase B can hand the shelf mesh `ShallowWater`
without renaming anything. `MaterialFor(DeepWater)` and `catalog.Water` return the same asset,
which is checked by a test.

**`RF_Ground` is retired from the map but not from the project.** Nothing a level builds wears
it any more, and `LevelCatalog` no longer carries it — a catalog row nothing reads is exactly
the rot that class warns about. The asset stays because the art preview stands its models on
it, and a neutral grey backdrop is what a new model wants to be photographed against.

**Every crossing is `Asphalt`; both shores are `Grass`; nothing is `Sand`.** Confirmed with the
user against the plan's two open questions. A full road network — bunker to depot, depot to a
crossing — is a real balance change (a road is a fast lane and a fast lane is an ambush) and
waits for Phase E, when Phase D has made the road actually faster.
The bridgeheads being asphalt is **not decoration**: a built surface is the one whose edges
Phase C keeps exactly as written, and the 13 m channel at each bridgehead is measured by a
test that computes the jeep's ballistic jump from its real top speed.

### The palette, and how it was arrived at

| Surface | Written | Comes out at | Step |
|---|---|---|---|
| DeepWater | `0.035, 0.075, 0.135` | 38 | — |
| ShallowWater | `0.09, 0.15, 0.24` | 53 | ×1.37 |
| Grass | `0.15, 0.25, 0.10` | 73 | ×1.39 |
| Asphalt | `0.37, 0.375, 0.39` | 117 | ×1.60 |
| Sand | `0.76, 0.66, 0.45` | 190 | ×1.63 |

"Comes out at" is Rec. 709 brightness sampled off a real render, 0–255. The ramp is the whole
design: M7 established that a player reads **value, not hue**, at thirty-four metres, and five
surfaces is five chances to forget it. Every step is at least a fifth, and
`TheMapIsARampFromTheOpenSeaToTheBeach` asserts that so it stays true.

The sand at 190 lands within a couple of points of what the reference screenshot's sand
measures, which is the one colour on this map there is an original to check against. The grass
at 73 against a sea at 38 is within a hair of the coastline contrast M7 bought with a flat grey
ground, so this phase gave none of it away.

### Gotchas, in the order they will bite

- **`_BaseColor` is an sRGB property even though the project renders linear.** This cost the
  most time in the phase, and it cost it silently: the palette was first written as linear
  values, which is wrong by a squaring, and the numbers still *looked* plausible. The tell was
  that the two waters came out almost the same colour. If you are choosing a surface colour,
  write what a colour picker would show you.

- **Smoothness moves the measured brightness, and by more than you would expect.** The ground
  is one enormous flat plane under a sun at 52 degrees, so a rough surface scatters more back
  at a camera looking straight down than a smooth one. The two matte waters come out about a
  third brighter than their colours alone would put them, which is why the first shelf measured
  48 rather than the 44 that was predicted. **Change a smoothness and re-measure the ramp.**

- **Predicting a rendered colour does not work; measuring one takes two minutes.** Drop a
  throwaway level of parallel strips into `StreamingAssets/Levels`, run **Render Level
  Overview** at it, sample the pixels, delete the level. That is how the table above was
  arrived at, and it is how the next row should be.

- **`LevelCatalogBuilder.Load` does not refresh** — M7 recorded this and it is still true, and
  five more generated materials multiply the number of times somebody will hit it. Change a
  colour in the table and the next render shows the old one until you run **Build Level
  Catalog**. `EverySurfaceHasAMaterialPaintedTheColourTheTableSays` runs `EnsureAssets` itself
  for exactly this reason, so a test run also fixes it.

- **Rebuilding a generated scene rewrites every local `fileID` in it.** `Sandbox.unity` and
  `LevelEditor.unity` had to be rebuilt so the baked map wears the new materials — which is
  the whole point of `SandboxWiringTests` comparing the bake to what the loader builds — and
  the diff for a colour change is twenty-four thousand lines of renumbering. It is noise;
  don't go looking for meaning in it.

- **The trees are green and the ground is now green too.** Nothing broke, and a tree still
  reads on the map shot, but it reads less strongly than it did on grey. Worth watching when
  Phase E repaints the map, and worth remembering before adding any prop that is mostly
  foliage.

- **A land rectangle painted `ShallowWater` or `DeepWater` builds fine and is nonsense.**
  Nothing stops it, because the material lookup does not care. `SurfaceTests` asserts the
  shipped map never does it. If the in-game editor grows a surface palette, it should offer
  the rows where `Drowns` is false and no others — that filter is deliberately the same
  question as "would this drown you", so there is no second list to keep in step.

### Afterwards: the channel was redrawn

Not part of the surfaces pass, but done in the same session and it is what the map shot now
shows, so it is recorded here rather than lost.

**The middle of the channel is open water.** There used to be one 16 m causeway on the centre
line, which meant the shortest line between the two bunkers was also dry, also the fastest way
home, and also where both sides drove at each other. The rest of the island was scenery. Now:

```
  x=-62      x=-30        x=0        x=+30      x=+62
 causeway    bridge    open water    bridge    causeway
   12 m      13 m of                 13 m of     12 m
  of land    narrows                 narrows    of land
```

Two bridges anybody can drop, and two causeways nobody can, out on the flanks. Getting home is
now a choice of flank; losing both bridges costs a detour of about half again rather than
funnelling everything through one gap. Cover moved with the crossings it guards — the two
blocks that used to sit beside the central causeway now sit at the mouths of the two flank
causeways — and the depots, towers, bunkers and trees did not move at all.

**Two tests had to stop describing the old map and start stating a rule**, which is the useful
part of this for anybody redrawing it again:

- `TheOnlyCrossingNobodyCanDestroyIsTheCauseway` asserted "exactly one land rectangle contains
  the origin". That is a description of a causeway down the middle, not a design rule. It is
  now `EveryWayOverIsSomethingSomebodyBuilt`: take away every rectangle whose surface has
  `NaturalEdge` false and the two halves must fall apart. Same guarantee, no opinion about
  where the crossings are — and it leans on the surface table, which is the first thing outside
  the rendering that has.
- `TheCausewayCarriesATank` dropped one tank on `Vector3.zero`. It is now
  `EveryPermanentCrossingCarriesATank`, which finds every rectangle with dry ground on the
  centre line and drops a tank on each. Checking all of them rather than the first matters: a
  second crossing that turned out to be under water is exactly the thing nobody notices until a
  match deadlocks.

**And one test was added for what the redraw was actually for**:
`NeitherBunkerHasAStraightRunAtTheOther` walks the straight line between the two bunkers and
insists some of it is wet. That is the whole point of opening the middle, and nothing else
would have caught somebody quietly closing it again.

The three numbers that constrain any further redraw, all enforced by tests: each bridge must
span a **narrows** rather than the full channel; the narrowest water anywhere must stay wider
than a jeep at top speed can jump, which is why the bridgeheads hold 13 m; and every prop must
have a twin rotated 180 degrees about the origin.

### What Phase B inherits

- A table with `Drowns`, `NaturalEdge` and `BeachWidth` already written and already argued,
  none of which anything reads yet.
- Materials for both waters, wired into the catalog, ready for a shelf mesh and a deep mesh.
- `SurfaceTuning.Roster()`, which the catalog, the material generator and the tests all walk,
  so a new surface is one enum member, one case, and **Build Level Catalog**.
- A level format that already carries a surface per rectangle, and one shipped map that uses
  it.

The plan's Phase B is unchanged by any of this: `SurfaceField.Build`/`.At`/`.IsLand`, derived
beach and shelf bands, the sea drawn as two meshes, and `IsOnLand` and `LevelValidation`
consulting the field.

### File map

| Path | Change |
|---|---|
| `unity/Assets/RF/Scripts/Levels/SurfaceKind.cs` | **New.** The enum, with its empty member |
| `unity/Assets/RF/Scripts/Levels/SurfaceTuning.cs` | **New.** The table — colour, gloss, grip, thirst, drowning, edges, beach |
| `unity/Assets/RF/Scripts/Levels/LevelSurfaceMaterial.cs` | **New.** One catalog row: a surface and its material |
| `unity/Assets/RF/Scripts/Levels/LevelLand.cs` | Gains `Surface` (the name) and `Ground` (what it resolves to) |
| `unity/Assets/RF/Scripts/Levels/LevelNames.cs` | Gains `ToSurface` |
| `unity/Assets/RF/Scripts/Levels/LevelCatalog.cs` | Gains the surface rows and `MaterialFor`; loses `Ground` |
| `unity/Assets/RF/Scripts/Levels/LevelBuilder.cs` | `BuildLand` paints each slab with its own surface |
| `unity/Assets/RF/Scripts/Levels/LevelValidation.cs` | Names a surface nobody recognises, and quotes it |
| `unity/Assets/RF/Editor/ArtPipeline/GeneratedMaterials.cs` | Surface materials generated from the table; `SurfaceMaterial(kind)` |
| `unity/Assets/RF/Editor/Gameplay/LevelCatalogBuilder.cs` | Wires one material per surface into the catalog |
| `unity/Assets/StreamingAssets/Levels/iron-channel.json` | Surfaces named, and the channel redrawn: open middle, two bridges, two causeways |
| `unity/Assets/RF/Levels/LevelCatalog.asset` | Rebuilt |
| `unity/Assets/RF/Art/Materials/RF_Surface_*.mat` | **New.** Generated, four of them |
| `unity/Assets/RF/Art/Materials/RF_Water.mat` | Colour now comes from the table |
| `unity/Assets/RF/Scenes/Sandbox.unity`, `LevelEditor.unity` | Rebuilt so the bake wears the new materials |
| `unity/Assets/RF/Tests/EditMode/SurfaceTests.cs` | **New.** The table, the ramp, the names, the round trip, the materials |
| `unity/Assets/RF/Tests/EditMode/LevelValidationTests.cs` | The new branch, and that a surface-less rectangle is not a fault |
| `unity/Assets/RF/Tests/EditMode/LevelDesignTests.cs` | The causeway rule generalised; no-straight-run added |
| `unity/Assets/RF/Tests/PlayMode/LevelLoadingTests.cs` | Every permanent crossing is checked, not the origin |
| `return-fire-homage-asset-spec.md` | The palette gains the ground surfaces |
| `surfaces-a-map.png` | **The map.** How this phase is judged |
| `surfaces-a-sandbox.png` | The same raid as `m8-sandbox.png`, on green |

### Verified

- **415 tests pass**: 298 edit-mode, 117 play-mode. Eleven of the edit-mode ones are new, plus
  two new validation branches and one new design rule; two older tests were rewritten to state
  a rule rather than describe the old map.
- **Build Level Catalog, Build Vehicle Sandbox Scene, Build Level Editor Scene** and both
  stills all run clean, with no warnings and no exceptions during generation.
- **The catalog reports no problems**, which now includes a row per surface — so a surface
  without a material would have said so by name.
- **The baked `Sandbox.unity` wears two grass slabs and six asphalt ones** — two shores, two
  causeways, four bridgeheads — and no reference to `RF_Ground` survives in it.

**Not verified: whether green is the right interior.** It is the plan's recommendation and the
user's choice, and it reads well at map scale — but the reference shot is a sand island with
grass over it, and nobody has seen this map with a beach on it yet, which is Phase B. The
question is genuinely open until then.

**Also not verified: any of the handling numbers.** Grip 1.06 on asphalt and 0.80 on sand are
arguments, not measurements. Nothing reads them until Phase D, and the first time a jeep
crosses the causeway beside a jeep crossing the sand is the first time anybody finds out
whether a 6% road is worth having.

---

## Phase B — the field, the beach and the shelf

### What Phase B is

The map is no longer only what somebody drew. A level file is rasterised once into a
**`SurfaceField`** — one surface and one signed distance-to-the-coast per square metre — and
that field, rather than the list of rectangles, is now what the game asks when it wants to
know what is at a place. Two things fall out of it for free, and they are the ones in
`surfaces-b-map.png`: **four metres of sand inside every waterline and five metres of pale
shelf outside it**, on every coast of every map, with nothing in any level file mentioning
either. `iron-channel.json` is byte-for-byte the same file it was at the end of Phase A.

That is the difference between two rectangles in a pond and an island. The shelf is what
makes the land sit *in* the water rather than on top of it; the sand is what makes the
waterline a place rather than an edge; and because both are derived from distance to the
realised coast, a map cannot forget them and a map cannot get them wrong.

Nothing about how the game *plays* changed, again. The shelf drowns you exactly as the open
sea does, it has no collider, and the sand is a colour laid two centimetres over ground that
has not moved. `surfaces-b-sandbox.png` was rendered and came out **pixel-identical** to
`surfaces-a-sandbox.png`, so it was deleted rather than committed — the raid that still
photographs is staged inland, and from there this phase is invisible. What it does look like
from the camera you actually play through is `surfaces-b-coast.png`, which is the west
bridgehead from 34 m at 58 degrees.

### Decisions made, and why

**`BeachWidth` became `RimWidth` and `RimSurface`, and the shelf uses the same two columns.**
The plan has a beach rule ("land within `BeachWidth` of the coast becomes Sand") and a shelf
rule ("water within `ShelfWidth` of the coast becomes ShallowWater") and they are the same
sentence: *a surface hands its outermost metres to another one*. Written as two columns
instead of two rules, grass gives up four metres of its coast to sand and the open sea gives
up five of its coast to the shelf, and `SurfaceField.Rim` is one pass over one column.
Two rules that say the same thing are two rules that can stop saying it. It also means the
third rim — scuffed dirt at the edge of a road, whatever it turns out to be — is a table edit
and not a code edit. The cost is that a Phase-A column got renamed; nothing outside the
table and its tests read it.

**The shelf is 5 m and it is the wider of the two.** They are read against each other. The
beach only has to mark the waterline, so four metres — about a jeep and a half — is enough to
see from thirty-four metres up and narrow enough not to be a lane anybody drives down. The
shelf has the harder job, which is making the island sit in the water, so it gets more. It is
capped from above by something real: **the narrowest water on this map is the 13 m at each
bridgehead**, so a shelf wider than 6.5 m would close over a crossing and paint it pale from
bank to bank — which reads as a ford, and it is nothing of the kind, it drowns you.
`TheShelfLeavesOpenSeaInTheNarrows` asserts that the middle of the water each bridge spans is
still open sea, so nobody can widen the shelf into that lie by accident.

**The sea is still a slab, with the shelf laid over it.** The plan says "the sea is drawn as
two meshes now — shelf and deep — instead of one slab", and it did not get that. The slab is
the sea's collider (nothing should ever fall past the sea, and a wreck thrown into it has to
come to rest somewhere), its thickness, its edge at the horizon, and the object `WaterLine`
lives on. Splitting it in two would have meant a collider with no renderer plus two flat
meshes, for a result that is pixel-for-pixel the same from a camera that always looks down.
So the slab is the open sea and the shelf is one flat mesh two centimetres above it, which is
what the plan actually wanted — the sea shows two colours — and `WaterLine` is untouched, as
the plan also asked. See the first gotcha for the one place this shows.

**The field is cached on the `LevelDefinition`, and it checks the map rather than being told
about it.** Everything that used to ask the rectangles now asks the field, and building one is
a hundred thousand cells of work, so it cannot be built per question. But the level editor
moves a coastline by writing straight into `Land` and has no way to announce that it has —
and a field that went stale would answer questions about a map somebody used to have, which
is exactly the class of bug this pass exists to remove. So `SurfaceField.Describes` compares
the land it was built from against the land it is being asked about, which is forty
comparisons, and rebuilds when they differ. In the editor that is once per completed edit
rather than once per frame: `Commit` runs on mouse-up, and a drag shows a ghost.

**`IsOnLand` now measures a signed distance, and that fixed a real bug.** It used to ask each
rectangle "do you contain this point with this margin?" and take the best answer, which is
wrong wherever two rectangles meet. A point a metre inside the seam where a bridgehead is
built onto a shore is within 2.5 m of an edge of *both* rectangles and was refused by both,
though it is thirty metres from any water. Nothing on the shipped map happened to stand
there. Something would have, and it would have been reported as standing in the sea.
`RectanglesThatMeetAreOneLandmassRatherThanASeam` is that case.

**There is no `SurfaceField.Active`.** The plan puts one there for Phase D to sample. Nothing
reads it yet, a static with no reader is exactly the rot `LevelCatalog` warns about, and it
would need a lifecycle — the sort `WaterLine` has, with the race about which instance may
clear it. It is also probably not needed at all: `LevelLoader.Current` already means "the map
that is up", it already survives a scene change, and `LevelLoader.Current.Field` is the same
sentence with no new static in it. Phase D should try that first.

**The schema version did not move, again, and for a stronger reason than last time.** Nothing
was added to the file format at all. The beach and the shelf are derived, so a Phase-A build
reading today's `iron-channel.json` builds exactly the map it built before, minus two colours.
Phase C is still where the bump belongs, because that is where a version-1 build would put a
bridgehead in the wrong place.

**Land painted with one of the two waters is now a fault rather than a curiosity.** Phase A
noted that nothing stopped a level painting a rectangle `ShallowWater`, and that it was
nonsense. It is worse than nonsense now: drowning goes by how low a vehicle is rather than by
what it is standing on, so such a rectangle is a stretch of sea you drive straight across —
while the field, which does go by the surface, counts it as a hole in the island and will
happily cut a map in two through the middle of it. `LevelValidation` names it.

**One metre cells, capped at 512 across.** The plan's recommendation, and it is a three-way
trade between memory, how fine a coastline can wiggle and how long anything that walks the
grid takes: a metre is a third of the widest vehicle, gives a four-metre beach four steps, and
costs about half a megabyte for a 240 m world. The cap is not tuning, it is a guard — a
half-extent is a number typed into an editor, and three digits of slip without a cap is not a
bad map but an allocation that takes the process with it. Past it the cells grow instead.

**The distance transform is exact rather than a chamfer.** Felzenszwalb and Huttenlocher, the
lower envelope of one parabola per cell, down the columns and along the rows. A 3-4 chamfer
would have been ten lines shorter and about 2% wrong, and 2% of the bunker's ten-metre margin
is 20 cm of a rule that exists to be exact. It is also linear, which searching for the nearest
coast cell is emphatically not.

### Gotchas, in the order they will bite

- **A coastline that is not on a metre line is rounded to one, and the slab it belongs to is
  not.** The field claims a cell for whichever rectangle covers its middle, so a rectangle
  edge at z = -6.5 becomes a coast at z = -6.0 — while `BuildLand` still draws the slab to
  exactly -6.5. The half metre between them is drawn by neither the slab nor the shelf, and
  the open sea shows through it as a thin dark line. On this map that is the four bridgehead
  edges at z = ±6.5, the only coordinates in `iron-channel.json` that are not whole metres;
  it is about three pixels in the map shot and invisible at play altitude. **It is bounded by
  half a cell and Phase C dissolves it**, because the land will be a mesh generated from the
  same field, so the two cannot disagree. If it ever matters before then, the fix is to draw
  every band *from the coast outward*: paint the sea slab with the shelf and draw the open sea
  as the mesh over it, so a rounding gap always shows the colour that belongs at a coastline.

- **The derived bands have cell-quantised edges, so a corner comes out as stairs.** Along a
  straight coast there is nothing to see; where a coast turns — around each bridgehead — the
  band's outer edge is a quarter circle drawn in one-metre steps. Invisible in the map shot,
  clearly visible in `surfaces-b-coast.png`. Marching squares in Phase C cuts a diagonal
  across a cell instead of stepping around it, and a coast that wanders will break the pattern
  up anyway, so this is a temporary look rather than a thing to design around.

- **`SurfaceTuning.For` allocates, and the field would have called it a hundred and fifteen
  thousand times per build.** It hands back a fresh copy on purpose, so callers can stamp and
  edit it, which is right for the handful of callers that want one row and ruinous for a loop
  over every cell. `SurfaceField.Table()` reads it once per kind into an array indexed by the
  enum. Anything else that walks the whole grid — Phase D's sampling will not, but a minimap
  might — must do the same.

- **The generated meshes are serialised into the two baked scenes.** They are small — 272 and
  168 vertices for the whole map — because the cells are merged into as few rectangles as they
  will go before any geometry is made. A wandering coastline in Phase C will not merge like
  that, and the moment to look at what the scene files weigh is the first bake after
  displacement is switched on.

- **Re-measure the ramp after anything that touches a colour or a smoothness.** It was
  re-measured here off `surfaces-b-map.png` and every surface came out on its Phase-A number
  to a tenth — deep 38.5, shelf 52.6, grass 73.1, asphalt 116.9, sand 190.0 — so the derived
  bands cost the palette nothing. The method is the one Phase A recorded: sample the pixels of
  a real render, never predict them.

- **Unity in batch mode is a GUI process, so PowerShell's `&` does not wait for it.** The test
  run appears to finish instantly, with an empty exit code and a half-written log. Use
  `Start-Process -Wait -PassThru` and read `ExitCode`. Also: `-projectPath unity` is resolved
  against the shell's working directory, which is not necessarily the repo root — pass it
  absolutely, or Unity helpfully creates a new project wherever it landed.

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -runTests -projectPath unity -testPlatform EditMode -testResults editmode.xml -logFile -
```

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -runTests -projectPath unity -testPlatform PlayMode -testResults playmode.xml -logFile -
```

### What Phase C inherits

- **A signed distance field to the coastline**, already built, already the thing every
  placement margin is measured against, and already the input Phase C wants: displacement is
  adding noise to `coast[]` before the surfaces are decided, and the rim pass and every margin
  then follow the moved coast without being told about it.
- **`SurfaceMesh`**, with one entry point and one tessellator to swap: rectangles merged
  greedily today, marching squares tomorrow, over the same cells.
- **`NaturalEdge`, still read by nothing.** It is the flag that says which rectangles may be
  displaced, and Phase C is the phase that reads it. `LevelDesignTests` already leans on it to
  say what a crossing is.
- **The schema bump, still owed**, and the `Ellipse` shape, and the coastline skirt, and the
  one non-convex `MeshCollider` that replaces the slabs.
- **Two artefacts that dissolve rather than needing fixing** — the half-metre rounding gap and
  the stepped corners — both of which are the field and the geometry disagreeing, which stops
  being possible when the geometry comes out of the field.

### File map

| Path | Change |
|---|---|
| `unity/Assets/RF/Scripts/Levels/SurfaceField.cs` | **New.** The map rasterised: surface and signed distance-to-coast per cell, the rim rule, and an exact Euclidean distance transform |
| `unity/Assets/RF/Scripts/Levels/SurfaceMesh.cs` | **New.** Cells of one surface merged into rectangles and turned into a flat mesh |
| `unity/Assets/RF/Scripts/Levels/SurfaceTuning.cs` | `BeachWidth` becomes `RimSurface` + `RimWidth`; the sea gets a 5 m shelf; `Rims()` |
| `unity/Assets/RF/Scripts/Levels/LevelDefinition.cs` | Gains `Field`, cached and self-checking; `IsOnLand` asks it |
| `unity/Assets/RF/Scripts/Levels/LevelBuilder.cs` | `BuildCoast` lays the derived rims over the map; the sea says what it is |
| `unity/Assets/RF/Scripts/Levels/LevelValidation.cs` | The flood fill walks the field; land painted with water is a fault |
| `unity/Assets/RF/Tests/EditMode/SurfaceFieldTests.cs` | **New.** The field, the two bands, the seam, determinism, the cache, the cap, and the shipped map |
| `unity/Assets/RF/Tests/EditMode/SurfaceTests.cs` | The rim columns, read against each other |
| `unity/Assets/RF/Tests/EditMode/LevelValidationTests.cs` | The new fault |
| `unity/Assets/RF/Scenes/Sandbox.unity`, `LevelEditor.unity` | Rebuilt, so the bake wears the coast |
| `return-fire-homage-asset-spec.md` | Says the beach and shelf are derived, and that a prop near water stands on sand |
| `surfaces-b-map.png` | **The map.** How this phase is judged |
| `surfaces-b-coast.png` | One bridgehead from the camera you play through, which is where the beach has to work |

Not changed, and worth noticing: **`iron-channel.json`**, `LevelLand.cs`, `LevelNames.cs`,
`LevelCatalog.cs`, `WaterLine.cs`, and every generated material.

### Verified

- **428 tests pass**: 311 edit-mode, 117 play-mode. Thirteen of the edit-mode ones are new —
  eleven for the field, one more for the rim columns, one for the new fault — and none of the
  428 had to be changed to accommodate this phase.
- **Both scene builds and both stills run clean**, with no warnings and no exceptions during
  generation, and the catalog reports no problems.
- **The ramp was re-measured off the map shot** and is unchanged to a tenth of a level.
- **The baked `Sandbox.unity` carries the coast**: a `Coast` group with a `Sand` and a
  `ShallowWater` mesh, 168 and 272 vertices, and the same in `LevelEditor.unity`.
- **The sandbox still is pixel-identical to Phase A's**, which is the claim that this phase
  changed no balance stated as a measurement rather than as an intention.

**Not verified: whether five metres is the right shelf.** It is argued against the beach and
capped by the narrows, and it looks right in both stills, but nobody has driven past one yet.
The first person to mistake a pale narrows for something they could cross is the test that
matters, and that is Phase D at the earliest.

**Also not verified: whether green is still the right interior.** Phase A left this open
pending a beach. There is a beach now, and the island reads as an island — but the reference
shot is a sand island with grass over it, and this is a grass island with sand around it.
They are not the same picture. Phase E repaints the map and is the place to settle it.

---

## Phase C — the coast stops being a rectangle

### What Phase C is

**Nothing on the map is drawn per rectangle any more.** The island is one shape cut out of
`SurfaceField`, so the coastline the game measures against and the coastline you can see are
the same line and cannot come apart. Every coast a natural surface owns is displaced by up to
three metres of seeded noise; every asphalt edge, and the water within two metres of one, is
exactly where the level file wrote it. That contrast is the whole phase, and
`surfaces-c-coast.png` is where it reads: a shore that wanders on both sides of a bridgehead
that is dead straight and square.

The other half is that the map now has a **bank**. Land is at `y = 0` everywhere — the game is
resolved on one plane — so the 1.2 m drop to the water is the only relief on the whole island,
and until this phase it was the side of a box. It is now cut from the coastline itself, one
mesh per surface, painted that surface's colour taken down a step.

`iron-channel.json` gained exactly two things: `"Seed": 1995` and `"SchemaVersion": 2`. Not one
coordinate moved. The `Ellipse` shape is in the format and in the tests and no map uses it yet;
repainting the shores as ellipses is Phase E, which is where the plan puts it.

Nothing about how the game *plays* changed, for the third time. `Grip`, `FuelDraw` and
`SurfaceSensitivity` are still read by nothing — that is Phase D.

### Decisions made, and why

**Three metres of wobble, not the plan's 1.5.** The plan capped it at 1.5 m to stay inside
`ShoreMargin`, and 1.5 m was built, rendered and rejected, because **value noise only reaches
its full height at a lattice point**: the coast typically moves about half of whatever the
amplitude says and reaches all of it perhaps seven times along a 164 m shore. At 1.5 m that is
four pixels on the map shot and you have to look for it. Three metres is past the 2.5 m that
every prop owes the water, which matters less than it sounds: `LevelValidation` measures that
against the field, so a prop the coast wandered up to is **named** rather than quietly left
standing in the sea. The tightest thing on the shipped map is a tree drawn 3 m inland which the
realised coast leaves standing 3.5 m from the water. Both amplitudes pass all 444 tests; the
choice was made off a render, which is the only way this project has ever chosen a number.

**Built land is measured; natural land is counted in cells.** These are two different
calculations on purpose and it is the least obvious thing in the phase.

- A built shape answers `LevelLand.Signed` directly, so its edge is exact *between* two cells.
  That is what keeps each causeway 12 m wide because somebody typed 12, and each bridgehead's
  narrows at the 13 m a test worked out a jeep cannot jump. It also dissolves Phase B's
  half-metre rounding gap: the four bridgehead edges at z = ±6.5 fall exactly on a cell middle,
  and they now come out at ±6.5 rather than at ±6.0.
- A natural shape is measured through the realised cells with the same distance transform
  everything else on this grid uses, because **the best answer from a list of shapes is not the
  distance to the edge of what they make together**. Where two rectangles meet it is nearly
  zero however far inland that place is — so an island drawn as two rectangles side by side,
  plus three metres of wobble, gets a channel cut straight down the join. That is not
  hypothetical: it is what `RectanglesThatMeetAreOneLandmassRatherThanASeam` caught on the
  first run, and it is the same seam Phase B fixed for margins, arriving a second time by a
  different door. The cost is that a natural coast is rounded to half a cell before the wobble
  is added, which is a sixth of the wobble and invisible under it.

**The map is a stack of sheets, and each one covers every sheet above it.** Cut as five
separate pieces that had to meet exactly, three of them would meet at a point somewhere on the
map and leave a hole a quarter of a cell across showing whatever is under the whole island.
Cut as a stack — open sea, shelf, sand, grass, asphalt, each a couple of centimetres over the
last and each drawn over its own cells *and every layer above it* — the sheet below is always
a superset of the one above, so a gap is impossible rather than unlikely, and the colour that
shows through any disagreement is the one that belongs just outside it. That is one new column
in `SurfaceTuning` (`Layer`) and no new rules. A layer nobody's surface occupies is not drawn,
so the lowest sheet on a map with no beach is the grass rather than an invisible sand island
underneath it.

**Every layer's edge at the water is the coastline, exactly.** `SurfaceField.Layer` measures
two things and takes the smaller: how far the cell is from the nearest cell of the same host
the layer does *not* cover — which is what puts the beach's inland edge where the sand runs out
— and how far it is inside the host, which is the measured coastline. The water is deliberately
not counted in the first, so a road that ends at the sea has no inland edge there to find and
inherits the coastline instead. Without that, the asphalt sheet stopped half a cell short of
the bank at each bridgehead and showed a stripe of beach on the end of a road.

**One collider, on the top face only.** A collider per sheet would leave a vehicle resting on a
two-centimetre step wherever a road met a field. Giving the bank one would turn the coastline
into a wall — and driving into the sea is one of the things this game is about. So the island is
one non-convex `MeshCollider` over the lowest sheet, which is the whole island, and the sea slab
keeps its own for everything that falls off.

**The sea is still a slab.** Same as Phase B, same reasons: it needs a collider, a thickness, an
edge at the horizon and somewhere for `WaterLine` to live. It is the bottom of the stack and the
only sheet not cut to a shape, which is what guarantees no gap anywhere on the map can show
something that is not water.

**A bank per surface rather than one bank colour.** Confirmed with the user against the plan's
"a darker bank colour". Deriving it from the surface above means the map cannot grow a colour
the ramp has never been read against, and a repaint of a surface repaints its coast with it. One
shade factor for every row (`SurfaceTuning.BankShade`, 0.7), because there is no argument yet for
a sand coast being a different darkness from an asphalt one.

**The schema moved to 2, finally.** Phases A and B both declined it and both were right — a
build that had never heard of surfaces lost two colours and still built the same map. A map
authored against a displaced coast and cut to a shape does not survive that: the coastline is in
the wrong place, an ellipse comes out square, and a bridgehead measured against one of those
could stand in the sea. That is exactly the sentence `LevelFile` refuses a file with.

**One blur of the layer distances.** A boundary that is not the coastline — the inland edge of a
beach — is decided a whole cell at a time, so where it runs nearly along the grid it came out as
long straight runs with a one-metre step every so often. That was the most visible thing on the
first render, on the one boundary with a strong contrast across it. Two passes of a five-point
blur *of the distance*, not of the picture: a blur leaves a field that is already straight
exactly where it was, and only rounds off where the cells disagree with each other. It cannot
touch a coastline, because the coastline is not in the blurred number — the measured outline is
taken against it afterwards and the smaller of the two wins.

### Gotchas, in the order they will bite

- **A metre of noise is not a metre of coast.** Written above and repeated here because it will
  be the first thing anybody changes: doubling `SurfaceNoise.Amplitude` doubles the wander, but
  the number is not the wander. Half of it is a good guess for what you will see.

- **The baked scenes went from 200 KB to 2.7 MB, and it was 26 MB first.** A coastline cut cell
  by cell is a lot of triangles, and the first version tessellated the *interior* of every sheet
  the same way — two triangles per square metre, 55,000 for the sand alone. `SurfaceMesh` now
  merges squares that are wholly inside a layer into as few rectangles as they will go and cuts
  only the boundary ones, which is the same shape and a twenty-fourth of the geometry. The
  remaining 2.7 MB is real and is the price of a coast that wanders. **Watch this number.** The
  largest single sheet is the shelf at 14,571 vertices, because it is a five-metre band with two
  wandering edges and almost no interior.

- **Everything on the map stands two centimetres under the grass.** Props are placed at `y = 0`
  and the grass sheet is the second layer of the stack, so it is drawn at `y = 0.02` and the
  asphalt at `y = 0.04`. Invisible at any camera this game uses, and worth knowing before
  somebody wonders why a decal will not sit flat.

- **`SurfaceField.Outline` and `SurfaceField.ToTheCoast` are two different numbers on purpose,
  and using the wrong one is silent.** The outline is what the coastline is *cut* from: exact for
  built edges, and with a seam in it wherever a built shape meets a natural one. The measured
  distance is what a margin is *measured* against: no seam anywhere, and a little coarser.
  Anything asking "how far is this from the water" wants `ToTheCoast`. Only `SurfaceMesh` reads
  the outline.

- **A prop can now end up nearer the water than the level file suggests.** Predicted by the plan
  and it has not bitten yet, because the shipped map has nothing closer than 3 m to a shore. It
  will bite Phase E, which moves props. The tell is `LevelValidation` naming the prop; the fix is
  to move it, and to move its twin, because `EveryPropOnTheMapHasAMirrorImage` is watching.

- **`LevelCatalogBuilder.Load` still does not refresh**, and there are three more generated
  materials to be caught by it now. Change `BankShade` and the next render shows the old banks
  until **Build Level Catalog** runs. `EveryGroundSurfaceHasABankPaintedTheColourTheTableSays`
  runs `EnsureAssets` itself, so a test run also fixes it.

- **The sandbox still is no longer pixel-identical to Phase A's, and the difference is
  meaningless.** 82% of it is identical, 18% differs by one part in 255, and 343 pixels — a
  hundredth of a percent — differ visibly because a piece of wreckage settles a few centimetres
  differently on a mesh collider than it did on a box. It is recorded because Phase B's notes
  make a point of the still being identical, and somebody will check.

- **Unity in batch mode is a GUI process**, `-projectPath` resolves against the shell's working
  directory, and `LevelPreview`/`VehicleSandboxScene` take `-levelOutput`/`-sandboxOutput`. All
  of that is Phase B's gotcha and all of it is still true. There is no menu item for the coast
  shot — it was rendered from a throwaway editor script placing a camera 34 m up at 58 degrees
  over the west bridgehead, and the script was deleted.

### The palette, re-measured

| | Top | Bank |
|---|---|---|
| DeepWater | 38.5 | — |
| ShallowWater | 52.6 | — |
| Grass | 73.1 | never seen |
| Asphalt | 116.9 | 67 |
| Sand | 190.0 | 114 |

The five tops are unchanged from Phase A and Phase B **to a tenth of a level**, so cutting the
map out of a field rather than out of boxes cost the ramp nothing. The banks are measured off
`surfaces-c-coast.png` rather than off the map shot, because a vertical face has no area seen
from directly overhead — which is also why the sand bank at 114 between a beach at 190 and a
shelf at 52 is worth the trouble: from the camera you play through it is a step on both sides.
The grass bank exists, is generated, and is never seen, because grass hands its own coast to
sand four metres before it reaches the water.

### What Phase D inherits

- **A field that still answers the same three questions**, plus `Outline` and `Layer`, neither of
  which Phase D wants. `LevelLoader.Current.Field` is still the way to reach it and there is
  still no `SurfaceField.Active`.
- **`Grip`, `FuelDraw` and the whole handling half of the table, still read by nothing.** Phase D
  is `GroundVehicleMotion.Step` taking a `SurfaceTuning`, `VehicleTuning.SurfaceSensitivity`, and
  `VehicleSupply` multiplying demand — and it is independent of everything here.
- **A road that is now visibly a road.** Phase A argued that asphalt at 1.06 grip is the point of
  having roads and nobody could see one; there are four hard-edged crossings on the map now and
  the first thing Phase D should measure is a jeep on one against a jeep on the sand beside it.
- **`Ellipse`, unused.** Phase E is where an island stops being two rectangles.

### File map

| Path | Change |
|---|---|
| `unity/Assets/RF/Scripts/Levels/SurfaceNoise.cs` | **New.** The wobble: hashed value noise, the amplitude, and the guard that holds a built edge still |
| `unity/Assets/RF/Scripts/Levels/LandShape.cs` | **New.** The shape enum, with its empty member |
| `unity/Assets/RF/Scripts/Levels/SurfaceField.cs` | `Cut` replaces `Paint`: built shapes measured, natural ones counted, both displaced. Gains `Outline`, `Layer`, `Covers`, `Middle` |
| `unity/Assets/RF/Scripts/Levels/SurfaceMesh.cs` | Rewritten: marching squares, merged interiors, and the bank |
| `unity/Assets/RF/Scripts/Levels/SurfaceTuning.cs` | Gains `Layer`, `Bank`, `BankShade` and `Stack`; `NaturalEdge` is read at last |
| `unity/Assets/RF/Scripts/Levels/LevelLand.cs` | Gains `Shape`, `Form` and `Signed`; `Contains` is measured rather than compared |
| `unity/Assets/RF/Scripts/Levels/LevelDefinition.cs` | Gains `Seed`; schema 2 |
| `unity/Assets/RF/Scripts/Levels/LevelNames.cs` | Gains `ToShape` |
| `unity/Assets/RF/Scripts/Levels/LevelBuilder.cs` | Builds sheets, banks and one collider instead of slabs |
| `unity/Assets/RF/Scripts/Levels/LevelCatalog.cs` | Gains `BankFor`, and a problem when a ground surface has no bank |
| `unity/Assets/RF/Scripts/Levels/LevelSurfaceMaterial.cs` | Gains `Bank` |
| `unity/Assets/RF/Scripts/Levels/LevelValidation.cs` | Names an unknown shape; the world's edge allows for the wobble |
| `unity/Assets/RF/Editor/ArtPipeline/GeneratedMaterials.cs` | Generates `RF_Bank_*` from the table |
| `unity/Assets/RF/Editor/Gameplay/LevelCatalogBuilder.cs` | Wires the banks |
| `unity/Assets/StreamingAssets/Levels/iron-channel.json` | A seed, a schema version, and a paragraph. No coordinates |
| `unity/Assets/RF/Art/Materials/RF_Bank_*.mat` | **New.** Generated, three of them |
| `unity/Assets/RF/Levels/LevelCatalog.asset` | Rebuilt |
| `unity/Assets/RF/Scenes/Sandbox.unity`, `LevelEditor.unity` | Rebuilt, and thirteen times the size |
| `unity/Assets/RF/Tests/EditMode/SurfaceMeshTests.cs` | **New.** The exact built edge, the cut coast, the stack, the bank, the winding |
| `unity/Assets/RF/Tests/EditMode/SurfaceFieldTests.cs` | Wander against exactness, the seed, the ellipse; the band tests measure from the realised coast |
| `unity/Assets/RF/Tests/EditMode/SurfaceTests.cs` | The stack, the bank shade, the bank materials, the shape round trip |
| `unity/Assets/RF/Tests/EditMode/LevelValidationTests.cs` | The new fault |
| `return-fire-homage-asset-spec.md` | The banks, and that a coastline is not where the file draws it |
| `surfaces-c-map.png` | **The map.** How this phase is judged |
| `surfaces-c-coast.png` | The west bridgehead from the camera you play through, which is where wander-against-exact reads |
| `surfaces-c-sandbox.png` | The same raid again, for the record |

Not changed, and worth noticing: every coordinate in `iron-channel.json`, `WaterLine.cs`,
`LevelPick.cs` and the whole level editor. The editor picks against the definition rather than
against the geometry, which is why replacing all of the geometry cost it nothing.

### Verified

- **444 tests pass**: 327 edit-mode, 117 play-mode. Sixteen of the edit-mode ones are new. Four
  existing field tests were rewritten to measure from the realised coast rather than from a
  rectangle's edge, and a fifth gained a line — the same change of habit Phase A's redraw forced
  on `LevelDesignTests`, because a test that names a coordinate is a test about a map somebody
  used to have. **This phase changed nothing in `LevelDesignTests` or in either play-mode
  file**, which is the plan's stated bar for it.
- **Build Level Catalog, both scene builds and all three stills** run clean, with no warnings and
  no exceptions during generation, and the catalog reports no problems — which now includes a
  bank per ground surface, so a missing one would have said so by name.
- **The ramp was re-measured** off `surfaces-c-map.png` and is unchanged to a tenth of a level;
  the banks were measured off `surfaces-c-coast.png`.
- **The crossings were measured on the realised map**, not on the file.
  `ANaturalCoastWandersAndABuiltOneDoesNot` walks the widest causeway at every metre along it
  and finds its written width every time, on the same map whose southern shore it finds at more
  than one waterline; `ABuiltEdgeComesOutWhereTheFileWroteIt` holds the geometry itself to a
  millimetre, on an island whose edges fall exactly on a cell middle.

**Not verified: whether three metres is the right wander.** It is a rendered choice rather than a
guessed one, which is better than 1.5 m was, but nobody has driven along one of these coasts yet.
The thing to watch is whether a bay big enough to see is also a bay big enough to hide a tank in,
because that is a balance change nobody asked for.

**Also not verified: whether green is still the right interior.** Left open by Phase A and by
Phase B. There is a beach, a shelf, a bank and a coastline now, and the island reads as an
island — but the reference shot is still a sand island with grass over it and this is still a
grass island with sand around it. Phase E repaints the map and is the place to settle it.

---

## Phase D — the ground is something you feel

### What Phase D is

**The surface table stopped being a paint chart.** Three columns of it that nothing had ever
read — `Grip`, `FuelDraw`, and the vehicle-side weighting the plan asked for — are now in the
loop that moves every ground vehicle, fifty times a second. A jeep on a road is faster than a
jeep in a field, a jeep on a beach is a fifth slower than either, a tank crossing the same
beach barely notices, and every one of them pays the beach in fuel.

Nothing was drawn, moved, repainted or rebuilt. `iron-channel.json` is byte-for-byte the file
Phase C left, no material changed, no mesh changed, and no still was rendered — there is
nothing to photograph, which is the first time that has been true in this pass. What changed
is entirely in how the map plays, and the only way to see it is to measure it.

The shape of it is two numbers multiplied:

```
traction = lerp(1.0, surface.Grip, vehicle.SurfaceSensitivity)
```

and that one figure scales the vehicle's top speed, its acceleration and its turn rate. Fuel
is separate and deliberately simpler: the ground multiplies the *working* half of the engine's
demand, and every vehicle pays it in full.

### Decisions made, and why

**Sensitivity weighs grip and nothing else; thirst is paid in full by everybody.** Confirmed
with the user against the plan, which is silent on it. They are different claims: grip is
about how a vehicle puts its power down, which is what tracks are for, and thirst is about how
much power the ground demands, which soft sand demands of six tonnes as readily as of one. The
consequence is the interesting part and it was the reason for choosing it: **the beach costs
the jeep time and costs the tank range.** A tank that shrugged off sand in both columns would
be strictly better on every surface on the map, and the one row that is supposed to cost
something would cost it nothing.

**Grip never touches the brakes.** The table says top speed, acceleration and turn rate, and
that list is now enforced rather than described. Soft ground that also took the stopping
distance away would make a beach a death trap rather than a slow lane, on a map where the
thing just past the beach drowns you. It also means driving off a road onto sand needs no
special case at all: the target speed drops below the speed the vehicle already has, which is
*already* the definition of slowing down in `StepSpeed`, so it sheds the difference at its own
braking rate and settles at what the sand allows.

**The surface is handed to the model rather than looked up by it.** `GroundVehicleMotion.Step`
takes a `SurfaceTuning` — a row of a table, not a field, not a scene — so the whole of the new
behaviour is still testable with no map, no rasterisation and no play mode, which is what the
class has protected since M2. `null` means *nothing in particular* and answers 1.0, which is
what a rig with no world under it is standing on and what an aircraft is over.

**The helicopter is excluded by never asking.** `GroundVehicle` samples the field;
`Helicopter` passes `null` and always did. That is the same free result that already keeps it
from drowning — a rule about what a thing does rather than a check on what it is. Its
`SurfaceSensitivity` is 0.0 as well, which is belt to that pair of braces: a helicopter handed
a surface by some future mistake would still fly exactly as it flies over the sea, and a test
says so.

**`LevelLoader.Current.Field`, and still no `SurfaceField.Active`.** Phase B declined to add
the static the plan asked for and told Phase D to try the existing one first. It works, and it
is better: `LevelLoader.Current` already means "the map that is up", it already survives a
scene change, and the field behind it already rebuilds itself when the land has moved — so a
level editor that drags a coastline is driven on correctly with nothing announcing anything.
See the gotchas for the one thing that buys.

**The row is looked up when the ground changes, not fifty times a second.**
`SurfaceTuning.For` hands back a fresh copy on purpose, and Phase B's notes flag it as ruinous
in a loop. A vehicle crosses a handful of boundaries a minute, so `GroundVehicle` keeps the
last kind and the last row and only calls the table when the kind changes. `Standing` and
`Underfoot` are public because that cache is exactly what M8's dust, debris colour and engine
audio will want, and none of them should sample the field a second time.

**Fuel scales the working half of the draw, not the whole of it and not the demand.** Three
choices and only one is true. Scaling the whole draw would charge a vehicle extra for standing
still on a beach it is not driving on, and would quietly retune every depot, because a
refuelling rate is only fast or slow against the draw it is racing. Scaling the *demand* would
be worse than untidy: demand is clamped into 0..1, so a thirstier surface would have cost
nothing at all at full throttle, which is precisely where it should cost the most. So
`DrawFor` gained a third argument and multiplies only the part that is doing work.

**`NoJeepCanJumpTheChannel` had to learn about the road.** The test computes the jeep's
ballistic jump from its real top speed, and as of this phase the jeep's real top speed depends
on what it is launching from — and every crossing on this map is asphalt right up to the
waterline. It now takes the best traction any surface *on the map being tested* offers, walked
rather than named, so a map that grows a surface or a table that grows a row is measured
instead of assumed. The margin is in the numbers below.

### The numbers, measured

Metres actually covered by a rigidbody, from a standing start, full throttle for four seconds,
down a ninety-metre straight of one surface. Not predicted — driven, in
`SurfaceDrivingTests`, over a throwaway three-strip level built for the purpose, which is the
same trick Phase A used to measure a colour.

| Four seconds from rest | Road | Open country | Beach |
|---|---|---|---|
| Jeep | **84.11 m** | 79.35 m | **63.48 m** |
| Tank | 34.47 m | — | 32.03 m |

- **A beach costs the jeep 24.9% of a road run and costs the tank 7.1%.** That is the phase's
  bar — "a jeep crossing the causeway is measurably quicker than a jeep crossing the sand
  beside it, and a tank barely notices the difference" — as a measurement rather than an
  intention.
- **A road is worth 6.0% over open country**, which is a real number and a small one. See the
  gotchas: it is small because this map is a grass island, not because the road is weak.

The table those come out of, for reading against the two tuning tables:

| | Sand (0.80) | Grass (1.00) | Asphalt (1.06) |
|---|---|---|---|
| Jeep (sensitivity 1.00) | 0.800 → 17.60 m/s | 1.000 → 22.00 m/s | 1.060 → 23.32 m/s |
| ASV (0.45) | 0.910 → 8.19 m/s | 1.000 → 9.00 m/s | 1.027 → 9.24 m/s |
| Tank (0.25) | 0.950 → 11.40 m/s | 1.000 → 12.00 m/s | 1.015 → 12.18 m/s |
| Helicopter (0.00) | 1.000 | 1.000 | 1.000 |

And the fuel, measured the same way — a tank, four seconds flat out, in seconds of running
drawn from the pool:

| | Road | Open country | Beach |
|---|---|---|---|
| Flat out | 3.853 | 4.018 | **4.519** |
| Parked, engine running | 0.830 | — | 0.805 |

The beach costs the tank **12.5% more fuel than open country while it is moving and nothing at
all while it is parked**, which is the decision above stated as two measurements. The two
parked figures differ by 3% in the *wrong* direction, which is frame-count noise rather than
anything about sand — see the gotchas.

### Gotchas, in the order they will bite

- **`IronFlag.Vehicles` now depends on `IronFlag.Levels`, which already depended on
  `IronFlag.Vehicles`.** `LevelValidation` and `WaterLine` have read the vehicle tables since
  M7; `GroundVehicleMotion` and `GroundVehicle` now read the surface table and the loader. It
  compiles because `IronFlag.Runtime` is one assembly, and it is the arrangement the plan
  asked for — but it is a cycle, and the moment anybody splits this project into two assemblies
  it is the first thing that will refuse. The seam if it ever matters is small: everything
  Phase D added crosses at exactly two places, `Step`'s new argument and
  `GroundVehicle.ReadTheGround`.

- **A serialized field added to `VehicleTuning` is a phase that silently does nothing.** Unity
  gives a prefab that has never heard of `SurfaceSensitivity` the field initializer, which is
  zero, which is "ignores the ground" — so every vehicle in the game would have driven exactly
  as it did before, on every surface, with all 341 edit-mode tests passing. **Run
  `Tools > IronFlag > Build Vehicle Prefabs`.** `EveryPrefabCarriesItsOwnTuning` now checks the
  column, so this is caught rather than shipped, but it will be caught *after* somebody has
  wondered why nothing happened. The rebuild is a one-line diff per prefab; the scenes carry
  prefab instances rather than copies of the tuning, so nothing else moved.

- **`LevelLoader.Current` outlives the scene it came from, and a vehicle now reads it.** A rig
  assembled in a scene with no loader in it is standing on whatever map the last test to load
  one left behind. Today that is harmless — the shipped map is water at the origin, and water
  has no opinion about handling — but it is luck rather than design, and it is the reason
  `SurfaceDrivingTests` puts back the map it inherited in its teardown. Anything that measures
  a speed in play mode should either put a map up on purpose or expect to be standing on
  somebody else's.

- **A metre of the road bonus is not free: the narrows are the ceiling.** `NoJeepCanJumpTheChannel`
  demands the narrowest water on the map be half again the distance a jeep clears leaving a
  bank. At asphalt 1.06 the jeep clears 6.2 m and the test wants 9.3 m of the 13 m the
  bridgeheads hold, so there is room — but the road cannot go past a grip of about **1.47**
  before a jeep launching off a bridgehead can be argued over the channel it is supposed to
  need a bridge for. That number is the real cap on how exciting a road is allowed to be on
  this map, and it moves if `WaterDepth` or the jeep's top speed does.

- **The road is worth 6% today because the map is grass, not because 1.06 is timid.** Road
  against sand is 24.9% and there is nowhere on `iron-channel` to drive it: sand exists only as
  the four-metre rim every coastline derives, which is a place you drive *across* rather than
  along. Phase E repaints the island and is the first time the 0.80 row is felt over a distance
  — and is therefore the first honest chance to decide whether 1.06 and 0.80 are right.

- **The play-mode fuel figures are frame-count noise at the third decimal.** `VehicleSupply`
  burns per frame off `Time.deltaTime` while the runs are driven per fixed step, so two runs of
  "four seconds" are not the same number of `Update` calls. It is worth about 3%, which is
  nothing against the 12.5% the beach costs and is everything against the 0% a parked vehicle
  should. `AParkedVehiclePaysNothingForTheGroundUnderIt` is written with a 5% tolerance for
  exactly this reason, and the arithmetic it is a proxy for is pinned exactly in
  `SoftGroundCostsFuelOnlyWhileTheEngineIsWorking`.

- **A causeway is twenty-six metres long, which is not a test track.** A jeep at full throttle
  is off the far end of one in rather less than a second, which is why the long comparisons are
  run over a purpose-built three-strip level and why the one test that measures the *shipped*
  map compares two standing starts instead. Grip scales acceleration as well as the ceiling, so
  a four-tenths-of-a-second burst measures it without needing a straight nobody built.

- **`SurfaceTuning.For` still allocates**, still on purpose, and there is now a caller in a
  fixed step. `GroundVehicle` keeps the last row and only asks the table when the kind under it
  changes. Anything else that wants to know what a vehicle is standing on should read
  `GroundVehicle.Underfoot` rather than calling the table itself.

- **Unity in batch mode is a GUI process**, `-projectPath` resolves against the shell's working
  directory, and a compile error makes `-executeMethod` exit 1 while `Start-Process` still
  looks like it worked. All of that is Phase B's gotcha and all of it is still true; the new
  corner is that a failed `-executeMethod` run leaves the prefabs *unrebuilt* and says so only
  in the log.

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -quit -projectPath unity -executeMethod IronFlag.Editor.Gameplay.VehiclePrefabBuilder.BuildAll -logFile -
```

### What Phase E inherits

- **A repaint is now a balance change.** Until this phase, moving the boundary between grass
  and sand moved a colour. It moves a route now, and it moves it differently for each of the
  three vehicles that drive. That is the whole reason the plan put the repaint last.
- **The two numbers most worth arguing, and the first map on which the argument is visible.**
  Asphalt 1.06 and sand 0.80 have been measured but not played. Sand as an interior rather than
  a rim is what makes them matter, and it is Phase E's job.
- **A ceiling on the road bonus of about 1.47**, set by the narrowest water on the map and the
  jeep's jump, and enforced by a test that now measures it rather than assuming it.
- **`GroundVehicle.Standing` and `GroundVehicle.Underfoot`**, which are the hook M8's wheel
  dust, debris colour and per-surface engine audio all wanted. All three read one cached row
  rather than sampling the field again.
- **A rig for measuring any future row**: `SurfaceDrivingTests` lays a three-strip level and
  drives ninety-metre straights down it. A new surface is one more strip and one more run.
- **`Ellipse`, still unused**, still waiting for the phase where an island stops being two
  rectangles.

### File map

| Path | Change |
|---|---|
| `unity/Assets/RF/Scripts/Vehicles/VehicleTuning.cs` | **Gains `SurfaceSensitivity`**, and four rows argued against each other |
| `unity/Assets/RF/Scripts/Vehicles/GroundVehicleMotion.cs` | `Step`, `StepSpeed` and `StepYaw` take a surface; **new `Traction`** |
| `unity/Assets/RF/Scripts/Vehicles/GroundVehicle.cs` | Samples the field each fixed step; `Standing` and `Underfoot` |
| `unity/Assets/RF/Scripts/Vehicles/Helicopter.cs` | Passes no surface, in one line and a paragraph |
| `unity/Assets/RF/Scripts/Supply/VehicleSupply.cs` | `DrawFor` and `Burn` take the ground's thirst; `GroundDraw` |
| `unity/Assets/RF/Scripts/Levels/SurfaceTuning.cs` | `Grip` and `FuelDraw` say what they now do, and what they deliberately do not |
| `unity/Assets/RF/Prefabs/Vehicles/RF_Vehicle_*.prefab` | Rebuilt. One line each |
| `unity/Assets/RF/Tests/EditMode/GroundVehicleMotionTests.cs` | Eight new: traction, the two ceilings, acceleration, the brakes, the turn, leaving a road |
| `unity/Assets/RF/Tests/EditMode/VehicleRosterTests.cs` | The sensitivity column, and the two tables read against each other |
| `unity/Assets/RF/Tests/EditMode/SupplyRosterTests.cs` | The ground's thirst, and that the slow surface is the thirsty one |
| `unity/Assets/RF/Tests/EditMode/SurfaceTests.cs` | No unusable handling numbers; neither water has an opinion |
| `unity/Assets/RF/Tests/EditMode/LevelDesignTests.cs` | The jeep's jump is measured off the best ground on the map |
| `unity/Assets/RF/Tests/EditMode/VehiclePrefabTests.cs` | The new column, so an unrebuilt prefab is caught |
| `unity/Assets/RF/Tests/PlayMode/SurfaceDrivingTests.cs` | **New.** Three strips, six measurements |
| `unity/Assets/RF/Tests/PlayMode/LevelLoadingTests.cs` | Every permanent crossing is quicker than the country beside it |
| `README.md`, `SURFACES_PLAN.md` *(now the appendix above)* | Status |

Not changed, and worth noticing: **`iron-channel.json`**, every material, every mesh, both
baked scenes, `LevelCatalog.asset`, `SurfaceField.cs`, `SurfaceMesh.cs`, `LevelBuilder.cs` and
the whole level editor. This phase is the first in the pass that touches no asset at all.

### Verified

- **465 tests pass**: 341 edit-mode, 124 play-mode. Twenty-one are new — fourteen edit-mode,
  seven play-mode. One existing test was rewritten (`NoJeepCanJumpTheChannel`, to measure the
  launch off the best ground rather than off the table) and four gained a line; the rest of the
  444 that existed before this phase are untouched and still pass.
- **The four vehicle prefabs were rebuilt** and the diff is one line each. `SandboxWiringTests`
  and `LevelEditorWiringTests` still pass, so neither baked scene needed rebuilding — the
  scenes carry prefab instances, not copies of the tuning.
- **No level catalog rebuild, no scene rebuild and no render**, because nothing this phase
  touched is visible. That is checked rather than assumed: no material, mesh, colour or
  coordinate changed, and the wiring tests compare each bake against what the loader builds.
- **Every number in the tables above was measured off a real rigidbody**, per the standing rule
  this pass has followed since Phase A: sample the run, never predict it.

**Not verified: whether 1.06 and 0.80 are the right numbers.** They have been driven now, which
is more than Phase A could say, but they have been driven on a map with no sand to speak of. The
first time anybody chooses a longer route to stay on a road is the test that matters, and there
is no route on `iron-channel` that offers the choice. That is Phase E.

**Also not verified: whether the tank is now too comfortable.** A twentieth off its speed on a
beach is close to nothing, and the thing that is supposed to make up for it — 12.5% more fuel —
is a cost the player pays much later and somewhere else. If the sandy island turns out to be a
tank's map, the number to look at first is the tank's 0.25 rather than the sand's 0.80.

**Also not verified, and unchanged from Phase C: whether green is the right interior.** Four
phases have now deferred it, which is the correct answer each time and is starting to be
conspicuous. Phase E settles it.

---

## Phase E — the map

### What Phase E is

**The map is a place.** `iron-channel.json` went from eight axis-aligned rectangles to forty
shapes: two islands cut as overlapping ellipses, each **drawn as sand with grass laid over the
middle of it**, and a road network per side running from the bunker out to its depots and on to
its crossings. `surfaces-e-map.png` is the result and is how this phase is judged.

Nothing that decides a match moved. Both bunkers, all four towers, all four depots and both
bridges are at the coordinates M7 gave them; the two causeways are still 12 m wide at x ±62 and
each bridge still spans 13 m of narrows. Every coastline around them, and every prop that is not
one of those, moved.

The other half is that the surface table finally has somewhere to matter. Phase D's notes close
by saying the road's 1.06 and the sand's 0.80 "have been driven now, but they have been driven
on a map with no sand to speak of", and that "there is no route on `iron-channel` that offers
the choice". There is now: **a third of the land is sand**, it lies across the diagonal a driver
cutting the corner from a bunker to a crossing would take, and the roads are the way round it.

### Decisions made, and why

**Grass stays the interior, and the island gets real sand rather than becoming one.** Confirmed
with the user, four phases after the question was first asked and deferred. The reference shot
is a sand island with grass over it, and this is the one place the homage is knowingly not
followed: sand is `Grip 0.80`, so a sand interior would take a fifth off the jeep's speed over
most of the map — a whole-game slowdown arriving through a repaint, and one that would want the
grip column re-argued from scratch afterwards. Grass keeps 1.00 and keeps the bulk. Sand went
from 13% of the land to 34%, and it is placed rather than scattered.

**The island is drawn as sand, and the grass is painted inside it. Not the other way round.**
This is the least obvious decision in the phase and everything else rests on it. The obvious
construction is a grass island with sand painted along the channel — and it cannot be built,
because **a natural shape that sticks out past the others is the coastline**. A sand apron wide
enough to be worth driving on has to reach the water, and any ellipse that reaches the water
past the grass moves the coast to wherever that ellipse's edge happens to be. Fitting one inside
the other is worse than fiddly: a shallower ellipse of the same width always has a flatter top
than a deeper one, so an apron-shaped ellipse inscribed in an island-shaped one pokes out of it
by construction.

Drawn the other way it is one sentence with no arithmetic in it: the sand shapes are the island,
the grass is a smaller ellipse offset inland, and **the beach is whatever the grass does not
cover**. No sand shape can move a coast, because the sand *is* the coast. The derived four-metre
rim still runs underneath all of it, which is what keeps the promise on the stretches of shore
no apron was drawn on.

**The shores are ellipses, so the crossings grew to meet them.** An ellipse's edge curves away
from the channel at the flanks, which is what makes it an island instead of a quay. The
crossings had to reach the new banks: each causeway went from z ±13 to z ±15, and each
bridgehead from z −13 to z −20. **The narrows did not move.** The water each bridge spans is
still the 13 m between the two bridgeheads — the number `NoJeepCanJumpTheChannel` computes a
jeep's ballistic jump against — and the map measures the same 14.25 m of channel at each bridge
that it did before the repaint.

**Headlands, because a causeway should not be sixty metres of mole.** With one ellipse per shore
the west causeway's landfall would have been at z −32, so it would have crossed 63 m of water: a
long, exposed, slow flank route, which is a balance change nobody asked for. A second ellipse
per flank pushes land out to z −11, so each causeway crosses about 22 m and reads as an isthmus
between two promontories. It also gives the permanent crossings a piece of ground that looks
like a reason somebody built there.

**Each side gets a road to one causeway, not both, and that is what saved the picture.** The
first four road networks all failed the same way, and the failure is the most transferable thing
in this phase: **roading every crossing from both bunkers draws a closed rectangle around the
channel.** Two bunkers in the middle, four crossings at the edges and axis-aligned geometry give
two long horizontals and four verticals, and the causeways close the loop. Rendered, it is not a
map, it is a racetrack — and a tarmac ring round the channel is a real balance change as well as
an ugly picture. Moving the trunk south, narrowing the roads from 6 m to 5 m and shortening the
spurs each made it a *tidier* racetrack.

What broke it was giving green a road out to the **west** causeway and brown a road out to the
**east** one. That is exactly 180°-symmetric, so neither side is favoured; each side has one
roaded flank and one rough one, and they are opposite, which quietly discourages both sides
committing to the same crossing. And there is no loop, because there is no second vertical.

**The road cannot pay for a right angle, and this map says so out loud.** Asphalt at 1.06 buys
6% per metre. An axis-aligned road from a bunker to a bridge is 75 m where the straight line is
64 m, which is 17%. So **the road is never the shortest route to a crossing on this map**, and
no plausible amount of sand changes that — paving the whole diagonal in sand would be needed to
make the road win on time, and that is a sand island by the back door. What the sand buys
instead is that the two are within a few per cent on time while the road is clearly cheaper on
fuel. That is the choice, and it is a better one than a road that simply wins.

**Trees went from 16 to 36, and every prop kept its twin.** The reference shot's island is
covered in palms and ours had sixteen. Everything is still placed in pairs rotated 180° about
the origin, which `EveryPropOnTheMapHasAMirrorImage` checks; the throwaway generator that wrote
this file writes the green half and turns it, so a pair cannot be half-written.

### The numbers, measured

The map, before and after. "Before" is the map Phase D left, measured the same way.

| | Phase D | Phase E |
|---|---|---|
| Pieces of land in the file | 8 | 40 |
| Props | 28 | 50 |
| Land | 26,920 m² | 24,122 m² |
| Grass | 82% | **52%** |
| Sand | 13% | **34%** |
| Asphalt | 3% | **14%** |
| Narrowest channel | 14.25 m | **14.25 m** |
| Widest channel | 85.25 m | 53.25 m |
| `Sandbox.unity` | 2.7 MB | 3.7 MB |

The narrowest channel is the row that matters: it is the same to the centimetre, on a map whose
every coastline moved, because the four crossings are built and a built edge is kept exactly as
written.

**Soft ground on a run at a crossing**, measured through the field along the straight line from
a bunker to the middle of each crossing — which is what
`EveryStraightRunAtACrossingCrossesSoftGround` now holds the map to:

| From the green bunker to | Straight line | Of which soft |
|---|---|---|
| the west bridge | 76 m | 14.3 m |
| the east bridge | 76 m | 16.6 m |
| the west causeway | 94 m | 29.7 m |
| the east causeway | 94 m | 28.8 m |

**The choice, as arithmetic on the surface table** — bunker to the west bridge, road against the
straight line, distance over top speed:

| | Length | Time | Fuel |
|---|---|---|---|
| By road | 75 m | 3.22 s | 3.06 |
| Straight across | 64 m | 3.11 s | 3.24 |

The road costs **3.5% more time and saves 5.6% of the fuel**. Read those two with the caveat
they deserve, because it is the one number in this pass that is not off a render or a rigidbody:
this is exact arithmetic on `Grip` and `FuelDraw` rather than a driven run, and it ignores both
acceleration and the road route's two right-angle turns. Both of those count against the road,
so the real gap on time is wider than 3.5% and the fuel saving is the honest half of the trade.
Driving it is the measurement that would settle it, and nobody has.

### Gotchas, in the order they will bite

- **A sand shape that reaches the water *is* the coastline.** Written above as a decision and
  repeated here because it is what anybody repainting this map will trip over first. Every
  natural piece contributes to the outline, so painting a colour and drawing a shape are the
  same act. If you want a surface that does not move a coast, keep it strictly inside another
  natural shape — or, better, make it the outline and paint over it.

- **The channel measures 14.25 m and the file says 13.** Not new, and it still catches you: the
  bridgeheads are drawn to z ±6.5, `ChannelAt` walks in quarter-metre steps, `IsLand` answers per
  one-metre cell, and the two round outwards. 14.25 m is what this map has always measured. Do
  not "fix" the file to make the number come out at 13.

- **`OpenCountryNear` needs grass, with grass ten metres north of it, at each causeway's x.** A
  play-mode test drops a jeep in open country beside each permanent crossing and asserts it is
  standing on `Grass`. A repaint that put sand or road down the whole of x ±62 fails it from
  inside `LevelLoadingTests`, with a message about open country, several hundred lines from
  anything that mentions the map. Both flanks currently answer at z −54 and z −37.

- **`SurfaceField.Describes` is O(pieces) and runs on every single `IsOnLand`.** It walks every
  piece of land and calls `Enum.TryParse` twice per piece to read `Ground` and `Form`, so five
  times the pieces is five times that work — and `LevelDesignTests` asks `IsOnLand` tens of
  thousands of times sweeping the channel. It was the one thing about a forty-piece map that
  looked likely to hurt, and it did not: the edit-mode suite came in at 31.2, 32.1, 31.7 and
  31.8 seconds across the runs this phase took, with no trend. No before-and-after was
  measured against the eight-piece map, so that is an observation rather than a comparison —
  and it is still the first place to look if a future map doubles the count again.

- **Two right angles are worth more than a grip figure.** The general form of the road finding: a
  right-angled road needs the ground it avoids to be about a tenth softer *over the whole route*
  before it wins on time, and a road that turns twice needs more than that. A future map that
  wants a road which is genuinely the fast way somewhere has to run roughly straight at it —
  which, in an axis-aligned format, means putting the thing it leads to on an axis.

- **A prop standing in the road looks like a mistake and nothing catches it.** `LevelValidation`
  checks that a prop is on land with 2.5 m to spare and says nothing about what it is standing
  on. Two trees came out in the middle of the depot road and were found by looking at a picture.
  If props ever grow another rule, "not on a built surface" is the one to write.

- **Forty pieces are kept in step by hand now.** The file was written by a throwaway
  generator that draws the green half and turns it half a turn, and that generator is gone with
  the rest of the scratch work. Editing this map means editing it twice.
  `EveryPropOnTheMapHasAMirrorImage` has caught a prop without a twin since M7;
  `TheGroundIsTheSameMapTurnedHalfATurn` is new and catches a *piece of land* without one, which
  nothing did before — fine while the map was two rectangles and a glance settled it, not fine
  at forty shapes, where one side's approach to a bridge quietly being grass where the other's
  is sand is a fifth of a jeep's speed and nothing else would notice.

- **The scenes grew by a third and their diff is meaningless.** `Sandbox.unity` went 2.7 → 3.7 MB
  and `LevelEditor.unity` to 4.0 MB, because the sand sheet now has a long inland boundary as
  well as a coast. Phase C's "watch this number" still applies. As before, rebuilding a generated
  scene renumbers every local `fileID` in it, so most of the diff is noise.

- **Rebuild both scenes and re-run both suites after *any* edit to the level file.**
  `SandboxWiringTests` compares the bake against what the loader builds, so a repaint with no
  rebuild fails with "the baked map has the wrong number of destructibles" — a true statement
  about a stale scene that reads like a broken map.

- **Unity in batch mode is still a GUI process**, `-projectPath` still resolves against the
  shell's working directory, and a failed `-executeMethod` still exits 1 while `Start-Process`
  still looks like it worked. Phase B's gotcha, unchanged. Pass every path absolutely and read
  `ExitCode`.

### How this map was drawn

Worth recording, because the next repaint will want it and because it is the only reason this
phase converged: **a Python transcription of `SurfaceField`, `SurfaceNoise` and
`LevelLand.Signed` was written first**, and every version of the map was rasterised, checked
against the rules in `LevelValidation` and `LevelDesignTests`, and rendered to a PNG in the real
measured palette — about three seconds a go, against roughly ten minutes for a Unity round trip.
Seven road networks and half a dozen paintings of the ground were drawn that way and all but one
of each thrown away, several of them only revealing themselves as wrong once rendered.

Two things about it are worth knowing. First, it was run against the map that already existed
before it was trusted, and it reproduced Phase C's numbers exactly — including "the tightest
thing on the map is a tree drawn 3 m inland which the realised coast leaves standing 3.5 m from
the water", a sentence written in these notes three phases ago. Second, **it was deleted**, like
Phase C's coast-shot script, and on purpose: a second copy of the field rules is a second set of
rules that can stop agreeing with the first, which is what this whole pass has argued against
since `RimWidth` replaced two rules with one column. Every number written down here was decided
by Unity.

### What is left

The surfaces pass is finished. What it leaves behind:

- **`Ellipse` is finally used** — twenty-four of the forty pieces are ellipses — and the format did
  not have to change to allow any of this. `SchemaVersion` stayed at 2.
- **A road that is a choice rather than scenery**, and a measured statement of what the choice
  costs. Nobody has played a match on it.
- **The hook M8 wanted**: `GroundVehicle.Standing` and `GroundVehicle.Underfoot` now sit over a
  map with three drivable surfaces in real quantities, so wheel dust, debris colour and
  per-surface engine audio have something to tell apart.
- **A level editor that can paint all of this and has never been pointed at it.** The editor
  picks against the definition rather than the geometry, so forty pieces cost it nothing — but
  nobody has dragged one of these ellipses yet.

### File map

| Path | Change |
|---|---|
| `unity/Assets/StreamingAssets/Levels/iron-channel.json` | **Rewritten.** Forty pieces of land, fifty props, a road network, a new description. Every bunker, tower, depot and bridge at the coordinate it already had |
| `unity/Assets/RF/Scenes/Sandbox.unity`, `LevelEditor.unity` | Rebuilt, and half again the size |
| `unity/Assets/RF/Tests/EditMode/LevelDesignTests.cs` | **Two new tests**: every straight run at a crossing crosses soft ground, and the ground is the same map turned half a turn. Plus `Crossings`, `SoftGroundBetween` and a `HasMirror` for land |
| `unity/Assets/RF/Tests/EditMode/SurfaceFieldTests.cs` | `Built` finds a crossing rather than the widest built shape, and its width is measured only where there is water either side; the derived-coast test takes the painting away rather than forbidding it |
| `return-fire-homage-asset-spec.md` | Sand is derived *and* painted now, and roughly what a model stands on |
| `README.md`, `SURFACES_PLAN.md` *(now the appendix above)* | Status |
| `surfaces-e-map.png` | **The map.** How this phase is judged |
| `surfaces-e-sandbox.png` | The same raid again, for the record |

Not changed, and worth noticing: **every line of the runtime**. `SurfaceKind`, `SurfaceTuning`,
`SurfaceField`, `SurfaceMesh`, `SurfaceNoise`, `LevelLand`, `LevelBuilder`, `LevelValidation`,
`GroundVehicleMotion`, `VehicleTuning`, `VehicleSupply`, every material and the whole level
editor are untouched. Phase E is a level file and two test files — which is the strongest thing
the first four phases could have left behind, and it is why the plan put the repaint last.

### Verified

- **467 tests pass**: 343 edit-mode, 124 play-mode. Two are new —
  `EveryStraightRunAtACrossingCrossesSoftGround`, which holds the map to having soft ground on
  the way to every crossing, and `TheGroundIsTheSameMapTurnedHalfATurn`, which holds the land
  to the symmetry the props have been held to since M7. Two existing ones were rewritten in
  `SurfaceFieldTests`, both for the same reason Phase A's redraw and Phase C's cut forced
  rewrites: they described the map that used to be there rather than stating a rule. Nothing
  in `LevelDesignTests`' existing rules, in `LevelValidationTests`, in `SurfaceTests`, in
  `SurfaceMeshTests` or in any play-mode file had to change to accommodate a completely
  redrawn map, which is the strongest thing that can be said about the four phases underneath
  this one.
- **The two rewritten tests, and why.** `ANaturalCoastWandersAndABuiltOneDoesNot` measured the
  widest built shape's width by counting land cells across it — which works only when the
  shape is surrounded by water, and the widest built shape is now a road through a field. It
  finds the widest *crossing* now, and measures it only where there is water either side.
  `TheShippedMapHasACoastNobodyDrew` asserted that the shipped level file never paints a rim
  surface by hand; this map paints sand on purpose. It now proves the same claim by taking the
  painting away — repaint every natural shape with the fallback surface, leave the built ones
  so the outline does not move, and the beach and the shelf have to come back on their own.
- **Both scene builds and both stills run clean**, with no warnings and no exceptions during
  generation, and the catalog reports no problems. No catalog rebuild was needed: no colour,
  material or table row changed in this phase.
- **The map validates with nothing to say.** `LevelPreview` logs every
  `LevelValidation.Problems` entry as a warning before it renders, and the render log has none
  — on a map with forty pieces of land, fifty props and every coastline moved.
- **`surfaces-e-sandbox.png` is the argument the map shot cannot make.** The overview is
  busier than the reference shot, and honestly so: the reference has one road and this map has
  a network, because the brief was a network. From thirty-four metres up, which is the only
  altitude anybody plays at, that network is a lane through a dune field with a depot on it —
  and that still is the phase's real answer to "does it look like a place".

**Not verified: whether the road is worth taking.** The soft-ground figures are measured from
the field and held by a test; the time and fuel figures are arithmetic on the table. Nobody has
driven a jeep from a bunker to a crossing both ways and timed it, and that is the measurement
that would settle whether 1.06 and 0.80 are right — which is the question Phase D handed
forward and this phase has now given a map to answer it on.

**Not verified: whether the flanks are still even.** Each side has a road to its own causeway
and none to the other, which is exactly 180°-symmetric and therefore fair by construction. It
is also the first thing on this map that is not the same on both flanks, and a difference that
is fair on paper can still be a difference somebody learns to exploit.

---

## Addendum — an adversarial review, before committing

### What this is

Before this pass was committed, every changed and new file went through a max-effort
adversarial review: ten independent finder angles (line-by-line, removed-behaviour, cross-file
tracing, C#/Unity pitfalls, cache/wrapper correctness, reuse, simplification, efficiency,
altitude, and this project's CLAUDE.md conventions), each candidate checked by a second,
skeptical pass reading the live source, plus a gap sweep once the first round was in. Fifteen
findings survived verification. Eight were fixed here; seven are recorded below rather than
fixed, because fixing them safely would have meant refactoring the pass's core geometry code
(`SurfaceField`, `SurfaceMesh`) with no still to eyeball the result against, or changing a
public method signature that edit-mode tests already call directly.

### Fixed

**The level editor's "Mirror to the other side" silently dropped a piece of land's Surface and
Shape.** `LevelEdits.Mirror()`'s `EditTarget.Land` case rebuilt the mirrored piece through
`AddLand()`, which only ever carries `Name`/`MinX`/`MaxX`/`MinZ`/`MaxZ` - the two fields this
pass added were never in it, so mirroring a piece of Sand or an Ellipse handed back Grass and a
Rectangle instead. The adjacent `EditTarget.Structure` case in the same method already does the
right thing - place, then copy the extra fields onto the placed copy - and `Mirror` now does
that for land too. This is the one finding in this review with a real player-facing
consequence: every one of Phase E's forty pieces of land is exactly the kind of thing this
silently broke, and `TheGroundIsTheSameMapTurnedHalfATurn` would have caught it the next time
anybody dragged a repaint through the editor rather than writing JSON by hand - which is the
whole reason M8 exists.

**`SurfaceField.Describes()` compared the level's seed as a `float`.** The seed was packed into
the same flattened `float[]` as every rectangle's coordinates for the staleness check, and a
`float` only represents an `int` exactly up to 2^24 - past that, two different seeds can round
to the same float and the field would wrongly report itself as still describing the map.
Nothing on this map is anywhere near that (the shipped seed is 1995), so this could not have
bitten yet. It is now a plain `int seed` field, compared exactly, instead of a value that has
to survive a round trip through a `float[]` first.

**`GroundVehicle.ReadTheGround()` carried a null check that could never fire.** `underfoot` is
only ever assigned from `SurfaceTuning.For(...)`, which - by its own contract and every branch
of its switch - never returns null. The `&& underfoot != null` half of the early-return
condition was always true and invited a reader to wonder about a state the class's own contract
already rules out. Removed.

**`SurfaceNoise.Ease()` hand-rolled the exact cubic `Mathf.SmoothStep(0, 1, t)` already
computes**, two methods away from a call site (`Weight()`) that calls the real thing. It now
calls it too, so there is one easing curve in the file instead of two that happen to agree.

**A surface's bank was drawn two to four centimetres below its own sheet.** `BuildLand` lifts
each surface's sheet by `rank * CoastLift` so the stack does not z-fight, but built that same
surface's bank - the vertical drop to the water - at a hardcoded `y = 0`. Every permanent
crossing on the map is Asphalt, rank 2, so every bridgehead and causeway had an unmodelled 4 cm
seam between its road and its own retaining wall. The bank now lifts with its sheet. Both baked
scenes were rebuilt to carry it (`Sandbox.unity`, `LevelEditor.unity`) - the diff for that is
the usual renumbered-`fileID` noise Phase A's notes already flagged, not a review finding.

**`LevelLoadingTests.CrossingsOverTheChannel()` was the one "find the permanent crossings"
helper in the codebase without a `NaturalEdge` check.** `LevelDesignTests.Crossings()` and
`SurfaceFieldTests.Built()` - written in the same phase - both require
`!SurfaceTuning.For(piece.Ground).NaturalEdge` before counting a piece as a crossing; this one
only checked that it spans the centre line. Harmless today, because no natural shape happens to
cross z = 0, but it meant the two tests built on it were not actually testing what their names
claim. Brought into line with its siblings.

**`SurfaceTuning.Rims()`'s doc comment claimed a caller that does not exist.** It said "the
builder that lays them over the map... walks this"; the builder does not, and cannot the same
way - `Rim()` needs one surface's own row per cell, not the distinct set of everything something
rims into, which is what `Rims()` actually answers. Only the test suite calls it. Corrected the
comment instead of forcing a fit that was never there.

**`LevelCatalog.MaterialFor`/`BankFor` were two copies of the same six-line lookup loop**, new
in this pass, sitting next to a third, pre-existing copy in `PrefabFor`. Factored the two new
ones through a private `RowFor`; left `PrefabFor` alone - it is a different row type and out of
this pass's scope.

### Left as found, and why

**`SurfaceField.Build`, `.Cut` and `.Layer` each write out the same "partition, run `Spread` on
both halves, turn the result into a signed metre distance" block by hand.** Real duplication in
the pass's most load-bearing file. Not touched here: unifying it means changing the one file
every coastline, margin and validation rule in this pass reads, with no still or render to
catch a subtle sign error by eye the way every other change in this pass was checked. Worth
doing with a render in hand, not blind.

**`SurfaceMesh.Build()` and `.Bank()` share their scratch-array setup and the marching-squares
double loop that drives `Trace()`, then diverge.** Same reasoning as above, plus the review's
own finding hedges it: unifying the two loop bodies behind one driver trades the duplication for
a delegate call inside a per-cell hot path, which is not obviously a win.

**`GeneratedMaterials.MaterialSet()` builds the surface list and the bank list as two
near-identical `foreach` loops over `SurfaceTuning.Roster()`/`.Stack(false)`.** Editor-only,
runs once per **Build Level Catalog**, and the two loops read differently enough (one names
`SurfaceMaterial`, the other `BankMaterial`; one reads `.Colour`, the other `.Bank`) that
collapsing them would trade two short loops for one with a branch in it. Left as two.

**`GroundVehicleMotion.Traction(tuning, surface)` is computed once in `StepSpeed` and once more
in `StepYaw`, from the same two arguments, every fixed step.** A real, small, redundant
computation - and fixing it means `Step` computing it once and handing the float to both, which
changes `StepSpeed`/`StepYaw`'s signatures. Phase D's edit-mode tests call both directly with a
surface argument to check traction in isolation; changing the signature to take a precomputed
float instead breaks all of them to save a duplicated `Lerp`/`Clamp01` a tick. Not worth it at
this vehicle count.

**Four edit-mode test files each open the shipped level the same way.**
`LevelDesignTests.ReadTheLevel()`, `SurfaceFieldTests.TheShippedMap()`,
`SurfaceMeshTests.TheShippedMap()` and an inline copy in `SurfaceTests.cs` all run the same
`ShippedPathFor` / `File.Exists` / `LevelFile.TryRead` three lines. `SurfaceFieldTests.cs` and
`SurfaceMeshTests.cs` even name each other in their own class remarks, so this was not missed,
just not factored. Test-only, no behaviour at stake; left for whoever next touches all four.

**`SurfaceFieldTests.Island()` and `SurfaceMeshTests.Island()` build the same fixture with
different seeds** (unset, so 0, in one file; `4` in the other), and neither file's assertions
currently depend on the value, so the drift is inert rather than wrong. Recorded so it is not
mistaken for deliberate the next time someone ports a coordinate between the two files.

**`GroundVehicle.ReadTheGround()` reads `level.Field` every fixed step, and the `Field` getter
re-runs `SurfaceField.Describes()` - an O(land-piece) walk with two `Enum.TryParse` calls per
piece - on every access, whether the kind under the vehicle changed or not.** Phase D's notes
say the *row* is only looked up when the kind changes; that caching is real, but it covers
`SurfaceTuning.For`, not the `Field.At` call that decides whether the kind changed in the first
place, which pays the full `Describes()` cost regardless. Unmeasured at today's vehicle count,
and the Phase E gotchas already flag `Describes()` as "the first place to look if a future map
doubles the count again" - this is the same place, from a different caller. A cheap fix exists
(a version counter on `LevelDefinition`, bumped only where `Land` is actually mutated, checked
in O(1) instead of walked) but it touches every land-mutating call site in the level editor to
be correct, which is more than this review should do unsupervised.

### File map

| Path | Change |
|---|---|
| `unity/Assets/RF/Scripts/Editing/LevelEdits.cs` | `Mirror()`'s Land case now copies `Surface`/`Shape` onto the mirrored piece |
| `unity/Assets/RF/Scripts/Levels/SurfaceField.cs` | `Describes()` compares the seed as a true `int` field instead of through the packed `float[]` |
| `unity/Assets/RF/Scripts/Vehicles/GroundVehicle.cs` | Removed an unreachable `underfoot != null` check |
| `unity/Assets/RF/Scripts/Levels/SurfaceNoise.cs` | `Ease()` calls `Mathf.SmoothStep` instead of hand-rolling it |
| `unity/Assets/RF/Scripts/Levels/LevelBuilder.cs` | A surface's bank now lifts with its own sheet's `CoastLift` rank |
| `unity/Assets/RF/Tests/PlayMode/LevelLoadingTests.cs` | `CrossingsOverTheChannel` gained the `NaturalEdge` check its siblings already had |
| `unity/Assets/RF/Scripts/Levels/SurfaceTuning.cs` | `Rims()`'s doc comment no longer claims a production caller it doesn't have |
| `unity/Assets/RF/Scripts/Levels/LevelCatalog.cs` | `MaterialFor`/`BankFor` factored through a shared private `RowFor` |
| `unity/Assets/RF/Scenes/Sandbox.unity`, `LevelEditor.unity` | Rebuilt to carry the bank-height fix |
| `SURFACES_NOTES.md` | This addendum |

### Verified

- **467 tests pass**: 343 edit-mode, 124 play-mode - the same split Phase E left, unchanged by
  any of the eight fixes above. None of them changed a table row, a tested return value, or a
  test's own assertions - `CrossingsOverTheChannel`'s new filter agrees with the shipped map's
  existing two causeways, the same two `SurfaceFieldTests.Built()` already finds.
- **Both scene builds run clean**, no warnings, no exceptions, and both baked scenes
  (`Sandbox.unity`, `LevelEditor.unity`) were rebuilt to carry the bank-height fix before the
  suites above were run against them.
- **Fifteen findings, eight fixed, seven recorded above** rather than silently dropped - the
  same "not verified" discipline the rest of this document uses for a result that was measured
  rather than assumed, applied here to a review instead of a render.

---

## Appendix: the original plan, as written

What follows is `SURFACES_PLAN.md` exactly as it was written, before Phase A started, folded
into this file rather than kept as a separate one so the plan and the outcome can be read
against each other in one document.

### Plan — surfaces, and coastlines that look like coastlines

**Status: finished. All five phases shipped.** What actually landed, the decisions taken along
the way and the gotchas found are in [SURFACES_NOTES.md](SURFACES_NOTES.md); this file is left
as written, so the plan and the outcome can be read against each other. This is a
plan for a pass over the map's *ground*: what
it is made of, what that does to a vehicle driving on it, and what the edge of an island
looks like. It changes the level format, the level builder, the ground-vehicle motion model
and the generated materials. It changes no balance number that is not new, and it moves no
bunker, tower or depot.

**Read [M7_NOTES.md](M7_NOTES.md) first.** This assumes the level format, `LevelBuilder`,
`LevelValidation` and `WaterLine` exactly as M7 left them, and it inherits M7's two
load-bearing rules: **land is always at y = 0**, and **the map is described by its land**.
Neither is challenged here.

---

#### What the original does

The reference shot is a top-down island, and almost none of what makes it read as an island
is geometry:

- **Four ground surfaces, not one.** Sand is the bulk of the island; grass sits in irregular
  patches over it; asphalt runs as a narrow road from the base out to a walled compound; bare
  dirt scuffs show through the sand in soft blotches.
- **Two waters.** A pale band hugs every coastline — a shelf a couple of vehicle-lengths
  wide — and the open sea beyond it is a distinctly darker blue. The shelf is what makes the
  island sit *in* the water rather than on top of it.
- **Nothing is axis-aligned.** The coast wanders. The sand-to-grass boundary wanders. The
  only straight lines in the entire picture are the road, the compound walls and the bridge —
  the things a person built.
- **The road is the exception that proves it.** It is dead straight, hard-edged, right-angled,
  and it reads as man-made precisely *because* everything around it does not.

Two islands, one road, one compound, and it reads as a place. There is no height in it
either — it is flat, exactly as ours is.

#### What we have

`m7-map.png` is two grey rectangles in a dark blue square. The ground is one material,
`RF_Ground`, at a flat (78, 79, 75); the sea is one material, `RF_Water`, at (33, 39, 50).
Every coastline is a right angle because every piece of land is an axis-aligned rectangle
drawn as a `GameObject.CreatePrimitive(Cube)`, and the map is seven of them. The causeway —
the single most important piece of ground on the map — is the same grey as the open country
either side of it, so the map's one deliberate corridor is invisible.

The result is exactly what you said: a harbour. Two quays, a basin, and a couple of jetties.

Nothing in the game knows what it is driving on, either. `GroundVehicle.FixedUpdate` hands
`GroundVehicleMotion.Step` a `VehicleTuning` and a delta time and nothing else, so a jeep on
open sand and a jeep on a road are the same jeep. The only thing the ground can do to a
vehicle today is drown it.

#### The proposal in one paragraph

Add a **surface**: a named row in one table that carries a colour, a smoothness, how much
grip it offers, how hard it is on fuel, whether it drowns you, and whether its edges are
natural or built. Let a level's land rectangles each name one. Derive the rest — the beach
that rims every island, the shelf that rims every beach — from distance to the coast, so
those are free to author and impossible to forget. Then stop drawing land as boxes: rasterise
the rectangles into a signed distance field, push the coastline around with **seeded** noise
everywhere the surface says its edges are natural, and generate one flat mesh per surface
with a skirt down to the sea. Roads, causeways and bridgeheads name a **built** surface and
keep their exact edges, which is what makes them read as built. Finally, let the surface a
vehicle is standing on scale its handling, weighted by a per-vehicle sensitivity — so sand
costs the jeep a fifth of its speed and costs the tank almost nothing, and the road that is
the fastest way home is also the most obvious place to be ambushed.

---

#### The shape of the data

Four pieces. The first is the one you asked for; the other three are what it needs to be
worth having.

##### 1. `SurfaceKind` + `SurfaceTuning` — what a surface *is*

An enum with an empty member, and a table with one row per member, in
`Scripts/Levels/SurfaceKind.cs` and `Scripts/Levels/SurfaceTuning.cs`:

```csharp
public enum SurfaceKind
{
    None = 0,
    Grass = 1,
    Sand = 2,
    Asphalt = 3,
    ShallowWater = 4,
    DeepWater = 5,
}
```

```csharp
public sealed class SurfaceTuning
{
    public Color Colour;            // what it is painted
    public float Smoothness;        // URP gloss; water stays matte for M7's reason
    public float Grip = 1.0f;       // multiplies top speed, acceleration and turn rate
    public float FuelDraw = 1.0f;   // multiplies the engine's demand
    public bool Drowns;             // whether a vehicle standing here is lost
    public bool NaturalEdge = true; // whether its coastline is displaced, or exact
    public float BeachWidth;        // metres of Sand it rims itself with, or zero

    public static SurfaceTuning For(SurfaceKind kind) { /* one case per member */ }
}
```

**This is a code table on purpose, not a ScriptableObject and not a second JSON file** — the
same shape and the same reason as `VehicleTuning.For`, `WeaponTuning.For` and
`StructureTuning.For`, all three of which already say it out loud: a handful of rows balanced
by being read against each other is something a diff can show and a handful of assets cannot.
Adding a surface is *one enum member and one case*, and everything downstream — the material,
the level-file name, the editor palette, the validation — falls out of the enum. That is the
"easy to add extra surfaces" you asked for, and it costs a recompile rather than an asset
import and a hand-wired reference.

The one thing that argues the other way is the in-game level editor, which would like to add a
surface without a recompile. It is not worth designing for yet, and the seam is cheap if it
ever is: `SurfaceTuning.For` is a single function to swap for a lookup into a loaded table.

**A starting table**, to be argued with:

| Surface | Colour | Grip | Fuel | Drowns | Natural edge | Beach |
|---|---|---|---|---|---|---|
| Grass | mid olive-green | 1.00 | 1.00 | no | yes | 4 m of sand |
| Sand | warm pale tan | 0.80 | 1.15 | no | yes | — |
| Asphalt | neutral mid grey | 1.06 | 0.95 | no | **no** | — |
| ShallowWater | desaturated mid blue | — | — | yes (for now) | yes | — |
| DeepWater | M7's (33, 39, 50) | — | — | yes | — | — |

Asphalt above 1.0 is deliberate and is the whole point of having roads: the fastest line
across the map should be a line somebody drew, so that both players know where it is.

##### 2. `LevelLand.Surface` — where each surface *is*

One new field on the rectangle, carried as a name exactly the way `LevelStructure.Kind`
already is, and resolved through `LevelNames` (which already rejects digits, for the reason
M7 records):

```json
{ "Name": "The causeway", "Surface": "Asphalt",
  "MinX": -8.0, "MaxX": 8.0, "MinZ": -13.0, "MaxZ": 13.0 }
```

A level says *where*; the table says *what it does*. That is the same split M7 drew between
`LevelStructure` and `StructureTuning` — a level places a building, it does not decide how
tough a building is — and it is what keeps two maps from being two games. A missing or
unrecognised name resolves to `Grass`, so every rectangle already in `iron-channel.json` keeps
working untouched.

**Overlap is paint order.** Rectangles already overlap on purpose — M7's own comment is that a
headland is a second rectangle laid over the shore — so the last one in the file wins, and a
road is a thin rectangle laid over a shore.

##### 3. `SurfaceField` — the map, rasterised once

A grid over the level bounds, one byte per cell, built once from a `LevelDefinition`, and
deterministic:

```csharp
SurfaceField field = SurfaceField.Build(level);
SurfaceKind under = field.At(vehicle.transform.position);
bool dry = field.IsLand(at);
```

At 1 m cells a 240 m world is 240 × 240 = 57.6 KB. It is built by `LevelBuilder`, held on the
level root behind a static `Active` (the arrangement `WaterLine` already uses), and it is the
**single source of truth** for three questions that would otherwise be answered three
different ways: what does the ground look like here, what is the vehicle standing on, and is
this dry land. In particular `LevelDefinition.IsOnLand` and `LevelValidation`'s flood fill stop
asking the raw rectangles and start asking the field — otherwise a displaced coastline could
put a bunker in the sea that validated clean.

How a cell gets its surface:

1. **Built rectangles first, exactly.** Their signed distance is not displaced at all, so a
   16 m causeway is 16 m wide to the millimetre and a bridgehead is where the file says.
2. **Natural rectangles, displaced.** Take the union's signed distance, add
   `noise(x, z) * Amplitude`, and call it land where the result is positive.
3. **Beach, derived.** Land within `BeachWidth` of the realised coast becomes `Sand`, unless
   the rectangle underneath is a built surface — a road that runs out onto a jetty stays a road.
4. **Shelf, derived.** Water within `ShelfWidth` of the realised coast becomes `ShallowWater`;
   beyond it, `DeepWater`.

Steps 3 and 4 are why this is worth doing at all. They put a beach around every island and a
pale band around every beach *without anybody drawing one*, which is the single biggest
difference between the reference shot and ours, and the one a hand-authored map would get
wrong first.

**The noise must be a hash of the cell position and a per-level seed, never `Random`.**
`SandboxWiringTests` asserts that the baked copy of the map in `Sandbox.unity` matches what the
loader builds from the file, prop for prop. A coastline that came out differently each time
would break that, and would break it *intermittently*, which is worse. The seed is a new
`LevelDefinition.Seed` field, so two maps get two coastlines and one map gets the same one
forever.

##### 4. `LevelCatalog.MaterialFor(SurfaceKind)` — what it is painted with

`GeneratedMaterials` gains one material per surface, generated *from the table* rather than
listed by hand, and `LevelCatalog` gains a surface row next to the prefab rows it already
carries — because a built player has no asset database, which is the reason the catalog exists
at all. So adding a surface is: one enum member, one table row, **Build Level Catalog**.

The tidier-sounding alternative — one vertex-coloured mesh — does not work here. URP's Lit
shader ignores vertex colour; the imported models only carry it because glTFast's own shader
reads it; and this project has no custom shaders and should not gain its first one for this.

---

#### Decisions to make before any work starts

Recommendations given; each is a real choice.

**1. Does shallow water drown you?** Today all water does, and M7 argues that hard: drowning is
what the original did, it makes the amphibious jeep a real future upgrade rather than a nicety,
and it makes a blown bridge a kill zone as well as a severed route. A survivable shelf would
soften the "a mis-steer at 22 m/s costs a whole vehicle" that M7 itself flags as unverified.
*Recommendation: it drowns, for now.* Ship the shelf as pure appearance so this pass changes no
balance and breaks no test, and leave `Drowns` in the table so the experiment is one word in
one row later. Deciding it now, before anybody has played a match on the map, is deciding blind.

**2. Rectangles, or a new shape?** Displacing a coastline makes a rectangle's *edge* natural at
the metre scale. It does not make a 164 × 79 m rectangle stop being a rectangle at the
hundred-metre scale, and it would be dishonest to imply otherwise: at that scale the island's
outline is authored, and noise will not rescue it.
*Recommendation: add one shape, `Ellipse`, and keep the same four fields.* `MinX/MaxX/MinZ/MaxZ`
is already a bounding box, so an ellipse is the one inscribed in it and the format costs
exactly one optional `"Shape": "Ellipse"` name. Three or four overlapping ellipses plus a
displaced coast is an island. A polygon list would be more general, would be unreadable by hand
— which is the property M7 chose the whole format for — and would be the wrong thing to hand a
level editor that does not exist yet.

**3. Where does handling get modified?** `GroundVehicleMotion` is static, pure and edit-mode
tested with no scene and no rigidbody, and that is worth protecting.
*Recommendation: `Step` gains one `SurfaceTuning` argument, and `GroundVehicle.FixedUpdate`
samples `SurfaceField.Active` to supply it.* The maths stays pure and testable, the sampling is
a grid lookup rather than a raycast, and no vehicle needs a collider callback or a physics
material. The helicopter never calls it and never has to be excluded — the same free result
that already keeps it from drowning.

**4. How is per-vehicle difference expressed?** Either a full surface × vehicle matrix, or a
scalar on each.
*Recommendation: one `SurfaceSensitivity` column on `VehicleTuning`, and
`effective = Mathf.Lerp(1.0f, surface.Grip, tuning.SurfaceSensitivity)`.* Four numbers instead
of twenty, and it says something true: the wheeled jeep is at the mercy of what it is driving
on and the tracked tank is not. That is the same distinction `PivotTurn` already draws, and it
is the project's habit — the helicopter ignores terrain because of a rule about altitude, not
because of a check on its type. Suggested: Jeep 1.0, ASV 0.45, Tank 0.25, Helicopter 0.0.

**5. Does the schema version move?** Adding an optional field does not require it; `JsonUtility`
defaults it and old files read.
*Recommendation: bump `LevelDefinition.Schema` to 2.* A map authored against displaced
coastlines and a road network is genuinely mis-built by version-1 code — a bridgehead could
land in the sea — and `LevelFile` already refuses a file from a newer build with a sentence
naming it. That refusal is the whole point of having a version.

**6. One mesh with submeshes, or a stack of flat layers?**
*Recommendation: a stack.* Each surface is generated as its own flat mesh by marching squares
over its own field, drawn in a fixed order — deep water, shelf, sand, grass, asphalt — each a
couple of millimetres above the last. Boundaries are then automatic and exact, with no shared
edge to stitch and no seam for the sea to show through. The cost is a little overdraw on a
handful of flat meshes, which is nothing, and 2 mm does not disturb `CombatPlane`, which
resolves at y = 0. A single submeshed mesh is the tidier artefact and a great deal more code.

---

#### Phases

Ordered so each is worth doing alone, and so the first is a single evening with most of the
visible payoff. If only one thing gets built, build A.

##### Phase A — surfaces exist, and the map gets painted

No new geometry, no new shapes, no field. Rectangles stay boxes.

- `SurfaceKind`, `SurfaceTuning.For`, `LevelNames.ToSurface`.
- `LevelLand.Surface`, defaulting to `Grass`.
- `GeneratedMaterials` generates one material per surface from the table;
  `LevelCatalog.MaterialFor`; `LevelCatalogBuilder` wires them.
- `LevelBuilder.BuildLand` paints each slab with its surface's material instead of `Ground`.
- `iron-channel.json` names surfaces: the causeway and both bridgeheads become `Asphalt`, the
  two shores `Grass`.
- `RF_Ground` stops being used by the map. Leave the asset; other things reference it.

**Done when:** `Render Level Overview` shows a green island with a grey road down the middle of
it, and the roster of surfaces is one table anybody can add a row to.

##### Phase B — the field, the beach and the shelf

- `SurfaceField.Build` / `.At` / `.IsLand`, with cells, no displacement yet.
- Derived beach and shelf bands from distance to coast.
- The sea is drawn as two meshes now — shelf and deep — instead of one slab. It keeps its
  collider, and `WaterLine` is untouched.
- `LevelDefinition.IsOnLand` and `LevelValidation` start consulting the field.

**Done when:** every island in the preview has a sand rim and a pale shelf, and neither of them
was drawn by hand in the level file.

##### Phase C — natural coastlines

- `LevelDefinition.Seed`; hash-based value noise; displacement applied only where
  `NaturalEdge`.
- Marching squares over each surface's field into a flat mesh, plus the coastline skirt down to
  `LandThickness` below y = 0, in a darker bank colour.
- One `MeshCollider` on the land, non-convex and static; the sea slab keeps its own.
- `LevelLand.Shape`, with `Ellipse`.
- The three shape-sensitive validation rules get re-measured against the field: the bunker's
  10 m of dry land, the 13 m channel at each bridgehead, and props standing on land.

**Done when:** the preview's coast wanders, the causeway's edges are still dead straight and
still exactly 16 m apart, and `LevelDesignTests` still passes unmodified.

##### Phase D — handling

Independent of C, and can be done straight after B.

- `SurfaceTuning` argument on `GroundVehicleMotion.Step`; `VehicleTuning.SurfaceSensitivity`.
- `GroundVehicle.FixedUpdate` samples the field. `Helicopter` does not.
- `VehicleSupply` multiplies demand by the surface's `FuelDraw`.
- Edit-mode tests on the maths, play-mode tests on the vehicles.

**Done when:** a jeep crossing the causeway is measurably quicker than a jeep crossing the sand
beside it, and a tank barely notices the difference.

##### Phase E — the map, and the notes

- Repaint `iron-channel.json` as an island rather than a basin: ellipses for the two shores, a
  short road network joining each bunker to its depots and to the causeway, sand where the
  shores meet the channel.
- A new map shot and a new sandbox still, which is how this pass is judged.
- `SURFACES_NOTES.md`, with the decisions, the gotchas and the file map, per the project's
  standing rule for the end of a phase.

**Done when:** the map shot looks like a place.

---

#### What this deliberately does not include

- **Elevation.** M7's rule stands and this plan does not touch it: `CombatPlane` resolves every
  round on one plane and the camera looks down from a fixed height, so a hill would quietly make
  a tank shootable through a rise. The only relief on the map is still the 0.7 m bank.
- **Blended surface transitions.** The reference's dithered sand-to-grass edge wants a splat map
  or a custom shader. Hard boundaries suit a flat-shaded project, and this is not the change that
  should introduce the first `.shader` file into it.
- **The amphibious jeep.** Still a design-document stretch goal. The `Drowns` flag is where it
  would land, and that is as far as this goes toward it.
- **Painting surfaces in-game.** That is the level editor. This pass owes it a format it can
  paint into and a palette it can offer, and owes it nothing else.
- **Animated water.** Foam at the shoreline and a slow scroll on the sea are the cheapest juice
  in the whole project, and they belong in M8's polish pass.
- **Physics materials.** Motion is scripted rather than simulated, so grip is a number in a table
  and not a `PhysicMaterial` on a collider.

#### Risks and things already known to bite

- **The beach can undo M7's coastline contrast, and this is the biggest risk in the plan.** M7
  spent real effort establishing that value contrast, not hue, is what a player reads at speed,
  and killed the sea's specular to take the bank from invisible to twice the contrast. A pale
  sand band at the waterline puts the *lightest* thing on the map next to the darkest, which is
  fine — but a pale *shelf* between them is a mid value in that gap, which is exactly how the
  first sea went wrong. Keep the shelf clearly darker than the sand, keep the beach narrow, and
  measure it in a still before believing it.
- **Seeded noise, or the bake and the load disagree.** Anything drawing on `Random` makes
  `Sandbox.unity`'s baked map differ from the one `LevelLoader` builds on the first frame, and
  `SandboxWiringTests` compares them. Hash the cell coordinates and the level seed; nothing else.
- **Displacement must be bounded, and validation must know the bound.** Every margin in
  `LevelValidation` — `ShoreMargin` at 2.5 m, `BunkerShoreMargin` at 10 m — was measured against
  rectangles. An amplitude anywhere near 2.5 m eats the first one. Cap it (1.5 m is the
  suggestion) *and* have validation ask the field rather than the rectangles, so the cap is a
  design comfort rather than the thing correctness rests on.
- **The 13 m channel is load-bearing and would now be measured on a wavy line.** M7 has a test
  that computes the jeep's ballistic jump from the real top speed and the real bank height rather
  than asserting 13. It has to keep passing against the realised coast, which means the
  bridgeheads must be `Asphalt` — that is not a decoration, it is what keeps the narrows exact.
- **`MeshCollider` on generated geometry.** Non-convex, static, and exactly one: a collider per
  surface layer would leave vehicles resting on a 2 mm step.
- **`MarkStatic` only bites in the editor**, per `LevelBuilder`'s existing comment, so the bake is
  static and the runtime build is not. Nothing changes; it is just easy to trip over when
  profiling one and not the other.
- **`LevelCatalogBuilder.Load` does not refresh.** M7 records that this cost twenty minutes:
  change a generated colour and the next render still shows the old one. Five new surface
  materials multiply the number of times somebody will hit it. Run **Build Level Catalog**.
- **Trees and buildings may end up standing on a coastline that moved under them.** They were
  placed against rectangles. Validation checking props against the field will catch it; expect it
  to catch a few, and expect to move them in Phase E.
- **Cell size is a three-way trade** between memory, how fine a coastline can wiggle, and how long
  validation's flood fill takes. 1.0 m is the recommendation and it is a guess; the flood fill
  already walks at its own 2.0 m and does not have to change.

#### File map, if it goes ahead

| Path | Change |
|---|---|
| `unity/Assets/RF/Scripts/Levels/SurfaceKind.cs` | **New.** The enum, with its empty member |
| `unity/Assets/RF/Scripts/Levels/SurfaceTuning.cs` | **New.** The table — the data element you asked for |
| `unity/Assets/RF/Scripts/Levels/SurfaceField.cs` | **New.** The map rasterised; one source of truth |
| `unity/Assets/RF/Scripts/Levels/SurfaceMesh.cs` | **New.** Marching squares, and the coastline skirt |
| `unity/Assets/RF/Scripts/Levels/LevelLand.cs` | Gains `Surface` and `Shape` |
| `unity/Assets/RF/Scripts/Levels/LevelDefinition.cs` | Gains `Seed`; `IsOnLand` asks the field; schema 2 |
| `unity/Assets/RF/Scripts/Levels/LevelNames.cs` | Gains `ToSurface` and `ToShape` |
| `unity/Assets/RF/Scripts/Levels/LevelBuilder.cs` | Builds meshes instead of slabs. Still knows no coordinates |
| `unity/Assets/RF/Scripts/Levels/LevelCatalog.cs` | Gains `MaterialFor(SurfaceKind)` |
| `unity/Assets/RF/Scripts/Levels/LevelValidation.cs` | Measures against the field; new built-edge rules |
| `unity/Assets/RF/Scripts/Vehicles/GroundVehicleMotion.cs` | `Step` takes a surface |
| `unity/Assets/RF/Scripts/Vehicles/GroundVehicle.cs` | Samples the field each fixed step |
| `unity/Assets/RF/Scripts/Vehicles/VehicleTuning.cs` | Gains `SurfaceSensitivity`, four rows |
| `unity/Assets/RF/Scripts/Supply/VehicleSupply.cs` | Demand scaled by the surface |
| `unity/Assets/RF/Editor/ArtPipeline/GeneratedMaterials.cs` | Materials generated from the surface table |
| `unity/Assets/RF/Editor/Gameplay/LevelCatalogBuilder.cs` | Wires the surface materials |
| `unity/Assets/StreamingAssets/Levels/iron-channel.json` | Surfaces, ellipses, a road network, a seed |
| `unity/Assets/RF/Tests/EditMode/SurfaceTests.cs` | **New.** Table, names, determinism, bounded displacement |
| `unity/Assets/RF/Tests/EditMode/LevelDesignTests.cs` | Its shape rules re-measured against the field |
| `unity/Assets/RF/Tests/PlayMode/SurfaceDrivingTests.cs` | **New.** Jeep on sand vs road; tank unmoved |
| `return-fire-homage-asset-spec.md` | The palette gains the ground surfaces |
| `SURFACES_NOTES.md` | **New.** The written summary this phase ends with |

#### Open questions for you

1. **Is the island mostly sand or mostly grass?** The reference is a sand island with grass
   patches over it. Ours is a temperate-looking channel map, and green reads as "drivable open
   ground" more immediately than tan does. I would make grass the interior and sand the rim, but
   the reference argues the other way and it is your homage.
2. **How much of a road network does `iron-channel` get?** The causeway and the two bridgeheads
   are free and are already the right answer. A full network — bunker to each depot, depot to the
   causeway — is what makes a map *look* designed, and it is also a real balance change, because
   a road is a fast lane and a fast lane is an ambush.
3. **Should the shelf be survivable?** Decision 1 says no for now. If you would rather find out,
   it is one word in one row and about six play-mode tests, and it is the kind of thing worth
   knowing early rather than late.
4. **How wide is the beach?** Four metres is a guess — about a jeep and a half, wide enough to
   see from 34 m up and narrow enough not to be a lane you drive down. It is the number most
   likely to be wrong, and it is free to change.
5. **Does anything else want to know what it is standing on?** Debris colour on impact, wheel
   dust, and the sound a vehicle makes are all M8's, all three would read this same field, and
   none of them should be built here. Worth knowing they are coming.
