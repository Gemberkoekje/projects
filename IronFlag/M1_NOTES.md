# M1 — Vehicle movement & camera: what exists and why

**To understand this, start by reading `unity/Assets/RF/Scripts/Vehicles/GroundVehicleMotion.cs`,
then `GroundVehicle.cs`, then `unity/Assets/RF/Scripts/Core/TopDownCameraRig.cs`.** The first is
the whole driving model, the second is how it reaches the physics engine, and the third is the
only other thing M1 adds. `unity/Assets/RF/Editor/Gameplay/VehiclePrefabBuilder.cs` explains how
the models become drivable.

This covers milestone **M1** from the design doc: controllers for all four vehicles and a
per-viewport top-down camera. M0's scaffolding is in [SCAFFOLDING_NOTES.md](SCAFFOLDING_NOTES.md);
read its "Gotchas" section before touching art or materials.

---

## How to see it

```bash
./blender/build.ps1
```

is unchanged and still builds the art. What is new is, from inside the editor:

- **Tools > IronFlag > Build Vehicle Prefabs** — regenerates the four vehicle prefabs from
  the models.
- **Tools > IronFlag > Build Vehicle Sandbox Scene** — regenerates `Sandbox.unity`, which is
  the scene the build settings already point at. Press Play and drive.

Controls, for now, are whatever the Input System's stock `Player` map already binds:

| Action | Keyboard and mouse | Gamepad |
|---|---|---|
| Drive | `WASD` or arrows | left stick |
| Aim the turret | mouse pointer | right stick |
| Climb / descend (helicopter) | `Space` / `C` | A / B |
| Next / previous vehicle | `2` / `1` | d-pad right / left |

M2 replaces all of it with real per-player bindings, which is also when the d-pad stops being
bound to both driving and switching vehicles at the same time.

