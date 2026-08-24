# Plan — surfaces, and coastlines that look like coastlines

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

## What the original does

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

## What we have

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

## The proposal in one paragraph

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

## The shape of the data

Four pieces. The first is the one you asked for; the other three are what it needs to be
worth having.

### 1. `SurfaceKind` + `SurfaceTuning` — what a surface *is*

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

### 2. `LevelLand.Surface` — where each surface *is*

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

### 3. `SurfaceField` — the map, rasterised once

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

### 4. `LevelCatalog.MaterialFor(SurfaceKind)` — what it is painted with

`GeneratedMaterials` gains one material per surface, generated *from the table* rather than
listed by hand, and `LevelCatalog` gains a surface row next to the prefab rows it already
carries — because a built player has no asset database, which is the reason the catalog exists
at all. So adding a surface is: one enum member, one table row, **Build Level Catalog**.

The tidier-sounding alternative — one vertex-coloured mesh — does not work here. URP's Lit
shader ignores vertex colour; the imported models only carry it because glTFast's own shader
reads it; and this project has no custom shaders and should not gain its first one for this.

---

## Decisions to make before any work starts

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

## Phases

Ordered so each is worth doing alone, and so the first is a single evening with most of the
visible payoff. If only one thing gets built, build A.

### Phase A — surfaces exist, and the map gets painted

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

### Phase B — the field, the beach and the shelf

- `SurfaceField.Build` / `.At` / `.IsLand`, with cells, no displacement yet.
- Derived beach and shelf bands from distance to coast.
- The sea is drawn as two meshes now — shelf and deep — instead of one slab. It keeps its
  collider, and `WaterLine` is untouched.
- `LevelDefinition.IsOnLand` and `LevelValidation` start consulting the field.

**Done when:** every island in the preview has a sand rim and a pale shelf, and neither of them
was drawn by hand in the level file.

### Phase C — natural coastlines

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

### Phase D — handling

Independent of C, and can be done straight after B.

- `SurfaceTuning` argument on `GroundVehicleMotion.Step`; `VehicleTuning.SurfaceSensitivity`.
- `GroundVehicle.FixedUpdate` samples the field. `Helicopter` does not.
- `VehicleSupply` multiplies demand by the surface's `FuelDraw`.
- Edit-mode tests on the maths, play-mode tests on the vehicles.

**Done when:** a jeep crossing the causeway is measurably quicker than a jeep crossing the sand
beside it, and a tank barely notices the difference.

### Phase E — the map, and the notes

- Repaint `iron-channel.json` as an island rather than a basin: ellipses for the two shores, a
  short road network joining each bunker to its depots and to the causeway, sand where the
  shores meet the channel.
- A new map shot and a new sandbox still, which is how this pass is judged.
- `SURFACES_NOTES.md`, with the decisions, the gotchas and the file map, per the project's
  standing rule for the end of a phase.

**Done when:** the map shot looks like a place.

---

## What this deliberately does not include

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

## Risks and things already known to bite

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

## File map, if it goes ahead

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

## Open questions for you

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
