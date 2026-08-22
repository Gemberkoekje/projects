# M2 — Split-screen input: what exists and why

**To understand this, start by reading `unity/Assets/RF/Scripts/Players/LocalMultiplayer.cs`,
then `DeviceAssignment.cs`, then `PlayerControls.cs`.** The first is the whole two-player
session, the second is the rule that decides who holds what, and the third is the two lines of
Input System configuration that make one machine behave like two.
`unity/Assets/RF/Scripts/Core/SplitScreenLayout.cs` is the other half of the milestone and is
twenty lines of arithmetic.

This covers milestone **M2** from the design doc: two-player local input routing and viewport
setup. M1's vehicles and camera are in [M1_NOTES.md](M1_NOTES.md); M0's scaffolding is in
[SCAFFOLDING_NOTES.md](SCAFFOLDING_NOTES.md).

---

## How to see it

Open `unity/Assets/RF/Scenes/Sandbox.unity` and press Play. The screen splits: green on top,
brown below, four vehicles each. Plug a gamepad in and player two can drive.

| Action | Keyboard and mouse | Gamepad |
|---|---|---|
| Drive | `WASD` or arrows | left stick |
| Aim the turret | mouse pointer | right stick |
| Climb / descend (helicopter) | `Space` / `Left Ctrl` or `C` | A / B |
| Fire (wired to `VehicleWeapon` since M3; fires a round) | left mouse | right trigger |
| Next / previous vehicle | `E` / `Q` | RB / LB |
| Deploy (bunker select / field recall, since M4) | `F` / `Enter` | West button (X / Square) |

**Who gets what**: player one keeps the keyboard and mouse; player two is always on a gamepad.
Plug a second pad in and both players move onto pads. Nobody has to press a "join" button and
there is no device-selection screen — see the rule in
[`DeviceAssignment.Solve`](unity/Assets/RF/Scripts/Players/DeviceAssignment.cs).

