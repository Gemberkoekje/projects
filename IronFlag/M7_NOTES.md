# M7 — Greybox map: what exists and why

> **One rule below has since changed.** Flag towers became destructible after this milestone:
> an intact tower now hides what it holds, breaking one open is the only way to find out and
> the only way to take it, and the ten-metre reveal radius no longer exists. Everything else
> here — the level format, the loader, the map — is unaffected. See
> [TOWER_RULES_NOTES.md](TOWER_RULES_NOTES.md).

**To understand this, start by reading `unity/Assets/StreamingAssets/Levels/iron-channel.json`,
then `unity/Assets/RF/Scripts/Levels/LevelDefinition.cs`, then `Levels/LevelBuilder.cs`, then
`Levels/LevelLoader.cs`.** The first is the map — the actual one, the whole of it, in a text
file. The second is the format it is written in. The third turns one into geometry and knows
no coordinates of its own, which is the test of whether the format really is the map. The
fourth is what throws the scene's baked copy away on the first frame of play and rebuilds from
the file. `Levels/LevelValidation.cs` is the rulebook a map has to pass, and
`Editor/Gameplay/VehicleSandboxScene.cs` is what is left of the scene generator once every
coordinate has moved out of it.

This covers milestone **M7** from the design doc: the v0.1 map — bases, depots, terrain, the
water crossing and the bridge over it — built as an **external level file** rather than as
code, because levels and a level editor are where this is going. M6's flag is in
[M6_NOTES.md](M6_NOTES.md), M5's destruction in [M5_NOTES.md](M5_NOTES.md), M4's bunker and
supplies in [M4_NOTES.md](M4_NOTES.md), M3's combat in [M3_NOTES.md](M3_NOTES.md), M2's split
screen in [M2_NOTES.md](M2_NOTES.md), M1's vehicles in [M1_NOTES.md](M1_NOTES.md), M0's
scaffolding in [SCAFFOLDING_NOTES.md](SCAFFOLDING_NOTES.md).

---

## How to see it

`m7-map.png` is the whole map from directly above, which is the picture of this milestone —
the split-screen still shows sixty metres of a map that is nearly two hundred across, and you
cannot judge a map through it. Both are generated:

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -quit -projectPath unity -executeMethod IronFlag.Editor.Gameplay.LevelPreview.RenderToFile -levelOutput ../m7-map.png -logFile -
```

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -quit -projectPath unity -executeMethod IronFlag.Editor.Gameplay.VehicleSandboxScene.RenderToFile -sandboxOutput ../m7-sandbox.png -logFile -
```

Then open `unity/Assets/RF/Scenes/Sandbox.unity` and press Play. Both bunkers now face each
other down a 140 m centre line, across a channel that cuts the island in two. There are three
ways over it: a **causeway** in the middle that nobody can take away, and a **bridge** on each
flank that anybody can. Drive off a bank and you drown — the same explosion, the same trip
home, the same four seconds of repairs as being shot.

**The best thing to do next is edit the JSON and press Play again.** No menu item, no scene
rebuild, no recompile. That is the milestone.

The map, in numbers:

| | |
|---|---|
| Island | 164 m × 184 m of land in a 240 m sea |
| Bunker to bunker | 140 m, down the centre line |
| Channel | 26 m wide, pinching to 13 m at each bridgehead |
| Bank height | 0.7 m; you drown 0.35 m below the land |
| Crossings | 1 causeway (16 m wide, indestructible), 2 bridges (320 hp each) |
| Towers | 4, two a side, 36 m apart — far enough that one blast cannot open both |
| Depots | one fuel and one ammunition in each half, on opposite flanks |
| Cover | 8 buildings, 16 trees, every one of them half of a mirrored pair |

---

## What was built

