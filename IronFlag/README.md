# IronFlag

A split-screen vehicular capture-the-flag game — a homage to *Return Fire*, where
only the fastest and weakest vehicle can carry the flag.

**Start here:** [return-fire-homage-design-doc.md](return-fire-homage-design-doc.md)
for what the game is, then [return-fire-homage-asset-spec.md](return-fire-homage-asset-spec.md)
for how every model must be built. [SCAFFOLDING_NOTES.md](SCAFFOLDING_NOTES.md) records what
the project setup does and why, [M1_NOTES.md](M1_NOTES.md) does the same for the vehicles and
the camera, [M2_NOTES.md](M2_NOTES.md) for the split screen and its two players,
[M3_NOTES.md](M3_NOTES.md) for the weapons and the damage, [M4_NOTES.md](M4_NOTES.md)
for the bunker you choose from and the fuel and ammunition you leave it with, [M5_NOTES.md](M5_NOTES.md) for the buildings you can knock down,
[M6_NOTES.md](M6_NOTES.md) for the flag and the match you win with it,
[M7_NOTES.md](M7_NOTES.md) for the map itself — which is a file you can edit — and
[M8_EDITOR_NOTES.md](M8_EDITOR_NOTES.md) for the level editor you can edit it in, and
[SURFACES_NOTES.md](SURFACES_NOTES.md) for what the ground is made of.
[MAIN_MENU_NOTES.md](MAIN_MENU_NOTES.md) covers the screen the game now starts on.
[TOWER_RULES_NOTES.md](TOWER_RULES_NOTES.md) explains why you have to shell a pyramid before
you can rob it, [RESERVES_NOTES.md](RESERVES_NOTES.md) how many vehicles you get to lose
before you have lost, [TURRETS_NOTES.md](TURRETS_NOTES.md) why an emplacement watches you
for twelve metres before it starts shooting, and [SOLO_NOTES.md](SOLO_NOTES.md) how to play
on your own.

---

## Layout

```
IronFlag/
├── return-fire-homage-design-doc.md    game design + M0..M8 milestone plan
├── return-fire-homage-asset-spec.md    per-asset dimensions, palette, naming
├── SCAFFOLDING_NOTES.md                what M0 set up, and the gotchas in it
├── blender/                            asset generation (Python, no .blend files)
│   ├── build.ps1 / build.sh            run a build
│   ├── build.py                        headless entry point
│   ├── rf/                             shared primitive/material/export helpers
│   └── assets/                         one module per asset family
└── unity/                              the Unity 6 (URP) project
    ├── Assets/StreamingAssets/Levels/  the maps, as JSON. Edit these
    └── Assets/RF/                      everything else lives under here
        ├── Art/Models/                 .glb output from blender/ (committed)
        ├── Editor/ArtPipeline/         the "rebuild art" editor menu
        ├── Editor/Gameplay/            vehicle, combat, destructible and scene generators
        ├── Input/                      input actions
        ├── Levels/                     the catalog: what a map is built out of
        ├── Scripts/Levels/             the level format, its loader and its rules
        ├── Scripts/Editing/            the in-game level editor
        ├── Scripts/Menu/               the main menu, its settings and the ways back to it
        ├── Prefabs/ · Scenes/ · Scripts/ · Tests/
```

There are three scenes. `Scenes/MainMenu.unity` is where the game starts,
`Scenes/Sandbox.unity` is the game and `Scenes/LevelEditor.unity` is where maps are made. All
three are generated, and they are in that order in the build list — the menu is index 0, which
is what makes a built copy start there. The menu reaches the other two; the editor's **PLAY
THIS MAP** button and the game's `F1` move between those; and `ESC` from a match or the
editor's **MENU** button come back.

`Assets/StreamingAssets/` sits outside `Assets/RF/` because Unity requires it there. It is
the one exception to "all project content lives under `Assets/RF/`".

## Prerequisites

| Tool | Version used | Notes |
|---|---|---|
| Unity | 6000.5.9f1 | Pinned in `unity/ProjectSettings/ProjectVersion.txt` |
| Blender | 5.2 LTS | Found automatically; override with `IRONFLAG_BLENDER` |

## Getting started