Both generators also run headless, which is how M1 was verified:

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -quit -projectPath unity -executeMethod IronFlag.Editor.Gameplay.VehicleSandboxScene.RenderToFile -sandboxOutput sandbox.png -logFile -
```

Run `-batchmode` but **not** `-nographics` — rendering needs a real graphics device.

---

## What was built

**A movement model that is not a physics simulation.** A vehicle is a heading and a signed
speed along it (`GroundMotionState`), advanced by a pure function
(`GroundVehicleMotion.Step`). There is no lateral slide and no engine torque; the rigidbody
exists so vehicles collide with the world and fall onto it, not so it decides where they go.
Every fixed step the model writes horizontal velocity onto the body and leaves the vertical
component to gravity.

**Four vehicles that differ only by their numbers.** `VehicleTuning.For(kind)` is the whole
roster in one table, and nothing in the movement code branches on which vehicle it is driving.
The one structural difference is the helicopter, which adds a vertical axis
(`HelicopterMotion`) and turns gravity off.

**A camera that never rotates.** `TopDownCameraRig` holds a fixed pitch and heading and moves
only its focus point, which leads the vehicle by up to nine metres of travel. It follows a
flying target's altitude at a fraction, so climbing shows more ground rather than chasing the
aircraft upwards. Split-screen in M2 is two of these with different `Viewport` rectangles.

**Prefabs generated from the models.** `VehiclePrefabBuilder` reads each `.glb`, fits a
collider, finds the wheels, turret and rotors by name, and writes
`Assets/RF/Prefabs/Vehicles/RF_Vehicle_*.prefab`. Nothing in them is meant to be hand-edited.

**A sandbox to drive in.** `VehicleSandboxScene` generates `Sandbox.unity`: ground, the four
vehicles, scenery with colliders, a camera rig and one player.

---

## Decisions worth knowing

**The handling numbers live in code, not in ScriptableObjects.** Balance is a comparison
between four rows - the jeep is the fastest, the ASV the slowest, the tank the heaviest - and a
table in one file can be reviewed in a diff in a way four YAML assets cannot.
`VehicleRosterTests` asserts the ordering rather than the values, so retuning is free but
inverting a design pillar is not. The prefab builder stamps a copy onto each prefab, so the
numbers stay editable in the inspector while playing; rebuilding resets them.

**The movement maths is pure, and the components are thin.** `GroundVehicleMotion` and
`HelicopterMotion` take a state and return a state. That is what lets M1 have fifty-two
edit-mode tests covering the feel of the vehicles with no scene, no rigidbody and no play mode
- and it is why `TopDownCameraRig`'s placement is static too: the headless render frames its
shot by calling the same function the rig calls.

**Wheeled and tracked vehicles differ by one boolean.** `PivotTurn` decides whether a vehicle
can rotate on the spot. The jeep cannot, and its steering also reverses when it backs up, which
is most of why it feels like a car while the tank feels like a tank.

**Prefabs are root / Visual / Model.** The cosmetic tilt gets its own node so a banking
helicopter does not bank its collider, and the model stays a nested prefab instance so
re-importing the `.glb` flows through. The team trim keeps Blender's neutral placeholder in the
prefab; which side a vehicle is on is a runtime decision made by `VehicleTeamPaint`, which is
the one-material swap SCAFFOLDING_NOTES describes.

**Releasing the controls is not a state.** An abandoned vehicle simply stops receiving input,
so it brakes to a halt and its turret recentres. Nothing has to be switched off, and M4's
vehicle switching gets that behaviour for free.

**Material generation moved out of the art preview.** `GeneratedMaterials` now owns the small
set of materials Unity has to supply, because the vehicle prefabs bind them too. It updates
existing assets in place rather than recreating them - see the gotcha below. `CameraCapture`
similarly holds the render-to-PNG path both scene generators use.

---

## Gotchas

**Ground friction reads exactly like driving into a wall.** `GroundVehicle` reads its velocity
back each step to notice obstructions - without that, hitting a wall leaves the model at full
speed and the vehicle leaps forward the moment it is free. But a vehicle resting on the ground
has friction applied to it between the write and the read, so the reconciliation saw the ground
as a permanent obstruction: the first play-mode test had the jeep travel **0.33 m in 1.5
seconds** at full throttle, with nothing in the log to say why. Vehicles now get a frictionless
`PhysicsMaterial` at runtime (`VehicleController.ApplyFrictionlessSurface`). Friction is not
something these vehicles model, so it must not be applied to them. If a vehicle ever
mysteriously refuses to accelerate, look here first.

**`AssetDatabase.CreateAsset` over an existing path hands out a new GUID.** It deletes the old
asset first, taking its `.meta` with it, so every prefab referencing that material silently
unbinds. This was harmless while only the preview scene used the generated materials and is not
harmless now; `GeneratedMaterials.EnsureAssets` loads-and-updates instead of recreating. Do not
"simplify" it back.

**The reconciliation compares horizontal speed, not the forward component.** Steering rotates
the body between the velocity write and the read, so projecting onto the new forward axis loses
a fraction of a percent per step - which reads as a permanent drag while turning.

**The helicopter's altitude is authoritative, not emergent.** Gravity is off and the body is
flown towards the modelled altitude each step, clamped to twice the climb rate so a collision
recovers by flying rather than teleporting. Integrating vertical velocity instead lets the
model and the body drift apart.

**The collider skips the rotors.** A helicopter collider fitted to the 4.4 m rotor disc cannot
fit through gaps its 1.34 m fuselage plainly clears. `VehiclePrefabBuilder.AddCollider` excludes
anything under `MainRotor` or `TailRotor`, and `VehiclePrefabTests` asserts the resulting width.

**Aim comes from the pointer unless a stick moved it.** The stock `Look` action is bound to
mouse delta, which is useless for a top-down turret, so the driver ignores the binding's value
when the last control used was mouse or keyboard and raycasts the pointer onto the vehicle's
ground plane instead. On a gamepad it uses the stick direction, rotated by the camera's fixed
heading. M2 owns making this a real two-player control scheme; the split-screen version also has
to decide which viewport the pointer is in.

**`Sandbox.unity` is generated now.** It was the URP template scene; regenerating it replaces
the contents, including the Global Volume - URP falls back to the default volume profile from
graphics settings, which is why the sandbox still looks right. Keep the path: the build settings
point at it.

**The ground is very dark.** `RF_Ground` is the art preview's backdrop grey, shared for now.
When M7 builds the real map it will want its own materials; do not read the sandbox's lighting
as a look.

---

## Verified

Run from `C:\git\projects\IronFlag`, all on Unity 6000.5.9f1:

- The project compiles headless with no errors and no warnings.
- **52 edit-mode tests pass.** Movement, flight, camera placement, the roster ordering, the
  generated prefabs' structure, and the input actions the driver looks up by name.
- **8 play-mode tests pass.** Real rigidbodies at a real fixed timestep: the jeep accelerates
  and cannot pivot, the tank pivots without drifting, the helicopter climbs then holds altitude
  and ignores the ground, released controls stop a vehicle, and the camera ends up looking at
  what it follows.
- `VehiclePrefabBuilder.BuildAll` builds all four prefabs with no warnings, which means every
  wheel, turret, rotor and team-trim object was found in the models.
- `VehicleSandboxScene.RenderToFile` renders the sandbox: four green vehicles, the helicopter
  airborne with its shadow below it, scenery, and the bunker.

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -runTests -projectPath unity -testPlatform EditMode -testResults editmode.xml -logFile -
```

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -runTests -projectPath unity -testPlatform PlayMode -testResults playmode.xml -logFile -
```

**Not verified:** how it feels. Nothing here has been played with a controller in hand, and the
tuning table is a first guess sized against the design doc's ordering, not against a playtest.

---

## File map

| Path | What it is |
|---|---|
| `unity/Assets/RF/Scripts/Vehicles/VehicleKind.cs` | The four vehicles, in roster order |
| `unity/Assets/RF/Scripts/Vehicles/VehicleTuning.cs` | **The balance table.** Start here to retune |
| `unity/Assets/RF/Scripts/Vehicles/FlightTuning.cs` | The helicopter's vertical numbers |
| `unity/Assets/RF/Scripts/Vehicles/VehicleInput.cs` | One frame of pilot intent, device-agnostic |
| `unity/Assets/RF/Scripts/Vehicles/GroundMotionState.cs` | Heading plus signed speed |
| `unity/Assets/RF/Scripts/Vehicles/GroundVehicleMotion.cs` | **The driving model**, pure |
| `unity/Assets/RF/Scripts/Vehicles/FlightState.cs` | Altitude plus rate of climb |
| `unity/Assets/RF/Scripts/Vehicles/HelicopterMotion.cs` | The vertical axis and the cosmetic tilt, pure |
| `unity/Assets/RF/Scripts/Vehicles/VehicleController.cs` | Base: identity, tuning, rigidbody, input |
| `unity/Assets/RF/Scripts/Vehicles/GroundVehicle.cs` | Jeep, tank, ASV |
| `unity/Assets/RF/Scripts/Vehicles/Helicopter.cs` | The one that flies |
| `unity/Assets/RF/Scripts/Vehicles/VehicleWheels.cs` | Rolls and steers the wheel objects |
| `unity/Assets/RF/Scripts/Vehicles/VehicleRotors.cs` | Spins the rotors |
| `unity/Assets/RF/Scripts/Vehicles/VehicleTurret.cs` | Traverses the turret; M3 reads `AimYawDegrees` |
| `unity/Assets/RF/Scripts/Vehicles/VehicleTeamPaint.cs` | The one-material team swap |
| `unity/Assets/RF/Scripts/Core/Team.cs` | Green and brown |
| `unity/Assets/RF/Scripts/Core/TopDownCameraRig.cs` | **The camera.** One per player viewport |
| `unity/Assets/RF/Scripts/Players/PlayerVehicleDriver.cs` | Device input to `VehicleInput`; M2 replaces the device half |
| `unity/Assets/RF/Editor/Gameplay/VehiclePrefabBuilder.cs` | Models to drivable prefabs |
| `unity/Assets/RF/Editor/Gameplay/VehicleSandboxScene.cs` | Generates `Sandbox.unity` and its render |
| `unity/Assets/RF/Editor/ArtPipeline/GeneratedMaterials.cs` | Team, light and ground materials (moved out of `ArtPreviewScene`) |
| `unity/Assets/RF/Editor/ArtPipeline/CameraCapture.cs` | Render-to-PNG and the SRP batcher workaround (moved) |
| `unity/Assets/RF/Prefabs/Vehicles/*.prefab` | Generated. Rebuild rather than edit |
| `unity/Assets/RF/Scenes/Sandbox.unity` | Generated. Rebuild rather than edit |
| `unity/Assets/RF/Tests/EditMode/*.cs` | Movement, flight, camera, roster, prefab and wiring tests |
| `unity/Assets/RF/Tests/PlayMode/*.cs` | The same vehicles driven with real physics |

---

## What M2 inherits

*M2 has since built all of this — see [M2_NOTES.md](M2_NOTES.md). Left here as the plan it
started from, not as an open question.*

- `PlayerVehicleDriver` is one player reading a shared action map. Split-screen needs
  per-player device pairing (`PlayerInput` or manual pairing), two rigs with `Viewport` set to
  the top and bottom halves, and a decision about which viewport a pointer belongs to. M2 did
  this with `PlayerControls.For` cloning the action asset per player and `DeviceAssignment.Solve`
  dealing out devices.
- `TopDownCameraRig.Viewport` already exists and is unused; setting it is most of the camera
  half of M2. M2 sets it in `LocalMultiplayer.ApplyViewports`.
- The vehicles need nothing: they take a `VehicleInput` from whoever is holding them.
