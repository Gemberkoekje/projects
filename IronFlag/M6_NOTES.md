# M6 — Flag & win conditions: what exists and why

> **Partly superseded.** The way a flag is *found* changed after M7: an intact tower now hides
> what it holds from everybody, breaking one open is the only way to learn and the only way to
> take it, and the ten-metre reveal radius no longer exists. Everything else here — the
> jeep-only rule, the drop timer, the capture, the HUD strip — still stands. See
> [TOWER_RULES_NOTES.md](TOWER_RULES_NOTES.md), which lists exactly which lines below are out
> of date.

**To understand this, start by reading `unity/Assets/RF/Scripts/Objective/Flag.cs`, then
`Objective/FlagRules.cs`, then `Objective/FlagTower.cs`, then `Objective/Match.cs`.** The
first is the milestone: five states, and the four transitions between them are the whole of
the design document's core loop. The second is every number and the one rule the game is
built around. The third is the decoy — why two identical pyramids are a mechanic rather than
scenery. The fourth is thirty lines that turn a delivered flag into a result.
`Editor/Gameplay/VehicleSandboxScene.cs` (`PlaceObjective`) is what puts four towers, two
flags and one match on the map.

This covers milestone **M6** from the design doc: the flag tower given a job, the jeep-only
pickup rule, and the capture/return win check. M5's destruction is in
[M5_NOTES.md](M5_NOTES.md), M4's bunker and supplies in [M4_NOTES.md](M4_NOTES.md), M3's
combat in [M3_NOTES.md](M3_NOTES.md), M2's split screen in [M2_NOTES.md](M2_NOTES.md), M1's
vehicles in [M1_NOTES.md](M1_NOTES.md), M0's scaffolding in
[SCAFFOLDING_NOTES.md](SCAFFOLDING_NOTES.md).

---

## How to see it

Open `unity/Assets/RF/Scenes/Sandbox.unity` and press Play. There are now **four** pyramids
on the map, two at the back of each half, and they are identical. Take the tank out and drive
into the far pair. At about ten metres one of them shows you a flag on its pole — or does not,
in which case the other one is the real one and you have just spent a trip finding that out.
That is the design document's decoy, and it is the only thing on this map you cannot learn by
looking.

Now go home, take the jeep, and drive to the flag. Nothing else can pick it up: park the tank
on top of it and the HUD tells you so. The jeep drives through and leaves with the flag on a
mast above the roll cage, visible from anywhere on the map — including to the player whose
flag it is, whose own strip has just turned red. Get it back to your own bunker, which is the
same place you refuel, and the match is over: both halves of the screen say who won and
neither vehicle answers the controls any more.

Get killed on the way instead and the flag stands where you died for twelve seconds. Anybody's
second jeep can take it on; if nobody does, it goes back to its tower.

Both HUD halves gained a strip at the top that is on whether you are choosing a vehicle or
driving one:

| Strip line | When |
|---|---|
| `THEIR FLAG  SEALED - BREAK A TOWER` | nobody has broken open the tower it is on (was `NOT FOUND`; see [TOWER_RULES_NOTES.md](TOWER_RULES_NOTES.md)) |
| `THEIR FLAG  ON ITS TOWER` | you have found it and left it there |
| `THEIR FLAG  ON YOUR MAST - GET HOME` | you are carrying it |
| `THEIR FLAG  ON THE GROUND - 7s` | it was dropped, and how long you have |
| `YOUR FLAG  STOLEN` | in the alarm colour, because it is the alarm |

The numbers everything above turns on:

| Rule | Value | Read against |
|---|---|---|
| Pickup radius | 4 m | a jeep is 4 m long, so this is "drive into it" |
| Reveal radius | 10 m | the camera sits 34 m back: scouting costs a trip, not a glance |
| Dropped flag returns after | 12 s | a wrecked jeep costs 4 s of repairs plus a 1.2 s ride out |
| Mast height | 1.4 m | above the roll cage, above `CombatPlane.ShootingHeight` |
| Flag height on a tower | 5.2 m | the foot of the pole the tower model already carries |