Open `unity/` from Unity Hub. The first open resolves packages (needs network) and
generates `Library/`, `.meta` files for the folders committed here, and
`Packages/packages-lock.json` — **commit those `.meta` files and the lock file**, they
are what keeps asset references stable across machines.

To rebuild the art:

```bash
./blender/build.ps1
```

or, from inside the editor, **Tools > IronFlag > Rebuild All Art from Blender**.

See [blender/README.md](blender/README.md) for the asset pipeline and the list of
models still to build.

## Playing it

Open `unity/Assets/RF/Scenes/MainMenu.unity` and press Play. **PLAY** lists every map in both
level folders — the ones that shipped and the ones you have drawn — with what each one is
underneath its name; pick one and the match starts. **LEVEL EDITOR** goes straight to the
editor and **SETTINGS** holds the window and the detail level, which are remembered between
sessions. `ESC` backs out of either panel, and from inside a match it leaves the match: once to
ask, again to go. In the editor the way back is the **MENU** button, which asks twice if you
have unsaved work. (`Scenes/Sandbox.unity` still opens straight into a match if you would
rather skip the menu.)

The screen splits in two: green on
top, brown below — unless the map you picked is a **one-player** map, in which case there is
one of you, you have the whole screen, and the enemy is a field of flag towers behind their
own emplacements with nobody driving. The menu marks those maps `1 PLAYER`; the shipped one is
`IRON WATCH`, and the level editor generates more. See [SOLO_NOTES.md](SOLO_NOTES.md).

Either way you start **inside your bunker**, looking at a panel listing your
four vehicles — pick one and send it out, and it rides up the lift (or lifts off the roof pad,
if you picked the helicopter) before you can drive it.

Player one is on the keyboard and mouse — `WASD` drives, the mouse aims, the left button
fires, `Q` and `E` move down the roster, `F` deploys, `Space` and `Left Ctrl` fly the
helicopter up and down. Player two picks up a gamepad: left stick drives, right stick aims,
right trigger fires, the shoulder buttons move down the roster, `X` deploys, `A` and `B` climb
and descend. Plug a second pad in and both players move onto pads. Full details are in
[M2_NOTES.md](M2_NOTES.md).

You only ever have one vehicle out. To swap, **hold** the deploy button: standing on your own
bunker that parks it and puts you back in front of the roster with nothing spent, and anywhere
else it blows the vehicle up — which is also the way out when you have run dry in the middle
of the map. A wreck spends four seconds being repaired before it can be picked again, and it
costs you one of that vehicle for good.

Because **you only have so many**. The count beside each row of the roster is how many of that
vehicle your side has left — eight jeeps, three tanks, three ASVs and three helicopters to
start with, and whatever the map says instead. Anything destroyed comes off it and nothing
puts one back: not the bunker, which repairs and refuels everything else, and not time. Run out
of a vehicle and its row says **NONE LEFT** and stays there, unpickable, for the rest of the
match. **Run out of jeeps and you have lost** — only a jeep can carry a flag, so a side without
one has no way left to win. Blowing up your own vehicle rather than driving it home is
therefore a real price now, which is the whole point of it. Details in
[RESERVES_NOTES.md](RESERVES_NOTES.md).

Everything you take out has a tank of fuel and a load of ammunition, both on the strip in the
corner of your half. Fuel is measured in seconds of running, drains faster the harder you work
the engine, and runs out fastest in the helicopter — which is also the one vehicle that cannot
top up at the depots on the map and has to fly home. An empty tank strands you where you
stand, still able to shoot.

Shoot the other side and things happen: vehicles take damage and explode. Each vehicle has its
own weapon and its own armour — the tank outranges everybody, one ASV rocket ends a jeep, and
the jeep has to be almost touching what it wants to hit. Combat is resolved on the map rather
than in the air above it, which is why a tank can hit a jeep it is taller than and a helicopter
can hit anything at all. The numbers are in [M3_NOTES.md](M3_NOTES.md) and
[M4_NOTES.md](M4_NOTES.md).

