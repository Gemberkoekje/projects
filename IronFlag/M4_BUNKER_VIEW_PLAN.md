# Plan — making the bunker read like the original

**Status: proposed, not started.** This is a plan for a follow-on pass over M4's
vehicle-select screen. M4 itself is finished and described in [M4_NOTES.md](M4_NOTES.md);
nothing here changes the rules it established, only what the player is looking at while those
rules run.

**Read [M4_NOTES.md](M4_NOTES.md) first.** This assumes the bunker flow, `VehicleBay`,
`TeamBunker` and `PlayerHud` as they stand.

---

## What the original does

The reference screenshot is one screen doing three things at once:

- **A cutaway of an underground base.** Brown earth in cross-section, sky and grass along the
  top edge, and the whole base seen from the side as if the near wall had been removed.
- **Four lit bays around a central elevator shaft**, two per level: helicopter and tank on the
  upper level, support vehicle and jeep on the lower one. Each bay is a dark box with the
  vehicle sitting in it, lit from inside, with small status lights on the frame. The shaft
  runs from the lower level up to a hazard-striped hatch at the surface.
- **A hardware console along the bottom third.** A screen with a vehicle icon and a count, a
  fuel figure, a drawn joystick, two big buttons (deploy, map) and a terrain panel either side.

The important part is not the pixel art. It is that **none of it is a menu**. You are looking
at your own base, the vehicles are really in it, and the thing you press is drawn as a button
on a console rather than as a highlighted row of text.

## What we have

M4 gives the player a translucent panel with four text rows, floating over a top-down view of
their bunker's roof. Everything it says is true and none of it is a place. The vehicles that
are not deployed are *hidden* — renderers and colliders switched off — so the panel is talking
about four things the player cannot see.

Our base is also genuinely a different building from the original's. The asset spec has it as
an above-ground concrete blockhouse, roughly 10 m square, with a lift bay opening at the front
and a helipad on the roof. That is not a hatch in the ground, and a straight copy of the
original's cutaway would contradict our own map.

## The proposal in one paragraph

Build the underground half of the bunker that our exterior already implies — a two-level hall
with four bays and a central shaft — put the stowed vehicles in it *visibly*, and point the
choosing player's camera at it from the side in cutaway. Turn M4's ride-out into a real
elevator carrying the vehicle up the shaft to the lift platform that is already there. Move
the roster panel from a floating list to a console strip along the bottom of the viewport, and
let the selection be shown by lighting the bay in the world rather than by a highlighted row.
Keep the helipad, because it is ours and it is better: the helicopter rides the same shaft and
steps off at the roof instead of at ground level.

---

## Decisions to make before any work starts

These are the ones that change what gets built. Recommendations are given; each is a real
choice.

**1. Where the bays are, given our exterior.** The original's shaft comes up through a hatch in
open ground. Ours comes up at the lift platform 5.2 m in front of the blockhouse — which is
already modelled, already where `TeamBunker.LiftPoint` is, and already what every M4 test
asserts against. *Recommendation: keep it.* The hall goes under the blockhouse and forward, and
the shaft rises to the existing platform. **No gameplay geometry moves and no M4 test changes.**

**2. Whether the helicopter has an underground bay.** In the original all four ride the same
elevator. Ours takes off from the roof.
*Recommendation: it has a bay underground and rides the same shaft, stepping off at the roof
pad.* One shaft, four bays, and the helipad keeps its job. The alternative — a fifth space at
roof level — costs an extra asset and loses the single-shaft read the screenshot is built on.

**3. The select camera's heading.** M2 fixed the battlefield camera's yaw for both players
forever, so the two halves of a split screen agree about which way north is. A cutaway wants to
look at the bays *from the front*, and the two bunkers face opposite ways.
*Recommendation: the select camera uses its own bunker's facing.* The fixed-yaw rule is about
the battlefield view; while you are choosing you are indoors, looking at your own building, and
there is no shared compass to protect. The alternative is a hall open on both long sides plus a
per-side bay-to-roster mapping so the order still reads left-to-right — twice the art and a
mapping nobody can see, to protect a rule that is not in play.

