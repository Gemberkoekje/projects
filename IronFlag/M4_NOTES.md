# M4 — Bunker, selection and supplies: what exists and why

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
