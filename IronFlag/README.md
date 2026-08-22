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
[M6_NOTES.md](M6_NOTES.md) for the flag and the match you win with it, and
[M7_NOTES.md](M7_NOTES.md) for the map itself — which is a file you can edit — and
[TOWER_RULES_NOTES.md](TOWER_RULES_NOTES.md) for why you have to shell a pyramid before you
can rob it.

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
        ├── Prefabs/ · Scenes/ · Scripts/ · Tests/
```

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

Open `unity/Assets/RF/Scenes/Sandbox.unity` and press Play. The screen splits in two: green on
top, brown below. Both of you start **inside your bunker**, looking at a panel listing your
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
of the map. A wreck spends four seconds being repaired before it can be picked again.

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

The map is one island cut in two by a channel, with the two bunkers facing each other down its
centre line a hundred and forty metres apart. There are three ways across: a **causeway** in
the middle that nobody can destroy, and a **bridge** on each flank that anybody can — so
dropping both funnels the whole match through one sixteen-metre chokepoint without ever making
it unwinnable. **Drive off a bank and you drown**, which costs exactly what being shot costs.
The helicopter is the one thing that can ignore all of it.

**The map is a file**: `unity/Assets/StreamingAssets/Levels/iron-channel.json`. Edit it, press
Play, and you are on the changed map — no menu item and no recompile, because the scene reloads
it every time. **Tools > IronFlag > Render Level Overview** draws whatever is in that file from
straight above, which is the view to judge a map by. The rules a map has to obey — no bunker in
the sea, no pair of towers close enough to scout in one drive, and above all no map whose only
crossings can all be destroyed — are checked when it loads and warned about by name. Details in
[M7_NOTES.md](M7_NOTES.md).

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
goes back to its tower. The numbers are in [M6_NOTES.md](M6_NOTES.md).

The scene and the prefabs are all generated — **Tools > IronFlag > Build Vehicle Sandbox
Scene**, **Build Vehicle Prefabs**, **Build Combat Prefabs** and **Build Destructible
Prefabs** — so rebuild them rather than editing them by hand. The flag and its tower come from
**Build Objective Prefabs**, and **Build Level Catalog** is what tells the running game which
prefab a level file's `"Bridge"` means. Run that one after changing any generated material, or
the next render will still show the old colour.

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
  **external level file** rather than code: an island split by a channel, one indestructible
  causeway and two destructible bridges, mirrored bases, depots and cover, and water that drowns
  anything that drives into it. Nothing in the codebase knows where anything on the map is any
  more, which is the scaffold the in-game level editor will stand on.
- **M8 onward**: not started. M8 is the polish pass - per-vehicle audio, a minimap, HUD
  readability and juice. The minimap now has a data source: a level is a list of rectangles.
  Ahead of M8 there is also a near-term backlog - locking the helicopter to a fixed altitude,
  automated turrets, and finishing off destruction (no hitbox once wrecked, everything actually
  destructible) - kept in
  [return-fire-homage-design-doc.md §10](return-fire-homage-design-doc.md#10-near-term-backlog-not-yet-scheduled-to-a-milestone).
