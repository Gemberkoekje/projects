# Turrets: one heading, an early swing, and a slower gun

**To understand this, start by reading `unity/Assets/RF/Scripts/Destruction/AutoTurret.cs` —
`WatchMargin`, `Watching()` and `Target()` are the whole of the behaviour change — then the
`Emplacement()` row in `unity/Assets/RF/Scripts/Combat/WeaponTuning.cs` for the gun, then
`LevelEdits.FacingTheEnemy` in `unity/Assets/RF/Scripts/Editing/LevelEdits.cs` for which way
one points before anything has driven at it.**

Not a milestone and not in the plan: three tweaks asked for directly, after M9 shipped the
emplacement. It supersedes part of [M9_NOTES.md](M9_NOTES.md) — see *What M9 said that is no
longer true* at the bottom.

---

## How to see it

Open `unity/Assets/RF/Scenes/Sandbox.unity` and press Play. Take the **tank** out of the green
bunker and drive up the map towards a brown bridgehead.

At about **thirty-two metres** the emplacement's barrel starts coming round onto you. Nothing
is fired: it cannot reach you and will not try. By the time you cross twenty metres it is
already pointing at you and the first round arrives immediately — which is the whole point of
the early swing, and the opposite of the old behaviour, where the barrel began its swing at
the moment it could first have fired.

Then stand in front of it. Instead of a stream you get **one round every second and a half**,
each one worth fifteen — a tenth of your hull for every second you stay. Five cannon shells
takes the emplacement down in six seconds; you drive away having spent sixty to seventy-five
of your hundred. Try the same thing in the **jeep** and you die at four rounds, about four and
a half seconds in, with the turret still standing.

And look at a side's emplacements before the shooting starts: every green barrel is parallel
to every other green barrel, and the brown ones face them.

---

## The three asks, and what each one became

### 1. Every turret of a side starts on the same heading

There were two authoring paths and they were wrong in two different ways.

**The generator rolled a heading.** `LevelGenerator.Emplacement` placed each gun at its side's
bearing *plus up to twenty-five degrees either way*, on the theory that a dead-straight row
looks stamped out. What it actually looked like was a map where nobody had aimed anything: at
rest, four guns pointing four slightly different ways read as scattered rather than sited. The
roll is gone; the heading is now `LevelEdits.FacingTheEnemy(side)` and nothing else.

**A hand-placed turret was placed square to the map.** `LevelEdits.NewStructure` gave every
structure `YawDegrees = 0`, which is right for a tree and right for a green emplacement and
exactly backwards for a brown one — a gun facing its own back line, which looks precisely like
a gun that works. Placing one from the editor palette now uses the same function.

That function is the whole rule, and it is deliberately a constant per side rather than
anything derived from where the turret stands:

```csharp
public static float FacingTheEnemy(Team side) => side == Team.Brown ? 180.0f : 0.0f;
```

Pointing each gun at the middle of the map — which is what `FacingTheField` does for a bunker,
and rightly, because a bunker facing the wrong way deploys into its own wall — would have
fanned a row of emplacements out by a few degrees each and produced the same "nobody aimed
these" reading by a different route. Green sits at negative Z and Brown at positive Z on every
map this game can describe: it is the convention `Starter` lays the bunkers out on and the one
`Mirror` flips a placement across, so neither the editor nor the generator has to read the map
to know which way is at the enemy.

The shipped map already said 0 and 180 and is untouched.

### 2. The gun starts turning before it can shoot

An emplacement now has **two circles**: it watches out to `Range + WatchMargin` and it fires
inside `Range`. With the gun's twenty metres of reach that is a thirty-two metre watch and a
twenty metre kill.

Twelve metres is not a taste number. The gun traverses at eighty degrees a second, so the
worst case — a quarter turn — costs about 1.1 seconds, and twelve metres is what a tank covers
in that. An emplacement that picks a tank up at the edge of its watch is therefore aimed at it
by the moment it crosses into range, whichever way the barrel happened to be pointing. Faster
things arrive with the swing half finished, which is the right way round: the jeep is the one
vehicle meant to be able to run past a turret.