**A level is a file, and the file is the map.** `LevelDefinition` is bounds, a list of land
rectangles, bunkers, towers and structures; `LevelFile` reads and writes it as JSON;
`LevelBuilder` turns one into a scene. The thing worth checking is what is *not* in the C#:
there is no coastline, no bunker position, no tower spacing, no depot placement anywhere in
the codebase any more. `LevelBuilder` has no coordinates at all. If it had even one, the
format would be a description of the map rather than the map itself, and the level editor this
is scaffolding for would have half a map to edit.

**The map is described by its land, not by its water.** The design document's map is an
island, so water is the default and land is the thing somebody drew. A level cannot
accidentally describe a hole in the sea, and the shape of a coastline costs exactly as many
rectangles as it is worth. Rectangles are stated as their edges — "the green shore runs from
z -92 to z -13" is a sentence about the map; a centre and a half-size is arithmetic about it.

**Two folders, and the order between them is the level editor.** A shipped map lives in
`StreamingAssets/Levels`, which is a plain folder of plain files in a built game as well as in
the editor. A map the player has edited lives in `persistentDataPath/Levels` and *shadows* the
shipped one of the same name. So editing a map writes a copy next to the player's saves, the
game picks it up without being told, reverting is deleting a file, and nothing ever has to
write into the install folder — which on a real machine it often cannot.

**The scene bakes a copy and then throws it away.** `Sandbox.unity` still contains the whole
map, because a scene that opened empty would be a scene nobody could look at and the
command-line still would have nothing to photograph. `LevelLoader.Awake` destroys it and
rebuilds from the file, every time, without comparing them. The bake and the load call the
*same* builder — the editor passes prefab instantiation so the saved scene keeps its links,
and that is the only difference between them.

**A rulebook for maps, not just for this map.** `LevelValidation` is the set of ways a level
file can be wrong that would look completely normal in the editor: a bunker in the sea, a side
with two real towers, a decoy close enough to its twin that one round's blast could crack open
both, a prop standing on water, a bridge spanning dry land. It runs at bake, at load, and in the
tests, so the next map gets the same treatment as this one.

**The rule that keeps a match winnable.** The strongest thing in that rulebook: the two
bunkers must be joined by **dry land**, with bridges deliberately not counted. Only the jeep
carries the flag and the jeep cannot fly, so a map whose every crossing is destructible is a
map that becomes unwinnable the moment somebody drops the last one. That is why this map has a
causeway. Blowing both bridges is still a real play — it funnels the entire match through one
16 m chokepoint — but it can never end the game by deleting the route.

**Water that costs you the vehicle.** `WaterLine` walks `VehicleController.OnTheField` — the
same roll-call the flag uses — and self-destructs anything below the drowning line. The
helicopter never drowns and nothing had to be written to arrange that: `FlightTuning.MinAltitude`
holds it at or above the land, so it is never below the line. A vehicle stowed in its bunker
cannot drown either, for the same free reason M6 got its pickup rule from — the roll-call means
exactly "drivable, and out of its bunker".

**A map shot, because a game shot cannot judge a map.** `LevelPreview` renders a level
orthographically from straight above, framed on the land. It is the first thing to look at
after editing a level file, and it is the view the in-game editor will want.

**The material-swap rule moved into the runtime.** `ModelPaint` now owns which Blender
material group gets replaced by what; `GeneratedMaterials` keeps the half only the editor can
do, which is turning an asset name into the material behind it. The game paints its own
bunkers green now, and a built player has no asset database.

---

## Decisions worth knowing

**Land is always at y = 0, and that is a rule rather than a simplification.**
`CombatPlane` resolves every round on one plane and the camera looks down at that plane from a
fixed height. A map with hills would break both quietly — a tank behind a rise would be
shootable through it, and a vehicle on a slope would tip. So *elevation* on this map is exactly
one thing: the 0.7 m bank down to the water. The design document's "terrain variety" is
delivered as **route** variety — where the water is, where you may cross it, and what stands
in the way — and not as height. If height ever becomes worth having, it costs a rewrite of
how combat resolves, not a level file field.