**4. Stowed vehicles stop being hidden.** This is what actually makes the screenshot read, and
it is a change to `VehicleBay`: a stowed vehicle is parked in its bay, visible, with its
colliders and movement model off. It is underground and out of every weapon's reach, so nothing
about combat changes — but this needs saying out loud, because "hidden" was M3's answer to
"cannot be shot" and it stops being the mechanism.

**5. What the console shows.** The original shows a vehicle icon and a count because it has a
fleet. We have one of each, so the count is meaningless; what we have instead is a state per
vehicle and two numbers per vehicle.
*Recommendation: the bays are the icons* — they are in the world, lit, with the vehicle in
them — and the console carries the name, the state, the fuel and rounds it will leave with, and
the deploy prompt. Optionally render four real icons with the tooling we already have (see
Phase D).

---

## Phases

Ordered so that each one is worth doing on its own, and so the first two together are the
smallest change that alters the feel. If only one thing gets built, build A and B.

### Phase A — the underground base, in Blender

Two new assets, both spec-conformant (primitives, flat vertex colour, `RF_<Category>_<Name>`):

**`RF_Structure_BunkerHall`** — the hall, in bunker-local Blender coordinates (Z up, −Y toward
the field, matching the existing `structure_bunker.py`):

| Part | Size (m) | At | Colour |
|---|---|---|---|
| Upper floor | 20.0 × 13.0 × 0.4 | (0, −1.5, −3.8) | `CONCRETE` |
| Lower floor | 20.0 × 13.0 × 0.4 | (0, −1.5, −7.8) | `CONCRETE` |
| Back wall | 20.0 × 0.5 × 7.6 | (0, +5.0, −4.0) | `CONCRETE` |
| End walls ×2 | 0.5 × 13.0 × 7.6 | (±10.0, −1.5, −4.0) | `CONCRETE` |
| Bay recesses ×4 | 7.0 × 5.5 × 3.0 | (±6.0, −5.2 / +1.0, −2.1 / −6.1) | `HULL_DARK` |
| Shaft walls ×2 | 0.4 × 3.2 × 8.2 | (±2.0, −5.2, −3.8) | `METAL_DARK` |
| Shaft guide rails ×2 | 0.2 × 0.2 × 8.0 | (±1.7, −5.2, −3.8) | `METAL` |
| Hazard chevrons | 3.4 × 0.3 × 0.1 | (0, −6.7, −0.05) | `WARNING` |

No near wall: the side facing the field is the cut plane, and it is simply not built. The four
bay recesses are open toward the shaft and toward the cut, which is what makes a vehicle in one
visible from the select camera.

Four empty marker children named `Bay0`..`Bay3`, each with its origin on the bay floor where a
vehicle stands — the same trick `LiftPlatform` and `Helipad` already use, so bay positions are
an art decision rather than four numbers in a C# file. Slot order is roster order: jeep, tank,
ASV, helicopter, reading left-to-right and top-to-bottom as the select camera sees them.

**`RF_Structure_BunkerLift`** — the elevator car: a 3.2 × 2.8 × 0.3 deck in `METAL_DARK` with a
`WARNING`-striped edge and two side rails, origin on the deck surface. It is a separate asset
because it moves; the hall never does.

Also: strip the `LiftPlatform` slab out of `structure_bunker.py` and let the elevator car
*be* the platform at its up position, so the surface has a hole with a car in it rather than a
slab with a car under it. `TeamBunker.LiftPoint` then reads the car's own marker and nothing
downstream notices.

**Done when:** `./blender/build.ps1 -Asset Bunker` builds three assets clean and the triangle
counts it prints are in the same order of magnitude as the existing structures.

### Phase B — the cutaway view

- `TeamBunker` gains the hall: it instantiates or references `RF_Structure_BunkerHall`, exposes
  `BayFor(int slot)` from the markers, and keeps `LiftPoint`/`HelipadPoint` exactly as they are.