It is one roll-call and two questions asked of the answer, not two searches:

```csharp
VehicleController watched = Watching();          // nearest hostile inside the watch
...
if (IsInReach(watched) && aimed) weapon.TryFire();
```

That works because the watch is the *wider* circle, so anything inside reach is inside it too
and the nearest thing being watched is the only candidate for the nearest thing in range. A
second search for "nearest in range" could only ever return this vehicle or nobody — and a
turret that tracked one vehicle while firing at another would be aiming at neither.

**Nothing about reach changed.** The tank's sixteen metres of standoff is exactly what it was;
it is simply watched while it uses it. What the watch removes is the free second a vehicle
used to get at the moment of entry, and what it adds is a warning given at a distance where
turning around is still free.

### 3. Fewer rounds, heavier ones

|  | Was | Now |
|---|---|---|
| Damage | 12 | **15** |
| Shot interval | 0.33 s | **1.5 s** |
| Damage per second | 36 | **10** |
| Reach | 20 m | 20 m (unchanged) |
| Splash | none | none (unchanged) |
| Traverse | 80 °/s | 80 °/s (unchanged) |

One round every second and a half is the tank's own cadence for under half the tank's damage.
The point of the heavier round is that a hit is an **event** rather than a tick — three end a
helicopter, four end a jeep — and the second and a half between them is the room a driver has
to do something about it. A stream at thirty-six damage a second gave neither: it was a line on
the map, and the only decision it left was which vehicle can shoot from outside it.

What a stay inside twenty metres costs now, and what a straight trade costs — standing still
and firing back, which is the worst way to do it and the only one that can be read off the
table:

| Vehicle | Hull | Rounds to kill it | Seconds it survives | Straight trade with the turret |
|---|---|---|---|---|
| Helicopter | 40 | 3 | 3.0 s | **Loses** — dead three rounds in, at 3 s — but outranges it at 26 m |
| Jeep | 50 | 4 | 4.5 s | **Loses** at about 4.5 s, turret still standing |
| Tank | 100 | 7 | 9.0 s | **Wins** in 6 s for 60–75 of its 100 |
| ASV | 140 | 10 | 13.5 s | **Wins** in 7.5 s for 90 of its 140 — and outranges it at 30 m |

Those two rows in bold are the design, and they are pinned by a test rather than left as
prose: `StructureRosterTests.TheEmplacementLosesToTheTankAndBeatsTheJeep`. A turret that beat
the tank sent to remove it leaves exactly one decision on the map — bring the gun that
outranges it — and a turret the jeep could shoot its way past would break the design
document's first pillar, that everything else exists to clear the jeep's path. The gun is
tuned to sit between those, and that is a relationship worth asserting; 15 and 1.5 are taste.

### The fourth thing, which was a question rather than an ask

*"I'm unsure if turrets should get back to their default position."* They still do, and it was
left alone on purpose. It is the same rule that makes ask 1 work: a side's guns are placed on
one heading, and stowing is what puts them back on it once the raid is over, so the tidy row
survives the first vehicle that drives past. A barrel left where the last raider died also
reads as a turret still tracking somebody who is not there.

It is one line if that turns out to be the wrong call — in `AutoTurret.Update`, drop the
`RestYawDegrees` branch so `wanted` keeps its previous value when nothing is watched — plus
`TurretTests.AnIdleTurretStowsItsGun`, which asserts the current behaviour.

---

## Decisions worth knowing

**The heading is authoring, not runtime.** `AutoTurret` still rests at whatever yaw its
placement gives it, rather than forcing its side's bearing at load. Forcing it would have made
the rule hold everywhere including on old files, and would also have made the editor's rotate
handle a lie on the one kind of structure where somebody might genuinely want a gun covering a
flank. A level places props; this decides what a *newly placed* prop looks like.

