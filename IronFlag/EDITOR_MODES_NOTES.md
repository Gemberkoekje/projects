# Two Editor Modes — what shipped

**To understand this, start by reading
[`EditorMode.cs`](unity/Assets/RF/Scripts/Editing/EditorMode.cs) and its helper
[`EditorModes.cs`](unity/Assets/RF/Scripts/Editing/EditorModes.cs) — thirty lines that are the
whole idea — then the three-argument `At` in
[`LevelPick.cs`](unity/Assets/RF/Scripts/Editing/LevelPick.cs), which is where the fix
actually lives, then `Press` and `SetTool` in
[`LevelEditorSession.cs`](unity/Assets/RF/Scripts/Editing/LevelEditorSession.cs).** The editor
as a whole is [M8_EDITOR_NOTES.md](M8_EDITOR_NOTES.md); this changes one paragraph of it.

This was **not in [MASTER_PLAN.md](MASTER_PLAN.md)**. It was asked for directly, in two
sentences: that trying to place an asset selected and moved the ground instead, and that it
should be two separate modes — one to create the island, one to set and move assets and
turrets.

---

## How to see it

`editor-modes.png` is the editor with the shipped map open, showing the new **MODE** row at the
top of the left-hand column. It is generated the same way M8's still is:

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -quit -projectPath unity -executeMethod IronFlag.Editor.Gameplay.LevelEditorScene.RenderToFile -editorOutput ../editor-modes.png -logFile -
```

Then open `unity/Assets/RF/Scenes/LevelEditor.unity`, press Play, pick **TREE** off the
palette, and click on the island. Before this it selected the shore and the click became a
drag of the coastline; now it plants a tree.

---

## The feature in one paragraph

The editor is now in one of two modes and **Tab** crosses between them. In **LAND** the mouse
can only see rectangles of dry ground — drag one out over open water, drag its corners to pull
the coastline about — and nothing standing on the map can be clicked at all. In **BUILD** the
mouse can only see what stands on the ground — props, towers, bunkers — and the ground cannot
be clicked, so a click on the shore places whatever is in hand *on* the shore rather than
picking the shore up. The mode is not a new state to keep: it is read off the tool in hand,
because every tool belongs to exactly one of them.

| | |
|---|---|
| Modes | land, build — `Tab`, or the two buttons at the top of the tool column |
| Land mode | one tool; the whole mode is "shape the island" |
| Build tools | select, prop, tower, bunker — `1`–`4` |
| Opens in | build, on select — the mode that changes nothing by being entered |
| Shift | still forces a placement, in both modes |
| Where it lives | `LevelPick.At(level, at, mode)` — a filter on the candidates, three lines |

---

## The four decisions

### 1. A filter on what can be found, not a re-ordering of what was found

`LevelPick` already tried props before ground: *"anything standing on the ground is picked
before the ground, or nothing on a map could be selected without first moving the coastline"*.
That rule is correct and it is why the editor worked at all — and it could never have fixed
this, because **an order only settles which of two things wins a click both of them are
under.** The loser of that contest is then unreachable everywhere the winner is, and on a
finished map the ground is under every point worth building on. So the ordering made props
selectable at the exact cost of making ground the answer to every click meant to place one.

The mode removes one side from the question entirely. In build mode land is not a candidate,
so a click on the shore comes back with nothing and the tool in hand runs. In land mode props
are not candidates, so a shore can be dragged out from under a row of trees. Both directions
matter and only the second one is a bonus.

### 2. The mode is derived from the tool, not stored beside it

`LevelEditorSession.Mode` is `EditorModes.Of(tool)` and there is no mode field. Two fields
that have to agree are two fields that can come to disagree, and the way it would have
happened here is obvious: **any button that sets a tool without also setting the mode leaves
the editor in a mode whose tool it is not holding.** Picking a prop off the palette is exactly
such a button, and it existed before this change did. With one field there is nothing to keep
in step — choosing a tool *is* choosing its mode, so `SetPaletteKind` crossing from land to
build is not a special case anybody had to remember to write.

The one thing that is remembered is `buildTool`: the round trip is lossy in only one
direction, because land has a single tool and build has four. Somebody laying a row of walls
who reaches for Tab to widen the ground beneath them gets the wall back when they return.

### 3. Land mode has one tool, so it needs no tool row

There is no "select land" to go with "draw land". The mode already cannot touch anything but
land, so the only thing a select-only variant would add is the *inability to draw* — which is
the reason for being in this mode at all. The mode button is therefore the whole of that
choice, and a tool row that only ever repeated what the button above it said would be one more
thing to read.

That is also what makes the mode readable off the tool: with a `Select` in both modes, `Select`
would have belonged to neither, and the mode would have had to become a field again.

### 4. Changing mode lets go of what the new mode cannot touch

Half a separation is not a separation. A mouse that cannot reach the coastline is no
protection if the coastline is still *selected*, because `Del`, `Q`, `E` and every field in the
inspector go through the selection rather than through the mouse — so the mode would stop
somebody dragging a shore by accident and leave them able to delete one by accident. `EnterMode`
drops a selection the new mode cannot hold, and drops any drag in progress with it.

---

## Two smaller changes that came with it

**A click on open water is a click now, not a rectangle of no size.** Pressing and releasing
without dragging in land mode used to fall through to `AddLand`, which rejected a zero-sized
rectangle and said so — a complaint about a minimum nobody was trying to reach, fired by the
gesture people use to deselect. It clears the selection instead, the way a click on nothing
does everywhere else.

**The opening sentence teaches the modes.** It used to read *"Press 2 and drag to draw land,
or 3 to place something"*, which was both stale and the wrong lesson; it now names the key that
crosses between the two, which is the one thing about this editor nobody can guess at. The help
strip along the bottom leads with `TAB land/build` for the same reason.

---

## Gotchas found while building this

### `EditMode` is already a word in this project, and the compiler noticed before I did

The enum was called `EditMode` for about an hour. It compiled fine in
`IronFlag.Editing` and produced 51 errors in the tests, all of them variations on
`The type or namespace name 'Build' does not exist in the namespace 'IronFlag.Tests.EditMode'`
— because Unity's two test assemblies live in folders called `EditMode` and `PlayMode`, the
test namespace is `IronFlag.Tests.EditMode`, and inside it `EditMode.Build` resolves to the
namespace and not to the type.

Renaming to `EditorMode` fixed it and was the better name anyway: the `Editing` folder is
already `EditorButton`, `EditorCameraRig`, `EditorInspector`, `EditorOutline`, `EditorOverlay`,
`EditorTheme`, `EditorUi`, and in a Unity repo "EditMode" will always read as *the test
platform* to whoever comes next. **Anything named after a Unity concept needs checking against
`IronFlag.Tests.<that concept>` before it is typed twenty times.**

### The stale hint was only visible in the still

Nothing failed. 745 tests passed with the status line telling the player to press `2` to draw
land, a key that now picks the prop tool. It was caught by rendering `editor-modes.png` and
reading the bottom of the picture — which is the argument for the still being part of the loop
rather than a thing done at the end: **a string is not covered by a test suite that never
reads it.**

### The tool column had room, but only just

The mode row costs a heading and a row (70 px) and gives back the land tool's row (40 px), so
the column is 30 px taller than it was: 814 px of the 974 it has at 1080 lines, once the grid
button's own height is counted. The palette is `StructureTuning.Roster()` and grows by
existing — M8's *"a seventh prop appears by existing"* — so **the next four props are free and
the fifth one runs off the bottom.** Nothing measures this; it will show up in a still.

---

## File map

**New**

| File | What it is |
|---|---|
| `unity/Assets/RF/Scripts/Editing/EditorMode.cs` | The two modes, and why there are two |
| `unity/Assets/RF/Scripts/Editing/EditorModes.cs` | `Of` a tool, `DefaultToolOf` a mode, what a mode `Reaches`, and the `Other` one |

**Changed**

| File | What changed |
|---|---|
| `Editing/LevelPick.cs` | `At(level, at, mode)` — the filter. `StandingAt` split out of the old `At`, which is kept, unfiltered, and now used by tests only |
| `Editing/LevelEditorSession.cs` | `Mode`, `SetMode`, `EnterMode`, `buildTool`; `Press` asks with the mode; `SetTool` carries the mode; `SetPaletteKind` goes through `SetTool`; `Tab` bound, digits re-lettered to `1`–`4`; the click-on-water fix; the opening sentence |
| `Editing/EditorUi.cs` | The MODE row and `AddMode`; the tool column loses its LAND row and gains the "BUILD TOOLS" heading; the help strip leads with Tab |
| `Editing/EditTool.cs` | Docs: a tool belongs to one mode; why land has only one |
| `Editing/EditTarget.cs` | Docs: why the pick order is not enough on its own |
| `README.md`, `M8_EDITOR_NOTES.md` | The keys, and a pointer here |

---

## Tests

745 pass: 546 EditMode and 199 PlayMode. Seven are new.

**Without a scene** (`Tests/EditMode/LevelEditingTests.cs`)

- `TheBuildModeCannotFindTheGround` — the unfiltered pick answers `Land` on bare shore and the
  build mode answers nothing, while still finding the tree four metres away. The two halves
  together are the claim; the first one alone would pass against a broken filter that found
  nothing at all.
- `TheLandModeCannotFindWhatIsStandingOnIt` — the same point from the other side, on a point
  the unfiltered pick answers `Structure` for.
- `EveryToolHasAModeAndEveryModeOpensOnOneOfItsOwnTools` — walks both enums, so a sixth tool
  added without a mode fails here rather than silently reading as `None`.
- `AModeReachesOnlyItsOwnHalfOfTheMap` — the full matrix, both directions.

**In the editor scene** (`Tests/PlayMode/`)

- `ClickingTheGroundWithAPropInHandPlacesTheProp` — the actual gesture, through a virtual
  mouse: lay a patch of ground over the middle of the map, assert the middle of the map is
  clear ground *before* clicking (or the test proves nothing), pick a tree, press and release,
  and assert a structure appeared, is selected, and that the ground did not move. This is the
  one that would have failed before the change.
- `ChangingModeLetsGoOfWhatTheNewModeCannotTouch` — both directions, and that land mode is
  entered on its own tool.
- `ComingBackFromTheLandModeFindsTheSameToolInHand` — `buildTool`.

The arithmetic tests could not have caught the original bug and there was nothing wrong with
them: `LevelPick` always answered exactly what it was asked. What was wrong was the question
`LevelEditorSession` asked on a real press, which is why the regression test is a real press.

---

## What this does not do

- **Nothing is greyed out.** The palette, the side buttons and the build tools stay live in
  land mode, and clicking one crosses over — the same rule the palette always followed. An
  editor that refused a button it understood would cost a keystroke to say nothing.
- **No third mode.** Bounds, name, description and the vehicle reserves are still edited in
  the inspector with nothing selected, in either mode.
- **The modes are not remembered between sessions.** The editor opens in build, on select,
  every time. That is the mode that changes nothing by being entered.
- **The overlay does not dim the half you cannot touch.** Land in build mode looks exactly the
  same as land in land mode; only the mode buttons say which is live. If that turns out to be
  the thing people miss, the overlay is where it goes.
