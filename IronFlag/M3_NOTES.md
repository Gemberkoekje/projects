# M3 — Combat basics: what exists and why

**To understand this, start by reading `unity/Assets/RF/Scripts/Combat/CombatPlane.cs`,
then `WeaponTuning.cs`, then `Projectile.cs`, then `Core/VehicleBay.cs`.** The first is the
one idea the rest of it hangs off - this is a game played on a plane, and combat is resolved
there. The second is the whole arsenal in one table, the third is what a round actually does
between the barrel and the target, and the fourth is the death → bunker → repaired-and-waiting
loop (`VehicleBay` is what this doc originally called `VehicleRespawn`; M4's bunker/roster work
folded the repair timer into it and took the "drives back out on its own" part out — see
[M4_NOTES.md](M4_NOTES.md)). `ProjectileMotion.cs` is the ballistics and is sixty lines of
arithmetic with no state in it.

This covers milestone **M3** from the design doc: weapons, projectiles, vehicle HP and the
death → return-to-bunker flow. M2's split screen is in [M2_NOTES.md](M2_NOTES.md), M1's
vehicles in [M1_NOTES.md](M1_NOTES.md), M0's scaffolding in
[SCAFFOLDING_NOTES.md](SCAFFOLDING_NOTES.md).

---

## How to see it

Open `unity/Assets/RF/Scenes/Sandbox.unity` and press Play. Left mouse or the right trigger
fires. Shoot the other side's vehicles: they take damage, explode, disappear, and are repaired
after four seconds — M4 later made that wait in the bunker for the player to pick and deploy it,
rather than driving back out on its own.

You will have to close in. Nothing reaches further than the camera can see, and the jeep has
to be almost touching what it wants to hit. The two start lines are 68 m apart and the
longest gun in the game reaches 36 m, so a match starts with both sides driving at each
other.

| Action | Keyboard and mouse | Gamepad |
|---|---|---|
| Fire | left mouse (hold) | right trigger (hold) |

Everything else is as M2 left it. Holding the trigger fires at each weapon's own rate; there
is no separate "tap to fire". At the time this was M3's only limiter — M4 later added ammunition
(`WeaponTuning.Rounds`, spent by `VehicleWeapon.TryFire` and refilled by `VehicleSupply`), so a
gun now stops firing however fast you tap once its magazine runs dry, not just at its rate of
fire.

