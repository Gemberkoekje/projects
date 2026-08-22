# M5 — Destruction: what exists and why

**To understand this, start by reading `unity/Assets/RF/Scripts/Destruction/Destructible.cs`,
then `Combat/IDamageable.cs`, then `Destruction/StructureTuning.cs`, then
`Destruction/DebrisBurst.cs`.** The first is the whole milestone: a hit point pool, three
models, and the moment it swaps from one to the next. The second is the reason a round did
not have to learn what a building is. The third is the six rows anybody balancing this will
read, and the fourth is the mess it all makes.
`Editor/Gameplay/DestructiblePrefabBuilder.cs` is what puts three exported `.glb` files back
together into one thing that can be shot.

This covers milestone **M5** from the design doc: state-swap destruction for the props and
buildings, with a debris burst on each transition. M4's bunker and supplies are in
[M4_NOTES.md](M4_NOTES.md), M3's combat in [M3_NOTES.md](M3_NOTES.md), M2's split screen in
[M2_NOTES.md](M2_NOTES.md), M1's vehicles in [M1_NOTES.md](M1_NOTES.md), M0's scaffolding in
[SCAFFOLDING_NOTES.md](SCAFFOLDING_NOTES.md).

---

## How to see it

Open `unity/Assets/RF/Scenes/Sandbox.unity` and press Play, take out the tank, and shoot the
building in your half. Seven shells: the roof caves in on the fourth and the walls come down
on the seventh, and each of those two moments throws a handful of charred cubes that arc out,
fall, and are gone inside a second. Nothing else about the controls changed.

Then shoot at something standing behind it. While the building is up your shells stop on it;
once it is rubble they go straight over and hit what is behind. That is the point of the
whole milestone — cover you can remove is the tactical puzzle the design document opens with.

Three trees is a quarter of the jeep's whole load, a depot is four shells, and a bridge is ten.
What each vehicle has to spend:

| Structure | Hit points | Grenades (jeep) | Shells (tank) | Rockets (ASV) | Chaingun (heli) |
|---|---|---|---|---|---|
| Tree | 40 | 2 | 2 | **1** | 10 · 1.3 s |
| Depot (either) | 130 | 6 | 4 | 3 | 33 · 4.1 s |
| Building A | 220 | 10 | 7 | 4 | 55 · 6.9 s |
| Building B | 260 | 12 | 8 | 5 | 65 · 8.1 s |
| Bridge | 320 | 15 | 10 | 6 | 80 · 10.0 s |