**Water drowns rather than blocks.** An invisible wall at the shoreline is a lie the player
cannot see, and it makes a dropped bridge cost a route and nothing else. Drowning is what the
original did, it makes the design document's amphibious jeep a real future upgrade rather than
a nicety, and it makes a blown bridge both a severed route *and* a kill zone. The price is
honest: a mis-steer at 22 m/s costs a whole vehicle.

**The causeway is deliberately the shortest route and deliberately open.** Bunkers are at
x = 0 and so is the causeway, so the fastest line home is also the most defended one, and the
bridges are what you pay a detour for to avoid it. Putting no cover on the causeway is the
same decision: a chokepoint you can hide in is not a chokepoint.

**The channel is 13 m wide at the bridgeheads, and that number is load-bearing.** A jeep
leaving a bank at full speed is a projectile until it is under the water line — 5.9 m of it,
measured against the real top speed, the real bank height and Unity's own gravity. Thirteen is
comfortably more than that, and there is a test that computes the whole thing rather than
asserting 13, so retuning the jeep or the bank fails *here* rather than in a playtest six
months later.

**Everything is placed in pairs rotated half a turn about the origin.** Not mirrored across
the channel — rotated — which is what M6's tower rule already was (green's real tower at −x,
brown's at +x). It means neither side has a shorter run, and there is a test that walks every
prop on the map looking for its opposite number.

**Which tower is real is still authored, and now it is authored somewhere a second map can
disagree.** That was the one thing about the decoy that grated in M6: a second match on this
map is a match where both players know the answer. It still is — but "where is it on this map"
is now a property of a file, so a second map is a second answer, and the fix for staleness is
new maps rather than a coin flip.

**The match is not part of the map.** It moved off the objective root and onto the session
object. If it hung off the map, loading a level would destroy it and take `Match.IsFinished`
with it, so an editor reloading a map mid-session would silently restart the match — and a
level file would be describing a rule. A level says a building stands here; how much a building
takes is still `StructureTuning.For`, and how far you can see a flag from is still `FlagRules`.
Two levels that disagreed about either would be two games wearing one name.

**Enums are written as names, never as numbers.** `JsonUtility` writes an enum as the integer
behind it, which is the one thing a level file may not contain: it cannot be read, cannot be
reviewed in a diff, and means something else the day somebody inserts a row into
`StructureKind`. So the file carries `"Kind": "Bridge"`, `LevelNames` does the conversion, and
it rejects digits on purpose — `Enum.TryParse` will happily accept `"7"` and hand back a member
that does not exist.

**`JsonUtility` rather than a JSON library.** The format was designed to be readable by it:
flat classes, public fields, no dictionaries, no inheritance, names instead of enums. The
dependency a nicer serialiser would add is not worth what it would buy on a file this shape.

**The sea is matte, which is the one deliberately unphysical material in the project.** Water
is glossier than dirt. But the first sea on this map rendered at (67, 81, 93) against land at
(78, 79, 75) — *identical* in brightness, different only in hue — and a gloss highlight under a
sun at 52° was most of the reason. From thirty-four metres up in half a screen, the coastline
all but vanished, on a map where crossing one costs a vehicle. Killing the specular took the
sea to (33, 39, 50): twice the contrast, and a bank you can see. Value contrast is what a
player reads at speed; hue is not.

---

## Gotchas

**A bridge is placed sunk, and the number comes from the asset.** `RF_Prop_Bridge` stands on
its piers with the deck 1.2 m above the origin, so the level file puts it at `y: -1.2` and the
deck comes out flush with the banks. Place one at ground level and you get a metre-high step
that looks like a perfectly good bridge and is a wall. Nothing in a level file can catch this —
it is a fact about an asset's dimensions — so `LevelLoadingTests` drives a tank onto one
instead. The asset spec now records it.