Almost everything on the map can be shot down. A building takes seven tank shells: the roof
caves in halfway through and the walls come down at the end, and each of those two moments
throws debris. Rubble stops being cover — shells go straight over a building you have
flattened, which is how you open a firing lane — and a flattened depot stops refuelling
anybody, so taking one away is worth doing. The bunker is the only thing on the map that cannot
be destroyed at all - the flag towers can, and have to be. The numbers are in
[M5_NOTES.md](M5_NOTES.md).

A **wall** is the one thing on the map built to be placed in rows: five metres a segment, two
metres tall — over a jeep and under a tank — and neutral, so the wall a side put up is cover
for whoever reaches it first. Three tank shells, four grenades, two rockets or two and a half
seconds of chaingun, per segment — you pay for the hole, not for the wall. Put several in a
line and they read as one: the piers stand at the joins rather than in the middle, so the seam
between two segments is exactly where a pier is.

A **gate** is a wall segment that opens. It is the same five metres on the same grid, with the
same piers at the same joins, and the only thing that tells it apart from a wall is that it
belongs to somebody: drive one of that side's vehicles within sixteen metres and the steel leaf
drops into the ground and stays down until you are through. To the other side nothing happens
at all, and a gate is simply the part of the wall that is a different colour. It is also the
part that is cheapest to break — sixty hit points against the wall's eighty, two tank shells or
three grenades — which is the oldest rule there is about walls: the gate is where one gets
attacked. Nothing in the game opens one with a single round, and a gate that has been broken
open stays open, because there is nothing left to close.

Each bridgehead has a two-segment run across its inboard shoulder, closing the strip of beach a
raider used to slip along to get clear of the emplacement covering that crossing. The seaward
segment is a plain wall; the inland one is a gate belonging to whichever side that bridgehead
is on. So the turn inland is ten metres further into the turret's reach for an attacker, and no
detour at all for the side that built it.

A **gun tower** is four metres of hexagonal shaft with an automated gun on top of it, and it is
one of the two things on the map that belongs to a side. It shoots at anything of the other
side within twenty metres and it is knocked down like everything else. Four metres is exactly
twice a wall and well under the flag tower's six, which is the whole of why it is that number:
the three built things on the map are meant to read as a sequence at a glance. Height is
*only* silhouette here — a round in this game sweeps a column from knee height to well above
the helicopter, so what it hits depends on where things are on the map and never on how tall
they are.

The map is one island cut in two by a channel, with the two bunkers facing each other down its
centre line a hundred and forty metres apart — over water, because the middle of the channel is
open. There are four ways across and not one of them is the short way: a **bridge** on each
side of the middle, spanning thirteen metres of narrows, which anybody can drop; and a
**causeway** further out on each flank, twelve metres wide, crossing the full channel, which
nobody can take away. So getting home is a decision about which flank to commit to, and
dropping both bridges makes the match a great deal longer without ever making it unwinnable.
**Drive off a bank and you drown**, which costs exactly what being shot costs - including off
the pale shelf that rims every coast, which is shallow to look at and no shallower than the
rest. The helicopter is the one thing that can ignore all of it.

**The map is a file**: `unity/Assets/StreamingAssets/Levels/iron-channel.json`. Edit it, press
Play, and you are on the changed map — no menu item and no recompile, because the scene reloads
it every time. **Tools > IronFlag > Render Level Overview** draws whatever is in that file from
straight above, which is the view to judge a map by. The rules a map has to obey — no bunker in
the sea, no pair of towers close enough to scout in one drive, and above all no map whose only
crossings can all be destroyed — are checked when it loads and warned about by name. Details in
[M7_NOTES.md](M7_NOTES.md).

## Making a map

Open `unity/Assets/RF/Scenes/LevelEditor.unity` and press Play. The map is above you, seen
straight down: **right-drag** to pan, **wheel** to zoom, click anything to select it and drag it
to move it. Keys `1`–`5` pick the tool — select, land, prop, tower, bunker — and the panel on
the left is the palette. Hold **shift** to place something on top of what is already there, hold
**alt** to ignore the grid, `Q`/`E` to turn what is selected, `Del` to remove it, `Ctrl+Z` to
undo, `Ctrl+S` to save.