The generators still run headless:

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -quit -projectPath unity -executeMethod IronFlag.Editor.Gameplay.VehicleSandboxScene.RenderToFile -sandboxOutput ../m5-sandbox.png -logFile -
```

`m5-sandbox.png` is the same staged moment M4's still froze, with the scenery around the
fight shot up: two felled trees and a building with its roof caved in. As before, **none of it
is in the scene** — `StageRubble` puts real damage through `Destructible.TakeDamage` on
everything within 24 m of the staged tank, so a still that looks right is evidence about the
game rather than about the staging. The debris is not in the shot: it lasts under a second,
and nothing animates in a scene nobody is playing.

---

## What was built

**One interface, and a round that never learned what a building is.** M3's
`Projectile` asked `VehicleHealth` three questions — may I hit this, is it already wrecked,
take this much off. M5 adds a second kind of target that answers the same three, so the three
became `IDamageable` rather than a second branch in the round. `VehicleHealth` implements it
unchanged; `Destructible` is the second implementer. The alternative — teaching the projectile
about walls — needs a third branch for the flag tower and a fourth for whatever M7 puts on the
map.

**The state is the model.** Each of `Intact`, `Damaged` and `Destroyed` is a child object
carrying that `.glb` and its own mesh colliders, and swapping states is turning one on and the
other two off. Nothing is scaled, faded or reshaped, so what a player sees and what a vehicle
drives into can never disagree — a rubble field is a different shape from the building that
stood there, and it collides like one.

**A prefab per destructible, assembled from three exported files.** The asset spec exports
each state as its own `.glb`, "not a modifier or hidden-state trick".
`DestructiblePrefabBuilder` is what puts them back together, so a re-exported building is a
rebuilt prefab rather than three props somebody has to keep in step by hand.

**Six rows of numbers, read against the four guns.** `StructureTuning.For` is the same shape
as `VehicleTuning.For` and `WeaponTuning.For` and is balanced the same way — by reading the
rows against each other in one diff. Hit points are in the same unit as a vehicle's, which is
what makes the table above possible.

**A debris burst on each transition.** Ten cubes on closed-form ballistic arcs, fanned at the
golden angle so no two follow the same path, thrown from the middle of what broke and shrinking
away after two thirds of a second. Not a particle system and not rigidbodies: one is an asset
nobody can review in a diff and the other is a physics bill for something that is over in a
second. `DebrisBurst.Offset` and `.Scale` are static and side-effect free, like
`Explosion.Scale` before them, so where a chunk is at a given moment is something a test can
read rather than watch.

**Depots that can be taken away.** The design document calls the depots
"destroyable/contestable", and that is now literally true: flattening one switches its
`SupplyPoint` off, and the other side has to drive further to refuel. Nothing in `SupplyPoint`
changed — M4 left it as a component rather than as scenery, and this is the thing that was
waiting for.

**Rubble is not cover.** A round passes through anything that is already destroyed. A building
that went on stopping shells after it had been knocked down would make knocking it down
pointless, and opening a firing lane is most of what destruction is for.

---

## Decisions worth knowing

**A building belongs to nobody, so everybody can shoot it.** `Destructible.Team` is
`Team.None` and is not a serialized field, because there is no such thing as a green
building: a side's own ground is the bunker, and the bunker is not destructible. It falls
straight out of `Teams.IsHostile`, which already says anything on no side is fair game for
everybody.

**The bunker and the flag tower were the two exceptions, and they were exceptions for the same
reason.** A bunker is where a side lives and a tower is what a match is won at; either one
being removable would let a player end a match by deleting it. The asset spec said so at the
time by giving the tower no destruction states, and there was a test that said it in the scene.
*A post-M7 rules change made the flag tower destructible on purpose — breaking one open is now
how a raider learns whether it holds the flag. The bunker is the only exception left. See
[TOWER_RULES_NOTES.md](TOWER_RULES_NOTES.md).*

**Rubble stops being cover for fire, but not for driving.** A destroyed building is knee-high
stub walls and a slab; a vehicle still bumps into it. Making it drivable would mean either
throwing away the colliders — a vehicle driving through visible geometry — or giving the
ground vehicles the ability to climb, which is a terrain question and belongs to M7. Fire and
movement disagreeing here is deliberate and is the smaller of the two wrongs.

**The damaged mesh comes in at half the pool.** The model changes exactly when the structure
has taken half of what it can, so a player can read how much more a wall wants without a
health bar floating over every building.

**The bridge has two states, not three.** The asset spec gives it `_Intact` and `_Destroyed`
only, and that is right: a bridge is either crossable or it is not. `Destructible` treats a
missing damaged model as a structure that stays whole until it is rubble, rather than as an
authoring error.

**A depot is softer than the building next to it, and both depots hold the same.** A depot is
a target worth a detour, so the reward has to be reachable inside one sortie; and neither
side's fuel should be easier to take away than their ammunition.

**Nothing explodes when a building falls.** The design document asks for debris on each
transition and nothing else; an explosion is what a *vehicle* death looks like, and reusing it
would make a collapsing wall read as a kill. Screen shake and the rest of the juice are M8's.

**Debris is deterministic.** A burst that is different every time cannot be photographed by
the command-line still or asserted on in a test. The fan differs per chunk in bearing, in
distance and in height, which is enough to read as an explosion from thirty-four metres up.

**`Restore` exists, and nothing in the game calls it.** Scenery does not repair — a building
that came back would undo the one permanent change a player can make to the map. It is there
because a test that knocks something down wants to stand it up again, and because M7's map
builder will want it.

**Structures are not marked static.** Everything else in the scenery is; an object whose
renderers are turned on and off is not, and telling Unity otherwise is how a batched building
stays visible after it has been flattened.

---

## Gotchas

**`Instantiate` copies the inactive flag, and a VFX prefab built in a scene is inactive.**
`DebrisBurst.Spawn` has to `SetActive(true)` on what it just created — exactly the same trap
`Projectile.Fire` already carried a comment about. Found by a test that counted the bursts and
got none: the object existed, was never enabled, never ran `Update`, and would have sat there
for the rest of the match.

**Unity's fake-null does not survive an interface reference.** `IDamageable` is not a
`UnityEngine.Object`, so `target != null` on one is a plain reference check rather than Unity's
lifetime-aware operator — a component that has been `Destroy`ed would read as alive and throw
on the next call. It cannot happen today: a round is created, resolved and destroyed inside one
`FixedUpdate`, and nothing in the game `Destroy`s a hull or a structure mid-frame (a wreck is
hidden, and rubble stays where it is). The day something does, every `IDamageable` held across
a frame needs an `is Object host && host != null` guard.

**A nested prefab instance cannot have components added to its children.** The state models
have to be unpacked when the destructible prefab is assembled, or none of them can be given a
collider.

**`Awake` does not run outside play mode, and the still knocks buildings down.** `Destructible`
brings itself up whole lazily on the first question anybody asks it, the same way `VehicleBay`
does and for the same reason.

**The debris floor is in the burst's own space.** A burst is thrown from the middle of what
broke, which for a building is metres above the ground, so the chunks' local zero is not the
ground and clamping them at it leaves them resting in mid-air. The ground is taken to be world
`y = 0`, which is what the map is until M7 puts any height on it.

**The debris origin is measured before the swap, not after.** Taking the middle of the model
that is showing once the state has changed throws a building's worth of masonry out of a
knee-high rubble pile.

**The blast and sweep buffers are 32 colliders, and scenery is made of mesh colliders.** Every
mesh in an active state is one entry, so a rocket going off inside a copse spends more of the
buffer than one going off in the open. It has not overflowed, and nothing today gets close —
but adding much denser scenery in M7 is what would first make splash damage quietly go missing.

**A material has to join the generated set, not be created next to it.** `RF_Debris` is a row
in `GeneratedMaterials.MaterialSet` for the reason that file already documents: recreating a
material hands out a fresh GUID and unbinds every prefab that referenced the old one.

**There are two bridges standing on dry land in the sandbox.** There is no water until M7 draws
the map. They are there because the toughest destructible in the game should be in a scene
somebody can actually shoot at.

---

## Verified

Run from `C:\git\projects\IronFlag`, all on Unity 6000.5.9f1:

- The project compiles headless with no errors and **no warnings**.
- **173 edit-mode tests pass**, sixteen more than M4 left behind. The new roster suite reads
  the six structures against the four guns — cover tougher than the vehicles hiding behind it,
  a tree worth a couple of grenades and not one, every structure inside one full load of the
  biggest gun in the game and none of them inside a single round, a depot softer than the
  building beside it, and the two depots equal. Alongside them: the state machine at every
  threshold including a structure with no middle mesh, the debris arc going up before it comes
  down and no two chunks sharing a path, a prefab per destructible carrying its states, its
  numbers and a collider in every state, the debris prefab carrying chunks and no colliders,
  and every `.glb` the asset spec promises being on disk. Three more in the sandbox suite: all
  fourteen pieces of scenery shootable and stamped from the table, every depot standing on
  something that can be destroyed, and only the bunkers indestructible (the flag towers lost
  that exception in a later rules change — see [TOWER_RULES_NOTES.md](TOWER_RULES_NOTES.md)).
- **69 play-mode tests pass**, ten more than M4 left behind, all of them about what only
  exists once a round is in the air: a building walking through all three models as it is shot
  up, a real shell taking exactly what the table says off a wall, both sides able to shoot the
  same building, rubble absorbing nothing and collapsing only once, a firing lane that opens
  when the building in it comes down, a flattened depot that stops refuelling people and a
  rebuilt one that starts again, debris on each transition and none for a hit that changes
  nothing, and exactly one model showing at every point in the pool.
- `Build Destructible Prefabs`, `Build Combat Prefabs` and `Build Vehicle Sandbox Scene` all
  run clean, and the sandbox wiring tests are what check the result.
- `VehicleSandboxScene.RenderToFile` renders the staged fight with two felled trees and a
  caved-in building — see `m5-sandbox.png`, against `m4-sandbox.png` for the same shot with
  everything still standing.

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -runTests -projectPath unity -testPlatform EditMode -testResults editmode.xml -logFile -
```

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -runTests -projectPath unity -testPlatform PlayMode -testResults playmode.xml -logFile -
```

**Still not verified:** whether any of the numbers are any good. Nobody has yet had to decide
between shooting a building and shooting the vehicle behind it with the ammunition they were
carrying, which is the decision the whole table exists to create. The two most likely to be
wrong are the tree at 40 — small enough that a stray rocket clears one, which may make cover
too easy to lose — and the depot at 130, because a depot that a single sortie can remove may
turn "destroy their supply" into the obvious opening move rather than a choice.

**Also not verified by playing it:** how the debris reads at speed. It has been looked at one
frame at a time and in a test, never at sixty frames a second from a moving camera, and ten
cubes may turn out to be either invisible or confetti.

---

## File map

| Path | What it is |
|---|---|
| `unity/Assets/RF/Scripts/Destruction/Destructible.cs` | **The milestone.** Read this first |
| `unity/Assets/RF/Scripts/Destruction/DestructionState.cs` | The three states, in the order they happen |
| `unity/Assets/RF/Scripts/Destruction/StructureKind.cs` | What can be knocked down |
| `unity/Assets/RF/Scripts/Destruction/StructureTuning.cs` | **The six rows of numbers** |
| `unity/Assets/RF/Scripts/Destruction/DebrisBurst.cs` | **The mess**, in closed form |
| `unity/Assets/RF/Scripts/Combat/IDamageable.cs` | **Why a round needs no branch** |
| `unity/Assets/RF/Scripts/Combat/VehicleHealth.cs` | Now the first of two `IDamageable`s |
| `unity/Assets/RF/Scripts/Combat/Projectile.cs` | Hits anything damageable; passes through rubble |
| `unity/Assets/RF/Editor/Gameplay/DestructiblePrefabBuilder.cs` | **Three `.glb` files into one prefab** |
| `unity/Assets/RF/Editor/Gameplay/CombatPrefabBuilder.cs` | Gained the debris prefab |
| `unity/Assets/RF/Editor/ArtPipeline/GeneratedMaterials.cs` | Gained the charred debris material |
| `unity/Assets/RF/Editor/Gameplay/VehicleSandboxScene.cs` | Places destructibles; stages rubble for the still |
| `unity/Assets/RF/Tests/EditMode/StructureRosterTests.cs` | The table read against the guns |
| `unity/Assets/RF/Tests/EditMode/SandboxWiringTests.cs` | Gained the scenery and depot checks |
| `unity/Assets/RF/Tests/PlayMode/DestructionTests.cs` | **Knocking things down, end to end** |
| `unity/Assets/RF/Prefabs/Props/RF_Prop_*.prefab` | Tree, both buildings, bridge |
| `unity/Assets/RF/Prefabs/Structures/RF_Structure_Depot*.prefab` | Both depots |
| `unity/Assets/RF/Prefabs/Combat/RF_Debris.prefab` | Ten cubes |
| `unity/Assets/RF/Art/Materials/RF_Debris.mat` | Generated, charred dark grey |
| `unity/Assets/RF/Scenes/Sandbox.unity` | Regenerated. Fourteen destructibles |
| `m5-sandbox.png` | The staged fight, with the scenery around it shot up |

---

## What M6 inherits

- **`Destructible.Collapsed` is the structure's version of `VehicleHealth.Destroyed`.** Both
  say "this thing is gone" and both are the event everything else hangs off. A mission, a
  score or a win condition that cares about a building coming down subscribes to it.
- **The flag towers are already placed on their own**, by `PlaceFlagTowers`, and are the only
  scenery deliberately left indestructible. Hanging the flag off one is a change to that
  method and nothing else. *This prediction did not hold: a post-M7 rules change made the tower
  itself destructible — breaking it open is how the flag is found. See
  [TOWER_RULES_NOTES.md](TOWER_RULES_NOTES.md).*
- **`IDamageable` is where anything new that can be shot plugs in.** If M6 wants a flag pole,
  a generator or a radar dish to be shootable, it implements three members and every gun in
  the game already works on it.
- **The depots being destroyable is a strategic layer nobody is using yet.** Nothing rewards
  taking one away, because nothing yet counts anything. M6 brings the first thing that does.