**`LevelLoader.Show` disables the old map before destroying it, and that is not tidiness.**
`Destroy` is deferred to the end of the frame, so without the `SetActive(false)` the outgoing
bunkers, towers and flags are still on their roll-calls while the incoming ones register, and
`TeamBunker.For` can hand a player a bunker that is about to stop existing — with their whole
roster stowed inside it.

**`LevelLoader` loads in `Awake`, and it has to.** `PlayerVehicleDriver` stows its roster in
`Start`, which needs `TeamBunker.For` to answer. M4 already made that a `Start` for exactly this
reason; the loader has to be one step earlier than the thing that was already careful.

**`LevelCatalogBuilder.Load` does not refresh anything.** It returns the existing asset if
there is one, so changing a colour in `GeneratedMaterials` and re-rendering shows the *old*
colour — the materials are only rebuilt by `Build`. Run **Build Level Catalog** after touching
a generated material, or the picture is a lie. This cost twenty minutes during M7.

**`WaterLine` walks the roll-call backwards.** Drowning a vehicle takes it off the roll-call
inside the loop, because `SelfDestruct` raises the same event being shot does. Forwards over a
shrinking list skips the vehicle after every one that dies — so a jeep and a tank that go in
together, one lives.

**A flag dropped in the water is unreachable, and it is meant to be.** The carrier drowns, the
flag stands where it went in — visible, since the staff is taller than the bank is deep — and
goes back to its tower on the usual twelve seconds. Nobody can contest it, because contesting
it means driving in after it. Pushing a carrier into the sea is therefore a way to *deny* a
capture rather than to fight over it. That is a real change to M6's drop rule on a map with
water and it is worth playing before deciding it is right.

**A test that writes into `persistentDataPath` has to clean up after itself.**
`LevelFormatTests` proves that a player's copy of a level shadows the shipped one, which means
writing a real file into a real folder that outlives the editor. A leftover would shadow a
shipped map for every later run — which is exactly the mechanism being tested.

**`FindObjectsSortMode` is deprecated in 6000.5.** `FindObjectsByType<T>()` with no argument
is the current call. The project builds with no warnings and intends to keep doing so.

**~~Pre-existing, and not fixed here:~~ fixed since.** Deploying a vehicle logged "Setting
linear velocity of a kinematic body is not supported" from `VehicleBay.Freeze`, which zeroed
velocity on a body that was already kinematic — the ordinary path, because a stowed vehicle is
already frozen when `Deploy` freezes it again. `Freeze` now only clears the velocities on a body
that is still simulated, and `BunkerTests.SendingAStowedVehicleBackOutSaysNothingToTheLog` parks
a jeep and sends it back out with a listener on the log. It was one line, and it was worth
forty of those messages a play-mode run.

**~~Still there, and it is the tests rather than the game:~~ fixed since.** `WaterTests`,
`FlagTests` and `DestructionTests` each build a vehicle with a kinematic rigidbody and leave its
`GroundVehicle` enabled, so `GroundVehicle.FixedUpdate` wrote a velocity onto it every fixed step
and logged the same message — about 235 a play-mode run. Disabling the controller was never the
fix: that is what takes a vehicle off `VehicleController.Live`, which is the roll-call `WaterLine`
walks and these tests rely on. `FixedUpdate` now returns while `Body.isKinematic`, for the same
reason `Freeze` does nothing to a body that is still frozen — there is no measured speed for
`SpeedAfterObstruction` to reconcile against and nowhere for the planned velocity to go, so the
whole step was work done on a vehicle physics is not moving. `Helicopter.FixedUpdate` had the
identical shape and took the identical guard; nothing else in the game writes to a rigidbody
outside those two, and `Teleport` is only ever reached after `Thaw`. The tests keep their
kinematic bodies and their enabled controllers, and both logs are now silent: **216 edit-mode and
96 play-mode tests pass with not one kinematic message between them.**

---

## Verified

Run from `C:\git\projects\IronFlag`, all on Unity 6000.5.9f1:

- The project compiles headless with no errors and **no warnings**.
- **213 edit-mode tests pass**, twenty-two more than M6 left behind. The new ones are in two
  groups. `LevelFormatTests` is the format's contract: a level survives a round trip prop for
  prop, every enum is written as a name and never as a number, a misspelled prop resolves to
  nothing rather than to something else, a digit is not a name, a level from a newer build is
  refused whole, every failure names its file, a player's own copy shadows the shipped one, and
  the output is pretty-printed because it lives in source control. `LevelDesignTests` reads the
  shipped map as a design: it validates clean, every prop has an opposite number, neither side
  has a shorter raid, no pair of towers can be opened by one round, each half has a fuel and
  an ammunition depot, the two bridges each stand in a narrows, nothing is parked on the
  objective, no jeep can jump the channel — and, the one that matters most, **removing the
  causeway disconnects the map**, which is what makes "the bunkers are joined by dry land" mean
  something. `SandboxWiringTests` gained five: the scene loads its map from a file, the baked
  map matches that file prop for prop, the sea is where the file puts it, the match is not part
  of the map, and no vehicle starts in the water.
- **92 play-mode tests pass**, ten more than M6 left behind. `WaterTests` is the
  drowning rule on its own — a ground vehicle is lost, one on the ground is not, a helicopter
  hovering over open water is fine because its floor is the land, a stowed vehicle cannot drown,
  and the sea takes a vehicle exactly once. `LevelLoadingTests` is the map: what comes up on the
  first frame is what the file says, loading again replaces the map rather than doubling it, the
  causeway carries a tank, a bridge deck is level with the banks it joins, and the channel is
  water.
- `Build Level Catalog`, `Build Vehicle Sandbox Scene`, `Render Level Overview` and the
  split-screen still all run clean, with no warnings from the level or the catalog.
- `m7-map.png` is the map from above; `m7-sandbox.png` is M6's staged raid played on it.

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -runTests -projectPath unity -testPlatform EditMode -testResults editmode.xml -logFile -
```

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -runTests -projectPath unity -testPlatform PlayMode -testResults playmode.xml -logFile -
```

**Not verified: whether the map is any good.** Nobody has played a match on it. Every number in
it is a first guess against a camera 34 m up, and the three most likely to be wrong are the
140 m between bunkers (a jeep covers it in under seven seconds of clear driving, so a raid may
still be too cheap), the 16 m causeway (wide enough to be a corridor rather than a duel, or
narrow enough to be a cork), and the 26 m channel (which may simply be too much dead space on a
map this size).

**Also not verified:** whether the coastline reads *in motion*. It has been measured in a still
and it is twice the contrast it was, but "can you see the bank in time at 22 m/s in half a
screen" is a question only playing answers — and getting it wrong costs a vehicle every time.

**Since resolved:** the reveal radius at 10 m was flagged here as the number M7 was most
likely to move. M7 did not move it — the tower rules change deleted it outright, and what a
flag costs to find is now a tower's hit points. See
[TOWER_RULES_NOTES.md](TOWER_RULES_NOTES.md).

---

## File map