- A new `BunkerView` component (Scripts/Core or Scripts/UI) owns the select framing: given a
  bunker, it produces the camera pose that frames the hall in cutaway — low pitch (8–14°), the
  bunker's own yaw, far enough back to hold four bays and the shaft.
  Static and side-effect free like `TopDownCameraRig.SolveFocus`, so it can be tested without a
  screen and reused by the still.
- `TopDownCameraRig` gains a second parked pose, or `Park` grows an overload taking a full
  pose. `PlayerVehicleDriver.ShowTheBunker` uses it instead of parking overhead.
- **Occlusion:** the hall is underground and the ground is a single-sided Unity `Plane`, so a
  camera below grade sees straight through it and the gameplay camera above never sees the
  hall's interior at all. The one case to handle is a player driving over their own base at a
  shallow angle; mitigate by enabling each hall only while its own player is choosing, which is
  one `SetActive` in `PlayerVehicleDriver`.
- **Lighting:** an underground hall gets nothing from the sun and very little from the trilight
  ambient. Add a `RF_BayLight` emissive material via `GeneratedMaterials` (the tracer and blast
  materials are the pattern) plus one small point light per bay, warm, short range. The lit-bay
  look in the screenshot is most of what sells it.

**Done when:** pressing Play drops both players into a lit cutaway of their own base with four
vehicles sitting in bays, and driving out and back never shows the hall from the battlefield.

### Phase C — the elevator

- `VehicleBay` drives the car instead of lerping a vehicle through empty ground: on deploy the
  car travels from the chosen bay's level to the surface with the vehicle standing on it, then
  the vehicle rolls off to `LiftPoint`. The ride is already 1.2 s and already kinematic; this
  is the same movement with something under it.
- Two legs rather than one, because the bays are off to the side: the vehicle crosses to the
  shaft, then rises. Keep it dumb — two straight lerps, no pathing.
- The helicopter's leg ends at the roof pad rather than at the platform, which is the one
  branch that already exists in `TeamBunker.DeployPointFor`.
- Stowing runs it backwards.
- **No physics hole is needed.** The ground plane stays solid, the shaft is scenery, and the
  vehicle is kinematic for the whole ride — which is why this is cheap.

**Done when:** the ride out is visibly an elevator, and `BunkerTests` still passes unchanged
apart from where it asserts the vehicle starts from.

### Phase D — the console

- Replace `PlayerHud`'s bunker panel with a strip along the bottom of the viewport in the same
  place the driving strip already sits, so the two panels stop moving around.
- Contents: the highlighted vehicle's name, its state (READY / REPAIRING 3.4), the fuel and
  rounds it will leave with, and the deploy prompt in the words of that player's device — which
  `PlayerHud.Prompt` already does.
- Selection moves into the world: the highlighted bay is lit brighter, or its frame takes the
  team accent, and the elevator sits at the highlighted bay's level. That is the original's
  trick and it is why its console needs so little text.
- The two big buttons on the original's console are worth copying as affordances: deploy, and a
  slot where M8's minimap goes.
- **Optional (D.2): real vehicle icons.** `ArtPreviewScene` and `CameraCapture` already render
  prefabs to PNG from the command line. A `Tools > IronFlag > Build Vehicle Icons` menu item
  writing four small transparent PNGs would give the console the original's icon strip for
  perhaps forty lines of editor code. Do it only if the bays turn out not to read at a glance.

**Done when:** the select screen has no floating panel on it, and a screenshot of it is
recognisably the same idea as the reference.

### Phase E — tests, still, notes

- Edit mode: the hall exports four bay markers; every bay is inside the hall and distinct; bay
  order matches roster order; the cutaway pose frames all four bays; the console fits the
  narrowest viewport a split screen produces.
- Play mode: a stowed vehicle is visible, in its bay, and cannot be hit; choosing shows the
  hall and not the battlefield; the elevator carries the vehicle from its bay to the lift point
  and the helicopter to the roof; the hall is not visible from the gameplay camera.
- Regenerate `m4-sandbox.png`, or add `m4-bunker.png` alongside it — the still is the only way
  to review any of this without opening Unity, and a HUD change that is not in a still is a
  change nobody can review.