Both generators still run headless, which is how M2 was verified:

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -quit -projectPath unity -executeMethod IronFlag.Editor.Gameplay.VehicleSandboxScene.RenderToFile -sandboxOutput m2-sandbox.png -logFile -
```

Run `-batchmode` but **not** `-nographics` — rendering needs a real graphics device. The render
is now both viewports composited into one image, so a broken split shows up in the PNG.

---

## What was built

**A controls asset that is about this game.** `Assets/RF/Input/IronFlagControls.inputactions`
replaces the Input System template's `Player` map with a `Vehicle` map: `Drive`, `Aim`, `Lift`,
`Fire`, `NextVehicle`, `PreviousVehicle`, each bound on both `Keyboard&Mouse` and `Gamepad`. It
is the same file as before — renamed, keeping its GUID — so it is still the project-wide
actions asset. The `UI` map is untouched, and is Unity's; M4 owns it when the bunker needs it.
(M4 later added a seventh action, `Deploy`, to this same map for bunker vehicle-select and
field recall — see [M4_NOTES.md](M4_NOTES.md).)

**Per-player input routing in two lines.** `PlayerControls.For` clones the asset per player,
sets `bindingMask` to that player's control scheme and `devices` to that player's hardware.
That is the whole mechanism: after it, one player's stick is invisible to the other.

**A rule for who holds what**, `DeviceAssignment.Solve(players, gamepads, keyboardAndMouse)`,
which is arithmetic over three numbers and has no idea what a device is. It re-runs whenever
something is plugged in or unplugged.

**A session**, `LocalMultiplayer`, which is the only thing in the scene that knows there are two
players. It deals the devices out, hands each driver their controls, and gives each camera rig
its viewport.

**A screen divided by a pure function.** `SplitScreenLayout.ViewportFor(slot, count)` — full
screen for one player, upper and lower bands for two, quadrants for three or four.

**A sandbox that is a two-player game.** `Sandbox.unity` now generates two mirrored start lines
facing each other, green at one end and brown at the other, with a bunker behind each and the
scenery mirrored about the middle.

---

## Decisions worth knowing

**Not `PlayerInput`, and not `InputUser`.** Both exist to manage players joining, leaving and
losing devices at runtime; this game has two seats decided before the match starts. What is left
of them after taking that away is `bindingMask` and `devices`, which `PlayerControls` sets
directly. The result is forty lines that can be read in one sitting and tested without a scene,
instead of a component whose behaviour is configured through an inspector. If M4's bunker turns
out to want a real join flow, `PlayerControls` is the thing to replace and nothing above it
changes.

**Aim is a different quantity on each scheme, and that is deliberate.** On the keyboard, `Aim`
is bound to `<Mouse>/position` — a place on the screen. On a gamepad it is the right stick — a
direction. `PlayerVehicleDriver` branches on `ControlScheme` to turn either into a direction on
the map. M1 did this by inspecting `lookAction.activeControl` to guess which device the player
was on; asking the scheme is the same answer without the guess, and it is why nothing outside
`PlayerControls` mentions a device type.

**The pointer is clamped to its owner's viewport.** There is one mouse and two halves of a
screen, and a pointer parked over player two's half would otherwise aim player one's turret at
something they cannot see. Clamping puts the aim on the edge of what the player is looking at,
which is what a turret pushed against its limits should do.

**Gamepads are dealt to the players *after* the first.** Player two gets pad 0, player three pad
1, and only a pad left over after that reaches player one. It reads backwards until you plug a
second pad in mid-session: this way player two keeps the pad already in their hands, and player
one moves off the keyboard onto the new one. Dealing from the front would swap the two pads over
and take a controller out from under someone mid-game.

**The first player's camera is the scene's own.** It keeps the `MainCamera` tag and the audio
listener; every seat after it gets a bare camera. Two audio listeners is a warning and doubled
sound, and a split screen still only has one pair of speakers.

**`Fire` exists but nothing shoots.** M3 owns weapons. The binding is here because a control
scheme without a fire button would have to be re-laid-out in M3 anyway, and it costs one action
and one property.

---

## Gotchas

**`[UnitySetUp]` runs *before* `[SetUp]`, not after.** This cost an hour. `InputTestFixture`
swaps the whole input system out in its `[SetUp]`, so a scene loaded from `[UnitySetUp]` wakes
up, finds no devices, deals nothing to anybody — and then the fixture resets the input system,
taking the session's `onDeviceChange` subscription with it, so it never recovers. Every test in
`SplitScreenTests` loads the sandbox from inside its own body, by yielding on `StartTheGame`,
for exactly this reason. The failure looks like "player one was never given a device", which
reads like a bug in the pairing rather than in the test.

**`InputTestFixture.Set` and `Press` do not work in these play-mode tests.** They build
*delta* state events, which need `control.currentStatePtr`, and under the batch-mode test runner
that pointer is null — so every one of them throws
`ArgumentNullException: Control 'Stick:/Gamepad/leftStick' does not have an associated state`
before the test does anything. Queue a whole `GamepadState` instead, which needs no such
pointer; `SplitScreenTests.Hold` is the one-line helper. Queue it rather than applying it, too:
the player loop's own input update delivers it at the top of the next frame, so a button reads
as pressed *this* frame in the driver's `Update`. Applying it inline spends the press before
anyone looks.

**Renaming an `.inputactions` file is safe; deleting it is not.** The stock asset was the
project-wide actions asset, referenced from `ProjectSettings/EditorBuildSettings.asset` by GUID
*and* by file ID. The importer registers the asset under the literal identifier `"<root>"` and
takes its name from the file path, so moving the `.meta` along with the file keeps both stable.
Deleting and recreating it would have silently unset the project-wide actions.

**Each player needs their own copy of the asset.** `bindingMask` and `devices` are properties of
the `InputActionAsset` *instance*. Point two players at the same asset and the second one's
settings overwrite the first's, which looks exactly like "player one's controls stopped
working". `PlayerControls.For` clones; nothing else should touch the shared asset.

**A vehicle whose player loses their device has to be told.** `LocalMultiplayer` calls
`UseControls(null)`, which releases the vehicle's controls, because a vehicle keeps the last
input it was given until it gets another one — so an unplugged controller would otherwise leave
it driving off with the throttle held.

**The prefabs were stale before this milestone started.** The jeep's `Acceleration` and
`Braking` had been retuned in `VehicleTuning` without `Build Vehicle Prefabs` being run, and
`VehiclePrefabTests.EveryPrefabCarriesItsOwnTuning` was failing on `main` because of it. The
prefabs have been rebuilt from the current table (jeep: 26 / 34). If that test fails again, the
fix is the menu item, not the test.

---

## Verified

Run from `C:\git\projects\IronFlag`, all on Unity 6000.5.9f1:

- The project compiles headless with no errors and **no warnings**. Three pre-existing
  `FindFirstObjectByType` deprecation warnings were fixed along the way.
- **80 edit-mode tests pass** (52 from M1, 28 new). The viewport maths, the device-assignment
  rule over every combination of up to four players and four pads, the controls asset against
  every name the code looks up, and the generated sandbox's two players, two viewports, two
  sides and single audio listener.
- **20 play-mode tests pass** (8 from M1, 12 new). Two virtual gamepads in the real
  `Sandbox.unity`: each pad drives only its own player's vehicle, both drive at once, a shoulder
  button changes one player's vehicle and not the other's, the vehicle left behind stops even
  with the throttle held, unplugging a pad puts player one back at the keyboard, unplugging both
  stops everybody, and each camera follows its own player in its own half of the screen.
- `VehicleSandboxScene.RenderToFile` renders both viewports into one image: green's four
  vehicles above, brown's four below, mirrored scenery, neither start line blocked.

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -runTests -projectPath unity -testPlatform EditMode -testResults editmode.xml -logFile -
```

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -runTests -projectPath unity -testPlatform PlayMode -testResults playmode.xml -logFile -
```

**Not verified:** two people actually playing it. No second gamepad has been held by a second
person, the split has not been looked at on a television, and the keyboard-and-pointer half has
only been exercised by tests — the pointer clamping in particular is checked as arithmetic, not
as a turret that feels right when the mouse leaves the viewport.

---

## File map

| Path | What it is |
|---|---|
| `unity/Assets/RF/Input/IronFlagControls.inputactions` | **The controls.** Renamed from `InputSystem_Actions`, same GUID |
| `unity/Assets/RF/Scripts/Core/SplitScreenLayout.cs` | Who gets which slice of the screen, pure |
| `unity/Assets/RF/Scripts/Players/ControlScheme.cs` | Keyboard-and-mouse, or gamepad |
| `unity/Assets/RF/Scripts/Players/ControlSchemes.cs` | The two binding-group names, spelled once |
| `unity/Assets/RF/Scripts/Players/DeviceAssignment.cs` | **The rule** for who holds what, pure |
| `unity/Assets/RF/Scripts/Players/PlayerControls.cs` | One player's private copy of the controls |
| `unity/Assets/RF/Scripts/Players/LocalMultiplayer.cs` | **The session.** Deals devices, sets viewports |
| `unity/Assets/RF/Scripts/Players/PlayerVehicleDriver.cs` | Controls to `VehicleInput`; owns the aim maths |
| `unity/Assets/RF/Scripts/Core/TopDownCameraRig.cs` | Unchanged but for its `Viewport` documentation |
| `unity/Assets/RF/Editor/Gameplay/VehicleSandboxScene.cs` | Now generates two players and two mirrored sides |
| `unity/Assets/RF/Editor/ArtPipeline/CameraCapture.cs` | Gained a several-cameras-into-one-image overload |
| `unity/Assets/RF/Tests/EditMode/SplitScreenLayoutTests.cs` | The viewport arithmetic |
| `unity/Assets/RF/Tests/EditMode/DeviceAssignmentTests.cs` | Every combination of players and hardware |
| `unity/Assets/RF/Tests/EditMode/ControlAssetTests.cs` | The asset against the names the code uses |
| `unity/Assets/RF/Tests/EditMode/SandboxWiringTests.cs` | Rewritten for two players |
| `unity/Assets/RF/Tests/PlayMode/SplitScreenTests.cs` | **Two virtual pads in the real scene** |
| `unity/Assets/RF/Prefabs/Vehicles/*.prefab` | Rebuilt: the jeep's tuning had drifted |
| `unity/Assets/RF/Scenes/Sandbox.unity` | Regenerated. Two players, two cameras |

---

## What M3 inherits

*M3 has since built the weapon side of this — see [M3_NOTES.md](M3_NOTES.md).*

- `PlayerControls.Firing` is bound and unread. Weapons hang off
  `VehicleController`, the same way the movement models do, and read them through the driver.
  (There never was a separate `FiredThisFrame` member; M3's `VehicleWeapon` reads `Firing`
  itself, via `VehicleController.CurrentInput.Fire`.)
- `VehicleTurret.AimYawDegrees` is where the gun is actually pointing, as M1 left it.
- Both players' vehicles are already on opposite sides (`VehicleTeamPaint.Team`), so "whose
  shot was that" has an answer before damage exists.
- Nothing about the input path has to change for a second weapon or a third player: add an
  action to the `Vehicle` map with a binding in both groups, and `ControlAssetTests` will tell
  you if you only did half of it.