The generators still run headless:

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -quit -projectPath unity -executeMethod IronFlag.Editor.Gameplay.VehicleSandboxScene.RenderToFile -sandboxOutput ../m3-sandbox.png -logFile -
```

Note the `../`. Unity changes its working directory to `-projectPath`, so a bare filename
lands in `unity/`, not next to the repo's other renders. Run `-batchmode` but **not**
`-nographics`, and do not drop the `-quit` — without it the editor sits in batch mode
afterwards burning a core.

`m3-sandbox.png` has a round out of every barrel and a blast on both start lines. **None of
that is in the scene.** Nothing moves in a saved scene, so the still is staged by
`VehicleSandboxScene.StageCombat` and exists only inside the render; `BuildAndSave` stages
nothing.

---

## What was built

**One table for the arsenal.** `WeaponTuning.For(kind)` is the whole thing: damage, blast
radius, muzzle speed, rate of fire, reach, drop, launch elevation and calibre, one row per
vehicle. It is the companion to `VehicleTuning`, which gained the armour column the design
doc's roster table always had.

| Vehicle | Weapon | Damage | Blast | Speed | Interval | Arms at | Reach | Armour |
|---|---|---|---|---|---|---|---|---|
| Jeep | Grenade | 22 | 4.0 m | 28 m/s | 1.0 s | — | **14 m, lobbed** | 50 |
| Tank | Cannon | 34 | 1.5 m | 70 m/s | 1.5 s | — | 36 m | 100 |
| ASV | Rocket | 55 | 4.5 m | 30 m/s | 2.5 s | 6 m | 30 m | 140 |
| Helicopter | Chaingun | 4 | — | 110 m/s | 0.125 s | 8 m | 26 m | 40 |

Every reach is inside what the camera shows, which is about thirty metres ahead of the
vehicle it is following. That is the constraint the whole column is set by: a weapon that
outranges the screen is one whose hits the player never sees.

**Combat happens on the map, not in the air above it.** `CombatPlane` is the rule and it is
the most important file in the milestone. A round is not a point: it is a *column* standing
from just above the ground to over the helicopter's ceiling, swept horizontally. What it hits
is decided by where things are and never by how tall they are. Read that file before touching
any of this.

**Ballistics as a pure function.** `ProjectileMotion.Step` integrates constant acceleration in
closed form, so a hundred small steps land exactly where one big one does and the jeep's arc
is not a function of the frame rate. `LaunchElevation` solves the angle that carries a lobbed
round exactly as far as its reach claims, and `BallisticRange` is its inverse - the two are
tested against each other, so the arc a player watches and the distance the table promises
cannot drift apart.

**Rounds that are not rigidbodies.** A `Projectile` has no collider and takes part in no
collisions. It moves itself each fixed step and sweeps its column across the ground it
crossed. It also decides who it may hurt, which is one question asked once
(`Teams.IsHostile`): fire that cannot hurt a vehicle passes *through* it rather than stopping
on it.

**A health pool and an event.** `VehicleHealth` is a number, the no-friendly-fire rule, and
`Destroyed`. Everything that reacts to a vehicle dying hangs off that event rather than being
wired into it.

**The bunker loop, as M3 first built it.** `VehicleRespawn` lived on the vehicle: it hid the
wreck, counted four seconds, repaired it and put it back on its side's `TeamBunker` pad — one
pad per vehicle, so a side that lost all four got them back in a row. `PlayerVehicleDriver`
declined to drive a wreck, stepped over wrecks when the player changed vehicle, and cut the
camera to whatever drove back out. M4 later renamed this to `VehicleBay` and took the "drives
back out on its own" part out: a repaired vehicle now waits in the bunker until the player picks
it from the roster and deploys it. See [M4_NOTES.md](M4_NOTES.md).

**Two things that were never modelled.** `CombatPrefabBuilder` generates the round and the
explosion from primitives and the generated materials, because a tracer is a lit sphere and an
explosion is a bigger one, and putting either through Blender would add a `.glb` carrying no
information that is not in four numbers.

**Muzzles on all four vehicles.** The jeep gained a cowl-mounted grenade launcher and the
helicopter a chin gun, so every model now exports the `Muzzle` object the pipeline already
had a material group for. Both fit inside the dimensions the asset spec already fixed.

---

## Decisions worth knowing

**Combat is two-dimensional, and everything else follows from that.** This game is played
through a fixed camera looking down at a plane. Height exists — the helicopter flies, the
tank's barrel is higher than a jeep's roof — but none of it is something a player can see,
aim at, or reason about, so none of it is allowed to decide whether a shot connects. The
original this is a homage to looked three-dimensional and was not, and that is the right
answer here too. Rounds are still *drawn* in three dimensions, because a tracer that does not
leave the barrel it came out of looks broken; `CombatPlane.DepressionFrom` is what keeps the
two stories close, aiming each shot down so it arrives at hull height at the far end of its
reach.

**A ground vehicle can shoot down a helicopter overhead**, deliberately, which is what
stops altitude being a hiding place.

**Two weapons carry a fuze delay, and it is the price of the column.** A column has no idea
that the thing at its near end is ten metres *below* the barrel rather than in front of it,
so the helicopter was detonating its own burst on whatever roof it happened to be flying
over — a rule that reads as the gun being broken. `WeaponTuning.ArmingDistance` is how far a
round travels before it can hit anything: eight metres for the chaingun, which clears both
the airframe and a ten-metre building, and six for the rocket, whose four-and-a-half-metre
blast has no business going off at arm's length. Both are a real cost — neither weapon can
fight at contact range now, which is the price of being the most mobile vehicle on the field
and of carrying the heaviest warhead, and hovering on top of somebody is no longer a firing
position.

**The other two guns have no fuze delay, and that is a decision rather than an omission.**
The tank and the jeep fire from close to hull height, so their columns barely reach anything
their barrels are not already pointing at. A heavy that cannot defend itself when something
drives into it would be a worse problem than the one arming it would solve, and the jeep only
has fourteen metres to begin with. The one case this leaves is a tank shooting a helicopter
hovering exactly on top of it, which is rare enough, and pointed enough at the roster's
anti-air answer, to be worth leaving alone.

**The gun fires where the muzzle points, and that is the only rule.** Nothing in
`VehicleWeapon` branches on which vehicle it is bolted to. The tank and the ASV mount their
muzzle on the part that traverses, so their aim is the turret's and it takes time to get
there; the jeep and the helicopter mount theirs on the hull, so they have to drive at what
they want to hit. Two very different weapons, one line of code, and no way for a round to
leave in a direction the barrel is not pointing. It also means aiming — the thing M2 built
the whole pointer-and-stick path for — steers the turreted pair and nothing else, which is a
real trade: the two fastest vehicles have fixed guns.

**Only the horizontal part of that direction is used.** A banking helicopter is tilting a
cosmetic node, and a gun that followed it would put every shot fired in a turn into the
ground.

**No weapon outranges the camera.** The view shows about thirty metres ahead of the vehicle
it is following, so that is the ceiling on reach: the tank sits right at it and everything
else is inside it. A weapon that shoots further than the player can see produces hits nobody
witnesses, which reads as the gun not working.

**Reach is authored and the launch angle is derived.** Reach is what the roster is balanced
on; an angle is only ever a way of getting one. The jeep's grenade is angled by
`ProjectileMotion.LaunchElevation` to come down exactly at its fourteen metres, and the
solver deliberately returns the flatter of the two angles that reach a given distance — the
steeper one is a mortar shot, and a round that climbs out of the frame on its way to
something fourteen metres away is not a thing anyone can aim. It also matters because the
higher the drawn arc, the further the picture strays from the column that decides the hit.

**There is no friendly fire.** Four vehicles a side share one start line and one bunker, and a
match where a player can wreck their own roster by firing down it is annoying rather than
tactical. Anything on *no* side is fair game for everybody, deliberately: a vehicle that
reaches the field without a team is an authoring mistake, and damage that silently vanishes is
much harder to notice than a vehicle anyone can shoot.

**Ammunition was not here yet.** The design doc gave fuel and ammo pools to M4 along with the
depots that refill them, so what limited a gun in M3 was its rate of fire and nothing else. M4
has since added `WeaponTuning.Rounds` and `VehicleSupply`/`SupplyPoint` — see
[M4_NOTES.md](M4_NOTES.md) — so a gun's rate of fire is no longer the only thing limiting it.

**Rounds are swept, not stepped.** A chaingun round crosses over two metres per fixed step,
which is most of the length of a jeep. A collider moving that far per step passes clean
through one, and the bug reads as "the helicopter's gun does nothing at close range" rather
than as tunnelling.

**Blast damage is measured from the nearest point of a hull, not its middle.** A tank is five
and a half metres long, and measuring to its origin makes the same rocket lethal or harmless
depending on which end it lands at. The blast is a column too, and its falloff is measured
across the map, so a rocket that goes off under a helicopter is as dangerous to it as one
that goes off beside a tank.

**A wreck is hidden, not destroyed.** Every vehicle is a fixed roster entry its owner will
drive again, so deleting one would mean rebuilding the player's roster, its camera target and
its team paint around the replacement.

**The pilot keeps their seat in the wreck.** The camera holds on the explosion and then cuts
to whatever drives back out; pressing the switch button moves them into any vehicle of theirs
still standing. That is as close as M3 gets to "return to the bunker and select again" — M4
builds the screen where the choosing actually happens.

**The explosion is one expanding sphere, not particles.** Debris belongs with M5's destruction
states, and screen shake and the rest of the juice with M8. What M3 needs is for a hit to be
unmistakable at gameplay camera distance.

---

## Gotchas

**Height was resolved in three dimensions first, and every symptom of it looked like a
different bug.** The tank "shot over the jeep" (barrel at 1.9 m, jeep 1.6 m tall). The
helicopter "could not hit anything" (deploys at 10 m, so its rounds flew level at 10.6 m for
ever). The jeep's grenade "arced over things it was next to". Three reports, one cause, and
none of them pointed at it. Worse, the play-mode rig gave every test vehicle the same
2 m box and the same muzzle height, so not one of the twelve combat tests could see any of
it. `CombatTests.HullOf` and `MuzzleHeightOf` now build vehicles at the real sizes, and
`ATankShellHitsAJeepItIsTallerThan` asserts the muzzle really is above the target's roof
*before* it fires — a regression test that cannot quietly stop reproducing the bug it exists
for.

**A stamped copy of a table goes stale silently, and a test that checks four of its eight
fields will not notice.** The blast radii were retuned after the prefabs were built, and
`EveryPrefabCarriesItsOwnWeapon` sailed straight past it because it was checking damage,
range, speed and kind. It now checks every number. Same failure mode as M2's stale jeep
tuning; if a combat number looks wrong in Play, run **Build Vehicle Prefabs** before
debugging the code.

**Switch the movement model off *before* making the body kinematic.** `VehicleRespawn.Hide`
does them in that order deliberately. A `GroundVehicle` still running writes a velocity onto
its rigidbody every fixed step, and writing one onto a kinematic body is a warning per vehicle
per step for as long as the wreck is waiting.

**A round spawns inside the hull that fired it.** Every muzzle is within its own vehicle's box
collider — the ASV's is 0.36 m *behind* the vehicle origin, because the rocket rack is
recessed into the deck. `Projectile.Sweep` skips anything under the firing vehicle's root, and
without that every shot would go off in the barrel.

**A sweep that starts already overlapping reports `distance: 0` and `point: (0,0,0)`.** Not a
position on the map. `Projectile` uses the muzzle as the impact point in that case; taking the
reported point would detonate every point-blank shot at the world origin.

**`Instantiate` of an inactive template gives an inactive clone.** `Projectile.Fire` calls
`SetActive(true)`, which is a no-op for a real prefab asset and is what lets a play-mode test
hand it a round built in the scene. Without it the test's rounds sit at the muzzle forever,
and the failure reads as "nothing does any damage".

**Emission above about four clips to a flat white disc.** The first blast material ran at 6.0
and rendered as a white circle with no colour in it at all. It is at 3.4 now, which keeps the
warm edge. The tracer is at 3.0 for the same reason and needs to stay bright: it has to read
against a sunlit ground from 34 m up, in half a screen, in a moment.

**`VehicleRespawn` re-enables every renderer it disabled.** Anything switched off for its own
reasons — a headlight that should be dark, a damaged-state mesh — comes back on when the
vehicle redeploys. Nothing does that yet; M5's destruction states will have to.

**Transforms are not auto-synced in this project.** A collider created this frame is not
somewhere a sweep can find it until a fixed step has run, which is why every play-mode combat
test waits before firing.

**`FindObjectsByType<T>(FindObjectsSortMode)` is deprecated on this editor.** The overload
without the sort mode is the one to call. The project builds with no warnings and it is worth
keeping that way; a warning nobody fixes is a warning nobody reads.

---

## Verified

Run from `C:\git\projects\IronFlag`, all on Unity 6000.5.9f1:

- The project compiles headless with no errors and **no warnings**.
- **142 edit-mode tests pass** (110 from M1 and M2, 32 new). The weapon table read against
  the armour table — one rocket ends a jeep, nothing one-shots the ASV, the jeep's reach is
  under half the tank's, nothing outranges the camera, only the two weapons that fire from
  above the fight carry a fuze delay and none of them spends more than half its reach on
  one — the ballistics including that step
  size does not move where a grenade lands and that the angle solver and the range formula
  are exact inverses, the combat column covering everything from under a jeep to over the
  helicopter's ceiling, the health pool and the no-friendly-fire rule, the bunker pad layout,
  the shape of an explosion, every number stamped onto every prefab, and both bunkers in the
  generated sandbox deploying onto their own half of the map.
- **41 play-mode tests pass** (20 from M2, 21 new). Real rounds at real colliders, at the
  real vehicle sizes. Every reported failure has a test: a tank shell hits a jeep it is
  taller than, a helicopter ten metres up hits what is on the ground, a tank shoots a
  helicopter out of the air, a helicopter *cannot* hit what it is hovering over but still
  hits what is twenty metres ahead in the same burst, a rocket does not go off in its own
  launcher, and a tank can still shoot something touching it. Alongside them: a shell takes hit points off an enemy, two
  wreck a jeep, nothing is hit beyond the weapon's reach, a shot at a friend passes through
  without hurting it *and* without being eaten by it, the chaingun does not tunnel through a
  jeep, a gun will not fire faster than its interval, the jeep's grenade hits what is nine
  metres away and nothing thirty metres away, a wreck leaves the field and comes back on its
  own side's pad, two wrecks come back on different pads, a pilot cannot climb into a wreck,
  and the camera cuts to what drives back out.
- The **whole firing path** is walked once, in the real `Sandbox.unity` with two virtual
  gamepads: a trigger on a pad that does not exist, through one player's own copy of the
  controls, into a vehicle's intent, out of the barrel the prefab builder bolted on - and
  only that player's vehicle fires. Every other combat test pulls the trigger by calling the
  gun directly, so this is the one that would catch the path being unwired.
- `Build Combat Prefabs` and `Build Vehicle Prefabs` both build with no warnings, which means
  every muzzle was found in every model.
- `VehicleSandboxScene.RenderToFile` renders both viewports with a round out of every barrel
  and a blast on each start line — see `m3-sandbox.png`.

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -runTests -projectPath unity -testPlatform EditMode -testResults editmode.xml -logFile -
```

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -runTests -projectPath unity -testPlatform PlayMode -testResults playmode.xml -logFile -
```

**Verified by playing it once**, which is where the two-dimensional combat plane and the
shorter ranges came from — the first version had all three of the failures listed above and
every one of them shipped green.

**Still not verified:** whether the numbers are any good. The damage figures are a first
guess sized against the design doc's rock-paper-scissors, the reaches have been cut to fit
the camera but not yet fought over, and the four-second respawn was picked because it felt
like a beat rather than because anyone waited it out. The helicopter has the highest
sustained damage in the game and, until M4 gives it an ammo pool to run dry, nothing paying
for it but its armour. The jeep's fourteen metres and the helicopter's eight-metre fuze are
the two numbers most likely to still be wrong — the second one is the difference between a
helicopter that has to keep its distance and one that cannot hit anything it can catch.

---

## File map

| Path | What it is |
|---|---|
| `unity/Assets/RF/Scripts/Combat/CombatPlane.cs` | **Combat happens on the map.** Read this first |
| `unity/Assets/RF/Scripts/Combat/WeaponKind.cs` | The four weapons, in roster order |
| `unity/Assets/RF/Scripts/Combat/WeaponTuning.cs` | **The arsenal table.** Start here to retune |
| `unity/Assets/RF/Scripts/Combat/ProjectileState.cs` | Where a round is and how fast it is going |
| `unity/Assets/RF/Scripts/Combat/ProjectileMotion.cs` | **The ballistics**, pure |
| `unity/Assets/RF/Scripts/Combat/Projectile.cs` | One round: flies, sweeps, detonates |
| `unity/Assets/RF/Scripts/Combat/VehicleWeapon.cs` | The gun. Fires where the muzzle points |
| `unity/Assets/RF/Scripts/Combat/VehicleHealth.cs` | Hit points, and the moment they run out |
| `unity/Assets/RF/Scripts/Core/VehicleBay.cs` | **Death → bunker → repaired and waiting to deploy** (M3 called this `VehicleRespawn`) |
| `unity/Assets/RF/Scripts/Combat/Explosion.cs` | The flash, and the curve that shapes it |
| `unity/Assets/RF/Scripts/Core/Teams.cs` | Who counts as an enemy, spelled once |
| `unity/Assets/RF/Scripts/Core/TeamBunker.cs` | Where a side's vehicles come back |
| `unity/Assets/RF/Scripts/Vehicles/VehicleTuning.cs` | Gained the armour column |
| `unity/Assets/RF/Scripts/Vehicles/VehicleInput.cs` | Gained `Fire` |
| `unity/Assets/RF/Scripts/Players/PlayerControls.cs` | `Firing` is read now; `FiredThisFrame` is gone |
| `unity/Assets/RF/Scripts/Players/PlayerVehicleDriver.cs` | Passes the trigger; steps over wrecks |
| `unity/Assets/RF/Editor/Gameplay/CombatPrefabBuilder.cs` | Generates the rounds and the explosion |
| `unity/Assets/RF/Editor/Gameplay/VehiclePrefabBuilder.cs` | Now adds health, weapon, muzzle point, respawn |
| `unity/Assets/RF/Editor/Gameplay/VehicleSandboxScene.cs` | Places the bunkers; stages the render |
| `unity/Assets/RF/Editor/ArtPipeline/GeneratedMaterials.cs` | Gained `RF_Tracer` and `RF_Blast` |
| `unity/Assets/RF/Tests/EditMode/WeaponRosterTests.cs` | The arsenal against the armour |
| `unity/Assets/RF/Tests/EditMode/ProjectileMotionTests.cs` | The ballistics, without firing one |
| `unity/Assets/RF/Tests/EditMode/VehicleHealthTests.cs` | Damage, friendly fire, dying once |
| `unity/Assets/RF/Tests/EditMode/CombatRulesTests.cs` | Teams, bunker pads, the flash curve |
| `unity/Assets/RF/Tests/PlayMode/CombatTests.cs` | **Real rounds at real colliders** |
| `unity/Assets/RF/Tests/PlayMode/SplitScreenTests.cs` | Gained the trigger-to-barrel test |
| `blender/assets/vehicle_jeep.py` | Gained a cowl grenade launcher and its muzzle |
| `blender/assets/vehicle_helicopter.py` | Gained a chin gun and its muzzle |
| `unity/Assets/RF/Prefabs/Combat/*.prefab` | Generated. Rebuild rather than edit |
| `unity/Assets/RF/Prefabs/Vehicles/*.prefab` | Rebuilt with the combat half |
| `unity/Assets/RF/Scenes/Sandbox.unity` | Regenerated. Two bunkers |

---

## What M4 inherits

- `TeamBunker` is a position, a facing and a lookup by side. The lift, the helipad, the
  vehicle-select panel and the fuel and ammo attach to it rather than replace it.
- `VehicleRespawn.DeployNow` is the whole "put a vehicle on the field" move, and it already
  knows the helicopter deploys into the air. A bunker that lets the player choose calls it
  instead of the countdown doing so.
- `VehicleHealth.Fraction` is the damage bar; `VehicleWeapon.Cooldown` is the reload bar.
  Neither has a HUD yet.
- Ammunition wants a pool per weapon that `VehicleWeapon.TryFire` decrements and a depot that
  refills it. `WeaponTuning` is where the magazine size belongs.
- `PlayerVehicleDriver.AtTheBunker` is the state a bunker screen would show, and
  `PlayerControls` needs nothing new for it — the `UI` map is still Unity's and untouched.