| Path | What it is |
|---|---|
| `unity/Assets/StreamingAssets/Levels/iron-channel.json` | **The map.** Read this first |
| `unity/Assets/RF/Scripts/Levels/LevelDefinition.cs` | **The format**, and what it deliberately excludes |
| `unity/Assets/RF/Scripts/Levels/LevelBuilder.cs` | **Turns one into a world**, and knows no coordinates |
| `unity/Assets/RF/Scripts/Levels/LevelLoader.cs` | **The file is the truth**; the seam an editor stands on |
| `unity/Assets/RF/Scripts/Levels/LevelValidation.cs` | **What a broken map is**, including the deadlock rule |
| `unity/Assets/RF/Scripts/Levels/LevelLibrary.cs` | Where levels live, and which copy wins |
| `unity/Assets/RF/Scripts/Levels/LevelFile.cs` | JSON in, JSON out, every failure a sentence |
| `unity/Assets/RF/Scripts/Levels/LevelNames.cs` | Why the file says `Bridge` and not `4` |
| `unity/Assets/RF/Scripts/Levels/LevelCatalog.cs` | The prefabs a runtime build cannot look up |
| `unity/Assets/RF/Scripts/Levels/LevelStructurePrefab.cs` | One row of that catalog |
| `unity/Assets/RF/Scripts/Levels/LevelBounds.cs` | Three numbers: extent, water, and the land at y = 0 |
| `unity/Assets/RF/Scripts/Levels/LevelLand.cs` | One rectangle of dry ground |
| `unity/Assets/RF/Scripts/Levels/LevelBunker.cs` · `LevelTower.cs` · `LevelStructure.cs` | What a level places |
| `unity/Assets/RF/Scripts/Levels/WaterLine.cs` | **The sea**, and what it takes |
| `unity/Assets/RF/Scripts/Core/ModelPaint.cs` | The material-swap rule, now runtime |
| `unity/Assets/RF/Editor/Gameplay/LevelCatalogBuilder.cs` | Builds the catalog; run it after touching a material |
| `unity/Assets/RF/Editor/Gameplay/LevelPreview.cs` | **The map shot** |
| `unity/Assets/RF/Editor/Gameplay/VehicleSandboxScene.cs` | What is left: players, cameras, HUD, session |
| `unity/Assets/RF/Editor/ArtPipeline/GeneratedMaterials.cs` | Gained water and smoothness; delegates the paint rule |
| `unity/Assets/RF/Editor/ArtPipeline/CameraCapture.cs` | Gained a command-line value reader |
| `unity/Assets/RF/Tests/EditMode/LevelFormatTests.cs` | **The format's contract** |
| `unity/Assets/RF/Tests/EditMode/LevelDesignTests.cs` | **The map read as a design** |
| `unity/Assets/RF/Tests/EditMode/SandboxWiringTests.cs` | Gained the loader, the bake and the sea |
| `unity/Assets/RF/Tests/PlayMode/WaterTests.cs` | Who the sea takes |
| `unity/Assets/RF/Tests/PlayMode/LevelLoadingTests.cs` | **The map, driven on** |
| `unity/Assets/RF/Levels/LevelCatalog.asset` | Generated |
| `unity/Assets/RF/Scenes/Sandbox.unity` | Regenerated: a shell plus a baked copy of the map |
| `return-fire-homage-asset-spec.md` | Gained the bridge's origin rule |
| `m7-map.png` | **The map** |
| `m7-sandbox.png` | M6's raid, played on it |

---

## What M8 inherits

- **The in-game level editor is the obvious next thing, and nothing has to be built for it
  first.** Editing is `LevelLoader.Show` with a changed definition; saving is
  `LevelFile.TryWrite` into `LevelLibrary.UserFolder`; playing it is `LevelLoader.Load`. What
  is missing is only the UI: a palette (which is `LevelCatalog`'s rows), a way to drag a
  rectangle, and a way to place a prop. `LevelPreview`'s overhead camera is the view it wants.
- **`LevelValidation` is the editor's error panel already.** It returns sentences, in order,
  about one level. Nothing about it assumes a test is calling it.
- **A second map is now cheap and is the best way to test all of this.** The design tests are
  written against whatever level is loaded, not against `iron-channel` — point them at a new
  file and they check the new map.
- **The minimap M8 wants has a data source now.** `LevelLoader.Current` is the whole map, and
  `LevelLand` is a list of rectangles, which is about the cheapest thing there is to draw.
- **`Destructible.Collapsed` still has no listener**, and it now has something to say: dropping
  a bridge changes the map. Nothing scores it and nothing tells the other player.
- **Two numbers on this map are still guesses that only playing can settle**: what a tower
  costs to break open (340 hit points, five tank shells) and the twelve-second dropped-flag
  timer, which M6 called a formality on a 100 m map and which is a real decision on one with a
  channel in it.