The generators still run headless:

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -quit -projectPath unity -executeMethod IronFlag.Editor.Gameplay.VehicleSandboxScene.RenderToFile -sandboxOutput ../m6-sandbox.png -logFile -
```

`m6-sandbox.png` is one moment of a raid seen from both sides at once: the brown jeep leaving
the green base with a green flag on its mast, its own HUD saying `GET HOME`, and the green
player standing in their bunker choosing a vehicle under a strip that says `YOUR FLAG STOLEN`.
As with M4 and M5, **none of it is in the saved scene** — `StageRaid` scouts a real tower and
puts the flag through `Flag.GiveTo`, which refuses anything that is not a hostile jeep, so the
flag is on that mast because the rules allowed it there.

---

## What was built

**A flag that looks for its own carrier.** Every other arrangement puts a component on all
four vehicles asking once a frame whether it is allowed to touch something, and three of the
four exist only to answer no. Instead `Flag` walks `VehicleController.OnTheField` — a new
static roll-call, the same one `TeamBunker` and `SupplyPoint` already keep — and the vehicles
know nothing about the objective at all. The roll-call is worth its own line: membership is
exactly "drivable", because `VehicleBay` disables the controller while a vehicle is stowed,
being repaired or riding the lift. A jeep in its bunker cannot pick anything up, and neither
can one halfway out of it, and nobody had to write either rule.

**One rule, in one place.** `FlagRules.CanCarry` is the design document's first pillar and it
is a single comparison in a file of its own, for the reason `Teams.IsHostile` is: the flag
asks it to decide whether to jump onto a vehicle, and the HUD asks it to decide whether to
explain why it did not. Written out at both call sites, the interesting version of the rule —
a damaged jeep that cannot carry, a second carrier unlocked by something — becomes two edits
that can disagree.

**A decoy that is a mechanic rather than two props.** `FlagTower` hides what it holds until a
*hostile* vehicle has been within ten metres. Any of the four can do the looking, which is
what the design document's roster table means by giving the other three "can find/reveal
flag, can't carry it": scouting in the tank is the cheap way to buy the answer, and the jeep
is the expensive way to guess. The two towers of a side stand 36 m apart, which is more than
twice the reveal radius, so one drive cannot confirm both — and there is a test that says so,
because that is a layout mistake nothing would look wrong about.

**The win condition, and the definition of "home" it shares with the fuel gauge.** A capture
is a carried flag standing on a supply point of the carrier's own side, which is the same
place `PlayerVehicleDriver.IsHome` means by "close enough to swap vehicles". That was
`SupplyPoint.ServingAt`, which answers with the first point of *any* team; it is now
`SupplyPoint.HomeFor`, which answers with your own side's. The two are the same answer until
two points overlap, and then the difference is a neutral fuel drum parked on a bunker
swallowing the only way to win a match. Both callers moved.

**A match that listens instead of looking.** `Match` subscribes to one static event,
`Flag.AnyCaptured`, and turns it into a winner and a `Team` whose flag went. Static rather
than per-flag because a match that subscribed to each flag in turn would have to be created
after both of them and would silently miss a third. Once it is over `Match.IsFinished` freezes
both flags and both players — whatever is on the field coasts to a stop, because the last
thing either player should be able to do after somebody has won is drive off and keep playing.

**A flag asset with three homes and one origin.** `RF_Prop_Flag` is 74 triangles: a staff, a
finial, a collar and a banner. The origin is at the foot of the staff because all three places
it stands — a tower's pole, the ground, a jeep's mast — want "put the bottom here". The banner
is the only `TeamTrim` on it, applied where the flag is placed rather than in the prefab,
because one prefab serves both sides and a flag never changes teams.

**A HUD strip that is on in the bunker as well as in the field.** The other two panels are
about a decision you are making right now and only one of them is ever up. Where the flags are
is different: it is the thing a player picks their *next* vehicle by. A stolen flag is a
reason to take the tank out, and a flag on the ground with four seconds left is a reason to
take nothing out and wait.

---

## Decisions worth knowing

**The flag is hidden, and that is what makes the tower asset's brief true.** The asset spec
demands the real tower and the decoy be "visually identical" and neutral-coloured. That is
free to satisfy and worth nothing if the flag on the real one is visible from across the map,
so a flag on an unscouted tower has its renderers off. The Blender module said as much before
any of this existed: "the flag itself is deliberately *not* part of this mesh… a decoy with no
flag on it would be readable from across the map."

**A scouted tower is scouted for everybody, including the side being raided.** There is one
world and two viewports of it. Hiding the flag per player would mean putting it on a render
layer per viewport, and the thing that buys is symmetry; the thing it costs is the defender's
only warning that somebody is standing in their base. The warning is worth more. It also
means the strip saying `YOUR FLAG STOLEN` is never the first a defender hears of it.

**Which tower is real is authored, not rolled.** The design document puts the decoy under *Map
Design*, and a tower that moved between matches could not be learned, defended or planned
around — it would be a coin flip wearing a mechanic's clothes. Green's flag is on the tower at
`-x` and brown's on the mirror of it at `+x`, so neither side has a shorter run, and there is
a test measuring both. The cost is honest: **a second match on this map is a match where both
players already know the answer.** Re-rolling per match is one line in `PlaceObjective` if
playing it says the decoy is worth more as a surprise than as terrain.

**A dropped flag comes home on a clock, not on a touch.** Driving over your own flag to return
it instantly would make "kill the carrier" and "kill the carrier *and hold the ground*" the
same play, and the second one is the interesting one. Twelve seconds is longer than the
raiding side's minimum cost of coming again — four seconds of repairs plus the ride out — so
killing the carrier never wins the flag back by itself.

**Only the carrier's own arrival captures.** Reaching the bunker you stole *from* does nothing,
which is the one way the win condition could be read backwards, and there is a test for it.

**The design document's secondary win condition is deliberately not implemented.** "Destroy
all enemy vehicles/base structures" cannot be satisfied by anything in the game today: a
side's roster is a fixed four that are always repaired and put back, and the bunker is the only
thing on the map that may not be shot down (a post-M7 rules change made the flag towers
destructible too — see [TOWER_RULES_NOTES.md](TOWER_RULES_NOTES.md)). A rule that can never
fire is a rule nobody can tell is broken. It needs either a finite roster or a destructible base
first, and both are design decisions rather than M6 work.

**The flag rides its carrier rather than being parented to it.** Parenting would put the flag
inside the vehicle's own hierarchy, where `VehicleBay` hides it along with the hull and drags
it back to the bunker with the wreck — a flag that goes home with the jeep that stole it,
which is one metre from an accidental capture.

**Nothing may shoot the objective.** The towers join the bunkers as the two things on the map
without a `Destructible`, and the flag prefab has no collider at all: a flag you could bump
into would let a defender park on their own and physically shove a raider off it, which is a
rule nobody wrote and nobody could find. The asset spec's claim that the tower "needs
`_Intact`/`_Damaged`/`_Destroyed`" is still true of the *pipeline* — all three are exported
— but only the intact one is ever shown. The spec now says so, and so does the comment in
`StructureKind` that used to claim the opposite.

**`FlagRules` is a static class of constants rather than a `For(kind)` table.** Every other
family of numbers in this project is a table read against itself in one diff. There is exactly
one flag, and a table with one row is a table nobody can balance.

---

## Gotchas

**The drop position has to be recorded while the carrier is alive.** By the time
`VehicleHealth.Destroyed` reaches anything, `VehicleBay` has already hidden the hull and moved
it inside its own bunker — subscribing to the event and reading `carrier.transform.position`
plants the flag in the raider's base, one metre from a capture. `Flag` records the carry
position every frame instead and drops there, which also means it does not care *why* the
carrier went away: shot, scuttled or parked all land in the same branch.

**`Flag` runs in `LateUpdate`, not `Update`.** A carried flag is placed above a vehicle that
has already moved this frame. In `Update` it trails a frame behind, which at the jeep's
twenty-two metres a second is most of a metre of daylight between the mast and the vehicle it
is supposedly bolted to.

**`OnEnable`/`OnDisable` on `VehicleController` are `protected virtual`, and they have to be.**
Unity calls only the most derived declaration, so a subclass that declared its own without
calling back would silently take every vehicle of that type off `OnTheField` — flags and
towers would stop seeing it, and nothing else in the game would change. `Awake` on the same
class already carries this shape for the same reason.

**`ExecuteAlways` plus an `Update` is a state machine ticking in the editor.** `Flag`,
`FlagTower` and `Match` are `ExecuteAlways` only so they register themselves for the
command-line still, exactly as `TeamBunker` does. All three guard on `Application.isPlaying`,
because without it opening the saved scene walks it through a match nobody asked for — and
the scene would then be *saved* mid-raid.

**`Match.IsFinished` is a static, and a play-mode test that leaves one behind poisons every
test after it.** `FlagTests` tears down with `DestroyImmediate` rather than `Destroy` for
exactly this: deferred destruction leaves a won match registered while the next test starts,
and the next test then sits there doing nothing and blames the flag.

**`SplitScreenTests` was leaving the whole sandbox loaded, and that is fixed here.** It is the
only test in the project that loads a scene, and `LoadSceneMode.Single` never unloads. The
symptom was three classes away: `SupplyTests` parks a tank at thirty metres, the sandbox's
fuel depot stands six metres from there, and the test asserting that driving costs more than
idling failed because the tank was being refuelled as fast as it burned. It now creates an
empty scene and unloads the sandbox in a `[UnityTearDown]`. Anything that loads a scene in a
later milestone owes the same.

**The flag's banner is a plain vertical rectangle, deliberately.** The camera looks down at
58°, so a vertical face still shows a bit over half its area — plenty. A flag folded or angled
to catch a top-down view reads as a windvane from the side, and the side is where the team
colour has to be legible.

**Both towers of a side must be more than twice the reveal radius apart.** They are 36 m
against a 10 m radius today. Put them closer in M7 and one drive confirms both, the decoy
becomes a formality, and nothing about the scene looks wrong. There is a test.

---

## Verified

Run from `C:\git\projects\IronFlag`, all on Unity 6000.5.9f1:

- The project compiles headless with no errors and **no warnings**.
- **191 edit-mode tests pass**, eighteen more than M5 left behind. The new suite reads the
  rulebook against the rest of the game — only the jeep carries, you always learn what a tower
  holds before you can reach it, scouting costs a trip measured against the camera's own
  distance, the mast flies above the plane rounds are resolved on and below its ceiling, and a
  flag stands higher on a tower than on a jeep. Alongside them: the flag and tower prefabs
  carrying the right components, the flag carrying no collider, the tower carrying colliders
  and no `Destructible`, both `.glb` files on disk, a fresh flag starting on its tower, an
  unscouted tower hiding it, and a match refusing to be won twice. Six more in the sandbox
  suite: two towers a side with exactly one real, a pair too far apart to scout from one spot,
  one flag a side standing on its own side's real tower, both sides having the same distance
  to run, one match in the scene, and a dropped flag outlasting the raider's trip back to the
  bunker — read off the real prefabs rather than restated.
- **82 play-mode tests pass**, thirteen more than M5 left behind, all of them about what only
  exists once vehicles are driving: a tower that reveals itself because something came close
  and stays hidden from its own side, a jeep taking a flag and carrying it on an unparented
  mast, all three of the other vehicles driving over it and leaving it there, a side unable to
  steal its own, a decoy that has nothing on it however hard it is looked at, a flag dropping
  where its carrier died rather than where the wreck went, a dropped flag going home on its own
  and a second jeep taking it before it does, a capture at the right bunker and no capture at
  the wrong one, a depot parked on a bunker not swallowing the win, and a second flag that
  stops mattering the moment the match is decided.
- `Build Objective Prefabs`, `Build Destructible Prefabs`, `Build Combat Prefabs`, `Build
  Vehicle Prefabs` and `Build Vehicle Sandbox Scene` all run clean.
- `VehicleSandboxScene.RenderToFile` renders the staged raid — see `m6-sandbox.png`, against
  `m5-sandbox.png` for the same two viewports before there was anything to win.

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -runTests -projectPath unity -testPlatform EditMode -testResults editmode.xml -logFile -
```

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -runTests -projectPath unity -testPlatform PlayMode -testResults playmode.xml -logFile -
```

**Not verified:** whether any of it is fun, and two numbers in particular. The reveal radius
at ten metres is a guess at what "commit to the trip" feels like from a camera thirty-four
metres up; too small and scouting is suicide, too large and the decoy is free to see through.
The twelve-second return is generous on *this* map — a jeep crosses the hundred metres between
the bunkers in under five seconds, so almost any drop can be reached in time — which makes it
a formality until M7 draws a map with water, bridges and a route that is not a straight line.

**Also not verified:** whether the flag reads at speed. It has been looked at one frame at a
time and from a still; a metre of team colour two and a half metres over a jeep doing
twenty-two metres a second in half a screen is exactly the kind of thing that turns out to be
either invisible or the only thing you can see.

**And still open from M5:** nobody has yet had to choose between shooting a building and
shooting the vehicle behind it. M6 adds the reason to care — the building is now between
somebody and a flag.

---

## File map

| Path | What it is |
|---|---|
| `unity/Assets/RF/Scripts/Objective/Flag.cs` | **The milestone.** Read this first |
| `unity/Assets/RF/Scripts/Objective/FlagRules.cs` | **The one rule, and every number** |
| `unity/Assets/RF/Scripts/Objective/FlagState.cs` | The five states, in the order they happen |
| `unity/Assets/RF/Scripts/Objective/FlagTower.cs` | **The decoy**, and what scouting costs |
| `unity/Assets/RF/Scripts/Objective/Match.cs` | Who won, and why there is no second condition |
| `unity/Assets/RF/Scripts/Vehicles/VehicleController.cs` | Gained `OnTheField`, the roll-call |
| `unity/Assets/RF/Scripts/Supply/SupplyPoint.cs` | Gained `HomeFor` — one definition of home |
| `unity/Assets/RF/Scripts/Players/PlayerVehicleDriver.cs` | Stops answering the controls once won |
| `unity/Assets/RF/Scripts/UI/PlayerHud.cs` | Gained the flag strip and the result banner |
| `unity/Assets/RF/Editor/Gameplay/ObjectivePrefabBuilder.cs` | **Flag and tower prefabs** |
| `unity/Assets/RF/Editor/Gameplay/VehicleSandboxScene.cs` | `PlaceObjective`; stages a raid for the still |
| `unity/Assets/RF/Tests/EditMode/ObjectiveRosterTests.cs` | The rulebook read against the game |
| `unity/Assets/RF/Tests/EditMode/SandboxWiringTests.cs` | Gained the tower, flag and match checks |
| `unity/Assets/RF/Tests/PlayMode/FlagTests.cs` | **The raid, end to end** |
| `unity/Assets/RF/Tests/PlayMode/SplitScreenTests.cs` | Now unloads the sandbox behind itself |
| `blender/assets/prop_flag.py` | **The flag**, 74 triangles |
| `unity/Assets/RF/Art/Models/RF_Prop_Flag.glb` | Generated |
| `unity/Assets/RF/Prefabs/Props/RF_Prop_Flag.prefab` | Model plus one component, no collider |
| `unity/Assets/RF/Prefabs/Structures/RF_Structure_FlagTower.prefab` | Model, colliders, one component |
| `unity/Assets/RF/Scenes/Sandbox.unity` | Regenerated. Four towers, two flags, one match |
| `return-fire-homage-asset-spec.md` | Gained the flag rule; corrected the tower's states |
| `m6-sandbox.png` | The raid, from both sides at once |

---

## What M7 inherits

- **`PlaceObjective` is the whole of the objective's placement**, and it is four positions and
  a mirror. M7's map builder wants the same four calls with real coordinates, and the tests
  that check the mirror and the tower spacing will keep it honest.
- **The reveal radius is the one number in `FlagRules` that is a level-design decision.** Ten
  metres is right for open ground. A tower behind a wall, on an island, or across a bridge is
  a different question, and it is the number M7 is most likely to move.
- **The twelve-second return timer only becomes a distance once the map has one.** On a real
  map with a water crossing, where a flag is dropped starts deciding who gets it.
- **`Destructible.Collapsed` still has no listener.** The depots being destroyable is now
  worth something — a raid needs fuel — but nothing scores it. That is M8's, along with the
  minimap, which is where "which of those two pyramids did I already check" wants to live.
- **Nothing yet ends a match except a capture.** If a secondary win condition is wanted, it
  needs a finite roster or a destructible base first; see the decision above.