The panel on the right is the exact numbers behind whatever is selected — the mouse is for
roughly where a thing goes and that is for exactly where — and with nothing selected it is the
map itself: its name, what it is trying to be, how big the world is, and how many of each
vehicle it gives a side. Under it is every rule
the map is currently breaking, live, in the same words the game logs. **Mirror to the other
side** copies whatever is selected to the far side of the origin, rotated half a turn and
handed to the other team, which is how every map in this game is laid out.

Press **PLAY THIS MAP** and you are driving on it; press **F1** in the game and you are back in
the editor with it still open. Maps you make are saved next to your saves rather than into the
game, and a map you edit there shadows the shipped one of the same name — so nothing you do can
damage `iron-channel`, and reverting is deleting one file. Details in
[M8_EDITOR_NOTES.md](M8_EDITOR_NOTES.md).

You do not have to start from an empty island either. **GENERATE** draws a whole map out of a
seed — the coast, the beaches, the roads, the bunkers, the towers, the depots, the emplacements
and the trees — on one of three kinds of ground, at one of three sizes, with the two halves
either matching or not. It is a map like any other when it arrives: every tool works on it and
the same rules panel judges it. The seed is shown and can be typed back in, so a map worth
keeping is a number worth writing down. Details in [GENERATOR_NOTES.md](GENERATOR_NOTES.md).

Then there is the reason you are out there. Four identical pyramids stand on the map, two at
the back of each half, and one of each pair is holding that side's flag - but an intact tower
looks exactly the same either way, from any distance, forever. **The only way to find out is to
break one open**, which takes five tank shells, and a broken tower then shows what it was
holding to everybody including its owner. Guess wrong and you have spent the shells to learn
that the other one is real. A tank load pays to open both; a jeep can do it with eight grenades
and eight seconds parked in the enemy's base, which is a gamble rather than a plan.

**Only the jeep can carry a flag, and only out of a tower somebody has already broken open.**
Drive it into the enemy's and it leaves on a mast above the roll cage, where the whole map can see it - the strip at the top of each half tells both
of you where both flags are, whatever else you are doing. Get it back to your own bunker,
which is the same place you refuel, and you have won. Get killed on the way and the flag
stands where you fell for twelve seconds: anybody's jeep can take it on, and if nobody does it
goes back to its tower. The numbers are in [M6_NOTES.md](M6_NOTES.md). That is one of the two
ways a match ends; the other is one side running out of jeeps, which is the same sentence read
backwards.

The scenes and the prefabs are all generated — **Tools > IronFlag > Build Vehicle Sandbox
Scene**, **Build Level Editor Scene**, **Build Vehicle Prefabs**, **Build Combat Prefabs** and
**Build Destructible Prefabs** — so rebuild them rather than editing them by hand. The flag and
its tower come from **Build Objective Prefabs**, and **Build Level Catalog** is what tells the
running game which prefab a level file's `"Bridge"` means. Run that one after changing any
generated material, or the next render will still show the old colour.

What the game looks like is generated the same way. **Build Volume Profile** writes
`Assets/Settings/DefaultVolumeProfile.asset` — the tone curve, bloom, grade and vignette — from
the table in `PostTuning.cs`, so the settings can be argued with in a diff instead of in a
YAML file full of file IDs. The sun, the ambient fill, the haze and the sky come from
`LightingTuning.cs`, one row per lighting condition, and every scene here is lit through the
same call. The HUD and the editor's panels are drawn by a second camera stacked on each view so
the grade never touches them; [LIGHTING_NOTES.md](LIGHTING_NOTES.md) explains why that is a
camera and not a screen-space overlay canvas, and why a still has to composite it by hand.

The map is the exception: it is a file rather than a generator, and the scene only carries a
baked copy of it so that opening the scene shows something. That copy is thrown away and
rebuilt from the file on the first frame of play.

## Where the milestones stand

- **M0 — project setup**: done. See [SCAFFOLDING_NOTES.md](SCAFFOLDING_NOTES.md).
- **M1 — vehicle movement & camera**: done. See [M1_NOTES.md](M1_NOTES.md). All four vehicles
  drive, the helicopter flies, and one top-down camera follows whichever one you are in.
- **M2 — split-screen input**: done. See [M2_NOTES.md](M2_NOTES.md). Two players, two
  viewports, per-player device pairing, and a controls asset that is about this game rather
  than about the Input System template.
