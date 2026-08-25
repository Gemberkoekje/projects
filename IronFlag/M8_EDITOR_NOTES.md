# M8 — The in-game level editor: what exists and why

**To understand this, start by reading `unity/Assets/RF/Scripts/Editing/LevelEdits.cs`, then
`Editing/LevelEditorSession.cs`, then `Editing/EditorUi.cs`, then
`Editor/Gameplay/LevelEditorScene.cs`.** The first is every change the editor can make to a
map, as plain functions on a `LevelDefinition` — no scene, no camera, no components, and
therefore the place the feature is actually tested. The second is the mouse wrapped around it.
The third is the panels. The fourth generates the scene the whole thing lives in.
`Editing/LevelPick.cs` is what is under the cursor and `Editing/EditHistory.cs` is undo.

M7 said the editor was the obvious next thing and that nothing had to be built for it first —
"editing is `LevelLoader.Show` with a changed definition, saving is `LevelFile.TryWrite` into
`LevelLibrary.UserFolder`, playing it is `LevelLoader.Load`; what is missing is only the UI."
That turned out to be true. Nothing in the level format, the builder, the loader or the
validator changed to make this work. M7's map is in [M7_NOTES.md](M7_NOTES.md), M6's flag in
[M6_NOTES.md](M6_NOTES.md), M5's destruction in [M5_NOTES.md](M5_NOTES.md), M4's bunker in
[M4_NOTES.md](M4_NOTES.md), M3's combat in [M3_NOTES.md](M3_NOTES.md), M2's split screen in
[M2_NOTES.md](M2_NOTES.md), M1's vehicles in [M1_NOTES.md](M1_NOTES.md), M0's scaffolding in
[SCAFFOLDING_NOTES.md](SCAFFOLDING_NOTES.md).

---

## How to see it