The cost is real and worth knowing: **a map saved before this change keeps its rolled
headings.** Level files under `%USERPROFILE%\AppData\LocalLow\Gemberkoekje\IronFlag\Levels\`
are data, not code, and nothing rewrites them. Regenerate the map, or turn the guns in the
editor.

**Circling still beats the barrel.** The traverse rate was not touched, so M9's other answer to
an emplacement — get inside sixteen metres in something fast and out-turn it — works exactly
as it did. The watch does not make the turret quicker, only earlier.

**The slower gun makes the turret more answerable, not weaker.** Thirty-six damage a second
was unanswerable at range zero and irrelevant at range twenty-one; ten damage a second is a
fight you can choose to have. The hit points are unchanged at 170, so what it costs to remove
one is exactly what M9 priced.

---

## Gotchas

**The gun's numbers are stamped onto the prefab, so the table alone changes nothing.**
`DestructiblePrefabBuilder.AddGun` copies `WeaponTuning.Emplacement()` into
`RF_Structure_Turret.prefab` at build time. Editing `WeaponTuning.cs` and running the tests
will pass — the tests read the table — while every turret in every scene still fires the old
numbers. Re-run **Build Destructible Prefabs** and then grep the prefab for `Damage:` and
`ShotInterval:` to confirm; it is nine lines of YAML and it is the only proof.

**`Target()` quietly changed meaning.** It used to be "the nearest enemy in range", which
doubled as "is this turret tracking anything". It now answers only the first, and the second is
`Watching()`. Two assertion messages in `TurretTests` said "it is not even tracking anything"
about a call that no longer means that; anything else reaching for `Target()` to ask about
tracking will be wrong in the same quiet way.

**Dropping the yaw roll shifts every generated map.** `dice.Spread(25.0f)` was a draw from the
side's stream, so removing it moves every subsequent placement on that map. Nothing is broken —
the sweep across seeds still validates — but **a seed no longer reproduces the map it produced
before this change**, so a saved seed from an older session is a different island now.

**This repo's `.cs` files are a mix of LF and CRLF in the working tree.** `AutoTurret.cs` and
`WeaponTuning.cs` are LF; `LevelEdits.cs`, `LevelGenerator.cs` and most of the tests are CRLF.
Any scripted patching has to detect and preserve per file, or a one-line change lands as a
whole-file diff. `.gitattributes` normalises on the way in, so this is invisible until you
write to a file with the wrong newline.

---

## Verified

Run from `C:\git\projects\IronFlag` on Unity 6000.5.9f1, after
`DestructiblePrefabBuilder.BuildAll` rebuilt all **9** destructible prefabs and re-stamped the
turret at `Damage: 15`, `ShotInterval: 1.5`: the project compiles with no errors, **468
edit-mode tests pass** and **166 play-mode tests pass**.

New coverage:

- **`LevelGeneratorTests.EveryEmplacementOfASideFacesTheSameWay`** — twelve seeds across both
  symmetries at Hard, every turret on its side's exact heading, with a guard that both sides
  actually got one so the sweep cannot pass vacuously.
- **`LevelEditingTests.AnEmplacementIsPlacedFacingTheOtherSide`** — green at 0, brown at 180,
  two of a side on the same heading, and a tree still square to the map so the rule is about
  turrets rather than about placement.
- **`TurretTests.TheGunComesRoundBeforeTheVehicleIsInRangeToBeShot`** — the whole of the watch
  as one sequence: tracked and not fired at from inside the watch and outside the reach, aimed
  within tolerance while still out of reach, and firing at once when it closes. Written as one
  test on purpose — the value of the early swing is not that the barrel moves sooner, it is
  that there is no swing left to pay for when the shooting starts.
- **`TurretTests.NothingOutsideItsWatchIsEvenLookedAt`** — the watch is a limit too, so a
  turret is something you drive around rather than a barrel that follows you across the map.
- **`TurretTests.NothingOutsideItsReachIsATarget`** — extended: the vehicle at reach + 4 m is
  now *watched* and still never fired at.
- **`StructureRosterTests.TheEmplacementLosesToTheTankAndBeatsTheJeep`** — the trade, read off
  the live weapon and vehicle tables rather than restated.

**Not verified: whether 15 at 1.5 s is the right feel.** It is a first guess at "much less
often, a little more per hit", and the two ways it can be wrong pull in opposite directions —
too slow and an emplacement is scenery you drive past, too heavy and the second and a half of
room stops mattering because two rounds end you anyway. The numbers to move are in
`WeaponTuning.Emplacement()`; the trade test will say when either has gone too far.

**Not looked at in a still.** The facing is exact and asserted across twenty-four generated
maps, and the other two changes are only visible in motion — a photograph of a turret firing
slowly looks like a photograph of a turret firing. This one wants playing rather than
rendering.

**One thing noticed and left alone:** `StructureTuning.For(StructureKind.Turret)`'s comment
says a turret is "about eleven seconds of chaingun". Against the live table it is 5.3 seconds
(170 hit points at 32 damage a second). That predates this pass and is about the turret's
armour rather than its gun, so it is flagged rather than fixed.

---

## File map

| File | What changed |
|---|---|
| `unity/Assets/RF/Scripts/Destruction/AutoTurret.cs` | `WatchMargin`, `WatchRange`, `Watching()`, `IsInReach()`; `Target()` narrowed to "what it would shoot"; `Update` aims on the watch and fires on the reach |
| `unity/Assets/RF/Scripts/Combat/WeaponTuning.cs` | `Emplacement()` — 15 damage at 1.5 s, and the paragraph that justifies it |
| `unity/Assets/RF/Scripts/Editing/LevelEdits.cs` | **New** `FacingTheEnemy(Team)`; `NewStructure` places a turret on it |
| `unity/Assets/RF/Scripts/Editing/LevelGenerator.cs` | `Emplacement` takes no dice and rolls no heading |
| `unity/Assets/RF/Prefabs/Structures/RF_Structure_Turret.prefab` | Re-stamped by the prefab builder |
| `unity/Assets/RF/Tests/PlayMode/TurretTests.cs` | Two new tests, one extended, one remark |
| `unity/Assets/RF/Tests/EditMode/LevelGeneratorTests.cs` | **New** heading sweep |
| `unity/Assets/RF/Tests/EditMode/LevelEditingTests.cs` | **New** placement test |
| `unity/Assets/RF/Tests/EditMode/StructureRosterTests.cs` | **New** trade test |

Commands used:

```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -quit -projectPath C:\git\projects\IronFlag\unity -executeMethod IronFlag.Editor.Gameplay.DestructiblePrefabBuilder.BuildAll -logFile C:\git\projects\IronFlag\turret-build.log
```

---

## What M9 said that is no longer true

[M9_NOTES.md](M9_NOTES.md) is otherwise still the right description of the emplacement. These
parts are superseded:

| M9 said | Now |
|---|---|
| `Damage / interval \| 12 @ 0.33 s \| 36 dps` | 15 @ 1.5 s — 10 dps |
| "a full tank in under 3 s, a jeep in under 1.5 s" | A tank in 9 s, a jeep in 4.5 s |
| "The damage row is deliberately brutal" | Deliberately answerable: the tank can close with an emplacement and win, and is meant to |
| "Nothing in the roster is meant to sit inside twenty metres of one and trade shots" | The tank and the ASV both win that trade; the jeep and the helicopter still lose it |
| The gun points where it rests until something is in range | It comes round twelve metres before that, and only fires inside range |
| A generated emplacement faces its side's bearing give or take 25° | Exactly its side's bearing, and so does a hand-placed one |
