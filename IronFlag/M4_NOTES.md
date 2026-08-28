# M4 — Bunker, selection and supplies: what exists and why

> **The bunker view was rebuilt later** — [MASTER_PLAN.md § 10](MASTER_PLAN.md#10-bunker-view-rework),
> all five phases. Nothing below changed in the rules; what a player is *looking at* while
> those rules run did. Read this file first and then
> [The bunker view](#the-bunker-view-what-the-player-is-looking-at-while-they-choose) at the
> end, which is where the differences are written down.

**To understand this, start by reading `unity/Assets/RF/Scripts/Players/PlayerVehicleDriver.cs`,
then `Core/VehicleBay.cs`, then `Supply/VehicleSupply.cs`, then `UI/PlayerHud.cs`.** The first
is the one idea the rest hangs off — a player is either in their bunker choosing or out on the
field driving, never both, and only ever one vehicle is out. The second is everything physical
about that: where a vehicle waits, how long it takes to repair, and the ride out. The third is
the resource economy, and the fourth is the only part of any of it the player can actually see.
`Core/TeamBunker.cs` answers one question: where does a vehicle appear.

This covers milestone **M4** from the design doc: bunker UI, the spawn and switch flow, and
fuel and ammo tracking. M3's combat is in [M3_NOTES.md](M3_NOTES.md), M2's split screen in
[M2_NOTES.md](M2_NOTES.md), M1's vehicles in [M1_NOTES.md](M1_NOTES.md), M0's scaffolding in
[SCAFFOLDING_NOTES.md](SCAFFOLDING_NOTES.md).

---

## How to see it

Open `unity/Assets/RF/Scenes/Sandbox.unity` and press Play. **Neither player starts in a
vehicle.** Both are looking at their own bunker with a roster panel beside it: four vehicles,
all READY. Pick one and send it out; it rises on the lift outside the bunker door, or lifts off
the roof pad if you picked the helicopter, and about a second later you are driving it.

| Action | Keyboard and mouse | Gamepad |
|---|---|---|
| Move down the roster | `Q` / `E` | left / right shoulder |
| Deploy the highlighted vehicle | `F` or `Enter` | `X` (west button) |
| Leave the field (hold 1 s) | `F` or `Enter` | `X` |
| Drive · aim · fire · climb | as M2 and M3 left them | as M2 and M3 left them |

Holding the deploy button while you are out is how you change vehicle. Standing on your own
bunker it **parks** the vehicle — no explosion, no repairs, and it can be picked again straight
away. Anywhere else it **scuttles** it, which costs exactly what being shot costs: an explosion
and four seconds of repairs before that vehicle is available again. The other three are
available the whole time, so dying is a choice about which vehicle you want next rather than a
pause.

Three bars in the corner of your half say how you are doing: armour, fuel, ammunition. Drive
onto a depot to top one of them up, or go home to top up both. Run the tank dry and you stop
where you stand, still able to shoot — and holding the deploy button is the way out.

The generators still run headless:

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -quit -projectPath unity -executeMethod IronFlag.Editor.Gameplay.VehicleSandboxScene.RenderToFile -sandboxOutput ../m4-sandbox.png -logFile -
```

Note the `../` — Unity changes its working directory to `-projectPath`, so a bare filename
lands in `unity/`. The same is true of `-testResults`.

`m4-sandbox.png` is one moment of a match that never happened: the green player still in their
bunker in front of a full roster, and the brown player out in the tank with 62 armour, 83
seconds of fuel and half a load of shells. **None of it is in the scene** — `StageMatch` stows
both rosters, deploys one vehicle, shoots it, burns its fuel and fires off half its ammunition,
all through the same public methods the game uses, and `ShowHuds` generates the panels. The
saved scene has an empty canvas and four vehicles parked outside each bunker.

---

## What was built

**One vehicle out at a time.** This is the change everything else follows from. M3 parked all
four of a side's vehicles on the start line and let the pilot teleport between them; M4 keeps
three of them inside the bunker where nobody can shoot them and nobody can drive them.
Getting a different one means driving home or dying, which is the design document's core loop
and the reason the bunker is a place rather than a spawn point.

**A bay per vehicle, and a state machine with four states.** `VehicleBay` is M3's
`VehicleRespawn` with the decision taken out of it: `Ready` in the bunker, `Repairing` after
being wrecked, `Deploying` on the way out, `OnField`. It is what hides a wreck, counts its
repairs, refills it, and rides it out. Nothing stows itself — a vehicle with a bay and no
player is simply on the field, which is what every rig in the combat tests is.

**A ride out, not a teleport.** The design document asks for the spawn beat by name — "a
deliberate pacing moment, not just a menu" — so a ground vehicle rises out of the lift bay over
1.2 seconds and the helicopter climbs off the roof pad in the same time. Both points are found
in the bunker model by name (`LiftPlatform`, `Helipad`), the same way a round's muzzle is, so
moving either is an art change.

**Fuel measured in seconds.** A pool and an endurance are then the same number, which is the
only unit anybody balances in.

| Vehicle | Tank | Flat out | Standing still | Range flat out |
|---|---|---|---|---|
| Jeep | 90 s | 90 s | 600 s | 1980 m |
| Tank | 150 s | 150 s | 833 s | 1800 m |
| ASV | 180 s | 180 s | 1000 s | 1620 m |
| Helicopter | **70 s** | 70 s | **127 s** | 1400 m |

The draw is a straight line between the idle rate and one, so fuel depletes with use *and*
with time exactly as the document asks. The helicopter's idle draw is 0.55 — holding a hover is
most of the work of crossing the map — which is the roster table's "must return to refuel"
written as a number.

**Ammunition measured in seconds of trigger.** Four guns firing at eight times each other's
rate cannot be compared any other way.

| Weapon | Rounds | Seconds of fire | Damage in a full load |
|---|---|---|---|
| Grenade (jeep) | 24 | 24.0 | 528 |
| Cannon (tank) | 20 | 30.0 | 680 |
| Rocket (ASV) | 12 | 30.0 | 660 |
| Chaingun (helicopter) | 240 | 30.0 | 960 |

**Two kinds of place to fill them up.** `SupplyPoint` is one component covering both, because
the difference is entirely in the numbers: a bunker belongs to a side, reaches 12 m, fills both
pools in four seconds and takes the helicopter; a depot belongs to nobody, reaches 7 m, fills
one pool in eight, and does not. Rates are fractions of a full pool per second rather than
litres, which is what lets one depot serve pools that differ by a factor of twenty.

**A HUD per half of the screen, generated rather than authored.** Two panels, never both: the
roster while you are choosing, and armour/fuel/ammunition while you are driving.
`PlayerVehicleDriver.AtTheBunker` decides which. Everything on it is built from the player's
own roster at runtime, which is what lets the command-line still photograph a real HUD instead
of a mock-up of one.

**One button for the whole flow.** `Deploy` sends the highlighted vehicle out when you are in
the bunker and takes the current one off the field when you hold it out there. The existing
`NextVehicle`/`PreviousVehicle` moved from swapping vehicles mid-match to moving the highlight,
which is what they always read like.

---

## Decisions worth knowing

**A player is in exactly one of two places, and the ride out counts as the bunker.** A vehicle
on the lift is not one anybody is driving, and a pilot who could steer it would drive it off the
platform. That makes `AtTheBunker` true for the whole beat and means the HUD, the camera and
the input all switch on one flag.

**Dying and parking end in the same place and cost different things.** Both put you in front of
the roster. Being wrecked costs four seconds of repairs on that vehicle; driving home costs the
drive and nothing else, and refills you on arrival. That difference is the entire reason to
ever drive home, and it is why parking is deliberately free.

**Self-destruct is always available, not only when stranded.** The document offers it as the
answer to running out of fuel in the middle of nowhere, and gating it on an empty tank would
make the one escape hatch unavailable in every other bad situation. It costs what dying costs,
so it is never free — but it is always there.

**Leaving the field is a hold, and the hold has to be armed by letting go.** One button doing
two things is worth it — it is the same idea from both sides — but the ride out lasts longer
than the hold does, so a pilot who kept their thumb down after choosing would have been handed
a vehicle that immediately blew itself up. The recall arms only once the button has been
released. This was found by reading the code rather than by playing it, and it has a test that
holds the button for the whole ride and then some.

**Any vehicle can be highlighted, including one that is being repaired; only a ready one can be
deployed.** Skipping over the wreck would hide the countdown the player most wants to read —
"how long until I can take the tank" is a question the panel should answer rather than silently
refuse. Pressing deploy on it is refused rather than queued, because a request that is granted
three seconds later happens at a moment nobody chose.

**Vehicles are not lost permanently.** The design document's secondary win condition is
destroying all the enemy's vehicles, which implies attrition, but that belongs with M6's win
conditions rather than here: a roster that can be permanently ground down turns the sandbox
into something you cannot keep playing, and there is nothing yet that would end the match.

**The helicopter cannot use a field depot.** That is the drawback the roster table gives it,
spelled as one boolean. It is also why it cannot refuel while hovering: a supply point serves
an aircraft only below its landing altitude, so the helicopter has to actually come down onto
its own roof pad. All three of those together are what pays for the highest sustained damage in
the game.

**A stranded vehicle keeps its turret and its trigger and loses its engine.** The document is
explicit that it can still fight in place. A stranded *helicopter* is given a descent rather
than a hover, so it ends up on the ground where it can be shot rather than parked in the sky as
permanent cover.

**Steering is free and reversing is not.** Demand is the larger of the throttle and the
collective, so a climbing helicopter pays what an accelerating one pays, and a tank turning on
the spot pays nothing. Charging for steering would tax the tracked vehicles for doing the one
thing they are good at.

**A vehicle with no supply component never runs out of anything.** That is what every vehicle
assembled in a test is, and a rig should not need an economy bolted to it before it can drive
across a room or fire a shot. The same rule covers a supply that was never told what it is
carrying.

**Both pools are refilled instantly by the bunker and at a rate by everything else.** Waiting
for repairs is already the cost of dying; a vehicle that came out of its own bunker dry would
be a second, invisible cost.

**The HUD is generated from the roster, like everything else in this project.** A fifth vehicle
would appear on the panel by rebuilding rather than by anyone opening a prefab, and the saved
scene carries an empty canvas rather than a frozen snapshot of numbers that were true when
somebody pressed a menu item.

**The bunker panel sits to one side.** The camera parks on the player's own bunker while they
choose, and a panel in the middle of the view covers the building it is a panel about.

---

## Gotchas

**A screen-space canvas is geometry hanging a metre in front of a camera, and any other camera
close enough will draw it.** In split screen that is not a corner case: it is what happens the
moment both players drive to the same place, which in a game about capturing a flag is most of
the match. Each player's HUD is on its own layer (`Hud1`..`Hud4`) and each camera's culling
mask excludes everybody else's. Layers cannot be created from a runtime script, so
`VehicleSandboxScene.EnsureHudLayers` writes them into the tag manager — if that ever stops
running, both HUDs land on the default layer and the bug looks like the other player's fuel
gauge sliding across your windscreen.

**`Camera.Render()` draws Screen Space - Camera canvases and not Screen Space - Overlay ones.**
That is the whole reason the HUD is bound to a camera rather than to the screen: an overlay
canvas would never appear in the command-line still, and the still is the only way to look at
the HUD without pressing Play.

**Stowing the roster happens in `Start`, not `OnEnable`.** A vehicle can only be stowed once its
side's `TeamBunker` has registered itself, and the order two objects in the same scene wake up
in is not something to rely on. `OnEnable` still does the event subscription, which does not
care.

**A vehicle deploys half its own length past the lift platform.** The platform is 2.8 m deep and
the tank is 5.5 m long, so standing it on the middle of the platform left its back end inside
the bunker wall and physics spent the first second of every deploy shoving it out — which reads
as the vehicle being kicked as it appears. It is measured off the vehicle's own box collider,
so it is right for all four.

**`Awake` does not run outside play mode, and the command-line still deploys a vehicle.**
`TeamBunker` is `[ExecuteAlways]` so it registers itself in the editor, and `VehicleBay` finds
its own components lazily rather than only on `Awake`. `VehicleSupply.IsAircraft` had to become
lazy for the same reason and for a different caller: a *prefab on disk* has never woken up
either, and the test that asks four prefabs whether they fly failed on exactly that.

**NUnit's `Within` tolerance does not apply to a `Vector3`.** `Is.EqualTo(somewhere).Within(1)`
compiles and then compares exactly. Compare `Vector3.Distance` instead.

**Measure travel after the settle, not through it.** A vehicle set down on the lift drops the
last few centimetres onto the ground and shifts half a metre doing it, which is enough to fail
a test asserting that the *other* player's vehicle did not move.

**A stranded vehicle's input has to be overwritten after the pilot writes it.** `VehicleSupply`
runs at execution order 50 for that reason; at the default order the throttle would come back
every frame.

**Rounds are whole things, and at sixty frames a second a depot owes a jeep less than a tenth
of a grenade per frame.** Rounding that down refills nothing at all, ever, so a point that owes
anything hands over at least one round — which makes a small load fill faster than its rate
says, and is why the rate is written for the pool it is slowest on.

**Panel transparency is not a free knob.** The first bunker panel was at 72% and the sunlit
concrete roof behind it came through every row of the roster. It is at 88% now.

**A still has to be staged into the state it is claiming.** The first render showed four
vehicles parked outside the bunker under a panel that said all four were IN THE FIELD, because
nothing had stowed them — `Start` does not run in a scene nobody is playing.

**Unity writes `-testResults` relative to `-projectPath` too.** Same trap as `-sandboxOutput`;
the XML lands in `unity/`.

---

## Verified

Run from `C:\git\projects\IronFlag`, all on Unity 6000.5.9f1:

- The project compiles headless with no errors and **no warnings**.
- **157 edit-mode tests pass**, fifteen more than M3 left behind: a new suite for the supply
  roster, and new bunker-geometry, prefab, control-asset and sandbox-wiring checks. The fuel and ammunition
  columns read against each other and against the 104 m between the two bunkers — every
  vehicle can cross it at least four times over, the helicopter carries the smallest tank and the
  shortest loiter, every gun carries between twenty and forty seconds of trigger, and no load
  is worth twice another one. Alongside them: the draw curve and the demand rule, what a
  stranded pilot is left holding, a bunker deploying from the lift its model carries and a
  helicopter from the roof, the fallback for a bunker with no model, both pools stamped onto
  every prefab, both bunkers resupplying their own side and the helicopter, four depots each
  handing out one commodity to anybody, two HUDs on two cameras on two layers that cannot see
  each other, and every vehicle in the generated scene starting at its own bunker.
- **59 play-mode tests pass** - eleven new for the bunker flow, ten for the supply economy,
  and M3's five respawn tests replaced by the bunker suite that supersedes them. The whole loop: a match that starts with
  everybody inside choosing, a tank that rides the lift and is not drivable until it arrives, a
  helicopter that leaves from the roof and climbs away, only ever one vehicle out, a wreck that
  cannot be picked until it has been repaired, a selection that can rest on a wreck, parking at
  the bunker costing nothing, scuttling anywhere else costing the same as dying, a vehicle that
  comes back repaired and refuelled, and a camera that shows the bunker between vehicles and
  the vehicle when there is one. The economy: driving costing more than idling, a dry tank
  stopping a vehicle that goes on shooting, a helicopter that sinks when it runs dry, rounds
  being spent and an empty gun refusing to fire, a gun with no supply behind it never running
  out, both depot types serving both sides and only their own commodity, a helicopter turned
  away by a field depot and served by its own bunker only once it is down, and an enemy bunker
  that will not serve you.
- The **whole bunker path** is walked twice in the real `Sandbox.unity` with two virtual
  gamepads: a shoulder button that moves only its own player's highlight, a deploy button that
  sends out only its own player's choice, and a held deploy that takes only its own player off
  the field. Plus the regression test for the flaw above — holding the button through the
  entire ride out and finding the vehicle still in one piece.
- `Build Vehicle Prefabs` and `Build Vehicle Sandbox Scene` both run clean, and the sandbox
  wiring tests are what check the result.
- `VehicleSandboxScene.RenderToFile` renders both halves with both panels — see
  `m4-sandbox.png`.

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -runTests -projectPath unity -testPlatform EditMode -testResults editmode.xml -logFile -
```

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -runTests -projectPath unity -testPlatform PlayMode -testResults playmode.xml -logFile -
```

**Still not verified:** whether any of the numbers are any good. The tanks were sized so that
every vehicle can cross the map several times over, which is deliberately generous — fuel is
meant to be a decision about when to go home rather than a leash — and nobody has yet run out
at a moment that mattered. The four-second repair and the one-second hold were picked because
they felt like a beat. Two things are most likely to be wrong: the helicopter's 70-second tank,
which is the only number standing between it and being the best vehicle in the game, and the
1.2-second ride out, which is a pacing beat the first time and a wait the twentieth.

**Also not verified by playing it:** a helicopter descending onto its own roof pad fights the
roof — the altitude model keeps commanding downwards while the collider refuses, so it sits
there vibrating until the pilot lets go of the collective. It refuels correctly throughout. It
wants a "landed" state rather than a fudge, and there is nowhere sensible to put one until the
helicopter has a reason to land other than this.

---

## File map

| Path | What it is |
|---|---|
| `unity/Assets/RF/Scripts/Players/PlayerVehicleDriver.cs` | **The core loop.** Read this first |
| `unity/Assets/RF/Scripts/Core/VehicleBay.cs` | **Wait, repair, ride out.** Was `VehicleRespawn` |
| `unity/Assets/RF/Scripts/Core/VehicleBayState.cs` | The four states, in the order they happen |
| `unity/Assets/RF/Scripts/Core/TeamBunker.cs` | Rewritten: the lift and the pad, found in the model |
| `unity/Assets/RF/Scripts/Supply/VehicleSupply.cs` | **The tank and the ammunition box** |
| `unity/Assets/RF/Scripts/Supply/SupplyPoint.cs` | Bunkers and depots, one component |
| `unity/Assets/RF/Scripts/UI/PlayerHud.cs` | **Both panels**, generated from the roster |
| `unity/Assets/RF/Scripts/UI/HudPalette.cs` | The whole look of the HUD in one file |
| `unity/Assets/RF/Scripts/UI/HudBar.cs` | One labelled bar |
| `unity/Assets/RF/Scripts/UI/HudLayers.cs` | Why one player's HUD is not in the other's view |
| `unity/Assets/RF/Scripts/Vehicles/VehicleTuning.cs` | Gained the fuel columns |
| `unity/Assets/RF/Scripts/Vehicles/VehicleNames.cs` | What each vehicle is called on screen |
| `unity/Assets/RF/Scripts/Combat/WeaponTuning.cs` | Gained `Rounds` and what it is worth |
| `unity/Assets/RF/Scripts/Combat/VehicleWeapon.cs` | Takes a round out of the box now |
| `unity/Assets/RF/Scripts/Combat/VehicleHealth.cs` | Gained `SelfDestruct` |
| `unity/Assets/RF/Scripts/Core/TopDownCameraRig.cs` | Gained `Park`, for the bunker view |
| `unity/Assets/RF/Scripts/Players/PlayerControls.cs` | Gained `Deploy` and `DeployHeld` |
| `unity/Assets/RF/Input/IronFlagControls.inputactions` | Gained the `Deploy` action |
| `unity/Assets/RF/Editor/Gameplay/VehiclePrefabBuilder.cs` | Adds supply and a bay |
| `unity/Assets/RF/Editor/Gameplay/VehicleSandboxScene.cs` | Supply points, HUDs, layers, staging |
| `unity/Assets/RF/Tests/EditMode/SupplyRosterTests.cs` | The economy read against the map |
| `unity/Assets/RF/Tests/EditMode/SandboxWiringTests.cs` | Gained the supply, HUD and bunker checks |
| `unity/Assets/RF/Tests/PlayMode/BunkerTests.cs` | **The loop, end to end** |
| `unity/Assets/RF/Tests/PlayMode/SupplyTests.cs` | **Fuel and ammunition with a clock running** |
| `unity/Assets/RF/Tests/PlayMode/SplitScreenTests.cs` | Gained the bunker half of the input path |
| `blender/assets/structure_bunker.py` | Exports `LiftPlatform` and `Helipad` as markers |
| `unity/Assets/RF/Art/Models/RF_Structure_Bunker.glb` | Regenerated |
| `unity/Assets/RF/Prefabs/Vehicles/*.prefab` | Rebuilt with supply and a bay |
| `unity/Assets/RF/Scenes/Sandbox.unity` | Regenerated. Rosters parked at their bunkers |
| `unity/ProjectSettings/TagManager.asset` | Gained `Hud1`..`Hud4` |

---

## What M5 inherits

- **Destruction has a shape to copy.** `VehicleBay` already swaps a vehicle between visible and
  invisible by walking its renderers and colliders; a structure's three-state swap is the same
  move with meshes instead of a flag, and every structure already ships `_Intact`, `_Damaged`
  and `_Destroyed`.
- **`VehicleHealth.Destroyed` is still the one event everything hangs off**, and it now has two
  callers: a round and a pilot. A destructible building wants the same shape.
- **The depots are already components rather than scenery.** `SupplyPoint` on a depot that M5
  can blow up is what makes the design document's "destroyable/contestable" real, and nothing
  needs to change here for it — a destroyed depot switches its point off.
- **`PlayerHud` has room for the radar** the design document wants in the bunker panel, and
  `HudLayers` already keeps one player's from appearing in the other's view. M8 owns the
  minimap.
- **Nothing yet ends a match.** M6 brings the flag and the win conditions, and the two things it
  will want from here are `PlayerVehicleDriver.ActiveVehicle` (the jeep-only pickup rule is a
  question about what is out) and `VehicleBay.Stow` (carrying the flag home is a docking event
  that already exists). *M6 has since done exactly this — `Objective/Match.cs` and
  `Objective/Flag.cs` own the win check now; see [M6_NOTES.md](M6_NOTES.md).*

---

# The bunker view: what the player is looking at while they choose

**To understand this, start by reading
[`BunkerView.cs`](unity/Assets/RF/Scripts/Core/BunkerView.cs) — it is the whole of where the
camera stands and how bright each bay is, and all of it is arithmetic that takes numbers.
Then [`blender/assets/structure_bunker_hall.py`](blender/assets/structure_bunker_hall.py),
which is the room it is pointed at, and
[`TeamBunker.cs`](unity/Assets/RF/Scripts/Core/TeamBunker.cs), which is now the whole base
rather than two markers on a blockhouse.** After that,
[`VehicleBay.cs`](unity/Assets/RF/Scripts/Core/VehicleBay.cs) for the ride out and
`PlayerHud.BuildBunkerPanel` for the console.

This is [MASTER_PLAN.md § 10](MASTER_PLAN.md#10-bunker-view-rework), phases A–E, folded in
here rather than into a notes file of its own because it is the same milestone finished
properly: M4 gave the player a translucent list of four words floating over a roof, and the
design document had asked for a place.

**Look at it**: `ui-hud.png` — the top half is a player choosing. Rebuild it with
`-executeMethod IronFlag.Editor.Gameplay.VehicleSandboxScene.RenderToFile -sandboxOutput ../ui-hud.png`.

| Plan phase | What shipped |
|---|---|
| A. The underground base, in Blender | `RF_Structure_BunkerHall` + `RF_Structure_BunkerLift`; the bunker's lift slab became a collar |
| B. The cutaway view | `BunkerView`, `CutawayPose`, `TopDownCameraRig.Park(pose)`, `RF_BayLight` and a light per bay |
| C. The elevator | A three-legged ride — bay, shaft, surface — on a car that carries it |
| D. The console | A strip across the bottom; the selection moved into the world |
| E. Tests, still, notes | 10 new edit-mode, 7 new play-mode, `ui-hud.png`, this |

---

## What it is now

The player is looking into their own base from the field side: two decks of two bays around a
lift shaft, their own four vehicles parked in them under the lights, and a console along the
bottom of the viewport. Pressing a shoulder button lights a different bay and **sends the lift
to that deck**; pressing deploy rolls that vehicle out of its bay onto the car, rides it up the
shaft, and hands it over at the surface. The camera does not cut away for the ride — it is
watched from where the whole of it can be seen, which is where the camera already is.

The four bays are roster order read from the left of the picture: jeep and tank on the left,
ASV and helicopter on the right, with the heavy pair upstairs. That is the arrangement of the
game this is a homage to, and the helicopter is upstairs because it is the one that leaves
from the roof.

## Decisions worth knowing

**The front wall of the hall is not built.** The cutaway is a modelling convention, not a
shader and not a clipping plane: `RF_Structure_BunkerHall` has a back, two sides, a roof, two
decks and a facade with five holes in it, and simply no wall on the field side. It reads from
exactly one direction, which is why `BunkerView.YawOffsetDegrees` is 180 and nothing else. A
free-look camera would show it hollow, and there isn't one.

**The picture is hung from the top of the hall, not centred on it.** This is the one framing
mistake this view can make that looks like a bug in the world rather than in the camera: above
the hall's roofline there is nothing to draw but the underside of the sea slab and then sky.
So the camera solves its distance from the shape of the viewport and then puts the *top edge*
of the frame on a `Skyline` marker in the model. Everything the hall does not cover is below
it, where the facade carries on for ten metres.

**The distance is solved rather than written down**, because the viewport is a whole screen
for one player and a 3.5:1 letterbox for two, and no single number frames a 19 × 10 m room in
both. `BunkerView.SolveDistance` takes whichever of the two dimensions fits worse.

**A parked vehicle is visible and intangible.** What used to make a stowed vehicle unreachable
was having its renderers off; what makes it unreachable now is being twelve metres underground
with its colliders off. Combat is unaffected — a disabled controller was already off
`VehicleController.OnTheField`, so no turret ever saw one either way.

**A vehicle being repaired is not drawn.** An empty bay is the honest picture of a wreck in the
shop, because the only model this game has of a vehicle is the intact one, and an undamaged
tank sitting under the word REPAIRING would say the opposite of what the console says.

**The lift follows the highlight.** It is the only part of the select screen that answers "what
did I just press" while the player is looking at the picture rather than at the words under it
— and it means the ride out starts with the car already where it needs to be, which is what
keeps the ride to two legs and a step rather than three legs and a wait.

**The ride is the same length for everybody, so the legs are fractions rather than speeds.**
30% rolling out of the bay, 55% up the shaft, 15% stepping off. A constant speed would make the
jeep's deploy visibly slower than the tank's because the jeep lives one deck lower, for a reason
no player could see. **The ride went from 1.2 s to 2.0 s** — it used to be a metre and a half of
empty ground and it is now up to twenty metres of hall and shaft; the old number would have sent
every vehicle up the shaft at forty miles an hour.

**Where a vehicle ends up is untouched.** Half its own length past the lift for a ground vehicle,
the helicopter at its cruising altitude over the pad. Every M4 rule about the ride out survives;
what changed is that there is now a route to get there. `TeamBunker.DeployPointFor` did not move,
and neither did any of M4's tests about it.

**The hall is optional, and every bunker in every test has none.** A bunker with no hall parks
its roster at the top of the shaft, rides them out in the single lift M4 always did, and frames
the select camera on the building from above. That is not a fallback nobody uses — it is what a
bunker assembled out of two empty markers is, which is every bunker in `BunkerTests`,
`ReserveTests` and `CombatRulesTests`.

**The selection is not a highlighted row.** Which vehicle is chosen is said in the world — the
bay lights up, the lift comes to that deck — and the console only agrees with it, with corner
marks round one cell and its name in full ink. A filled highlight bar would be the menu this
whole view exists to stop being.

**The console carries capacities, not levels.** Everything waiting in a bunker is full, so a
fuel gauge would read 100% four times over. What differs between the four is what each one
*holds*, and that is what the choice is about: seventy seconds of fuel is the price of the
helicopter's gun.

**The hall is drawn only while its own player is choosing.** The ground is one-sided and opaque
from above, so it hides the hall on its own — the shaft is the hole in that argument, and a
player driving over their own bunker would otherwise be looking down a lit stairwell. The lift
car is deliberately *not* part of that: it is the deck a vehicle stands on at the top of the
shaft, and the battlefield camera would otherwise be shown a collar with nothing in it.

## Gotchas

**The sea is a box as wide as the whole map, and it is drawn under the island too.** A level's
sea slab runs from the water level down through `SeaThickness`, everywhere, so anything
underground that reaches up into it gets a sheet of water drawn across the middle of the select
view. The hall's roof is at −4.2 m and the shipped maps' seabed is at −3.7; that half a metre is
the whole reason this base is not one storey shallower. `SandboxWiringTests` measures it against
the real slab rather than trusting the number.

**glTFast negates x on import, and the select camera looks back along the bunker's heading — two
flips that do not cancel.** Blender +x is Unity −x, and the camera's right-hand vector is the
bunker's −x, so **Blender −x is the left of the picture**. Getting this backwards builds a hall
whose bays read helicopter-to-jeep against a console strip that reads jeep-to-helicopter, and it
took a render to notice. The `LiftPlatform` and `Helipad` offsets never showed it up, because one
is at x = 0 and the other's sign had never been checked against the model.

**`AddStaticColliders` would put a `MeshCollider` on every piece of the hall.** It is skipped
explicitly. Nothing can reach a room under the island's own collider, and every round in this game
resolves on a plane that starts at ground level, so that would have been several hundred triangles
of physics per bunker bought for nothing. There is a test that asserts the hall has no colliders
at all.

**Blender renames a second object called `Lamp` to `Lamp.001`.** The four bay lamps are `Lamp0`
to `Lamp3` for that reason: the first version of the asset exported three objects Unity was never
going to find, silently, and three bays with no light in them.

**Two owners of one transform is a fight, so the car has two methods.** `SendTo` eases towards a
height and is what the highlight uses; `Snap` writes it and is what the ride uses, every frame,
because a deck that eased into place one frame late is a vehicle hovering. `SendTo` also arrives
immediately outside play mode, which is what puts the car in the right place in the saved scene
and in the command-line still.

**A material property block, not `Renderer.material`.** Eight lamps share one asset and every one
of them is a different brightness; touching `.material` would clone the shared asset per bay and
leak one material per bunker per scene load.

**The console covers the bottom of the picture, and how much depends on the split.** It is laid
out in canvas units against a fixed reference width, so on the letterbox half of a shared screen
it covers twice the fraction it covers on a whole one. `BunkerView.ConsoleShare` is a *promise*
of 22% rather than a measurement; framing round the real height would push the camera far enough
back to lose the base in a field of rock. On a shared screen the strip stands in front of the
bottom of the lower bays, which is where the console in the reference sat anyway.

**Two play-mode tests are flaky, and neither is this pass's.** Across five runs of the suite
two different tests failed once each and passed unchanged on the next run, and both are
time-based measurements in code nothing here touches.
`SoloModeTests.BreakingTheRightTowerAndDrivingItHomeWinsTheMatch` writes `transform.position`
on a non-kinematic body without setting `Rigidbody.position`, so whether the write survives
depends on how many fixed steps land between two `yield return null`s;
`SurfaceDrivingTests.AParkedVehiclePaysNothingForTheGroundUnderIt` compares two fuel draws
measured over wall-clock time and asks them to agree within 5%. They are on the record here
because the next person to see one fail will want to know it is not the bunker.

## Verified

Run from `C:\git\projects\IronFlag`, Unity 6000.5.9f1, Blender 5.2:

- The project compiles headless with **no new errors and no new warnings** — 32 pre-existing
  `CS0618` obsolete-API warnings remain, all in files this pass did not touch.
- **542 edit-mode tests pass**, ten more than before. A new `BunkerViewTests` suite checks the
  framing and the lighting as arithmetic: every bay in the picture at three viewport shapes
  including the real 3.5:1 split, the top edge on the hall's roofline at all three, the console's
  share left clear on a whole screen, the chosen lamp brighter than a resting one and under the
  4.0 that clips to white, a bay lit warm white and only washed towards its side, and a bunker
  with no hall still getting a pose. Three new wiring tests run against the generated scene: a
  hall with a bay, a lamp and a light per roster slot and a car in its shaft; the hall having no
  colliders, being switched off, sitting under the ground and clear of the sea slab; and the bays
  reading in roster order from the left of the picture with the heavy pair upstairs.
- **196 play-mode tests pass**, seven more. A new `BunkerBaseTests` suite covers a bunker that
  has a hall: every vehicle waiting in its own bay, drawn, intangible and underground; every one
  of them flank-on to the camera and nose towards the shaft; a wreck leaving its bay empty until
  it is repaired; the lift waiting at whichever bay is highlighted and returning to the surface
  when the player leaves; a ride out that is inside the shaft halfway through and finishes exactly
  where a bunker with no hall would have put the same vehicle; the hall drawn only while its own
  player is choosing, and drawn for the whole of the ride; and only the highlighted bay lit.
- `BunkerTests` was corrected rather than extended in one place: what takes a waiting vehicle off
  the field is being intangible, not being invisible.
- `Build Vehicle Prefabs`, `Build Level Catalog` and all three scene builders run clean, and
  `ui-hud.png`, `ui-menu.png` and `ui-editor.png` are regenerated.

**Not verified:** whether 2.0 s is the right ride. It is M4's beat stretched to cover the distance
this gave it, nobody has played it, and it is the number most likely to be wrong in either
direction — a lift that crosses a hall and climbs fourteen metres in two seconds is quick, and the
twentieth deploy of a match is where a slow one would start to hurt. **Also not verified by
playing it:** how the letterbox half of a split screen actually reads. The arithmetic says every
bay is in the picture at 3.5:1 and a test asserts it, but the picture at that shape is a base
sitting in the middle of a lot of rock, and nobody has looked at one.

## File map

| Path | Change |
|---|---|
| `blender/assets/structure_bunker_hall.py` | **New.** Two decks, four bays, the shaft, the cutaway face |
| `blender/assets/structure_bunker_lift.py` | **New.** The car |
| `blender/assets/structure_bunker.py` | The lift slab became a collar round the shaft mouth |
| `unity/Assets/RF/Scripts/Core/BunkerView.cs` | **New.** Where the camera stands, how bright a bay is |
| `unity/Assets/RF/Scripts/Core/CutawayPose.cs` | **New.** Four numbers that are a parked camera |
| `unity/Assets/RF/Scripts/Core/BunkerLift.cs` | **New.** The car owns its own height |
| `unity/Assets/RF/Scripts/Core/TeamBunker.cs` | Gained the hall, the bays, the lamps and the car |
| `unity/Assets/RF/Scripts/Core/VehicleBay.cs` | Parks visibly in a bay; the ride out has legs |
| `unity/Assets/RF/Scripts/Core/TopDownCameraRig.cs` | `Park(CutawayPose)`: a pose, not just a point |
| `unity/Assets/RF/Scripts/Players/PlayerVehicleDriver.cs` | Shows the base, lights the choice, moves the lift |
| `unity/Assets/RF/Scripts/UI/PlayerHud.cs` | The roster list became a console strip |
| `unity/Assets/RF/Scripts/Levels/LevelBuilder.cs` | Hangs the hall and the car off each bunker |
| `unity/Assets/RF/Scripts/Levels/LevelCatalog.cs` | Gained the two models and the bay material |
| `unity/Assets/RF/Editor/ArtPipeline/GeneratedMaterials.cs` | Gained `RF_BayLight` |
| `unity/Assets/RF/Editor/Gameplay/LevelCatalogBuilder.cs` | Loads the two new models |
| `unity/Assets/RF/Editor/Gameplay/VehiclePrefabBuilder.cs` | The ride out is 2.0 s |
| `unity/Assets/RF/Editor/Gameplay/VehicleSandboxScene.cs` | Stages a player choosing; frames the cutaway |
| `unity/Assets/RF/Tests/EditMode/BunkerViewTests.cs` | **New.** The framing and the lighting as arithmetic |
| `unity/Assets/RF/Tests/EditMode/SandboxWiringTests.cs` | Three new checks on the generated halls |
| `unity/Assets/RF/Tests/PlayMode/BunkerBaseTests.cs` | **New.** A bunker that has a hall, with the clock running |
| `unity/Assets/RF/Tests/PlayMode/BunkerTests.cs` | One assertion corrected: intangible, not invisible |
| `return-fire-homage-asset-spec.md` | The bunker entry gained the hall, the lift and the bay rule |
| `ui-hud.png` | Regenerated: the top half is the bunker view |

## What is left undone

- **The generator and the editor never place a hall, and they do not have to.** It is hung off
  every bunker by `LevelBuilder`, so every map that has ever been made already has one. There is
  nothing to author, and nothing in the level format changed.
- **The bunker's team trim is still painted `Team.None`.** `LevelBuilder.BuildBunkers` has always
  done this, so both sides' blockhouses wear the neutral placeholder and are indistinguishable.
  Untouched here because it is not this pass's bug, but the bunker view is where somebody will
  notice it: the console says GREEN BUNKER over a building that is not green, and the bay lighting
  is currently the only thing about a bunker wearing a side's colour.
- **Bay lights cast no shadows**, deliberately. There are eight of them on a two-sided map and
  what a shadow would buy is the underside of a jeep.
- **The plan's optional rendered vehicle icons were not built.** The bays turned out to read at a
  glance on their own, which was the condition the plan put on them.