`m8-editor.png` is the editor with the shipped map open and a tower selected. It is generated:

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -quit -projectPath unity -executeMethod IronFlag.Editor.Gameplay.LevelEditorScene.RenderToFile -editorOutput ../m8-editor.png -logFile -
```

Then open `unity/Assets/RF/Scenes/LevelEditor.unity` and press Play. The map is above you,
orthographic and straight down; drag with the right button to pan and roll the wheel to zoom.
Click anything to select it, drag it to move it, and type its exact numbers in the panel on
the right. Press **PLAY THIS MAP** and you are driving on it four seconds later. Press **F1**
in the game and you are back in the editor with the same map open.

**That round trip is the milestone.** Everything else is a way of changing what goes into it.

| | |
|---|---|
| Tools | select, land, prop, tower, bunker — `1`–`5` |
| Palette | the rows of `StructureTuning.Roster()`, so a seventh prop appears by existing |
| Editable | every field of `LevelDefinition`: land, props, towers, bunkers, bounds, name, description |
| Undo | 64 steps, each a whole map |
| Rules | `LevelValidation`, unfiltered, live, in the bottom-right panel |
| Saves to | `persistentDataPath/Levels`, never over the shipped map |

---

## What was built

**The editor is a mouse wrapped around three files that need no Unity at all.**
`LevelEdits` is every change that can be made to a map; `LevelPick` is what is under a point;
`EditHistory` is undo. All three are plain functions on a `LevelDefinition`, which is why 46 of
the new tests build no scene, open no canvas and move no pointer. What is left in
`LevelEditorSession` is a state machine for the mouse and somewhere to keep the selection, and
it decides nothing about what a level *is*.

**A separate scene rather than a mode inside the game.** The game scene is two players, two
cameras, two HUDs and a match, and none of that is any use while drawing a coastline — all of
it would have to be switched off and back on again. A separate scene starts with nothing to
switch off. The price is that playtesting is a scene load, and the map has to get across the
gap; it already could, because **a map is a file**. `LevelHandoff` carries the name, the editor
saves before it leaves, and the game opens it the way it opens any other map.

**The editor knows the three traps a level file cannot catch, so nobody else has to.** A
bridge is placed sunk 1.2 m to its deck, because a bridge at ground level is a metre-high step
that looks like a perfectly good bridge. A depot is placed with its rate already on, because a
depot with a rate of zero is a model of a depot that refuels nobody. A new bunker faces the
middle of the map, because a bunker's yaw is which way its lift bay points and one facing away
deploys its vehicles into its own back wall. All three produce maps that look completely
normal, and all three are now impossible to author by accident.

**`LevelValidation` turned out to be the error panel already, exactly as M7 predicted.** It
returns sentences, in order, about one level, and nothing about it assumed a test was calling
it. The panel shows what it says, unedited, and turns green when it says nothing.

**Mirroring, because every map in this game is built in pairs.** One button copies whatever is
selected to the other side of the origin — *rotated* half a turn rather than reflected, which
is the difference between two identical runs and two mirror-image ones. A tower or a bunker
comes back belonging to the **other** side, so mirroring green's real tower produces brown's
real tower. There is a test that builds half a map, mirrors five things and asserts the result
validates clean.

**A ruler for the same rule.** With something selected, a faint box is drawn where its
opposite number would stand. Nothing is placed there and nothing is checked against it — it is
held up next to whatever is being dragged so a map can be kept symmetrical by eye.

**Which tower is real is a promotion, not a checkbox.** A side must have exactly one real
tower, and a checkbox on each of four identical pyramids is a way to build a map with two or
with none — neither of which looks any different on the map, because that is what a decoy is
for. So the only thing the editor offers is "put the flag on this one", which demotes the
others on that side and cannot touch the other side's.

**The mouse for roughly, the panel for exactly.** Both matter. The channel on the shipped map
is 13 m wide at the bridgeheads because a test computes that number from the jeep's real top
speed and the real bank height; it was never a number anybody was going to find by dragging.
So every coordinate is typeable, and the four edges of a rectangle of land are shown as the
four edges the file states rather than as a centre and a size.

**A new map is playable before anybody touches it.** `LevelEdits.Starter` makes an island, two
bunkers, four towers and two depots. An empty level fails eleven of `LevelValidation`'s rules
at once, and an editor that opens on eleven red lines has taught the player nothing except
that they have already done something wrong.

**The way back out of a playtest, and only when there is one.** `PlaytestReturn` sits in the
game scene permanently and switches *itself* off unless the editor is what loaded the scene.
A player who launched the game normally has no editor behind them, and a key that quietly took
them to one would be a way to lose a match by mistake.

---

## Decisions worth knowing

**Undo is a list of whole maps, not a list of reversible commands.** A level is a few kilobytes
of JSON and the format already knows how to write it; sixty-four of those is a few hundred
kilobytes. The alternative is an inverse operation per edit that has to stay correct as the
format grows. This one is correct by construction and stays correct the day somebody adds a
field — and it gets a real property for free: undoing is exactly what loading that map from
disk would do, so there is no state anywhere in the editor that undo can miss.

**Hit-testing asks the level file, not the physics.** A raycast comes back with a collider
somewhere inside an instantiated prefab, and turning that into "the fourteenth entry of the
structure list" means walking up a hierarchy looking for something nobody labelled. Worse, it
can only find things that were *built*: a prop whose kind the catalog has no prefab for is on
the map, is in the file, and has no collider — so it could be neither selected nor deleted,
which is the exact situation somebody opening the editor would most want to fix.

**The map is not rebuilt while the mouse is down.** A rebuild destroys and re-instantiates
every prefab on the map, which is fine once per edit and absurd sixty times a second. What
moves during a drag is a rectangle on the overlay; the world catches up on release.

**Every tool selects with the same button, and only differs in what a click on empty ground
does.** An editor where selecting is a different gesture per tool is one where you have to
remember which mode you are in before you can find out which mode you are in. Holding **shift**
overrides it and always creates, which is the only way to draw a rectangle over land that is
already there.

**Overlapping rectangles resolve smallest-first, and props resolve before ground.** Land
rectangles overlap on purpose — a headland is a small one laid over a long shore — so the small
one on top is what a click on it means. Picking the first would make a headland unselectable
the moment it was drawn. And anything standing on the ground is picked before the ground, or
nothing on a map could be selected without first moving the coastline.

**The selection is a list and an index rather than a reference.** Every mutation rebuilds the
array it touches and undo replaces the whole definition with one parsed out of JSON, so a held
reference would survive both and point at an object nothing is looking at.

**The editor's panels are pixel-exact; the HUD's scale with the screen.** Opposite decisions
for opposite reasons. A HUD is read at a glance while driving, so it should take the same share
of any screen; an editor is worked on, and a bigger screen should mean more map rather than
bigger buttons. The interface doubles in whole steps past 1080 lines, because the type is a
bitmap font and ×2 is crisp where ×1.3 is a row of blurred captions.

**Numbers are read and written in the invariant culture.** A level file is JSON written by
`JsonUtility`, which is invariant, so a machine set to a comma-decimal locale must not be able
to type a number into this editor that its own level file cannot then express.

**Saving always goes to the player's folder, never over the shipped map.** That is M7's rule
doing the job it was built for: the edit lands next to the player's saves, the game picks it up
because a player's copy shadows the shipped one, and reverting is deleting one file.

**Playtesting saves first.** The game reads a file, so a playtest of unsaved changes would be a
playtest of the last thing that was saved — the most confusing possible way for an editor to
behave. It also means a playtest cannot lose work, because the only way into one is through a
save.

**The three buttons that discard a map ask twice rather than opening a dialogue.** New, open
and revert throw away unsaved work, and a modal is a lot of interface for a question with one
consequence. Each asks in the status line and waits to be pressed again — and arms nothing at
all when there is nothing to lose, so the ordinary case of opening one map after another costs
no extra clicks. Pressing a different one of the three disarms the first, and saving makes all
three harmless, so the state cannot outstay the danger it is about.

**Undo does not clear the unsaved marker.** Undoing back to the state a file was saved in
leaves the editor holding a map that matches the file, but working that out means comparing
them, and an editor that quietly decides you have no unsaved work is one that eventually
decides it wrongly.

---

## Found on review

An adversarial review of this feature — five independent passes (correctness, Unity lifecycle,
data safety, simplification, test coverage), each finding checked by three independent
skeptics before being trusted — surfaced twenty-one real defects out of twenty-three candidates,
two of them genuine ways to lose work silently. All were fixed before this landed, and every
fix below has a test that pins it down. This is not a changelog kept separate from the
decisions above it; a decision does not stop being one just because it arrived a day later.

**Mirroring a decoy onto an empty side put the flag on it.** `AddTower` auto-promotes a side's
very first tower to real, which is right when mirroring the *real* tower and wrong when
mirroring a decoy — `Mirror` used to trust that heuristic instead of copying the source
tower's own flag status explicitly. A map built by mirroring only decoys validated clean and
shipped with the flag on the wrong pyramid, with no rule anywhere that could tell.

**A right-click pan mid-drag silently ate the drag.** Holding the left button on something to
move it, then reaching for the right button to pan without letting go — an ordinary two-hand
gesture on a scroll-wheel mouse — switched the state machine straight to panning, and
releasing the pan button put it back to *nothing held* rather than to the move in progress.
The drag vanished: no commit, no undo step, no message, and the thing stayed visibly selected
as if it had just been picked up. Fixed by refusing to start a pan while a drag already has
the state machine's attention.

**A stationary click could silently relocate an off-grid object.** Every frame a button was
held, the active grid ran over wherever the pointer currently was — including the press frame
itself, before the mouse had moved at all. An object placed off-grid by typing an exact
coordinate into the inspector, then merely *clicked* to select it, got silently snapped onto
the nearest grid point and the move committed as a real, undoable edit. Fixed by tracking
whether the pointer has actually moved since the button went down, and refusing to commit a
move or a resize until it has.

**The New/Open/Revert confirmation went stale the moment it was armed.** Pressing one of the
three once armed a "press again to confirm" warning, correctly — but nothing ever cleared it
except pressing a *different* one of the three, or actually confirming. Any amount of further
editing between the two presses was silently discarded by the second one, with no fresh warning
for the work done in between: exactly backwards for a confirmation that exists to protect
unsaved work. Fixed by stamping the arming with `LevelEditorSession.EditVersion` - a count of
real edits committed - and only honouring a second press when that count has not moved since.

**Picking a row from the open-map list bypassed that same confirmation entirely.** The toolbar
button that raises the dialog was guarded; the individual rows inside it were not, and the map
underneath the dialog stays fully editable - Delete, a drag, anything - the whole time it is
showing. Dirtying the map after the list opened clean, then picking a row, discarded the new
edit with no warning at all. Fixed by guarding each row the same way.

**Typed "NaN" or "Infinity" passed every safety check in the file.** `float.TryParse` accepts
both as valid numbers regardless of the style flags asked for, and neither survives a clamp -
`Mathf.Max` compares with `>`, and every comparison against NaN is false - so a NaN half-extent
read as a fully valid, playable map and `LevelFile.TryWrite` serialised it without complaint.
Fixed by rejecting non-finite input before it reaches a single setter, in a small
`EditorInspector.TryParseNumber` that is now tested on its own, without a scene.

**Closing the window dropped unsaved work with no warning at all.** The New/Open/Revert guard
was the *only* place unsaved work was protected, and closing the window - Alt+F4, the OS close
button - is how most desktop sessions actually end. Fixed with an `Application.wantsToQuit`
handler that refuses the first attempt while the map is dirty and lets a second one through -
the same shape as the toolbar guard, and sharing its staleness fix: an edit made after a
cancelled quit re-arms the warning.

**The pan/zoom/frame limit never learned about a bigger world.** It was computed once from the
level's half-extent at scene-generation time; the inspector's own "Half extent" field lets that
number change live, and the limit just never heard about it - leaving newly available parts of
an enlarged map unreachable by any tool. Fixed by recomputing the limit every time the map
rebuilds, in `EditorCameraRig.SetWorldExtent`.

**The batch-mode still could show the wrong file name.** `RenderToFile`'s `-level` override
read a different map than the scene's own default, but nothing told the toolbar's FILE field or
the session's saved-name bookkeeping about it, so a rendered picture of one map could be
captioned with another's name. Fixed by threading the name through `Adopt`.

**`EditorUi.Awake` ran before `Configure` ever could.** The scene generator builds this
component with `new GameObject(name, ..., typeof(EditorUi))`, which fires `Awake` on that very
line - before the next line can call `Configure` and give it a session. `Awake` used to build
the panels unconditionally and threw reaching the inspector, which reads the still-null
session. The exception was swallowed by the engine and logged rather than surfaced, and the
status bar and open-map dialog were left half-built in whatever got saved - invisible at
runtime only because neither `panels` nor `openPanel` is a serialized field, so any subsequent
load (Play, or opening the file) discards that partial hierarchy and rebuilds cleanly from
scratch regardless of what was on disk. Fixed by skipping the build in `Awake` until a session
exists, and having `Configure` finish the job itself - the same defensive shape `PlayerHud`
already used for the identical hazard, which this file had not followed.

**Three more findings were dead code genuinely dead, and one was data declared three times.**
`EditorButton.Control`/`.Text` and `EditorUi.OpenPanel` were unreachable from outside their own
class - the first two are gone; `OpenPanel` survived because a new test now genuinely needs it.
`{ Team.Green, Team.Brown }` was independently written out in `LevelValidation`,
`VehicleSandboxScene` and this editor; it is now `Teams.Playing`, declared once.

**Five were real gaps in the test suite rather than product bugs**, and the mouse-driven ones
were the most instructive: `LevelEditorInputTests.cs` is new precisely because nothing before
it ever drove `LevelEditorSession.Update` through an actual virtual mouse, or invoked a
generated button or field through its real Unity wiring rather than calling the session's own
methods directly - which is exactly where the pan-interrupt and stationary-click bugs above
were hiding, and exactly why the tests that pin them down needed a virtual mouse to find them
at all. Writing them surfaced a test-design trap of its own: the first version targeted the
shipped map's own bridge, off to one side of the map, and its screen projection landed under
the tool column at the test runner's window size - so the simulated click missed the map
entirely, and the test passed by verifying that *nothing* happened, which coincidentally looks
identical to the fix working. It now places a fresh object near the map's centre instead, which
is never under a panel at any window size the panels are laid out for. Also newly covered:
`LevelEditorSession.Start`'s fallback for a level name that does not exist - exactly the branch
the *Gotchas* below call out by name - which nothing had ever exercised.

Two findings from the same pass were checked and refuted rather than fixed: a worry that
`EnsureUiActions` would silently accept a pre-existing but wrongly-shaped `"UI"` action map, and
a worry about two batch-mode processes racing to write `IronFlagControls.inputactions` at once.
Both are real shapes of bug in the abstract; neither is reachable the way this project is
actually built and run.

---

## Gotchas

**A screen-space *overlay* canvas appears in nothing rendered to a texture.** It is drawn by
the engine after every camera, so the command-line still came out as a picture of a map with no
editor around it. Both editor canvases are `ScreenSpaceCamera` on the editor's own camera, the
same arrangement `PlayerHud` uses, and there is a test that says so.

**The canvas scaler does not run in a scene nobody is playing.** This cost most of an hour. The
panels work out how much of the view they cover so the camera can frame the map in what is
left; measuring the laid-out canvas gave 640 units in batch mode, a tool column that was
apparently a third of the screen, and a map framed at the zoom limit in the wrong half of the
picture. Everything is now computed from the camera's own pixel size and a scale factor the UI
sets itself — no measurement of a laid-out rectangle anywhere.

**The overlay is anchored in fractions of the view, never in pixels.** Same root cause. The
canvas is one size while the game runs and another while a still is being rendered, so a
selection box placed in pixels lands somewhere else in the picture. `WorldToViewportPoint`
answers in fractions and `EditorOutline` anchors by fractions, so the two can never disagree.

**In batch mode the camera is pointed at a texture of the right size *before* the panels are
built.** Otherwise the panels lay themselves out for the game view, the camera frames the map
for the game view, and the picture is taken at 1920×1080.

**`Canvas.ForceUpdateCanvases()` has to happen before the panels are refreshed as well as
after.** Before, so they measure a canvas that knows its own size; after, so the markers are
placed on a layout that has settled.

**`EventSystem.current.IsPointerOverGameObject()` is what stops one click doing two things.**
Without it, pressing Save also places a tree behind the button: the button takes the click and
the world takes it too, because they are two different input paths reading the same mouse.

**Keyboard shortcuts are skipped entirely while a text field has focus**, or typing a map
called "Sandy" selects the structure tool, deletes the selection and saves.

**Fields commit on `onEndEdit`, not on `onValueChanged`.** Every change rebuilds the map and
adds an undo step, so a field that reported each keystroke would rebuild the world eleven times
while somebody typed "Iron Channel" and leave eleven steps to get back through.

**An inspector row must not overwrite the field it is being typed into.** The rows are rebound
rather than rebuilt for the same reason: rebuilding would destroy the field the player is
typing in on exactly the frame their edit lands.

**`LevelLoader.Current` is static and survives a scene change.** A loader whose file will not
read leaves it holding the map from the scene before — so the editor now adopts
`loader.Shown`, which is empty in that case. Adopting the static would have handed the editor
one map under another map's file name, and the first save would have written one into the
other's file.

**A test that saves has to delete the file afterwards.** `LevelEditorTests` writes into
`persistentDataPath`, which outlives the run, and a leftover under a shipped map's name would
shadow it for every later test — which is the mechanism being tested.

**The `UI` action map was already in the controls asset.** `IronFlagControls` is the
project-wide input actions asset, so the Input System maintains one there whether anything uses
it or not. `LevelEditorScene.EnsureUiActions` is a check rather than a step, and exists for the
case where somebody has taken it out — a mouse that silently does nothing is a long way from
where anybody would start looking.

---

## Verified

Run from `C:\git\projects\IronFlag`, all on Unity 6000.5.9f1:

- The project compiles headless with no errors and **no warnings**.
- **284 edit-mode tests pass**, seventy-one more than M7 left behind. The new ones are in two
  groups. `LevelEditingTests` is the editor without an editor: a new map is playable before
  anybody touches it, a placed bridge is sunk to its deck and stays sunk when dragged, a placed
  depot actually supplies something, a side's first tower is the real one, promoting a tower
  demotes its own side's and leaves the other side alone, mirroring a decoy onto an empty side
  leaves it a decoy, a second bunker for a side moves the first, a new bunker faces the middle
  of the map, a rectangle is sorted and a sliver refused, mirroring rotates about the origin and
  hands towers and bunkers to the other side, **a map built by mirroring one half of it
  validates clean**, a prop is picked before the ground under it, the smallest rectangle
  covering a point wins regardless of which was drawn first, corners are grabbable from
  outside, undo and redo walk whole maps, a map that has not changed costs no undo step,
  number parsing rejects NaN and Infinity, the camera's pan limit moves when the world does,
  and what the editor writes reads back as the same map. `LevelEditorWiringTests` is the
  generated scene: it exists, both scenes are in the build with the game first, it has a map
  and a view and an editor, it carries a baked copy of the map, it can be clicked on, both
  canvases are drawn by its own camera (two of them, checked before iterating), the overlay
  does not swallow clicks, the controls carry the UI actions, the game scene carries the way
  back, and the panels finish building even when `Awake` fires before `Configure` can.
- **117 play-mode tests pass**, twenty-five more than M7 left behind, across three files.
  `LevelEditorTests` drives the session's own methods directly: it opens on the map that is up
  rather than a second copy of it, placing a prop puts it in the world, undo takes it back off
  and redo puts it back, deleting clears the selection, moving a tower moves it on the map,
  saving writes a copy the game would open, reverting puts the map back and empties the
  history, breaking a map shows up in the problem list, an ordinary match has no way into the
  editor, quitting with unsaved work is refused once and then allowed, an edit after a
  cancelled quit asks again, resizing the world lets the camera reach the new edge, a missing
  level name falls back to a new map — and, the one that matters most, **a map can be played
  and come back to the editor still open**, with the edit made before leaving present in the
  game. `LevelEditorInputTests` is new: it drives a virtual mouse through
  `LevelEditorSession.Update` and presses the real generated buttons and fields rather than
  calling the session directly - a right-click pan no longer abandons a drag in progress (and
  panning alone still works), a stationary click on an off-grid object does not move it (and a
  real drag still commits), a second press of New after fresh edits asks again rather than
  discarding them, picking a map row asks if the dialog was left open over a new edit, and
  typing a name into the FILE field and pressing enter actually saves under it.
- `Build Level Editor Scene`, `Build Vehicle Sandbox Scene`, `Render Level Overview` and both
  stills all run clean, with no warnings and no exceptions during generation.
- `m8-editor.png` is the editor; `m8-sandbox.png` is M6's staged raid, unchanged.

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -runTests -projectPath unity -testPlatform EditMode -testResults editmode.xml -logFile -
```

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -runTests -projectPath unity -testPlatform PlayMode -testResults playmode.xml -logFile -
```

**Not verified: whether anybody can make a good map with it.** Every claim above is about
whether the editor does what it says. Nobody has sat down and built a second map end to end,
and that is the only thing that will find out whether the tools are the right tools — in
particular whether drawing a coastline out of axis-aligned rectangles is bearable, which is a
question about the *format* that this makes askable for the first time.

**Also not verified: whether the round trip is fast enough to iterate on.** It works, and it is
a scene load each way. If it turns out to be four seconds, that is four seconds every time a
number is worth trying, and this whole feature is about trying numbers.

---

## File map

| Path | What it is |
|---|---|
| `unity/Assets/RF/Scripts/Editing/LevelEdits.cs` | **Every change the editor can make**, as plain functions |
| `unity/Assets/RF/Scripts/Editing/LevelPick.cs` | **What is under the cursor**, asked of the file rather than the physics |
| `unity/Assets/RF/Scripts/Editing/EditHistory.cs` | Undo, as a list of whole maps |
| `unity/Assets/RF/Scripts/Editing/LevelEditorSession.cs` | **The editor itself**: the tool, the selection, and what a drag does |
| `unity/Assets/RF/Scripts/Editing/EditorCameraRig.cs` | The overhead view, and how it frames a map around the panels |
| `unity/Assets/RF/Scripts/Editing/EditorUi.cs` | **The panels**, generated from the palette and the rules |
| `unity/Assets/RF/Scripts/Editing/EditorInspector.cs` | The numbers behind whatever is selected |
| `unity/Assets/RF/Scripts/Editing/EditorOverlay.cs` | What is drawn on top of the map |
| `unity/Assets/RF/Scripts/Editing/EditorOutline.cs` | Four bars anchored in fractions of the view |
| `unity/Assets/RF/Scripts/Editing/EditorTheme.cs` · `EditorButton.cs` | The look, and the four widgets it is made of |
| `unity/Assets/RF/Scripts/Editing/EditTool.cs` · `EditTarget.cs` · `EditSelection.cs` · `LandGrip.cs` · `LandEdge.cs` | What is being edited, and where |
| `unity/Assets/RF/Scripts/Editing/PlaytestReturn.cs` | **The way back**, and the only thing in the game that knows the editor exists |
| `unity/Assets/RF/Scripts/Levels/LevelHandoff.cs` | **The one thing that survives a scene change**: which map |
| `unity/Assets/RF/Scripts/Levels/LevelScenes.cs` | The two scenes, by name, so a load and a save cannot disagree |
| `unity/Assets/RF/Scripts/Levels/LevelLoader.cs` | Gained `Shown` and a quiet `Show`; honours the handoff |
| `unity/Assets/RF/Scripts/Levels/LevelDefinition.cs` | Gained `LandBounds`, which both views of a map are framed on |
| `unity/Assets/RF/Scripts/Core/Teams.cs` | Gained `Playing`, the roster three places used to declare separately |
| `unity/Assets/RF/Scripts/Levels/LevelValidation.cs` | Its own copy of the roster replaced with `Teams.Playing` |
| `unity/Assets/RF/Editor/Gameplay/LevelEditorScene.cs` | **Generates the editor scene**, and the still |
| `unity/Assets/RF/Editor/Gameplay/VehicleSandboxScene.cs` | Gained the way back; its scene path comes off `LevelScenes`; its own roster copy replaced too |
| `unity/Assets/RF/Editor/Gameplay/LevelPreview.cs` | Its land-bounds maths moved onto the level |
| `unity/Assets/RF/Tests/EditMode/LevelEditingTests.cs` | **The editor, without an editor** |
| `unity/Assets/RF/Tests/EditMode/LevelEditorWiringTests.cs` | The generated scene, checked for the silent failures |
| `unity/Assets/RF/Tests/PlayMode/LevelEditorTests.cs` | **The editor running, and the round trip** |
| `unity/Assets/RF/Tests/PlayMode/LevelEditorInputTests.cs` | **The mouse and the real buttons**, driven rather than called |
| `unity/Assets/RF/Scenes/LevelEditor.unity` | Generated |
| `m8-editor.png` | **The editor** |

---

## What comes next

- **Make a second map with it.** That is the only way to find out whether these are the right
  tools, and M7 already noted that the design tests are written against whatever level is
  loaded rather than against `iron-channel` — point them at a new file and they check the new
  map.
- **The surfaces pass is now the obvious partner to this.**
  [SURFACES_NOTES.md](SURFACES_NOTES.md#appendix-the-original-plan-as-written) (then still
  proposed and not started) said it owes the editor "a format it can paint into and a palette
  it can offer". The palette is a list of enum members in a column, which is exactly the shape
  a surface palette would be.
- **Nothing here saves a map into the shipped folder, deliberately.** Promoting a map somebody
  made into `StreamingAssets/Levels` is a copy in a file manager today. If maps start being
  shared, that becomes a real feature and it is one this format is already ready for.
- **The two numbers M7 flagged are still guesses**: what a tower costs to break open, and the
  twelve-second dropped-flag timer. Both are now a great deal cheaper to experiment with, since
  a variant map is four clicks and a Play button.
- **`Destructible.Collapsed` still has no listener.** M7 noted it; M8 did not change it.
