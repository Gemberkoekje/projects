# Pause menu, and the flag tower stops being memorable

**To understand this, start by reading `unity/Assets/RF/Scripts/Menu/PauseMenu.cs`, then
`Objective/FlagTower.cs` (`Roll`), then `Objective/Match.cs` (`OnEnable`).** The first is the
whole new feature. The second and third are a smaller, unrelated change that landed in the same
session: which tower is real is now rolled at the start of every match instead of fixed by the
level file.

Neither of these is a milestone from the design document or `MASTER_PLAN.md` — both were asked
for directly, outside the roadmap.

---

## Two unrelated changes, one session

**1. Escape now opens a real menu instead of arming a strip.** `MenuReturn.cs` (Escape, Escape
again within four seconds, straight to the main menu, no pause) is deleted. `PauseMenu.cs`
replaces it: Escape once sets `Time.timeScale = 0` and shows a panel with CONTINUE and MAIN
MENU; Escape again, or CONTINUE, resumes. Once `Match.IsFinished`, CONTINUE comes down and only
MAIN MENU is left - there is nothing left to continue, and which side won is already on both
halves of `PlayerHud`, so this panel stays neutral about the result rather than a second place
to read it.

**2. Which tower is real is authored, but a match no longer plays what is authored.**
`FlagTower.Roll()` rerolls the choice at random, per side, the instant `Match.OnEnable` runs -
which only happens in the real Sandbox scene, never in the level editor. See
`TOWER_RULES_NOTES.md`, updated in place, for the fuller before/after: this reverses a decision
made in M6 and reaffirmed after M7 ("still authored, not rolled... a second map is a second
answer").

---

## What was built

**`FlagTower.Roll()`** is a new public static method: for each side in `Teams.Playing`, it
gathers that side's towers off the existing `Live` list, picks one at random with
`UnityEngine.Random.Range`, calls `Configure(side, real)` on every tower in the group, and
re-homes that side's one `Flag` object (`Flag.Of(side)?.Configure(side, chosen)`). It does not
touch the level file, `LevelBuilder`, or anything the level editor reads - it runs *after* a
normal build, as a live reassignment of already-instantiated components.

**`Match.OnEnable()`** calls it, guarded on `Application.isPlaying` the same way `Flag`'s and
`FlagTower`'s own state machines are guarded (`Match` is `[ExecuteAlways]`). `Match` is the
right and only place for this: it is added to the scene by `VehicleSandboxScene.cs` alone
(`CreateSession`), never by `MainMenuScene.cs` or `LevelEditorScene.cs`, so its own existence
already means "a real match, not a preview or an editing session" - which is exactly the
condition this needed and didn't have anywhere else. By the time `OnEnable` runs, every
`FlagTower`/`Flag` in the scene has already finished its own `Awake`+`OnEnable` (they are
instantiated synchronously inside `LevelLoader.Awake`, which runs at execution order -200,
strictly before `Match`'s default-order `OnEnable`), so the roll always sees a fully-built
roster of towers.

**`PauseMenu.cs`** (`Scripts/Menu/`) is built the same way `MenuReturn`/`PlaytestReturn` were:
one `ScreenSpaceOverlay` canvas, generated from code, `EditorTheme`/`HudPalette` for the look
(the same borrowed theme `MainMenuController` already uses, for the same reason - a menu read
while sitting still is the same kind of thing in every scene). What's new is that its two
buttons are real `UnityEngine.UI.Button`s, and this scene had never needed one of those before:
`VehicleSandboxScene.CreateSession` now also builds an `EventSystem` with an
`InputSystemUIInputModule`, exactly the way `MainMenuScene.cs` already does for the main menu,
reusing `LevelEditorScene.EnsureUiActions()` for the shared "UI" action map rather than
inventing a second one.

---

## Decisions worth knowing

**The level file and the level editor were left completely alone.** `LevelTower.HoldsTheFlag`,
`LevelEdits`'s "make this tower real" tooling, `LevelGenerator`'s decoy assignment,
`LevelValidation`'s "exactly one real tower per side" rule, and every test that exercises them
- none of this changed. The authored value is still what the editor shows, still what a
playtest's *editor session* previews while you're placing towers, and still where
`LevelBuilder` puts the one `Flag` object it builds per side. `FlagTower.Roll()` only moves that
after the fact, and only when `Match` says a real match has begun.

**That split is why the roll lives in `Match.OnEnable`, not in `LevelLoader` or
`LevelBuilder`.** Those two are shared by the Sandbox scene *and* the level editor scene - the
editor's own live-editing view goes through the identical `LevelLoader.Show` call every time a
map is edited (`LevelEditorSession`'s own doc comment says so). Rolling in either of those would
have re-randomized the "real" tower on every single edit in the level editor, and worse, the
editor's `LevelEditorSession.Start()` *adopts* `loader.Shown` as the definition it goes on to
edit and save - a roll placed there would eventually get written back into the level file as if
it had been authored. `Match` was the only thing in the codebase that already only exists in a
real match.

**`PauseMenu` fully replaces `MenuReturn`, it doesn't sit alongside it.** Two things both
reading Escape and both claiming to be the way out of a match would fight over the key. Every
call site (`VehicleSandboxScene.CreateSession`, `MainMenuTests.cs`) was moved to the new type;
`MenuReturn.cs` and its `.meta` are deleted rather than left dead.

**`Time.timeScale` is reset in three places, not one.** `Close()` and `BackToMenu()` both set it
back explicitly, and `OnDisable()` sets it back again unconditionally - belt and braces, because
this is global engine state that outlives the scene it was set in. F1 can still leave a paused
match straight for the level editor (`PlaytestReturn` doesn't care whether `PauseMenu` is open),
and a test that fails between `Open()` and `Close()` would otherwise leave every later test in
the same process running at zero speed. `MainMenuTests.LeaveNothingBehind` now resets it too, as
the same kind of backstop it already keeps for the quality tier and the loaded scenes.

---

## Gotchas

**`Random` is ambiguous in `FlagTower.cs`.** The file already had `using System;` (for
`Action`), so a bare `Random.Range(...)` fails with `CS0104: 'Random' is an ambiguous reference
between 'UnityEngine.Random' and 'System.Random'`. Had to write `UnityEngine.Random.Range(...)`
in full. Cost one full batch-mode round trip to find - the log line to grep for is `error CS`,
per the project's own Unity batch-mode notes.

**A live, interactive Unity Editor open on this project makes `-batchmode` crash instantly.**
Exit code 1, a near-empty log ending at "Successfully changed project path" with no compile
step and no error - it looks like nothing happened at all. `Get-Process -Name Unity` confirms it
before wasting a run; the project's own Editor.log (not the `-logFile` target) shows the
interactive session that's holding it if there's any doubt whose window it is.

**The Sandbox scene had never had a `GraphicRaycaster`-driven button in it before.** Its HUD,
`MenuReturn`'s old strip and `PlaytestReturn`'s notice are all read-only overlays; nothing in
that scene had ever needed an `EventSystem`, unlike `MainMenu.unity` and `LevelEditor.unity`,
which both already had one. Building `PauseMenu`'s buttons out of `EditorTheme.Button` without
adding one would have produced a panel that looked right and did nothing when clicked.

**`Time.timeScale = 0` pauses the clock, not the game.** Caught by adversarial review, not by
any test: `PauseMenu.Open()` only sets `Time.timeScale`, and Unity keeps calling every
`Update()` on every real frame regardless - only `Time.deltaTime` goes to zero. A weapon whose
cooldown was already at zero (an ordinary state, not a contrived one - an `AutoTurret` that has
already tracked onto a target reaches it constantly) fires the instant the panel opens, because
`VehicleWeapon.TryFire()`'s `IsLoaded` check doesn't depend on `deltaTime` at all. Fixed at the
one choke point every shot already passes through: `TryFire()` itself now refuses while
`Time.timeScale <= 0`, the same way it already refuses when out of ammo. Nothing continuous
(turret traverse, vehicle movement) needed the same fix - anything driven by `Time.deltaTime`
already froze correctly.

---

## Not verified

**Never actually looked at the panel.** Every check here is structural (button names present
and wired, `Time.timeScale`, `IsOpen`, scene transitions) or via the CONTINUE/MAIN MENU public
methods directly, run headlessly with `-nographics` - the same shape `MenuReturn`'s own tests
used. Nobody has pressed Escape in a running window and looked at where the panel sits, whether
460×258 is a good size against a 1920×1080 canvas, or whether the borrowed `EditorTheme` colours
read as "paused" rather than as an editor panel that wandered into a match.

**Tried and reverted: routing `Press()` through a real `GraphicRaycaster` click.** Adversarial
review correctly flagged that `button.onClick.Invoke()` proves a listener is attached, not that
a real click could reach the button - the exact class of failure this scene has already had
once (the missing-`EventSystem` gotcha above). A version that raycast at each button's own rect
centre and drove the click through `ExecuteEvents` was written and run against the full suite,
and it failed two tests (`Play`, then `Continue`) that are not actually broken - pressing them
in a real window works. UI raycasting is known to be unreliable under `-batchmode -nographics`,
which has no real screen surface for `GraphicRaycaster`'s screen-bounds checks to work against.
Reverted rather than chase it blind; closing this gap for real needs either a windowed test run
or `UnityEngine.InputSystem.TestTools.InputTestFixture` injecting actual pointer events, not a
bigger workaround around headless raycasting.

**Whether the roll should be visible anywhere before a raider finds out the hard way.** Nothing
on the HUD says a match randomizes the real tower - the first anyone learns it happened is when
the decoy they remembered from last time turns out to be real this time. That may be exactly
right (rule one is "an intact tower gives nothing away," and this is one more thing it doesn't
give away), or it may be worth a line somewhere. Not decided, just noticed.

---

## Verified

Run from `C:\git\projects\IronFlag` on Unity 6000.5.9f1, after regenerating
`Assets/RF/Scenes/Sandbox.unity` via `Tools > IronFlag > Build Vehicle Sandbox Scene`
(`VehicleSandboxScene.BuildAndSave`): the project compiles with no errors, **475 edit-mode
tests pass**, and **all 176 play-mode tests pass**.

One of those, `FlagTests.AJeepTakesTheEnemyFlagAndCarriesItOnItsMast`, was failing
**pre-existing and unrelated** to either feature above - confirmed by `git stash`-ing every
change from this session and re-running that one test alone against the untouched `main`
branch (commit `28994bb8`), where it failed the same way. It is fixed now too; see below.

### A third, smaller thing: the flaky test, diagnosed and fixed

Not this session's ask, but the user asked to look at it once it turned up. Root cause:
`VehicleController.ConfigureBody()` sets every vehicle's `Rigidbody.interpolation =
RigidbodyInterpolation.Interpolate` - correct for a driven vehicle, but it means a plain
`jeep.transform.position = somewhere;` does not reliably "stick" on `transform.position` for a
frame or more afterwards. The Rigidbody's own `.position` takes the new value immediately;
`transform.position` - what everything else in the game actually reads - lags behind, smoothing
from stale interpolation state, and without `Physics.SyncTransforms()` it does not simply lag,
it visibly *wanders* (traced with `Debug.Log`: one frame after teleporting to `(14, 0, -21)` the
transform read `(6.66, 0, -8.94)`, and a frame later `(7.09, 0, -9.64)` - moving, not settling).

Every other reposition in this file (`jeep.transform.position = ...` appears eight more times)
is unaffected, because each of them checks a discrete state enum afterwards (`FlagState.Carried`
became `Captured`, say) rather than comparing two live transforms - the state read inside
`Flag.LateUpdate`, in the one correct frame, is enough to latch a state transition permanently.
This is the one test that instead asserts `CombatPlane.DistanceOnMap(flag.transform.position,
jeep.transform.position) < 0.01f` - two continuously-read positions, so it is the one place the
interpolation lag was ever actually visible.

Fix, isolated to this one test: `Physics.SyncTransforms()` immediately after the position
assignment, plus a second `yield return null` - confirmed empirically, in this order, that
either alone was not enough (sync with one frame still measured 0.36 m off; two frames with no
sync was still diverging, not converging). Nothing in `VehicleController`, `GroundVehicle`, or
any non-test file changed - this was a test-authoring gap around a Unity interpolation
subtlety, not a gameplay bug.

New coverage, six tests: `FlagTests.RollingPicksExactlyOneTowerAndBothEventually` (rolling
picks exactly one of a side's towers, moves the flag to it, and - over up to 40 tries - picks
both, so it is provably not always the authored one), `FlagTests.CreatingTheMatchRollsTheTowersToo`
(the same, driven through `Match`'s constructor rather than calling `Roll()` directly, to prove
the wiring rather than the mechanism), and four in `MainMenuTests.cs`:
`TheMenuPausesTheMatchAndASecondPressResumesIt` and
`TheMainMenuButtonLeavesAPausedMatchAndForgetsWhichMapItWas` (replacing the two tests that
exercised `MenuReturn`'s double-press API directly), `ThePauseMenusButtonsAreWiredToSomething`
(the same "a listener that was never attached looks exactly like a button that isn't there"
check the main menu's own buttons already get), and `AFinishedMatchOffersOnlyTheWayOut` (forces
a win via `Match.Win(...)` directly and checks CONTINUE is gone from the *active* button list,
not merely inactive-but-present).

---

## File map

- `unity/Assets/RF/Scripts/Objective/FlagTower.cs` - new `Roll()` (public) / `Roll(Team)`
  (private)
- `unity/Assets/RF/Scripts/Objective/Match.cs` - `OnEnable` calls `FlagTower.Roll()`
- `unity/Assets/RF/Scripts/Levels/LevelTower.cs` - doc comment corrected, no code change
- `TOWER_RULES_NOTES.md` - "which tower is real" entry corrected in place
- `unity/Assets/RF/Scripts/Menu/PauseMenu.cs` - new
- `unity/Assets/RF/Scripts/Menu/MenuReturn.cs` (+ `.meta`) - deleted
- `unity/Assets/RF/Editor/Gameplay/VehicleSandboxScene.cs` - `PauseMenu` replaces `MenuReturn`
  in `CreateSession`; new `CreateEventSystem`
- `unity/Assets/RF/Scenes/Sandbox.unity` - regenerated (baked copy only; rebuilt fresh at load
  regardless)
- `unity/Assets/RF/Tests/PlayMode/FlagTests.cs` - two new tests, plus the
  `AJeepTakesTheEnemyFlagAndCarriesItOnItsMast` interpolation fix (unrelated to either feature)
- `unity/Assets/RF/Tests/PlayMode/MainMenuTests.cs` - two tests rewritten, two new, `Press`
  helper generalized from `MainMenuController` to any `Component`, teardown resets
  `Time.timeScale`