- Fold the result into `M4_NOTES.md` rather than writing a fifth notes file: this is the same
  milestone, finished properly.

---

## What this deliberately does not include

- **The minimap.** The original's console has terrain panels either side. That is M8's minimap;
  leave the slot and nothing else.
- **Sound.** The lift, the doors and the console are exactly where M8's per-vehicle audio hook
  wants to be, and none of it belongs here.
- **Destruction states for the bunker.** The asset spec is explicit that the bunker has none —
  it is the win-condition target, not a combat prop.
- **A fleet.** The original's "3" is a count of remaining tanks. Attrition belongs with M6's
  win conditions, as [M4_NOTES.md](M4_NOTES.md) already records.
- **A separate select scene or render texture.** Everything in this project is one generated
  scene, and a base you are really standing in is the whole point of the change.

## Risks and things already known to bite

- **Lighting an interior.** The scene has one directional sun and a trilight ambient tuned for
  daylight. Bay lights are additive point lights in URP forward — cheap at eight of them, but
  the ambient ground colour will make the hall read flatter than the reference until the bay
  emissives carry it. Expect to spend time here; it is the difference between the screenshot
  and a dark box.
- **Mesh colliders on generated geometry.** `AddStaticColliders` puts a `MeshCollider` on every
  `MeshFilter` under a placed prop. An underground hall would get a dozen for no reason —
  nothing can reach it. Skip colliders for the hall explicitly rather than paying for them.
- **The cutaway is a modelling convention, not a shader.** Nothing culls the near wall at
  runtime; the wall simply is not built. That means the hall only ever reads from one side, and
  a future free-look camera would show it hollow.
- **Emission clips.** M3 learned that emission much above 4.0 goes to flat white with no colour
  in it. Bay lights want to be well under that.
- **The still has to be staged into the state it claims**, and `Start` does not run in a scene
  nobody is playing — `StageMatch` already had to stow both rosters by hand for exactly this
  reason.

## File map, if it goes ahead

| Path | Change |
|---|---|
| `blender/assets/structure_bunker.py` | Loses the lift slab; gains the shaft mouth |
| `blender/assets/structure_bunker_hall.py` | **New.** The two-level hall, bays and shaft |
| `blender/assets/structure_bunker_lift.py` | **New.** The elevator car |
| `unity/Assets/RF/Scripts/Core/TeamBunker.cs` | Gains the hall and `BayFor(slot)` |
| `unity/Assets/RF/Scripts/Core/VehicleBay.cs` | Parks visibly in a bay; rides the car |
| `unity/Assets/RF/Scripts/Core/BunkerView.cs` | **New.** The cutaway framing, static and testable |
| `unity/Assets/RF/Scripts/Core/TopDownCameraRig.cs` | A parked pose, not just a parked point |
| `unity/Assets/RF/Scripts/Players/PlayerVehicleDriver.cs` | Shows the hall; enables its own |
| `unity/Assets/RF/Scripts/UI/PlayerHud.cs` | Roster panel becomes a console strip |
| `unity/Assets/RF/Editor/ArtPipeline/GeneratedMaterials.cs` | Gains `RF_BayLight` |
| `unity/Assets/RF/Editor/Gameplay/VehicleSandboxScene.cs` | Places the hall and its lights |
| `unity/Assets/RF/Tests/...` | New bay-geometry and cutaway tests |
| `return-fire-homage-asset-spec.md` | The bunker entry gains the hall and the bay markers |

## Open questions for you

1. **Two levels or one?** The reference stacks two bays over two, which reads well in a
   cutaway and is why the plan above is two-storey. A single row of four is less art and less
   like the original. I would do two.
2. **Should choosing be blind?** It already is, as of M4 — the camera leaves the battlefield.
   Underground makes it emphatic. That is faithful, and it is also a real cost in a two-player
   match where the other player is still driving. Worth deciding on purpose rather than by
   default.
3. **How far to take the console?** Everything above stops at "the same idea as the reference".
   Matching its actual chrome — the drawn joystick, the bevelled metal, the CRT panel — is a
   different and much larger job, and it would be the first hand-authored art in a project that
   generates all of it.
