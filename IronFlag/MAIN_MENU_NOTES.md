# Main Menu — what shipped

**To understand this, start by reading
[`MainMenuScene.cs`](unity/Assets/RF/Editor/Gameplay/MainMenuScene.cs) — it is the whole
shape of the thing in one file — then
[`MainMenuController.cs`](unity/Assets/RF/Scripts/Menu/MainMenuController.cs), which is the
screen itself, then [`MenuBackdrop.cs`](unity/Assets/RF/Scripts/Menu/MenuBackdrop.cs), whose
comments are the only place in the project that says out loud that this game has no horizon.**
Read `MainMenuScene` against its sibling
[`LevelEditorScene.cs`](unity/Assets/RF/Editor/Gameplay/LevelEditorScene.cs): they are the same
kind of generator, and the menu is the smaller one because it has no gameplay in it at all.

This is item 4 of [MASTER_PLAN.md](MASTER_PLAN.md). It shipped close to the plan as written.
Three of the plan's four open questions were answered before starting; the fourth — Play's
level-select preview — was answered by looking at what a level file actually contains.

---

## How to see it

`menu-root.png` is the boot screen, `menu-levels.png` the map list and `menu-settings.png` the
settings. They are the same frame with a different panel up, which is the point: three screens
in one column, and the map behind it never stops turning.

`menu-editor-bar.png` is the level editor with the new **MENU** button on the right of its top
bar, next to PLAY THIS MAP.

Rebuild and re-photograph with:

```bash
Unity -batchmode -quit -projectPath unity -executeMethod IronFlag.Editor.Gameplay.MainMenuScene.BuildAndSave
```

```bash
Unity -batchmode -quit -projectPath unity -executeMethod IronFlag.Editor.Gameplay.MainMenuScene.RenderToFile -menuPanel root -menuOutput menu-root.png
```

---

## The feature in one paragraph

The game now **starts in a menu** instead of starting in a match. `MainMenu.unity` is index 0
of the build list, so it is the one scene every session passes through, which makes it the only
honest place to apply a stored setting. It offers **Play** — a list of every map in both level
folders, each one read off disk so the row can say what the map is — **Level Editor**, which is
the first direct door into the editor the project has ever had, **Settings**, which is three
rows that all do something today, and **Quit**. Behind the column a real map turns slowly under
the game's own lighting and grade. Getting back is **Escape** from a match — which now pauses
first and offers a panel, see [PAUSE_MENU_NOTES.md](PAUSE_MENU_NOTES.md) for the mechanism that
replaced the double-press described in decision 4 below — and a **MENU** button in the editor,
which were out of scope in the plan and are the reason the menu is a screen you use rather than
a screen you pass.

| | |
|---|---|
| Build order | `MainMenu` (0) → `Sandbox` (1) → `LevelEditor` (2) |
| The menu column | 620 of 1920 canvas units, full height, three panels switched in it |
| Map list | 8 rows a page, paged; name, file, size, towers and props per row |
| Settings | screen mode, window size, quality tier — `PlayerPrefs`, written through |
| Backdrop | 52° down, 34° lens, 0.75 × half-extent back, a full turn every four minutes |
| Out of a match | `ESC`, twice, within four seconds — superseded, see [PAUSE_MENU_NOTES.md](PAUSE_MENU_NOTES.md) |
| Out of the editor | the **MENU** button, guarded like New/Open/Revert |
| Tests | EditMode 428/428, PlayMode 153/153 |

---

## The decisions

### 1. `LevelHandoff.Play`, because the plan's step 3 would have lied

The plan says level-select should call `LevelHandoff.Playtest(name)`. It must not.
`Playtest` sets `FromEditor = true`, and that flag is what
[`PlaytestReturn`](unity/Assets/RF/Scripts/Editing/PlaytestReturn.cs) reads to decide whether
`F1` has anywhere to go. Reusing it would have put a **PLAYTESTING … F1 BACK TO THE EDITOR**
notice over every match in the game and bound `F1` to a scene the player had never been in.

So `LevelHandoff` grew a third verb. `Playtest` means the editor sent this, `Play` means the
menu did, `Edit` means the map is going the other way. It is four lines and the reason it
exists is entirely in its doc comment.
`AMapChosenOnTheMenuDoesNotClaimToBeAPlaytest` is the regression test.

### 2. This game has no horizon, and it cost two attempts to find out

`LevelBuilder` builds the sea as a slab exactly two half-extents across — the map, no margin.
Every camera in the project until now was either close to the ground (the chase camera, 34 m)
or pointing straight down at it (the editor, 200 m), so nothing had ever looked *out* across
the map at an angle. The first backdrop stood 138 m back at 44° with the stock 60° lens and
photographed **the edge of the world**: a hard diagonal across the top of the frame with
nothing behind it.

The arithmetic says a wide oblique shot of a whole map is not available at all — holding 240 m
across the frame puts the far corners past 120 m, which is where the sea stops. So the menu
shows a *close* view of part of the map, which is the better picture anyway: at this range the
bunkers and the turrets are models rather than dots. Three numbers do it, and all three are
about the same problem:

- **A 34° lens** rather than 60°. At 60° the horizontal field on a 16:9 screen is 91°, and the
  corners of a frame that wide reach past the bounds from any useful distance.
- **0.75 × half-extent back** at **52°** down — near enough the chase camera's 58° to be
  recognisably the same game.
- **`LookInset`**, which aims 35% of that distance *short* of the middle, toward whichever side
  the camera is on. This is the one number that is about what the picture is *of*: every map
  here is a pair rotated half a turn about the origin, so the middle is the one place that
  belongs to nobody — on the shipped map it is open water, and the second attempt was a
  handsome photograph of a channel. Aiming short always lands on somebody's half.

It **orbits** rather than panning, because a pan reaches the coast in about forty seconds and
every fix for that is a rule about where the edges are on a map whose size is read out of a
file. An orbit has no edge to reach.

### 3. Settings is three rows because three rows is what works today

Nothing in this project had a stored preference before — no `PlayerPrefs` anywhere. The rule
used for what goes on the panel was that a setting has to **do something the day it ships**.
That rules out a master volume, which would be a slider over a game with no sounds in it
(item 8), and key rebinding, which is a feature rather than a row. Screen mode, window size and
quality tier all take effect the moment they are pressed.

Everything is stored as **what it means** rather than as an index: a resolution is a width and
a height, a tier is its name. Both lists belong to the machine rather than to the game, and an
index saved against one list and read against another is a player who set 1920×1080 once and
starts in 800×600 on a different monitor. A tier name that no longer exists answers −1 and the
quality is left alone, rather than dropping the player onto whichever tier happens to be first.

The two tiers this project has are Unity's stock `Mobile` and `PC`, which name a machine rather
than what they do — and one of those machines is not a thing this game runs on. There is a
small table that shows them as **SIMPLE** and **FULL**, falling back to the raw name for
anything it does not know, rather than renaming them in `ProjectSettings`.

### 4. Escape twice out of a match; a button out of the editor

> **Superseded.** The double-press strip this section describes was replaced outside the
> roadmap: `PauseMenu.cs` now pauses the match on the first Escape and shows a panel (CONTINUE /
> MAIN MENU) instead of arming a silent timer. `MenuReturn.cs`, the component this section is
> about, is deleted. See [PAUSE_MENU_NOTES.md](PAUSE_MENU_NOTES.md) for the replacement and why
> it happened; the reasoning below for *why the editor gets a button and a match gets a key* is
> still the current design and is left as written.

The plan put a return path out of scope. Without one the menu is a screen the game shows once
per launch, so it is in.

The two doors are deliberately different, and the editor's is the reason. **Escape is already
bound in the editor** — it clears the selection, which is something somebody does dozens of
times an hour — so rebinding it there would have been the most expensive keystroke in the
project. The editor gets a **MENU** button instead, on the right of the top bar with PLAY THIS
MAP, guarded by the same "press it again" guard as New, Open and Revert. `Playtest` saves on
its way out; `BackToMenu` deliberately does not, because leaving is leaving and a button that
silently wrote the map to disk could not be used to abandon an experiment.

A match has no interface anybody clicks, so it gets the key. Twice, because the whole of what
it does is throw away a match in progress — the same shape as `LevelEditorSession.ConfirmQuit`
and the editor's guard. It **says nothing until the first press**: a permanent "ESC to quit"
strip would be on every screenshot of a match this project takes from now on, and Escape is the
one key a player tries without being told, so the first press is the discovery and the line it
puts up is the instruction. During a playtest both doors are live — `F1` to the editor, `ESC`
to the menu — and the notice stands clear of `PlaytestReturn`'s.

### 5. The map list reads the files; it does not just list them

`EditorUi.ShowOpenPanel` lists file names, which is right for an editor where you already know
what your maps are. A menu is where somebody chooses a map they have not seen, so each row
parses its file and shows the map's own name over a line of facts — file name, size across,
towers, props. It costs one parse per map on the way into the list, which for a folder of text
files is nothing.

The level format has a `Description` field and it is **not** used: on the shipped map it is
1,900 words of design essay, and truncating that at the first sentence is a coin flip.

A map that cannot be **read** is listed and refused, with the parse error where its size would
be. A map that reads but breaks a rule is offered as normal — the generator makes single-player
maps that fail validation on purpose, and a menu that hid them would hide a shipped feature.

The list **pages**. `EditorUi`'s open panel shows the first 11 maps and silently drops the
rest; that is a bug it has and this does not.

---

## The traps

**`Awake` fires on the constructor line, and it fires in edit mode.** This is written down in
`EditorUi` and it bit twice more here. `MainMenuController` originally applied the stored
settings in `Awake` — which meant pressing *Tools > Build Main Menu Scene* resized the Unity
editor's own game view and switched its quality tier. `MenuReturn` built its notice canvas the
same way, so every saved copy of the game scene carried a strip that only means something
during a match. Settings moved to `Start`; both are now guarded on `Application.isPlaying`.
(`MenuReturn` is since deleted — `PauseMenu.cs`, its replacement, inherited the same guard on
its own `Build`; see [PAUSE_MENU_NOTES.md](PAUSE_MENU_NOTES.md).)

