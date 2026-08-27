# 1-Player Mode — what shipped

*To understand this, start by reading `Scripts/Players/SessionSeating.cs`, then
`LevelDefinition.IsSolo`, then the solo branches in `LevelValidation`. Those three files are
the whole feature; everything else in this document is a consequence of them.*

Master plan [item 6](MASTER_PLAN.md#6-1-player-mode). One player against a map with nobody
behind the other side: your bunker, and an enemy that is a field of flag towers behind their
own emplacements. Break towers until you find the one that was holding the flag, drive it home
in the jeep, win.

## The shape of it

**One-player mode is not a mode.** There is no menu toggle, no second scene, no second game
loop and no flag in the save file. It is a property of the *map*: a level with one bunker on
it is a one-player map, and the game reads that off the file at load. Pick `IRON WATCH` from
CHOOSE A MAP and you are playing on your own; pick `IRON CHANNEL` and you are not.

That fell out of what was already there rather than being a design choice made against
resistance. The flag, the decoys, the pickup, the capture and the win are the objective a
two-player match already had, pointed at a side with nobody behind it. `AutoTurret` has never
had a concept of "human". `SplitScreenLayout.ViewportFor` already had a full-screen answer for
one player. `LocalMultiplayer` was already a list. What did not exist was anything that
decided **how many of the two built seats are actually played**, and that is the whole of the
new code.

## What is new

**`LevelDefinition.IsSolo`** — one side has a bunker and the other does not. Derived, never
stored: a map with one bunker *is* a solo map, and a boolean in the JSON would be a second
answer that could disagree with the first. `IsPlayed(side)` is the same question per side, and
a bunker is the whole of it — it is where vehicles come from, where they are repaired, and
where a flag has to be driven for the match to end. Towers and emplacements are not a side
taking part; they are scenery that belongs to somebody, which is exactly what a solo map's
enemy is.

**`SessionSeating`** (on the session host, execution order −150) — reads the loaded map and
empties every seat whose side the map does not play, before anybody is dealt a controller or
half a screen. `LocalMultiplayer.Seat` then re-applies the viewports, and the survivor gets
`SplitScreenLayout.FullScreen`.

**Validation learned the second shape** rather than being bypassed for it. `LevelValidation`
branches on `IsSolo` in three places and is unchanged everywhere else:

| | match | one-player map |
|---|---|---|
| bunkers | both sides owe one | exactly one; the missing one is the mode |
| towers | both sides owe ≥2, exactly one real | the enemy owes them; **the played side owes none** |
| crossings | bunker ↔ bunker by land | bunker ↔ **every** enemy tower by land |

The other rules — shore margins, tower spacing, the reserve, the land, the structures — are
the same question either way and are not duplicated.

**`SoloLevelBuilder`** — a menu item (`Tools/IronFlag/Build Solo Level`) that draws the shipped
map from a written-down seed and writes it to `StreamingAssets/Levels/iron-watch.json`. The
map is *generated*, not hand-authored: settling four fortresses away from each other is
arithmetic the generator already does, and doing it by hand would be doing its job worse. What
is committed is the output, so the game reads a plain file like every other map.

## Decisions worth knowing about

**The empty seat is destroyed, not disabled.** A disabled seat is still four vehicles parked on
the enemy's ground, still on `TeamReserve`'s books, and still something a turret can decide to
shoot at the moment anything re-enables it. There is no state worth keeping — the seat is not
coming back inside a match, and the way to get it back is to load a two-sided map.

**Towers on the solo player's own side are a fault, not clutter.** Nothing on a one-player map
ever comes for them, so they would be an objective with no opponent: a HUD line that can never
change, and a second real flag in a scene whose entire loop is finding the first one.

**The home bunker gets no defence.** The plan asked this out loud (open question 2). Nothing in
this mode attacks it, so guarding it would be a defence against nobody. The generator already
took this position — a solo map gets no ramparts across green's front — and the shipped map
keeps it.

**Decoy count is the difficulty lever.** Open question 1, answered by what already existed:
the generator's tiers carry `SoloTowers`/`SoloTurrets` separately from a match's numbers, so
more towers to check is how a harder solo map gets harder. Turret *stats* are global and still
are; difficulty is count and placement. `iron-watch` is Medium: **4 enemy towers**, one real.

**`LevelGenerator.SoloFaults` is gone.** It was a private copy of the rules that survive the
shape change, scored against by the re-roll loop because validation only knew a match's rules
and every solo candidate was therefore tied at three faults. Its own comment said it should
move into `LevelValidation` when this item landed; it did. `Faults` is now
`LevelValidation.Problems` and nothing else, so the re-roll loop and the editor's Problems
panel are reading the same answer again.

**Losing still works, and it is the reserve.** Run out of jeeps and `Match` awards the win to
`Teams.OpponentOf(Green)` — a side with nobody behind it, which is correct: the HUD says
DEFEAT / GREEN HAS NO JEEPS LEFT. Nothing needed changing for that; it is worth knowing it is
what happens.

## Gotchas

**Execution order is load-bearing.** `LevelLoader` had no declared order and
`LocalMultiplayer` runs at −100, so the session used to seat players *before* the map existed.
The loader is now −200 and `SessionSeating` −150. If the map is not built by the time seating
runs, seating reads `null` and leaves both seats alone — which is the safe failure (two
players on a broken map is the game as it was; an empty session is a black screen with no way
out) but it is silent, so the order is the thing to check first if a solo map ever seats two.

**Destroying in `Awake` needs the switch-off first.** `Destroy` only takes effect at end of
frame. Without `SetActive(false)` first, the retired vehicles still register with the bunkers,
the retired HUD still builds its panels, and the retired camera still draws a frame.

**The audio listener travels with seat 0.** The scene builder gives it to the first seat
because there is only one pair of speakers. A hand-written level that plays *Brown* and not
Green destroys that seat and would leave the game with no listener at all, so `KeepAnEar`
hands it to whoever is left. It asks the surviving cameras rather than the scene, because a
listener destroyed a moment ago is still in the scene until the end of the frame and would
answer "yes, there is one" right up until the frame it stops existing.

**`AMissingBunkerIsRejected` was a test of the old world.** Taking a bunker off a match no
longer leaves a match with a bunker missing — it leaves a one-player map, judged as one from
that moment. The test is now `RemovingABunkerMakesItAOnePlayerMap`, and
`ALevelWithNoBunkerAtAllIsStillRejected` covers what a file with *no* bunker does (still
broken, still reported as a match).

**`IsSolo` counts played sides, not bunkers, and a duplicate slipped through on that gap.**
Two bunkers both on Green and none on Brown still reads as "one side played," so the missing-
bunker check waved it through as a clean one-player map while `LevelBuilder` quietly built
both Green bunkers and every gameplay system resolved "the" Green bunker to whichever
registered first — the second sat on the map fully built, holding its own vehicle stock,
reachable by nobody. `CheckBunkers` now counts bunkers per side directly and rejects more than
one, the same way it would reject a third side if this format ever grew one.
`LevelValidationTests.TwoBunkersOnOneSideAreRejected` covers it.

## Verified

- **EditMode 474/474**, **PlayMode 172/172**, headless, both clean.
- Six new solo checks in `LevelValidationTests`; six new tests in `SoloModeTests`, which load
  the real `Sandbox.unity` — the scene that is built with two seats, which is the only
  arrangement worth testing — on the real shipped map.
- The last of those plays the mode end to end: break the towers, take the flag, drive it home,
  `Match.Winner == Green`, `MatchOutcome.FlagCaptured`.
- `solo-map.png` is the shipped map from above; `solo-menu.png` is it on the menu, marked
  `1 PLAYER`.
- **One flake seen**: `FlagTests.AJeepTakesTheEnemyFlagAndCarriesItOnItsMast` failed once
  ("the flag did not follow the jeep") and passed on the re-run with nothing changed in
  between. It is unrelated to this work — it builds its own scene — but it is worth knowing it
  can flake rather than chasing it as a regression next time.

## What this does not do

- **No AI opponent.** The opposition is emplacements and geometry. Nothing drives.
- **No solo-specific HUD.** The objective panel degrades honestly on its own — the defence line
  is empty because there is no flag to defend — but nothing says "one player" once you are in
  the match.
- **Only one shipped solo map.** The generator makes more, from the editor's MAKE panel with
  PLAYERS set to 1, and they are playable the moment they are saved.
- **`Garrison` still rings solo towers at 40–53 m** rather than the 13–19 m the mode wants —
  see [GENERATOR_NOTES.md](GENERATOR_NOTES.md#gotchas-found-while-building-this). That is a
  pre-existing generator issue, untouched here because fixing it moves every solo map for
  every seed.

## File map

| File | What changed |
|---|---|
| `Scripts/Players/SessionSeating.cs` | **new** — empties the seats the map has no side for |
| `Scripts/Players/LocalMultiplayer.cs` | `Seat()`, which re-seats without touching the controls asset |
| `Scripts/Levels/LevelDefinition.cs` | `IsPlayed`, `IsSolo` |
| `Scripts/Levels/LevelValidation.cs` | three rules branch on `IsSolo`; `CheckSoloCrossings` |
| `Scripts/Levels/LevelLoader.cs` | execution order −200, so the map exists before anything asks about it |
| `Scripts/Editing/LevelGenerator.cs` | `SoloFaults` deleted; `Faults` is validation and nothing else |
| `Scripts/Editing/LevelEditorSession.cs`, `EditorUi.cs`, `MapOptions.cs` | the "cannot be played yet" wording, which is no longer true |
| `Scripts/Menu/MainMenuController.cs` | `1 PLAYER` on a solo map's row |
| `Editor/Gameplay/SoloLevelBuilder.cs` | **new** — draws and writes the shipped solo map |
| `Editor/Gameplay/VehicleSandboxScene.cs` | puts `SessionSeating` in the scene; `PlayerCount` is now seats *built* |
| `StreamingAssets/Levels/iron-watch.json` | **new** — the shipped one-player map |
| `Tests/EditMode/LevelValidationTests.cs` | six solo rules; one test rewritten |
| `Tests/PlayMode/SoloModeTests.cs` | **new** — the mode, on the real scene and the real map |