- **M3 — combat basics**: done. See [M3_NOTES.md](M3_NOTES.md). Four weapons, projectiles
  that sweep rather than tunnel, a hit-point pool per vehicle, and wrecks that leave the field.
- **M4 — bunker & vehicle selection**: done. See [M4_NOTES.md](M4_NOTES.md). A roster panel
  per split-screen half, one vehicle out at a time, a lift and a helipad to leave from, and
  fuel and ammunition with depots and bunkers to refill them at.
- **M5 — destruction (state-swap)**: done. See [M5_NOTES.md](M5_NOTES.md). Every prop and
  building on the map has a hit point pool and three models, a round hurts a wall the same way
  it hurts a hull, rubble stops being cover, and the depots can be taken away.
- **M6 - flag & win conditions**: done. See [M6_NOTES.md](M6_NOTES.md). Two towers a side and
  only one of them real, a flag that hides until somebody scouts it, a jeep-only pickup rule,
  a dropped flag on a twelve-second clock, and a match that ends when one reaches a bunker.
- **M7 - greybox map**: done. See [M7_NOTES.md](M7_NOTES.md). The v0.1 map exists, and it is an
  **external level file** rather than code: an island split by a channel, crossings both
  destructible and not, mirrored bases, depots and cover, and water that drowns anything that
  drives into it. Nothing in the codebase knows where anything on the map is any more, which is
  the scaffold the in-game level editor will stand on.
- **The level editor**: done. See [M8_EDITOR_NOTES.md](M8_EDITOR_NOTES.md). Its own scene, every
  field of a level file editable with a mouse or typed exactly, live validation, undo, and a
  round trip to the game and back. Not in the design document's milestone plan - it is what M7
  said should come next once levels became files, and nothing in the format had to change to
  allow it.
- **The main menu**: done. See [MAIN_MENU_NOTES.md](MAIN_MENU_NOTES.md). The game starts in a
  menu rather than in a match — a map list read off the level folders, a direct door into the
  editor, three settings that are remembered between sessions, and a real map turning slowly
  behind all of it. `ESC` twice leaves a match and a guarded **MENU** button leaves the editor,
  which is what makes it a screen you use rather than one you pass through at boot.
- **M8 - polish pass**: not started. Per-vehicle audio, a minimap, HUD readability and juice.
  The minimap has a data source: a level is a list of rectangles.
- **Surfaces**: done, all five phases. See [SURFACES_NOTES.md](SURFACES_NOTES.md). The map is
  made of more than one thing: a level file names a surface per piece of land, one table
  says what each surface is, and the crossings are grey roads through green country instead of
  grey ground on grey ground. It is also an island rather than two rectangles in a pond - the
  map is rasterised into a field of surfaces, and every coastline derives four metres of beach
  inside it and five metres of pale shelf outside it, neither of them drawn by hand. The
  coast is no longer a rectangle: the island is one shape cut out of that field, every natural
  coast wanders by up to three metres of seeded noise, every asphalt edge is exactly where the
  file wrote it, and the drop to the water is a bank rather than the side of a box. And the
  ground is no longer only a colour: a vehicle asks the field what it is standing on and gets
  slower, thirstier and less nimble for it, weighted by how much of the ground each vehicle
  feels - four seconds from rest carries a jeep 84 m along a road and 63 m along a beach, and
  a tank barely notices the difference. And the map was repainted to suit all that: two islands
  cut out of ellipses, each drawn as sand with grass laid over the middle so the beach is
  simply where the grass runs out, and a road network per side out to its depots and its
  crossings. A third of the land is sand now rather than a thirteenth, it lies across the line
  a driver cutting the corner at a crossing would take, and the road is the way round it - so
  the surface table finally decides a route instead of only a colour. The plan the whole pass
  was built against is kept in [SURFACES_NOTES.md](SURFACES_NOTES.md#appendix-the-original-plan-as-written).
- **Not scheduled**: a near-term backlog -
  locking the helicopter to a fixed altitude, automated turrets, and finishing off destruction
  (no hitbox once wrecked, everything actually destructible) - kept in
  [return-fire-homage-design-doc.md §10](return-fire-homage-design-doc.md#10-near-term-backlog-not-yet-scheduled-to-a-milestone).