**A generated screen has to be rebuilt on the first frame of play, and forgetting it is
silent.** The scene carries a baked copy of the menu so the still has something to photograph,
but the private field that says "built" is not serialized — so after a scene load the menu was
a hierarchy nobody owned, with buttons whose listeners had never been attached. The two
PlayMode tests that caught it (`TheMenuComesUpWithTheMapsThatExist`,
`TheButtonsOnTheFrontScreenAreWiredToSomething`) failed with *the menu never generated itself*,
which is exactly the message that was wanted; without them it would have presented as buttons
that do nothing.

**One status line shared by three panels needs `Refresh` to know which one is up.** `Refresh`
originally refreshed all three, like the editor's panels do — and the map list's *"No maps
found"* appeared under the title screen, where nobody had asked about maps. Visible in the
first `menu-root.png` render, invisible to every test.

**`Smallest` must filter what is offered, never where you already are.** The size list dropped
anything below 1280×720 including the window's current size, so a batch run at 640×480 showed a
size that was not on its own list — and the stepper, which finds its place by looking the
current size up, could not step off it. `TheSizeTheWindowIsAtIsAlwaysOnTheList` is the test.

**`PlayerPrefs` is typed.** Asking a stored integer for its string is an error rather than an
empty answer, which matters in the test that saves and restores the machine's real settings.

**The canvas is matched on height, not half-and-half.** Everything on this screen is one column
measured down from the top and one row measured up from the bottom, so the only dimension it
can run out of is the vertical one. `matchWidthOrHeight = 0.5` makes the canvas shorter than
1080 units on a wide screen; on a 32:9 monitor that put the last map in the list underneath the
BACK button.

**Build order now has one owner.** `LevelEditorScene` used to append missing scenes to the
build list. That cannot express "the menu is first", and two generators that each knew a
different order would fight — whichever menu item was pressed last would decide what the game
boots into. It is `BuildScenes.Register()` now, called by all three generators, and a scene
that is not on disk yet is left out rather than listed as missing.

---

## What is still open

- ~~**Nothing returns to the menu from a *finished* match.**~~ Resolved by `PauseMenu`: once
  `Match.IsFinished`, its panel drops CONTINUE and offers only MAIN MENU. See
  [PAUSE_MENU_NOTES.md](PAUSE_MENU_NOTES.md).
- **No confirmation on Quit.** Escape out of a match opens a pause panel to confirm through;
  Quit from the menu does not, because there is nothing to lose on that screen. If a session
  ever holds unsaved state, that changes.
- **Settings apply on the menu's first frame only.** A player who never passes through the
  menu — there is no such path today — would get the defaults.
- **The backdrop is always the default map.** It could be the map under the cursor in the list,
  which would make the level select a preview. That is a bigger change than it looks: it means
  rebuilding the world on hover.
- **Item 9 (UI Visual Identity) will re-skin this.** It is built against `EditorTheme` as the
  plan directed, so that is one file rather than two.

---

## File map

**New**

| File | What it is |
|---|---|
| `unity/Assets/RF/Scripts/Menu/MainMenuController.cs` | the screen: three panels, the map list, the settings rows |
| `unity/Assets/RF/Scripts/Menu/MenuPanel.cs` | which of the three is up |
| `unity/Assets/RF/Scripts/Menu/GameSettings.cs` | the three stored preferences and how they are applied |
| `unity/Assets/RF/Scripts/Menu/MenuBackdrop.cs` | the slow orbit, and why it is framed the way it is |
| `unity/Assets/RF/Scripts/Menu/PauseMenu.cs` | Escape, out of (or paused inside) a match — see [PAUSE_MENU_NOTES.md](PAUSE_MENU_NOTES.md) |
| `unity/Assets/RF/Editor/Gameplay/MainMenuScene.cs` | the scene generator and its still |
| `unity/Assets/RF/Editor/Gameplay/BuildScenes.cs` | the build list, in the order the game needs it |
| `unity/Assets/RF/Scenes/MainMenu.unity` | generated |
| `unity/Assets/RF/Tests/EditMode/MainMenuTests.cs` | scene wiring, build order, the settings store |
| `unity/Assets/RF/Tests/PlayMode/MainMenuTests.cs` | the menu running, and both ways back |

**Changed**

| File | Why |
|---|---|
| `Scripts/Levels/LevelScenes.cs` | `MainMenu` and `MainMenuPath` |
| `Scripts/Levels/LevelHandoff.cs` | `Play` — see decision 1 |
| `Scripts/Editing/LevelEditorSession.cs` | `BackToMenu` |
| `Scripts/Editing/EditorUi.cs` | the **MENU** button, and `PinRight` for the two buttons that leave |
| `Editor/Gameplay/VehicleSandboxScene.cs` | adds `PauseMenu`; registers the build list |
| `Editor/Gameplay/LevelEditorScene.cs` | its own `RegisterScenes` replaced by `BuildScenes` |
| `Tests/EditMode/LevelEditorWiringTests.cs` | the game is no longer first on the build list |
| `ProjectSettings/EditorBuildSettings.asset` | `MainMenu` at index 0 |
